using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Mode;
using TF2.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// A downed hero fires nothing (WO6): every TF weapon's chain asks
/// <see cref="ITFWeaponHook.BeforeFire"/> before an attack spends anything,
/// and a downed owner's is vetoed. His movement and buttons are held by TF's
/// controls stun (<see cref="IDownedWorld.Freeze"/>), which this build's
/// weapons do not read; this is the fire's half. The client's twin predicts
/// the same veto from the hero's networked state.
/// </summary>
[SideService(Side.Server)]
public sealed class DownedWeapons(IDescentModeState mode, Heroes heroes) : ITFWeaponHook
{
    /// <summary>In TF2 Descent's downed block (the chain runs TF2's own hooks first whatever the number).</summary>
    public const int Order = ModStages.First + 954;

    public int HookOrder => Order;

    public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack) =>
        !(mode.Active && weapon.OwnerIndex > 0 && heroes.Of(weapon.OwnerIndex)?.Downed == true);
}
