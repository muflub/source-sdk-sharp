using System.Collections.Immutable;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Furniture;

/// <summary>A marker a room declares: its id (unique on the level) and its <c>poi_type</c>.</summary>
public sealed record FurnitureMarker(string Id, string Poi);

/// <summary>A room and the markers it declares (WO1 reads them from the level).</summary>
public sealed record RoomMarkers(string Room, IReadOnlyList<FurnitureMarker> Markers);

/// <summary>
/// What stands on one marker. <see cref="Variant"/> is the row it is: a shrine effect, a
/// container kind, an explosive, a trap ("dispenser" for a Dispenser). A container may be
/// locked (<see cref="KeyId"/>, whose key drops in <see cref="KeyRoom"/>) and may be trapped
/// (<see cref="Trap"/>).
/// </summary>
public sealed record Placed(
    string Marker,
    string Room,
    FurnitureKind Kind,
    string Variant,
    string? KeyId = null,
    string? KeyRoom = null,
    string? Trap = null);

/// <summary>A level's furniture: one entry per furnished marker, in room then marker order.</summary>
public sealed record Furnishing(ImmutableArray<Placed> Placed)
{
    public IEnumerable<Placed> In(string room) => Placed.Where(p => p.Room == room);
}

/// <summary>
/// Placing furniture (plan_tf2_rpg 2l, R24; plan_descent_logic RU11). Pure in the level
/// seed and the rooms' markers: for each room and each marker kind, the markers are put in a
/// seeded order, at most the kind's per-room limit are considered, and each considered one
/// is furnished with the kind's fill chance. What stands on it, and whether a container is
/// locked or trapped, is rolled from the level seed and the marker, so the same room is
/// furnished differently on another level seed. A locked container's key drops in another
/// room of the level; with no other room, nothing is locked.
/// </summary>
public sealed class FurnishingRules
{
    private readonly DescentContent _content;

    public FurnishingRules(DescentContent content) => _content = content;

    private FurnitureSettings S => _content.Furniture;

    public Furnishing Furnish(ulong levelSeed, IReadOnlyList<RoomMarkers> rooms)
    {
        var placed = ImmutableArray.CreateBuilder<Placed>();
        var allRooms = rooms.Select(r => r.Room).Distinct(StringComparer.Ordinal).ToList();
        foreach (var room in rooms)
        {
            foreach (var rule in S.Markers)
            {
                var ofKind = room.Markers.Where(m => m.Poi == rule.Poi).OrderBy(m => m.Id, StringComparer.Ordinal);
                var order = Seeded.From(levelSeed, "furnish-order", room.Room, rule.Poi).Shuffle(ofKind);
                foreach (var marker in order.Take(rule.PerRoom).OrderBy(m => m.Id, StringComparer.Ordinal))
                {
                    var roll = Seeded.From(levelSeed, "furnish", marker.Id);
                    if (!roll.Chance(rule.FillPct)) continue;
                    placed.Add(Place(room.Room, marker, rule.Kind, ref roll, allRooms));
                }
            }
        }
        return new Furnishing(placed.ToImmutable());
    }

    private Placed Place(string room, FurnitureMarker marker, FurnitureKind kind, ref Seeded roll, List<string> rooms)
    {
        switch (kind)
        {
            case FurnitureKind.Shrine:
                return new Placed(marker.Id, room, kind, S.Shrines[roll.Pick([.. S.Shrines.Select(s => s.Weight)])].Id);
            case FurnitureKind.Dispenser:
                return new Placed(marker.Id, room, kind, "dispenser");
            case FurnitureKind.Explosive:
                return new Placed(marker.Id, room, kind, S.Explosives[roll.Pick([.. S.Explosives.Select(x => x.Weight)])].Id);
            case FurnitureKind.Trap:
                return new Placed(marker.Id, room, kind, PickTrap(ref roll));
            case FurnitureKind.Container:
            {
                var c = S.Containers;
                string variant = c.Kinds[roll.Pick([.. c.Kinds.Select(k => k.Weight)])].Id;
                var others = rooms.Where(r => r != room).ToList();
                bool locked = others.Count > 0 && roll.Chance(c.LockedPct);
                string? keyRoom = locked ? others[roll.Next(others.Count)] : null;
                string? trap = roll.Chance(c.TrappedPct) ? PickTrap(ref roll) : null;
                return new Placed(marker.Id, room, kind, variant, locked ? "key:" + marker.Id : null, keyRoom, trap);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a furniture kind");
        }
    }

    private string PickTrap(ref Seeded roll) => S.Traps[roll.Pick([.. S.Traps.Select(t => t.Weight)])].Id;
}
