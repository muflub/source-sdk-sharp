using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
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
