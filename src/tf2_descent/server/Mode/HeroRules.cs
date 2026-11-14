using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Mode;

/// <summary>
/// No friendly fire between heroes (<see cref="DescentMode"/>), whatever
/// <c>mp_friendlyfire</c> says: a player's hit on a teammate is refused. One's
/// own damage still lands (a rocket jump), and a hit from or on anything that
/// is not a player is not this rule's.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroFriendlyFire(IDescentModeState mode, IEnginePlayers players, IEngineEntities entities)
    : IDamageRule, IOrderedHook
{
    /// <summary>After TF2's damage rules (999,700s), at TF2 Descent's first mode stage.</summary>
    public const int Order = ModStages.First + 100;

    public int HookOrder => Order;

    public Decision Decide(in DamageContext hit)
    {
        if (!mode.Active || hit.Attacker == EntityHandle.None || hit.Attacker == hit.Victim)
            return Decision.UseStockRule;

        if (!entities.IsPlayer(hit.Attacker) || !entities.IsPlayer(hit.Victim))
            return Decision.UseStockRule;

        int team = players.GetTeam(hit.Victim);
        return team > (int)TFTeam.Spectator && team == players.GetTeam(hit.Attacker)
            ? Decision.No
            : Decision.UseStockRule;
    }
}

/// <summary>
/// TF2's loadout screen, <c>tf2_equip</c> and <c>tf2_give</c> are closed to
/// heroes (<see cref="ITFLocalInventoryGate"/>): they read and write TF2's
/// local inventory, and a hero's kit comes from Descent's own items.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroInventoryGate(IDescentModeState mode) : ITFLocalInventoryGate, IOrderedHook
{
    /// <summary>What the log says when a hero asks.</summary>
    public const string Reason = "a hero's kit is TF2 Descent's items, not TF2's inventory";

    /// <summary>After TF2's band, at TF2 Descent's first mode stage.</summary>
    public const int Order = ModStages.First + 100;

    public int HookOrder => Order;

    public bool IsOpen(int entIndex, out string reason)
    {
        bool open = !mode.Active;
        reason = open ? string.Empty : Reason;
        return open;
    }
}

/// <summary>
/// A hero plays his character's class (<see cref="ITFClassChangeGate"/>):
/// while the Descent mode plays, a hero's own <c>joinclass</c> (the class
/// menu's) may choose only the class of his hero sheet (<see cref="IHeroClass"/>),
/// and any other is refused with <see cref="Reason"/>. The mode's own
/// placement (<see cref="HeroPlacement"/>) is the game's choice, which asks no
/// gate. A bot is no hero and is not this gate's.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroClassGate(IDescentModeState mode, IEnginePlayers players, IBotPlayers bots, IHeroClass heroClass)
    : ITFClassChangeGate, IOrderedHook
{
    /// <summary>What the hero is told; it names no class, since a refused <c>random</c> has none.</summary>
    public const string Reason = "#Descent_Hero_ClassIsTheSheets";

    /// <summary>After TF2's band, at TF2 Descent's first mode stage.</summary>
    public const int Order = ModStages.First + 100;

    public int HookOrder => Order;

    public bool MayChoose(int entIndex, TFClass cls, out string reason)
    {
        reason = string.Empty;
        if (!mode.Active || bots.IsBot(players.GetHandle(entIndex)) || cls == heroClass.ClassOf(entIndex))
            return true;

        reason = Reason;
        return false;
    }
}
