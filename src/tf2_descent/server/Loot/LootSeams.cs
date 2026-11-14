using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Loot;

//=============================================================================
// What the floor asks of the rest of the server, each behind a seam of its
// own so the floor's facts hold it without an engine: the party (who shares a
// kill), the ground under a point, and the engine's create. The rooms (which
// one a point is in, and whether it is awake) are the level's IRoomActivity.
//=============================================================================

/// <summary>One hero a kill's loot is rolled for: his client slot, the ledger's name for him and where he stands.</summary>
public sealed record LootMember(int Player, string Hero, Vector Origin);

/// <summary>Who shares a kill (R4), and who a client slot is.</summary>
public interface ILootParty
{
    /// <summary>
    /// The heroes a kill at <paramref name="at"/> pays loot to: each living hero with a sheet within
    /// the party range (experience.json's, the XP's) who is in the killer's party (WO9; a solo
    /// killer is his own party, a stranger in range gets no seat; a kill by no hero pays every hero
    /// in range), the killer first when he is one of them, then by slot; at most the rules' seats
    /// (<see cref="Rules.Loot.LootRules.Seats"/>).
    /// </summary>
    IReadOnlyList<LootMember> Near(Vector at, int killer);

    /// <summary>The hero in client slot <paramref name="player"/> as the ledger names him; null for none.</summary>
    string? HeroOf(int player);

    /// <summary>Where the player in slot <paramref name="player"/> stands; null for no living hero there.</summary>
    Vector? Origin(int player);
}

/// <summary>The heroes, the engine's places, the ledger's names (<see cref="HeroKeys"/>) and the killer's party (<see cref="Party.IPartyRoster"/>).</summary>
[SideService(Side.Server, As = [typeof(ILootParty)])]
public sealed class HeroLootParty(Heroes heroes, HeroKeys keys, IEngineEntities entities, IEnginePlayers players, TF2DescentRules rules,
                                   Party.IPartyRoster roster)
    : ILootParty
{
    public IReadOnlyList<LootMember> Near(Vector at, int killer)
    {
        double range = rules.Content.Experience.PartyRangeUnits;
        var near = new List<LootMember>();
        bool byHero = killer > 0 && heroes.Of(killer) is not null;
        foreach (var hero in heroes.All.OrderBy(h => h.PlayerIndex != killer).ThenBy(h => h.PlayerIndex))
        {
            if (hero.Sheet is null || hero.Player == EntityHandle.None || !players.IsAlive(hero.Player)
                || (byHero && !roster.SameParty(killer, hero.PlayerIndex)))
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
