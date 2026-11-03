using System.Globalization;
using System.Text;

namespace SourceSharp.Ai;

/// <summary>One actor's distributions over a recording (plan_hl2mp_ai §3.4.4).</summary>
public sealed record ActorMetrics(
    ActorId Actor,
    bool Bot,
    int Commands,
    double Minutes,
    double[] AngularSpeedDegPerSec,
    double[] FlickPeaksDegPerSec,
    int SightsGained,
    double[] TimeToAcquireMs,
    double[] ReactionMs,
    double[] StrafeIntervalMs,
    double ReversalsPerMinute,
    HitRates Hits,
    int ShootingTicks = 0,
    int DuckedShootingTicks = 0,
    double[]? DuckHoldsMs = null)
{
    /// <summary>The share of the ticks holding fire spent ducked, or NaN with none.</summary>
    public double DuckedShootingShare => ShootingTicks > 0 ? DuckedShootingTicks / (double)ShootingTicks : double.NaN;
}

/// <summary>Hits of shots, under a label.</summary>
public readonly record struct HitLine(string Label, int Hits, int Shots)
{
    /// <summary>The share of shots that hit, or NaN with no shots.</summary>
    public double Rate => Shots > 0 ? Hits / (double)Shots : double.NaN;
}

/// <summary>
/// An actor's hit rate (user rulings 2026-11-02: "After my recording set med bot
/// to match my hit rate", "make sure to only count when i was shooting at bots,
/// not things in the world"): overall, per weapon and per range band (under 300,
/// 300 to 800, over 800 units), of the shots aimed at someone (<see cref="AimCone"/>)
/// and the hits they made. <see cref="Untargeted"/> shots had nobody in that cone:
/// they count in no rate, and a hit from one neither. <see cref="ByCone"/> is the
/// overall rate at each of <see cref="AimCone.SensitivityMarginsDeg"/>, so the
/// choice of cone can be seen.
/// </summary>
public sealed record HitRates(HitLine Overall, IReadOnlyList<HitLine> ByWeapon, IReadOnlyList<HitLine> ByRange, int Untargeted, IReadOnlyList<HitLine> ByCone);

/// <summary>
/// The §3.4.4 metrics of a recording, per actor, humans' and bots' by the same
/// definitions (<c>ai-replay metrics</c>). Pure; reads only UserCmd, Frame,
/// Event, Observed and BotAdded records.
/// <list type="bullet">
/// <item><b>angular speed</b>: between consecutive commands of an actor, the
/// view's turn (√(Δyaw² + Δpitch²), yaw wrapped) over the time between their
/// command ticks (1 to 8 ticks apart; others are gaps), in °/s.</item>
/// <item><b>flick</b>: a run of consecutive speeds of at least
/// <see cref="MovingDegPerSec"/> whose net turn is over 15° and that lasts
/// at most 400 ms; its peak is the run's highest speed.</item>
/// <item><b>time to acquire</b>: from a <c>SightGained</c> (a bot's own
/// event, a person's Observed one) to the first command whose view is within
/// 2° of the target's body (the segment from its origin to its eye, from the
/// frames), within the sighting or 3 s.</item>
/// <item><b>reaction</b>: from the same <c>SightGained</c> to the first
/// command pressing attack, or to the start of a turn at <see cref="MovingDegPerSec"/>
/// or more that takes at least <see cref="ReactionTurnDeg"/> off the angle to the
/// target, within 2 s: a person's hand is never still, and any small movement
/// toward the target read as a 30 ms reaction. A button already held and a turn
/// already going when the sight came are no answer to it: they count from the
/// next press, or the next turn after the view has slowed.</item>
/// <item><b>strafe interval</b>: between changes of <c>SideMove</c>'s sign
/// (zeros skipped).</item>
/// <item><b>reversals</b>: changes of the yaw step's sign (steps of 0.5° or
/// more) per minute of commands.</item>
/// <item><b>hit rate</b>: hits over shots. A shot is one trigger pull (a
/// <c>ShotFired</c>), however many pellets; a hit is one victim hurt in one
/// tick, however many pellets or events. A shot counts only if a sighted actor's
/// body was within <see cref="AimCone"/> of its crosshair; one with nobody there
/// (at a crate, a door, a place) is untargeted, in no rate, and a hit counts only
/// through a shot that counted: the same tick's, or a projectile's last shot. By
/// weapon: the weapon in the shooter's hands. By range band: the distance to the
/// actor the shot was aimed at. A hit by a weapon that fired no shot (a
/// crowbar's swing is no <c>ShotFired</c>) stays on its weapon's line and out of
/// the overall and band rates. A sighting held when the recording began gives a
/// shot its target and no reaction.</item>
/// </list>
/// A step counts only while the actor lives (its frames), and not across a
/// teleport or respawn. A bot's <c>SightGained</c> comes after its recognition delay; a person's
/// at the first tick a line was clear (its stimulus).
/// </summary>
public static class AiMetrics
{
    public const float TickSeconds = 0.015f;

    /// <summary>The speed a view must turn at to count as moving (a flick's run, a reaction's turn).</summary>
    public const double MovingDegPerSec = 60;

    public const double FlickMinDeg = 15, FlickMaxMs = 400, AcquireDeg = 2, AcquireWindowMs = 3000, ReactionWindowMs = 2000;

    /// <summary>How far a turn must take the view toward its target to be a reaction, not a hand that is never still.</summary>
    public const double ReactionTurnDeg = 3;

    /// <summary>How long after its shot a projectile's hit still belongs to that shot, in ticks (3 s).</summary>
    public const int ProjectileTicks = 200;

    /// <summary>The range bands' labels; a range under 300 is the first, over 800 the last.</summary>
    public static readonly System.Collections.Immutable.ImmutableArray<string> RangeBands = ["<300", "300-800", ">800"];

    /// <summary>The band of a range in units.</summary>
    public static int RangeBand(float range) => range < 300 ? 0 : range <= 800 ? 1 : 2;

    private const int Attack = 1;   // IN_ATTACK

    /// <summary>Every actor that sent a command, in actor order (slot, then serial).</summary>
    public static IReadOnlyList<ActorMetrics> Read(Stream recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        AiRecordFormat.ReadFileHeader(recording);
        var bots = new HashSet<ActorId>();
        var cmds = new Dictionary<ActorId, List<AiUserCmd>>();
        var frames = new Dictionary<ActorId, SortedList<int, AiActorFrame>>();
        var sights = new List<AiEvent>();
        var shots = new List<AiEvent>();
        var weapons = new Dictionary<int, string>();
        while (AiRecordFormat.TryReadRecord(recording, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.BotAdded:
                    bots.Add(AiRecordFormat.ReadActor(r));
                    break;
                case AiRecordType.UserCmd:
                    AiUserCmd c = AiRecordFormat.ReadUserCmd(r);
                    if (!cmds.TryGetValue(c.Actor, out List<AiUserCmd>? list))
                        cmds[c.Actor] = list = new List<AiUserCmd>();
                    list.Add(c);
                    break;
                case AiRecordType.Frame:
                    AiActorFrame f = AiRecordFormat.ReadFrame(r);
                    if (!frames.TryGetValue(f.Actor, out SortedList<int, AiActorFrame>? at))
                        frames[f.Actor] = at = new SortedList<int, AiActorFrame>();
                    at[f.Tick.Value] = f;
                    break;
                case AiRecordType.Event:
                case AiRecordType.Observed:
                    AiEvent e = AiRecordFormat.ReadEvent(r);
                    if (e.Kind is AiEventKind.SightGained or AiEventKind.SightLost)
                        sights.Add(e);
                    else if (e.Kind is AiEventKind.ShotFired or AiEventKind.HitDealt)
                        shots.Add(e);
                    break;
                case AiRecordType.World:
                    foreach (WeaponFacts w in AiRecordFormat.ReadWorld(r).Map.Weapons.All)
                        weapons[w.Kind] = w.Name;
                    break;
            }
        }

        return [.. cmds.OrderBy(kv => kv.Key.Slot).ThenBy(kv => kv.Key.Serial)
                       .Select(kv => For(kv.Key, bots.Contains(kv.Key), kv.Value, frames, sights, shots, weapons))];
    }

    /// <summary>One actor's metrics from its commands, everyone's frames and the sight events.</summary>
    public static ActorMetrics For(ActorId actor, bool bot, IReadOnlyList<AiUserCmd> commands,
                                   IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>> frames, IReadOnlyList<AiEvent> sights,
                                   IReadOnlyList<AiEvent> shots, IReadOnlyDictionary<int, string> weapons)
    {
        var speeds = new List<double>();
        double[] speedAt = new double[commands.Count];
        var steps = new List<(double Speed, double Yaw, double Pitch, int Index)>();
        bool framed = frames.ContainsKey(actor);
        for (int i = 1; i < commands.Count; i++)
        {
            int dt = commands[i].TickCount - commands[i - 1].TickCount;
            if (dt is < 1 or > 8 || (framed && !Living(frames, actor, commands[i - 1], commands[i])))
            {
                steps.Add((double.NaN, 0, 0, i));
                continue;
            }

            (double dy, double dp) = Delta(commands[i - 1].View, commands[i].View);
            double speed = Math.Sqrt(dy * dy + dp * dp) / (dt * TickSeconds);
            speeds.Add(speed);
            speedAt[i] = speed;
            steps.Add((speed, dy, dp, i));
        }

        var peaks = new List<double>();
        int run = -1;
        for (int i = 0; i <= steps.Count; i++)
        {
            bool moving = i < steps.Count && steps[i].Speed >= MovingDegPerSec;
            if (moving && run < 0)
                run = i;
            if (!moving && run >= 0)
            {
                AiUserCmd from = commands[steps[run].Index - 1], to = commands[steps[i - 1].Index];
                (double ny, double np) = Delta(from.View, to.View);
                double ms = (to.TickCount - from.TickCount) * TickSeconds * 1000;
                if (Math.Sqrt(ny * ny + np * np) > FlickMinDeg && ms <= FlickMaxMs)
                    peaks.Add(steps.Skip(run).Take(i - run).Max(s => s.Speed));
                run = -1;
            }
        }

        var acquire = new List<double>();
        var reaction = new List<double>();
        int gained = 0;
        foreach (AiEvent g in sights)
        {
            if (g.Kind != AiEventKind.SightGained || g.Bot != actor || g.Code == AiEvent.HeldSight)
                continue;
            gained++;
            int lost = sights.FirstOrDefault(e => e.Kind == AiEventKind.SightLost && e.Bot == actor && e.Other == g.Other && e.Tick.Value > g.Tick.Value).Tick.Value;
            int end = Math.Min(lost > 0 ? lost : int.MaxValue, g.Tick.Value + Ticks(AcquireWindowMs));
            int reactEnd = g.Tick.Value + Ticks(ReactionWindowMs);
            bool acquired = false, reacted = false;
            double lastAngle = double.NaN;
            int turnFrom = -1;
            double turnAngle = 0;
            // What was already under way when the sight came is no answer to it: a
            // button held on, a turn still going.
            bool held = true, turnedBefore = true;
            for (int i = 0; i < commands.Count; i++)
            {
                AiUserCmd c = commands[i];
                int t = c.Tick.Value;
                if (t < g.Tick.Value)
                    continue;
                if (t > Math.Max(end, reactEnd) || (acquired && reacted))
                    break;
                double angle = AngleTo(c, actor, g.Other, frames);
                if (!reacted && t <= reactEnd)
                {
                    bool attack = (c.Buttons & Attack) != 0;
                    bool fired = attack && !held;
                    held &= attack;
                    turnedBefore &= speedAt[i] >= MovingDegPerSec;
                    bool turning = !turnedBefore && !double.IsNaN(lastAngle) && !double.IsNaN(angle) && angle < lastAngle
                                   && speedAt[i] >= MovingDegPerSec;
                    if (!turning)
                    {
                        turnFrom = -1;
                    }
                    else if (turnFrom < 0)
                    {
                        turnFrom = t;
                        turnAngle = lastAngle;
                    }

                    if (turning && turnAngle - angle >= ReactionTurnDeg)
                    {
                        reaction.Add((turnFrom - g.Tick.Value) * TickSeconds * 1000);
                        reacted = true;
                    }
                    else if (fired)
                    {
                        reaction.Add((t - g.Tick.Value) * TickSeconds * 1000);
                        reacted = true;
                    }
                }

                if (!acquired && t <= end && angle <= AcquireDeg)
                {
                    acquire.Add((t - g.Tick.Value) * TickSeconds * 1000);
                    acquired = true;
                }

                lastAngle = angle;
            }
        }

        var strafes = new List<double>();
        int sign = 0;
        int? lastChange = null;
        foreach (AiUserCmd c in commands)
        {
            int s = Math.Sign(c.SideMove);
            if (s == 0 || s == sign)
                continue;
            if (sign != 0)
            {
                if (lastChange is int l)
                    strafes.Add((c.TickCount - l) * TickSeconds * 1000);
                lastChange = c.TickCount;
            }

            sign = s;
        }

        int reversals = 0, lastYawSign = 0;
        foreach ((double speed, double yaw, _, _) in steps)
        {
            if (double.IsNaN(speed) || Math.Abs(yaw) < 0.5)
                continue;
            int s = Math.Sign(yaw);
            if (lastYawSign != 0 && s != lastYawSign)
                reversals++;
            lastYawSign = s;
        }

        double minutes = commands.Count < 2 ? 0 : (commands[^1].TickCount - commands[0].TickCount) * TickSeconds / 60.0;
        (int shooting, int ducked, double[] holds) = Ducking(commands);
        return new ActorMetrics(actor, bot, commands.Count, minutes, [.. speeds], [.. peaks], gained, [.. acquire], [.. reaction], [.. strafes],
                                minutes > 0 ? reversals / minutes : 0, HitRatesOf(actor, commands, frames, sights, shots, weapons), shooting, ducked, holds);
    }

    private static HitRates HitRatesOf(ActorId actor, IReadOnlyList<AiUserCmd> commands, IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>> frames,
                                       IReadOnlyList<AiEvent> sights, IReadOnlyList<AiEvent> shots, IReadOnlyDictionary<int, string> weapons)
    {
        string Name(int kind) => weapons.TryGetValue(kind, out string? n) ? n : kind < 0 ? "unknown" : $"weapon {kind}";

        var fired = new List<AiEvent>();
        var hits = new List<AiEvent>();
        var counted = new HashSet<(ActorId, int)>();
        foreach (AiEvent e in shots)
        {
            if (e.Bot != actor)
                continue;
            if (e.Kind == AiEventKind.ShotFired)
                fired.Add(e);
            else if (counted.Add((e.Other, e.Tick.Value)))
                hits.Add(e);   // one victim in one tick is one hit, however many pellets
        }

        fired.Sort((a, b) => a.Tick.Value.CompareTo(b.Tick.Value));
        var aims = new (double Over, float Range)?[fired.Count];
        for (int i = 0; i < fired.Count; i++)
            aims[i] = ShotAim(actor, fired[i].Tick.Value, commands, frames, sights);

        int[] bandShots = new int[RangeBands.Length], bandHits = new int[RangeBands.Length];
        var byWeapon = new SortedDictionary<string, (int Hits, int Shots)>(StringComparer.Ordinal);
        int untargeted = 0, aimed = 0;
        for (int i = 0; i < fired.Count; i++)
        {
            if (aims[i] is not { } aim || aim.Over > AimCone.MarginDeg)
            {
                untargeted++;
                continue;
            }

            aimed++;
            bandShots[RangeBand(aim.Range)]++;
            byWeapon.TryGetValue(Name(fired[i].Code), out var w);
            byWeapon[Name(fired[i].Code)] = (w.Hits, w.Shots + 1);
        }

        var firing = new HashSet<int>(fired.Select(f => f.Code));
        int rated = 0;
        int[] coneShots = new int[AimCone.SensitivityMarginsDeg.Length], coneHits = new int[coneShots.Length];
        for (int m = 0; m < coneShots.Length; m++)
            coneShots[m] = aims.Count(a => a is { } x && x.Over <= AimCone.SensitivityMarginsDeg[m]);
        foreach (AiEvent hit in hits)
        {
            // A weapon that fired no shot (a crowbar's swing is no Fired): on its own line, in no rate.
            if (!firing.Contains(hit.Code))
            {
                byWeapon.TryGetValue(Name(hit.Code), out var w);
                byWeapon[Name(hit.Code)] = (w.Hits + 1, w.Shots);
                continue;
            }

            int shot = ShotOf(fired, hit);
            if (shot < 0 || aims[shot] is not { } aim)
                continue;
            for (int m = 0; m < coneHits.Length; m++)
            {
                if (aim.Over <= AimCone.SensitivityMarginsDeg[m])
                    coneHits[m]++;
            }

            if (aim.Over > AimCone.MarginDeg)
                continue;
            rated++;
            bandHits[RangeBand(aim.Range)]++;
            byWeapon.TryGetValue(Name(hit.Code), out var had);
            byWeapon[Name(hit.Code)] = (had.Hits + 1, had.Shots);
        }

        return new HitRates(new HitLine("overall", rated, aimed),
                            [.. byWeapon.Select(kv => new HitLine(kv.Key, kv.Value.Hits, kv.Value.Shots))],
                            [.. RangeBands.Select((band, i) => new HitLine(band, bandHits[i], bandShots[i]))],
                            untargeted,
                            [.. AimCone.SensitivityMarginsDeg.Select((margin, i) => new HitLine(string.Create(CultureInfo.InvariantCulture, $"+{margin:F0}\u00b0"), coneHits[i], coneShots[i]))]);
    }

    /// <summary>The shot a hit came from: the same weapon's at its tick, or its last before within a projectile's flight; or -1.</summary>
    private static int ShotOf(IReadOnlyList<AiEvent> fired, in AiEvent hit)
    {
        int found = -1;
        for (int i = 0; i < fired.Count; i++)
        {
            int t = fired[i].Tick.Value;
            if (t > hit.Tick.Value)
                break;
            if (fired[i].Code == hit.Code && hit.Tick.Value - t <= ProjectileTicks)
                found = i;
        }

        return found;
    }

    /// <summary>
    /// The sighted actor nearest the crosshair when the shot was fired, by how
    /// far past its body's angular half-width the crosshair was (see
    /// <see cref="AimCone"/>) and its distance; or null with nobody sighted.
    /// </summary>
    private static (double Over, float Range)? ShotAim(ActorId actor, int tick, IReadOnlyList<AiUserCmd> commands,
                                                       IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>> frames, IReadOnlyList<AiEvent> sights)
    {
        AiUserCmd? view = null;
        foreach (AiUserCmd c in commands)
        {
            if (c.Tick.Value > tick)
                break;
            view = c;
        }

        if (view is not { } v || FrameAt(frames, actor, tick) is not { } me)
            return null;
        var seen = new Dictionary<ActorId, bool>();
        foreach (AiEvent e in sights)
        {
            if (e.Bot == actor && e.Tick.Value <= tick)
                seen[e.Other] = e.Kind == AiEventKind.SightGained;
        }

        (double Over, float Range)? best = null;
        foreach ((ActorId target, bool sighted) in seen)
        {
            if (!sighted || FrameAt(frames, target, tick) is not { } them)
                continue;
            float range = me.Eye.DistanceTo(them.Origin + (them.Eye - them.Origin) * 0.5f);
            double over = AngleTo(v, actor, target, frames) - AimCone.HalfWidthDeg(range);
            if (best is not { } b || over < b.Over)
                best = (over, range);
        }

        return best;
    }

    /// <summary>
    /// Whether a step is a turn of a living actor: the actor has a frame at
    /// both commands' ticks and the tick before the first (frames are written
    /// only while it lives, and a command run in the tick it respawns still
    /// carries its dead view, so a death's and a respawn's views are no turn),
    /// and its body did not jump between them (a teleport).
    /// </summary>
    private static bool Living(IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>> frames, ActorId actor, in AiUserCmd a, in AiUserCmd b)
    {
        SortedList<int, AiActorFrame> at = frames[actor];
        return at.ContainsKey(a.Tick.Value - 1) && at.TryGetValue(a.Tick.Value, out AiActorFrame fa) && at.TryGetValue(b.Tick.Value, out AiActorFrame fb)
               && fa.Origin.DistanceTo(fb.Origin) <= TeleportUnitsPerTick * Math.Max(1, b.Tick.Value - a.Tick.Value);
    }

    /// <summary>Faster than any fall (3,500 u/s is 52 a tick): a body that moved more in a tick was moved, not run.</summary>
    public const float TeleportUnitsPerTick = 64;

    private static int Ticks(double ms) => (int)Math.Ceiling(ms / 1000 / TickSeconds);

    /// <summary>The yaw (wrapped to ±180) and pitch steps from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static (double Yaw, double Pitch) Delta(AiAngles a, AiAngles b)
    {
        double dy = b.Yaw - a.Yaw;
        dy -= 360 * Math.Round(dy / 360);
        return (dy, b.Pitch - a.Pitch);
    }

    /// <summary>The angle between the command's view and the nearest point of the target's body, or NaN without frames.</summary>
    private static double AngleTo(in AiUserCmd c, ActorId actor, ActorId target, IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>> frames)
    {
        if (FrameAt(frames, actor, c.Tick.Value) is not { } me || FrameAt(frames, target, c.Tick.Value) is not { } them)
            return double.NaN;
        return AimCone.AngleToBody(c.View, me.Eye, them.Origin, them.Eye);
    }

    /// <summary>The actor's frame at the tick, or its last within four ticks before it.</summary>
    private static AiActorFrame? FrameAt(IReadOnlyDictionary<ActorId, SortedList<int, AiActorFrame>> frames, ActorId actor, int tick)
    {
        if (!frames.TryGetValue(actor, out SortedList<int, AiActorFrame>? at))
            return null;
        for (int t = tick; t >= tick - 4; t--)
        {
            if (at.TryGetValue(t, out AiActorFrame f))
                return f;
        }

        return null;
    }

    /// <summary>The q-quantile of <paramref name="values"/> (nearest rank), or NaN for none.</summary>
    public static double Quantile(IReadOnlyCollection<double> values, double q)
    {
        if (values.Count == 0)
            return double.NaN;
        double[] sorted = [.. values.Order()];
        return sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    /// <summary>The table <c>ai-replay metrics</c> prints: one row per actor.</summary>
    /// <summary>
    /// How an actor ducks (§5.4, ruling 2026-11-02): the ticks holding fire, those ducked, and how
    /// long each duck was held. A run of IN_DUCK that was in the air with IN_JUMP pressed is a
    /// crouch jump, no crouch; a command gap ends a run.
    /// </summary>
    public static (int Shooting, int Ducked, double[] HoldsMs) Ducking(IReadOnlyList<AiUserCmd> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        const int Attack = 1, Duck = 4, Jump = 2;
        int shooting = 0, ducked = 0, runStart = -1;
        bool jumped = false;
        var holds = new List<double>();
        for (int i = 0; i <= commands.Count; i++)
        {
            bool down = i < commands.Count && (commands[i].Buttons & Duck) != 0;
            bool gap = i > 0 && i < commands.Count && commands[i].TickCount - commands[i - 1].TickCount > 8;
            if (runStart >= 0 && (!down || gap))
            {
                if (!jumped)
                    holds.Add((commands[i - 1].TickCount - commands[runStart].TickCount + 1) * TickSeconds * 1000);
                runStart = -1;
                jumped = false;
            }

            if (i == commands.Count)
                break;
            if (down && runStart < 0)
                runStart = i;
            jumped |= down && (commands[i].Buttons & Jump) != 0;
            if ((commands[i].Buttons & Attack) != 0)
            {
                shooting++;
                ducked += down ? 1 : 0;
            }
        }

        return (shooting, ducked, [.. holds]);
    }

    public static string Table(IReadOnlyList<ActorMetrics> actors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("actor                 kind   cmds    min | ang.speed °/s med  p90  max | flicks /min peak med  p90 | acquire n/sights med ms | reaction n med ms | strafe n med ms | reversals/min");
        foreach (ActorMetrics m in actors)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{$"{m.Actor.Slot}#{m.Actor.Serial}",-21} {(m.Bot ? "bot" : "human"),-5} {m.Commands,6} {m.Minutes,6:F2} | ");
            sb.Append(CultureInfo.InvariantCulture, $"{F(Quantile(m.AngularSpeedDegPerSec, 0.5)),17} {F(Quantile(m.AngularSpeedDegPerSec, 0.9)),4} {F(Quantile(m.AngularSpeedDegPerSec, 1)),4} | ");
            sb.Append(CultureInfo.InvariantCulture, $"{m.FlickPeaksDegPerSec.Length,6} {(m.Minutes > 0 ? m.FlickPeaksDegPerSec.Length / m.Minutes : 0),4:F1} {F(Quantile(m.FlickPeaksDegPerSec, 0.5)),8} {F(Quantile(m.FlickPeaksDegPerSec, 0.9)),4} | ");
            sb.Append(CultureInfo.InvariantCulture, $"{$"{m.TimeToAcquireMs.Length}/{m.SightsGained}",9} {F(Quantile(m.TimeToAcquireMs, 0.5)),9} | ");
            sb.Append(CultureInfo.InvariantCulture, $"{m.ReactionMs.Length,10} {F(Quantile(m.ReactionMs, 0.5)),6} | ");
            sb.Append(CultureInfo.InvariantCulture, $"{m.StrafeIntervalMs.Length,8} {F(Quantile(m.StrafeIntervalMs, 0.5)),6} | ");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{m.ReversalsPerMinute,13:F1}"));
        }

        sb.AppendLine("ducking: the ticks holding fire, the share of them ducked; holds of duck (a crouch jump is none), n, median, p90 ms");
        foreach (ActorMetrics m in actors)
        {
            double[] holds = m.DuckHoldsMs ?? [];
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{$"{m.Actor.Slot}#{m.Actor.Serial}",-21} {(m.Bot ? "bot" : "human"),-5} {m.ShootingTicks,6} {(double.IsNaN(m.DuckedShootingShare) ? "-" : $"{m.DuckedShootingShare * 100:F1}%"),6} | {holds.Length,4} {F(Quantile(holds, 0.5)),6} {F(Quantile(holds, 0.9)),6}"));
        }

        sb.AppendLine("hit rate: hits/shots aimed at someone (a shot is one trigger pull with a sighted actor within the cone, a hit one victim in one tick)");
        foreach (ActorMetrics m in actors)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{$"{m.Actor.Slot}#{m.Actor.Serial}",-21} {(m.Bot ? "bot" : "human"),-5} {Rate(m.Hits.Overall)} | ");
            sb.Append(string.Join(", ", m.Hits.ByWeapon.Select(Rate)));
            sb.Append(" | ");
            sb.Append(string.Join(", ", m.Hits.ByRange.Select(Rate)));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $", no target {m.Hits.Untargeted} shots"));
            sb.AppendLine("  by cone (the body's half-width plus): " + string.Join(", ", m.Hits.ByCone.Select(Rate)));
        }

        return sb.ToString();
    }

    private static string Rate(HitLine l)
        => string.Create(CultureInfo.InvariantCulture, $"{l.Label} {l.Hits}/{l.Shots} {(double.IsNaN(l.Rate) ? "-" : $"{l.Rate * 100:F1}%")}");

    private static string F(double v) => double.IsNaN(v) ? "-" : v.ToString("F0", CultureInfo.InvariantCulture);
}
