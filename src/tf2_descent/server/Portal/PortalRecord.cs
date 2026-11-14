using System.Globalization;

using SourceSharp.Abi;

namespace TF2.Descent.Server.Portal;

/// <summary>Where a town portal is in its one trip.</summary>
public enum PortalState
{
    /// <summary>Standing: its entrance on the level, its exit in town. Either end may be used.</summary>
    Open,

    /// <summary>Used back down once: both pads are gone, and a second return is refused.</summary>
    Spent,

    /// <summary>Its level is gone (left for another level, the instance ended), or it was moved or closed: refused, its town pad removed.</summary>
    Gone,
}

/// <summary>
/// One town portal (plan_descent_logic WO4, ruling L10): whose it is, the level it stands on and
/// where, and where it is in its trip. It lives as long as the server, across every map change
/// (<see cref="TownPortals"/>), so it holds no entity: a pad is the map's, made from the record
/// on each map it belongs to and forgotten when the map goes.
/// </summary>
public sealed class PortalRecord
{
    /// <summary>The server's number for it, from 1; the pad's net state carries it.</summary>
    public required int Id { get; init; }

    /// <summary>The hero who read the scroll, as the ledger names him (<c>HeroKeys</c>).</summary>
    public required string Owner { get; init; }

    /// <summary>The host's character id of the owner (the anchor's owner); empty unhosted.</summary>
    public string Character { get; init; } = "";

    /// <summary>The level's map, as this server loaded it.</summary>
    public required string Map { get; init; }

    public required int Depth { get; init; }

    /// <summary>The level's seed, null while it has none (a hosted level before Booting answers).</summary>
    public ulong? Seed { get; init; }

    /// <summary>The host's level copy the portal stands on (<c>BootInfo.LevelId</c>, the anchor's level); empty unhosted.</summary>
    public string LevelId { get; init; } = "";

    /// <summary>The entrance pad's place on the floor.</summary>
    public required Vector Position { get; init; }

    /// <summary>The entrance pad's facing, degrees.</summary>
    public required float Yaw { get; init; }

    /// <summary>The owner's team, for the pads' colour.</summary>
    public int Team { get; init; }

    public PortalState State { get; internal set; } = PortalState.Open;

    /// <summary>Why it is <see cref="PortalState.Gone"/> or <see cref="PortalState.Spent"/>; empty while open.</summary>
    public string Note { get; internal set; } = "";

    /// <summary>
    /// The hero to be set on a pad of the next map that has one (<see cref="ArrivalEnd"/>): who
    /// went up and is to stand on the exit in town, or who came back and is to stand at the
    /// entrance's place. Null when nobody is on his way.
    /// </summary>
    public string? Arriving { get; internal set; }

    /// <summary>The end <see cref="Arriving"/> lands on.</summary>
    public Shared.PortalEnd ArrivalEnd { get; internal set; }

    /// <summary>Trips made through it: up, and the one back.</summary>
    public int Trips { get; internal set; }

    /// <summary>One line for the dev listing (<c>tf2_descent_portals</c>).</summary>
    public string Line() => string.Create(CultureInfo.InvariantCulture,
        $"portal {Id}: {State}{(Note.Length > 0 ? " (" + Note + ")" : "")}, owner {Owner}{(Character.Length > 0 ? " character " + Character : "")}, " +
        $"{Map} depth {Depth} seed {(Seed is { } s ? s.ToString(CultureInfo.InvariantCulture) : "none")}" +
        $"{(LevelId.Length > 0 ? " level " + LevelId : "")}, entrance at ({Position.X:0} {Position.Y:0} {Position.Z:0}) yaw {Yaw:0}, " +
        $"trips {Trips}{(Arriving is { } a ? $", {a} arriving at the {ArrivalEnd.ToString().ToLowerInvariant()}" : "")}");
}
