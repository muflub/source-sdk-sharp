namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// Pieces the grey placeholder families share: lamps along a room's plus
/// shape, and the door sets they are written with. Plain on purpose; the real
/// shapes of plan §5 replace the families that use them.
/// </summary>
public static class ShapeParts
{
    /// <summary>A chamber's lamp: the default's colour, brightness 100, reaching a 512 room's floor and walls.</summary>
    public static Lamp ChamberLamp { get; } = Lamp.Default with { Brightness = 100, FiftyPercent = 320, ZeroPercent = 900 };

    /// <summary>
    /// A lamp at the centre of the room's square and one halfway along each
    /// door's arm when the room is narrower than its cell, all at
    /// <paramref name="z"/> above the floor.
    /// </summary>
    public static RoomBuilder ArmLamps(this RoomBuilder b, Lamp lamp, float z)
    {
        float m = b.Kit.Cell / 2f, f = b.Floor;
        b.WithLamp(new Point(m, m, f + z), lamp);
        if (b.Width >= b.Kit.Interior)
            return b;
        float arm = (b.InteriorHigh + b.Kit.Cell - b.Kit.WallDepth) / 2f, low = b.Kit.Cell - arm;
        foreach (CellFace face in b.DoorFaces)
        {
            Point at = face switch
            {
                CellFace.East => new(arm, m, f + z),
                CellFace.West => new(low, m, f + z),
                CellFace.North => new(m, arm, f + z),
                _ => new(m, low, f + z),
            };
            b.WithLamp(at, lamp);
        }

        return b;
    }

    /// <summary>
    /// Lamps on a grid over the room's square, <paramref name="pitch"/> apart
    /// and half a pitch in from its walls, at <paramref name="z"/> above the floor.
    /// </summary>
    public static RoomBuilder GridLamps(this RoomBuilder b, Lamp lamp, float z, float pitch)
    {
        float lo = b.InteriorLow, hi = b.InteriorHigh;
        int n = Math.Max(1, (int)Math.Round((hi - lo) / pitch));
        float step = (hi - lo) / n;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                b.WithLamp(new Point(lo + step * (i + 0.5f), lo + step * (j + 0.5f), b.Floor + z), lamp);
        return b;
    }

    /// <summary>A pack spawn on the floor at the room's centre, offset by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public static RoomBuilder CentrePack(this RoomBuilder b, float dx = 0, float dy = 0, int radius = 64)
    {
        float m = b.Kit.Cell / 2f;
        return b.WithMarker(MarkerKind.PackSpawn, new Point(m + dx, m + dy, b.Floor), radius: radius);
    }

    /// <summary>One door (east).</summary>
    public static CellFace[] One => [CellFace.East];

    /// <summary>Two doors opposite (west and east).</summary>
    public static CellFace[] Straight => [CellFace.West, CellFace.East];

    /// <summary>Two doors at a corner (north and east).</summary>
    public static CellFace[] Corner => [CellFace.North, CellFace.East];

    /// <summary>Three doors (north, east and west).</summary>
    public static CellFace[] Three => [CellFace.North, CellFace.East, CellFace.West];

    /// <summary>Four doors.</summary>
    public static CellFace[] Four => [CellFace.North, CellFace.East, CellFace.South, CellFace.West];
}
