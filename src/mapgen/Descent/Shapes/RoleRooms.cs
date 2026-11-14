namespace SourceSharp.MapGen.Descent.Shapes;

/// <summary>
/// The transition rooms the grey placeholder families share (plan §5, role
/// rooms): a chamber-height plus shape 768 wide (narrower than a boss kit's
/// cell, so a large cell stays cheap) with its role's pieces
/// (<see cref="RoomBuilder.WithRole"/>: the transition volume, the button or
/// hallway trigger, the arrival point west of the centre).
/// </summary>
public static class RoleRooms
{
    /// <summary>The width of a role room's square and arms.</summary>
    public const int Width = 768;

    /// <summary>A role room called <paramref name="name"/> with <paramref name="doors"/>.</summary>
    public static Room Build(string name, RoomKit kit, RoomTheme theme, RoomRole role, CellFace[] doors, RoomEntry entry, string label)
    {
        float m = kit.Cell / 2f;
        var b = new RoomBuilder(name, kit, theme).WithHeight(ChamberShape.Height).WithWidth(Math.Min(kit.Interior, Width))
            .WithDoors(doors)
            .WithEntry(entry)
            .WithMapLabel(label);
        return b.WithRole(role, new Point(m - 192, m, b.Floor), 0)
            .ArmLamps(ShapeParts.ChamberLamp, ChamberShape.Height - 2 * kit.WallDepth - 96)
            .Build();
    }
}
