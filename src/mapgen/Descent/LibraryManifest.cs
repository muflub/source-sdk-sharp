using System.Text;
using System.Text.Json;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// What the game knows of a room that the map tools do not (task A12): its
/// tags, weight, depth range, whether it is a jump room or a low one. The
/// markers and doors in its <c>library.json</c> entry are read off the room.
/// </summary>
/// <param name="Tags">Tags from <see cref="LibraryManifestWriter.KnownTags"/>.</param>
/// <param name="Weight">How often the layout draws it, 1 or more.</param>
/// <param name="MinDepth">The shallowest level it may stand in, 1 to 15.</param>
/// <param name="MaxDepth">The deepest.</param>
/// <param name="Jump">R19: the room holds a rocket-jump volume (lint measures it from the brushes).</param>
/// <param name="Low">R19: a low, tight room, where the ground game wins.</param>
public sealed record RoomEntry(IReadOnlyList<string> Tags, int Weight = 1, int MinDepth = 1, int MaxDepth = LibraryManifestWriter.Depths,
                               bool Jump = false, bool Low = false)
{
    /// <summary>
    /// A quiet room (brief p9): no pack spawn, its name's flag <c>q</c>, placed by the layout only
    /// beside the way in (<see cref="RoomCatalogue.QuietTwins"/>).
    /// </summary>
    public bool Quiet { get; init; }

    /// <summary>No tags, weight 1, every depth.</summary>
    public static RoomEntry Default { get; } = new([]);
}

/// <summary>
/// Writes a library's <c>library.json</c> for the host
/// (<c>../source-sdk-sharp-host</c>, <c>TF2.Descent.MapForge/LibraryManifest.cs</c>
/// at 9a70c1c): <c>mod</c>, <c>library</c>, and per room <c>tags</c>,
/// <c>weight</c>, <c>depths</c> [min, max], <c>jump</c>, <c>low</c> and
/// <c>markers</c>, the fields its lint reads; plus <c>doors</c> (the walls a
/// room opens on) and <c>height</c>, which plan task G1 asks the host to
/// read and which it ignores until then, <c>breakables</c> (how many
/// breakable points it holds, M34, which <c>descent-level</c> sums against
/// the level cap), and <c>focus</c> (where a
/// visitor's eye goes, room-local) and <c>cell</c>, which the preview's
/// per-room cameras read (<see cref="DescentLevel.Cameras"/>).
/// </summary>
public static class LibraryManifestWriter
{
    /// <summary>The mod the manifest is for.</summary>
    public const string Mod = "tf2_descent";

    /// <summary>The shaft's depth (plan M1).</summary>
    public const int Depths = 15;

    /// <summary>The host's <c>LibraryManifest.KnownTags</c>; its lint refuses any other tag.</summary>
    public static IReadOnlyList<string> KnownTags { get; } = ["start", "stairs", "boss_arena", "treasure", "corridor", "junction", "shrine"];

    /// <summary>The manifest of <paramref name="rooms"/>, a library called <paramref name="library"/>, in the rooms' order.</summary>
    public static string Write(string library, IEnumerable<Room> rooms)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteString("mod", Mod);
            w.WriteString("library", library);
            w.WriteStartObject("rooms");
            foreach (Room room in rooms)
            {
                RoomEntry e = room.Entry;
                Check(room.Name, e);
                w.WriteStartObject(room.Name);
                w.WriteStartArray("tags");
                foreach (string tag in e.Tags)
                    w.WriteStringValue(tag);
                w.WriteEndArray();
                w.WriteNumber("weight", e.Weight);
                w.WriteStartArray("depths");
                w.WriteNumberValue(e.MinDepth);
                w.WriteNumberValue(e.MaxDepth);
                w.WriteEndArray();
                w.WriteBoolean("jump", e.Jump);
                w.WriteBoolean("low", e.Low);
                w.WriteStartArray("markers");
                foreach (string marker in MarkersOf(room))
                    w.WriteStringValue(marker);
                w.WriteEndArray();
                w.WriteStartArray("doors");
                foreach (CellFace face in DoorOrder.Where(room.Doors.Contains))
                    w.WriteStringValue(RoomBuilder.WallName(face));
                w.WriteEndArray();
                w.WriteNumber("height", room.Height);
                w.WriteStartArray("focus");
                w.WriteNumberValue(room.Focus.X);
                w.WriteNumberValue(room.Focus.Y);
                w.WriteNumberValue(room.Focus.Z);
                w.WriteEndArray();
                w.WriteNumber("cell", room.Kit.Cell);
                w.WriteNumber("breakables", Breakables.Of(room).Count);
                if (room.Cells > 1)
                    w.WriteNumber("cells", room.Cells);
                var (at, yaw) = PreviewCamera.Of(room);
                w.WriteStartArray("camera");
                w.WriteNumberValue(at.X);
                w.WriteNumberValue(at.Y);
                w.WriteNumberValue(at.Z);
                w.WriteNumberValue(yaw);
                w.WriteEndArray();
                if (room.Arena is { } arena)
                    WriteArena(w, arena);
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>
    /// A boss arena's <c>arena</c> entry, room-local (<see cref="ArenaData"/>): <c>boss</c> [x, y, z, yaw],
    /// <c>track</c> [[x, y, z, flags], ...] in order, <c>mouths</c> [[x, y, z, yaw, leg], ...].
    /// <c>descent-level</c> turns it with the room into the level's <c>.arena</c> file.
    /// </summary>
    private static void WriteArena(Utf8JsonWriter w, ArenaData arena)
    {
        w.WriteStartObject("arena");
        w.WriteStartArray("boss");
        foreach (float v in (float[])[arena.Boss.X, arena.Boss.Y, arena.Boss.Z, arena.BossYaw])
            w.WriteNumberValue(v);
        w.WriteEndArray();
        w.WriteStartArray("track");
        foreach (TrackPoint p in arena.Track)
        {
            w.WriteStartArray();
            foreach (float v in (float[])[p.At.X, p.At.Y, p.At.Z, p.Flags])
                w.WriteNumberValue(v);
            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WriteStartArray("mouths");
        foreach (ArenaMouth m in arena.Mouths)
        {
            w.WriteStartArray();
            foreach (float v in (float[])[m.At.X, m.At.Y, m.At.Z, m.Yaw, m.Leg])
                w.WriteNumberValue(v);
            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>
    /// The marker names a room's entry lists: each Descent marker's <c>poi_type</c> once, in order, its
    /// breakables' among them (the server reads those markers too, M34).
    /// </summary>
    public static IReadOnlyList<string> MarkersOf(Room room) =>
        [.. room.Markers.Select(m => Markers.PoiType(m.Kind)).Concat(Breakables.Of(room).Select(b => b.Key("poi_type")!))
            .Distinct().Order(StringComparer.Ordinal)];

    private static readonly CellFace[] DoorOrder = [CellFace.East, CellFace.West, CellFace.North, CellFace.South];

    private static void Check(string room, RoomEntry e)
    {
        foreach (string tag in e.Tags.Where(t => !KnownTags.Contains(t)))
            throw new ArgumentException($"room {room}: tag '{tag}' is not one of {string.Join(", ", KnownTags)}");
        if (e.Weight < 1)
            throw new ArgumentException($"room {room}: weight {e.Weight} is below 1");
        if (e.MinDepth < 1 || e.MaxDepth > Depths || e.MinDepth > e.MaxDepth)
            throw new ArgumentException($"room {room}: depths [{e.MinDepth}, {e.MaxDepth}] are not 1 <= min <= max <= {Depths}");
        if (e.Jump && e.Low)
            throw new ArgumentException($"room {room}: a room is a jump room or a low one, not both");
    }
}
