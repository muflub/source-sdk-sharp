using System.Collections.Immutable;
using System.Text.Json;

namespace TF2.Descent.Rules.Automap;

/// <summary>
/// A party's automap (plan_tf2_rpg 2m, R23): the rooms entered, the door stubs seen, and
/// the markers' live state (shrines used, locked containers opened, the portal, corpses by
/// owner). One map a party: a member's reveals are every member's (<see cref="AutomapRules.Merge"/>).
/// </summary>
public sealed record Automap
{
    public ImmutableSortedSet<string> RoomsSeen { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
    public ImmutableSortedSet<string> DoorsSeen { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
    public ImmutableSortedSet<string> ShrinesUsed { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
    public ImmutableSortedSet<string> ContainersOpened { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    /// <summary>The room the party's portal stands in, if one does.</summary>
    public string? PortalRoom { get; init; }

    /// <summary>Corpses by owner: the room each lies in.</summary>
    public ImmutableSortedDictionary<string, string> Corpses { get; init; } = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

    public static Automap Empty { get; } = new();

    public bool Seen(string room) => RoomsSeen.Contains(room);

    /// <summary>Equal when every set and the portal match (records compare collections by reference).</summary>
    public bool SameAs(Automap other) =>
        RoomsSeen.SetEquals(other.RoomsSeen) && DoorsSeen.SetEquals(other.DoorsSeen) && ShrinesUsed.SetEquals(other.ShrinesUsed)
        && ContainersOpened.SetEquals(other.ContainersOpened) && PortalRoom == other.PortalRoom
        && Corpses.Count == other.Corpses.Count && Corpses.All(c => other.Corpses.TryGetValue(c.Key, out var r) && r == c.Value);
}

/// <summary>What a marker on a drawn map shows.</summary>
public enum MapMarkerKind
{
    Arrival,
    Stairs,
    Portal,
    PartyMember,
    Corpse,
    ShrineUnused,
    ShrineUsed,
    LockedContainer,
}

/// <summary>A room as a client may draw it.</summary>
public sealed record MapRoom(string Id, int Storey);

/// <summary>
/// A door stub in a revealed room: <see cref="To"/> is the room beyond, or null while that
/// room is unentered (the stub shows the door leads onward and says nothing of where).
/// </summary>
public sealed record MapDoor(string Id, string From, string? To);

/// <summary>A marker as a client may draw it; <see cref="Label"/> names the party member a member or corpse marker is for.</summary>
public sealed record MapMarker(MapMarkerKind Kind, string Room, string? Label = null);

/// <summary>What one party member's client receives: revealed rooms, their door stubs, the markers in them.</summary>
public sealed record AutomapView(IReadOnlyList<MapRoom> Rooms, IReadOnlyList<MapDoor> Doors, IReadOnlyList<MapMarker> Markers);

/// <summary>Where a party member stands, for the party-member markers.</summary>
public sealed record MemberPosition(string Member, string Room);

/// <summary>
/// The automap's rules (plan_tf2_rpg 2m; plan_descent_logic RU12). The server reveals and the
/// client only draws: <see cref="View"/> and <see cref="Serialise"/> are the one projection a
/// client gets, and they carry nothing about a room no member has entered. The map of a
/// level is a pure function of the level and the order of what happened on it.
/// </summary>
public static class AutomapRules
{
    /// <summary>A member enters <paramref name="room"/>: it is revealed, with every door it has.</summary>
    public static Automap Enter(Automap map, AutomapLevel level, string room)
    {
        if (!level.HasRoom(room)) throw new ArgumentException($"'{room}' is not a room of this level", nameof(room));
        return map with
        {
            RoomsSeen = map.RoomsSeen.Add(room),
            DoorsSeen = map.DoorsSeen.Union(level.Doors.Where(d => d.Touches(room)).Select(d => d.Id)),
        };
    }

    /// <summary>The party's map: everything either side has seen or changed. Idempotent, commutative.</summary>
    public static Automap Merge(Automap a, Automap b) => new()
    {
        RoomsSeen = a.RoomsSeen.Union(b.RoomsSeen),
        DoorsSeen = a.DoorsSeen.Union(b.DoorsSeen),
        ShrinesUsed = a.ShrinesUsed.Union(b.ShrinesUsed),
        ContainersOpened = a.ContainersOpened.Union(b.ContainersOpened),
        PortalRoom = a.PortalRoom ?? b.PortalRoom,
        Corpses = b.Corpses.Aggregate(a.Corpses, (acc, c) => acc.ContainsKey(c.Key) ? acc : acc.Add(c.Key, c.Value)),
    };

    /// <summary>Every room and door revealed (a shrine's effect, 2l).</summary>
    public static Automap RevealAll(Automap map, AutomapLevel level) =>
        level.Rooms.Aggregate(map, (m, r) => Enter(m, level, r.Id));

    public static Automap UseShrine(Automap map, string shrine) => map with { ShrinesUsed = map.ShrinesUsed.Add(shrine) };

    public static Automap OpenContainer(Automap map, string container) => map with { ContainersOpened = map.ContainersOpened.Add(container) };

    public static Automap PlacePortal(Automap map, string? room) => map with { PortalRoom = room };

    public static Automap PlaceCorpse(Automap map, string owner, string room) => map with { Corpses = map.Corpses.SetItem(owner, room) };

    public static Automap ClearCorpse(Automap map, string owner) => map with { Corpses = map.Corpses.Remove(owner) };

    /// <summary>
    /// What <paramref name="viewer"/>'s client may draw: the revealed rooms, the stubs of
    /// their doors (naming the far room only once it too is revealed), and the markers in
    /// revealed rooms: arrival and stairs, shrines used or not, locked containers not yet
    /// opened, the portal, party members, and the viewer's own corpse only.
    /// </summary>
    public static AutomapView View(Automap map, AutomapLevel level, string viewer, IReadOnlyList<MemberPosition> party)
    {
        var rooms = level.Rooms.Where(r => map.Seen(r.Id)).Select(r => new MapRoom(r.Id, r.Storey)).ToList();
        var doors = new List<MapDoor>();
        foreach (var d in level.Doors.Where(d => map.DoorsSeen.Contains(d.Id)))
        {
            // A stub belongs to a revealed room; the far side is named only when it is revealed too.
            string from = map.Seen(d.RoomA) ? d.RoomA : d.RoomB;
            if (!map.Seen(from)) continue;
            string other = d.Other(from);
            doors.Add(new MapDoor(d.Id, from, map.Seen(other) ? other : null));
        }

        var markers = new List<MapMarker>();
        foreach (var m in level.Markers.Where(m => map.Seen(m.Room)))
        {
            MapMarkerKind? kind = m.Kind switch
            {
                LevelMarkerKind.Arrival => MapMarkerKind.Arrival,
                LevelMarkerKind.Stairs => MapMarkerKind.Stairs,
                LevelMarkerKind.Shrine => map.ShrinesUsed.Contains(m.Id) ? MapMarkerKind.ShrineUsed : MapMarkerKind.ShrineUnused,
                LevelMarkerKind.LockedContainer => map.ContainersOpened.Contains(m.Id) ? null : MapMarkerKind.LockedContainer,
                _ => null,
            };
            if (kind is { } k) markers.Add(new MapMarker(k, m.Room));
        }
        if (map.PortalRoom is { } portal && map.Seen(portal))
            markers.Add(new MapMarker(MapMarkerKind.Portal, portal));
        foreach (var p in party.Where(p => map.Seen(p.Room)).OrderBy(p => p.Member, StringComparer.Ordinal))
            markers.Add(new MapMarker(MapMarkerKind.PartyMember, p.Room, p.Member));
        if (map.Corpses.TryGetValue(viewer, out var corpse) && map.Seen(corpse))
            markers.Add(new MapMarker(MapMarkerKind.Corpse, corpse, viewer));
        return new AutomapView(rooms, doors, markers);
    }

    /// <summary>
    /// The view's bytes, as sent: canonical JSON (fields in a fixed order, no whitespace), so
    /// the same view is always the same bytes and a fact can read what left the server.
    /// </summary>
    public static byte[] Serialise(AutomapView view)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartArray("rooms");
            foreach (var r in view.Rooms)
            {
                w.WriteStartObject();
                w.WriteString("id", r.Id);
                w.WriteNumber("storey", r.Storey);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("doors");
            foreach (var d in view.Doors)
            {
                w.WriteStartObject();
                w.WriteString("id", d.Id);
                w.WriteString("from", d.From);
                if (d.To is { } to) w.WriteString("to", to);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("markers");
            foreach (var m in view.Markers)
            {
                w.WriteStartObject();
                w.WriteString("kind", m.Kind.ToString());
                w.WriteString("room", m.Room);
                if (m.Label is { } label) w.WriteString("label", label);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
