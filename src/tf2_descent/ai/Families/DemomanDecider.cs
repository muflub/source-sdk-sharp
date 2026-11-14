using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The grenade demoman (plan_descent_logic HO12; stock's demoman bot as the reference): it keeps
/// its distance and lobs.
///
/// <list type="number">
/// <item><b>The band</b> is its family's range times its <c>preferredRangeScale</c>
/// (Aggression). Past its outer edge, or where no arc reaches the hero, it closes along its
/// route; nearer than its <c>crowdedShare</c> (Nerve) it backs straight away; inside it holds, or
/// strafes with the chance its <c>strafeWeight</c> gives (Agility).</item>
/// <item><b>The lob</b> (<see cref="Pitch"/>): the hero's feet, led by where the hero will be
/// when the pipe lands times its <c>leadQuality</c> (Accuracy); the low arc that reaches that
/// point at the rules' pipe speed under the rules' gravity, solved from the robot's eye; the look
/// goes along the arc's first line, thrown off by its <c>aimErrorDeg</c> (Accuracy).</item>
/// <item><b>Fire</b> inside the band's outer edge times its <c>fireRangeGate</c> (Discipline),
/// aimed within <see cref="FamilyNumbers.FireToleranceDeg"/>, when the line of fire the view
/// carries runs clear along the arc's first line until the arc has risen to its top (a lob
/// through a doorway whose lintel stands in that line would hit the wall above the door), and no
/// pack-mate stands in it.</item>
/// <item>No hero in sight: the hunt.</item>
/// </list>
/// </summary>
public sealed class DemomanDecider : FamilyDecider
{
    /// <summary>The band's outer edge, a share of the band.</summary>
    public const float BandOuter = 1.15f;

    private readonly float _crowded;

    public DemomanDecider(BrainSetup setup, HordeRoutes routes, FamilyNumbers numbers) : base(setup, routes, numbers) =>
        _crowded = HordeTemperament.At(numbers.Curves, "crowdedShare", HordeTemperament.Nerve(numbers.Curves, setup.Params));

    /// <summary>Where the last decide aimed (the arc's first line, before the aim error), and the point it lobbed at.</summary>
    public AiVector LastAim { get; private set; }

    public AiVector LastTarget { get; private set; }

    /// <summary>
    /// The low arc's launch pitch, engine degrees (positive down), that takes a projectile fired at
    /// <paramref name="speed"/> under <paramref name="gravity"/> from <paramref name="from"/> to
    /// <paramref name="to"/>; false when no arc reaches it.
    /// </summary>
    public static bool Pitch(AiVector from, AiVector to, float speed, float gravity, out float pitch)
    {
        pitch = 0f;
        float x = (to - from).Length2D, h = to.Z - from.Z;
        if (x < 1f || speed <= 0f)
            return false;
        if (gravity <= 0f)
        {
            pitch = -MathF.Atan2(h, x) * (180f / MathF.PI);
            return true;
        }

        float v2 = speed * speed;
        float disc = v2 * v2 - gravity * (gravity * x * x + 2f * h * v2);
        if (disc < 0f)
            return false;
        float tan = (v2 - MathF.Sqrt(disc)) / (gravity * x);
        pitch = -MathF.Atan(tan) * (180f / MathF.PI);
        return true;
    }

    /// <summary>How long a lob at <paramref name="pitch"/> takes to cover <paramref name="x"/> on the floor.</summary>
    public static float Flight(float x, float pitch, float speed) => x / MathF.Max(1f, speed * MathF.Cos(pitch * (MathF.PI / 180f)));

    protected override IntentCommand Decide(Tick tick, PerceivedView view, int seq, AiVector self)
    {
        if (!NearestHero(view, self, out ActorFacts hero))
            return Hunt(tick, view, seq);

        DecisionParams p = Params;
        float band = HordeWeapons.EngageRange(HordeWeapons.GrenadeLauncher) * p.PreferredRangeScale;
        float d = self.DistanceTo(hero.Origin);
        AiVector eye = EyeOf(self);
        float speed = Numbers.PipeSpeed, g = Numbers.PipeGravity;

        // The lead: where the hero will be when the pipe lands, twice refined.
        AiVector feet = hero.Origin + new AiVector(0f, 0f, FamilyNumbers.FeetUnits), target = feet;
        bool reaches = Pitch(eye, target, speed, g, out float pitch);
        for (int k = 0; k < 2 && reaches; k++)
        {
            target = feet + hero.Velocity * (Flight((target - eye).Length2D, pitch, speed) * p.LeadQuality);
            reaches = Pitch(eye, target, speed, g, out pitch);
        }

        LastTarget = target;
        float yaw = YawOf(target - eye), x = (target - eye).Length2D;
        AiVector first = reaches ? Along(eye, pitch, yaw, MathF.Max(64f, (target - eye).Length)) : Chest(hero);
        LastAim = first;
        AiVector aim = Err(tick, eye, first, p.AimErrorDeg);
        var look = new LookIntent(LookKind.Point, aim, hero.Id, AimPoint.Feet, 0f, 0);

        bool inRange = reaches && x <= band * BandOuter * p.FireRangeGate;
        bool aimed = HordeView.AimOff(view.Self.Eye, eye, aim) <= FamilyNumbers.FireToleranceDeg;
        bool clear = reaches && LobClear(view, x, pitch, speed, g);
        bool mate = MateInLine(view, eye, first);
        bool fire = inRange && aimed && clear && !mate;
        var action = fire ? FireAction : ActionIntent.None;
        string verdict = fire ? "fire" : !inRange ? "hold: out of reach" : !aimed ? "hold: off aim" : !clear ? "hold: lintel in the arc" : "hold: mate in line";

        AiVector away = Flat(self - hero.Origin);
        if (d < band * _crowded)
            return End(tick, seq, FamilyNode.BackOff, Along(MoveKind.Retreat, away), look, action, hero.Origin, hero.Id, verdict);
        if (d > band * BandOuter || !reaches)
            return End(tick, seq, FamilyNode.Close, Route.Follow(tick, view.Self, self, hero.Origin, MoveSpeed.Run), look, action, hero.Origin, hero.Id, verdict);
        if (Strafe(tick, out float side))
            return End(tick, seq, FamilyNode.Strafe, Along(MoveKind.Strafe, new AiVector(-away.Y * side, away.X * side, 0f)), look, action, hero.Origin, hero.Id, verdict);
        return End(tick, seq, FamilyNode.Lob, MoveIntent.Hold, look, action, hero.Origin, hero.Id, verdict);
    }

    /// <summary>
    /// Whether the line of fire runs clear along the arc's first line as far as the arc rises: to
    /// the top of the arc (or the target, if nearer), at least <see cref="FamilyNumbers.LobLeastClearUnits"/>,
    /// less <see cref="FamilyNumbers.LobClearSlackUnits"/>. Past its top the arc falls away under
    /// the line, so what the line meets beyond it the pipe passes below.
    /// </summary>
    private static bool LobClear(PerceivedView view, float x, float pitch, float speed, float gravity)
    {
        int line = view.Self.Reserve;
        if (line == HordeView.NotTraced)
            return true;
        float up = -pitch * (MathF.PI / 180f), cos = MathF.Cos(up);
        float topX = up > 0f && gravity > 0f ? speed * speed * MathF.Sin(up) * cos / gravity : 0f;
        float along = MathF.Min(x, MathF.Max(topX, FamilyNumbers.LobLeastClearUnits)) / MathF.Max(0.05f, cos);
        return line >= along - FamilyNumbers.LobClearSlackUnits;
    }
}
