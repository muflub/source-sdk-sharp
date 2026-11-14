namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The chamber (plan §5, chambers), a grey placeholder for the pillar room,
/// split level, pit room and yard: a 512 room over the whole cell with two,
/// three or four doors, a pillar for cover and pack spawns. The variant with
/// a wall free of doors carries a mezzanine (S2).
/// </summary>
public static class ChamberShape
{
    /// <summary>The chamber's height class (§3.1).</summary>
    public const int Height = 512;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "chamber");
        yield return Build("chamber_a", kit, theme, rng, ShapeParts.Straight, mezzanine: true);
        yield return Build("chamber_b", kit, theme, rng, ShapeParts.Corner, mezzanine: true);
        yield return Build("chamber_c", kit, theme, rng, ShapeParts.Three, mezzanine: true);
        yield return Build("chamber_d", kit, theme, rng, ShapeParts.Four, mezzanine: false);
    }

    private static Room Build(string name, RoomKit kit, RoomTheme theme, RoomSeed rng, CellFace[] doors, bool mezzanine)
    {
        float m = kit.Cell / 2f;
        var b = new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([]))
            .WithMapLabel("Chamber");
        // A square pillar off the centre, toward the south-west, clear of every door's approach.
        float px = m - 192 - 16 * rng.Int(0, 4), py = m - 192;
        b.WithBox(new Point(px - 48, py - 48, b.Floor), new Point(px + 48, py + 48, b.CeilingUnderside));
        if (mezzanine)
        {
            // The south wall has no door in these variants; the deck runs along its west half.
            float from = b.InteriorLow + 96;
            b.Mezzanine(CellFace.South, from, from + 256, depth: 160);
        }

        return b.CentrePack(160, 160, 96).CentrePack(-64, 192, 64)
            .GridLamps(ShapeParts.ChamberLamp, Height - 2 * kit.WallDepth - 96, 512)
            .Build();
    }
}
