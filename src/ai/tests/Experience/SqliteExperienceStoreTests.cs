using Microsoft.Data.Sqlite;
using SourceSharp.Abi;
using SourceSharp.Ai.Store;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The SQLite store (plan_bots_teams_tf2 Part G, G.3): every store's contract
/// over a temp file, and what is the database's own: two processes sharing a
/// file sum, the schema's version and its migrations, and a file that cannot
/// be used is a refusal, never an exception and never overwritten.
/// </summary>
public sealed class SqliteExperienceStoreTests : ExperienceStoreContract, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-experience-").FullName;
    private int _files;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string NewPath() => Path.Combine(_dir, $"store{_files++}.db");

    protected override IExperienceStore Open() => SqliteExperienceStore.Open(NewPath());

    private static string Scalar(string path, string sql)
    {
        using SqliteConnection raw = SqliteFile.Open(path);
        return SqliteFile.Scalar(raw, sql);
    }

    private static List<string> Column(string path, string sql)
    {
        using SqliteConnection raw = SqliteFile.Open(path);
        using SqliteCommand command = raw.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }

    [Fact]
    public void TwoStoresOnOneFileSumTheirMerges()
    {
        string path = NewPath();
        using SqliteExperienceStore one = SqliteExperienceStore.Open(path), other = SqliteExperienceStore.Open(path);
        Assert.True(one.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 1d, 0d, 0d, 0d))), 0d));
        Assert.True(other.Merge(Key, Delta(d =>
        {
            d.Add(HereCell, new CellStat(2d, 0d, 1d, 0d, 0d));
            d.Add(new RoundRow(60d, 1, 1d, 0d, "a"));
        }), 0d));
        Assert.True(one.Merge(Key, Delta(d =>
        {
            d.Add(HereCell, new CellStat(1d, 0d, 0d, 0d, 0d));
            d.Add(new RoundRow(90d, 2, 1d, 0d, "b"));
        }), 0d));
        foreach (SqliteExperienceStore store in new[] { one, other })
        {
            MapExperience e = store.Load(Key, 0d);
            Assert.Equal(new CellStat(11d, 1d, 1d, 0d, 0d), e.Cells[HereCell]);
            Assert.Equal(2, e.Rounds);
            Assert.Equal(["a", "b"], e.RoundRows.Select(r => r.Composition));
        }
    }

    [Fact]
    public void WhatOneStoreMergedAnotherOpenedLaterLoads()
    {
        string path = NewPath();
        using (SqliteExperienceStore first = SqliteExperienceStore.Open(path))
            first.Merge(Key, Delta(FillEveryTable), HalfLife);
        using SqliteExperienceStore second = SqliteExperienceStore.Open(path);
        Assert.Equal(new MapExperience(Key, 1, Delta(FillEveryTable)).ContentHash, second.Load(Key, HalfLife).ContentHash);
    }

    [Fact]
    public void AFreshFileHoldsThisBuildsSchemaVersionAndThePlansTables()
    {
        string path = NewPath();
        using (SqliteExperienceStore store = SqliteExperienceStore.Open(path))
            Assert.Null(store.Refusal);
        Assert.Equal("1", Scalar(path, "SELECT value FROM meta WHERE key = 'schema_version'"));
        Assert.Equal(["cell_stat", "context", "link", "look", "map", "meta", "outcome", "round", "sight_line", "spot"],
                     Column(path, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name"));
    }

    /// <summary>No name, SteamID or slot has a column: the one name is the map's.</summary>
    [Fact]
    public void NoColumnOfTheSchemaIsNamedForAPlayer()
    {
        string path = NewPath();
        SqliteExperienceStore.Open(path).Dispose();
        List<string> columns = Column(path, "SELECT m.name || '.' || p.name FROM sqlite_master m JOIN pragma_table_info(m.name) p WHERE m.type = 'table' ORDER BY 1");
        Assert.Contains("cell_stat.seconds", columns);
        Assert.Contains("map.name", columns);
        Assert.DoesNotContain(columns, c => c != "map.name" && System.Text.RegularExpressions.Regex.IsMatch(c.Split('.')[1], "name|steam|slot|user|actor|player|identity|serial|account|address"));
    }

    private static readonly Migration Later = new(2, "ALTER TABLE map ADD COLUMN note TEXT; CREATE TABLE later (x INTEGER);");

    [Fact]
    public void AnOlderFileIsMigratedAndKeepsItsRows()
    {
        string path = NewPath();
        using (SqliteExperienceStore old = SqliteExperienceStore.Open(path))
            old.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife);
        Assert.Equal("1", Scalar(path, "SELECT value FROM meta WHERE key = 'schema_version'"));

        using (SqliteExperienceStore now = SqliteExperienceStore.Open(path, [.. SqliteSchema.Migrations, Later]))
        {
            Assert.Null(now.Refusal);
            Assert.Equal(8d, now.Load(Key, HalfLife).Cells[HereCell].Seconds);
        }

        Assert.Equal("2", Scalar(path, "SELECT value FROM meta WHERE key = 'schema_version'"));
        Assert.Equal("1", Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'later'"));
        Assert.Equal("1", Scalar(path, "SELECT COUNT(*) FROM pragma_table_info('map') WHERE name = 'note'"));
    }

    [Fact]
    public void AMigratedFileIsNotMigratedAgain()
    {
        string path = NewPath();
        SqliteExperienceStore.Open(path, [.. SqliteSchema.Migrations, Later]).Dispose();
        // Run twice, ALTER TABLE ... ADD COLUMN would fail on the column it added.
        using SqliteExperienceStore again = SqliteExperienceStore.Open(path, [.. SqliteSchema.Migrations, Later]);
        Assert.Null(again.Refusal);
    }

    [Fact]
    public void AFileOfANewerBuildIsRefusedAndLeftAsItIs()
    {
        string path = NewPath();
        using (SqliteExperienceStore newer = SqliteExperienceStore.Open(path, [.. SqliteSchema.Migrations, Later]))
            Assert.True(newer.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife));

        using (SqliteExperienceStore store = SqliteExperienceStore.Open(path))
        {
            Assert.Contains("newer", store.Refusal);
            Assert.Contains(path, store.Refusal);
            Assert.True(store.Load(Key, HalfLife).IsEmpty);
            Assert.False(store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(1d, 0d, 0d, 0d, 0d))), HalfLife));
            Assert.Equal(default, store.Counts(Key));
        }

        Assert.Equal("2", Scalar(path, "SELECT value FROM meta WHERE key = 'schema_version'"));
        Assert.Equal("8", Scalar(path, "SELECT seconds FROM cell_stat"));
    }

    /// <summary>First a good file is accepted, so the refusal below is of the garbage and not of every file.</summary>
    [Fact]
    public void AGoodFileIsAcceptedAndAFileOfGarbageIsRefusedWithoutThrowingOrBeingOverwritten()
    {
        string path = NewPath();
        using (SqliteExperienceStore good = SqliteExperienceStore.Open(path))
            Assert.True(good.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife));
        using (SqliteExperienceStore reopened = SqliteExperienceStore.Open(path))
        {
            Assert.Null(reopened.Refusal);
            Assert.Equal(8d, reopened.Load(Key, HalfLife).Cells[HereCell].Seconds);
        }

        foreach (string file in Directory.GetFiles(_dir, Path.GetFileName(path) + "*"))
            File.Delete(file);
        byte[] garbage = [.. Enumerable.Range(0, 8192).Select(i => (byte)(i * 31 + 7))];
        File.WriteAllBytes(path, garbage);

        using (SqliteExperienceStore store = SqliteExperienceStore.Open(path))
        {
            Assert.NotNull(store.Refusal);
            Assert.Contains(path, store.Refusal);
            Assert.DoesNotContain('\n', store.Refusal);
            MapExperience e = store.Load(Key, HalfLife);
            Assert.True(e.IsEmpty);
            Assert.Equal(Key, e.Key);
            Assert.False(store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(1d, 0d, 0d, 0d, 0d))), HalfLife));
            Assert.Equal(default, store.Counts(Key));
        }

        Assert.Equal(garbage, File.ReadAllBytes(path));
    }

    [Fact]
    public void ADatabaseOfSomethingElseIsRefusedAndGainsNoTable()
    {
        string path = NewPath();
        using (SqliteConnection raw = SqliteFile.Open(path))
            SqliteFile.Scalar(raw, "CREATE TABLE savings (amount INTEGER)");
        using (SqliteExperienceStore store = SqliteExperienceStore.Open(path))
        {
            Assert.Contains("something else", store.Refusal);
            Assert.False(store.Merge(Key, Delta(FillEveryTable), HalfLife));
        }

        Assert.Equal(["savings"], Column(path, "SELECT name FROM sqlite_master WHERE type = 'table'"));
    }

    [Fact]
    public void APathThatCannotBeOpenedIsRefusedWithoutThrowing()
    {
        string file = Path.Combine(_dir, "a-file");
        File.WriteAllText(file, "not a directory");
        using SqliteExperienceStore store = SqliteExperienceStore.Open(Path.Combine(file, "under", "store.db"));
        Assert.NotNull(store.Refusal);
        Assert.True(store.Load(Key, HalfLife).IsEmpty);
        Assert.False(store.Merge(Key, Delta(FillEveryTable), HalfLife));
    }

    [Fact]
    public void AStoreCreatesItsFilesDirectory()
    {
        string path = Path.Combine(_dir, "made", "deeper", "store.db");
        using SqliteExperienceStore store = SqliteExperienceStore.Open(path);
        Assert.Null(store.Refusal);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AFileAnotherHoldsLockedFailsTheMergeAndIsNoRefusal()
    {
        string path = NewPath();
        using SqliteExperienceStore store = SqliteExperienceStore.Open(path, SqliteSchema.Migrations, busyTimeoutMs: 20);
        using (SqliteConnection holder = SqliteFile.Open(path))
        {
            SqliteFile.Scalar(holder, "BEGIN IMMEDIATE");
            Assert.False(store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife));
            Assert.Null(store.Refusal);
            Assert.Equal(1, store.BusyFailures);
            SqliteFile.Scalar(holder, "COMMIT");
        }

        Assert.True(store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife));
        Assert.Equal(8d, store.Load(Key, HalfLife).Cells[HereCell].Seconds);
    }

    /// <summary>A practice row as the practice tool writes it (lane G8): Here to There, source Practice, its recipe's bytes.</summary>
    private static readonly LinkKey PracticeRow = new(3, Here, There, 1, LinkSource.Practice, new LinkRecipe([1, 2, 3]));

    [Fact]
    public void AToolsPracticeRowsWrittenWhileAGameHasTheFileOpenAndIsReadingItAreLoadedByTheGame()
    {
        // Lane G8: the practice tool writes while a game has the store open. The game's store, its round written; a read of
        // the game under way (a deferred transaction that has read: WAL keeps its snapshot); the tool's own store merges.
        string path = NewPath();
        using SqliteExperienceStore game = SqliteExperienceStore.Open(path);
        Assert.True(game.Merge(Key, Rounds(1), HalfLife));
        using (SqliteConnection reading = SqliteFile.Open(path))
        {
            using SqliteTransaction read = reading.BeginTransaction(deferred: true);
            using SqliteCommand count = reading.CreateCommand();
            count.Transaction = read;
            count.CommandText = "SELECT COUNT(*) FROM link";
            Assert.Equal(0L, (long)count.ExecuteScalar()!);
            using (SqliteExperienceStore tool = SqliteExperienceStore.Open(path))
                Assert.True(tool.Merge(Key, Delta(d => d.Add(PracticeRow, new LinkStat(40d, 36d, 80d, 2000d))), HalfLife));

            // The read under way still sees its own snapshot.
            Assert.Equal(0L, (long)count.ExecuteScalar()!);
        }

        MapExperience loaded = game.Load(Key, HalfLife);
        Assert.Equal(new LinkStat(40d, 36d, 80d, 2000d), loaded.Links[PracticeRow]);
        Assert.Equal(1, loaded.Rounds);
        Assert.Null(game.Refusal);
        Assert.Equal(0, game.BusyFailures);
    }

    [Fact]
    public void AToolsMergeWaitsForAGamesWriteToEndRatherThanFailing()
    {
        // The game's worker holds the write lock a moment (a round's flush): the tool's merge waits within the busy timeout.
        string path = NewPath();
        using SqliteExperienceStore game = SqliteExperienceStore.Open(path);
        Assert.True(game.Merge(Key, Rounds(1), HalfLife));
        using SqliteConnection holder = SqliteFile.Open(path);
        SqliteFile.Scalar(holder, "BEGIN IMMEDIATE");
        // Released from a thread of its own, not the pool: under a full test run the
        // pool's queue held a Task.Run past the 2 s busy timeout (failed twice at load
        // over 100, 2026-11-15) and the merge was refused before the lock ever lifted.
        var release = new Thread(() =>
        {
            Thread.Sleep(300);
            SqliteFile.Scalar(holder, "COMMIT");
        }) { IsBackground = true };
        release.Start();
        using SqliteExperienceStore tool = SqliteExperienceStore.Open(path);
        Assert.True(tool.Merge(Key, Delta(d => d.Add(PracticeRow, new LinkStat(40d, 36d, 80d, 2000d))), HalfLife));
        release.Join();
        Assert.Equal(0, tool.BusyFailures);
        Assert.Equal(36d, game.Load(Key, HalfLife).Links[PracticeRow].Successes);
    }

    [Fact]
    public void TheKeysAreEveryMapAndModeTheFileHoldsInTheOrderWritten()
    {
        string path = NewPath();
        using SqliteExperienceStore store = SqliteExperienceStore.Open(path);
        Assert.Empty(store.Keys());
        var other = new ExperienceKey("ctf_other", "def456", "ctf");
        store.Merge(Key, Rounds(1), HalfLife);
        store.Merge(other, Rounds(1), HalfLife);
        store.Merge(Key with { Mode = "koth" }, Rounds(1), HalfLife);
        Assert.Equal([Key, other, Key with { Mode = "koth" }], store.Keys());
    }

    [Fact]
    public void AMergeThatFailsHalfWayLeavesNothingBehind()
    {
        string path = NewPath();
        using SqliteExperienceStore store = SqliteExperienceStore.Open(path);
        store.Merge(Key, Rounds(1), HalfLife);
        // A trigger stops the merge at its first look row, after its map row, its round and its cells went in.
        using (SqliteConnection raw = SqliteFile.Open(path))
            SqliteFile.Scalar(raw, "CREATE TRIGGER stop BEFORE INSERT ON look BEGIN SELECT RAISE(ABORT, 'stopped'); END");
        Assert.False(store.Merge(Key, Delta(FillEveryTable), HalfLife));
        Assert.Equal("1", Scalar(path, "SELECT rounds FROM map"));
        Assert.Equal("0", Scalar(path, "SELECT COUNT(*) FROM cell_stat"));
    }

    /// <summary>The game resolves the native library from the managed directory (the load context has no other): the build must have put it there.</summary>
    [Fact]
    public void TheNativeLibraryIsWhereTheGameResolvesItFrom()
    {
        string managed = RepoLayout.Repo("bin", RepoLayout.Config, "bin", "managed");
        Assert.True(File.Exists(Path.Combine(managed, "SourceSharp.Ai.Store.dll")), $"no SourceSharp.Ai.Store.dll in {managed}");
        Assert.True(File.Exists(SqliteNative.PathIn(managed)), $"no {SqliteNative.PathIn(managed)}");
        Assert.NotEqual(0, SqliteNative.Load(SqliteNative.Library, managed));
        Assert.Equal(0, SqliteNative.Load(SqliteNative.Library, _dir));
        Assert.Equal(0, SqliteNative.Load("another", managed));
    }

    [Fact]
    public void TheModsFactoryOpensAStoreOfTheFile()
    {
        var stores = new SqliteExperienceStores(FakeEngine.Of<IModInfo>((m, _) => m.Name == "get_ManagedDir" ? AppContext.BaseDirectory : throw FakeEngine.Unasked(m)));
        string path = NewPath();
        using IExperienceStore store = stores.Open(path);
        Assert.Null(store.Refusal);
        Assert.True(store.Merge(Key, Delta(FillEveryTable), HalfLife));
        Assert.True(File.Exists(path));
    }
}
