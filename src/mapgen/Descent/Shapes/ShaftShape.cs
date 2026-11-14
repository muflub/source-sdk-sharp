namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The shaft (plan §5, halls and shafts), a grey placeholder for the chimney
/// and the cavern: 1536 high over the whole cell, doors west and east, chimney
/// ledges at the crouched rocket jump's height (B2) on the north wall and the
/// sticky jump's (B3) on the south, each with its reward.
/// </summary>
public static class ShaftShape
{
    /// <summary>The shaft's height (§3.1's shaft class starts at 1536).</summary>
    public const int Height = 1536;

    /// <summary>A shaft's upper lamp: the hall's, hung high to light the ledges.</summary>
    public static Lamp UpperLamp { get; } = HallShape.HallLamp with { Brightness = 80 };

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "shaft");
        float m = kit.Cell / 2f, f = kit.Sill;
        var b = new RoomBuilder("shaft_a", kit, theme).WithHeight(Height).WithDoors(ShapeParts.Straight)
            .WithEntry(new RoomEntry([], Jump: true))
            .WithMapLabel("Shaft")
            .ChimneyLedges(CellFace.North, CellFace.South, span: 256 + 64 * rng.Int(0, 3))
            .WithMarker(MarkerKind.PackSpawn, new Point(m, m, f), radius: 96);
        foreach (float x in (float[])[m - 256, m + 256])
        {
            b.WithLamp(new Point(x, m, f + 448), HallShape.HallLamp);
            b.WithLamp(new Point(x, m, f + 1024), UpperLamp);
        }

        yield return b.Build();
    }
}
