using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Shared;
using SourceSharp.Tests;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The rocket jump's service as the game composes it (plan_bots_teams_tf2_part_g G.5 steps 2 and
/// 6), over this tree's built nav of ss_ai_tf_jump: a navigation set opens the level's desk without
/// reading the nav on the game's thread, a brain's ask is answered by a task and never by the
/// brain, the run lane has a jump and the low lane's slab has none, and the binding hands the
/// book to its trees and the program to the applier.
/// </summary>
public class TFJumpsTests
{
    /// <summary>The game's files: the rocket launcher's script, as the mod's tests hold it.</summary>
    private sealed class LauncherScript(bool present = true) : IEngineFiles
    {
        private const string Script = "scripts/tf_weapon_rocketlauncher.txt";

        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

        public string? ReadGameFile(string path) => ReadGameFileBytes(path) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;

        public byte[]? ReadGameFileBytes(string path)
            => present && path == Script ? File.ReadAllBytes(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons", "tf_weapon_rocketlauncher.txt")) : null;

        public byte[]? ReadFileOnPath(string path, string pathId) => ReadGameFileBytes(path);
    }

    private sealed class Log : ILog
    {
        public readonly List<string> Lines = [];

        public void Msg(string message) => Lines.Add(message);

        public void Warning(string message) => Lines.Add("warning: " + message);
    }

    private sealed class Navigation : IAiNavigation
    {
        public LoadedNav? Current { get; set; }

        public string? Refusal => Current is null ? "no navigation" : null;

        public NavObstacleState? Obstacles => null;

        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;

        public NavSight? Sight => null;

        public ChokeSlot? Chokes => null;

        public string? CacheDirectory { get; set; }
    }

    /// <summary>A level whose placed entities are these.</summary>
    private sealed class Placed(params MapEntity[] all) : ITFLevelEntities
    {
        public MapEntities Read() => new(all, null);
    }

    /// <summary>This tree's built nav of the jump map, loaded as the binding's loader hands it on.</summary>
    private static LoadedNav Level()
    {
        byte[] bytes = File.ReadAllBytes(JumpRecording.NavPath);
        Nav3dReader reader = Nav3dReader.Open(bytes);
        DerivedNav derived = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), TFNavProfile.ModNumbers, NavEdgeCosts.Default, []);
        return new LoadedNav(JumpRecording.Map, NavFile.Of(reader, "maps/" + JumpRecording.Map + ".nav3d"), derived, null!, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("bsp"u8)), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), false, 0d);
    }

    private sealed record Rig(TFJumps Jumps, Navigation Nav, Log Log)
    {
        /// <summary>The experience's slot the service reads its snapshots from, when a fact gave it one.</summary>
        public ExperienceSlot? Snapshots { get; init; }
    }

    private static Rig Service(bool script = true, ITFLevelEntities? placed = null, string? cache = null, string? skip = null, ExperienceSlot? snapshots = null)
    {
        var log = new Log();
        var nav = new Navigation { CacheDirectory = cache };
        return new Rig(new TFJumps(Sims(), new global::TF2.Shared.TFWeaponScripts(log, new LauncherScript(script)), Classes(), nav, log, placed,
                                   skip: DeclaredConVars.Holding<TFLinksSkipConVar>(skip), snapshots: snapshots), nav, log);
    }

    /// <summary>The service with the level's navigation set and its desk open.</summary>
    private static Rig Opened(ITFLevelEntities? placed = null, string? cache = null, LoadedNav? level = null, string? skip = null, ExperienceSlot? snapshots = null)
    {
        Rig rig = Service(placed: placed, cache: cache, skip: skip, snapshots: snapshots);
        rig.Nav.Current = level ?? Level();
        rig.Jumps.OnPreThink(TickSeconds);
        Assert.False(rig.Jumps.Desk.IsOpen, "the desk opened in the frame the navigation was set: its free space was read on the game's thread");
        Assert.True(rig.Jumps.Reading!.Wait(TimeSpan.FromSeconds(60)), "the navigation's free space was not read in a minute");
        rig.Jumps.OnPreThink(TickSeconds);
        Assert.True(rig.Jumps.Desk.IsOpen, string.Join("", rig.Log.Lines));
        return rig;
    }

    /// <summary>Waits for the worker's task to have answered <paramref name="answers"/> stretches.</summary>
    private static void Answered(TFJumps jumps, long answers)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (jumps.Book.Counts.Planned + jumps.Book.Counts.Refused < answers)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(120), $"the worker answered {jumps.Book.Counts.Planned + jumps.Book.Counts.Refused} of {answers} stretches in two minutes");
            Thread.Sleep(5);
        }
    }

    // The map's numbers (TFAiTestMaps): the run lane's middle, the low lane's, the floor, the blue flag by the run's east end, the red flag at the west of the low lane.
    private static readonly AiVector East = new(1f, 0f, 0f), West = new(-1f, 0f, 0f);

    private static JumpStretchAsk AlongTheRun(float from = 200f) => new(new AiVector(from, 232f, 16f), East, new AiVector(1736f, 232f, 24f), new Tick(100));

    private static JumpStretchAsk UnderTheSlab => new(new AiVector(1650f, 632f, 16f), West, new AiVector(128f, 720f, 24f), new Tick(100));

    [JumpMapFact]
    public void ANavigationSetOpensTheDeskOnceItsFreeSpaceHasBeenReadOffTheGamesThread()
    {
        Rig rig = Opened();
        Assert.Contains(rig.Log.Lines, l => l.Contains("TF2 rocket jumps: the simulator is up for " + JumpRecording.Map, StringComparison.Ordinal));
    }

    [JumpMapFact]
    public void ABrainsAskIsAnsweredByTheWorkersTaskAndTheRunLaneHasAJumpThatBeatsTheWalk()
    {
        Rig rig = Opened();
        JumpDeskCounts before = rig.Jumps.Desk.Counts;
        Assert.Null(rig.Jumps.Book.Advise(AlongTheRun()));
        Answered(rig.Jumps, 1);
        JumpPlan? plan = rig.Jumps.Book.Advise(AlongTheRun(from: 240f) with { At = new Tick(112) });
        Assert.True(plan is not null, $"the run lane has no jump: {string.Join(", ", Enum.GetValues<JumpRefusal>().Where(r => rig.Jumps.Book.Refused(r) > 0))}");
        Assert.True(plan!.Seconds <= plan.WalkSeconds * new JumpPlanSettings().PaysShare, $"{plan.Seconds} s against a walk of {plan.WalkSeconds} s");

        // What it cost the worker, written down for the tool and the plan (bin/<config>/ai-jump/plan.json).
        JumpDeskCounts after = rig.Jumps.Desk.Counts;
        string json = string.Create(CultureInfo.InvariantCulture,
            $"{{\n \"map\": \"{JumpRecording.Map}\",\n \"config\": \"{PerTreeNav.Config}\",\n \"flights\": {after.Flights - before.Flights},\n \"floods\": {after.Floods - before.Floods},\n"
            + $" \"worker_cpu_ms\": {(after.WorkMicros - before.WorkMicros) / 1000d:F1},\n \"jump_units\": {plan.Recipe.Landing.X - plan.Recipe.Takeoff.X:F0},\n \"seconds\": {plan.Seconds:F3},\n"
            + $" \"air_seconds\": {plan.AirSeconds:F3},\n \"walk_seconds\": {plan.WalkSeconds:F3},\n \"health\": {plan.Health:F1},\n \"robustness\": {plan.Robustness:F2},\n \"apex\": {plan.Apex:F1},\n"
            + $" \"pitch\": {plan.Recipe.Pitch:F1},\n \"yaw_offset\": {plan.Recipe.YawOffset:F1},\n \"shot_delay\": {plan.Recipe.ShotDelay},\n \"slide\": {plan.Slide:F0},\n \"hold_run\": {plan.HoldRun:F0},\n \"hold_seconds\": {plan.HoldSeconds:F3}\n}}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(BlastJumpDifferentialTests.ReportPath("plan.json"))!);
        File.WriteAllText(BlastJumpDifferentialTests.ReportPath("plan.json"), json);
        Assert.InRange(after.Flights - before.Flights, 1, new JumpSearchSettings().MaxFlights + 40);
    }

    [JumpMapFact]
    public void UnderTheLowLanesSlabThereIsNoJump()
    {
        Rig rig = Opened();
        Assert.Null(rig.Jumps.Book.Advise(UnderTheSlab));
        Answered(rig.Jumps, 1);
        Assert.Equal((0L, 1L), (rig.Jumps.Book.Counts.Planned, rig.Jumps.Book.Counts.Refused));
        Assert.Null(rig.Jumps.Book.Advise(UnderTheSlab with { Origin = new AiVector(1610f, 632f, 16f), At = new Tick(112) }));
    }

    [JumpMapFact]
    public void WhenTheNavigationGoesTheDeskClosesAndItsJumpsAreForgotten()
    {
        Rig rig = Opened();
        rig.Jumps.Book.Advise(AlongTheRun());
        Answered(rig.Jumps, 1);
        Assert.Equal(1, rig.Jumps.Book.Counts.Plans);
        rig.Nav.Current = null;
        rig.Jumps.OnPreThink(TickSeconds);
        Assert.False(rig.Jumps.Desk.IsOpen);
        Assert.Equal(0, rig.Jumps.Book.Counts.Plans);
    }

    [JumpMapFact]
    public void WithNoRocketLauncherScriptTheJumpsAreOffAndSaySo()
    {
        Rig rig = Service(script: false);
        rig.Nav.Current = Level();
        rig.Jumps.OnPreThink(TickSeconds);
        Assert.True(rig.Jumps.Reading!.Wait(TimeSpan.FromSeconds(60)));
        rig.Jumps.OnPreThink(TickSeconds);
        Assert.False(rig.Jumps.Desk.IsOpen);
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("warning: ", StringComparison.Ordinal) && l.Contains("no rocket launcher script", StringComparison.Ordinal));
    }

    /// <summary>Waits for the links' work to have ended, however it ended.</summary>
    private static void LinksDone(TFJumps jumps)
    {
        Task work = jumps.LinkWork ?? throw new Xunit.Sdk.XunitException("no links' work was started");
        Assert.True(((IAsyncResult)work).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(300)), "the links' work did not end in five minutes");
    }

    [JumpMapFact]
    public void TheLevelsBlastLinksAreFoundOnAWorkerAndPublished()
    {
        Rig rig = Opened();
        // The pass flies for a second or more: the frame that opened the desk came back with it under way, the slot empty.
        Assert.False(rig.Jumps.LinkWork!.IsCompleted, "the links were found in the frame that opened the desk");
        Assert.Null(rig.Jumps.LinkSlot.Links);
        LinksDone(rig.Jumps);
        NavLinks links = rig.Jumps.LinkSlot.Links ?? throw new Xunit.Sdk.XunitException($"no links: {rig.Jumps.Json()}");
        Assert.True(links.Count > 0 && links.IsOver(rig.Nav.Current!.Nav.Graph), $"{links.Count} links");
        Assert.Same(links, rig.Jumps.LinkSet!.Links);
        Assert.False(rig.Jumps.LinkOutcome!.FromCache);
        using var json = System.Text.Json.JsonDocument.Parse(rig.Jumps.Json());
        Assert.Equal((links.Count, "found", false), (json.RootElement.GetProperty("links").GetInt32(), json.RootElement.GetProperty("links.state").GetString(),
                                                      json.RootElement.GetProperty("links.from_cache").GetBoolean()));
        Assert.True(json.RootElement.GetProperty("links.flights").GetInt32() > 0);
        // Lane G5c-2b: published for the Soldiers' searches, and each link's plan in the book, flown once from its start.
        Assert.Same(rig.Jumps.LinkSet, rig.Jumps.Published.Set);
        JumpPlan[] plans = rig.Jumps.Book.LinkPlans();
        Assert.Equal(links.Count, plans.Length);
        for (int i = 0; i < plans.Length; i++)
        {
            Assert.Same(plans[i], rig.Jumps.Book.Plan(NavLinks.Argument(i)));
            Assert.True(plans[i].StartsStanding && plans[i].Apex > 0f && plans[i].AirSeconds > 0f, $"link {i}'s plan: apex {plans[i].Apex}, air {plans[i].AirSeconds}");
            Assert.Equal(rig.Jumps.LinkSet!.JumpOf(i).Recipe, plans[i].Recipe);
        }

        Assert.Equal(links.Count, json.RootElement.GetProperty("links.list").GetArrayLength());
    }

    /// <summary>What a follower said, as the main thread hands it over, is what the tree then reads from the book.</summary>
    [Fact]
    public void AFollowersReportGoesToTheBook()
    {
        Rig rig = Service();
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        JumpPlan plan = SourceSharp.Ai.Tests.TF2.TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true };
        rig.Jumps.Book.SetLinks(links, [plan]);
        var goal = new AiVector(10f, 20f, 0f);
        ((SourceSharp.Ai.Server.INavLinkReports)rig.Jumps).Note(new ActorId(4, 1), new Tick(7), new NavLinkStep(0, links.Hash, goal, true));
        Assert.Same(plan, rig.Jumps.Book.RouteLink(4, new Tick(8), goal));
    }

    [JumpMapFact]
    public void ASecondLoadOfTheLevelReadsItsLinksFromTheCacheBesideTheGraph()
    {
        string dir = Path.Combine(Path.GetTempPath(), "g5c2-tfjumps-" + Guid.NewGuid().ToString("N"));
        try
        {
            LoadedNav level = Level();
            Rig first = Opened(cache: dir, level: level);
            LinksDone(first.Jumps);
            Assert.False(first.Jumps.LinkOutcome!.FromCache);
            string file = Assert.Single(Directory.GetFiles(dir));
            Assert.EndsWith(".navgraph." + new JumpLinkCache().Suffix, file, StringComparison.Ordinal);

            Rig second = Opened(cache: dir, level: level);
            LinksDone(second.Jumps);
            Assert.True(second.Jumps.LinkOutcome!.FromCache, second.Jumps.Json());
            Assert.Equal(first.Jumps.LinkSet!.Links.Hash, second.Jumps.LinkSet!.Links.Hash);
            using var json = System.Text.Json.JsonDocument.Parse(second.Jumps.Json());
            Assert.Equal(("read from the cache", true), (json.RootElement.GetProperty("links.state").GetString(), json.RootElement.GetProperty("links.from_cache").GetBoolean()));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The 250 u ledge's top (TFAiTestMaps.LedgeTop(1)), and <c>ai_tf_links_skip</c> naming it (lane G6b).</summary>
    private static readonly AiVector Ledge250 = new(1028f, 1150f, 266f);

    private const string SkipLedge250 = "1028 1150 266 200";

    /// <summary>The links of <paramref name="set"/> that land on the 250 u ledge's top.</summary>
    private static int OntoLedge250(JumpLinkSet set)
        => set.Jumps.Count(j => MathF.Abs(j.Recipe.Landing.Z - Ledge250.Z) <= 24f && MathF.Abs(j.Recipe.Landing.X - Ledge250.X) <= 160f
                                && MathF.Abs(j.Recipe.Landing.Y - Ledge250.Y) <= 160f);

    [JumpMapFact]
    public void APlaceThePassIsToldToSkipGetsNoLink()
    {
        Rig plain = Opened();
        LinksDone(plain.Jumps);
        Assert.True(OntoLedge250(plain.Jumps.LinkSet!) > 0, $"the control: no link onto the 250 u ledge in {plain.Jumps.Json()}");

        Rig told = Opened(skip: SkipLedge250);
        LinksDone(told.Jumps);
        Assert.Equal(0, OntoLedge250(told.Jumps.LinkSet!));
        Assert.True(told.Jumps.LinkSet!.Links.Count > 0, "the rest of the level keeps its links");
        using var json = System.Text.Json.JsonDocument.Parse(told.Jumps.Json());
        Assert.Equal("1028 1150 266 200", json.RootElement.GetProperty("links.skip").GetString());
    }

    [JumpMapFact]
    public void ASkippedPassIsCachedUnderItsOwnKey()
    {
        string dir = Path.Combine(Path.GetTempPath(), "g6b-tfjumps-" + Guid.NewGuid().ToString("N"));
        try
        {
            LoadedNav level = Level();
            Rig first = Opened(cache: dir, level: level);
            LinksDone(first.Jumps);
            Assert.False(first.Jumps.LinkOutcome!.FromCache);

            // The file of a pass with no skip does not answer for one told to skip: the pass runs, and its file replaces it.
            Rig told = Opened(cache: dir, level: level, skip: SkipLedge250);
            LinksDone(told.Jumps);
            Assert.False(told.Jumps.LinkOutcome!.FromCache, told.Jumps.Json());
            Assert.Equal(0, OntoLedge250(told.Jumps.LinkSet!));

            Rig again = Opened(cache: dir, level: level, skip: SkipLedge250);
            LinksDone(again.Jumps);
            Assert.True(again.Jumps.LinkOutcome!.FromCache, again.Jumps.Json());
            Assert.Equal(told.Jumps.LinkSet!.Links.Hash, again.Jumps.LinkSet!.Links.Hash);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A snapshot of the jump map holding one Soldier's rocket jump onto the 250 u ledge from the ground east of it, from
    /// standing, the shot looking down and back the way it went two commands after the jump, seen <paramref name="seen"/>
    /// times (lane G6b), and a cell row besides; of <paramref name="map"/>.
    /// </summary>
    private static MapExperience ShownOnto250(double seen = 1d, string map = JumpRecording.Map, bool jump = true)
    {
        var takeoff = new AiVector(1330f, 1150f, 16f);
        var landing = new AiVector(1110f, 1150f, 266f);
        var trace = System.Collections.Immutable.ImmutableArray.CreateBuilder<ShownJumpCommand>(4);
        for (int i = 0; i < 4; i++)
        {
            byte buttons = (byte)(ShownJumpCommand.Duck | (i == 0 ? ShownJumpCommand.Jump : 0) | (i == 2 ? ShownJumpCommand.Attack : 0));
            trace.Add(new ShownJumpCommand(buttons, 65f, 0f, 0f, 0f));
        }

        var shown = new ShownJumpRecipe(ShownJumpKind.Blast, ShownJumpFlags.JumpPressed | ShownJumpFlags.OwnBlast | ShownJumpFlags.ShotSeen, takeoff, landing,
                                        default, default, default, default, 105, 40, 0, 0, 2, 3, trace.MoveToImmutable());
        var rows = new ExperienceDelta();
        rows.Add(new CellKey(new ExperienceContext(0, 2, (int)TFClass.Soldier), WorldBin.Of(takeoff), ExperienceSource.Human), new CellStat(10d, 0d, 0d, 0d, 0d));
        if (jump)
        {
            rows.Add(shown.CountKey((int)TFClass.Soldier), new LinkStat(seen, seen, seen * 1.6d, seen * 40d));
            rows.Add(shown.ExemplarKey((int)TFClass.Soldier), default);
        }

        return new MapExperience(new ExperienceKey(map, "0123456789abcdef", "ctf"), 3, rows);
    }

    /// <summary>The level opened with <paramref name="snapshot"/> in its experience, its shape links found, and a frame for the learn pass to start in.</summary>
    private static Rig Learning(MapExperience snapshot, string? skip = SkipLedge250)
    {
        var slot = new ExperienceSlot { Current = snapshot };
        Rig rig = Opened(skip: skip, snapshots: slot);
        LinksDone(rig.Jumps);
        Assert.NotNull(rig.Jumps.LinkSet);
        rig.Jumps.OnPreThink(TickSeconds);
        return rig with { Snapshots = slot };
    }

    /// <summary>Waits for the learn pass to have ended.</summary>
    private static void LearnDone(TFJumps jumps)
    {
        Task work = jumps.LearnWork ?? throw new Xunit.Sdk.XunitException($"no learn pass was started: {jumps.Json()}");
        Assert.True(((IAsyncResult)work).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(300)), "the learn pass did not end in five minutes");
    }

    [JumpMapFact]
    public void AJumpAPlayerShowedWhereThePassWasToldToSkipJoinsTheLinks()
    {
        Rig rig = Learning(ShownOnto250());
        JumpLinkSet shape = rig.Jumps.LinkOutcome!.Set;
        Assert.Equal(0, OntoLedge250(shape));
        LearnDone(rig.Jumps);
        JumpLinkSet set = rig.Jumps.LinkSet!;
        Assert.Equal(shape.Links.Count + 1, set.Links.Count);
        Assert.Equal(shape.Jumps, set.Jumps.Take(shape.Jumps.Count));
        JumpLink learned = set.JumpOf(set.Links.Count - 1);
        Assert.True(learned.Shown && learned.Robustness >= new JumpSearchSettings().Keep, $"{learned}");
        Assert.Equal(1, OntoLedge250(set));

        // Published as the shape pass's are: the slot, the Soldiers' searches and the book, its plan flown from its start.
        Assert.Same(set.Links, rig.Jumps.LinkSlot.Links);
        Assert.Same(set, rig.Jumps.Published.Set);
        JumpPlan plan = rig.Jumps.Book.LinkPlans()[set.Links.Count - 1];
        Assert.Equal(learned.Recipe, plan.Recipe);
        Assert.True(plan.StartsStanding && plan.Apex > 0f);
        using var json = System.Text.Json.JsonDocument.Parse(rig.Jumps.Json());
        Assert.Equal((1, 1, 1), (json.RootElement.GetProperty("links.learned").GetInt32(), json.RootElement.GetProperty("links.learn.fitted").GetInt32(),
                                 json.RootElement.GetProperty("links.learn.publishes").GetInt32()));
        Assert.Equal("shown", json.RootElement.GetProperty("links.list")[set.Links.Count - 1].GetProperty("origin").GetString());
        Assert.Equal("shape", json.RootElement.GetProperty("links.list")[0].GetProperty("origin").GetString());
    }

    [JumpMapFact]
    public void ASnapshotWithNothingNewPublishesNothing()
    {
        Rig rig = Learning(ShownOnto250());
        LearnDone(rig.Jumps);
        JumpLinkSet grown = rig.Jumps.LinkSet!;
        Assert.NotSame(rig.Jumps.LinkOutcome!.Set, grown);

        // The next round's snapshot: the same jump, seen again. Read, and searched no more.
        rig.Snapshots!.Current = ShownOnto250(seen: 2d);
        rig.Jumps.OnPreThink(TickSeconds);
        LearnDone(rig.Jumps);
        Assert.Same(grown, rig.Jumps.LinkSet);
        Assert.Null(rig.Jumps.LearnOutcome!.Set);
        Assert.Equal(0, rig.Jumps.LearnOutcome.Flights);
        using var json = System.Text.Json.JsonDocument.Parse(rig.Jumps.Json());
        Assert.Equal((2, 1), (json.RootElement.GetProperty("links.learn.passes").GetInt32(), json.RootElement.GetProperty("links.learn.publishes").GetInt32()));
    }

    [JumpMapFact]
    public void ALearnedLinkWhoseJumpFadesLeavesTheShapePasssOwnLinks()
    {
        Rig rig = Learning(ShownOnto250());
        LearnDone(rig.Jumps);
        Assert.NotSame(rig.Jumps.LinkOutcome!.Set, rig.Jumps.LinkSet);

        // Rounds later: seen under the floor, and no bot has landed its link.
        rig.Snapshots!.Current = ShownOnto250(seen: new ShownJumpFitSettings().MinSightings * 0.5d);
        rig.Jumps.OnPreThink(TickSeconds);
        LearnDone(rig.Jumps);
        Assert.Equal(ShownFitVerdict.Few, Assert.Single(rig.Jumps.LearnOutcome!.Fits.Fits).Verdict);
        Assert.Same(rig.Jumps.LinkOutcome.Set, rig.Jumps.LinkSet);
        Assert.Same(rig.Jumps.LinkOutcome.Set, rig.Jumps.Published.Set);
        Assert.Equal(rig.Jumps.LinkOutcome.Set.Links.Count, rig.Jumps.Book.LinkPlans().Length);
    }

    [JumpMapFact]
    public void WithNoJumpShownTheLinksAreTheShapePasssOwn()
    {
        Rig rig = Learning(ShownOnto250(jump: false));
        LearnDone(rig.Jumps);
        Assert.Same(rig.Jumps.LinkOutcome!.Set, rig.Jumps.LinkSet);
        Assert.Same(rig.Jumps.LinkSet, rig.Jumps.Published.Set);
        Assert.Equal(0, OntoLedge250(rig.Jumps.LinkSet!));
    }

    [JumpMapFact]
    public void AJumpTheShapePassAlsoFoundGivesOneLinkNotTwo()
    {
        Rig rig = Learning(ShownOnto250(), skip: null);
        LearnDone(rig.Jumps);
        Assert.True(OntoLedge250(rig.Jumps.LinkOutcome!.Set) > 0, "the control: the shape pass has a link onto the ledge");
        Assert.Same(rig.Jumps.LinkOutcome.Set, rig.Jumps.LinkSet);
        Assert.Equal(ShownFitVerdict.Known, Assert.Single(rig.Jumps.LearnOutcome!.Fits.Fits).Verdict);
    }

    [JumpMapFact]
    public void AnEmptySnapshotOrAnotherMapsIsNotRead()
    {
        Rig empty = Learning(MapExperience.Empty);
        Assert.Null(empty.Jumps.LearnWork);
        Rig other = Learning(ShownOnto250(map: "ctf_2fort"));
        Assert.Null(other.Jumps.LearnWork);
        Assert.Same(other.Jumps.LinkOutcome!.Set, other.Jumps.LinkSet);
    }

    [JumpMapFact]
    public void ANavigationChangeCancelsThePassAndEmptiesTheSlot()
    {
        Rig rig = Opened();
        NavLinkSlot before = rig.Jumps.LinkSlot;
        Task work = rig.Jumps.LinkWork!;
        Assert.False(work.IsCompleted, "the control: the pass is under way");
        rig.Nav.Current = null;
        rig.Jumps.OnPreThink(TickSeconds);
        Assert.NotSame(before, rig.Jumps.LinkSlot);
        LinksDone(rig.Jumps);
        Assert.Null(before.Links);
        Assert.Null(rig.Jumps.LinkSlot.Links);
        Assert.Null(rig.Jumps.LinkSet);
        using var json = System.Text.Json.JsonDocument.Parse(rig.Jumps.Json());
        Assert.Equal((0, "no navigation"), (json.RootElement.GetProperty("links").GetInt32(), json.RootElement.GetProperty("links.state").GetString()));
    }

    [JumpMapFact]
    public void TheLevelsTeamSpawnsSeedThePass()
    {
        // Two spawns on the jump map's floor, and an entity of another class there that is none.
        Rig rig = Opened(new Placed(new MapEntity("info_player_teamspawn", null, new AiVector(200f, 232f, 16f), null, null, Team: 2),
                                    new MapEntity("info_player_teamspawn", null, new AiVector(1700f, 232f, 16f), null, null, Team: 3),
                                    new MapEntity("item_teamflag", null, new AiVector(128f, 720f, 16f), null, null)));
        LinksDone(rig.Jumps);
        using var json = System.Text.Json.JsonDocument.Parse(rig.Jumps.Json());
        Assert.Equal(2, json.RootElement.GetProperty("links.seeds").GetInt32());
        Assert.True(json.RootElement.GetProperty("links").GetInt32() > 0, rig.Jumps.Json());
    }

    [Fact]
    public void TheBindingHandsItsTreesTheBookAndTheApplierTheProgram()
    {
        Rig rig = Service();
        var game = new TFBotGame(WeaponCatalog.Stock(), null!, Classes(), null!, null!, TF2.TFFeelSwitchTests.Switches(), rig.Jumps);
        Assert.IsType<RocketJumpProgram>(game.Programs?.Named(TFAbilities.RocketJump));
        Assert.Null(game.Programs?.Named(TFAbilities.SpinUp));
        var field = new TF2.TFField();
        var decider = Assert.IsType<TFDecider>(game.Decider(new BrainSetup(field.Bot, 7, field.Memory, field.Params, TickSeconds)));
        Assert.Same(rig.Jumps.Book, decider.Jumps);
        // A binding composed without the service has no program and its trees no book.
        var bare = new TFBotGame(WeaponCatalog.Stock(), null!, Classes(), null!, null!, TF2.TFFeelSwitchTests.Switches());
        Assert.Null(bare.Programs);
        Assert.Null(Assert.IsType<TFDecider>(bare.Decider(new BrainSetup(field.Bot, 7, field.Memory, field.Params, TickSeconds))).Jumps);
    }

    /// <summary>
    /// The tf2 mod's server list as mods.yaml has it (the mod, the generic binding, TF2's): no other frame member
    /// has the service's frame order and no other route provider its order. Two at one order stop the server at
    /// load (StageCollisionException: live, the first boot with the service, which shared order 0 with TFAutobalance).
    /// </summary>
    [Fact]
    public void TheServiceSharesItsFrameOrderAndItsRouteOrderWithNoOtherOfTheTF2List()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.Server.AiBots).Assembly.GetTypes(), .. typeof(TFJumps).Assembly.GetTypes()]);
        Type[] concrete = [.. registered.Where(t => t.IsClass && !t.IsAbstract).Distinct()];
        Assert.Contains(typeof(TFJumps), concrete);
        static object Bare(Type t) => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        string[] frames = [.. concrete.Where(t => typeof(IFrameLifecycle).IsAssignableFrom(t))
                                      .Where(t => ((IFrameLifecycle)Bare(t)).FrameOrder == ((IFrameLifecycle)Bare(typeof(TFJumps))).FrameOrder)
                                      .Select(t => t.Name)];
        Assert.Equal([nameof(TFJumps)], frames);
        string[] routes = [.. concrete.Where(t => typeof(global::Debug.Api.Server.Interfaces.IDebugRouteProvider).IsAssignableFrom(t))
                                      .Where(t => (Bare(t) as IOrderedHook)?.HookOrder == ((IOrderedHook)Bare(typeof(TFJumps))).HookOrder)
                                      .Select(t => t.Name)];
        Assert.Equal([nameof(TFJumps)], routes);
        string[] lifecycles = [.. concrete.Where(t => typeof(IGameLifecycle).IsAssignableFrom(t))
                                          .Where(t => ((IGameLifecycle)Bare(t)).LifecycleOrder == ((IGameLifecycle)Bare(typeof(TFJumps))).LifecycleOrder)
                                          .Select(t => t.Name)];
        Assert.Equal([nameof(TFJumps)], lifecycles);
        // The control: the list has other frame members and another route provider to clash with.
        Assert.True(concrete.Count(t => typeof(IFrameLifecycle).IsAssignableFrom(t)) > 5);
        Assert.Contains(concrete, t => t != typeof(TFJumps) && typeof(global::Debug.Api.Server.Interfaces.IDebugRouteProvider).IsAssignableFrom(t));
    }

    /// <summary>
    /// The main thread hands the followers' link steps to whatever the container has as the link reports
    /// (AiBots, lane G5c-2b): in the tf2 mod's list that is the jumps. Live, without it the reports reached nobody and
    /// no bot began a link.
    /// </summary>
    [Fact]
    public void TheTF2ListHasTheJumpsAsTheLinkReports()
    {
        var services = new ServiceContainer();
        SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.Server.AiBots).Assembly.GetTypes(), .. typeof(TFJumps).Assembly.GetTypes()]);
        Assert.True(services.IsRegistered(typeof(SourceSharp.Ai.Server.INavLinkReports)), "nothing is registered as the link reports");
        Assert.Contains(typeof(SourceSharp.Ai.Server.INavLinkReports), typeof(TFJumps).GetCustomAttributes(typeof(SideServiceAttribute), false).Cast<SideServiceAttribute>().SelectMany(a => a.As));
    }

    [Fact]
    public void TheCountersRouteListsTheJumpsTheBookAndTheWorker()
    {
        Rig rig = Service();
        Assert.Equal(TFJumps.Route, Assert.Single(rig.Jumps.Routes).Path);
        using var json = System.Text.Json.JsonDocument.Parse(rig.Jumps.Handle(TFJumps.Route, new Dictionary<string, string>()).Json);
        System.Text.Json.JsonElement root = json.RootElement;
        foreach (string counter in (string[])["ai.jump.begun", "ai.jump.ended", "ai.jump.landed.near", "ai.jump.landed.short", "ai.jump.landed.elsewhere", "ai.jump.called_off.view",
                                              "ai.jump.called_off.astray", "ai.jump.fizzled", "ai.jump.died", "ai.jump.timed_out", "ai.jump.book.asked", "ai.jump.book.planned",
                                              "ai.jump.refused.ceiling", "ai.jump.refused.notfaster", "ai.jump.work.flights", "ai.jump.work.cpu.us"])
            Assert.Equal(0, root.GetProperty(counter).GetInt64());
        Assert.Equal(("no navigation", 0, 0), (root.GetProperty("state").GetString(), root.GetProperty("last").GetArrayLength(), root.GetProperty("plans").GetArrayLength()));
    }
}
