namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The treasure room (tag <c>treasure</c>, placed off the critical path; plan
/// §5's treasure stub): a dead end of one door (east) worth the walk, a 512
/// vault with a container on a dais, guarded. A short neck into a cut vault;
/// a round vault behind a turn; and a vault whose dais is up crate steps.
/// </summary>
public static class TreasureShape
{
    public const int Height = 512;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "treasure");
        float c = kit.Cell, t = kit.WallDepth, m = c / 2f;
        float above = Parts.ChamberLampAbove;
        Lamp lamp = Parts.ChamberLamp;

        // a: a neck into a vault cut on the diagonal, the dais at its back.
        float x0 = rng.Pick<float>([176, 240]);
        var vault = new Rect(x0, 224, x0 + 512, 800);
        var a = New("treasure_a", kit, theme).WithOutline(Parts.Neck(kit, CellFace.East, 256, c - t - vault.X1 + 32), vault).WithBody(vault)
            .Chamfers(vault, 160)
            .Block(new Rect(vault.X0, m - 96, vault.X0 + 128, m + 96), 16);
        a.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(vault.X0 + 128, m, kit.Sill), 288, above, lamp)
            .LampAt(vault.CentreX, vault.Y0 + 128, above, lamp).LampAt(vault.CentreX, vault.Y1 - 128, above, lamp)
            .Mark(MarkerKind.Container, vault.X0 + 64, m, above: 16)
            .Pack(vault.CentreX + 64, vault.Y0 + 128).Pack(vault.CentreX + 64, vault.Y1 - 128);
        yield return a.Build();

        // b: a round vault north of a turn.
        var round = new Rect(256, 448, 768, 960);
        // Its far corners rounded wide, the two over the turn's mouth less, so a giant walks in.
        var b = New("treasure_b", kit, theme).WithOutline(new Rect(m - 128, m - 128, c - t, m + 128), round).WithBody(round);
        b.WithBrushes(b.RoundCorner(round, Corner.NorthWest, 192)).WithBrushes(b.RoundCorner(round, Corner.NorthEast, 192))
            .WithBrushes(b.RoundCorner(round, Corner.SouthWest, 96)).WithBrushes(b.RoundCorner(round, Corner.SouthEast, 96))
            .Block(new Rect(m - 96, round.Y1 - 160, m + 96, round.Y1 - 32), 16);
        b.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(m, m, kit.Sill), 288, above, lamp)
            .LampAt(m, round.CentreY + 64, above, lamp).LampAt(round.X0 + 128, round.CentreY, above, lamp).LampAt(round.X1 - 128, round.CentreY, above, lamp)
            .Mark(MarkerKind.Container, m, round.Y1 - 96, above: 16, yaw: 270)
            .Pack(round.X0 + 160, round.CentreY).Pack(round.X1 - 160, round.CentreY);
        yield return b.Build();

        // c: a long vault, the treasure on a dais up crate steps.
        var hall = new Rect(176, 288, c - t, 736);
        int rise = JumpTable.Of(JumpTier.T1).Ledge(0.5f);
        var cc = New("treasure_c", kit, theme).WithOutline(hall).WithBody(hall)
            .Chamfers(hall, 128, Corner.SouthWest, Corner.NorthWest)
            .Block(new Rect(hall.X0, m - 112, hall.X0 + 160, m + 112), 2 * rise)
            .Block(new Rect(hall.X0 + 160, m - 48, hall.X0 + 224, m + 48), rise);
        cc.LampsAlong(Parts.DoorPoint(kit, CellFace.East, 0), new Point(hall.X0 + 96, m, kit.Sill), 288, above, lamp)
            .LampAt(hall.CentreX, hall.Y0 + 96, above, lamp).LampAt(hall.CentreX, hall.Y1 - 96, above, lamp)
            .Mark(MarkerKind.Container, hall.X0 + 80, m, above: 2 * rise)
            .Mark(MarkerKind.Barrel, 720, hall.Y0 + 48)
            .Pack(560, hall.Y0 + 112).Pack(560, hall.Y1 - 112).Pack(480, m);
        yield return cc.Build();
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme) =>
        new RoomBuilder(name, kit, theme).WithHeight(Height).WithDoors(Parts.E)
            .WithEntry(new RoomEntry(["treasure"])).WithMapLabel("Treasure");
}
