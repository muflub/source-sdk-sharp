using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapTools.Nav;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>dm_lockdown (the committed bsp) built once, with no content (every prop's model missing), into a store of its own.</summary>
public sealed class LockdownBuild : IDisposable
{
    public LockdownBuild()
    {
        Bsp = File.ReadAllBytes(RepoLayout.Repo("game", "mod_test", "maps", "dm_lockdown.bsp"));
        Store = new BuiltNavStore(Dir);
        // With the floor check (ai_nav_build_check 1), so its facts read the same build.
        Job = NavBuildJob.Start(NavBuildInputs.Read("dm_lockdown", Bsp, _ => null), Settings(4, check: true), Store, null);
        Started = Environment.CurrentManagedThreadId;
        while (!Job.Done)
            Thread.Sleep(20);
        Outcome = Job.Outcome;
    }

    public static NavBuildSettings Settings(int threads, double timeoutSeconds = 600, bool check = false)
        => new(threads, TimeSpan.FromSeconds(timeoutSeconds), NavBuildSettings.DefaultMaxLeaves, check);

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ai-navbuild-" + Guid.NewGuid().ToString("N"));

    public byte[] Bsp { get; }

    public BuiltNavStore Store { get; }

    public NavBuildJob Job { get; }

    public int Started { get; }

    public NavBuildOutcome Outcome { get; }

    public void Dispose()
    {
        Job.Release();
        if (Directory.Exists(Dir))
            Directory.Delete(Dir, recursive: true);
    }
}

/// <summary>
/// One build of a map's navigation off the main thread (plan_bots_teams_tf2
/// E.3 steps 3-6): it writes its file and stamp into the store, binds and
/// derives it, runs on threads of its own, tells its progress, stops within
/// two seconds when cancelled and leaves no file, and answers every failure as
/// a reason, retryable only for a timeout or an I/O error.
/// </summary>
public sealed class NavBuildJobTests(LockdownBuild lockdown) : IClassFixture<LockdownBuild>
{
    private string Sha => BuiltNavStore.Sha256(lockdown.Bsp);

    [Fact]
    public void ABuildWritesItsFileIntoTheStoreAndHandsBackTheLevelFromIt()
    {
        NavBuildOutcome.Built built = Assert.IsType<NavBuildOutcome.Built>(lockdown.Outcome);

        byte[] file = File.ReadAllBytes(lockdown.Store.PathFor("dm_lockdown", Sha));
        Assert.Equal(BuiltNavStore.Sha256(file), built.Stamp.NavSha256);
        Assert.Equal(file.Length, built.Bytes);
        Assert.True(built.Nav.Built);
        Assert.Equal(built.Stamp.NavSha256, built.Nav.NavSha256);
        Assert.True(built.Nav.Nav.Graph.CellCount > 1000, $"{built.Nav.Nav.Graph.CellCount} cells");
    }

    [Fact]
    public void TheStampNamesTheBuilderAndTheReaderAndTheModelsAskedFor()
    {
        NavBuildOutcome.Built built = Assert.IsType<NavBuildOutcome.Built>(lockdown.Outcome);
        string stamp = lockdown.Store.TryReadStamp("dm_lockdown", Sha)!;

        Assert.Contains($"\"maptools\": \"{MapToolsVersion.Package}\"", stamp, StringComparison.Ordinal);
        Assert.Contains($"\"mapformats\": \"{NavReaderVersion.Package}\"", stamp, StringComparison.Ordinal);
        Assert.Contains($"\"bsp_sha256\": \"{Sha}\"", stamp, StringComparison.Ordinal);
        Assert.True(built.Stamp.Models > 0);
        Assert.Equal(built.Stamp.Models, built.Stamp.MissingModels);
    }

    [Fact]
    public void TheFloorCheckRanOnTheBuildAndItsWordIsInTheStamp()
    {
        NavBuildOutcome.Built built = Assert.IsType<NavBuildOutcome.Built>(lockdown.Outcome);

        Assert.StartsWith("floor check: ", built.CheckLine, StringComparison.Ordinal);
        Assert.Contains("places a player stands", built.CheckLine, StringComparison.Ordinal);
        Assert.NotEqual("not run", built.Stamp.Check);
        Assert.Contains($"\"check\": \"{built.Stamp.Check}\"", lockdown.Store.TryReadStamp("dm_lockdown", Sha)!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildRanOnItsOwnThreadsNotTheCallers()
    {
        Assert.NotEqual(0, lockdown.Job.BuiltOnThread);
        Assert.NotEqual(lockdown.Started, lockdown.Job.BuiltOnThread);
        Assert.Contains(lockdown.Job.BuiltOnThread, lockdown.Job.ThreadIds);
    }

    [Fact]
    public void ProgressReachesEveryCell()
    {
        (int done, int total) = lockdown.Job.Cells;
        Assert.True(total > 1, $"{total} cells");
        Assert.Equal(total, done);
        Assert.Equal(100, lockdown.Job.Percent);
    }

    [Fact]
    public void TheNextLevelLoadsTheBuildFromTheStore()
    {
        NavLoadResult again = NavLoader.Load("dm_lockdown", p => p == "maps/dm_lockdown.bsp" ? lockdown.Bsp : null, null, built: lockdown.Store);

        Assert.Null(again.Refusal);
        Assert.True(again.Nav!.Built);
    }

    [Fact]
    public void ACancelledBuildStopsWithinTwoSecondsAndLeavesNoFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ai-navcancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BuiltNavStore(dir);
            NavBuildJob job = NavBuildJob.Start(NavBuildInputs.Read("dm_lockdown", lockdown.Bsp, _ => null), LockdownBuild.Settings(2), store, null);
            for (int i = 0; i < 200 && job.Cells.Done == 0; i++)
                Thread.Sleep(10);
            Assert.False(job.Done, "the build finished before it could be cancelled");

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            bool inTime = job.Stop(TimeSpan.FromSeconds(SourceSharp.Ai.Server.Nav.AiNav.BuildStopSeconds));
            TimeSpan took = System.Diagnostics.Stopwatch.GetElapsedTime(start);

            Assert.True(inTime && took < TimeSpan.FromSeconds(SourceSharp.Ai.Server.Nav.AiNav.BuildStopSeconds), $"stopped in {took.TotalMilliseconds:F0} ms");
            Assert.IsType<NavBuildOutcome.Cancelled>(job.Outcome);
            Assert.False(File.Exists(store.PathFor("dm_lockdown", Sha)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AStoppedBuildLeavesNoThreadAlive()
    {
        NavBuildJob job = NavBuildJob.Start(NavBuildInputs.Read("dm_lockdown", lockdown.Bsp, _ => null), LockdownBuild.Settings(2),
                                            new BuiltNavStore(Path.Combine(Path.GetTempPath(), "ai-navstop-" + Guid.NewGuid().ToString("N"))), null);
        Assert.Equal(2, job.ThreadsAlive);

        job.Stop(TimeSpan.FromSeconds(SourceSharp.Ai.Server.Nav.AiNav.BuildStopSeconds));

        Assert.Equal(0, job.ThreadsAlive);
        Assert.True(job.Done);
    }

    [Fact]
    public void ABuildThatRunsOutOfTimeFailsAndMayBeRetried()
    {
        NavBuildJob job = NavBuildJob.Start(NavBuildInputs.Read("dm_lockdown", lockdown.Bsp, _ => null), LockdownBuild.Settings(2, timeoutSeconds: 0.001),
                                            new BuiltNavStore(Path.Combine(Path.GetTempPath(), "ai-navtime-" + Guid.NewGuid().ToString("N"))), null);
        while (!job.Done)
            Thread.Sleep(10);
        job.Release();

        NavBuildOutcome.Failed failed = Assert.IsType<NavBuildOutcome.Failed>(job.Outcome);
        Assert.True(failed.Retry);
        Assert.Contains("ai_nav_build_timeout", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoreThatCannotBeWrittenFailsAsAnIoErrorThatMayBeRetried()
    {
        string file = Path.Combine(Path.GetTempPath(), "ai-navfile-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "a file where the store's directory would be");
        try
        {
            NavBuildJob job = NavBuildJob.Start(NavBuildInputs.Read("dm_lockdown", lockdown.Bsp, _ => null), LockdownBuild.Settings(2),
                                                new BuiltNavStore(file), null);
            while (!job.Done)
                Thread.Sleep(10);
            job.Release();

            NavBuildOutcome.Failed failed = Assert.IsType<NavBuildOutcome.Failed>(job.Outcome);
            Assert.True(failed.Retry, failed.Reason);
            Assert.StartsWith("IOException", failed.Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AMapWithNoWorldFailsByNameAndIsNotRetried()
    {
        NavBuildJob job = NavBuildJob.Start(NavBuildInputs.Read("dm_test", TinyBsp.Make(TinyBsp.Stock), _ => null), LockdownBuild.Settings(1),
                                            new BuiltNavStore(Path.Combine(Path.GetTempPath(), "ai-navnoworld-" + Guid.NewGuid().ToString("N"))), null);
        while (!job.Done)
            Thread.Sleep(10);
        job.Release();

        NavBuildOutcome.Failed failed = Assert.IsType<NavBuildOutcome.Failed>(job.Outcome);
        Assert.False(failed.Retry);
        Assert.Contains("no world model", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANavOverTheLeafCapIsRefusedByName()
    {
        NavBuildException e = Assert.Throws<NavBuildException>(() => NavBuildJob.CheckLeaves(5, 4));
        Assert.Contains("over ai_nav_build_max_leaves", e.Message, StringComparison.Ordinal);
        NavBuildJob.CheckLeaves(4, 4);
    }
}
