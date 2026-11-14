using System.Globalization;
using System.Text;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// The rocket jump's presence in a level (plan_bots_teams_tf2_part_g G.5 steps 2, 6 and 7): the
/// level's book of jumps, which the brains read; the program the applier runs for the ability
/// (<see cref="RocketJumpProgram"/>); the tally of what each jump did; and the planner behind them.
///
/// <para>
/// <b>Where a recipe is found.</b> Never in a decide and never on the game's thread. When a
/// navigation is set, its free space is read on a task (<see cref="VoxelColumns.FromNav"/>) and,
/// the frame after that is done, the level's simulators and planner are made here on the game's
/// thread (the class table and the launcher's script are read once) and handed to the desk. From
/// then on a stretch a brain asked about, or a jump a bot made, cues one task that does the desk's
/// work (<see cref="JumpDesk.Work"/>: the route, the search of at most 160 flights, the flight
/// again); the brain reads the answer at a later decide. One piece of work runs at a time.
/// </para>
///
/// <para>
/// The counters are served at <c>/ai/jumps</c> (the dev API; a route provider of the TF2 binding's
/// own, joined to the providers' chain by implementing the interface): the jumps begun and how each ended,
/// the book's and the worker's counts, and the last jumps in full with the simulator's flight of
/// each beside the game's.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFJumps : IFrameLifecycle, IDebugRouteProvider, IOrderedHook, IAbilityPrograms
{
    /// <summary>Its place among the dev API's route providers: after the binding's own (order 0) and the core's.</summary>
    public int HookOrder => ModStages.First + 920;

    /// <summary>After the navigation's own frame work (First + 400), which sets the level's nav, and before the bots' commands (First + 500).</summary>
    public int FrameOrder => ModStages.First + 420;

    /// <summary>The route the counters are served at.</summary>
    public const string Route = "/ai/jumps";

    /// <summary>How fast the planner's own pilot turns its view in the simulator, degrees a second: a middling hand's flick.</summary>
    public const float PlannerTurn = 360f;

    private readonly BlastJumpSims _sims;
    private readonly TFWeaponScripts _scripts;
    private readonly TFClassTable _classes;
    private readonly IAiNavigation _nav;
    private readonly ILog _log;
    private readonly JumpDesk _desk;
    private readonly RocketJumpProgram _program;
    private LoadedNav? _level;
    private Task<VoxelColumns>? _reading;
    private float _tick = BlastJumpSims.TickSeconds;
    private double _openMs;
    private string _state = "no navigation";

    public TFJumps(BlastJumpSims sims, TFWeaponScripts scripts, TFClassTable classes, IAiNavigation nav, ILog log)
    {
        _sims = sims;
        _scripts = scripts;
        _classes = classes;
        _nav = nav;
        _log = log;
        _desk = new JumpDesk(new JumpBook(new JumpBookSettings()), new JumpTally(), ThreadCpuClock.Micros()) { Cue = Cue };
        _program = new RocketJumpProgram(_desk.Book, classes, _desk.Flew);
    }

    /// <summary>The read of the navigation's free space under way, or null: a fact waits on it.</summary>
    internal Task? Reading => _reading;

    /// <summary>The level's jumps as the brains read them.</summary>
    public JumpBook Book => _desk.Book;

    /// <summary>The desk: the book, the tally and the worker's work.</summary>
    public JumpDesk Desk => _desk;

    public IAbilityProgram? Named(string ability) => ability == TFAbilities.RocketJump ? _program : null;

    /// <summary>There is work: one task does it. From any thread.</summary>
    private void Cue() => Task.Run(_desk.Work);

    /// <summary>Follows the level's navigation: another one (or none) closes the desk, and its simulator opens it when its free space has been read.</summary>
    public void OnPreThink(float frameTime)
    {
        if (frameTime > 0f)
            _tick = frameTime;
        LoadedNav? now = _nav.Current;
        if (!ReferenceEquals(now, _level))
        {
            _level = now;
            _desk.Close();
            _state = now is null ? "no navigation" : "reading the navigation's free space";
            // The reader is immutable; the columns are read off the game's thread and picked up below.
            _reading = now is null ? null : Task.Run(() => VoxelColumns.FromNav(now.File.Source));
        }

        if (_reading is { IsCompleted: true } read)
        {
            _reading = null;
            Open(_level!, read);
        }
    }

    private void Open(LoadedNav level, Task<VoxelColumns> read)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (read.IsFaulted || _scripts.GetQuiet(Trees.TFDecider.SoldierLauncher) is not { } script)
        {
            _state = read.IsFaulted ? $"no free space: {read.Exception?.GetBaseException().Message}" : "no rocket launcher script";
            _log.Warning($"[SourceSharp/Ai] TF2 rocket jumps are off on {level.Map}: {_state}\n");
            return;
        }

        VoxelColumns columns = read.Result;
        JumpLauncher launcher = JumpLauncher.Rocket(script);
        JumpPilotLimits limits = JumpPilotLimits.Of(TFViewVectors.Params, _classes[TFClass.Soldier].MaxSpeed, _tick, PlannerTurn);
        _desk.Open(JumpPlanner.Of(level.Nav.Graph, _sims.Create(columns, launcher, _tick), _sims.Create(JumpEnvelope.OpenAir(), launcher, _tick), limits,
                                  new JumpSearchSettings(), new JumpPlanSettings()));
        _openMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _state = "open";
        _log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] TF2 rocket jumps: the simulator is up for {level.Map} ({columns.Width} by {columns.Depth} columns of {columns.VoxelSize:F0}, tick {_tick:F4} s, {_openMs:F1} ms on the game's thread)\n"));
    }

    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", Route, "the TF2 bots' rocket jumps: ai.jump.begun and how each ended (landed near the plan's touch, short, elsewhere, called off, fizzled, died, timed out), the book (asked, planned, refused by reason), the worker (flights, floods, cpu), and the last jumps in full (how long each stood for its view, how high it rose) with the simulator's flight of each from the same launch"),
    ];

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query) => new(200, Json());

    /// <summary>The counters and the last jumps, as <see cref="Route"/> serves them.</summary>
    public string Json()
    {
        JumpTallyCounts t = _desk.Tally.Counts;
        JumpBookCounts b = _desk.Book.Counts;
        JumpDeskCounts d = _desk.Counts;
        var json = new StringBuilder(512);
        json.Append(CultureInfo.InvariantCulture,
            $"{{\"state\":{AiJson.Quote(_state)},\"map\":{AiJson.Quote(_level?.Map ?? "")},\"nav_sha256\":{AiJson.Quote(_level?.NavSha256 ?? "")},"
            + $"\"ai.jump.begun\":{_program.Begun},\"ai.jump.unknown\":{_program.Unknown},\"ai.jump.ended\":{t.Ended},"
            + $"\"ai.jump.landed.near\":{t.LandedNear},\"ai.jump.landed.short\":{t.LandedShort},\"ai.jump.landed.elsewhere\":{t.LandedElsewhere},"
            + $"\"ai.jump.called_off.view\":{t.ViewLate},\"ai.jump.called_off.astray\":{t.Astray},\"ai.jump.fizzled\":{t.Fizzled},\"ai.jump.fizzled.felt\":{t.FizzledFelt},\"ai.jump.died\":{t.Died},\"ai.jump.timed_out\":{t.TimedOut},"
            + $"\"ai.jump.predicted\":{t.Predicted},\"ai.jump.book.advised\":{b.Advised},\"ai.jump.book.asked\":{b.Asked},\"ai.jump.book.dropped\":{b.Dropped},"
            + $"\"ai.jump.book.planned\":{b.Planned},\"ai.jump.book.refused\":{b.Refused},\"ai.jump.book.plans\":{b.Plans},\"ai.jump.book.waiting\":{b.Waiting},");
        foreach (JumpRefusal why in Enum.GetValues<JumpRefusal>())
        {
            if (why != JumpRefusal.None)
                json.Append(CultureInfo.InvariantCulture, $"\"ai.jump.refused.{why.ToString().ToLowerInvariant()}\":{_desk.Book.Refused(why)},");
        }

        json.Append(CultureInfo.InvariantCulture,
            $"\"ai.jump.work.pieces\":{d.Planned + d.Predicted},\"ai.jump.work.flights\":{d.Flights},\"ai.jump.work.floods\":{d.Floods},\"ai.jump.work.cpu.us\":{d.WorkMicros:F0},"
            + $"\"ai.jump.work.faults\":{d.Faults},\"ai.jump.open.ms\":{_openMs:F1},\"near_units\":{JumpTally.NearUnits:F0},\"last\":{_desk.Tally.LastJson()},\"plans\":[");
        JumpPlan[] plans = _desk.Book.Plans();
        for (int i = 0; i < plans.Length; i++)
        {
            JumpPlan p = plans[i];
            json.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"id\":{p.Id},\"takeoff\":[{p.Recipe.Takeoff.X:F1},{p.Recipe.Takeoff.Y:F1},{p.Recipe.Takeoff.Z:F1}],\"landing\":[{p.Recipe.Landing.X:F1},{p.Recipe.Landing.Y:F1},{p.Recipe.Landing.Z:F1}],"
                + $"\"pitch\":{p.Recipe.Pitch:F1},\"yaw_offset\":{p.Recipe.YawOffset:F1},\"shot_delay\":{p.Recipe.ShotDelay},\"run_up\":{p.Recipe.RunUp:F0},\"slide\":{p.Slide:F0},\"hold_run\":{p.HoldRun:F0},\"hold_seconds\":{p.HoldSeconds:F3},"
                + $"\"seconds\":{p.Seconds:F3},\"air_seconds\":{p.AirSeconds:F3},\"walk_seconds\":{p.WalkSeconds:F3},\"health\":{p.Health:F1},\"robustness\":{p.Robustness:F2},\"apex\":{p.Apex:F1}}}");
        }

        return json.Append("]}").ToString();
    }
}
