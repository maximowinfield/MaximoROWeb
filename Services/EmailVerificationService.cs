using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace MaximoROWeb.Services;

public enum EmailVerificationOutcome
{
    Verified,
    AlreadyVerified,
    Expired,
    Invalid,
    AccountUnavailable
}

public enum ResendTokenOutcome
{
    Created,
    NotAvailable,
    Cooldown
}

public sealed record ResendTokenResult(
    ResendTokenOutcome Outcome,
    string? Email = null,
    string? Username = null,
    string? Token = null);

public sealed class EmailVerificationService
{
    private readonly string _connectionString;
    private readonly IOptionsMonitor<EmailVerificationOptions> _options;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public EmailVerificationService(
        IConfiguration configuration,
        IOptionsMonitor<EmailVerificationOptions> options)
    {
        _connectionString =
            configuration.GetConnectionString("RathenaDatabase")
            ?? string.Empty;

        _options = options;
    }

    public async Task EnsureSchemaAsync(
        CancellationToken cancellationToken = default)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync(cancellationToken);

        try
        {
            if (_schemaReady)
            {
                return;
            }

            EnsureConnectionConfigured();

            await using var connection =
                new MySqlConnection(_connectionString);

            await connection.OpenAsync(cancellationToken);

            const string identitySql = """
                CREATE TABLE IF NOT EXISTS `web_email_identity`
                (
                    `account_id` INT UNSIGNED NOT NULL,
                    `email` VARCHAR(39) NOT NULL,
                    `created_at_utc` DATETIME(6) NOT NULL,
                    `verified_at_utc` DATETIME(6) NULL,
                    PRIMARY KEY (`account_id`),
                    UNIQUE KEY `uq_web_email_identity_email` (`email`)
                )
                ENGINE=InnoDB
                DEFAULT CHARSET=utf8mb4
                COLLATE=utf8mb4_unicode_ci;
                """;

            const string tokenSql = """
                CREATE TABLE IF NOT EXISTS `web_email_verification_token`
                (
                    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                    `account_id` INT UNSIGNED NOT NULL,
                    `token_hash` BINARY(32) NOT NULL,
                    `created_at_utc` DATETIME(6) NOT NULL,
                    `expires_at_utc` DATETIME(6) NOT NULL,
                    `consumed_at_utc` DATETIME(6) NULL,
                    PRIMARY KEY (`id`),
                    UNIQUE KEY `uq_web_email_token_hash` (`token_hash`),
                    KEY `ix_web_email_token_account` (`account_id`),
                    KEY `ix_web_email_token_expiry` (`expires_at_utc`)
                )
                ENGINE=InnoDB
                DEFAULT CHARSET=utf8mb4
                COLLATE=utf8mb4_unicode_ci;
                """;

            await using (var identityCommand =
                         new MySqlCommand(identitySql, connection))
            {
                await identityCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using (var tokenCommand =
                         new MySqlCommand(tokenSql, connection))
            {
                await tokenCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<string> CreateInitialVerificationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        string email,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var token = CreateToken();

        const string identitySql = """
            INSERT INTO `web_email_identity`
            (
                `account_id`,
                `email`,
                `created_at_utc`,
                `verified_at_utc`
            )
            VALUES
            (
                @accountId,
                @email,
                @createdAtUtc,
                NULL
            );
            """;

        await using (var identityCommand =
                     new MySqlCommand(
                         identitySql,
                         connection,
                         transaction))
        {
            identityCommand.Parameters.AddWithValue(
                "@accountId",
                accountId);

            identityCommand.Parameters.AddWithValue("@email", email);
            identityCommand.Parameters.AddWithValue("@createdAtUtc", now);

            await identityCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertTokenAsync(
            connection,
            transaction,
            accountId,
            token,
            now,
            cancellationToken);

        return token;
    }

    public async Task<ResendTokenResult> CreateResendTokenAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        EnsureConnectionConfigured();

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        const string accountSql = """
            SELECT
                identity_record.`account_id`,
                identity_record.`email`,
                identity_record.`verified_at_utc`,
                login_record.`userid`,
                login_record.`state`,
                (
                    SELECT MAX(token_record.`created_at_utc`)
                    FROM `web_email_verification_token` token_record
                    WHERE token_record.`account_id` =
                          identity_record.`account_id`
                ) AS `last_token_created_at_utc`
            FROM `web_email_identity` identity_record
            INNER JOIN `login` login_record
                ON login_record.`account_id` =
                   identity_record.`account_id`
            WHERE identity_record.`email` = @email
            LIMIT 1
            FOR UPDATE;
            """;

        long accountId;
        string storedEmail;
        string username;
        int state;
        DateTime? verifiedAtUtc;
        DateTime? lastTokenCreatedAtUtc;

        await using (var accountCommand =
                     new MySqlCommand(
                         accountSql,
                         connection,
                         transaction))
        {
            accountCommand.Parameters.AddWithValue("@email", email);

            await using var reader =
                await accountCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return new ResendTokenResult(
                    ResendTokenOutcome.NotAvailable);
            }

            accountId = reader.GetInt64("account_id");
            storedEmail = reader.GetString("email");
            username = reader.GetString("userid");
            state = reader.GetInt32("state");

            verifiedAtUtc = reader.IsDBNull("verified_at_utc")
                ? null
                : reader.GetDateTime("verified_at_utc");

            lastTokenCreatedAtUtc =
                reader.IsDBNull("last_token_created_at_utc")
                    ? null
                    : reader.GetDateTime(
                        "last_token_created_at_utc");
        }

        if (verifiedAtUtc is not null || state != 11)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ResendTokenResult(
                ResendTokenOutcome.NotAvailable);
        }

        var now = DateTime.UtcNow;
        var cooldownSeconds = Math.Clamp(
            _options.CurrentValue.ResendCooldownSeconds,
            30,
            3600);

        if (lastTokenCreatedAtUtc is not null
            && lastTokenCreatedAtUtc.Value >
               now.AddSeconds(-cooldownSeconds))
        {
            await transaction.CommitAsync(cancellationToken);
            return new ResendTokenResult(ResendTokenOutcome.Cooldown);
        }

        var token = CreateToken();

        await InsertTokenAsync(
            connection,
            transaction,
            accountId,
            token,
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ResendTokenResult(
            ResendTokenOutcome.Created,
            storedEmail,
            username,
            token);
    }

    public async Task<EmailVerificationOutcome> VerifyAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > 128)
        {
            return EmailVerificationOutcome.Invalid;
        }

        await EnsureSchemaAsync(cancellationToken);
        EnsureConnectionConfigured();

        var tokenHash = HashToken(token);

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        const string tokenSql = """
            SELECT
                token_record.`account_id`,
                token_record.`expires_at_utc`,
                token_record.`consumed_at_utc`,
                identity_record.`verified_at_utc`,
                login_record.`state`
            FROM `web_email_verification_token` token_record
            INNER JOIN `web_email_identity` identity_record
                ON identity_record.`account_id` =
                   token_record.`account_id`
            LEFT JOIN `login` login_record
                ON login_record.`account_id` =
                   token_record.`account_id`
            WHERE token_record.`token_hash` = @tokenHash
            LIMIT 1
            FOR UPDATE;
            """;

        long accountId;
        DateTime expiresAtUtc;
        DateTime? consumedAtUtc;
        DateTime? verifiedAtUtc;
        int? accountState;

        await using (var tokenCommand =
                     new MySqlCommand(
                         tokenSql,
                         connection,
                         transaction))
        {
            tokenCommand.Parameters.Add(
                "@tokenHash",
                MySqlDbType.Binary,
                tokenHash.Length).Value = tokenHash;

            await using var reader =
                await tokenCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return EmailVerificationOutcome.Invalid;
            }

            accountId = reader.GetInt64("account_id");
            expiresAtUtc = reader.GetDateTime("expires_at_utc");

            consumedAtUtc = reader.IsDBNull("consumed_at_utc")
                ? null
                : reader.GetDateTime("consumed_at_utc");

            verifiedAtUtc = reader.IsDBNull("verified_at_utc")
                ? null
                : reader.GetDateTime("verified_at_utc");

            accountState = reader.IsDBNull("state")
                ? null
                : reader.GetInt32("state");
        }

        if (accountState is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return EmailVerificationOutcome.AccountUnavailable;
        }

        if (verifiedAtUtc is not null || accountState == 0)
        {
            await MarkAccountVerifiedAsync(
                connection,
                transaction,
                accountId,
                DateTime.UtcNow,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return EmailVerificationOutcome.AlreadyVerified;
        }

        if (consumedAtUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return EmailVerificationOutcome.Invalid;
        }

        if (expiresAtUtc <= DateTime.UtcNow)
        {
            await transaction.CommitAsync(cancellationToken);
            return EmailVerificationOutcome.Expired;
        }

        if (accountState != 11)
        {
            await transaction.CommitAsync(cancellationToken);
            return EmailVerificationOutcome.AccountUnavailable;
        }

        var verifiedNow = DateTime.UtcNow;

        const string verifyIdentitySql = """
            UPDATE `web_email_identity`
            SET `verified_at_utc` = @verifiedAtUtc
            WHERE `account_id` = @accountId
              AND `verified_at_utc` IS NULL;
            """;

        await using (var identityCommand =
                     new MySqlCommand(
                         verifyIdentitySql,
                         connection,
                         transaction))
        {
            identityCommand.Parameters.AddWithValue(
                "@verifiedAtUtc",
                verifiedNow);

            identityCommand.Parameters.AddWithValue(
                "@accountId",
                accountId);

            await identityCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string unlockAccountSql = """
            UPDATE `login`
            SET `state` = 0
            WHERE `account_id` = @accountId
              AND `state` = 11;
            """;

        await using (var accountCommand =
                     new MySqlCommand(
                         unlockAccountSql,
                         connection,
                         transaction))
        {
            accountCommand.Parameters.AddWithValue(
                "@accountId",
                accountId);

            var updatedRows =
                await accountCommand.ExecuteNonQueryAsync(
                    cancellationToken);

            if (updatedRows != 1)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return EmailVerificationOutcome.AccountUnavailable;
            }
        }

        await ConsumeAccountTokensAsync(
            connection,
            transaction,
            accountId,
            verifiedNow,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return EmailVerificationOutcome.Verified;
    }

    private async Task InsertTokenAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        string token,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        const string tokenSql = """
            INSERT INTO `web_email_verification_token`
            (
                `account_id`,
                `token_hash`,
                `created_at_utc`,
                `expires_at_utc`,
                `consumed_at_utc`
            )
            VALUES
            (
                @accountId,
                @tokenHash,
                @createdAtUtc,
                @expiresAtUtc,
                NULL
            );
            """;

        var tokenHash = HashToken(token);
        var tokenLifetimeMinutes = Math.Clamp(
            _options.CurrentValue.TokenLifetimeMinutes,
            15,
            10_080);

        await using var tokenCommand =
            new MySqlCommand(tokenSql, connection, transaction);

        tokenCommand.Parameters.AddWithValue("@accountId", accountId);

        tokenCommand.Parameters.Add(
            "@tokenHash",
            MySqlDbType.Binary,
            tokenHash.Length).Value = tokenHash;

        tokenCommand.Parameters.AddWithValue(
            "@createdAtUtc",
            createdAtUtc);

        tokenCommand.Parameters.AddWithValue(
            "@expiresAtUtc",
            createdAtUtc.AddMinutes(tokenLifetimeMinutes));

        await tokenCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkAccountVerifiedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        DateTime verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        const string identitySql = """
            UPDATE `web_email_identity`
            SET `verified_at_utc` =
                COALESCE(`verified_at_utc`, @verifiedAtUtc)
            WHERE `account_id` = @accountId;
            """;

        await using (var identityCommand =
                     new MySqlCommand(
                         identitySql,
                         connection,
                         transaction))
        {
            identityCommand.Parameters.AddWithValue(
                "@verifiedAtUtc",
                verifiedAtUtc);

            identityCommand.Parameters.AddWithValue(
                "@accountId",
                accountId);

            await identityCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await ConsumeAccountTokensAsync(
            connection,
            transaction,
            accountId,
            verifiedAtUtc,
            cancellationToken);
    }

    private static async Task ConsumeAccountTokensAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        DateTime consumedAtUtc,
        CancellationToken cancellationToken)
    {
        const string consumeSql = """
            UPDATE `web_email_verification_token`
            SET `consumed_at_utc` = @consumedAtUtc
            WHERE `account_id` = @accountId
              AND `consumed_at_utc` IS NULL;
            """;

        await using var consumeCommand =
            new MySqlCommand(consumeSql, connection, transaction);

        consumeCommand.Parameters.AddWithValue(
            "@consumedAtUtc",
            consumedAtUtc);

        consumeCommand.Parameters.AddWithValue(
            "@accountId",
            accountId);

        await consumeCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string CreateToken() =>
        WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private void EnsureConnectionConfigured()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:RathenaDatabase is missing.");
        }
    }
}

public sealed class EmailVerificationSchemaInitializer
    : IHostedService
{
    private readonly EmailVerificationService _verificationService;
    private readonly ILogger<EmailVerificationSchemaInitializer> _logger;

    public EmailVerificationSchemaInitializer(
        EmailVerificationService verificationService,
        ILogger<EmailVerificationSchemaInitializer> logger)
    {
        _verificationService = verificationService;
        _logger = logger;
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _verificationService.EnsureSchemaAsync(
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Email verification tables could not be initialized. Registration will remain unavailable until the database is ready.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
