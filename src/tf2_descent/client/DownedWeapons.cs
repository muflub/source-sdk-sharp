using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Descent.Client;

/// <summary>
/// The client's half of the downed hero's fire veto (WO6): the owner's
/// client replays his commands through the same TF weapon chain, so it vetoes
/// a downed hero's attack from his networked state (<see cref="HeroState.Downed"/>),
/// as the server does, and predicts no shot the server will refuse.
/// </summary>
[SideService(Side.Client)]
public sealed class DownedWeaponsClient(IHeroStates heroes) : ITFWeaponHook
{
    /// <summary>In TF2 Descent's downed block, as the server's twin.</summary>
    public const int Order = 1_000_000 + 954;

    public int HookOrder => Order;

    public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack) =>
        !(weapon.OwnerIndex > 0 && heroes.Of(weapon.OwnerIndex)?.Downed == true);
}
