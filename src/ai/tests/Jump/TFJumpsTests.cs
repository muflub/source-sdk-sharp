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
    }

    /// <summary>This tree's built nav of the jump map, loaded as the binding's loader hands it on.</summary>
    private static LoadedNav Level()
    {
        byte[] bytes = File.ReadAllBytes(JumpRecording.NavPath);
        Nav3dReader reader = Nav3dReader.Open(bytes);
        DerivedNav derived = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), TFNavProfile.ModNumbers, NavEdgeCosts.Default, []);
        return new LoadedNav(JumpRecording.Map, NavFile.Of(reader, "maps/" + JumpRecording.Map + ".nav3d"), derived, null!, "bsp", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), false, 0d);
    }

    private sealed record Rig(TFJumps Jumps, Navigation Nav, Log Log);

    private static Rig Service(bool script = true)
    {
        var log = new Log();
        var nav = new Navigation();
        return new Rig(new TFJumps(Sims(), new global::TF2.Shared.TFWeaponScripts(log, new LauncherScript(script)), Classes(), nav, log), nav, log);
    }

    /// <summary>The service with the level's navigation set and its desk open.</summary>
    private static Rig Opened()
    {
        Rig rig = Service();
        rig.Nav.Current = Level();
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
        // The control: the list has other frame members and another route provider to clash with.
        Assert.True(concrete.Count(t => typeof(IFrameLifecycle).IsAssignableFrom(t)) > 5);
        Assert.Contains(concrete, t => t != typeof(TFJumps) && typeof(global::Debug.Api.Server.Interfaces.IDebugRouteProvider).IsAssignableFrom(t));
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
