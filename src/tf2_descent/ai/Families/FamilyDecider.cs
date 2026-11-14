using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>The family deciders' nodes: the hunt's eight (<see cref="HuntDecider.Node"/>, in its order), then the fights'.</summary>
public enum FamilyNode : short
{
    Hunt,
    Hold,
    Chase,
    Engage,
    LastKnown,
    Search,
    Investigate,
    Home,

    /// <summary>A rusher running to its approach point beside its target.</summary>
    Rush,

    /// <summary>A rusher at its target: on it, swinging.</summary>
    Swing,

    /// <summary>A soldier too far: closing to its band.</summary>
    Close,

    /// <summary>A soldier in its band, holding.</summary>
    Band,

    /// <summary>A soldier in its band, strafing.</summary>
    Strafe,

    /// <summary>A soldier with a hero in its face: backing off.</summary>
    BackOff,

    /// <summary>A medic beaming its patient.</summary>
    Heal,

    /// <summary>A medic going to its patient, or keeping by its pack.</summary>
    Follow,

    /// <summary>A medic running from a hero.</summary>
    Flee,

    /// <summary>A scout in its band, circling its hero and firing.</summary>
    Circle,

    /// <summary>A pyro on its hero, flame on.</summary>
    Burn,

    /// <summary>A demoman in its band, lobbing.</summary>
    Lob,

    /// <summary>A heavy holding its ground, gun turning onto its hero.</summary>
    Suppress,

    /// <summary>A sniper holding, aiming and charging its shot.</summary>
    Aim,

    /// <summary>A sniper rushed: running for room.</summary>
    Relocate,

    /// <summary>A spy going to a point behind its hero, cloaked.</summary>
    Stalk,

    /// <summary>A spy near its hero but in front of it: going round to its back.</summary>
    Flank,

    /// <summary>A spy at its hero, knife out.</summary>
    Stab,

    /// <summary>A sentry buster running at its hero.</summary>
    Run,

    /// <summary>A sentry buster at its hero: spinning up its blast.</summary>
    Detonate,

    /// <summary>A boss asking its special: the windup the heroes see and hear before it lands (HO13).</summary>
    Telegraph,

    /// <summary>A boss calling its phase's adds into its arena (HO13).</summary>
    Summon,

    /// <summary>A boss held inside its arena: its fight would have taken it out (HO13).</summary>
    Arena,

    /// <summary>The Tank rolling along its track to its next station (HO13).</summary>
    Track,

    /// <summary>The Tank stopped at a station, drilling (HO13).</summary>
    Drill,
}

/// <summary>
/// What the three family deciders share (HO6): a hunt for when no hero is in sight (the robot
/// goes on hunting, searching and walking home exactly as <see cref="HuntDecider"/> does), the
/// view's heroes and pack-mates (<see cref="HordeView"/>), a seeded stream a decide, the trace
/// and the doing record, and the intent. A family reads only what the core's recording writes.
/// </summary>
public abstract class FamilyDecider : IDecider, IHordeRouted
{
    private readonly HuntDecider _hunt;
    private DoingRecord _doing;

    protected FamilyDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers)
    {
        Setup = setup;
        Level = routes.Level;
        Numbers = numbers;
        Route = routes.NewRoute(setup.Bot.Slot);
        _hunt = new HuntDecider(setup, Route);
    }

    protected BrainSetup Setup { get; }

    protected HordeLevel Level { get; }

    /// <summary>The brain's way to its goals (the hunt's too): the level graph's paths, or the room route.</summary>
    public HordeRoute Route { get; }

    protected FamilyNumbers Numbers { get; }

    protected DecisionParams Params => Setup.Params;

    public BtTrace Trace { get; } = new();

    public DoingRecord Doing => _doing;

    /// <summary>The node names a recording's tree record names: the hunt's, then the fights'.</summary>
    public static ImmutableArray<string> Names => [.. Enum.GetValues<FamilyNode>().Select(NameOf)];

    /// <summary>Each node's branch: the hunt's own, then one a fight.</summary>
    public static ImmutableArray<int> Branches => [.. HuntDecider.Branches, 6, 6, 7, 7, 7, 7, 8, 8, 8, 9, 10, 11, 12, 13, 13, 14, 14, 14, 15, 15, 16, 16, 16, 17, 17];

    public IReadOnlyList<string> NodeNames => Names;

    public IReadOnlyList<int> NodeBranches => Branches;

    /// <summary>The node the last decide ended in.</summary>
    public FamilyNode Last { get; private set; }

    public static string NameOf(FamilyNode node) => node <= FamilyNode.Home
        ? HuntDecider.NameOf((HuntDecider.Node)node)
        : node switch
        {
            FamilyNode.Rush => "rush",
            FamilyNode.Swing => "swing",
            FamilyNode.Close => "close",
            FamilyNode.Band => "band",
            FamilyNode.Strafe => "strafe",
            FamilyNode.BackOff => "back-off",
            FamilyNode.Heal => "heal",
            FamilyNode.Follow => "follow",
            FamilyNode.Flee => "flee",
            FamilyNode.Circle => "circle",
            FamilyNode.Burn => "burn",
            FamilyNode.Lob => "lob",
            FamilyNode.Suppress => "suppress",
            FamilyNode.Aim => "aim",
            FamilyNode.Relocate => "relocate",
            FamilyNode.Stalk => "stalk",
            FamilyNode.Flank => "flank",
            FamilyNode.Stab => "stab",
            FamilyNode.Run => "run",
            FamilyNode.Telegraph => "telegraph",
            FamilyNode.Summon => "summon",
            FamilyNode.Arena => "arena",
            FamilyNode.Track => "track",
            FamilyNode.Drill => "drill",
            _ => "detonate",
        };

    public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
    {
        ArgumentNullException.ThrowIfNull(view);
        Trace.Begin(tick);
        // A hero in sight is the fight: what the robot heard and took before it is that fight (the
        // hunt's own sighted branch says so when it decides; a family fights without asking it), so
        // once the fight ends, with the hero dead or gone, it is not a lead to go and search.
        if (NearestHero(view, view.Self.Origin, out _))
            _hunt.Fought(tick);
        return Decide(tick, view, seq, view.Self.Origin);
    }

    /// <summary>The family's decide; <see cref="Hunt"/> for what it does with no fight of its own.</summary>
    protected abstract IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self);

    /// <summary>The hunt's decide (no hero in sight: last known, investigate, home), as this decider's node.</summary>
    protected IntentCommand Hunt(Tick tick, PerceivedView view, int seq)
    {
        var intent = _hunt.Decide(tick, view, seq);
        Mark(tick, (FamilyNode)_hunt.Last, _hunt.Doing.Goal, _hunt.Doing.Target);
        return intent;
    }

    /// <summary>The nearest hero in sight, or none.</summary>
    protected static bool NearestHero(PerceivedView view, AiVector self, out ActorFacts hero)
    {
        hero = default;
        float best = float.MaxValue;
        foreach (ref readonly ActorFacts f in view.Sighted)
        {
            if (!HordeView.IsHero(f))
                continue;
            float d = self.DistanceTo(f.Origin);
            if (d < best)
                (hero, best) = (f, d);
        }

        return best < float.MaxValue;
    }

    /// <summary>The robot's eye: its feet raised by the brains' eye height (scale 1; a family reads no scale).</summary>
    protected static AiVector EyeOf(AiVector feet) => feet + new AiVector(0f, 0f, HordeBrains.EyeHeight);

    /// <summary>A value in [0, 1) from the brain's seed, the tick and <paramref name="salt"/>: the same in every replay.</summary>
    protected float Roll(Tick tick, int salt)
    {
        ulong z = Setup.Seed ^ ((ulong)(uint)tick.Value * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)salt * 0xC2B2AE3D27D4EB4FUL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 40) / (float)(1UL << 24);
    }

    /// <summary>How long one draw of a brain's aim error holds.</summary>
    public const float AimErrorHoldSeconds = 0.6f;

    /// <summary><paramref name="point"/> moved off its line from <paramref name="eye"/> by up to <paramref name="degrees"/>, in a seeded direction: a brain's aim error.</summary>
    protected AiVector Err(Tick tick, AiVector eye, AiVector point, float degrees)
    {
        if (degrees <= 0f)
            return point;
        AiVector to = point - eye;
        float d = to.Length;
        if (d < 1f)
            return point;
        AiVector f = to * (1f / d);
        AiVector up = MathF.Abs(f.Z) < 0.99f ? new AiVector(0f, 0f, 1f) : new AiVector(1f, 0f, 0f);
        AiVector right = Cross(f, up).Normalised();
        AiVector top = Cross(right, f);
        // The error holds for a while (a hand's drift, not a fresh twitch each decide), so a gun can settle on it.
        var held = new Tick(tick.Value / Math.Max(1, (int)MathF.Round(AimErrorHoldSeconds / Setup.TickSeconds)));
        float angle = Roll(held, 101) * MathF.Tau, off = Roll(held, 102) * degrees * (MathF.PI / 180f);
        AiVector dir = f + (right * MathF.Cos(angle) + top * MathF.Sin(angle)) * MathF.Tan(off);
        return eye + dir.Normalised() * d;
    }

    /// <summary>Whether a pack-mate stands within <see cref="FamilyNumbers.MateClearUnits"/> of the line from <paramref name="eye"/> to <paramref name="aim"/>, nearer than the aim.</summary>
    protected static bool MateInLine(PerceivedView view, AiVector eye, AiVector aim)
    {
        AiVector line = aim - eye;
        float len = line.Length;
        if (len < 1f)
            return false;
        AiVector f = line * (1f / len);
        foreach (ref readonly ActorFacts m in view.Sighted)
        {
            if (!HordeView.IsMate(m))
                continue;
            AiVector chest = m.Origin + new AiVector(0f, 0f, FamilyNumbers.ChestUnits);
            float along = (chest - eye).Dot(f);
            if (along <= 0f || along >= len)
                continue;
            AiVector near = eye + f * along;
            if (near.DistanceTo(chest) < FamilyNumbers.MateClearUnits)
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ HO12's shared pieces

    private Tick _flipAt;
    private float _side = 1f;
    private bool _strafing;

    /// <summary>
    /// Whether to strafe now and to which side (+1 left of the hero's line, -1 right): every
    /// <c>strafeMeanSeconds</c> or so (Agility) the robot chooses again, strafing with the chance its
    /// <c>strafeWeight</c> gives (Agility; 1 for a family that always moves, <paramref name="always"/>).
    /// </summary>
    protected bool Strafe(Tick tick, out float side, bool always = false)
    {
        if (tick >= _flipAt)
        {
            _strafing = always || Roll(tick, 7) < Params.StrafeWeight;
            _side = Roll(tick, 8) < 0.5f ? -1f : 1f;
            _flipAt = tick + Math.Max(1, (int)MathF.Ceiling(Params.StrafeMeanSeconds * (0.5f + Roll(tick, 9)) / Setup.TickSeconds));
        }

        side = _side;
        return _strafing;
    }

    /// <summary>Whether the line of fire the view carries runs to within <paramref name="slack"/> of <paramref name="aim"/> (an untraced line counts as clear).</summary>
    protected static bool LineClear(PerceivedView view, AiVector eye, AiVector aim, float slack)
    {
        int line = view.Self.Reserve;
        return line == HordeView.NotTraced || line >= eye.DistanceTo(aim) - slack;
    }

    /// <summary>Whether the robot's life is under its <c>retreatHealthPct</c> (Nerve: a fearless robot fights on lower).</summary>
    protected bool Hurt(PerceivedView view) => HordeView.Life(view.Self) < Params.RetreatHealthPct;

    /// <summary>A sighted actor's chest.</summary>
    protected static AiVector Chest(in ActorFacts f) => f.Origin + new AiVector(0f, 0f, FamilyNumbers.ChestUnits);

    /// <summary>
    /// The centre of the pack-mates in view, and whether the robot stands past its <c>leashUnits</c>
    /// from it (Soloness: a lone robot's leash is long, a pack-bound one's short). False with no mate.
    /// </summary>
    protected bool Leashed(PerceivedView view, AiVector self, out AiVector centre)
    {
        centre = default;
        int n = 0;
        foreach (ref readonly ActorFacts m in view.Sighted)
        {
            if (!HordeView.IsMate(m))
                continue;
            centre += m.Origin;
            n++;
        }

        if (n == 0)
            return false;
        centre *= 1f / n;
        return self.DistanceTo(centre) > Params.LeashUnits;
    }

    /// <summary>The engine yaw of a floor direction, degrees.</summary>
    protected static float YawOf(AiVector d) => MathF.Atan2(d.Y, d.X) * (180f / MathF.PI);

    /// <summary>A point <paramref name="distance"/> from <paramref name="eye"/> along engine angles <paramref name="pitch"/>, <paramref name="yaw"/> (pitch positive down).</summary>
    protected static AiVector Along(AiVector eye, float pitch, float yaw, float distance) =>
        eye + HordeView.Forward(new AiAngles(pitch, yaw, 0f)) * distance;

    /// <summary><paramref name="d"/> turned by <paramref name="degrees"/> about the vertical.</summary>
    protected static AiVector Turned(AiVector d, float degrees)
    {
        float r = degrees * (MathF.PI / 180f), c = MathF.Cos(r), s = MathF.Sin(r);
        return new AiVector(d.X * c - d.Y * s, d.X * s + d.Y * c, d.Z);
    }

    /// <summary>The angle from <paramref name="from"/> to <paramref name="to"/>, degrees, -180 to 180.</summary>
    protected static float Delta(float from, float to)
    {
        float d = (to - from) % 360f;
        return d > 180f ? d - 360f : d < -180f ? d + 360f : d;
    }

    /// <summary>An ability ask: the horde's row (<see cref="HordeAbilities"/>).</summary>
    protected static ActionIntent Ability(int row) => new(ActionKind.Ability, FireStyle.Tap, row, 0);

    /// <summary>The intent, with the decide's node traced and its doing record.</summary>
    protected IntentCommand End(Tick tick, int seq, FamilyNode node, in MoveIntent move, in LookIntent look, in ActionIntent action, AiVector goal, ActorId target,
                                string verdict = "")
    {
        Mark(tick, node, goal, target, verdict);
        return new IntentCommand(Setup.Bot, tick, seq, tick + IntentCommand.LifetimeTicks, move, look, action,
                                 new DecisionReason((short)node, 1f, 0f, 0f));
    }

    private void Mark(Tick tick, FamilyNode node, AiVector goal, ActorId target, string verdict = "")
    {
        Last = node;
        Trace.Add((short)node, BtStatus.Success, 0f);
        Trace.End(new DecisionReason((short)node, 1f, 0f, 0f));
        _doing = new DoingRecord(GetType().Name, NameOf(node), true, goal, target, verdict, 0f, 0f, 0f, 0f, 0f, 0f, tick + IntentCommand.LifetimeTicks);
    }

    /// <summary>A move straight along <paramref name="direction"/> on the floor (a strafe or a retreat).</summary>
    protected static MoveIntent Along(MoveKind kind, AiVector direction) =>
        new(kind, MoveSpeed.Run, 0, default, default, default, default, Flat(direction), false, 0);

    protected static AiVector Flat(AiVector v)
    {
        float l = v.Length2D;
        return l > 1e-4f ? new AiVector(v.X / l, v.Y / l, 0f) : AiVector.Zero;
    }

    protected static AiVector Cross(AiVector a, AiVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    protected static ActionIntent FireAction => new(ActionKind.Fire, FireStyle.Tap, 0, 0);
}
