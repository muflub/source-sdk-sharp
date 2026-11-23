namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The way in (tag <c>start</c>, <c>room_role up</c>; plan §5, §7's "way
/// down" rows read upward, §8). Level 1: the bottom of the town's shaft, a
/// 192 shaft lined in the town's timber rising through the ceiling to the
/// sky, the cage at its foot. Levels 2 to 5: a cage station, the cage in an
/// alcove. Levels 7 to 10: the foot of a rubble ramp up to a blocked tunnel
/// mouth. Levels 12 to 15: under a chute's mouth, a signal bell to be drawn
/// up. Each with one door, two opposite and two at a corner; each with its
/// arrival facing into the room and four spawns round it: a body with necks
/// to its doors.
/// </summary>
public static class ArrivalShape
{
    public const int Height = 512;

    public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
    {
        var rng = new RoomSeed(seed, "arrival");
        // Level 5's lift station on its 512 kit (brief p18): 2 by 2 cells, its one door east.
        if (Parts.SpanCells(kit) > 1)
        {
            yield return Cage("arrival_cagea", kit, theme, Parts.E, rng);
            yield break;
        }

        foreach (var (id, doors) in DoorSets)
        {
            yield return Shaft("arrival_shaft" + id, kit, theme, doors, rng);
            yield return Cage("arrival_cage" + id, kit, theme, doors, rng);
            yield return Rubble("arrival_rubble" + id, kit, theme, doors, rng);
            yield return Chute("arrival_chute" + id, kit, theme, doors, rng);
        }
    }

    /// <summary>The door sets every way in comes in: one door, two opposite, two at a corner.</summary>
    public static IReadOnlyList<(string Id, CellFace[] Doors)> DoorSets { get; } = [("a", Parts.E), ("b", Parts.WE), ("c", Parts.NE)];

    /// <summary>The room's body: 672 by 640 in the cell's middle, room for a cage's alcove off any side.</summary>
    public static Rect Body(RoomKit kit) =>
        // On level 5's 512 kit 8 further north, so no face of a station's platform lies on the room's middle cell face.
        Rect.Around(Parts.Span(kit) / 2f, Parts.Span(kit) / 2f - (Parts.SpanCells(kit) > 1 ? 24 : 32), 672, 640);

    /// <summary>The body and its necks to <paramref name="doors"/>.</summary>
    public static RoomBuilder Shell(RoomBuilder b, Rect body, CellFace[] doors)
    {
        b.WithOutline(body).WithBody(body);
        foreach (CellFace face in doors)
            b.WithOutline(Parts.Neck(b, face, 256, NeckLength(b.Kit, body, face)));
        return b;
    }

    private static float NeckLength(RoomKit kit, Rect body, CellFace face) => face switch
    {
        CellFace.East => Parts.Span(kit) - kit.WallDepth - body.X1 + 32,
        CellFace.West => body.X0 - kit.WallDepth + 32,
        CellFace.North => Parts.Span(kit) - kit.WallDepth - body.Y1 + 32,
        _ => body.Y0 - kit.WallDepth + 32,
    };

    /// <summary>The wall of the body the station's fitting stands on: the north, or for a north door the west.</summary>
    public static CellFace FittingWall(CellFace[] doors) => doors.Contains(CellFace.North) ? CellFace.West : CellFace.North;

    /// <summary>Lamps over the way from each door to the body's middle, and over the body.</summary>
    public static void Light(RoomBuilder b, Rect body)
    {
        var hub = new Point(body.CentreX, body.CentreY, b.Floor);
        b.LampsFromDoors(hub, 288, Parts.ChamberLampAbove, Parts.ChamberLamp);
        foreach (float x in (float[])[body.X0 + 176, body.X1 - 176])
            foreach (float y in (float[])[body.Y0 + 160, body.Y1 - 160])
                b.LampAt(x, y, Parts.ChamberLampAbove, Parts.ChamberLamp);
    }

    private static RoomBuilder New(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, int minDepth, int maxDepth, int height = Height) =>
        new RoomBuilder(name, kit, theme).WithHeight(height).Seat(doors)
            .WithEntry(new RoomEntry(["start"], MinDepth: minDepth, MaxDepth: maxDepth)).WithMapLabel("Way in");

    /// <summary>Level 1: the foot of the town's shaft, open to the sky.</summary>
    private static Room Shaft(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, RoomSeed rng)
    {
        const int height = 768, roof = 384;
        Rect body = Body(kit);
        CellFace wall = FittingWall(doors);
        var b = Shell(New(name, kit, theme, doors, 1, 1, height), body, doors);
        float along = wall == CellFace.North ? body.CentreX + rng.Pick<float>([-96, 0, 96]) : body.CentreY;
        b.Cage(body, wall, along, RoomRole.Up, underSky: true);
        // The shaft: the town's 192 square over the cage, lined in its timber from the room's low roof to the sky.
        // The shaft over the cage, its back flush with the alcove's.
        var shaft = Stations.OutRect(body, wall, along - MineShaft.Half, Stations.AlcoveDepth - MineShaft.Size, along + MineShaft.Half, Stations.AlcoveDepth);
        float cx = shaft.CentreX, cy = shaft.CentreY;
        float t = kit.WallDepth, c = Parts.Span(kit);
        foreach (Rect r in Rects.Subtract(new Rect(t, t, c - t, c - t), [shaft]))
            b.WithBox(new Point(r.X0, r.Y0, b.Floor + roof), new Point(r.X1, r.Y1, b.CeilingUnderside), MineShaft.Lining);
        b.WithOutline(shaft).WithSkyCeiling(shaft);
        b.LampAt(cx, cy, roof - 32, MineShaft.Lantern);
        Light(b, body);
        return b.WithFocus(new Point(cx, cy, b.Floor)).Build();
    }

    /// <summary>Levels 2 to 5: a cage station.</summary>
    private static Room Cage(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, RoomSeed rng)
    {
        Rect body = Body(kit);
        CellFace wall = FittingWall(doors);
        var b = Shell(New(name, kit, theme, doors, 2, 5), body, doors)
            .Chamfers(body, 96, Corner.SouthWest, Corner.SouthEast);
        float along = wall == CellFace.North ? body.CentreX + rng.Pick<float>([-128, 0, 128]) : body.CentreY + 64;
        b.Cage(body, wall, along, RoomRole.Up);
        Light(b, body);
        return b.Build();
    }

    /// <summary>Levels 7 to 10: the foot of a rubble ramp up to a blocked tunnel mouth, which takes the party up.</summary>
    private static Room Rubble(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, RoomSeed rng)
    {
        Rect body = Body(kit);
        CellFace wall = FittingWall(doors);
        var b = Shell(New(name, kit, theme, doors, 7, 10), body, doors)
            .Chamfers(body, 128, Corner.SouthWest);
        float along = wall == CellFace.North ? body.CentreX + rng.Pick<float>([-96, 96]) : body.CentreY + 64;
        const int mouth = 128;
        // The mouth: an alcove whose floor is the ramp's top, 128 up.
        Rect alcove = Stations.OutRect(body, wall, along - 96, 0, along + 96, Stations.AlcoveDepth);
        b.WithOutline(alcove).Block(alcove, mouth, b.Theme.Wall);
        Rect ramp = Stations.OutRect(body, wall, along - 96, -256, along + 96, 0);
        b.WithBrush(Brush.Wedge(new Point(ramp.X0, ramp.Y0, b.Floor), new Point(ramp.X1, ramp.Y1, b.Floor + mouth), Toward(wall), b.Theme.Block));
        Rect inside = Stations.OutRect(body, wall, along - 80, 32, along + 80, Stations.AlcoveDepth - 16);
        b.WithTransitionBox(new Point(inside.X0, inside.Y0, b.Floor + mouth), new Point(inside.X1, inside.Y1, b.Floor + mouth + 64))
            .WithHallTrigger(new Point(inside.X0, inside.Y0, b.Floor + mouth), new Point(inside.X1, inside.Y1, b.Floor + mouth + 128));
        var (ax, ay) = Stations.Out(body, wall, along, -320);
        b.WithRole(RoomRole.Up, new Point(ax, ay, b.Floor), Stations.Inward(wall));
        b.Spawns(body, wall, along, -400);
        var (lx, ly) = Stations.Out(body, wall, along, 64);
        b.LampAt(lx, ly, mouth + 96, MineShaft.Lantern);
        Light(b, body);
        return b.Build();
    }

    /// <summary>Levels 12 to 15: under a chute's mouth high in the wall, a bell to be drawn back up.</summary>
    private static Room Chute(string name, RoomKit kit, RoomTheme theme, CellFace[] doors, RoomSeed rng)
    {
        Rect body = Body(kit);
        CellFace wall = FittingWall(doors);
        var b = Shell(New(name, kit, theme, doors, 12, 15), body, doors)
            .Chamfers(body, 160, Corner.SouthWest, Corner.SouthEast);
        float along = wall == CellFace.North ? body.CentreX + rng.Pick<float>([-64, 64]) : body.CentreY + 64;
        const int mouth = 192;
        // The chute: a slide too steep to climb, from a mouth 192 up in an alcove down to the floor.
        Rect alcove = Stations.OutRect(body, wall, along - 96, 0, along + 96, Stations.AlcoveDepth);
        b.WithOutline(alcove).Block(alcove, mouth, b.Theme.Wall);
        Rect slide = Stations.OutRect(body, wall, along - 96, -112, along + 96, 0);
        b.WithBrush(Brush.Wedge(new Point(slide.X0, slide.Y0, b.Floor), new Point(slide.X1, slide.Y1, b.Floor + mouth), Toward(wall), b.Theme.Block));
        // The bell beside the slide's foot.
        Rect bell = Stations.OutRect(body, wall, along + 128, -24, along + 160, 0);
        b.WithButton(new Point(bell.X0, bell.Y0, b.Floor + 48), new Point(bell.X1, bell.Y1, b.Floor + 80));
        Rect volume = Stations.OutRect(body, wall, along - 64, -240, along + 64, -112);
        b.WithTransitionBox(new Point(volume.X0, volume.Y0, b.Floor), new Point(volume.X1, volume.Y1, b.Floor + 64));
        var (ax, ay) = Stations.Out(body, wall, along, -176);
        b.WithRole(RoomRole.Up, new Point(ax, ay, b.Floor), Stations.Inward(wall));
        b.Spawns(body, wall, along, -288);
        var (lx, ly) = Stations.Out(body, wall, along, -160);
        b.LampAt(lx, ly, 256, Parts.ChamberLamp);
        Light(b, body);
        return b.Build();
    }

    /// <summary>The heading from the room toward <paramref name="wall"/>: a ramp against it rises that way.</summary>
    public static Compass Toward(CellFace wall) => wall switch
    {
        CellFace.North => Compass.North,
        CellFace.South => Compass.South,
        CellFace.East => Compass.East,
        _ => Compass.West,
    };
}
