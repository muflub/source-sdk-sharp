namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The drift (plan §5, connectors): a straight low hallway, two doors
/// opposite, 256 to 384 wide, lamps along it, a pack spawn in its middle.
/// The demonstration of a connector family.
/// </summary>
public static class DriftShape
{
    /// <summary>The drift's height class: low (plan §3.1).</summary>
    public const int Height = 256;

    /// <summary>A drift's lamp: the default, at less than half its brightness.</summary>
    public static Lamp DriftLamp { get; } = Lamp.Default with { Brightness = 60 };

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "drift");
        int width = rng.Pick<int>([256, 320, 384]);
        float m = kit.Cell / 2f, lampZ = Height - kit.WallDepth - 48;
        var builder = new RoomBuilder("drift_ew", kit, theme).WithHeight(Height).WithWidth(width).WithDoors(CellFace.East, CellFace.West)
            .WithEntry(new RoomEntry(["corridor"], Low: true))
            .WithMarker(MarkerKind.PackSpawn, new Point(m, m, kit.Sill), yaw: 0, radius: 64);
        // A lamp every 256 along the run, as level 1's electric lamps are spaced
        // (§6), dimmer than the default: four in a 256-square tube add up.
        for (float x = 128; x < kit.Cell; x += 256)
            builder.WithLamp(new Point(x, m, lampZ), DriftLamp);
        yield return builder.Build();
    }
}
