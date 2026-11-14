using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Loot;

//=============================================================================
// What the floor asks of the rest of the server, each behind a seam of its
// own so the floor's facts hold it without an engine: the rooms (which one a
// point is in, and whether it is awake), the party (who shares a kill), the
// ground under a point, and the engine's create.
//=============================================================================

/// <summary>
/// The rooms as the floor needs them (plan_descent_logic WO2, section 3.4):
/// what a sleeping room holds is data, so a drop's entity goes back to the
/// pool when its room sleeps and comes out again when it wakes.
/// </summary>
public interface ILootRooms
{
    /// <summary>The room holding <paramref name="at"/>; -1 when no room does, or the map has no rooms (the town).</summary>
    int RoomAt(Vector at);

    /// <summary>Whether <paramref name="room"/> is awake. No room (-1) is always awake.</summary>
    bool Awake(int room);
}

/// <summary>
/// The rooms as the horde knows them: the level's room for a point, and the
/// roster's awake flag (the embodiment rules wake a room when a hero comes
/// within two rooms and put it to sleep when none has been for a while).
/// With no horde (the town, a map that is no level) every point is in no
/// room, and no room sleeps.
/// </summary>
[SideService(Side.Server, As = [typeof(ILootRooms)])]
public sealed class HordeLootRooms(HordeService horde) : ILootRooms
{
    public int RoomAt(Vector at) =>
        horde.Current is { Begun: true, Level: { } level } ? level.RoomAt(HordeVectors.ToAi(at)) : -1;

    public bool Awake(int room) =>
        room < 0
        || horde.Current is not { Begun: true, Level: { } level, Roster: { } roster }
        || room >= level.RoomCount
        || roster.RoomAwake(room);
}

/// <summary>One hero a kill's loot is rolled for: his client slot, the ledger's name for him and where he stands.</summary>
public sealed record LootMember(int Player, string Hero, Vector Origin);

/// <summary>Who shares a kill (R4), and who a client slot is.</summary>
public interface ILootParty
{
    /// <summary>
    /// The heroes a kill at <paramref name="at"/> pays loot to: each living hero with a sheet within
    /// the party range (experience.json's, the XP's), the killer first when he is one of them, then
    /// by slot; at most the rules' seats (<see cref="Rules.Loot.LootRules.Seats"/>).
    /// </summary>
    IReadOnlyList<LootMember> Near(Vector at, int killer);

    /// <summary>The hero in client slot <paramref name="player"/> as the ledger names him; null for none.</summary>
    string? HeroOf(int player);

    /// <summary>Where the player in slot <paramref name="player"/> stands; null for no living hero there.</summary>
    Vector? Origin(int player);
}

/// <summary>The heroes, the engine's places and the ledger's names (<see cref="HeroKeys"/>).</summary>
[SideService(Side.Server, As = [typeof(ILootParty)])]
public sealed class HeroLootParty(Heroes heroes, HeroKeys keys, IEngineEntities entities, IEnginePlayers players, TF2DescentRules rules)
    : ILootParty
{
    public IReadOnlyList<LootMember> Near(Vector at, int killer)
    {
        double range = rules.Content.Experience.PartyRangeUnits;
        var near = new List<LootMember>();
        foreach (var hero in heroes.All.OrderBy(h => h.PlayerIndex != killer).ThenBy(h => h.PlayerIndex))
        {
            if (hero.Sheet is null || hero.Player == EntityHandle.None || !players.IsAlive(hero.Player))
                continue;
            var origin = entities.GetOrigin(hero.Player);
            if (LootFloor.Distance(origin, at) > range || keys.Of(hero.PlayerIndex) is not { } name)
                continue;
            near.Add(new LootMember(hero.PlayerIndex, name, origin));
            if (near.Count == rules.Loot.Seats)
                break;
        }

        return near;
    }

    public string? HeroOf(int player) => keys.Of(player);

    public Vector? Origin(int player) =>
        heroes.Of(player) is { Player: var p } && p != EntityHandle.None && players.IsAlive(p) ? entities.GetOrigin(p) : null;
}

/// <summary>The floor under a point.</summary>
public interface ILootGround
{
    /// <summary>
    /// The floor below <paramref name="at"/> (a little above it, so a drop does not sink into it);
    /// false when there is none within reach or the point is inside the world.
    /// </summary>
    bool TryGround(Vector at, out Vector ground);
}

/// <summary>A line traced straight down through the world's brushes.</summary>
[SideService(Side.Server, As = [typeof(ILootGround)])]
public sealed class EngineLootGround(IEngineWorldQuery world) : ILootGround
{
    /// <summary>Where the line starts above the point: a body's origin is at its feet, on the floor or a hair into it.</summary>
    public const float Above = 16f;

    /// <summary>How far below the point a floor is looked for: further down is a pit.</summary>
    public const float Below = 1024f;

    /// <summary>How far above the floor the drop stands.</summary>
    public const float Lift = 2f;

    public bool TryGround(Vector at, out Vector ground)
    {
        var tr = world.TraceLine(at + new Vector(0f, 0f, Above), at - new Vector(0f, 0f, Below), TraceMask.Brushes);
        if (!tr.Hit || tr.Fraction <= 0f)
        {
            ground = at;
            return false;
        }

        ground = tr.End + new Vector(0f, 0f, Lift);
        return true;
    }
}

/// <summary>Makes the floor's entities, and counts the engine's.</summary>
public interface ILootSpawner
{
    /// <summary>A <c>tf2_descent_loot</c>, or null when the engine refuses one.</summary>
    LootEntity? Make();

    /// <summary>How many edicts the engine holds now.</summary>
    int Edicts();
}

/// <summary>The engine's create, spawn and directory, and its edict count.</summary>
[SideService(Side.Server, As = [typeof(ILootSpawner)])]
public sealed class EngineLootSpawner(IEngineEntities entities, IEntityDirectory directory) : ILootSpawner
{
    public LootEntity? Make()
    {
        var h = entities.Create(Shared.LootNetState.ClassName);
        if (h == EntityHandle.None)
            return null;

        entities.Spawn(h);
        if (directory.Get(h) is LootEntity made)
            return made;

        entities.Remove(h);
        return null;
    }

    public int Edicts() => entities.Count();
}
