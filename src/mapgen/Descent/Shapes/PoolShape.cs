namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The pool (plan §5, chambers; levels 8, 12, 14): water standing in a basin
/// on the room's floor, walled by walkways a few steps up that every door's
/// stairs climb to, so the way between doors stays dry and the water never
/// reaches a door (a water socket would have to meet water at the
/// neighbour's door, which the layout cannot promise). Stepping pillars rise
/// out of the water a double jump above the walkways (S2), the last with a
/// container, and steps lead out of the water for whoever falls in. One to
/// four doors.
/// </summary>
public static class PoolShape
{
    public const int Height = 512;

    /// <summary>The walkways' height above the floor.</summary>
    public const int Walk = 64;

    /// <summary>How deep the water stands over the basin's floor.</summary>
    public const int Water = 48;

    /// <summary>The water's material (level 8's, plan §7).</summary>
    public const string WaterMaterial = "water/water_well";

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "pool");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;

        // a: one door; a deep pool, two pillars.
        var bodyA = new Rect(160, 160, c - t, 864);
        var a = Pool(New("pool_a", kit, theme, Parts.E), bodyA, new Rect(336, 336, 720, 688), rng, [(448, 512), (608, 512)]);
        a.Mark(MarkerKind.PackSpawn, 240, 240, above: Walk).Mark(MarkerKind.PackSpawn, 240, 784, above: Walk)
            .Mark(MarkerKind.Barrel, 800, 240, above: Walk);
        yield return a.Build();

        // b: two doors opposite; a long pool between them, the walkways round it.
        var bodyB = new Rect(t, 160, c - t, 864);
        var b = Pool(New("pool_b", kit, theme, Parts.WE), bodyB, new Rect(368, 320, 656, 704), rng, [(512, 448)]);
        b.Mark(MarkerKind.PackSpawn, 512, 224, above: Walk).Mark(MarkerKind.PackSpawn, 512, 800, above: Walk)
            .Mark(MarkerKind.Trap, 224, 224, above: Walk);
        yield return b.Build();

        // c: two doors at a corner; the pool in the south-west, its corner cut.
        var bodyC = new Rect(144, 144, c - t, c - t);
        var cc = Pool(New("pool_c", kit, theme, Parts.NE), bodyC, new Rect(272, 272, 656, 656), rng, [(464, 464)]);
        cc.Chamfers(bodyC, 128, Corner.SouthWest);
        cc.Mark(MarkerKind.PackSpawn, 816, 816, above: Walk).Mark(MarkerKind.PackSpawn, 240, 816, above: Walk)
            .Mark(MarkerKind.Dispenser, 816, 240, above: Walk);
        yield return cc.Build();

        // d: four doors; a square pool in the middle, walkways all round.
        var bodyD = new Rect(t, t, c - t, c - t);
        var d = Pool(New("pool_d", kit, theme, Parts.NESW), bodyD, new Rect(368, 368, 656, 656), rng, [(512, 512)]);
        d.Chamfers(bodyD, 160);
        d.Mark(MarkerKind.PackSpawn, 240, 240, above: Walk).Mark(MarkerKind.PackSpawn, 784, 784, above: Walk)
            .Mark(MarkerKind.Barrel, 784, 240, above: Walk);
        yield return d.Build();
    }

    /// <summary>
    /// The pool in <paramref name="body"/>: the basin's water, the walkways
    /// round it less each door's channel, the stairs up from each door, the
    /// pillars and the steps out of the water, and lamps over the walkways.
    /// </summary>
    private static RoomBuilder Pool(RoomBuilder b, Rect body, Rect basin, RoomSeed rng, (float X, float Y)[] pillars)
    {
        RoomKit kit = b.Kit;
        float run = Walk / 16 * 32;
        var channels = new List<Rect>();
        foreach (CellFace face in b.DoorFaces)
        {
            Rect channel = Parts.Neck(kit, face, 224, RoomBuilder.DoorApproach + run);
            channels.Add(channel);
            Rect stairs = Parts.Neck(kit, face, 224, RoomBuilder.DoorApproach + run);
            stairs = face switch
            {
                CellFace.East => stairs with { X1 = stairs.X0 + run },
                CellFace.West => stairs with { X0 = stairs.X1 - run },
                CellFace.North => stairs with { Y1 = stairs.Y0 + run },
                _ => stairs with { Y0 = stairs.Y1 - run },
            };
            // The flight climbs away from the door, to the walkway at its inner end.
            b.StairsUp(stairs, face switch { CellFace.East => Compass.West, CellFace.West => Compass.East, CellFace.North => Compass.South, _ => Compass.North }, Walk);
        }

        b.WithOutline(body).WithBody(body);
        foreach (Rect walk in Rects.Subtract(body, [basin, .. channels]))
            b.Block(walk, Walk, b.Theme.Wall);
        b.WithWater(basin, b.Floor, b.Floor + Water, WaterMaterial);
        // Steps out of the water in the basin's south-west corner.
        b.StairsUp(new Rect(basin.X0, basin.Y0, basin.X0 + 96, basin.Y0 + 64), Compass.South, Walk);

        int top = Walk + JumpTable.Of(JumpTier.S2).Ledge(rng.Fraction());
        b.Holds(JumpTier.S2);
        for (int i = 0; i < pillars.Length; i++)
        {
            b.WithBrush(Brush.Prism(pillars[i].X, pillars[i].Y, 48, 12, b.Floor, b.Floor + top, b.Theme.Block));
            if (i == pillars.Length - 1)
                b.Mark(MarkerKind.Container, pillars[i].X, pillars[i].Y, above: top);
        }

        // Lamps over the walkways round the basin, and one over the water.
        var ring = basin.Grow(96);
        foreach (float x in (float[])[ring.X0, ring.CentreX, ring.X1])
            foreach (float y in (float[])[ring.Y0, ring.CentreY, ring.Y1])
                if (x != ring.CentreX || y != ring.CentreY)
                    b.LampAt(MathF.Round(x), MathF.Round(y), Parts.ChamberLampAbove, Parts.ChamberLamp);
        b.LampsFromDoors(new Point(basin.CentreX, basin.CentreY, b.Floor), 256, Parts.ChamberLampAbove, Parts.ChamberLamp);
        return b;
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([])).WithMapLabel("Pool");
}
