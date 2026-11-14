using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Motion;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// A push on a body (plan_descent_logic L12, HO3): a velocity change added to
/// its motion between steps, so the body is thrown, lifted and lands by the
/// same movement that walks it. A blast, a knock-up, a shove. A push on a
/// parked or dead body is dropped (false).
/// </summary>
public interface IRobotImpulse
{
    bool Push(int body, AiVector velocityChange);
}

/// <summary>
/// One body's motion as later packages read it (the session's senses, the door
/// shove, the attacks): where it is and how fast it goes, whether it stands on
/// ground, its facing, the order in force (its move, look, expiry and the next
/// corner), its gait, and the two reports: <see cref="Blocked"/> (what stopped
/// it and for how long) and <see cref="Landed"/> (its last landing).
/// </summary>
public readonly record struct RobotMotionView(
    AiVector Position,
    AiVector Velocity,
    bool OnGround,
    float Facing,
    bool Ordered,
    MoveIntent Move,
    LookIntent Look,
    long Expires,
    int Corner,
    MotionGait Gait,
    BlockedReport Blocked,
    LandedReport Landed,
    long AirborneSince);

/// <summary>The motion's reports, by body index. A body with no motion (parked, dead, or never stepped) answers false and empty reports.</summary>
public interface IRobotMotionReports
{
    bool TryView(int body, out RobotMotionView view);

    BlockedReport Blocked(int body);

    LandedReport Landed(int body);
}
