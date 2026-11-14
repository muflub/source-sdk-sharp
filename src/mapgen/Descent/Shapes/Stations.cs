namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The pieces of the ways in and down (plan §5's role rooms, §8): a lift
/// cage standing in an alcove off a wall (the town's cage, <see cref="MineShaft"/>'s
/// sizes) with its button, a raised floor broken by a hole to drop, walk or
/// slide down into, and the party's spawn points round an arrival. Each
/// writes the transition volume and what fires it through
/// <see cref="RoomBuilder"/>'s role overrides.
/// </summary>
public static class Stations
{
    /// <summary>How deep a cage's alcove runs into the wall beyond the room's body.</summary>
    public const int AlcoveDepth = 160;

    /// <summary>The alcove's width: the cage and its bars, with a margin each side.</summary>
    public const int AlcoveWidth = MineShaft.CageSize + 48;

    /// <summary>The yaw that faces into the room from <paramref name="wall"/>.</summary>
    public static int Inward(CellFace wall) => wall switch
    {
        CellFace.North => 270,
        CellFace.South => 90,
        CellFace.East => 180,
        _ => 0,
    };

    /// <summary>A point given along <paramref name="wall"/> of <paramref name="body"/> (x for north and south, y for east and west) and out past it (negative is into the room).</summary>
    public static (float X, float Y) Out(Rect body, CellFace wall, float along, float outward) => wall switch
    {
        CellFace.North => (along, body.Y1 + outward),
        CellFace.South => (along, body.Y0 - outward),
        CellFace.East => (body.X1 + outward, along),
        _ => (body.X0 - outward, along),
    };

    /// <summary>The box between two points given as <see cref="Out"/> takes them.</summary>
    public static Rect OutRect(Rect body, CellFace wall, float along0, float out0, float along1, float out1)
    {
        var (x0, y0) = Out(body, wall, along0, out0);
        var (x1, y1) = Out(body, wall, along1, out1);
        return new Rect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
    }

    /// <summary>
    /// A lift cage in an alcove off <paramref name="wall"/> of
    /// <paramref name="body"/> at <paramref name="along"/>: the town's 128
    /// cage (its floor, bars on two sides and a roof), a button inside on a
    /// bar that fires the transition, the volume over the cage's floor, the
    /// arrival in front of it facing into the room, and four spawns.
    /// Adds the alcove to the outline.
    /// </summary>
    public static RoomBuilder Cage(this RoomBuilder b, Rect body, CellFace wall, float along, RoomRole role, bool underSky = false)
    {
        float f = b.Floor, h = MineShaft.CageHeight, half = MineShaft.CageSize / 2f;
        Rect alcove = OutRect(body, wall, along - AlcoveWidth / 2f, 0, along + AlcoveWidth / 2f, AlcoveDepth);
        b.WithOutline(alcove);
        // The lift's shaft over an alcove's cage is out of sight: the alcove's head comes down to the cage's roof,
        // so nothing stands on it. Under the sky the cage has no roof; the shaft rises over it.
        if (!underSky)
            b.WithBox(new Point(alcove.X0, alcove.Y0, f + h), new Point(alcove.X1, alcove.Y1, b.CeilingUnderside), b.Theme.Wall);
        Rect cage = OutRect(body, wall, along - half, 16, along + half, 16 + MineShaft.CageSize);
        b.Block(cage, 8, MineShaft.Floor);
        foreach (float side in (float[])[-half - 8, half])
        {
            Rect bar = OutRect(body, wall, along + side, 16, along + side + 8, 16 + MineShaft.CageSize);
            b.WithBox(new Point(bar.X0, bar.Y0, f + 8), new Point(bar.X1, bar.Y1, f + h - 8), MineShaft.Beam);
        }

        if (!underSky)
        {
            Rect roof = OutRect(body, wall, along - half - 8, 16, along + half + 8, 16 + MineShaft.CageSize);
            b.WithBox(new Point(roof.X0, roof.Y0, f + h - 8), new Point(roof.X1, roof.Y1, f + h), MineShaft.Beam);
        }
        Rect button = OutRect(body, wall, along + half - 8, 64, along + half, 96);
        b.WithButton(new Point(button.X0, button.Y0, f + 48), new Point(button.X1, button.Y1, f + 72));
        Rect inside = OutRect(body, wall, along - half, 16, along + half - 8, 16 + MineShaft.CageSize);
        b.WithTransitionBox(new Point(inside.X0, inside.Y0, f + 8), new Point(inside.X1, inside.Y1, f + 72));
        var (ax, ay) = Out(body, wall, along, -64);
        b.WithRole(role, new Point(ax, ay, f), Inward(wall));
        b.Spawns(body, wall, along, -144);
        var (lx, ly) = Out(body, wall, along, -48);
        return b.LampAt(lx, ly, 192, MineShaft.Lantern);
    }

    /// <summary>Four spawns in a square 128 across, <paramref name="outward"/> from the wall (negative: into the room), facing into the room.</summary>
    public static RoomBuilder Spawns(this RoomBuilder b, Rect body, CellFace wall, float along, float outward)
    {
        foreach (float da in (float[])[-64, 64])
            foreach (float d0 in (float[])[0, -96])
            {
                var (x, y) = Out(body, wall, along + da, outward + d0);
                b.Mark(MarkerKind.Spawn, x, y, yaw: Inward(wall));
            }

        return b;
    }

    /// <summary>How a hole in a raised floor is gone down.</summary>
    public enum Descent
    {
        /// <summary>A drop: the floor broken through (a collapsed floor, a fissure, a bore hole).</summary>
        Drop,

        /// <summary>A rubble ramp or a stair walked down into it.</summary>
        Ramp,

        /// <summary>A slide too steep to stand on (a chute, a slope of slime).</summary>
        Slide,
    }

    /// <summary>
    /// A down room's way down: a floor raised <paramref name="height"/> over
    /// <paramref name="platform"/>, stairs up to it from its side toward
    /// <paramref name="stairs"/>, and a hole <paramref name="hole"/> in it
    /// gone down by <paramref name="how"/>, the hole's far end (toward
    /// <paramref name="down"/>) the trigger that sends the party down.
    /// </summary>
    public static RoomBuilder Hole(this RoomBuilder b, Rect platform, int height, Rect hole, Descent how, Compass down, Compass stairs, Point arrival, int yaw)
    {
        float f = b.Floor;
        foreach (Rect r in Rects.Subtract(platform, [hole]))
            b.Block(r, height, b.Theme.Wall);
        // The stairs: a steep flight (16 a step on 16 treads) against the platform's low end, toward <paramref name="stairs"/>.
        float run = height;
        Rect flight = stairs switch
        {
            Compass.South => new(platform.X0, platform.Y0 - run, platform.X0 + 160, platform.Y0),
            Compass.North => new(platform.X0, platform.Y1, platform.X0 + 160, platform.Y1 + run),
            Compass.West => new(platform.X0 - run, platform.Y0, platform.X0, platform.Y0 + 160),
            _ => new(platform.X1, platform.Y0, platform.X1 + run, platform.Y0 + 160),
        };
        Compass up = stairs switch { Compass.South => Compass.North, Compass.North => Compass.South, Compass.West => Compass.East, _ => Compass.West };
        b.StairsUp(flight, up, height);

        // The trigger's end of the hole: its last 128 toward the way down (the whole hole for a drop).
        Rect end = how == Descent.Drop ? hole : down switch
        {
            Compass.North => hole with { Y0 = hole.Y1 - 128 },
            Compass.South => hole with { Y1 = hole.Y0 + 128 },
            Compass.East => hole with { X0 = hole.X1 - 128 },
            _ => hole with { X1 = hole.X0 + 128 },
        };
        if (how != Descent.Drop)
        {
            // From the platform's lip down to the trigger's end; a slide falls the same height over half the run.
            Rect slope = down switch
            {
                Compass.North => hole with { Y1 = end.Y0 },
                Compass.South => hole with { Y0 = end.Y1 },
                Compass.East => hole with { X1 = end.X0 },
                _ => hole with { X0 = end.X1 },
            };
            // A slide drops its height over a run of about 0.6 of it, on whole 16s: 59 degrees, too steep to stand on.
            float steep = MathF.Floor(height * 0.6f / 16) * 16;
            if (how == Descent.Slide)
                slope = down switch
                {
                    Compass.North => slope with { Y0 = slope.Y1 - Math.Min(slope.Depth, steep) },
                    Compass.South => slope with { Y1 = slope.Y0 + Math.Min(slope.Depth, steep) },
                    Compass.East => slope with { X0 = slope.X1 - Math.Min(slope.Width, steep) },
                    _ => slope with { X1 = slope.X0 + Math.Min(slope.Width, steep) },
                };
            Compass rises = down switch { Compass.North => Compass.South, Compass.South => Compass.North, Compass.East => Compass.West, _ => Compass.East };
            if (how == Descent.Slide)
            {
                // The slide's top: the rest of the hole behind it, at the platform's height.
                Rect behind = down switch
                {
                    Compass.North => hole with { Y1 = slope.Y0 },
                    Compass.South => hole with { Y0 = slope.Y1 },
                    Compass.East => hole with { X1 = slope.X0 },
                    _ => hole with { X0 = slope.X1 },
                };
                if (!behind.IsEmpty)
                    b.Block(behind, height, b.Theme.Wall);
            }

            b.WithBrush(Brush.Wedge(new Point(slope.X0, slope.Y0, f), new Point(slope.X1, slope.Y1, f + height), rises, b.Theme.Block));
        }

        b.WithTransitionBox(new Point(end.X0 + 16, end.Y0 + 16, f), new Point(end.X1 - 16, end.Y1 - 16, f + 64));
        b.WithHallTrigger(new Point(end.X0, end.Y0, f), new Point(end.X1, end.Y1, f + 128));
        b.WithRole(RoomRole.Down, arrival, yaw);
        return b.LampAt(hole.CentreX, hole.CentreY, height + 160, Parts.ChamberLamp);
    }
}
