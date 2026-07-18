namespace MaximoROweb.Models;

public sealed class ServerStatusViewModel
{
    public bool LoginServerOnline { get; init; }

    public bool CharServerOnline { get; init; }

    public bool MapServerOnline { get; init; }

    public int OnlinePlayers { get; init; }

    public DateTimeOffset CheckedAtUtc { get; init; }

    public bool IsOnline =>
        LoginServerOnline &&
        CharServerOnline &&
        MapServerOnline;
}