using Microsoft.Data.Sqlite;

namespace SourceSharp.Ai.Store;

/// <summary>
/// How the experience store opens its database file (plan_bots_teams_tf2 Part
/// G, G.3): created if absent, in WAL mode with a busy timeout, so two game
/// processes sharing one file wait for each other's short writes instead of
/// failing. Never pooled: closing the connection closes the file.
/// </summary>
internal static class SqliteFile
{
    /// <summary>How long a statement waits for another process's lock before it gives up, in milliseconds.</summary>
    public const int BusyTimeoutMs = 2000;

    /// <summary>
    /// An open connection to <paramref name="path"/> that waits <paramref name="busyTimeoutMs"/> for
    /// another's lock; throws <see cref="SqliteException"/> when it is no database or cannot be opened.
    /// </summary>
    public static SqliteConnection Open(string path, int busyTimeoutMs = BusyTimeoutMs)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            // The provider retries a busy statement itself for this long, in whole
            // seconds; 0 would be for ever.
            DefaultTimeout = Math.Max(1, busyTimeoutMs / 1000),
        }.ToString());
        try
        {
            connection.Open();
            Scalar(connection, "PRAGMA journal_mode=WAL");
            Scalar(connection, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"PRAGMA busy_timeout={busyTimeoutMs}"));
            Scalar(connection, "PRAGMA synchronous=NORMAL");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>The first column of the first row <paramref name="sql"/> answers, as text; "" for none.</summary>
    public static string Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
}
