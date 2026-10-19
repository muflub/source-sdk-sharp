using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Seconds a mode switch waits before its round starts. Zero switches
/// straight away, which is what the game did before a switch existed; the
/// effective wait is this and <c>mp_waitingforplayers_time</c> together (the
/// plan's default), so a server that already waits for players gets the same
/// wait on a switch without this convar being named at all.
/// </summary>
[ConVar("sv_mode_switch_warmup", "0", Help = "Seconds a mode switch waits before starting the round, at least mp_waitingforplayers_time")]
public sealed class SvModeSwitchWarmupConVar : FloatConVar { }

/// <summary>
/// The mode proxy (ruling U1): the game's rules questions, asked of whichever
/// <see cref="IGameMode"/> is active, because a mod can neither replace
/// <see cref="GameRules"/> nor subclass it — it registers a mode, and this
/// service asks it.
///
/// <para>
/// <b>The modes come from every assembly.</b> <see cref="Registered"/> is
/// every <see cref="IGameMode"/> the container answers: a mod's
/// <c>[SideService(Side.Server)]</c> class, or one an
/// <see cref="ISideServiceRegistrar"/> registers (the scan claims each marked
/// class under its own type, so two mods' modes never collide on a contract,
/// and neither can register over the core's). The pick at level init goes
/// through the <see cref="IGameModeSelector"/> chain — first non-null answer
/// wins, the core's own <see cref="NullModeId"/> answers last — and an unknown
/// id is a log line and an unchanged game: a map that asks for a mode nobody
/// shipped must not break the round that is running.
/// </para>
///
/// <para>
/// <b>The switch (D6, ruled 2026-10-11):</b> flush — the core's per-round
/// bookkeeping, the previous and next machines' own round state, and every
/// live entity's <see cref="ScriptedEntity.RoundReset"/>, each ONE time;
/// keep every player (nothing here touches them); run the new mode's
/// <see cref="IModeSetup"/>; and wait out the warmup before the round may
/// win. No live state crosses from one mode to the next, and the flush is
/// exactly why the reset and the walk live here rather than in a concommand's
/// body: the order is the feature.
/// </para>
/// </summary>
public sealed class GameModes : IGameLifecycle, IFrameLifecycle
{
    /// <summary>The core's own answer, and <see cref="NullMode"/>'s id.</summary>
    public const string NullModeId = "null";

    private readonly ILog _log;
    private readonly IGameClock _clock;
    private readonly IServiceLocator _services;
    private readonly SvModeSwitchWarmupConVar? _switchWarmup;
    private readonly MpWaitingForPlayersTimeConVar? _waitingTime;

    // The wiring Attach does, not the constructor's: GameRules takes this
    // service — it is the proxy's busiest caller — and neither can be the
    // other's parameter without one of them being built before it exists.
    private GameRules? _core;
    private IGameMode? _null;

    // Whether the pick has been asked. Distinct from _active: an unknown id
    // is picked-but-nothing-found, and asking again would re-ask the chain,
    // which answers once a level, not once a caller.
    private bool _picked;
    private IGameMode? _active;
    private float? _warmupEndsAt;

    public GameModes(ILog log, IGameClock clock, IServiceLocator services, ConVarRegistry convars)
    {
        _log = log;
        _clock = clock;
        _services = services;
        _switchWarmup = convars.Get<SvModeSwitchWarmupConVar>();
        _waitingTime = convars.Get<MpWaitingForPlayersTimeConVar>();
    }

    /// <summary>
    /// The wiring the module does once, after the rules exist: the core's own
    /// machine, and the default mode that is that machine.
    /// </summary>
    internal void Attach(GameRules core)
    {
        _core = core;
        _null = new NullMode(core);
    }

    /// <summary>
    /// Every mode any assembly registered, plus <see cref="NullMode"/> as the
    /// core's own answer — last, so a mod that registers its own <c>"null"</c>
    /// wins the lookup: mods extend the core, and this list is what they
    /// extend.
    /// </summary>
    public IReadOnlyList<IGameMode> Registered
    {
        get
        {
            var found = _services is ServiceContainer container
                ? new List<IGameMode>(container.ResolveAll<IGameMode>())
                : [];

            if (_null is { } nullMode && !found.Any(m => ReferenceEquals(m, nullMode)))
                found.Add(nullMode);

            return found;
        }
    }

    /// <summary>
    /// The mode the rules questions go to, picked the first time anything
    /// asks — so a harness that drives the rules without a level gets the
    /// chain's answer too. Null is the unknown-id arm: the chain named a mode
    /// nobody registered, this logged it, and every caller answers with its
    /// own stock rule, which is the game as it was before any mode existed.
    /// </summary>
    public IGameMode? Active
    {
        get
        {
            if (!_picked)
                Pick("(no level)");
            return _active;
        }
    }

    /// <summary>
    /// Whether a switch's warmup is running and the round has therefore not
    /// started. <see cref="GameRules.Think"/> is gated on this: a frag traded
    /// while the players are still waiting must not end a round that has not
    /// begun.
    /// </summary>
    public bool IsWarmingUp => _warmupEndsAt is not null;

    /// <summary>The id the last pick asked the chain for, and <see cref="SwitchTo"/> last named.</summary>
    public string? AskedId { get; private set; }

    /// <summary>The core's own machine, for the proxies' reference guards.</summary>
    internal GameRules? Core => _core;

    //-------------------------------------------------------------------------
    // The pick (LifecycleStage.GameModes = 75, BEFORE Rules = 100: the rules'
    // own level-init reset below it asks a picked mode, not a null field).
    //-------------------------------------------------------------------------

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.GameModes;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        // A switch's warmup ends with its map. Its end time is on the old
        // map's clock, and the new map's clock restarts near zero: carried
        // over, it would hold the win check (frag and time limits off) for
        // most of the new map. A fresh level is a fresh round.
        _warmupEndsAt = null;
        Pick(mapName);
    }

    private void Pick(string mapName)
    {
        _picked = true;

        string? asked = null;
        if (_services.Resolve<IHooks<IGameModeSelector>>() is { } chain)
        {
            foreach (var selector in chain.All)
            {
                try
                {
                    if (selector.PickMode() is { Length: > 0 } picked)
                    {
                        asked = picked;
                        break;
                    }
                }
                catch (Exception e)
                {
                    // A broken selector is no opinion, not no mode: no mode
                    // is no round machine, and the game would not start.
                    _log.Warning($"[SourceSharp/Modes] mode selector {selector.GetType().FullName} threw: {e}\n");
                }
            }
        }

        AskedId = asked ??= NullModeId;   // the core's own answer, last
        _active = Find(asked);

        if (_active is null)
        {
            _log.Warning($"[SourceSharp/Modes] {mapName}: no mode is registered as '{asked}' " +
                         $"({string.Join(", ", Registered.Select(m => m.Id))} are); " +
                         "the core's own rules run\n");
            return;
        }

        _log.Msg($"[SourceSharp/Modes] {mapName}: mode '{_active.Id}'\n");

        // A fresh level is a fresh round for the picked machine. The CORE's
        // reset stays GameRules' own line (LifecycleStage.Rules runs it just
        // after this); only a mode's own machine is this class's to clear, so
        // a game with no mode-owner runs the level init it always ran.
        if (!ReferenceEquals(_active.Rounds, _core))
            _active.Rounds.ResetRound();

        // The mode's own score totals — the IScoring half the machine's
        // ResetRound does not cover (tf2 stage 0.7 part C). NullMode's
        // scoring is null (deathmatch's scores ARE the core's scoreboard,
        // whose reset is the line above), so the no-owner arm runs nothing
        // here, as it did before scoring existed.
        ResetScoring(_active.Scoring, new HashSet<IScoring>(ReferenceEqualityComparer.Instance));

        Setup(_active);
    }

    /// <summary>
    /// Clears one mode's score totals, once per object PER FLUSH: two modes
    /// registered the same team table share its zeroing, and a later switch
    /// must zero it again — so the "once" set belongs to the flush, not to
    /// this class (the machine walk above applies the same rule by
    /// reference, and a field would make the second switch skip the reset).
    /// A scoring object that throws is named and the round still starts
    /// (the rule <see cref="Setup"/> applies to a mode that throws).
    /// </summary>
    private void ResetScoring(IScoring? scoring, HashSet<IScoring> done)
    {
        if (scoring is null || !done.Add(scoring))
            return;

        try
        {
            scoring.ResetRound();
        }
        catch (Exception e)
        {
            _log.Warning($"[SourceSharp/Modes] scoring {scoring.GetType().FullName} reset threw: {e}\n");
        }
    }

    private IGameMode? Find(string id)
        => Registered.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The startup check the module runs right after the mods' scan: two
    /// registered modes answering one id (compared as <see cref="Find"/>
    /// compares, ignoring case) are refused, naming both types — the lookup
    /// takes the first, so the second would be silently unreachable by
    /// <c>sv_mode</c>, the selector chain and <c>game_mode_switch</c> alike.
    /// The core's own <see cref="NullMode"/> is not a registration: a mod's
    /// <c>"null"</c> overriding it is the documented extension, not a clash.
    /// Returns the registered ids, for the module's log line.
    /// </summary>
    public IReadOnlyList<string> VerifyRegistry()
    {
        var registered = _services is ServiceContainer container
            ? container.ResolveAll<IGameMode>()
            : [];

        var byId = new Dictionary<string, IGameMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in registered)
        {
            if (byId.TryGetValue(mode.Id, out var first))
            {
                throw new InvalidOperationException(
                    $"two game modes are registered as '{mode.Id}': {first.GetType().FullName} " +
                    $"({first.GetType().Assembly.GetName().Name}) and {mode.GetType().FullName} " +
                    $"({mode.GetType().Assembly.GetName().Name}). A mode id names ONE mode — the " +
                    "lookup would take the first and the second could never be switched to.");
            }

            byId[mode.Id] = mode;
        }

        return [.. byId.Keys];
    }

    private void Setup(IGameMode mode)
    {
        // A mode's setup prepares its world; a broken one is logged and the
        // round still starts — the rule every other dispatch applies to a
        // participant that throws.
        try
        {
            mode.Setup?.SetupRound(_clock.Now);
        }
        catch (Exception e)
        {
            _log.Warning($"[SourceSharp/Modes] mode '{mode.Id}' setup threw: {e}\n");
        }
    }

    private float WarmupSeconds()
        => Math.Max(0f, Math.Max(_switchWarmup?.Value ?? 0f, _waitingTime?.Value ?? 0f));

    //-------------------------------------------------------------------------
    // The switch (D6).
    //-------------------------------------------------------------------------

    /// <summary>
    /// Switch to the named mode: flush, keep the players, set the new mode up,
    /// wait out the warmup, then let the round run. An unknown id is a log
    /// line and an unchanged game — including its round: nothing below runs
    /// before the mode is found. Returns whether the switch was taken.
    /// Switching to the mode already playing IS performed (two
    /// <c>sv_mode null</c> are two round restarts, which is what the live gate
    /// measures), not skipped.
    /// </summary>
    public bool SwitchTo(string id)
    {
        var next = Find(id);
        if (next is null)
        {
            _log.Warning($"[SourceSharp/Modes] switch to '{id}' refused: no such mode " +
                         $"({string.Join(", ", Registered.Select(m => m.Id))} are); " +
                         $"still '{_active?.Id ?? "the core's own rules"}'\n");
            return false;
        }

        float now = _clock.Now;

        // KEEP THE PLAYERS: nothing here touches the roster, the entities, or
        // the connections. A switch that reloaded the map would kick them;
        // D6 restarts the round, not the session.
        var previous = _active;
        _active = next;
        _picked = true;
        AskedId = next.Id;

        FlushRound(previous, next);

        _log.Msg($"[SourceSharp/Modes] switched to '{next.Id}'\n");
        Setup(next);

        float warmup = WarmupSeconds();
        _warmupEndsAt = warmup > 0f ? now + warmup : null;
        if (_warmupEndsAt is { } ends)
            _log.Msg($"[SourceSharp/Modes] warmup for {ends - now:F1}s before '{next.Id}' starts\n");

        return true;
    }

    /// <summary>
    /// The flush: the core's per-round bookkeeping, the machines' own round
    /// state, every live entity's in-place reset, and the modes' score totals
    /// — each ONE time, which for the machines means deduped by reference (the
    /// next mode's machine may be the previous one's, and <see cref="NullMode"/>'s
    /// IS the core's, so the plain call would be the core's reset twice over).
    /// </summary>
    private void FlushRound(IGameMode? previous, IGameMode next)
    {
        _core?.ResetRound();

        // Once each, by reference: NullMode's machine IS the core's (whose
        // reset above already ran), and the next mode's machine may be the
        // previous mode's — the plan's "SwitchTo flushes once" is what the
        // fact counts.
        var flushed = new HashSet<IRoundFlow>(ReferenceEqualityComparer.Instance);
        if (_core is { } coreFlow)
            flushed.Add(coreFlow);

        foreach (var flow in new[] { previous?.Rounds, next.Rounds })
        {
            if (flow is not null && flushed.Add(flow))
                flow.ResetRound();
        }

        // The live entities, once each, after the machines (a machine's reset
        // may re-arm what an entity's own reset reads) and before the new
        // mode's setup arranges the world. Counted out loud: a walk that
        // reaches nothing reads the same as no walk unless the number says so.
        int reset = ResetRoundEntities();
        _log.Msg($"[SourceSharp/Modes] round reset: {reset} entities\n");

        // The score totals of both modes, on ONE set: the same scoring object
        // named twice (two modes registered the same team table) is zeroed
        // once, before warmup, so a frag traded after the switch belongs to
        // the new mode's table alone.
        var scoringDone = new HashSet<IScoring>(ReferenceEqualityComparer.Instance);
        ResetScoring(previous?.Scoring, scoringDone);
        ResetScoring(next.Scoring, scoringDone);
    }

    /// <summary>
    /// The in-place half of stock's <c>CleanUpMap</c> (the divergence row):
    /// every spawned entity gets its <see cref="ScriptedEntity.RoundReset"/>.
    /// Stock removes the entities and reloads them from the map's entity
    /// strings; these are live objects with managed state no entity string
    /// ever held. Returns how many entities were walked (a throwing reset is
    /// named and still counted: it was asked).
    /// </summary>
    public int ResetRoundEntities()
    {
        // The concrete directory, which is where the snapshotting walk lives:
        // a RoundReset may remove entities, and the IEntityDirectory contract
        // has no walk-all.
        if (_services.Resolve<EntityDirectory>() is not { } live)
            return 0;

        int walked = 0;
        foreach (var entity in live.Snapshot())
        {
            walked++;
            try
            {
                entity.RoundReset();
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Modes] RoundReset on {entity.GetType().FullName} " +
                             $"({entity.Handle}) threw: {e}\n");
            }
        }

        return walked;
    }

    //-------------------------------------------------------------------------
    // The warmup (FrameStage.GameModes = 250, BEFORE Rules = 300: when the
    // wait ends the round starts, and the win check of a round that has not
    // started must not run in that frame).
    //-------------------------------------------------------------------------

    int IFrameLifecycle.FrameOrder => (int)FrameStage.GameModes;

    void IFrameLifecycle.OnThink(float frameTime)
    {
        if (_warmupEndsAt is not { } ends)
            return;

        if (_clock.Now < ends)
            return;

        _warmupEndsAt = null;
        if (_active is { } mode)
            _log.Msg($"[SourceSharp/Modes] warmup over; '{mode.Id}' round started\n");
    }

    /// <summary>Test seam: the id of the mode the proxy is answering for.</summary>
    public string? ActiveId => Active?.Id;
}
