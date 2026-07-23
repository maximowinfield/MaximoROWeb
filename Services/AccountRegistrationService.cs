using MySqlConnector;

namespace MaximoROWeb.Services;

public sealed class AccountRegistrationService
{
    private readonly string _connectionString;
    private readonly EmailVerificationService _verificationService;
    private readonly ILogger<AccountRegistrationService> _logger;

    public AccountRegistrationService(
        IConfiguration configuration,
        EmailVerificationService verificationService,
        ILogger<AccountRegistrationService> logger)
    {
        _connectionString =
            configuration.GetConnectionString("RathenaDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:RathenaDatabase is missing."
            );

        _verificationService = verificationService;
        _logger = logger;
    }

    public async Task<RegistrationResult> RegisterAsync(
        string username,
        string password,
        string email,
        string sex,
        CancellationToken cancellationToken = default)
    {
        await _verificationService.EnsureSchemaAsync(
            cancellationToken);

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        long accountId = 0;

        try
        {
            const string duplicateSql = """
                SELECT
                    EXISTS(
                        SELECT 1
                        FROM `login`
                        WHERE LOWER(`userid`) = LOWER(@username)
                    ) AS `UsernameExists`,
                    (
                        EXISTS(
                            SELECT 1
                            FROM `login`
                            WHERE LOWER(`email`) = LOWER(@email)
                        )
                        OR EXISTS(
                            SELECT 1
                            FROM `web_email_identity`
                            WHERE `email` = @email
                        )
                    ) AS `EmailExists`;
                """;

            await using (var duplicateCommand =
                         new MySqlCommand(
                             duplicateSql,
                             connection,
                             transaction))
            {
                duplicateCommand.Parameters.AddWithValue(
                    "@username",
                    username);

                duplicateCommand.Parameters.AddWithValue(
                    "@email",
                    email);

                await using var reader =
                    await duplicateCommand.ExecuteReaderAsync(
                        cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.GetBoolean("UsernameExists"))
                    {
                        await reader.DisposeAsync();
                        await transaction.RollbackAsync(
                            cancellationToken);

                        return RegistrationResult.Failure(
                            "That username is already in use.");
                    }

                    if (reader.GetBoolean("EmailExists"))
                    {
                        await reader.DisposeAsync();
                        await transaction.RollbackAsync(
                            cancellationToken);

                        return RegistrationResult.Failure(
                            "That email address is already registered.");
                    }
                }
            }

            const string insertSql = """
                INSERT INTO `login`
                (
                    `userid`,
                    `user_pass`,
                    `sex`,
                    `email`,
                    `group_id`,
                    `state`,
                    `unban_time`,
                    `expiration_time`,
                    `logincount`,
                    `last_ip`,
                    `character_slots`,
                    `pincode`,
                    `pincode_change`,
                    `vip_time`,
                    `old_group`,
                    `web_auth_token_enabled`
                )
                VALUES
                (
                    @username,
                    MD5(@password),
                    @sex,
                    @email,
                    0,
                    11,
                    0,
                    0,
                    0,
                    '',
                    0,
                    '',
                    0,
                    0,
                    0,
                    0
                );
                """;

            await using (var insertCommand =
                         new MySqlCommand(
                             insertSql,
                             connection,
                             transaction))
            {
                insertCommand.Parameters.AddWithValue(
                    "@username",
                    username);

                insertCommand.Parameters.AddWithValue(
                    "@password",
                    password);

                insertCommand.Parameters.AddWithValue("@sex", sex);
                insertCommand.Parameters.AddWithValue("@email", email);

                await insertCommand.ExecuteNonQueryAsync(
                    cancellationToken);

                accountId = insertCommand.LastInsertedId;
            }

            var verificationToken =
                await _verificationService
                    .CreateInitialVerificationAsync(
                        connection,
                        transaction,
                        accountId,
                        email,
                        cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return RegistrationResult.Success(verificationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await RollBackAndCleanUpAsync(
                connection,
                transaction,
                accountId,
                username,
                email);

            throw;
        }
        catch (MySqlException exception)
            when (exception.Number == 1062)
        {
            await RollBackAndCleanUpAsync(
                connection,
                transaction,
                accountId,
                username,
                email);

            return RegistrationResult.Failure(
                "That username or email address is already registered.");
        }
        catch (Exception exception)
        {
            await RollBackAndCleanUpAsync(
                connection,
                transaction,
                accountId,
                username,
                email);

            _logger.LogError(
                exception,
                "Account registration failed before verification was issued.");

            return RegistrationResult.Failure(
                "Account registration is temporarily unavailable. Please try again shortly.");
        }
    }

    private async Task RollBackAndCleanUpAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        string username,
        string email)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The registration transaction could not be rolled back.");
        }

        if (accountId <= 0
            || connection.State !=
               System.Data.ConnectionState.Open)
        {
            return;
        }

        try
        {
            const string cleanupSql = """
                DELETE FROM `login`
                WHERE `account_id` = @accountId
                  AND `userid` = @username
                  AND `email` = @email
                  AND `state` = 11
                  AND `logincount` = 0;
                """;

            await using var cleanupCommand =
                new MySqlCommand(cleanupSql, connection);

            cleanupCommand.Parameters.AddWithValue(
                "@accountId",
                accountId);

            cleanupCommand.Parameters.AddWithValue(
                "@username",
                username);

            cleanupCommand.Parameters.AddWithValue("@email", email);

            await cleanupCommand.ExecuteNonQueryAsync(
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "A pending rAthena account could not be cleaned up after registration failed.");
        }
    }
}

public sealed record RegistrationResult(
    bool Succeeded,
    string? Error,
    string? VerificationToken)
{
    public static RegistrationResult Success(
        string verificationToken) =>
        new(true, null, verificationToken);

    public static RegistrationResult Failure(string error) =>
        new(false, error, null);
}
