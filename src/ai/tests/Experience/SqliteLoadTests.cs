using Microsoft.Data.Sqlite;
using SourceSharp.Ai.Store;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The first fact of the SQLite store (plan_bots_teams_tf2 Part G, G.10): the
/// native SQLite library loads in this host and opens a database in a temp
/// file, in WAL mode with the busy timeout. A library that will not load fails
/// here, before any fact about rows.
/// </summary>
public sealed class SqliteLoadTests
{
    [Fact]
    public void TheNativeLibraryLoadsAndOpensADatabaseInATempFile()
    {
        string dir = Directory.CreateTempSubdirectory("ss-experience-").FullName;
        try
        {
            string path = Path.Combine(dir, "load.db");
            using (SqliteConnection connection = SqliteFile.Open(path))
            {
                Assert.Matches(@"^3\.\d+\.\d+", SqliteFile.Scalar(connection, "SELECT sqlite_version()"));
                Assert.Equal("wal", SqliteFile.Scalar(connection, "PRAGMA journal_mode"));
                Assert.Equal(SqliteFile.BusyTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture), SqliteFile.Scalar(connection, "PRAGMA busy_timeout"));
                SqliteFile.Scalar(connection, "CREATE TABLE t(x INTEGER)");
                SqliteFile.Scalar(connection, "INSERT INTO t VALUES (42)");
                Assert.Equal("42", SqliteFile.Scalar(connection, "SELECT x FROM t"));
            }

            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
