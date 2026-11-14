using SourceSharp.Abi;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Portal;

/// <summary>
/// The map this server is on, as a portal needs it: a level (its depth, seed and, hosted, the
/// host's level copy) or not one (the town).
/// </summary>
public readonly record struct PortalHere(string Map, bool IsLevel, int Depth, ulong? Seed, string LevelId, bool Live)
{
    /// <summary>Whether <paramref name="portal"/> stands on this level: hosted, the same level copy; unhosted, the same map, depth and seed.</summary>
    public bool Holds(PortalRecord portal) =>
        IsLevel && (portal.LevelId.Length > 0 || LevelId.Length > 0
            ? portal.LevelId == LevelId
            : portal.Map == Map && portal.Depth == Depth && portal.Seed == Seed);
}

/// <summary>A moment of a pad's show: TF2's sounds and particles by name.</summary>
public enum PortalShow
{
    /// <summary>A pad was built: the ready sound.</summary>
    Built,

    /// <summary>A hero left through it: the send sound, the team's portal and the sparkles on him.</summary>
    Departed,

    /// <summary>A hero arrived on it: the receive sound and the team's arrival portal.</summary>
    Arrived,
}

/// <summary>
/// The engine as the town portals use it (<see cref="TownPortals"/>): the heroes, the floor in
/// front of one, the pads, setting a hero on a pad, the show and the notices. The facts give a
/// fake; <see cref="EnginePortalWorld"/> is the engine's.
/// </summary>
public interface IPortalWorld
{
    /// <summary>What <paramref name="map"/> is to a portal.</summary>
    PortalHere Here(string map);

    /// <summary>The hero in client slot <paramref name="player"/> as the ledger names him; null for none.</summary>
    string? HeroOf(int player);

    /// <summary>The host's character id of the hero in <paramref name="player"/>; null unhosted or before his join answered.</summary>
    string? CharacterOf(int player);

    /// <summary>Every client slot that holds a hero.</summary>
    IReadOnlyList<int> Heroes();

    bool IsAlive(int player);

    /// <summary>Where the player's feet are; null for no player there.</summary>
    Vector? Feet(int player);

    /// <summary>The player's team (the pads' colour).</summary>
    int Team(int player);

    /// <summary>Where an entrance would stand in front of the player (<see cref="PortalPlacement"/>), or why not.</summary>
    PortalPlace Place(int player);

    /// <summary>Where the <paramref name="index"/>th exit pad stands in town (from 0), and its facing; null when the town has no place for one.</summary>
    (Vector At, float Yaw)? TownSpot(int index);

    /// <summary>Builds <paramref name="portal"/>'s <paramref name="end"/> at <paramref name="at"/>; its entity index, 0 when the engine refused.</summary>
    int MakePad(PortalRecord portal, PortalEnd end, Vector at, float yaw);

    /// <summary>Takes a pad away; one already gone is nothing.</summary>
    void RemovePad(int pad);

    /// <summary>Sets the player on a pad's deck at <paramref name="at"/>, facing <paramref name="yaw"/>, still.</summary>
    void SetOn(int player, Vector at, float yaw);

    void Show(PortalShow show, int pad, int player);

    /// <summary>A line on the player's HUD: a <c>#Descent_Portal_*</c> token whose <c>%s1</c> is <paramref name="arg"/>.</summary>
    void Notice(int player, string token, string arg = "");
}

/// <summary>What a trip through a portal came to: started, or why not; <see cref="Gone"/> when the far end no longer exists.</summary>
public readonly record struct PortalTrip(bool Started, string Why, bool Gone = false)
{
    public static PortalTrip Go => new(true, "");
}

/// <summary>
/// How a hero travels through a portal (<see cref="PortalRoute"/>): hosted through the host's
/// travel (up: <c>IHostLink.Travel</c> with <see cref="TravelDirection.Up"/>; back: the teleporter
/// anchor), unhosted by changing this process's level.
/// </summary>
public interface IPortalRoute
{
    /// <summary>Whether this server is hosted: a level and the town are then two processes.</summary>
    bool Hosted { get; }

    /// <summary>From the level up to the town, for the hero in <paramref name="player"/>.</summary>
    Task<PortalTrip> Up(int player, string hero, PortalRecord portal);

    /// <summary>From the town back to the portal's level and its entrance.</summary>
    Task<PortalTrip> Back(int player, string hero, PortalRecord portal);
}

/// <summary>
/// Who may use a hero's portal besides him (the death plan's Q5: any party member; WO9 builds
/// parties). Its registration is the party's (WO9: PartyPortalParty).
/// </summary>
public interface IPortalParty
{
    bool MayUse(string owner, string hero);
}

/// <summary>
/// A hero's portals are closed for good: both pads go, and the record is Gone. For the death,
/// corpse and party lanes (the death plan keeps a portal through a death, so nothing calls this
/// on a death): a character deleted, or one who leaves the party a portal was shared with.
/// Called by nobody yet.
/// </summary>
public interface IPortalOwnerGone
{
    /// <summary>Closes every open portal of <paramref name="hero"/> (the ledger's name), saying <paramref name="why"/>; how many.</summary>
    int OwnerGone(string hero, string why);
}

/// <summary>The portals as the death and corpse lanes may read them: a hero's open one, if any.</summary>
public interface IPortalRecords
{
    /// <summary>The open portal of <paramref name="hero"/>, or null.</summary>
    PortalRecord? OpenOf(string hero);

    IReadOnlyList<PortalRecord> All { get; }
}
