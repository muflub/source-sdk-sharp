using System.Globalization;
using System.Text;

namespace SourceSharp.Ai;

/// <summary>One actor's body in its fights over a recording (<c>ai-replay fight</c>): a bot's, or a person's by the same definitions.</summary>
public sealed record FightMotionRow(
    ActorId Bot,
    bool IsBot,
    int FiringTicks,
    int MovingTicks,
    int CommandedTicks,
    int[] IntentKinds,
    double FireSeconds,
    int SideFlips,
    int CommandFlips,
    int JumpsFiring,
    int DuckedTicks,
    int HitsTaken,
    double DamageTaken,
    int Kills,
    int Deaths,
    IReadOnlyDictionary<int, int> Leaves,
    int AskedTicks = 0)
{
    /// <summary>Of the living ticks whose intent asked to fire, the share whose command held attack (the hands wait for the eye), or NaN with none.</summary>
    public double FiredShare => AskedTicks > 0 ? FiringTicks / (double)AskedTicks : double.NaN;

    /// <summary>The share of firing ticks at <see cref="AiFightMotion.MovingSpeed"/> or more, or NaN with none.</summary>
    public double MovingShare => FiringTicks > 0 ? MovingTicks / (double)FiringTicks : double.NaN;

    /// <summary>The share of firing ticks whose command moved the body at all (a forward or side move), or NaN with none.</summary>
    public double CommandedShare => FiringTicks > 0 ? CommandedTicks / (double)FiringTicks : double.NaN;

    /// <summary>The share of firing ticks under an intent of <paramref name="kind"/>, or NaN with none.</summary>
    public double IntentShare(MoveKind kind) => FiringTicks > 0 ? IntentKinds[(int)kind] / (double)FiringTicks : double.NaN;

    /// <summary>The body's changes of side a second of fire, or NaN with none.</summary>
    public double FlipsPerSecond => FireSeconds > 0 ? SideFlips / FireSeconds : double.NaN;

    /// <summary>The commands' changes of side a second of fire, or NaN with none.</summary>
    public double CommandFlipsPerSecond => FireSeconds > 0 ? CommandFlips / FireSeconds : double.NaN;

    /// <summary>Kills over deaths (kills when it never died).</summary>
    public double KillsPerDeath => Deaths > 0 ? Kills / (double)Deaths : Kills;
}

/// <summary>
/// How a bot's body moves while it fires (lane Gfight, 2026-11-15: "and not move while shooting"):
/// per bot, from a recording's commands, intents, frames and events, and in total.
/// <list type="bullet">
/// <item><b>firing tick</b>: a command holding attack while the bot lives (its frame at the tick or
/// within four before, and the one before that).</item>
/// <item><b>moving</b>: the body's flat speed (<see cref="Motion"/>, from its frames' origins) at least <see cref="MovingSpeed"/>, half a Soldier's run.</item>
/// <item><b>commanded</b>: the command's forward or side move not zero: what the applier sent.</item>
/// <item><b>intent</b>: the kind of the bot's last intent at or before the tick: what its brain asked.</item>
/// <item><b>side flips</b>: in a run of firing commands (a gap over <see cref="RunGapTicks"/> ends one),
/// changes of sign of the body's velocity across the view (right of the view's yaw), counting only
/// speeds across of <see cref="SideUnits"/> or more; command flips the same over the side move.</item>
/// <item><b>jumps</b>: presses of jump on firing ticks; <b>ducked</b>: firing ticks holding duck.</item>
/// <item><b>hits taken</b>: the bot's own <see cref="AiEventKind.Damaged"/> events; <b>kills</b> and
/// <b>deaths</b>: the kill feed, each death once however many HUDs showed it.</item>
/// <item><b>leaves</b>: the firing ticks by the leaf the intent names.</item>
/// <item><b>fired of asked</b>: firing ticks over the living ticks whose intent asked to fire: what the hands' wait
/// for the eye (the applier's) kept back.</item>
/// </list>
/// The three shares in a row are the chain a stand-still is lost along: the brain's intent, the
/// command, the body. Pure; reads only BotAdded, DecisionTree, Intent, UserCmd, Frame and Event records.
/// </summary>
public static class AiFightMotion
{
    /// <summary>A body at least this fast is moving: half a Soldier's 240 u/s run.</summary>
    public const float MovingSpeed = 120f;

    /// <summary>The least speed across the view that has a side, for the flips.</summary>
    public const float SideUnits = 60f;

    /// <summary>A gap between firing commands longer than this (1 s) ends a run of fire.</summary>
    public const int RunGapTicks = 66;

    private const int Attack = 1, Jump = 2, Duck = 4;

    /// <summary>Every actor's row that sent a command (bots, then people, each by slot), and the recording's decision tree (null without one).</summary>
    public static (List<FightMotionRow> Rows, AiDecisionTree? Tree) Read(Stream recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        AiRecordFormat.ReadFileHeader(recording);
        var bots = new List<ActorId>();
        var cmds = new Dictionary<ActorId, List<AiUserCmd>>();
        var intents = new Dictionary<ActorId, List<IntentCommand>>();
        var frames = new Dictionary<ActorId, SortedList<int, AiActorFrame>>();
        var events = new List<AiEvent>();
        AiDecisionTree? tree = null;
        while (AiRecordFormat.TryReadRecord(recording, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.BotAdded:
                    ActorId bot = AiRecordFormat.ReadActor(r);
                    if (!bots.Contains(bot))
                        bots.Add(bot);
                    break;
                case AiRecordType.DecisionTree:
                    tree = AiRecordFormat.ReadDecisionTree(r);
                    break;
                case AiRecordType.Intent:
                    IntentCommand i = AiRecordFormat.ReadIntent(r);
                    Add(intents, i.Bot, i);
                    break;
                case AiRecordType.UserCmd:
                    AiUserCmd c = AiRecordFormat.ReadUserCmd(r);
                    Add(cmds, c.Actor, c);
                    break;
                case AiRecordType.Frame:
                    AiActorFrame f = AiRecordFormat.ReadFrame(r);
                    if (!frames.TryGetValue(f.Actor, out SortedList<int, AiActorFrame>? at))
                        frames[f.Actor] = at = new SortedList<int, AiActorFrame>();
                    at[f.Tick.Value] = f;
                    break;
                case AiRecordType.Event:
                    AiEvent e = AiRecordFormat.ReadEvent(r);
                    if (e.Kind is AiEventKind.Damaged or AiEventKind.KillFeed)
                        events.Add(e);
                    break;
            }
        }

        var rows = new List<FightMotionRow>();
        foreach (ActorId b in bots.Concat(cmds.Keys.Except(bots)).OrderBy(b => !bots.Contains(b)).ThenBy(b => b.Slot).ThenBy(b => b.Serial))
        {
            rows.Add(For(b, cmds.GetValueOrDefault(b) ?? [], intents.GetValueOrDefault(b) ?? [],
                         frames.GetValueOrDefault(b) ?? new SortedList<int, AiActorFrame>(), events) with { IsBot = bots.Contains(b) });
        }

        return (rows, tree);
    }

    private static void Add<T>(Dictionary<ActorId, List<T>> into, ActorId who, T item)
    {
        if (!into.TryGetValue(who, out List<T>? list))
            into[who] = list = new List<T>();
        list.Add(item);
    }

    /// <summary>One bot's row from its commands and intents (each in tick order), its frames and everyone's events.</summary>
    public static FightMotionRow For(ActorId bot, IReadOnlyList<AiUserCmd> commands, IReadOnlyList<IntentCommand> intents,
                                     SortedList<int, AiActorFrame> frames, IReadOnlyList<AiEvent> events)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(events);
        int firing = 0, moving = 0, commanded = 0, sideFlips = 0, commandFlips = 0, jumps = 0, ducked = 0, asked = 0;
        int[] kinds = new int[Enum.GetValues<MoveKind>().Length];
        var leaves = new Dictionary<int, int>();
        int intent = -1, lastFire = int.MinValue, side = 0, commandSide = 0, lastButtons = 0;
        foreach (AiUserCmd c in commands)
        {
            int t = c.Tick.Value;
            while (intent + 1 < intents.Count && intents[intent + 1].FromTick.Value <= t)
                intent++;
            bool held = (c.Buttons & Attack) != 0;
            int buttons = lastButtons;
            lastButtons = c.Buttons;
            AiVector? motion = Motion(frames, t);
            asked += motion is not null && intent >= 0 && intents[intent].Action.Kind == ActionKind.Fire ? 1 : 0;
            if (!held || motion is not { } velocity)
                continue;
            if (t - lastFire > RunGapTicks)
                (side, commandSide) = (0, 0);
            lastFire = t;
            firing++;
            float speed = velocity.Length2D;
            moving += speed >= MovingSpeed ? 1 : 0;
            commanded += c.ForwardMove != 0f || c.SideMove != 0f ? 1 : 0;
            jumps += (c.Buttons & Jump) != 0 && (buttons & Jump) == 0 ? 1 : 0;
            ducked += (c.Buttons & Duck) != 0 ? 1 : 0;
            if (intent >= 0)
            {
                IntentCommand i = intents[intent];
                kinds[(int)i.Move.Kind]++;
                leaves[i.Reason.NodeId] = leaves.GetValueOrDefault(i.Reason.NodeId) + 1;
            }
            else
            {
                kinds[(int)MoveKind.Hold]++;
            }

            // Across the view: right of its yaw, as the applier's side move is.
            float yaw = c.View.Yaw * (MathF.PI / 180f);
            float across = velocity.X * MathF.Sin(yaw) - velocity.Y * MathF.Cos(yaw);
            sideFlips += Flip(ref side, across, SideUnits);
            commandFlips += Flip(ref commandSide, c.SideMove, 1f);
        }

        int hits = 0, kills = 0, deaths = 0;
        double damage = 0;
        var seen = new HashSet<(ActorId, int)>();
        foreach (AiEvent e in events)
        {
            if (e.Kind == AiEventKind.Damaged && e.Bot == bot)
            {
                hits++;
                damage += e.Amount;
            }
            else if (e.Kind == AiEventKind.KillFeed && seen.Add((e.Other, e.Tick.Value)))
            {
                deaths += e.Other == bot ? 1 : 0;
                kills += e.Killer == bot && e.Other != bot ? 1 : 0;
            }
        }

        return new FightMotionRow(bot, true, firing, moving, commanded, kinds, firing * AiMetrics.TickSeconds, sideFlips, commandFlips, jumps, ducked,
                                  hits, damage, kills, deaths, leaves, asked);
    }

    /// <summary>One more flip when <paramref name="value"/> has a side (at least <paramref name="least"/> either way) and it is not the last one's.</summary>
    private static int Flip(ref int side, float value, float least)
    {
        int now = value >= least ? 1 : value <= -least ? -1 : 0;
        if (now == 0)
            return 0;
        int was = side;
        side = now;
        return was != 0 && was != now ? 1 : 0;
    }

    /// <summary>
    /// The body's velocity at <paramref name="tick"/>, from its frames' origins: the frame at the tick or
    /// within four before, against the one before that within <see cref="MotionTicks"/>; null for a
    /// body with no such pair (not alive, just spawned) or one moved faster than any run (a teleport, a
    /// respawn). The origins, not the frames' velocity, which read zero for people in older recordings.
    /// </summary>
    public static AiVector? Motion(SortedList<int, AiActorFrame> frames, int tick)
    {
        ArgumentNullException.ThrowIfNull(frames);
        for (int t = tick; t >= tick - 4; t--)
        {
            int i = frames.IndexOfKey(t);
            if (i < 0)
                continue;
            if (i == 0)
                return null;
            int dt = t - frames.Keys[i - 1];
            if (dt > MotionTicks)
                return null;
            AiVector step = frames.Values[i].Origin - frames.Values[i - 1].Origin;
            if (step.Length > MovedUnitsPerTick * dt)
                return null;
            return step * (1f / (dt * AiMetrics.TickSeconds));
        }

        return null;
    }

    /// <summary>Frames further apart than this make no velocity.</summary>
    public const int MotionTicks = 8;

    /// <summary>Faster than any fall or blast (52 u a tick is 3,500 u/s): a body that moved more was moved.</summary>
    public const float MovedUnitsPerTick = 52f;

    /// <summary>The rows summed into one, for the bots of a run (or of runs), or its people.</summary>
    public static FightMotionRow Total(IEnumerable<FightMotionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int[] kinds = new int[Enum.GetValues<MoveKind>().Length];
        var leaves = new Dictionary<int, int>();
        int firing = 0, moving = 0, commanded = 0, flips = 0, cflips = 0, jumps = 0, ducked = 0, hits = 0, kills = 0, deaths = 0, asked = 0;
        double seconds = 0, damage = 0;
        foreach (FightMotionRow r in rows)
        {
            (firing, moving, commanded, seconds) = (firing + r.FiringTicks, moving + r.MovingTicks, commanded + r.CommandedTicks, seconds + r.FireSeconds);
            (flips, cflips, jumps, ducked) = (flips + r.SideFlips, cflips + r.CommandFlips, jumps + r.JumpsFiring, ducked + r.DuckedTicks);
            (hits, damage, kills, deaths) = (hits + r.HitsTaken, damage + r.DamageTaken, kills + r.Kills, deaths + r.Deaths);
            for (int k = 0; k < kinds.Length; k++)
                kinds[k] += r.IntentKinds[k];
            asked += r.AskedTicks;
            foreach ((int node, int n) in r.Leaves)
                leaves[node] = leaves.GetValueOrDefault(node) + n;
        }

        return new FightMotionRow(ActorId.None, true, firing, moving, commanded, kinds, seconds, flips, cflips, jumps, ducked, hits, damage, kills, deaths, leaves, asked);
    }

    /// <summary>The report's text: a line an actor, the bots' total and the people's, and the bots' leaves.</summary>
    public static string Table(IReadOnlyList<FightMotionRow> rows, AiDecisionTree? tree)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var w = new StringBuilder();
        foreach (FightMotionRow r in rows)
            w.Append(Line($"{(r.IsBot ? "slot" : "person")} {r.Bot.Slot}", r));
        return w.Append(Summary(rows, tree)).ToString();
    }

    /// <summary>The totals alone: the bots', the people's (with any), and the bots' leaves.</summary>
    public static string Summary(IReadOnlyList<FightMotionRow> rows, AiDecisionTree? tree)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var w = new StringBuilder();
        FightMotionRow all = Total(rows.Where(r => r.IsBot));
        w.Append(Line("all", all));
        if (rows.Any(r => !r.IsBot))
            w.Append(Line("people", Total(rows.Where(r => !r.IsBot))));
        foreach ((int node, int n) in all.Leaves.OrderByDescending(kv => kv.Value).Take(8))
            w.Append(CultureInfo.InvariantCulture, $"fight: leaf {(tree is null ? node.ToString(CultureInfo.InvariantCulture) : tree.Name(node))} {n / (double)Math.Max(1, all.FiringTicks):P1}\n");
        return w.ToString();
    }

    private static string Line(string who, FightMotionRow r)
        => string.Create(CultureInfo.InvariantCulture,
            $"fight: {who} firing {r.FiringTicks} ({r.FireSeconds:F0} s) moving {r.MovingShare:P1} commanded {r.CommandedShare:P1} intent hold {r.IntentShare(MoveKind.Hold):P1} strafe {r.IntentShare(MoveKind.Strafe):P1} follow {r.IntentShare(MoveKind.Follow):P1} flips/s {r.FlipsPerSecond:F2} cmd flips/s {r.CommandFlipsPerSecond:F2} jumps {r.JumpsFiring} ducked {r.DuckedTicks / (double)Math.Max(1, r.FiringTicks):P1} hits taken {r.HitsTaken} damage {r.DamageTaken:F0} kills {r.Kills} deaths {r.Deaths} k/d {r.KillsPerDeath:F2} fired of asked {r.FiredShare:P1}\n");
}
