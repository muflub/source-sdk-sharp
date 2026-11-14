using TF2.Descent.Rules.Layout;
using LayoutShape = TF2.Descent.Rules.Layout.RoomShape;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// A library room's name as the layout reads it (plan decision E8):
/// <c>&lt;family&gt;_&lt;variant&gt;-&lt;doors&gt;-&lt;height&gt;[-&lt;flags&gt;]</c>,
/// spelled by the rules module's own codec (<see cref="RoomName.Format"/>) from
/// the room's facts: the walls its doors are on, its height class from
/// <c>room_height</c>, <c>j</c> when its <c>library.json</c> entry says jump,
/// and a flag for each tier above T1 whose feature it holds. An author writes
/// only <c>family_variant</c>; <see cref="RoomCatalogue"/> adds the rest.
/// </summary>
public static class LayoutNames
{
    /// <summary>The highest low room (plan §3.1).</summary>
    public const int LowMax = 256;

    /// <summary>The highest chamber.</summary>
    public const int ChamberMax = 512;

    /// <summary>The highest hall; anything taller is a shaft.</summary>
    public const int HallMax = 1024;

    /// <summary>The height class of a room <paramref name="height"/> high.</summary>
    public static HeightClass HeightClassOf(int height) => height switch
    {
        <= LowMax => HeightClass.Low,
        <= ChamberMax => HeightClass.Chamber,
        <= HallMax => HeightClass.Hall,
        _ => HeightClass.Shaft,
    };

    /// <summary>The codec's door set of a room's walls (east is +x, north is +y in both frames).</summary>
    public static Doors DoorsOf(IEnumerable<CellFace> faces)
    {
        Doors doors = Doors.None;
        foreach (CellFace face in faces)
            doors |= face switch
            {
                CellFace.East => Doors.East,
                CellFace.West => Doors.West,
                CellFace.North => Doors.North,
                _ => Doors.South,
            };
        return doors;
    }

    /// <summary>The flags of a room: <c>j</c> from its entry, the tiers from the features it holds.</summary>
    public static JumpFlags FlagsOf(Room room)
    {
        JumpFlags flags = room.Entry.Jump ? JumpFlags.Jump : JumpFlags.None;
        foreach (JumpTier tier in room.JumpTiers)
            flags |= tier switch
            {
                JumpTier.S2 => JumpFlags.S2,
                JumpTier.S3 => JumpFlags.S3,
                JumpTier.B1 => JumpFlags.B1,
                JumpTier.B2 => JumpFlags.B2,
                JumpTier.B3 => JumpFlags.B3,
                _ => JumpFlags.None,
            };
        return flags;
    }

    /// <summary>
    /// The layout facts of a room its author named <c>family_variant</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The author's name is not <paramref name="family"/>_ and a variant.</exception>
    public static LayoutShape ShapeOf(Room room, string family)
    {
        string prefix = family + "_";
        if (!room.Name.StartsWith(prefix, StringComparison.Ordinal) || room.Name.Length == prefix.Length)
            throw new ArgumentException($"family {family} made a room called {room.Name}; its rooms are named {family}_<variant>");
        return new LayoutShape(family, room.Name[prefix.Length..], DoorsOf(room.Doors), HeightClassOf(room.Height), FlagsOf(room))
        {
            CellsX = room.Cells,
            CellsY = room.Cells,
            SocketMask = room.SocketEdges is { } edges
                ? edges.Aggregate(0UL, (m, s) => m | LayoutShape.SocketBit(DoorsOf([s.Face]), s.Offset))
                : 0,
        };
    }

    /// <summary>The codec's name of a room its author named <c>family_variant</c>.</summary>
    public static string Name(Room room, string family) => RoomName.Format(ShapeOf(room, family));
}
