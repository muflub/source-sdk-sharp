using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using TF2.Tests.Combat;
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

        // The victim faces away (+X), the spy behind him facing his back. The
        // swing judges and latches; the smack's arm deals (stock's PrimaryAttack
        // calls Swing then Smack in one breath, tf_weapon_knife.cpp:229-231).
        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        var hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(SpyRig.Victim, hit.Victim);
        Assert.Equal(200f, hit.Damage, 4);            // twice the victim's 100 health
        Assert.Equal(TFDamageCustom.Backstab, hit.Custom);
        Assert.Equal(TFWeaponId.Knife, hit.Weapon);
        Assert.True(hit.Critical);

        // The swing remembers, until the next one.
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));
    }

    /// <summary>The line misses (a body at arm's length, no hitbox on it): the
    /// 18-unit box finds the body and the backstab still lands
    /// (DoSwingTraceInternal, tf_weaponbase_melee.cpp:392-).</summary>
    [Fact]
    public void A_swing_whose_line_misses_finds_the_body_with_the_box()
    {
        var rig = new SpyRig { TraceHits = false };
        rig.Engine.ReturnsEachCall("WorldQuery.TraceBox",
            () => new SurfaceTraceResult(true, 0.5f, default, default, SpyRig.Victim, 0, 0));

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        Assert.Equal(TFDamageCustom.Backstab, Assert.Single(rig.Damage.Hits).Custom);
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
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));
        var blocked = Assert.Single(rig.Damage.Hits);
        Assert.Equal(0f, blocked.Damage, 4);                     // zeroed at the gate, :6827
        Assert.Equal(TFDamageCustom.Backstab, blocked.Custom);   // still the backstab scope

        // The attribute was consumed with the block: the next one is whole.
        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));
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
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        // The swing judges and latches; the smack deals the backstab through
        // the seam and the chain's damage read answers 0 on top of it, so the
        // arm's own hit records nothing beside the seam's.
        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.115f);

        Assert.Equal(200f, Assert.Single(rig.Damage.Hits).Damage, 4);
        Assert.Equal(0f, Assert.Single(world.Effects.Swings).Shot.Damage, 4);
    }

    /// <summary>The ported order of stock's <c>PrimaryAttack</c>
    /// (<c>tf_weapon_knife.cpp:195-231</c>): the swing's trace judges the
    /// backstab and latches the victim (:206-211); the damage itself lands in
    /// the smack's melee half, where <c>GetMeleeDamage</c> reads the victim's
    /// health then (:377), flagged <c>TF_DMG_CUSTOM_BACKSTAB</c> (:381). So at
    /// the press only the latch stands; the hit arrives at the smack.</summary>
    [Fact]
    public void The_backstab_judged_at_the_swing_deals_at_the_smack()
    {
        var rig = new SpyRig();
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);            // the swing judges, the latch arms (:209)

        var hit = Assert.Single(rig.Damage.Hits);               // the smack runs in the swing's breath (:229-231)
        Assert.Equal(200f, hit.Damage, 4);                      // the victim's health then, doubled (:377)
        Assert.Equal(TFDamageCustom.Backstab, hit.Custom);      // (:381)
        Assert.True(hit.Critical);                              // IsBackstab answers the roll whole (:490)

        world.Run(w, vm, 0, now: 10.115f);                      // the smack time spends the latch's answer once

        Assert.Single(rig.Damage.Hits);                         // dealt once per swing, not twice
        Assert.Equal(0f, Assert.Single(world.Effects.Swings).Shot.Damage, 4);   // the chain's read answers beside the seam's hit
    }

    /// <summary>Stock's latch (<c>m_hBackstabVictim</c>, :209-210): the
    /// judgment lives on the handle, so the smack's half asks the latch — not
    /// the facing again (:355-382 reads <c>IsBackstab()</c>). A victim who
    /// turns between the swing and the smack still takes the doubled hit.</summary>
    [Fact]
    public void The_latched_victim_takes_the_backstab_after_turning()
    {
        var rig = new SpyRig();
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);            // judged from behind, latched

        rig.VictimAngles = new Vector(0f, 180f, 0f);            // he turns before the smack lands
        world.Run(w, vm, 0, now: 10.115f);

        var hit = Assert.Single(rig.Damage.Hits);               // the latch answers, the facing does not re-judge
        Assert.Equal(200f, hit.Damage, 4);
        Assert.Equal(TFDamageCustom.Backstab, hit.Custom);
    }

    [Fact]
    public void A_front_swing_smacks_the_scripts_damage()
    {
        var rig = new SpyRig();
        rig.VictimAngles = new Vector(0f, 180f, 0f);
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
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
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        Assert.Equal(rig.TraceEnd, Assert.Single(rig.Damage.Hits).Position);
    }

    [Fact]
    public void The_backstab_does_not_ask_whether_the_victim_lives()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;    // the gate is the trace and the dots only (tf_weapon_knife.cpp:199-213)

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));
        Assert.Single(rig.Damage.Hits);
    }

    [Fact]
    public void The_knife_never_touches_the_crit_bucket()
    {
        var rig = new SpyRig();
        rig.VictimAngles = new Vector(0f, 180f, 0f);
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
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
        var knifeWorld = new TFWeaponWorld(new TFKnifeServerHook(new MeltRecorder()));
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

        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
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
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_bat", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.25f);

        // No swing traced through the spy slice, and the bat hits its own damage.
        Assert.Empty(rig.Damage.Hits);
        Assert.Equal(35f, Assert.Single(world.Effects.Swings).Shot.Damage, 4);
    }

    /// <summary>sanguisuge (the Conniver's Kunai), stock's heal arm
    /// (<c>CTFKnife::PrimaryAttack</c>, tf_weapon_knife.cpp:267-289): when the
    /// backstab kills the victim, the spy takes the victim's health before the
    /// hit on top of his own, past the class maximum but capped at three times
    /// it, through <c>TakeHealth</c> with <c>DMG_IGNORE_MAXHEALTH</c>.</summary>
    [Fact]
    public void A_kunai_backstab_that_kills_heals_the_spy()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;   // stock's bSuccessfulBackstab is the dead victim (:243)
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30046, "attr 30046", "sanguisuge",
                                                                  AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                  false, false, AttributeValueType.Default,
                                                                  MergedKeys.Block("k")), 1f));
        rig.Owners.Items[SpyRig.Spy.EntryIndex] = knife;

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        // spyHealth 100 + max(victimHealthBeforeHit 100, 75), under the cap of
        // three times the answered maximum of 100 (:274-276).
        Assert.Equal($"take {SpyRig.Spy.EntryIndex} 100 True", Assert.Single(rig.Resupply.Takes));
    }

    /// <summary>The cap of stock's arm (:274): three times the class maximum,
    /// however much the victim carried.</summary>
    [Fact]
    public void The_kunai_heal_stops_at_three_times_the_class_maximum()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;
        rig.Resupply.MaxAnswer = 40;                       // cap: 120
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30046, "attr 30046", "sanguisuge",
                                                                  AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                  false, false, AttributeValueType.Default,
                                                                  MergedKeys.Block("k")), 1f));
        rig.Owners.Items[SpyRig.Spy.EntryIndex] = knife;

        rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f);
        rig.Spies.Smack(SpyRig.Spy.EntryIndex);

        Assert.Equal($"take {SpyRig.Spy.EntryIndex} 20 True", Assert.Single(rig.Resupply.Takes));
    }

    /// <summary>The gate of stock's arm (:270): the heal needs the kill — a
    /// victim standing after the hit heals no one.</summary>
    [Fact]
    public void A_kunai_backstab_on_a_living_victim_heals_nothing()
    {
        var rig = new SpyRig();
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30046, "attr 30046", "sanguisuge",
                                                                  AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                  false, false, AttributeValueType.Default,
                                                                  MergedKeys.Block("k")), 1f));
        rig.Owners.Items[SpyRig.Spy.EntryIndex] = knife;

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));   // VictimAlive stays true
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        Assert.Empty(rig.Resupply.Takes);
    }

    /// <summary>The attribute half of the gate (:269-270): without the kunai's
    /// own <c>sanguisuge</c> on the knife, a kill heals no one.</summary>
    [Fact]
    public void A_knife_without_the_attribute_heals_no_one_on_a_kill()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        Assert.Empty(rig.Resupply.Takes);
    }

    /// <summary>Big Earner's cloak-on-kill (stock's OnKilledOther_Effects,
    /// tf_player.cpp:11513-11521): with <c>add_cloak_on_kill</c> on the knife,
    /// a kill puts the attribute straight on the spy's cloak meter — forced,
    /// so it lands even where the regen would not, and here onto a meter
    /// parked at 30.</summary>
    [Fact]
    public void A_big_earner_kill_fills_the_cloak_meter()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;                                    // stock's kill is the dead victim
        rig.State.CloakMeter = 30f;
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30070, "attr 30070", "add_cloak_on_kill",
                                                                 AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                 false, false, AttributeValueType.Default,
                                                                 MergedKeys.Block("k")), 25f));
        rig.Owners.Items[SpyRig.Spy.EntryIndex] = knife;

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        Assert.Equal(55f, rig.State.CloakMeter, 4);                 // 30 + the attribute's 25
    }

    /// <summary>The clamp of the fill (tf_player_shared.cpp:9755): the meter
    /// tops out at a hundred, however much the attribute carries.</summary>
    [Fact]
    public void The_cloak_fill_caps_at_a_hundred()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;
        rig.State.CloakMeter = 90f;
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30070, "attr 30070", "add_cloak_on_kill",
                                                                 AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                 false, false, AttributeValueType.Default,
                                                                 MergedKeys.Block("k")), 25f));
        rig.Owners.Items[SpyRig.Spy.EntryIndex] = knife;

        rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f);
        rig.Spies.Smack(SpyRig.Spy.EntryIndex);

        Assert.Equal(100f, rig.State.CloakMeter, 4);
    }

    /// <summary>Big Earner's speed boost (tf_player.cpp:11567-11572): the
    /// weapon's <c>speed_boost_on_kill</c> rides TF_COND_SPEED_BOOST for the
    /// attribute's seconds.</summary>
    [Fact]
    public void A_big_earner_kill_adds_the_speed_boost()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30071, "attr 30071", "speed_boost_on_kill",
                                                                 AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                 false, false, AttributeValueType.Default,
                                                                 MergedKeys.Block("k")), 3f));
        rig.Owners.Items[SpyRig.Spy.EntryIndex] = knife;

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.SpeedBoost));
        Assert.Equal(3f, rig.Conditions.Of(SpyRig.Spy.EntryIndex)!.Duration(TFCond.SpeedBoost), 4);
    }

    /// <summary>Both gates (:11517, :11569): with neither attribute on the
    /// knife, the kill moves no meter and adds no cond.</summary>
    [Fact]
    public void Without_the_kill_attributes_a_kill_adds_nothing()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;
        rig.State.CloakMeter = 30f;

        Assert.True(rig.Spies.Swing(SpyRig.Spy.EntryIndex, 48f));   // no Items entry at all
        Assert.True(rig.Spies.Smack(SpyRig.Spy.EntryIndex));

        Assert.Equal(30f, rig.State.CloakMeter, 4);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.SpeedBoost));
    }

    /// <summary>Your Eternal Reward's disguise (stock's knife tail,
    /// tf_weapon_knife.cpp:243-264 and <c>DisguiseOnKill</c> :315-330): a kill
    /// backstab with the knife holding <c>set_disguise_on_backstab</c> wears
    /// the victim's team and class in place of the plain remove-disguise.</summary>
    [Fact]
    public void A_reward_backstab_disguises_as_the_victim()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;                                    // stock's bSuccessfulBackstab (:243)
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30072, "attr 30072", "set_disguise_on_backstab",
                                                                 AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                 false, false, AttributeValueType.Default,
                                                                 MergedKeys.Block("k")), 1f));
        rig.Owners.Items[SpyRig.Spy.EntryIndex] = knife;
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.115f);                       // the smack deals the hit, the tail rides it (:243)

        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));   // :328 runs the disguise
        rig.Slice.Tick(102.1f);                                              // the think completes
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));
        Assert.Equal((int)TFClass.HeavyWeapons, rig.State.DisguiseClass);    // the victim's class (:326)
        Assert.Equal((int)TFTeam.Blue, rig.State.DisguiseTeam);              // and team (:325)
    }

    /// <summary>The other half of stock's branch (:246, :263): the same kill
    /// backstab without the attribute takes the plain remove-disguise — here
    /// the removal of a disguise worn before the swing.</summary>
    [Fact]
    public void A_plain_knife_kill_backstab_removes_the_disguise_instead()
    {
        var rig = new SpyRig();
        rig.VictimAlive = false;
        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);                                           // the disguise complete before the swing
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));

        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);
        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.115f);                       // the smack deals the hit, the tail rides it (:243)

        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));   // the else arm's RemoveDisguise (:263)
    }

    /// <summary>The Spy-cicle's melted knife (<c>tf_weapon_knife.cpp:521-531</c>):
    /// <c>CanDeploy</c> recomputes the knife from the melt's stamp and window, so a
    /// melted one swings nothing at all until the window closes (:523) — then the
    /// next press answers whole.</summary>
    [Fact]
    public void A_melted_knife_skips_the_swing_until_the_window_closes()
    {
        var rig = new SpyRig();
        var world = new TFWeaponWorld(new TFKnifeServerHook(rig.Spies));
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        rig.Spies.MeltKnife(SpyRig.Spy.EntryIndex, now: 100f, seconds: 10f);   // ApplyOnInjuredAttributes :129-131

        Assert.False(rig.Spies.KnifeReady(SpyRig.Spy.EntryIndex, now: 105f));   // inside the window (:523)
        Assert.True(rig.Spies.KnifeReady(SpyRig.Spy.EntryIndex, now: 110.5f));  // past it (:523)

        world.Run(w, vm, Attack, now: 105f, seed: 1);               // the melted press runs no attack (:525-528)
        world.Run(w, vm, 0, now: 105.05f);
        Assert.Empty(world.Effects.Swings);
        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The Spy-cicle's other half (<c>tf_weaponbase.cpp:5528-5547</c>):
    /// burned or ignited with the Spy-cicle in hand, the holder turns fireproof for
    /// the fixed second (:5536), burns the flame off (:5538-5542), and carries the
    /// afterburn immunity for the item's seconds (:5544). The mark of the melt rides
    /// the same gate (:124).</summary>
    [Fact]
    public void A_burnt_spy_cicle_holder_turns_fireproof_and_burns_the_flame_off()
    {
        var rig = new CombatRig();
        rig.AttackerWeapon = "tf_weapon_flamethrower";                   // the hit's ignite bit (:5534's gate)
        rig.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_knife"; // the knife held in hand (:9504)
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30073, "attr 30073", "become_fireproof_on_hit_by_fire",
                                                                 AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                 false, false, AttributeValueType.Default,
                                                                 MergedKeys.Block("s")), 3f));
        rig.Owners.Items[CombatRig.Victim.EntryIndex] = knife;
        rig.Conditions.Add(CombatRig.Victim, TFCond.Burning);             // the flame was on at the hit (:5538)

        rig.Hit(5f, DamageType.Burn, HitGroup.Chest);

        var conds = rig.Conditions.Of(CombatRig.Victim.EntryIndex)!;
        Assert.True(conds.Has(TFCond.FireImmune));
        Assert.Equal(1f, conds.Duration(TFCond.FireImmune), 3);           // stock's fixed second (:5536)
        Assert.False(conds.Has(TFCond.Burning));                          // the flame-out (:5541)
        Assert.Equal(3f, conds.Duration(TFCond.AfterburnImmune), 3);      // the item's seconds (:5544)
    }

    /// <summary>The same burn without the attribute: none of the three marks lands.</summary>
    [Fact]
    public void A_burnt_plain_knife_holder_keeps_the_flame()
    {
        var rig = new CombatRig();
        rig.AttackerWeapon = "tf_weapon_flamethrower";
        rig.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_knife";
        rig.Owners.Items[CombatRig.Victim.EntryIndex] = new AttributeContainer();   // the plain knife, no hooks
        rig.Conditions.Add(CombatRig.Victim, TFCond.Burning);

        rig.Hit(5f, DamageType.Burn, HitGroup.Chest);

        var conds = rig.Conditions.Of(CombatRig.Victim.EntryIndex)!;
        Assert.False(conds.Has(TFCond.FireImmune));
        Assert.False(conds.Has(TFCond.AfterburnImmune));
        Assert.True(conds.Has(TFCond.Burning));                                     // the igniting arm's own burn stands
    }

    /// <summary>The melt half of the Spy-cicle (<c>tf_weapon_knife.cpp:122-131</c>):
    /// a burning hit on a holder whose knife carries melts_in_fire stamps the melt
    /// window on the spy state from the layer's own clock — the window the fact
    /// above gates at <c>CanDeploy</c> (:523). The walk stands where the burn gate
    /// does (:5534, :124): a knife without the attribute stamps nothing.</summary>
    [Fact]
    public void A_burnt_melting_knife_gets_its_window_stamped_by_the_hit()
    {
        var melts = new MeltRecorder();
        var rig = new CombatRig(spies: melts);
        rig.AttackerWeapon = "tf_weapon_flamethrower";                              // the burn bit's gate (:5534)
        rig.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_knife";           // the knife held in hand (:9504)
        var knife = new AttributeContainer();
        knife.Set(AttributeValue.OfFloat(new AttributeDefinition(30074, "attr 30074", "melts_in_fire",
                                                                 AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                 false, false, AttributeValueType.Default,
                                                                 MergedKeys.Block("m")), 10f));
        rig.Owners.Items[CombatRig.Victim.EntryIndex] = knife;

        rig.Hit(5f, DamageType.Burn, HitGroup.Chest);

        // The stamp rides the layer's clock for the item's seconds (:123-131).
        Assert.Equal([(CombatRig.Victim.EntryIndex, 100f, 10f)], melts.Melted);
    }

    /// <summary>A plain knife in hand takes the same burn with no window stamped.</summary>
    [Fact]
    public void A_burnt_plain_knife_stamps_no_melt_window()
    {
        var plain = new MeltRecorder();
        var rig = new CombatRig(spies: plain);
        rig.AttackerWeapon = "tf_weapon_flamethrower";
        rig.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_knife";
        rig.Owners.Items[CombatRig.Victim.EntryIndex] = new AttributeContainer();   // no hooks on the plain knife
        rig.Hit(5f, DamageType.Burn, HitGroup.Chest);

        Assert.Empty(plain.Melted);
    }

    /// <summary>The spy state as the melt arm's seam: the melt stamps recorded,
    /// every other answer empty.</summary>
    private sealed class MeltRecorder : ITFSpyState
    {
        public List<(int Owner, float Now, float Seconds)> Melted { get; } = [];

        public bool Swing(int ownerIndex, float range) => false;
        public bool Smack(int ownerIndex) => false;
        public bool IsBackstab(int ownerIndex) => false;
        public bool ShouldDisguiseOnBackstab(int ownerIndex) => false;
        public bool DisguiseOnKill(int ownerIndex) => false;
        public void RemoveDisguise(int playerIndex) { }
        public void RemoveInvisibility(int playerIndex) { }
        public bool ActivateInvisibilityWatch(int playerIndex) => false;
        public bool FeignDeathHit(int victimIndex, EntityHandle attacker = default) => false;
        public bool KnifeReady(int ownerIndex, float now) => true;
        public bool DecreaseKnifeRegenerationTime(int ownerIndex, float value) => false;   // the exists gate (:163)
        public void MeltKnife(int ownerIndex, float now, float seconds) => Melted.Add((ownerIndex, now, seconds));
        public bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam) => false;
        public bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined) => false;
    }
}
