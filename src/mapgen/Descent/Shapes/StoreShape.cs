namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The store (plan §5, chambers): a 512 room stacked with crates in a maze of
/// aisles a giant walks (128 wide or more), and crate steps (T1, each a
/// crouch-jump higher) up to a crate deck with a container on it. One or two
/// doors; the stacks' layout and the room's outline differ by variant.
/// </summary>
public static class StoreShape
{
    public const int Height = 512;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "store");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        float above = Parts.ChamberLampAbove;
        Lamp lamp = Parts.ChamberLamp;

        // a: one door; a deep store, stacks in rows across it, the deck at the back.
        var deep = new Rect(176, 192, c - t, 832);
        var a = New("store_a", kit, theme, Parts.E).WithOutline(deep).WithBody(deep)
            .Chamfers(deep, 96, Corner.NorthEast, Corner.SouthEast);
        float h1 = rng.Pick<float>([96, 128]);
        a.Crates(new Rect(624, 192, 720, 368), h1).Crates(new Rect(624, 656, 720, 832), 128)
            .Crates(new Rect(416, 352, 512, 672), 128).Crates(new Rect(176, 192, 304, 320), 96);
        Deck(a, new Rect(176, 704, 336, 832), 160, Compass.East, 2);
        a.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(256, m, kit.Sill), 256, above, lamp)
            .LampAt(560, 256, above, lamp).LampAt(560, 768, above, lamp).LampAt(816, 256, above, lamp).LampAt(816, 768, above, lamp)
            .Pack(304, 448).Pack(560, 768).Pack(816, 272)
            .Mark(MarkerKind.Barrel, 240, 600);
        yield return a.Build();

        // b: two doors opposite; stacks along both walls and an island, the aisle down the middle.
        var broad = new Rect(t, 176, c - t, 848);
        var b = New("store_b", kit, theme, Parts.WE).WithOutline(broad).WithBody(broad);
        float gap = rng.Pick<float>([128, 160]);
        b.Crates(new Rect(176, 176, 400, 320), 128).Crates(new Rect(400 + gap, 176, 688, 288), 96).Crates(new Rect(816, 176, 912, 320), 128)
            .Crates(new Rect(208, 704, 368, 848), 96).Crates(new Rect(624, 720, 912, 848), 128)
            .Crates(new Rect(448, 400, 576, 448), 64);
        Deck(b, new Rect(432, 720, 560, 848), 128, Compass.South, 2);
        b.LampsFromDoors(new Point(m, m, kit.Sill), 256, above, lamp).LampAt(m, 256, above, lamp).LampAt(m, 784, above, lamp).LampAt(176, 784, above, lamp).LampAt(848, 240, above, lamp)
            .Pack(352, 600).Pack(704, 400).Pack(640, 600)
            .Mark(MarkerKind.Trap, 736, 640);
        yield return b.Build();

        // c: two doors at a corner; an L store, stacks in its far arms.
        var south = new Rect(160, 160, c - t, 640);
        var north = new Rect(384, 160, 864, c - t);
        var cc = New("store_c", kit, theme, Parts.NE).WithOutline(south, north).WithBody(new Rect(160, 160, c - t, c - t))
            .Chamfers(north, 96, Corner.NorthEast);
        cc.Crates(new Rect(160, 160, 304, 288), 128).Crates(new Rect(160, 512, 256, 640), 96).Crates(new Rect(704, 160, 864, 256), 96)
            .Crates(new Rect(704, 704, 864, 832), 128).Crates(new Rect(384, 160, 480, 256), 64);
        Deck(cc, new Rect(160, 352, 288, 512), 128, Compass.East, 2);
        cc.LampsFromDoors(new Point(m, m, kit.Sill), 256, above, lamp).LampAt(272, 400, above, lamp).LampAt(784, 240, above, lamp).LampAt(800, 800, above, lamp)
            .Pack(448, 560).Pack(608, 832).Pack(880, 304)
            .Mark(MarkerKind.Barrel, 560, 208);
        yield return cc.Build();
    }

    /// <summary>
    /// A stack of crates over <paramref name="r"/>, <paramref name="height"/>
    /// high: a stack above a crouch-jump is stepped, its half toward the
    /// room's middle a crouch-jump lower, so everyone climbs the stacks (T1).
    /// </summary>
    private static RoomBuilder Crates(this RoomBuilder b, Rect r, float height)
    {
        int rise = JumpTable.Of(JumpTier.T1).Ledge(1f);
        if (height <= rise)
            return b.Block(r, height);
        if (height > 2 * rise)
            throw new ArgumentException($"a crate stack of {height} is more than two crouch-jumps; it would be a ledge without a reward");
        Rect body = b.Body;
        bool alongX = r.Width >= r.Depth;
        Rect low, high;
        if (alongX)
        {
            bool lowWest = r.CentreX > body.CentreX;
            low = lowWest ? r with { X1 = r.CentreX } : r with { X0 = r.CentreX };
            high = lowWest ? r with { X0 = r.CentreX } : r with { X1 = r.CentreX };
        }
        else
        {
            bool lowSouth = r.CentreY > body.CentreY;
            low = lowSouth ? r with { Y1 = r.CentreY } : r with { Y0 = r.CentreY };
            high = lowSouth ? r with { Y0 = r.CentreY } : r with { Y1 = r.CentreY };
        }

        return b.Block(low, height - rise).Block(high, height);
    }

    /// <summary>
    /// A crate deck over <paramref name="deck"/> at <paramref name="height"/>,
    /// with crate steps (T1) up to it from the side toward
    /// <paramref name="from"/>, and a container on it.
    /// </summary>
    private static void Deck(RoomBuilder b, Rect deck, float height, Compass from, int steps)
    {
        b.Block(deck, height);
        int rise = JumpTable.Of(JumpTier.T1).Ledge(1f);
        if (rise * (steps + 1) < height)
            throw new ArgumentException($"{steps} crate steps of {rise} do not reach a deck at {height}");
        // The steps run away from the deck, the highest beside it.
        for (int i = 0; i < steps; i++)
        {
            float h = rise * (steps - i) - (rise * (steps + 1) - height) * (steps - i) / (steps + 1);
            h = MathF.Round(h / 16) * 16;
            float near = 64 * i, far = 64 * (i + 1);
            Rect step = from switch
            {
                Compass.East => new(deck.X1 + near, deck.CentreY - 32, deck.X1 + far, deck.CentreY + 32),
                Compass.West => new(deck.X0 - far, deck.CentreY - 32, deck.X0 - near, deck.CentreY + 32),
                Compass.North => new(deck.CentreX - 32, deck.Y1 + near, deck.CentreX + 32, deck.Y1 + far),
                _ => new(deck.CentreX - 32, deck.Y0 - far, deck.CentreX + 32, deck.Y0 - near),
            };
            b.Block(step, h);
        }

        b.Mark(MarkerKind.Container, deck.CentreX, deck.CentreY, above: height);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(doors)
            .WithEntry(new RoomEntry([])).WithMapLabel("Store");
}
