using MySqlConnector;

namespace MaximoROWeb.Services;

public sealed class AccountRegistrationService
{
    private readonly string _connectionString;

    public AccountRegistrationService(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("RathenaDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:RathenaDatabase is missing."
            );
    }

    public async Task<RegistrationResult> RegisterAsync(
        string username,
        string password,
        string email,
        string sex,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string duplicateSql = """
            SELECT
                EXISTS(
                    SELECT 1
                    FROM `login`
                    WHERE LOWER(`userid`) = LOWER(@username)
                ) AS UsernameExists,
                EXISTS(
                    SELECT 1
                    FROM `login`
                    WHERE LOWER(`email`) = LOWER(@email)
                ) AS EmailExists;
            """;

        await using (var duplicateCommand =
                     new MySqlCommand(duplicateSql, connection))
        {
            duplicateCommand.Parameters.AddWithValue("@username", username);
            duplicateCommand.Parameters.AddWithValue("@email", email);

            await using var reader =
                await duplicateCommand.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                var usernameExists = reader.GetBoolean("UsernameExists");
                var emailExists = reader.GetBoolean("EmailExists");

                if (usernameExists)
                {
                    return RegistrationResult.Failure(
                        "That username is already in use."
                    );
                }

                if (emailExists)
                {
                    return RegistrationResult.Failure(
                        "That email address is already registered."
                    );
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
                0,
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

        await using var insertCommand =
            new MySqlCommand(insertSql, connection);

        insertCommand.Parameters.AddWithValue("@username", username);
        insertCommand.Parameters.AddWithValue("@password", password);
        insertCommand.Parameters.AddWithValue("@sex", sex);
        insertCommand.Parameters.AddWithValue("@email", email);

        try
        {
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            return RegistrationResult.Success();
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            return RegistrationResult.Failure(
                "That username or email address is already registered."
            );
        }
    }
}

public sealed record RegistrationResult(bool Succeeded, string? Error)
{
    public static RegistrationResult Success() =>
        new(true, null);

    public static RegistrationResult Failure(string error) =>
        new(false, error);
}