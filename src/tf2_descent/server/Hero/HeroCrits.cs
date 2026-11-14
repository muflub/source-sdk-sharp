using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Mode;
using TF2.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// A hero's crit chance (<see cref="HeroStats.CritChance"/>, what Aim buys)
/// added to TF2's own random crit for the hero's weapon shots (HE3): the
/// weapon chain's crit policy (<see cref="ITFWeaponHook.CritPolicy"/>) adds
/// it as <see cref="TFCritOwner.AddedChance"/>, so TF2's roll compares its
/// draw against stock's chance plus the sheet's, and its bucket and
/// observed-rate check stand as they do for stock's. A sheet with no crit
/// chance adds nothing.
///
/// <para>
/// Server only: the owner's client predicts the roll without the sheet's
/// share (it has no hook that knows it), so a crit the sheet's points won
/// shows when the server's shot arrives, not at the trigger.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroCrits(IDescentModeState mode, Heroes heroes) : ITFWeaponHook
{
    /// <summary>In TF2 Descent's stats block (the chain runs TF2's own hooks first whatever the number).</summary>
    public const int Order = ModStages.First + 300;

    public int HookOrder => Order;

    public TFCritOwner CritPolicy(in TFWeaponHookContext weapon, TFCritOwner policy) =>
        mode.Active && weapon.OwnerIndex > 0 && heroes.Of(weapon.OwnerIndex)?.Stats is { CritChance: > 0 } stats
            ? policy with { AddedChance = policy.AddedChance + (float)stats.CritChance }
            : policy;
}
