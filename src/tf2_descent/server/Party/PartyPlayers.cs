using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Party;

/// <summary>The heroes on this server by client slot and by ledger name: what the roster asks (and what the downed state's roster may ask without a cycle).</summary>
public interface IPartySlots
{
    /// <summary>The slots that hold a hero, in slot order.</summary>
    IReadOnlyList<int> Heroes();

    /// <summary>The ledger name of the hero in <paramref name="slot"/>; null for no hero there (or a hosted one not joined yet).</summary>
    string? HeroOf(int slot);

    /// <summary>The slot of the hero named <paramref name="hero"/>; 0 when he is not on this server.</summary>
    int SlotOf(string hero);
}

/// <summary>The heroes as the party's verbs, views and notices see them: their slots, names, class, level and whether they are down.</summary>
public interface IPartyPlayers
{
    /// <summary>The slots that hold a hero, in slot order.</summary>
    IReadOnlyList<int> Heroes();

    /// <summary>The ledger name of the hero in <paramref name="slot"/>; null for none.</summary>
    string? HeroOf(int slot);

    /// <summary>The slot of the hero named <paramref name="hero"/>; 0 when he is not on this server.</summary>
    int SlotOf(string hero);

    /// <summary>The player's name in <paramref name="slot"/>; empty for none.</summary>
    string NameOf(int slot);

    /// <summary>The hero's TF2 class number (3 is the Soldier); 0 for none.</summary>
    int ClassOf(int slot);

    /// <summary>The hero's level; 0 for none.</summary>
    int LevelOf(int slot);

    /// <summary>Whether the hero in <paramref name="slot"/> is down (WO6).</summary>
    bool IsDown(int slot);
}

/// <summary>The heroes and their ledger names.</summary>
[SideService(Side.Server, As = [typeof(IPartySlots)])]
public sealed class EnginePartySlots(Heroes heroes, HeroKeys keys) : IPartySlots
{
    public IReadOnlyList<int> Heroes() => [.. heroes.All.Select(h => h.PlayerIndex).Where(i => i > 0).Order()];

    public string? HeroOf(int slot) => heroes.Of(slot) is not null ? keys.Of(slot) : null;

    public int SlotOf(string hero)
    {
        foreach (var h in heroes.All)
            if (h.PlayerIndex > 0 && keys.Of(h.PlayerIndex) == hero)
                return h.PlayerIndex;
        return 0;
    }
}

/// <summary>The slots, the engine's player names, the heroes' states and the downed state.</summary>
[SideService(Side.Server, As = [typeof(IPartyPlayers)])]
public sealed class EnginePartyPlayers(IPartySlots slots, Heroes heroes, PlayerNames names, HeroDowned downed) : IPartyPlayers
{
    public IReadOnlyList<int> Heroes() => slots.Heroes();

    public string? HeroOf(int slot) => slots.HeroOf(slot);

    public int SlotOf(string hero) => slots.SlotOf(hero);

    public string NameOf(int slot) => names.NameOf(slot) is { Length: > 0 } n ? n : "player " + slot.ToString(CultureInfo.InvariantCulture);

    public int ClassOf(int slot) => heroes.Of(slot)?.State?.ClassId ?? 0;

    public int LevelOf(int slot) => heroes.Of(slot)?.State?.Level ?? 0;

    public bool IsDown(int slot) => downed.IsDown(slot);
}
