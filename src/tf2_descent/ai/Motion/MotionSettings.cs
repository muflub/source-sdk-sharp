namespace TF2.Descent.Ai.Motion;

/// <summary>
/// The numbers a robot body's motion runs on (plan_descent_logic 3.7, HO3).
/// Data, not logic: a fact or a later content table can hand other values.
///
/// <para>
/// <see cref="ArriveRadius"/>: how near (on the floor plane) a corner counts as
/// reached, at scale 1; a body's own radius grows it by
/// <see cref="ArriveHullShare"/> of its scaled half-width, so a giant consumes
/// a corner as soon as its hull covers it. <see cref="TurnRate"/>: degrees a
/// second the body's facing turns towards where it should face, so a robot
/// never snaps round in a tick. <see cref="BodyYawLimit"/> and
/// <see cref="BodyPitchLimit"/>: how far the torso twists and bends towards a
/// look the facing has not reached (the pose dials' reach).
/// <see cref="WalkFraction"/>: a <c>Walk</c> move's share of the robot's speed.
/// <see cref="RunGaitSpeed"/>: the ground speed above which the body runs
/// rather than stands. <see cref="PoseEpsilon"/>: the smallest pose change
/// written to the net state.
/// </para>
///
/// <para>
/// <b>Blocked.</b> A wishing body whose step covered less than
/// <see cref="BlockedProgress"/> of the distance its speed asks, with something
/// struck on the way, or whose move the accept clipped, is blocked this tick;
/// <see cref="BlockedClearTicks"/> unblocked ticks in a row end the report.
/// </para>
///
/// <para>
/// <b>Arriving.</b> Heading for its order's last corner, a body's speed is
/// held to <see cref="BrakeRate"/> per second of the distance left (TF's floor
/// friction, 4: what friction alone stops it from), never under
/// <see cref="LeastArriveSpeed"/>, so it stops at the corner rather than
/// sliding a hull's length past it. <b>The air gait</b> is played only after
/// <see cref="AirGaitTicks"/> ticks off the ground, so a body that settles
/// onto the floor at a lend or drops a step does not flicker into it.
/// </para>
///
/// <para>
/// <b>Separation.</b> Robots are one team and pass through each other; where
/// two overlap, stock's teammate pushaway pushes them apart, never weaker than
/// <see cref="LeastSeparation"/> units a second of wished move (stock's push
/// fades to nothing at the box's edge, under the floor's stop speed). A
/// robot never blocks another now, and passing one takes a few ticks; a
/// wishing body pushed among robots for <see cref="CrowdTicks"/> ticks in a
/// row (half a second: longer than passing one) reports the nearest robot it
/// overlaps as its blocker (<see cref="BlockerKind.Robot"/>: held up in a
/// crowd, for the brain's Stuck).
/// </para>
/// </summary>
public sealed record MotionSettings(
    float ArriveRadius,
    float ArriveHullShare,
    float TurnRate,
    float BodyYawLimit,
    float BodyPitchLimit,
    float WalkFraction,
    float RunGaitSpeed,
    float PoseEpsilon,
    float BlockedProgress,
    int BlockedClearTicks,
    float BrakeRate,
    float LeastArriveSpeed,
    int AirGaitTicks,
    float LeastSeparation,
    int CrowdTicks)
{
    /// <summary>The defaults HO3 chose (overrule by handing others).</summary>
    public static MotionSettings Default => new(
        ArriveRadius: 16f,
        ArriveHullShare: 0.5f,
        TurnRate: 360f,
        BodyYawLimit: 45f,
        BodyPitchLimit: 45f,
        WalkFraction: 0.5f,
        RunGaitSpeed: 20f,
        PoseEpsilon: 0.01f,
        BlockedProgress: 0.5f,
        BlockedClearTicks: 3,
        BrakeRate: 4f,
        LeastArriveSpeed: 40f,
        AirGaitTicks: 3,
        LeastSeparation: 120f,
        CrowdTicks: 33);
}
