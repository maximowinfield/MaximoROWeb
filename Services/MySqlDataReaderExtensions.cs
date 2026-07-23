using MySqlConnector;

namespace MaximoROWeb.Services;

internal static class MySqlDataReaderExtensions
{
    public static bool IsDBNull(
        this MySqlDataReader reader,
        string columnName) =>
        reader.IsDBNull(reader.GetOrdinal(columnName));
}
