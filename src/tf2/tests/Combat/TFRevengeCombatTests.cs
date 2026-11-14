using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// The Frontier Justice's tag in the damage layer
/// (<c>CTFShotgun_Revenge::GetCustomDamageType</c>,
/// tf_weapon_shotgun.cpp:200-209): while the owner's window is unspent, the
/// Justice's hit is a shotgun-revenge crit; a spent window names nothing.
/// </summary>
public sealed class TFRevengeCombatTests
{
    /// <summary>The Justice in the hands, its owner's window loaded.</summary>
    private static CombatRig Loaded(int crits)
    {
        var rig = new CombatRig { AttackerWeapon = TFRevengeCrits.ShotgunScript };
        rig.AttackerState.RevengeCrits = crits;
        return rig;
    }

    [Fact]
    public void TheWindowTagsItsShot()
    {
        CombatRig rig = Loaded(2);

        rig.Hit(20f, DamageType.Bullet);

        Assert.Equal(TFDamageCustom.ShotgunRevengeCrit,
                     rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
    }

    [Fact]
    public void TheSpentWindowTagsNothing()
    {
        CombatRig rig = Loaded(0);

        rig.Hit(20f, DamageType.Bullet);

        Assert.Equal(TFDamageCustom.None,
                     rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
    }
}
