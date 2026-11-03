using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>One bot the binding plays: its actor, its name and its skills.</summary>
public readonly record struct Hl2mpBot(ActorId Id, string Name, SkillProfile Skills);

/// <summary>
/// The HL2MP binding's bots (plan_hl2mp_ai §3.1, Phase 3 step 4): adds them as
/// engine clients through <see cref="IBotPlayers"/>, plays each through the
/// <see cref="BotCommandApplier"/> once per tick before the entities think,
/// and follows the core's view of who is still connected. No brain yet: a bot
/// holds, unless a scripted drive (<c>ai_bot_drive</c>, the nav-free live
/// check) gives it an intent.
/// </summary>
[SideService(Side.Server)]
public sealed class Hl2mpBots : IFrameLifecycle
{
    /// <summary>The match seed every bot's stream is drawn from (a convar later).</summary>
    public const ulong MatchSeed = 0x5EED_0001UL;

    private readonly IBotPlayers _players;
    private readonly IEngineFrame _frame;
    private readonly ILog _log;
    private readonly IBotSelf _self;
    private readonly BotCommandApplier _applier;
    private readonly IHl2mpNavigation _nav;
    private readonly List<Hl2mpBot> _bots = new();
    private readonly List<Drive> _drives = new();
    private readonly List<Goto> _gotos = new();
    private readonly WeaponCatalog _weapons;
    private readonly IBotInventory _inventory;
    private readonly IBotNameSource _names;
    private readonly IScoreboardTags _tags;

    /// <summary>Mixed into the match seed for a bot's name, so the name and the bot's other draws are separate streams.</summary>
    public const ulong NameSalt = 0x4E41_4D45UL;
    private int _serial;
    private int _seq;

    public Hl2mpBots(IBotPlayers players, IPlayerCommandRunner runner, IEnginePlayers enginePlayers, IEngineEntities entities,
                     WeaponCatalog weapons, IEngineFrame frame, IHl2mpNavigation nav, IBotInventory inventory,
                     IBotNameSource names, IScoreboardTags tags, ILog log, Senses.Hl2mpAiSession? ai = null,
                     IEngineNetworking? networking = null)
    {
        _ai = ai;
        _nav = nav;
        _names = names;
        _tags = tags;
        _weapons = weapons;
        _players = players;
        _frame = frame;
        _log = log;
        _self = new EngineBotSelf(enginePlayers, entities, weapons, inventory, networking);
        _inventory = inventory;
        _applier = new BotCommandApplier(runner, _self);
    }

    /// <summary>Before the entities think, so a bot's command runs in the same frame stage order every tick.</summary>
    public int FrameOrder => ModStages.First + 500;

    public IReadOnlyList<Hl2mpBot> Bots => _bots;

    /// <summary>Counts every change to <see cref="Bots"/>, so a reader keeps up with a compare.</summary>
    public int Version { get; private set; }

    public BotCommandApplier Applier => _applier;

    /// <summary>
    /// Adds a bot with <paramref name="skills"/>, called <paramref name="name"/>
    /// as given, or, with none, a name drawn from the pools by its most
    /// distinctive trait (§3.5.1); its skill label goes on the scoreboard
    /// beside the name. Answers null, or why not.
    /// </summary>
    public string? Add(string? name, SkillProfile skills)
    {
        // No navigation, no bot (plan_hl2mp_ai §6.3): the same line the level load printed.
        if (_nav.Refusal is { } refused)
            return refused;

        int serial = _serial + 1;
        if (name is null)
        {
            if (_names.Pools is not { } pools)
                return _names.Refusal;
            var taken = new HashSet<string>(_bots.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            name = BotNamer.Name(pools, skills, AiRandom.SeedFor(MatchSeed ^ NameSalt, serial), taken);
        }

        var stream = new AiRandom(AiRandom.SeedFor(MatchSeed, serial));
        EntityHandle player = _players.Add(name, BotAppearance.For(stream), out string refusal);
        if (player == EntityHandle.None)
            return refusal;

        BotPlayer made = _players.Bots.Last(b => b.Player == player);
        _serial = serial;
        var id = new ActorId(made.EntIndex, serial);
        _applier.Add(id, player, MotionCurves.For(skills), stream.NextUInt64());
        _bots.Add(new Hl2mpBot(id, made.Name, skills));
        Version++;
        string label = BotSkillLabel.For(skills);
        _tags.Set(player, label);
        _log.Msg($"[SourceSharp/Ai] bot {made.Name} {label} added: client {made.EntIndex}, {skills.Name} ({skills.Spent} points, {skills.StyleName})\n");
        return null;
    }

    /// <summary>Kicks the bot called <paramref name="name"/>, or every bot for "all"; answers how many.</summary>
    public int Kick(string name)
    {
        int kicked = 0;
        foreach (Hl2mpBot bot in _bots.ToArray())
        {
            if (name != "all" && !string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            EntityHandle player = _applier.PlayerOf(bot.Id);
            if (player != EntityHandle.None && _players.Kick(player, "kicked by ai_bot_kick"))
                kicked++;
        }

        return kicked;
    }

    /// <summary>
    /// Drives the bot called <paramref name="name"/> for <paramref name="seconds"/>:
    /// it faces <paramref name="yaw"/> and runs toward a point far along it.
    /// Through the applier and the real movement, with no nav.
    /// </summary>
    public string? DriveFor(string name, float yaw, float seconds)
    {
        int at = _bots.FindIndex(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
            return $"no bot called '{name}'";

        Hl2mpBot bot = _bots[at];
        EntityHandle player = _applier.PlayerOf(bot.Id);
        BotSelfFacts start = _self.Read(player);
        int ticks = Math.Max(1, (int)MathF.Round(seconds / 0.015f));
        _drives.RemoveAll(d => d.Bot == bot.Id);
        float rad = yaw * (MathF.PI / 180f);
        var dir = new AiVector(MathF.Cos(rad), MathF.Sin(rad), 0f);
        _drives.Add(new Drive(bot.Id, bot.Name, _frame.TickCount() + ticks, start.Origin, seconds,
                              start.Origin + dir * 4000f, start.Eye + dir * 4000f));
        return null;
    }

    /// <summary>
    /// Sends the bot called <paramref name="name"/> along a flow field to
    /// <paramref name="target"/> (plan_hl2mp_ai Phase 3 step 5): it holds
    /// <c>Follow</c> to the cell that touches the point, logs its arrival, and
    /// logs what it picks up there.
    /// </summary>
    public string? GoTo(string name, AiVector target, string? pickup)
    {
        if (_nav.Current is not { } nav || _nav.Obstacles is not { } obstacles)
            return _nav.Refusal ?? "no navigation";
        int at = _bots.FindIndex(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
            return $"no bot called '{name}'";

        Hl2mpBot bot = _bots[at];
        BotSelfFacts self = _self.Read(_applier.PlayerOf(bot.Id));
        NavGraph graph = nav.Nav.Graph;
        CellId objective = CellId.None;
        var field = new FlowField();
        CellId from = graph.FindCell(self.Origin + new AiVector(0f, 0f, 1f));
        foreach (CellId c in NavPoints.CellsNear(graph, target, ClearanceClass.Crouch).Take(8))
        {
            field.Build(graph, c, ClearanceClass.Crouch, obstacles);
            if (!from.IsValid || float.IsFinite(field.CostAt(from.Index)))
            {
                objective = c;
                break;
            }
        }

        if (!objective.IsValid)
            return $"no cell reaches ({target.X:F0} {target.Y:F0} {target.Z:F0}) from the bot";

        var follower = new NavFollower(graph, objective, target, ClearanceClass.Crouch, obstacles);
        follower.Rebuild();
        _gotos.RemoveAll(g => g.Bot == bot.Id);
        _drives.RemoveAll(d => d.Bot == bot.Id);
        int tick = _frame.TickCount();
        _gotos.Add(new Goto(bot.Id, bot.Name, follower, pickup, tick, tick, float.PositiveInfinity, Weapons(_applier.PlayerOf(bot.Id))));
        _log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] bot {bot.Id.Serial} ({bot.Name}) follows the field to cell {objective.Index} at ({target.X:F0} {target.Y:F0} {target.Z:F0}), "
            + $"{follower.Objective.Index}: cost {(from.IsValid ? field.CostAt(from.Index) : float.NaN):F0} from ({self.Origin.X:F0} {self.Origin.Y:F0} {self.Origin.Z:F0})\n"));
        return null;
    }

    /// <summary>
    /// A scripted action for the bot called <paramref name="name"/> (the
    /// scenario harness's, §6.6): fire for <paramref name="seconds"/>, or a
    /// one-shot reload or weapon select, holding where it stands and looks.
    /// </summary>
    public string? Act(string name, ActionKind kind, float seconds, int slot)
    {
        int at = _bots.FindIndex(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
            return $"no bot called '{name}'";
        Hl2mpBot bot = _bots[at];
        BotSelfFacts self = _self.Read(_applier.PlayerOf(bot.Id));
        float yaw = self.View.Yaw * (MathF.PI / 180f), pitch = self.View.Pitch * (MathF.PI / 180f);
        AiVector lookAt = self.Eye + new AiVector(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch)) * 1000f;
        int tick = _frame.TickCount();
        int until = tick + (kind == ActionKind.Fire ? Math.Max(1, (int)MathF.Round(seconds / 0.015f)) : 2);
        _acts.RemoveAll(a => a.Bot == bot.Id);
        _gotos.RemoveAll(g => g.Bot == bot.Id);
        _drives.RemoveAll(d => d.Bot == bot.Id);
        _acts.Add(new ScriptedAct(bot.Id, until, lookAt, kind == ActionKind.Fire
            ? new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0)
            : new ActionIntent(kind, FireStyle.Tap, slot, ++_oneShot)));
        return null;
    }

    private readonly List<ScriptedAct> _acts = new();
    private int _oneShot;

    private void Acting(Tick tick)
    {
        for (int i = _acts.Count - 1; i >= 0; i--)
        {
            ScriptedAct a = _acts[i];
            if (tick.Value >= a.UntilTick)
            {
                _acts.RemoveAt(i);
                continue;
            }

            var look = new LookIntent(LookKind.Point, a.LookAt, ActorId.None, AimPoint.Body, 0f, 0);
            var intent = new IntentCommand(a.Bot, tick, ++_seq, tick + IntentCommand.LifetimeTicks, MoveIntent.Hold, look, a.Action, default);
            _applier.Submit(in intent);
        }
    }

    private readonly record struct ScriptedAct(ActorId Bot, int UntilTick, AiVector LookAt, ActionIntent Action);

    /// <summary>The weapons a player holds, by class.</summary>
    private HashSet<string> Weapons(EntityHandle player) => new(_inventory.Held(player), StringComparer.Ordinal);

    /// <summary>How often a follower says where it is (2 s).</summary>
    public const int ProgressTicks = 133;

    /// <summary>How long a follower may make no progress before it says it is stuck.</summary>
    public const int StuckTicks = 200;

    public void OnPreThink(float frameTime)
    {
        Follow();
        var tick = new Tick(_frame.TickCount());
        Steer(tick);
        Walk(tick);
        Acting(tick);
        Think(tick);
        _applier.ApplyTick(tick, frameTime > 0f ? frameTime : 0.015f);
    }

    private readonly Senses.Hl2mpAiSession? _ai;
    private readonly BrainDriver _driver = new();

    /// <summary>Whether the brains drive the bots (<c>ai_think</c>); a scripted act, drive or walk overrides a bot's brain while it lasts.</summary>
    public bool Thinking { get; set; } = true;

    public BrainDriver Driver => _driver;

    /// <summary>Each bot's brain's latest intent, carried out for this tick (§2.3).</summary>
    private void Think(Tick tick)
    {
        if (_ai is null || !Thinking)
            return;
        foreach (Hl2mpBot bot in _bots)
        {
            if (Scripted(bot.Id) || !_ai.Session.TryLatestIntent(bot.Id.Slot, out IntentCommand brain) || brain.Bot != bot.Id)
                continue;
            EntityHandle player = _applier.PlayerOf(bot.Id);
            if (player == EntityHandle.None)
                continue;
            BotSelfFacts self = _self.Read(player);
            IntentCommand now = _driver.Drive(in brain, self.Origin, tick, ++_seq, _nav.Current?.Nav.Graph, _nav.Obstacles, self.OnLadder);
            _applier.Submit(in now);
            SayLadder(bot.Id, bot.Name, self, tick);
            if (LogThink && _ai.Session.BrainIn(bot.Id.Slot)?.Decider is { } decider)
                SayDoing(bot, decider.Doing, self.Origin, tick);
        }
    }

    /// <summary>Whether each change of a bot's branch, leaf or verdict is written to the console (<c>ai_think_log</c>).</summary>
    public bool LogThink { get; set; }

    private readonly Dictionary<int, (string, string, string)> _said = new();

    private void SayDoing(Hl2mpBot bot, in DoingRecord d, AiVector at, Tick tick)
    {
        SelfFlags flags = _ai is not null && _ai.Session.Snapshots.TryGet(new Tick(tick.Value - 1), out AiSnapshot snap) && snap.ViewFor(bot.Id.Slot) is { } v
            ? v.Self.Flags : SelfFlags.None;
        (string, string, string) key = (d.Branch, d.Leaf, d.Verdict + ((flags & SelfFlags.Reloading) != 0 ? "+r" : ""));
        if (_said.TryGetValue(bot.Id.Slot, out (string, string, string) last) && last == key)
            return;
        _said[bot.Id.Slot] = key;
        _log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] think {tick.Value} {bot.Name}: {d.Branch}/{d.Leaf} verdict '{d.Verdict}' target {(d.Target.IsValid ? d.Target.Slot : -1)} "
            + $"at ({at.X:F0} {at.Y:F0} {at.Z:F0}) goal {(d.HasGoal ? $"({d.Goal.X:F0} {d.Goal.Y:F0} {d.Goal.Z:F0})" : "-")} "
            + $"P(empty) {d.PEmpty:F2} P(reloading) {d.PReloading:F2} window {d.WindowSeconds:F2}s arrive {d.ArriveSeconds:F2}s "
            + $"scoreboard {d.ScoreboardTerm:F2} ammo {d.AmmoTerm:F2} self {flags}\n"));
    }

    private bool Scripted(ActorId bot)
    {
        foreach (Goto g in _gotos)
            if (g.Bot == bot)
                return true;
        foreach (Drive d in _drives)
            if (d.Bot == bot)
                return true;
        foreach (ScriptedAct a in _acts)
            if (a.Bot == bot)
                return true;
        return false;
    }

    /// <summary>The core's bots are the truth: one kicked or gone leaves, one a level change moved is rebound.</summary>
    private void Follow()
    {
        IReadOnlyList<BotPlayer> live = _players.Bots;
        for (int i = _bots.Count - 1; i >= 0; i--)
        {
            Hl2mpBot bot = _bots[i];
            int at = -1;
            for (int j = 0; j < live.Count; j++)
            {
                if (live[j].EntIndex == bot.Id.Slot)
                    at = j;
            }

            if (at < 0)
            {
                _applier.Remove(bot.Id.Slot);
                _drives.RemoveAll(d => d.Bot == bot.Id);
                _bots.RemoveAt(i);
                Version++;
                _log.Msg($"[SourceSharp/Ai] bot {bot.Name} left\n");
                continue;
            }

            _applier.Rebind(bot.Id.Slot, live[at].Player);
        }
    }

    private void Steer(Tick tick)
    {
        for (int i = _drives.Count - 1; i >= 0; i--)
        {
            Drive d = _drives[i];
            EntityHandle player = _applier.PlayerOf(d.Bot);
            BotSelfFacts now = _self.Read(player);
            if (tick.Value >= d.UntilTick)
            {
                float moved = new AiVector(now.Origin.X - d.From.X, now.Origin.Y - d.From.Y, 0f).Length;
                _log.Msg($"[SourceSharp/Ai] bot {d.Name} drove {moved:F0} units in {d.Seconds:F1} s, from "
                         + $"({d.From.X:F0} {d.From.Y:F0} {d.From.Z:F0}) to ({now.Origin.X:F0} {now.Origin.Y:F0} {now.Origin.Z:F0}), "
                         + $"view yaw {now.View.Yaw:F0}\n");
                _drives.RemoveAt(i);
                continue;
            }

            // One point to face and one to run to, fixed at the start: the
            // same look is one stimulus, reacted to once, then held.
            var move = new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, d.RunTo, default, default, default, default, false, 0);
            var look = new LookIntent(LookKind.Point, d.LookAt, ActorId.None, AimPoint.Body, 0f, 0);
            var intent = new IntentCommand(d.Bot, tick, ++_seq, tick + IntentCommand.LifetimeTicks, move, look, ActionIntent.None, default);
            _applier.Submit(in intent);
        }
    }

    private void Walk(Tick tick)
    {
        for (int i = _gotos.Count - 1; i >= 0; i--)
        {
            Goto g = _gotos[i];
            EntityHandle player = _applier.PlayerOf(g.Bot);
            if (player == EntityHandle.None)
            {
                _gotos.RemoveAt(i);
                continue;
            }

            BotSelfFacts self = _self.Read(player);
            if (tick.Value % 16 == 0)
                g.Follower.Rebuild();
            MoveIntent move = g.Follower.Next(self.Origin, out FollowState state);

            // What it picked up on the way: a weapon it did not hold before.
            HashSet<string> held = Weapons(player);
            foreach (string w in held.Where(w => !g.Held.Contains(w)))
            {
                var ev = AiEvent.PickedUp(g.Bot, tick, _weapons.IndexOf(w) + 1, self.Origin);
                _log.Msg($"[SourceSharp/Ai] bot {g.Bot.Serial} ({g.Name}) picked up {w} at ({self.Origin.X:F0} {self.Origin.Y:F0} {self.Origin.Z:F0}): {ev.Kind}\n");
            }

            g.Held.UnionWith(held);
            if (state == FollowState.Arrived)
            {
                _log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] bot {g.Bot.Serial} arrived cell {g.Follower.At.Index} at ({self.Origin.X:F0} {self.Origin.Y:F0} {self.Origin.Z:F0}) "
                    + $"after {(tick.Value - g.StartTick) * 0.015f:F1} s ({g.Name})\n"));
                _gotos.RemoveAt(i);
                continue;
            }

            float cost = g.Follower.CostLeft;
            if ((tick.Value - g.StartTick) % ProgressTicks == 0)
            {
                _log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] bot {g.Bot.Serial} at ({self.Origin.X:F0} {self.Origin.Y:F0} {self.Origin.Z:F0}), cell {g.Follower.At.Index}, "
                    + $"{state}, cost left {cost:F0}, {move.Kind} {move.CornerCount} corners, next {g.Follower.NextStep()}\n"));
            }

            if (cost < g.BestCost - 1f)
                _gotos[i] = g = g with { BestCost = cost, ProgressTick = tick.Value };
            else if (tick.Value - g.ProgressTick > StuckTicks)
            {
                _log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"[SourceSharp/Ai] bot {g.Bot.Serial} ({g.Name}) is stuck at ({self.Origin.X:F0} {self.Origin.Y:F0} {self.Origin.Z:F0}), "
                    + $"cell {g.Follower.At.Index}, {state}, cost left {cost:F0}, next {g.Follower.NextStep()}; {Steering(g.Bot, move)}; "
                    + $"{(g.Follower.ReportStuck(self.Origin) ? $"routes round {g.Follower.Avoided.Count} cells" : "no way round: jumps at it")}\n"));
                _gotos[i] = g = g with { ProgressTick = tick.Value, BestCost = float.PositiveInfinity };
            }

            var look = new LookIntent(LookKind.Path, default, ActorId.None, AimPoint.Body, 0f, 0);
            ActionIntent action = ActionIntent.None;
            if (_nav.Current?.Nav.Graph is { } graph)
                _driver.LadderFor(g.Bot.Slot, g.Follower, self.Origin, tick, self.OnLadder, graph, ref move, ref look, ref action);
            SayLadder(g.Bot, g.Name, self, tick);
            var intent = new IntentCommand(g.Bot, tick, ++_seq, tick + IntentCommand.LifetimeTicks, move, look, action, default);
            _applier.Submit(in intent);
        }
    }

    private readonly Dictionary<int, (bool On, AiVector From)> _ladder = new();

    /// <summary>A bot getting on and off a ladder, said once each way, with how far it climbed.</summary>
    private void SayLadder(ActorId bot, string name, in BotSelfFacts self, Tick tick)
    {
        (bool on, AiVector from) = _ladder.GetValueOrDefault(bot.Slot);
        if (self.OnLadder == on)
            return;
        _ladder[bot.Slot] = (self.OnLadder, self.Origin);
        AiVector at = self.Origin;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _log.Msg(self.OnLadder
            ? string.Create(inv, $"[SourceSharp/Ai] bot {bot.Serial} ({name}) is on a ladder at ({at.X:F0} {at.Y:F0} {at.Z:F0}), tick {tick.Value}\n")
            : string.Create(inv, $"[SourceSharp/Ai] bot {bot.Serial} ({name}) got off the ladder at ({at.X:F0} {at.Y:F0} {at.Z:F0}), {at.Z - from.Z:F0} u from where it got on, tick {tick.Value}\n"));
    }

    /// <summary>The corners a move carries and what the applier made of them, for a stuck line.</summary>
    private string Steering(ActorId bot, in MoveIntent move)
    {
        var corners = new List<string>();
        for (int c = 0; c < move.CornerCount; c++)
        {
            AiVector p = move.Corner(c);
            corners.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({p.X:F0} {p.Y:F0} {p.Z:F0})"));
        }

        (AiVector? corner, float forward, float side) = _applier.LastOf(bot);
        string to = corner is { } k ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({k.X:F0} {k.Y:F0} {k.Z:F0})") : "none";
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{move.Kind} corners {string.Join(" ", corners)}, steering to {to}, forward {forward:F0} side {side:F0}");
    }

    private sealed record Goto(ActorId Bot, string Name, NavFollower Follower, string? Pickup, int StartTick, int ProgressTick, float BestCost,
                               HashSet<string> Held);

    private readonly record struct Drive(ActorId Bot, string Name, int UntilTick, AiVector From, float Seconds,
                                         AiVector RunTo, AiVector LookAt);
}
