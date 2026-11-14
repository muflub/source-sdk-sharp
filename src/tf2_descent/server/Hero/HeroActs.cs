using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Mode;
using TF2.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// A hero's shot or swing is an act (<see cref="HeroComponent.Acted"/>): TF2's weapon chain
/// tells of each after it fired (<see cref="ITFWeaponHook.AfterFire"/>, the server's live run
/// only), and the downed state ends a respawn's grace on it. A cast counts in the skills
/// runtime.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroActTaps(IDescentModeState mode, Heroes heroes) : ITFWeaponHook
{
    /// <summary>In TF2 Descent's downed block (the weapon chain runs TF2's own hooks first whatever the number).</summary>
    public const int Order = ModStages.First + 956;

    public int HookOrder => Order;

    public void AfterFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (!mode.Active || !attack.EffectsLive || weapon.OwnerIndex <= 0)
            return;
        heroes.Of(weapon.OwnerIndex)?.Acted();
    }
}
