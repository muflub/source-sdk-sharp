using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SourceSharp.Ai.Store;

/// <summary>One step of the schema's history: the statements that take a database from the version before to <see cref="Version"/>.</summary>
internal readonly record struct Migration(int Version, string Sql);

/// <summary>
/// The experience store's schema (plan_bots_teams_tf2 Part G, G.3's tables)
/// and how a file reaches it: <c>meta</c> holds <c>schema_version</c>, and
/// opening a file runs, in one transaction, every migration newer than the
/// version it holds. A file of a newer build, or a database of something
/// else, is refused and left as it is.
///
/// <para>The tables. <c>map</c> is one map build in one mode (name, checksum,
/// mode) with the rounds played on it, which is what rows fade by;
/// <c>context</c> is the rest of a row's dimensions (stage, role, class).
/// Every count is a REAL (a bot weighs a quarter, and rows fade) and every
/// row carries <c>round_no</c>, the map's round count when it was last
/// written. A place is a <c>bin</c>: a WorldBin packed into one integer.
/// <c>link</c> keeps the seconds and health cost of its successes as sums,
/// so a merge stays a sum; the means divide by <c>successes</c>.</para>
/// </summary>
internal static class SqliteSchema
{
    public const string VersionKey = "schema_version";

    private const string V1 = """
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
        CREATE TABLE map (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL, checksum TEXT NOT NULL, mode TEXT NOT NULL,
            rounds INTEGER NOT NULL DEFAULT 0,
            UNIQUE (name, checksum, mode));
        CREATE TABLE context (
            id INTEGER PRIMARY KEY,
            map_id INTEGER NOT NULL, stage INTEGER NOT NULL, role INTEGER NOT NULL, class INTEGER NOT NULL,
            UNIQUE (map_id, stage, role, class));
        CREATE TABLE cell_stat (
            context_id INTEGER NOT NULL, bin INTEGER NOT NULL, source INTEGER NOT NULL,
            seconds REAL NOT NULL, deaths REAL NOT NULL, kills REAL NOT NULL, damage_taken REAL NOT NULL, damage_dealt REAL NOT NULL,
            round_no INTEGER NOT NULL,
            PRIMARY KEY (context_id, bin, source)) WITHOUT ROWID;
        CREATE TABLE sight_line (
            context_id INTEGER NOT NULL, victim_bin INTEGER NOT NULL, killer_bin INTEGER NOT NULL, weapon_class INTEGER NOT NULL,
            count REAL NOT NULL, round_no INTEGER NOT NULL,
            PRIMARY KEY (context_id, victim_bin, killer_bin, weapon_class)) WITHOUT ROWID;
        CREATE TABLE look (
            context_id INTEGER NOT NULL, bin INTEGER NOT NULL, yaw INTEGER NOT NULL, pitch INTEGER NOT NULL,
            seconds REAL NOT NULL, round_no INTEGER NOT NULL,
            PRIMARY KEY (context_id, bin, yaw, pitch)) WITHOUT ROWID;
        CREATE TABLE spot (
            context_id INTEGER NOT NULL, kind INTEGER NOT NULL, bin INTEGER NOT NULL, yaw INTEGER NOT NULL,
            uses REAL NOT NULL, seconds_held REAL NOT NULL, kills REAL NOT NULL, deaths REAL NOT NULL,
            round_no INTEGER NOT NULL,
            PRIMARY KEY (context_id, kind, bin, yaw)) WITHOUT ROWID;
        CREATE TABLE link (
            map_id INTEGER NOT NULL, class INTEGER NOT NULL, from_bin INTEGER NOT NULL, to_bin INTEGER NOT NULL,
            kind INTEGER NOT NULL, source INTEGER NOT NULL, recipe BLOB NOT NULL,
            tries REAL NOT NULL, successes REAL NOT NULL, seconds REAL NOT NULL, health_cost REAL NOT NULL,
            round_no INTEGER NOT NULL,
            PRIMARY KEY (map_id, class, from_bin, to_bin, kind, source, recipe)) WITHOUT ROWID;
        CREATE TABLE outcome (
            context_id INTEGER NOT NULL, kind INTEGER NOT NULL, choice INTEGER NOT NULL,
            tries REAL NOT NULL, score REAL NOT NULL, round_no INTEGER NOT NULL,
            PRIMARY KEY (context_id, kind, choice)) WITHOUT ROWID;
        CREATE TABLE round (
            map_id INTEGER NOT NULL, round_no INTEGER NOT NULL,
            seconds REAL NOT NULL, winning_role INTEGER NOT NULL, humans REAL NOT NULL, bots REAL NOT NULL, composition TEXT NOT NULL,
            PRIMARY KEY (map_id, round_no)) WITHOUT ROWID;
        """;

    /// <summary>The schema's history, oldest first; the last one's version is this build's.</summary>
    public static IReadOnlyList<Migration> Migrations => [new(1, V1)];

    /// <summary>
    /// Brings <paramref name="connection"/>'s database to the newest of
    /// <paramref name="migrations"/>, and answers null; or answers why it is
    /// refused, having changed nothing.
    /// </summary>
    public static string? Migrate(SqliteConnection connection, IReadOnlyList<Migration> migrations)
    {
        int newest = migrations[^1].Version;
        // Immediate: two processes creating one file's schema take turns.
        using SqliteTransaction transaction = connection.BeginTransaction();
        int at = Version(connection, transaction, out string? foreign);
        if (foreign is not null)
            return foreign;
        if (at > newest)
            return string.Create(CultureInfo.InvariantCulture, $"its schema is version {at}, newer than the {newest} this build reads");
        if (at == newest)
            return null;
        foreach (Migration step in migrations)
        {
            if (step.Version > at)
                Run(connection, transaction, step.Sql);
        }

        Run(connection, transaction, string.Create(CultureInfo.InvariantCulture,
            $"INSERT INTO meta (key, value) VALUES ('{VersionKey}', '{newest}') ON CONFLICT (key) DO UPDATE SET value = excluded.value"));
        transaction.Commit();
        return null;
    }

    /// <summary>The version the database holds: 0 for an empty file; <paramref name="foreign"/> says why a database that is not an experience store is none.</summary>
    private static int Version(SqliteConnection connection, SqliteTransaction transaction, out string? foreign)
    {
        foreign = null;
        if (Scalar(connection, transaction, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta'") == "0")
        {
            if (Scalar(connection, transaction, "SELECT COUNT(*) FROM sqlite_master") != "0")
                foreign = "it is a database of something else (it has tables and no meta table)";
            return 0;
        }

        string held = Scalar(connection, transaction, $"SELECT value FROM meta WHERE key = '{VersionKey}'");
        if (int.TryParse(held, NumberStyles.None, CultureInfo.InvariantCulture, out int version) && version > 0)
            return version;
        foreign = $"its meta table holds no {VersionKey}";
        return 0;
    }

    private static string Scalar(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }

    private static void Run(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
