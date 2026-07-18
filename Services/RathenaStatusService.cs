using System.Net.Sockets;
using MaximoROweb.Models;
using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

namespace MaximoROWeb.Services;

public sealed class RathenaStatusService
{
    private const string CacheKey = "rathena-server-status";

    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RathenaStatusService> _logger;

    public RathenaStatusService(
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<RathenaStatusService> logger)
    {
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ServerStatusViewModel> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(
                CacheKey,
                out ServerStatusViewModel? cachedStatus) &&
            cachedStatus is not null)
        {
            return cachedStatus;
        }

        Task<bool> loginTask = CheckPortAsync(
            "127.0.0.1",
            6900,
            cancellationToken);

        Task<bool> charTask = CheckPortAsync(
            "127.0.0.1",
            6121,
            cancellationToken);

        Task<bool> mapTask = CheckPortAsync(
            "127.0.0.1",
            5121,
            cancellationToken);

        await Task.WhenAll(loginTask, charTask, mapTask);

        int onlinePlayers = await GetOnlinePlayerCountAsync(
            cancellationToken);

        var status = new ServerStatusViewModel
        {
            LoginServerOnline = await loginTask,
            CharServerOnline = await charTask,
            MapServerOnline = await mapTask,
            OnlinePlayers = onlinePlayers,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };

        _cache.Set(
            CacheKey,
            status,
            TimeSpan.FromSeconds(5));

        return status;
    }

    private async Task<bool> CheckPortAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();

            using var timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutSource.CancelAfter(TimeSpan.FromSeconds(2));

            await client.ConnectAsync(
                host,
                port,
                timeoutSource.Token);

            return client.Connected;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "rAthena port {Port} is unavailable.",
                port);

            return false;
        }
    }

    private async Task<int> GetOnlinePlayerCountAsync(
        CancellationToken cancellationToken)
    {
        string? connectionString =
            _configuration.GetConnectionString("RathenaDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning(
                "The rAthena database connection string is missing.");

            return 0;
        }

        try
        {
            await using var connection =
                new MySqlConnection(connectionString);

            await connection.OpenAsync(cancellationToken);

            const string sql = """
                SELECT COUNT(*)
                FROM `char`
                WHERE `online` = 1;
                """;

            await using var command =
                new MySqlCommand(sql, connection);

            object? result =
                await command.ExecuteScalarAsync(cancellationToken);

            return Convert.ToInt32(result);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to retrieve the rAthena online-player count.");

            return 0;
        }
    }
}