using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The knife's chain links (<see cref="TFKnifeHook"/>) over the real spy
/// slice, and the backstab itself
/// (<c>CTFKnife::PrimaryAttack</c> over <c>IsBehindAndFacingTarget</c>,
/// tf_weapon_knife.cpp:180-280, 447-482).
/// </summary>
public sealed class TFKnifeTests
{
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>A command seed whose melee roll draws in the crit band.</summary>
    private static int CritSeed => TFWeaponWorld.MeleeSeed(0, 1500);

    [Fact]
    public void A_backstab_from_behind_kills_flagged()
    {
        var rig = new SpyRig();

        // The victim faces away (+X), the spy behind him facing his back.
        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));

        var hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(SpyRig.Victim, hit.Victim);
        Assert.Equal(200f, hit.Damage, 4);            // twice the victim's 100 health
        Assert.Equal(TFDamageCustom.Backstab, hit.Custom);
        Assert.Equal(TFWeaponId.Knife, hit.Weapon);
        Assert.True(hit.Critical);

        // The swing remembers, until the next one.
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));
    }

    /// <summary>Razorback's one block (stock's <c>CTFPlayer::CheckBlockBackstab</c>,
    /// tf_player.cpp:10072-10116, applied at tf_gamerules.cpp:6827): the victim's
    /// first item holding <c>set_blockbackstab_once</c> zeroes the backstab once,
    /// then hides itself (the stock item's <c>EF_NODRAW</c>), so the next swing
    /// lands whole.</summary>
    [Fact]
    public void ARazorbackBlocksOneBackstab()
    {
        var rig = new SpyRig();
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5100, "attr 5100", "set_blockbackstab_once",
                                                                   AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                   false, false, AttributeValueType.Default,
                                                                   MergedKeys.Block("x")), 1f));
        rig.Owners.Managers[SpyRig.Victim.EntryIndex] = manager;

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        var blocked = Assert.Single(rig.Damage.Hits);
        Assert.Equal(0f, blocked.Damage, 4);                     // zeroed at the gate, :6827
        Assert.Equal(TFDamageCustom.Backstab, blocked.Custom);   // still the backstab scope

        // The attribute was consumed with the block: the next one is whole.
        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.Equal(2, rig.Damage.Hits.Count);
        Assert.Equal(200f, rig.Damage.Hits[1].Damage, 4);
    }

    [Fact]
    public void A_swing_on_the_front_is_a_normal_hit_no_kill()
    {
        var rig = new SpyRig();
        rig.VictimAngles = new Vector(0f, 180f, 0f);  // facing the spy

        Assert.False(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));

        Assert.Empty(rig.Damage.Hits);
        Assert.False(rig.Spies.Smack(SpyRig.Spy.EntryIndex));
    }

    [Fact]
    public void A_swing_on_a_teammate_or_past_them_deals_nothing()
    {
        var rig = new SpyRig();

        rig.TraceHits = false;
        Assert.False(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));

        rig.TraceHits = true;
        rig.VictimTeam = (int)TFTeam.Red;
        Assert.False(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void A_backstabs_smack_lands_nothing_the_kill_went_through_the_seam()
    {
        var rig = new SpyRig();
        var world = new TFWeaponWorld(new TFKnifeHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        // The swing: the backstab leaves through the seam. The smack: the
        // chain's damage read answers 0, so the arm's hit records nothing.
        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.115f);

        Assert.Equal(200f, Assert.Single(rig.Damage.Hits).Damage, 4);
        Assert.Equal(0f, Assert.Single(world.Effects.Swings).Shot.Damage, 4);
    }

    [Fact]
    public void A_front_swing_smacks_the_scripts_damage()
    {
        var rig = new SpyRig();
        rig.VictimAngles = new Vector(0f, 180f, 0f);
        var world = new TFWeaponWorld(new TFKnifeHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.115f);

        Assert.Empty(rig.Damage.Hits);
        Assert.Equal(40f, Assert.Single(world.Effects.Swings).Shot.Damage, 4);
        Assert.False(Assert.Single(world.Effects.Swings).Shot.Critical);
    }

    [Fact]
    public void A_backstab_lands_at_the_traces_end()
    {
        var rig = new SpyRig();
        // A point on the victim's back, short of his centre: stock's melee
        // Swing dispatches at the trace's endpos
        // (tf_weaponbase_melee.cpp:820-880).
        rig.TraceEnd = new Vector(60f, 2f, 63f);

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));

        Assert.Equal(rig.TraceEnd, Assert.Single(rig.Damage.Hits).Position);
    }

    [Fact]
    public void The_backstab_does_not_ask_whether_the_victim_lives()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;    // the gate is the trace and the dots only (tf_weapon_knife.cpp:199-213)

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.Single(rig.Damage.Hits);
    }

    [Fact]
    public void The_knife_never_touches_the_crit_bucket()
    {
        var rig = new SpyRig();
        rig.VictimAngles = new Vector(0f, 180f, 0f);
        var world = new TFWeaponWorld(new TFKnifeHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.115f);

        // CTFKnife::CalcIsAttackCriticalHelper answers IsBackstab() whole
        // (tf_weapon_knife.cpp:487-500): the melee helper never runs, so no
        // check is counted and no damage is paid into the bucket.
        var knife = world.Parts.Buckets.For(TFWeaponWorld.WeaponEntity,
                                            world.Catalog.IndexOf("tf_weapon_knife"), 300f);
        Assert.Equal(1, knife.Checks);
        Assert.Equal(300f, knife.Tokens, 4);

        // The control: the bat's roll counts its check and pays its damage in.
        var batWorld = new TFWeaponWorld();
        var (bw, bvm) = batWorld.Armed("tf_weapon_bat", clip: -1, reserve: 0);
        batWorld.Run(bw, bvm, Attack, now: 10f, seed: CritSeed);
        var bat = batWorld.Parts.Buckets.For(TFWeaponWorld.WeaponEntity,
                                             batWorld.Catalog.IndexOf("tf_weapon_bat"), 300f);
        Assert.Equal(2, bat.Checks);
    }

    [Fact]
    public void The_knife_never_rolls_a_crit()
    {
        // The control: the bat on the same crit seed rolls one.
        var batWorld = new TFWeaponWorld();
        var (bw, bvm) = batWorld.Armed("tf_weapon_bat", clip: -1, reserve: 0);
        batWorld.Run(bw, bvm, Attack, now: 10f, seed: CritSeed);
        batWorld.Run(bw, bvm, 0, now: 10.25f);
        Assert.True(Assert.Single(batWorld.Effects.Swings).Shot.Critical);

        // The knife's chance is scaled to zero (CalcIsAttackCriticalHelper
        // answers only IsBackstab): the same seed draws nothing.
        var knifeWorld = new TFWeaponWorld(new TFKnifeHook());
        var (kw, kvm) = knifeWorld.Armed("tf_weapon_knife", clip: -1, reserve: 0);
        knifeWorld.Run(kw, kvm, Attack, now: 10f, seed: CritSeed);
        knifeWorld.Run(kw, kvm, 0, now: 10.115f);
        Assert.False(Assert.Single(knifeWorld.Effects.Swings).Shot.Critical);
    }

    [Fact]
    public void Attacking_ends_the_disguise_and_the_cloak()
    {
        var rig = new SpyRig();
        rig.VictimAngles = new Vector(0f, 180f, 0f);
        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);
        rig.State.CloakMeter = 100f;
        Assert.True(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));

        var world = new TFWeaponWorld(new TFKnifeHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);
        world.Run(w, vm, Attack, now: 10f, seed: 1);

        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.DisguiseWearingoff));
    }

    [Fact]
    public void The_knife_links_touch_nothing_but_the_knife()
    {
        var rig = new SpyRig();
        var world = new TFWeaponWorld(new TFKnifeHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_bat", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.25f);

        // No swing traced through the spy slice, and the bat hits its own damage.
        Assert.Empty(rig.Damage.Hits);
        Assert.Equal(35f, Assert.Single(world.Effects.Swings).Shot.Damage, 4);
    }
}
