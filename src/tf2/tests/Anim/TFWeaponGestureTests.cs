using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;
using SourceSharp.Tests;

using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Anim;

/// <summary>
/// The gesture table the TF rule claims, per stock's
/// <c>DoAnimationEvent</c> asks: every gun family's shot and every melee
/// swing are the attack, a reload's start is the reload and a shell-by-shell
/// loader's later rounds and finish are the loop and end.
/// </summary>
public sealed class TFWeaponGestureTests
{
    private static TFWeaponGestures Rule() =>
        new(new TFWeaponScripts(new RecordingLog(), WeaponFixtures.ShippedWithClasses()));

    /// <summary>The scattergun's shot (CTFWeaponBaseGun::PrimaryAttack through FireProjectile's tail).</summary>
    [Fact]
    public void TheScattergunsShotIsTheAttack()
        => Assert.Equal(PlayerAnimEvent.AttackPrimary, Rule().Gesture(WeaponGestureTrigger.Shot, "tf_weapon_scattergun"));

    /// <summary>A rocket is fired through the same tail: the projectile families answer too.</summary>
    [Fact]
    public void TheRocketLaunchersShotIsTheAttack()
        => Assert.Equal(PlayerAnimEvent.AttackPrimary, Rule().Gesture(WeaponGestureTrigger.Shot, "tf_weapon_rocketlauncher"));

    /// <summary>The minigun's firing loop is that same path per round (tf_weapon_minigun.cpp:372).</summary>
    [Fact]
    public void TheMinigunsShotIsTheAttack()
        => Assert.Equal(PlayerAnimEvent.AttackPrimary, Rule().Gesture(WeaponGestureTrigger.Shot, "tf_weapon_minigun"));

    /// <summary>A swing says so on a hit and a miss alike (CTFWeaponBaseMelee::Smash).</summary>
    [Fact]
    public void TheBatsSwingIsTheAttack()
        => Assert.Equal(PlayerAnimEvent.AttackPrimary, Rule().Gesture(WeaponGestureTrigger.Swing, "tf_weapon_bat"));

    /// <summary>The airblast's ask is the SECONDARY (FireAirBlast :981); no other TF secondary claims one.</summary>
    [Fact]
    public void TheAirblastsAskIsTheSecondary()
    {
        Assert.Equal(PlayerAnimEvent.AttackSecondary, Rule().Gesture(WeaponGestureTrigger.Secondary, "tf_weapon_flamethrower"));
        Assert.Null(Rule().Gesture(WeaponGestureTrigger.Secondary, "tf_weapon_pistol"));
    }

    /// <summary>The wind-up and the settle are the minigun's and the flame's PRE and POST (:276, :910, :749, :607).</summary>
    [Fact]
    public void TheWindUpAndSettleAreTheMinigunsAndTheFlamesPreAndPost()
    {
        Assert.Equal(PlayerAnimEvent.AttackPre, Rule().Gesture(WeaponGestureTrigger.WindUp, "tf_weapon_minigun"));
        Assert.Equal(PlayerAnimEvent.AttackPost, Rule().Gesture(WeaponGestureTrigger.WindDown, "tf_weapon_minigun"));
        Assert.Equal(PlayerAnimEvent.AttackPre, Rule().Gesture(WeaponGestureTrigger.WindUp, "tf_weapon_flamethrower"));
        Assert.Equal(PlayerAnimEvent.AttackPost, Rule().Gesture(WeaponGestureTrigger.WindDown, "tf_weapon_flamethrower"));
        // The arms are the burning kinds': a gun's wind-down is its idle, no event.
        Assert.Null(Rule().Gesture(WeaponGestureTrigger.WindUp, "tf_weapon_scattergun"));
        Assert.Null(Rule().Gesture(WeaponGestureTrigger.WindDown, "tf_weapon_scattergun"));
    }

    /// <summary>The burn's loop re-arm is the ATTACK_PRIMARY (tf_weapon_flamethrower.cpp:779).</summary>
    [Fact]
    public void TheBurnsRearmIsTheAttack()
        => Assert.Equal(PlayerAnimEvent.AttackPrimary, Rule().Gesture(WeaponGestureTrigger.SustainedFire, "tf_weapon_flamethrower"));

    /// <summary>A reload's start is the reload event, whatever the shape (DefaultReload :2219, ReloadSingly's first shell :2004).</summary>
    [Fact]
    public void AReloadStartsInTheAttackWeaponsGesture()
    {
        Assert.Equal(PlayerAnimEvent.Reload, Rule().Gesture(WeaponGestureTrigger.ReloadStart, "tf_weapon_scattergun"));
        Assert.Equal(PlayerAnimEvent.Reload, Rule().Gesture(WeaponGestureTrigger.ReloadStart, "tf_weapon_pistol"));
    }

    /// <summary>A shell-by-shell loader's later rounds are the loop and its finish the end (:2008, :2089).</summary>
    [Fact]
    public void TheScattergunsShellsLoopAndItsFinishEnds()
    {
        Assert.Equal(PlayerAnimEvent.ReloadLoop, Rule().Gesture(WeaponGestureTrigger.ReloadShell, "tf_weapon_scattergun"));
        Assert.Equal(PlayerAnimEvent.ReloadEnd, Rule().Gesture(WeaponGestureTrigger.ReloadEnd, "tf_weapon_scattergun"));
    }

    /// <summary>A whole-clip reload sends no shell or end: stock's DefaultReload stops at the one RELOAD.</summary>
    [Fact]
    public void ThePistolsReloadHoldsNoShell()
    {
        Assert.Null(Rule().Gesture(WeaponGestureTrigger.ReloadShell, "tf_weapon_pistol"));
        Assert.Null(Rule().Gesture(WeaponGestureTrigger.ReloadEnd, "tf_weapon_pistol"));
    }

    /// <summary>The medigun fires no attack and the flame's ask is deferred: neither claims a shot.</summary>
    [Fact]
    public void TheMedigunClaimsNoShot()
        => Assert.Null(Rule().Gesture(WeaponGestureTrigger.Shot, "tf_weapon_medigun"));

    /// <summary>A class the table does not name (an HL2MP weapon, the core's own) takes no claim: the core default stands.</summary>
    [Fact]
    public void AForeignClassTakesNoClaim()
        => Assert.Null(Rule().Gesture(WeaponGestureTrigger.Shot, "weapon_smg1"));
}
