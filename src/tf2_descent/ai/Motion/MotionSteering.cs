using SourceSharp.Ai;

namespace TF2.Descent.Ai.Motion;

/// <summary>
/// What a body wants this tick, from its order: a direction on the floor plane
/// (unit length, or zero for none), the share of its speed, and the two
/// buttons a robot presses. <see cref="Arrived"/> says the order's last corner
/// has been reached.
/// </summary>
public readonly record struct MotionWish(AiVector Direction, float SpeedShare, bool Jump, bool Crouch, bool Arrived, float LastDistance = -1f)
{
    public static MotionWish None => default;

    /// <summary>Whether the body is heading for its order's last corner (<see cref="LastDistance"/> is how far it is on the floor plane).</summary>
    public bool ToLast => LastDistance >= 0f;

    public bool Wishing => Direction.X != 0f || Direction.Y != 0f;
}

/// <summary>
/// An order's move turned into a wish (plan_descent_logic 3.7, HO3), engine-free.
///
/// <para>
/// <b>Corners</b> are consumed in order as the body comes within the arrive
/// radius of each on the floor plane (height is the movement's business: a
/// corner on a stair is reached from below it). A body past its last corner
/// has arrived and wishes nothing: it holds there.
/// </para>
///
/// <para>
/// <b>Kinds.</b> <c>Hold</c> and <c>Pause</c> wish nothing. <c>Follow</c>
/// goes to the next corner. <c>Strafe</c> and <c>Retreat</c> go along
/// <see cref="MoveIntent.Direction"/> when it has a floor-plane length, else
/// along the corners. <c>Jump</c>, <c>CrouchJump</c> and <c>Crouch</c> move as
/// <c>Follow</c> (or along the direction when there are no corners) and press
/// jump, jump and crouch, or crouch; <see cref="MoveIntent.CrouchOn"/> presses
/// crouch on any kind. <c>Walk</c> is <see cref="MotionSettings.WalkFraction"/>
/// of the robot's speed; <c>Run</c> and <c>Sprint</c> are all of it (a robot
/// has no sprint).
/// </para>
/// </summary>
public static class MotionSteering
{
    /// <summary>The wish for <paramref name="move"/> at <paramref name="at"/>, consuming the corners reached into <paramref name="corner"/>.</summary>
    public static MotionWish Wish(in MoveIntent move, ref int corner, AiVector at, float arriveRadius, float walkFraction)
    {
        if (move.Kind is MoveKind.Hold or MoveKind.Pause)
            return MotionWish.None;

        float share = move.Speed == MoveSpeed.Walk ? walkFraction : 1f;
        bool jump = move.Kind is MoveKind.Jump or MoveKind.CrouchJump;
        bool crouch = move.CrouchOn || move.Kind is MoveKind.CrouchJump or MoveKind.Crouch;

        bool byDirection = move.Kind is MoveKind.Strafe or MoveKind.Retreat || move.CornerCount == 0;
        if (byDirection && move.Direction.Length2D > 0f)
            return new MotionWish(Flat(move.Direction), share, jump, crouch, false);

        if (move.CornerCount == 0)
            return new MotionWish(AiVector.Zero, share, jump, crouch, false);

        bool arrived = Consume(in move, ref corner, at, arriveRadius);
        if (arrived)
            return new MotionWish(AiVector.Zero, share, false, crouch, true);

        var to = move.Corner(corner) - at;
        float last = corner == move.CornerCount - 1 ? to.Length2D : -1f;
        return new MotionWish(Flat(to), share, jump, crouch, false, last);
    }

    /// <summary>
    /// The speed a body heading for its last corner may have: the speed its
    /// floor's friction stops it from within the distance left (the arrive
    /// steering: <paramref name="brakeRate"/> per second of that distance), never
    /// under <paramref name="least"/>, never over <paramref name="speed"/>. So it
    /// comes to rest at the corner rather than sliding past it.
    /// </summary>
    public static float Arrive(float speed, float distance, float brakeRate, float least) =>
        MathF.Min(speed, MathF.Max(least, brakeRate * distance));

    /// <summary>Moves <paramref name="corner"/> past every corner within <paramref name="arriveRadius"/>; whether none is left.</summary>
    public static bool Consume(in MoveIntent move, ref int corner, AiVector at, float arriveRadius)
    {
        if (corner < 0)
            corner = 0;
        while (corner < move.CornerCount && Flat2D(move.Corner(corner) - at) <= arriveRadius)
            corner++;
        return corner >= move.CornerCount;
    }

    /// <summary>The arrive radius of a body whose hull is <paramref name="halfWidth"/> wide each way.</summary>
    public static float ArriveRadius(MotionSettings settings, float halfWidth) => settings.ArriveRadius + settings.ArriveHullShare * halfWidth;

    private static float Flat2D(AiVector v) => v.Length2D;

    /// <summary><paramref name="v"/> on the floor plane, unit length; zero for none.</summary>
    public static AiVector Flat(AiVector v)
    {
        float l = v.Length2D;
        return l > 1e-4f ? new AiVector(v.X / l, v.Y / l, 0f) : AiVector.Zero;
    }
}
