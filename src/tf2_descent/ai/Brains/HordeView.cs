using SourceSharp.Ai;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// What a horde brain's view carries beyond the core's meaning of its fields
/// (plan_descent_logic 3.6, HO6), every value in a field the core's recording
/// writes, so a decider that reads them replays byte for byte:
///
/// <list type="bullet">
/// <item><b>Its gun's look</b>: <see cref="SelfState.Eye"/> is the body's aim, the
/// facing plus the torso's twist and the torso's bend (the motion's dials: where
/// a shot would go now), pitch positive down as the engine's. A session with no
/// aim seam writes the facing and a level pitch.</item>
/// <item><b>Its life</b>: <see cref="SelfState.Health"/> is its life rounded up,
/// <see cref="SelfState.Armour"/> its most life (a robot wears no armour).</item>
/// <item><b>Its line of fire</b>: <see cref="SelfState.Reserve"/> is how far a shot
/// along that look goes before it meets the world, a prop, a body or a hero
/// (<see cref="FireLineReach"/> at most; a robot carries no reserve), traced on the
/// ticks the brain decides; -1 when it was not traced.</item>
/// <item><b>Its pack</b>: the pack-mates holding bodies, the nearest
/// <see cref="PackMates"/>, as sighted actors on the robots' team
/// (<see cref="HordeBrains.RobotTeam"/>; a hero is on the heroes'), each with its
/// family and its life's share packed into <see cref="ActorFacts.WeaponSeen"/>
/// (<see cref="Mate"/>): what a pack's board tells its members (plan_tf2_rpg B.4,
/// the board medics read), not what its eyes see.</item>
/// <item><b>Its navigation</b>: <see cref="SelfState.AltAmmo"/> is the level graph's epoch once
/// the server has it ready (<see cref="IHordePaths.Epoch"/>; 0 before: the room route), and
/// <see cref="SelfState.Grenades"/> the clearance class the robot plans by
/// (<see cref="HordeHulls.ClassOf"/> of its scale; a robot has no second trigger and carries no
/// grenades). The core's recording writes both, so a decider that plans on the graph replays.</item>
/// </list>
/// </summary>
public static class HordeView
{
    /// <summary>The level graph's epoch the view was published under, 0 for none ready.</summary>
    public static int NavEpoch(in SelfState self) => self.AltAmmo;

    /// <summary>The clearance class the robot plans its paths by.</summary>
    public static ClearanceClass NavClass(in SelfState self) =>
        self.Grenades is >= 0 and <= (int)ClearanceClass.Hull7 ? (ClearanceClass)self.Grenades : ClearanceClass.Stand;

    /// <summary>Pack-mates a view holds at most, nearest first.</summary>
    public const int PackMates = 7;

    /// <summary>How far a line of fire is traced.</summary>
    public const float FireLineReach = 4096f;

    /// <summary>A line of fire that was not traced this tick.</summary>
    public const int NotTraced = -1;

    private const int FamilyBits = 8, FamilyMask = (1 << FamilyBits) - 1;

    /// <summary>A pack-mate's <see cref="ActorFacts.WeaponSeen"/>: its family in the low byte, its life in thousandths above.</summary>
    public static int Mate(int family, double lifeShare) =>
        (family & FamilyMask) | ((int)Math.Round(Math.Clamp(lifeShare, 0, 1) * 1000, MidpointRounding.AwayFromZero) << FamilyBits);

    /// <summary>Whether a sighted actor is a pack-mate (a robot), not a hero.</summary>
    public static bool IsMate(in ActorFacts f) => f.Team == HordeBrains.RobotTeam;

    /// <summary>Whether a sighted actor is a hero.</summary>
    public static bool IsHero(in ActorFacts f) => f.Team == HordeBrains.HeroTeam;

    /// <summary>A pack-mate's family (<see cref="HordeWeapons"/>).</summary>
    public static int MateFamily(in ActorFacts f) => f.WeaponSeen & FamilyMask;

    /// <summary>A pack-mate's life as a share of its most, 0 to 1.</summary>
    public static float MateLife(in ActorFacts f) => (f.WeaponSeen >> FamilyBits) / 1000f;

    /// <summary>The robot's own life as a share of its most, 0 to 1 (1 when the view carries no most).</summary>
    public static float Life(in SelfState self) => self.Armour > 0 ? Math.Clamp((float)self.Health / self.Armour, 0f, 1f) : 1f;

    /// <summary>The unit vector of a look at <paramref name="eye"/> (engine angles: pitch positive down).</summary>
    public static AiVector Forward(AiAngles eye)
    {
        float p = eye.Pitch * (MathF.PI / 180f), y = eye.Yaw * (MathF.PI / 180f);
        return new AiVector(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
    }

    /// <summary>The angle in degrees between the robot's look and the direction to <paramref name="point"/> from <paramref name="eye"/>.</summary>
    public static float AimOff(AiAngles look, AiVector eye, AiVector point)
    {
        AiVector to = point - eye;
        float l = to.Length;
        if (l < 1e-3f)
            return 0f;
        float dot = Math.Clamp(Forward(look).Dot(to * (1f / l)), -1f, 1f);
        return MathF.Acos(dot) * (180f / MathF.PI);
    }
}

/// <summary>
/// The bodies' guns as the brains' views carry them (<see cref="HordeView"/>):
/// where a body's gun looks now, and how far a line along that look runs before
/// something stops it. The server answers from the body and an engine trace; a
/// fact answers its own.
/// </summary>
public interface IHordeBodyAim
{
    /// <summary>The body's aim: its facing plus its torso's twist and bend, engine angles (pitch positive down).</summary>
    AiAngles Aim(int body);

    /// <summary>How far a shot from the body's eye along its aim goes before it hits anything, at most <paramref name="reach"/>.</summary>
    float LineOfFire(int body, float reach);
}
