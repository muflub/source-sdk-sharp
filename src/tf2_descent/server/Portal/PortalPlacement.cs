using SourceSharp.Abi;

namespace TF2.Descent.Server.Portal;

/// <summary>The world as placing a pad asks it: a line through brushes, and whether a box is clear.</summary>
public interface IPortalGround
{
    /// <summary>A line from <paramref name="from"/> to <paramref name="to"/> through the world's brushes.</summary>
    TraceResult Line(Vector from, Vector to);

    /// <summary>Whether a box of <paramref name="mins"/>..<paramref name="maxs"/> at <paramref name="at"/> touches nothing solid.</summary>
    bool BoxClear(Vector at, Vector mins, Vector maxs);
}

/// <summary>Where a pad may stand, or why not (a <c>#Descent_Portal_*</c> token).</summary>
public readonly record struct PortalPlace(bool Ok, Vector Position, float Yaw, string Why)
{
    public static PortalPlace At(Vector position, float yaw) => new(true, position, yaw, "");

    public static PortalPlace Refused(string why) => new(false, default, 0f, why);
}

/// <summary>
/// Where a town portal's entrance stands (plan_descent_logic WO4): on the floor in front of the
/// hero, as an Engineer builds: a line to it clear of walls, floor under its middle and its four
/// corners, a floor flat enough to stand on, and room above it for the pad and a player standing
/// on it. The pad's box is TF2's teleporter's (48 by 48 by 12).
/// </summary>
public static class PortalPlacement
{
    /// <summary>How far in front of the hero's feet the pad's middle stands: his half width, the pad's, and a gap.</summary>
    public const float Distance = 64f;

    /// <summary>The pad's half width (stock's <c>TELEPORTER_MINS</c> and <c>MAXS</c>, 24 a side).</summary>
    public const float Half = 24f;

    /// <summary>The pad's height (stock's <c>TELEPORTER_MAXS</c> z).</summary>
    public const float Height = 12f;

    /// <summary>A TF player's standing height: the room kept above the pad.</summary>
    public const float PlayerHeight = 82f;

    /// <summary>How far above the hero's feet the floor in front may rise (a step), and how far it may fall.</summary>
    public const float StepUp = 18f, StepDown = 64f;

    /// <summary>The steepest floor a pad stands on: the floor's normal's z at least this (a walkable slope).</summary>
    public const float FlatEnough = 0.7f;

    /// <summary>The room above the floor: a wall, a ceiling or a crate in it.</summary>
    public const string NoRoom = "#Descent_Portal_NoRoom";

    /// <summary>No floor under the pad, or under one of its corners.</summary>
    public const string NoFloor = "#Descent_Portal_NoFloor";

    /// <summary>The floor is too steep.</summary>
    public const string TooSteep = "#Descent_Portal_TooSteep";

    public static Vector PadMins => new(-Half, -Half, 0f);

    public static Vector PadMaxs => new(Half, Half, Height);

    /// <summary>The pad with a player standing on it: what must be clear.</summary>
    public static Vector RoomMaxs => new(Half, Half, Height + PlayerHeight);

    /// <summary>Where the pad stands for a hero at <paramref name="feet"/> facing <paramref name="yaw"/> degrees, or why it cannot.</summary>
    public static PortalPlace Place(Vector feet, float yaw, IPortalGround ground)
    {
        float r = yaw * MathF.PI / 180f;
        var forward = new Vector(MathF.Cos(r), MathF.Sin(r), 0f);
        Vector spot = feet + forward * Distance;

        // Nothing between the hero and the spot at his waist: a pad is never put through a wall.
        var waist = new Vector(0f, 0f, 36f);
        if (ground.Line(feet + waist, spot + waist).Hit)
            return PortalPlace.Refused(NoRoom);

        TraceResult down = ground.Line(spot + new Vector(0f, 0f, StepUp), spot - new Vector(0f, 0f, StepDown));
        if (!down.Hit || down.Fraction <= 0f)
            return PortalPlace.Refused(down.Hit ? NoRoom : NoFloor);
        if (down.Normal.Z < FlatEnough)
            return PortalPlace.Refused(TooSteep);

        Vector floor = down.End;
        if (!ground.BoxClear(floor + new Vector(0f, 0f, 1f), PadMins, RoomMaxs))
            return PortalPlace.Refused(NoRoom);

        // Each corner over floor: a pad does not hang over a ledge.
        const float c = Half - 4f;
        foreach (var (x, y) in new[] { (c, c), (c, -c), (-c, c), (-c, -c) })
        {
            var corner = floor + new Vector(x, y, 0f);
            if (!ground.Line(corner + new Vector(0f, 0f, 8f), corner - new Vector(0f, 0f, 24f)).Hit)
                return PortalPlace.Refused(NoFloor);
        }

        return PortalPlace.At(floor, yaw);
    }
}
