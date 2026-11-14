using SourceSharp.Ai;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Ai.Roster;

/// <summary>A roster row and the serial it had when the id was taken: a row reused for another robot changes its serial, so an old id goes stale.</summary>
public readonly record struct RobotId(int Index, int Serial)
{
    public static RobotId None => default;

    public bool IsValid => Serial != 0;

    public override string ToString() => IsValid ? $"robot {Index}#{Serial}" : "no robot";
}

/// <summary>The three states of a robot (plan_descent_logic 3.1), and the end of one.</summary>
public enum RobotState : byte
{
    /// <summary>A row no robot holds.</summary>
    Empty,
    /// <summary>Its room is asleep. A row, no tick.</summary>
    Asleep,
    /// <summary>Its room is awake or its pack alerted, and it has no body: the far simulation moves it.</summary>
    Far,
    /// <summary>It holds a body from the pool.</summary>
    Embodied,
    /// <summary>Killed. It holds a body only until the body is released.</summary>
    Dead,
}

/// <summary>How alert a robot or its pack is.</summary>
public enum AlertLevel : byte
{
    Calm,
    Suspicious,
    Alerted,
}

/// <summary>What the far simulation had a far robot do on its last tick.</summary>
public enum FarActivity : byte
{
    /// <summary>Stood still: at its only marker, stunned, unable to route, or in a room a hero can see into.</summary>
    Hold,
    /// <summary>Walked between its room's markers, or waited at one.</summary>
    Patrol,
    /// <summary>Walked back towards its home room.</summary>
    Return,
    /// <summary>Walked towards its pack's last contact.</summary>
    Converge,
    /// <summary>Waited at a door because a hero can see into the room beyond it.</summary>
    Queue,
}

/// <summary>
/// What a robot's brain keeps across a demotion (plan_descent_logic 3.3): its target (a hero
/// seat, -1 for none), where and when it last knew that target to be, its alert level, and the
/// top entry of its threat table (a seat, -1 for none, and the amount).
/// </summary>
public readonly record struct CarriedMemory(
    int TargetSeat,
    AiVector LastKnownPosition,
    long LastKnownTick,
    AlertLevel Alert,
    int ThreatSeat,
    float Threat)
{
    public static CarriedMemory None => new(-1, default, 0, AlertLevel.Calm, -1, 0);
}

/// <summary>
/// A robot's live state as a body holds it: where it is, its life, statuses and clip, and the
/// memory it carries. A promotion hands the body the row's; a demotion writes the body's back
/// to the row. <see cref="Clip"/> -1 is "a full magazine of whatever the body carries".
/// </summary>
public readonly record struct RobotBodyState(
    int Room,
    AiVector Position,
    float Yaw,
    double Life,
    RobotStatuses Statuses,
    int Clip,
    CarriedMemory Carried);

/// <summary>A hero as the horde sees it: its seat, the room it stands in, and where.</summary>
public readonly record struct HeroPlace(int Seat, int Room, AiVector Position);

/// <summary>
/// What the heroes can see, answered by the caller (the server, with the potentially visible
/// set and a trace; facts with a fake). The horde asks it and never decides it.
/// </summary>
public interface IHeroSight
{
    /// <summary>Whether any hero can see <paramref name="point"/>.</summary>
    bool CanSee(AiVector point);

    /// <summary>Whether any hero can see into <paramref name="room"/>.</summary>
    bool CanSeeInto(int room);
}
