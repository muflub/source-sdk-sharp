using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Rules;
using TF2.Tests.Projectiles;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFDamageLayer"/> and <see cref="TFDamageRules"/> over the rig:
/// the weapon's crit flag tripled once, the range from the two players'
/// centres, the headshot, TF's fall damage, the records and the ignition.
/// </summary>
public sealed class TFDamageLayerTests
{
    private const DamageType Ranged = DamageType.Bullet | TFDamageBits.UseDistanceMod;

    [Fact]
    public void The_convars_start_at_stocks_defaults()
    {
        var rig = new CombatRig();
        Assert.Equal(0.5f, rig.ConVars.Declared<TFDamageRangeConVar>().Value);
        Assert.True(rig.ConVars.Declared<TFDamageDisableSpreadConVar>().Value);
        Assert.False(rig.ConVars.Declared<TFFallDamageDisableSpreadConVar>().Value);
    }

    // ---- the crit, once -----------------------------------------------------

    [Fact]
    public void A_crit_the_weapon_rolled_is_tripled_once_by_the_layer()
    {
        // The weapon flags its crit at the script's damage (TFWeaponBehaviour.Critical);
        // the layer multiplies. 6 per scattergun pellet, crit-boosted: 18, not 54.
        var world = new TFWeaponWorld();
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.CritboostedUserBuff);
        var (w, vm) = world.Armed("tf_weapon_scattergun", clip: 6, reserve: 32);
        ShotDamage shot = world.Behaviour("tf_weapon_scattergun").ModifyShot(world.Attack(w, vm, 10f, TFWeaponWorld.GunSeed(200, 10000)), default);
        Assert.Equal(new ShotDamage(6f, true), shot);

        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun" };
        float crit = rig.Hit(shot.Damage, Ranged, critical: shot.Critical);
        float plain = rig.Hit(shot.Damage, Ranged);

        Assert.Equal(18f, crit, 4);
        Assert.Equal(6f, plain, 4);
    }

    // ---- the range ----------------------------------------------------------

    [Theory]
    [InlineData(512f, 10f)]       // c = 0.5
    [InlineData(256f, 13.4375f)]  // c = 0.75
    [InlineData(768f, 6.5625f)]   // c = 0.25
    [InlineData(1500f, 5.28f)]    // c = 0: the band floor plus the spread
    public void The_range_is_the_distance_between_the_two_players_centres(float x, float expected)
    {
        var rig = new CombatRig { AttackerAt = new Vector(x, 0f, 0f) };
        Assert.Equal(expected, rig.Hit(10f, Ranged), 4);
    }

    [Fact]
    public void Self_damage_has_no_range_and_no_crit()
    {
        var rig = new CombatRig();
        Assert.Equal(10f, rig.Hit(10f, Ranged, critical: true, attacker: CombatRig.Victim), 4);
    }

    [Fact]
    public void A_shotguns_point_blank_takes_the_near_bonus_not_the_damp()
    {
        // g_aWeaponDamageTypes: the scattergun is DMG_BUCKSHOT |
        // DMG_USEDISTANCEMOD — no DMG_NOCLOSEDISTANCEMOD — so the near ramp
        // applies to its point-blank hits, and the fire path's plain bullet
        // does not stand in for the weapon's own set. 6 base at 128 of the
        // 512 optimal puts the band at 0.775..0.975; the disablespread value
        // 0.875 with the scattergun's 1.5x bonus lands 6 + spline(4.5).
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(128f, 0f, 0f) };

        rig.Hit(6f, DamageType.Bullet);

        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Scattergun), record.Bits & ~TFDamageBits.Critical);
        Assert.Equal(6f + TFDamageModifyRules.SimpleSplineRemapValClamped(0.875f, 0f, 1f, -4.5f, 4.5f),
                     record.Damage, 4);
    }

    [Fact]
    public void A_hit_on_a_player_without_a_carrier_passes_untouched()
    {
        var rig = new CombatRig();
        Assert.Equal(10f, rig.Hit(10f, Ranged, critical: true, victim: new EntityHandle(0x0001_0009u)), 4);
        Assert.Null(rig.Records.LastHit(9));
    }

    [Fact]
    public void The_victims_conditions_mini_crit_through_the_layer()
    {
        var rig = new CombatRig();
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Markedfordeath));
        Assert.Equal(13.5f, rig.Hit(10f, Ranged), 4);
        Assert.Equal(TFCritType.Mini, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Crit);
    }

    // ---- the airborne state (CTFPlayer::InAirDueToExplosion, tf_player.h:599)

    private static SsPlayer RigPlayer(CombatRig rig, EntityHandle handle)
    {
        var player = new SsPlayer(rig.Engine, rig.Engine, TestServices.Impacts(rig.Engine), TestServices.Fluid(), TestServices.InfiniteAux(), TestServices.PrintDamage())
            { Handle = handle };
        player.State.BindLocal();
        return player;
    }

    [Fact]
    public void A_victim_off_the_ground_and_blastjumping_takes_the_airborne_mini()
    {
        var rig = new CombatRig();
        var victim = RigPlayer(rig, CombatRig.Victim);
        rig.Entities.Placed[CombatRig.Victim] = victim;
        rig.AttackerItem("mini_crit_airborne", AttributeDescriptionFormat.Or, 1f);
        victim.State.Flags = 0u;
        victim.State.WaterLevel = 0;
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Blastjumping));

        rig.Hit(10f, Ranged);

        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.Mini, record.Crit);
        Assert.Equal(13.5f, record.Damage, 4);
    }

    [Fact]
    public void A_grounded_or_waterlogged_victim_is_no_airborne_mini()
    {
        var rig = new CombatRig();
        var victim = RigPlayer(rig, CombatRig.Victim);
        rig.Entities.Placed[CombatRig.Victim] = victim;
        rig.AttackerItem("mini_crit_airborne", AttributeDescriptionFormat.Or, 1f);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Blastjumping));

        victim.State.Flags = GameMovement.FlOnGround;
        rig.Hit(10f, Ranged);
        Assert.Equal(TFCritType.None, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Crit);

        victim.State.Flags = 0u;
        victim.State.WaterLevel = GameMovement.WlWaist;
        rig.Hit(10f, Ranged);
        Assert.Equal(TFCritType.None, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Crit);
    }

    [Fact]
    public void The_rocketpacks_cond_is_airborne_on_a_grounded_player()
    {
        // The OR's second prong: TF_COND_ROCKETPACK alone, feet on the ground.
        var rig = new CombatRig();
        var victim = RigPlayer(rig, CombatRig.Victim);
        rig.Entities.Placed[CombatRig.Victim] = victim;
        rig.AttackerItem("mini_crit_airborne", AttributeDescriptionFormat.Or, 1f);
        victim.State.Flags = GameMovement.FlOnGround;
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Rocketpack));

        rig.Hit(10f, Ranged);

        Assert.Equal(TFCritType.Mini, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Crit);
    }

    [Fact]
    public void An_attacker_blastjumping_crits_through_the_layer_and_a_grounded_one_does_not()
    {
        var rig = new CombatRig();
        var attacker = RigPlayer(rig, CombatRig.Attacker);
        rig.Entities.Placed[CombatRig.Attacker] = attacker;
        rig.AttackerItem("crit_while_airborne", AttributeDescriptionFormat.Or, 1f);
        rig.AttackerState.SetConditions(TFCondMask.Of(TFCond.Blastjumping));

        attacker.State.Flags = 0u;
        Assert.Equal(30f, rig.Hit(10f, Ranged), 4);
        Assert.Equal(TFCritType.Full, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Crit);

        attacker.State.Flags = GameMovement.FlOnGround;
        rig.Hit(10f, Ranged);
        var second = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.None, second.Crit);
        Assert.Equal(10f, second.Damage, 4);
    }

    [Fact]
    public void A_deflected_rockets_blast_mini_crits_through_its_scope()
    {
        var rig = new CombatRig();

        // The deflected rocket, as the blast leaves it: the air blast's own
        // deflection, then the projectile in the directory where the layer's
        // inflictor cast looks.
        var projectiles = new ProjectileRig();
        TFRocket rocket = projectiles.Rocket();
        rocket.DeflectBy(new EntityHandle(0x0001_0000u | 7), new Vector(0, 0, 1100), deflectorCritBoosted: false);
        Assert.True(rocket.Deflected);
        rig.Entities.Placed[rocket.Handle] = rocket;

        using (rig.Inflictors.Push(rocket.Handle, default))
            rig.Hit(10f, Ranged);

        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.Mini, record.Crit);
        Assert.Equal(13.5f, record.Damage, 4);
    }

    [Fact]
    public void An_undeflected_rockets_blast_is_plain_on_the_scope()
    {
        var rig = new CombatRig();

        var projectiles = new ProjectileRig();
        TFRocket rocket = projectiles.Rocket();
        Assert.False(rocket.Deflected);
        rig.Entities.Placed[rocket.Handle] = rocket;

        using (rig.Inflictors.Push(rocket.Handle, default))
            rig.Hit(10f, Ranged);

        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.None, record.Crit);
        Assert.Equal(10f, record.Damage, 4);
    }

    [Fact]
    public void A_deflected_pipes_blast_mini_crits_through_its_scope()
    {
        var rig = new CombatRig();

        // The deflected pipe, as the blast leaves it: the same air blast's
        // deflection, then the pipe in the directory where the layer's
        // inflictor cast looks (the grenade arm of tf_gamerules.cpp:6159,
        // ShouldMiniCritOnReflect being the regular mode this pipe is).
        var projectiles = new ProjectileRig();
        TFPipebomb pipe = projectiles.Pipebomb();
        pipe.DeflectedBy(new EntityHandle(0x0001_0000u | 7), deflectorCritBoosted: false, "tf_flamethrower");
        Assert.Equal(1, pipe.DeflectedCount);
        rig.Entities.Placed[pipe.Handle] = pipe;

        using (rig.Inflictors.Push(pipe.Handle, default))
            rig.Hit(10f, Ranged);

        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.Mini, record.Crit);
        Assert.Equal(13.5f, record.Damage, 4);
    }

    [Fact]
    public void An_undeflected_pipes_blast_is_plain_on_the_scope()
    {
        var rig = new CombatRig();

        var projectiles = new ProjectileRig();
        TFPipebomb pipe = projectiles.Pipebomb();
        Assert.Equal(0, pipe.DeflectedCount);
        rig.Entities.Placed[pipe.Handle] = pipe;

        using (rig.Inflictors.Push(pipe.Handle, default))
            rig.Hit(10f, Ranged);

        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.None, record.Crit);
        Assert.Equal(10f, record.Damage, 4);
    }

    [Fact]
    public void A_deflected_stickys_blast_stays_plain_on_the_scope()
    {
        // The remote-detonate grenade fails ShouldMiniCritOnReflect
        // (tf_weapon_grenade_pipebomb.cpp:167-170), so the deflected count
        // alone does not raise the sticky's own blast.
        var rig = new CombatRig();

        var projectiles = new ProjectileRig();
        TFStickybomb sticky = projectiles.Sticky();
        sticky.DeflectedBy(new EntityHandle(0x0001_0000u | 7), new Vector(0f, 0f, 600f));
        Assert.Equal(1, sticky.DeflectedCount);
        rig.Entities.Placed[sticky.Handle] = sticky;

        using (rig.Inflictors.Push(sticky.Handle, default))
            rig.Hit(10f, Ranged);

        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.None, record.Crit);
        Assert.Equal(10f, record.Damage, 4);
    }

    [Fact]
    public void A_rocket_specialists_direct_hit_keeps_the_far_damage_whole()
    {
        // The 2048-unit hit's long-range decrease lands only without the
        // attribute (tf_gamerules.cpp:6299-6306): 90 - 42.48 = 47.52 against
        // the direct hit's 90.
        var (plain, plainRocket) = FarRocketRig(specialist: false);
        using (plain.Inflictors.Push(plainRocket.Handle, default))
            Assert.Equal(47.52f, plain.Hit(90f, DamageType.Blast | TFDamageBits.HalfFalloff | TFDamageBits.UseDistanceMod), 2);

        var (specialist, specialistRocket) = FarRocketRig(specialist: true);
        using (specialist.Inflictors.Push(specialistRocket.Handle, default))
            Assert.Equal(90f, specialist.Hit(90f, DamageType.Blast | TFDamageBits.HalfFalloff | TFDamageBits.UseDistanceMod), 2);
    }

    /// <summary>
    /// A rig whose attacker stands 2048 units off in rocket-launcher hands,
    /// the inflictor a rocket whose touch was the victim — stock's
    /// <c>GetEnemy() == pVictim</c>, the touch run for its
    /// <see cref="TFRocket.DirectVictim"/>.
    /// </summary>
    private static (CombatRig Rig, TFRocket Rocket) FarRocketRig(bool specialist)
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_rocketlauncher", AttackerAt = new Vector(2048f, 0f, 0f) };
        if (specialist)
            rig.AttackerItem("rocket_specialist", AttributeDescriptionFormat.Additive, 2);

        var projectiles = new ProjectileRig();
        TFRocket rocket = projectiles.Rocket();
        rocket.OnTouch(CombatRig.Victim);
        rig.Entities.Placed[rocket.Handle] = rocket;
        return (rig, rocket);
    }

    // ---- the headshot -------------------------------------------------------

    private static CombatRig Sniper(bool zoomed)
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_sniperrifle" };
        if (zoomed)
            rig.AttackerState.SetConditions(TFCondMask.Of(TFCond.Zoomed));
        return rig;
    }

    [Fact]
    public void A_zoomed_sniper_rifles_headshot_crits_and_is_a_headshot()
    {
        var rig = Sniper(zoomed: true);
        float damage = rig.Hit(150f, DamageType.Bullet, HitGroup.Head);

        Assert.Equal(450f, damage, 3);
        var hit = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal((TFDamageCustom.Headshot, TFCritType.Full), (hit.Custom, hit.Crit));
        Assert.True((hit.Bits & TFDamageBits.Critical) != 0);
    }

    [Fact]
    public void A_body_shot_does_its_damage_and_no_more()
    {
        var rig = Sniper(zoomed: true);
        Assert.Equal(150f, rig.Hit(150f, DamageType.Bullet, HitGroup.Chest), 3);
        Assert.Equal(TFDamageCustom.None, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
    }

    [Fact]
    public void An_unzoomed_rifle_and_any_other_weapon_have_no_hit_locations()
    {
        Assert.Equal(50f, Sniper(zoomed: false).Hit(50f, DamageType.Bullet, HitGroup.Head), 3);

        var scattergun = new CombatRig { AttackerWeapon = "tf_weapon_scattergun" };
        scattergun.AttackerState.SetConditions(TFCondMask.Of(TFCond.Zoomed));
        Assert.Equal(6f, scattergun.Hit(6f, DamageType.Bullet, HitGroup.Head), 3);
    }

    [Fact]
    public void An_unzoomed_sniper_rifles_hit_carries_no_hit_locations()
    {
        // CTFSniperRifle::GetDamageType (tf_weapon_sniperrifle.cpp:907-917):
        // the rifle strips DMG_USE_HITLOCATIONS unless its owner is zoomed.
        // The bits ride the record (damagebits), so the record names them.
        var unzoomed = Sniper(zoomed: false);
        unzoomed.Hit(50f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(DamageType.Bullet,
                     unzoomed.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);

        var zoomed = Sniper(zoomed: true);
        zoomed.Hit(50f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(DamageType.Bullet | TFDamageBits.UseHitLocations,
                     zoomed.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);
    }

    [Fact]
    public void The_smg_and_revolver_carry_hit_locations_when_their_mode_is_one()
    {
        // CTFSMG::GetDamageType (tf_weapon_smg.cpp:69-78) and
        // CTFRevolver::GetDamageType (tf_weapon_revolver.cpp:83-92): the
        // weapon's set_weapon_mode 1 adds DMG_USE_HITLOCATIONS to its row.
        // The bits ride the record (damagebits), so the record names them.
        var smg = new CombatRig { AttackerWeapon = "tf_weapon_smg" };
        smg.Hit(8f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Smg),
                     smg.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);
        smg.AttackerItem("set_weapon_mode", AttributeDescriptionFormat.Additive, 1f);
        smg.Hit(8f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Smg) | TFDamageBits.UseHitLocations,
                     smg.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);

        var revolver = new CombatRig { AttackerWeapon = "tf_weapon_revolver" };
        revolver.AttackerItem("set_weapon_mode", AttributeDescriptionFormat.Additive, 1f);
        revolver.Hit(20f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Revolver) | TFDamageBits.UseHitLocations,
                     revolver.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);
    }

    [Fact]
    public void The_scouts_back_headshot_pistol_carries_hit_locations()
    {
        // CTFPistol_ScoutSecondary::GetDamageType (tf_weapon_pistol.cpp:250-259):
        // back_headshot on the item adds it.
        var pistol = new CombatRig { AttackerWeapon = "tf_weapon_handgun_scout_secondary" };
        pistol.Hit(15f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.HandgunScoutSecondary),
                     pistol.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);

        pistol.AttackerItem("back_headshot", AttributeDescriptionFormat.Additive, 1f);
        pistol.Hit(15f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.HandgunScoutSecondary) | TFDamageBits.UseHitLocations,
                     pistol.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);
    }

    [Fact]
    public void A_headshot_kills_a_scout_a_body_shot_does_not()
    {
        // An uncharged shot's 50 (TF_WEAPON_SNIPERRIFLE_DAMAGE_MIN): a scout's 125 hp
        // (the class script) dies to the head's 150 only.
        var head = Sniper(zoomed: true).Hit(50f, DamageType.Bullet, HitGroup.Head);
        var body = Sniper(zoomed: true).Hit(50f, DamageType.Bullet, HitGroup.Stomach);

        Assert.True(head >= 125f, $"the headshot did {head}");
        Assert.True(body < 125f, $"the body shot did {body}");
    }

    [Fact]
    public void A_headshot_too_soon_after_the_zoom_is_a_body_shot()
    {
        // CanFireCriticalShot: GetFOVTime under TF_WEAPON_SNIPERRIFLE_NO_CRIT_AFTER_ZOOM_TIME
        // 0.2 s ago refuses the crit (tf_weapon_sniperrifle.cpp:1050-1057).
        var rig = Sniper(zoomed: true);
        rig.ZoomTimes.Record(CombatRig.Attacker.EntryIndex, rig.Clock.Now - 0.1f);

        float damage = rig.Hit(150f, DamageType.Bullet, HitGroup.Head);

        Assert.Equal(150f, damage, 3);
        Assert.Equal(TFDamageCustom.None, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
    }

    [Fact]
    public void A_headshot_a_quarter_second_after_the_zoom_crits()
    {
        var rig = Sniper(zoomed: true);
        rig.ZoomTimes.Record(CombatRig.Attacker.EntryIndex, rig.Clock.Now - 0.25f);

        float damage = rig.Hit(150f, DamageType.Bullet, HitGroup.Head);

        Assert.Equal(450f, damage, 3);
        var hit = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal((TFDamageCustom.Headshot, TFCritType.Full), (hit.Custom, hit.Crit));
    }

    [Fact]
    public void A_set_weapon_mode_one_rifle_never_crits_a_headshot()
    {
        // CanFireCriticalShot :1020-1023: the jarate rifle (GetRifleType's
        // set_weapon_mode 1) returns false before the headshot arms, however
        // long the zoom has been up.
        var rig = Sniper(zoomed: true);
        rig.ZoomTimes.Record(CombatRig.Attacker.EntryIndex, rig.Clock.Now - 0.25f);
        rig.AttackerItem("set_weapon_mode", AttributeDescriptionFormat.Additive, 1f);

        float damage = rig.Hit(150f, DamageType.Bullet, HitGroup.Head);

        Assert.Equal(150f, damage, 3);
        Assert.Equal(TFDamageCustom.None, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
    }

    [Fact]
    public void A_no_full_charge_rifles_headshot_needs_the_charge_full()
    {
        // CanFireCriticalShot :1029-1035: sniper_no_headshot_without_full_charge
        // refuses the crit until m_flChargedDamage reaches the 150 cap (the
        // charge is the active weapon's, IsFullyCharged :565-568).
        var rig = Sniper(zoomed: true);
        rig.ZoomTimes.Record(CombatRig.Attacker.EntryIndex, rig.Clock.Now - 0.25f);
        rig.AttackerItem("sniper_no_headshot_without_full_charge", AttributeDescriptionFormat.Additive, 1f);
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();
        rig.Entities.WeaponEntity.State.BehaviourMeter = rig.Clock.Now - 1f;   // 50 of the 150 cap

        Assert.Equal(150f, rig.Hit(150f, DamageType.Bullet, HitGroup.Head), 3);
        Assert.Equal(TFDamageCustom.None, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);

        rig.Entities.WeaponEntity.State.BehaviourMeter = rig.Clock.Now - 3.5f; // past the cap
        Assert.Equal(450f, rig.Hit(150f, DamageType.Bullet, HitGroup.Head), 3);
        Assert.Equal(TFDamageCustom.Headshot, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
    }

    [Fact]
    public void Sniper_crit_no_scope_crits_a_headshot_before_the_zoom_is_a_quarter_second_old()
    {
        // CanFireCriticalShot :1037-1055: with sniper_crit_no_scope the zoom's
        // 0.2 s gate is skipped — a headshot the instant after the zoom crits.
        var rig = Sniper(zoomed: true);
        rig.ZoomTimes.Record(CombatRig.Attacker.EntryIndex, rig.Clock.Now - 0.1f);
        rig.AttackerItem("sniper_crit_no_scope", AttributeDescriptionFormat.Additive, 1f);

        float damage = rig.Hit(150f, DamageType.Bullet, HitGroup.Head);

        Assert.Equal(450f, damage, 3);
        Assert.Equal(TFDamageCustom.Headshot, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
    }

    // ---- the fall -----------------------------------------------------------

    [Fact]
    public void A_landing_does_tfs_fall_damage_not_the_cores()
    {
        // 900 u/s: the core's MoveMath.FallDamage is HL2's; TF's is 5 * 900 / 300 * 125 / 100
        // = 18.75 for a scout, times the roll's middle (1.0).
        var rig = new CombatRig(roll: 0.5f);
        float damage = rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);

        Assert.Equal(18.75f, damage, 3);
        Assert.Equal((0.8f, 1.2f), Assert.Single(rig.Random.Asked));
    }

    [Fact]
    public void A_landing_under_650_does_nothing_in_tf()
    {
        var rig = new CombatRig();
        Assert.Equal(0f, rig.Hit(MoveMath.FallDamage(640f), DamageType.Fall, attacker: EntityHandle.None));
    }

    // ---- the records --------------------------------------------------------

    [Fact]
    public void An_enemy_hit_enters_the_history_a_teammates_does_not()
    {
        var enemy = new CombatRig();
        enemy.Hit(10f, Ranged);
        Assert.Equal(CombatRig.Attacker, enemy.Records.RecentDamager(CombatRig.Victim.EntryIndex, 0, 3f, enemy.Clock.Now));

        var friend = new CombatRig { AttackerTeam = (int)TFTeam.Red };
        friend.Hit(10f, Ranged);
        Assert.Equal(EntityHandle.None, friend.Records.RecentDamager(CombatRig.Victim.EntryIndex, 0, 3f, friend.Clock.Now));
        Assert.NotNull(friend.Records.LastHit(CombatRig.Victim.EntryIndex));
    }

    [Fact]
    public void A_sentrys_hit_records_the_pair_for_the_kill_rules_assist()
    {
        // A real sentry, for the type the note asks the inflictor to be.
        var buildings = new TF2.Tests.Buildings.BuildingsRig();
        var sentry = buildings.Spawn<TFSentryGun>(TFSentryGun.MapClass);

        var rig = new CombatRig { AttackerWeapon = "tf_weapon_pistol" };
        rig.Entities.Placed[sentry.Handle] = sentry;
        using (rig.Inflictors.Push(sentry.Handle, default))
            rig.Hit(10f, Ranged);

        Assert.Equal(sentry.Handle,
                     rig.SentryDamages.DamagedBy(CombatRig.Victim.EntryIndex, CombatRig.Attacker, rig.Clock.Now));
    }

    [Fact]
    public void A_hit_without_a_sentry_inflictor_records_nothing()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_pistol" };
        rig.Hit(10f, Ranged);

        Assert.Equal(EntityHandle.None,
                     rig.SentryDamages.DamagedBy(CombatRig.Victim.EntryIndex, CombatRig.Attacker, rig.Clock.Now));
    }

    // ---- the ignition -------------------------------------------------------

    [Fact]
    public void An_igniting_hit_sets_the_victim_alight_and_the_afterburn_ticks_from_the_attacker()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.Equal(3.4f, slice.Burn.AfterburnDuration, 4);

        for (float t = 100f; t <= 100.6f; t += 1f / 66f)
            slice.Tick(t);

        var tick = Assert.Single(rig.Damage.Hits);
        Assert.Equal((CombatRig.Victim, CombatRig.Attacker, 4f, TFBurn.TickDamageType, TFDamageCustom.Burning, TFWeaponId.Flamethrower, false), tick);
    }

    [Fact]
    public void A_burn_survives_the_players_condition_countdown()
    {
        // The burn's conditions are the player's: added through the one
        // writer with stock's AddCond( TF_COND_BURNING, -1 ), so the
        // countdown the player's ticker runs every frame leaves them be.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        var set = rig.Conditions.Of(CombatRig.Victim.EntryIndex)!;

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        set.Tick(1f / 66f, healers: 0, default);

        Assert.True(set.Has(TFCond.Burning));
        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.True(set.Has(TFCond.HealingDebuff));
    }

    [Fact]
    public void The_healing_debuff_expires_with_the_weapons_initial_afterburn()
    {
        // Burn's first ignition (tf_player_shared.cpp:6645-6653):
        // AddCond( TF_COND_HEALING_DEBUFF, m_flAfterburnDuration ) with the
        // weapon's GetInitialAfterburnDuration — the flamethrower's 3 s — so
        // the debuff goes before the fire's 3.4 s does.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        var set = rig.Conditions.Of(CombatRig.Victim.EntryIndex)!;

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        for (float t = 100f; t <= 103.2f; t += 1f / 66f)
        {
            slice.Tick(t);
            set.Tick(1f / 66f, healers: 0, default);
        }

        Assert.False(set.Has(TFCond.HealingDebuff));
        Assert.True(set.Has(TFCond.Burning));
    }

    [Fact]
    public void A_weapon_without_an_initial_afterburn_loses_the_debuff_on_the_next_think()
    {
        // The flare gun overrides no GetInitialAfterburnDuration
        // (tf_weaponbase.h:557), so stock's AddCond carries 0 and the
        // condition think takes it off at once (:2369-2374) — the fire's 7.5 s
        // rate-on-hit is not the debuff's time.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flaregun" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        var set = rig.Conditions.Of(CombatRig.Victim.EntryIndex)!;

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        Assert.True(set.Has(TFCond.HealingDebuff));
        set.Tick(1f / 66f, healers: 0, default);

        Assert.False(set.Has(TFCond.HealingDebuff));
        Assert.True(set.Has(TFCond.Burning));
    }

    [Fact]
    public void A_hit_without_ignite_does_not_set_alight()
    {
        // The pistol's own set (g_aWeaponDamageTypes) carries no DMG_IGNITE,
        // so the hit does not light the victim even with the weapon resolved.
        var rig = new CombatRig();
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        rig.Hit(13f, DamageType.Burn);
        Assert.False(rig.VictimState.Conditions().Has(TFCond.Burning));
    }

    [Fact]
    public void An_extinguished_burn_tells_the_healing_once()
    {
        // The burning think's HEALTH_BUFF arm (tf_player_shared.cpp:2793): the
        // tick that takes the fire out hands the patient to the healing lane,
        // whose healer loop fires the extinguish event — once, not per tick.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        for (float t = 100f; t <= 104f; t += 1f / 66f)
            slice.Tick(t);

        Assert.Equal([CombatRig.Victim], rig.Healing.Patients);
    }

    [Fact]
    public void An_ignition_starts_the_burn_ramps_clock()
    {
        // OnAddBurning's clock (tf_player_shared.cpp:7306): the skin ramp
        // starts when TF_COND_BURNING arrives, and a re-ignite while already
        // burning keeps the earlier start (OnRemoveBurning never zeroes it).
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);

        Assert.Equal(0f, rig.VictimState.BurnEffectStartTime, 4);
        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        Assert.Equal(100f, rig.VictimState.BurnEffectStartTime, 4);

        rig.Clock.Now = 101f;
        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        Assert.Equal(100f, rig.VictimState.BurnEffectStartTime, 4);
    }

    // ---- set_dmgtype_ignite (tf_gamerules.cpp:7117-7133) --------------------

    private static void AttachBurn(CombatRig rig)
    {
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
    }

    [Fact]
    public void The_set_dmgtype_ignite_lights_a_plain_hit_that_carries_no_ignite_bit()
    {
        // :7122-7130: the held item's set_dmgtype_ignite adds DMG_IGNITE to a
        // non-DOT hit, so the Sharpened Volcano Fragment lights its airblast.
        var rig = new CombatRig();
        AttachBurn(rig);
        rig.AttackerItem("set_dmgtype_ignite", AttributeDescriptionFormat.Additive, 1f);

        rig.Hit(13f, DamageType.Bullet);

        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
    }

    [Fact]
    public void A_plain_weapon_without_the_attribute_leaves_the_victim_dry()
    {
        var rig = new CombatRig();
        AttachBurn(rig);

        rig.Hit(13f, DamageType.Bullet);

        Assert.False(rig.VictimState.Conditions().Has(TFCond.Burning)); // :7117 — plain Bullet bits carry no DMG_IGNITE
    }

    [Fact]
    public void The_ignite_attribute_does_not_re_light_a_burn_tick()
    {
        // The DOT guard (:7122, IsDOTDmg tf_shareddefs.h:1295-1301): the
        // burn's own ticks never ask the attribute, so they never re-light.
        // The held-item fallback keeps the item reachable through the scope,
        // as the burn tick's asks the holder's held weapon (:277-278).
        var rig = new CombatRig();
        AttachBurn(rig);
        rig.AttackerItem("set_dmgtype_ignite", AttributeDescriptionFormat.Additive, 1f);
        rig.Owners.Held[CombatRig.Attacker.EntryIndex] = "tf_weapon_pistol";

        using (rig.Scopes.Push(TFDamageCustom.Burning, TFWeaponId.Pistol, "tf_weapon_pistol"))
            rig.Hit(13f, DamageType.Bullet);

        Assert.False(rig.VictimState.Conditions().Has(TFCond.Burning));
    }

    // ---- the bleed ----------------------------------------------------------

    private static TFBleed BleedSlice(CombatRig rig)
    {
        var slice = (TFBleed)Assert.Single(rig.Bleeds.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        return slice;
    }

    [Fact]
    public void A_hit_from_a_bleeding_weapon_sets_the_victim_bleeding()
    {
        // flBleedingTime (tf_player.cpp:10577-10585, :10665-10667): the
        // weapon's bleeding_duration arms a bleed on the victim.
        var rig = new CombatRig();
        var slice = BleedSlice(rig);
        rig.AttackerItem("bleeding_duration", AttributeDescriptionFormat.Additive, 10f);

        rig.Hit(13f, DamageType.Bullet);

        Assert.True(rig.VictimState.Conditions().Has(TFCond.Bleeding));
        Assert.Single(slice.State.Bleeds);
        Assert.Equal(10f, slice.State.Bleeds[0].RemoveTime - rig.Clock.Now, 4);
    }

    [Fact]
    public void A_bleed_weapon_without_the_attribute_bleeds_nothing()
    {
        var rig = new CombatRig();
        var slice = BleedSlice(rig);

        rig.Hit(13f, DamageType.Bullet);

        Assert.False(rig.VictimState.Conditions().Has(TFCond.Bleeding));
        Assert.Empty(slice.State.Bleeds);
    }

    [Fact]
    public void A_bleed_tick_and_the_attackers_own_blast_arm_no_bleed()
    {
        // tf_player.cpp:10582: the gate is the hit's own custom kind and
        // bSelfBlastDmg (the attacker's own blast).
        var rig = new CombatRig();
        var slice = BleedSlice(rig);
        rig.AttackerItem("bleeding_duration", AttributeDescriptionFormat.Additive, 10f);

        rig.Hit(13f, DamageType.Blast, critical: false, attacker: CombatRig.Victim);
        Assert.Empty(slice.State.Bleeds);

        using (rig.Scopes.Push(TFDamageCustom.Bleeding, TFWeaponId.Knife, "tf_weapon_knife"))
            rig.Hit(13f, DamageType.Bullet);
        Assert.Empty(slice.State.Bleeds);
    }

    [Fact]
    public void A_bleed_ticks_slash_damage_from_its_attacker_as_the_bleeds_kind()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_bat" };
        var slice = BleedSlice(rig);
        rig.AttackerItem("bleeding_duration", AttributeDescriptionFormat.Additive, 10f);

        rig.Hit(13f, DamageType.Bullet);

        for (float t = rig.Clock.Now; t <= rig.Clock.Now + 0.3f; t += 1f / 66f)
            slice.Tick(t);

        var tick = Assert.Single(rig.Damage.Hits);
        Assert.Equal((CombatRig.Victim, CombatRig.Attacker, (float)TFCombatDefs.BleedingDamage,
                     TFBleed.TickDamageType, TFDamageCustom.Bleeding, TFWeaponId.Bat, false), tick);
    }

    // ---- the on-hit enemy slow (mult_onhit_enemyspeed, tf_weaponbase.cpp:5443-5467) ----

    [Fact]
    public void The_onhit_slow_stuns_the_victim_and_a_plain_weapon_does_not()
    {
        // A roll of 0.5 under the attribute's 0.6 lands the stun (:5450): 0.2 s,
        // its strength at the clamp's ceiling — the stock rig sits exactly 512 units
        // apart (:5454-5457) — the stack holding it remapped onto its 0..255 scale,
        // movement only, dealt by the attacker.
        var rig = new CombatRig();
        TFStunStack stuns = rig.AttachStun(CombatRig.Victim);
        rig.AttackerItem("mult_onhit_enemyspeed", AttributeDescriptionFormat.Additive, 0.6f);

        rig.Hit(10f, Ranged);

        Assert.NotNull(stuns.ActiveStun);
        Assert.Equal(0.2f, stuns.ActiveStun!.Duration, 4);
        Assert.Equal(255f * Math.Clamp(0.6f, 0f, 1f), stuns.ActiveStun.Amount, 3);
        Assert.Equal(TFStunFlags.Movement, stuns.ActiveStun.Flags);
        Assert.Equal(CombatRig.Attacker, stuns.ActiveStun.Attacker);

        var plain = new CombatRig();
        TFStunStack plainStuns = plain.AttachStun(CombatRig.Victim);
        plain.Hit(10f, Ranged);
        Assert.Null(plainStuns.ActiveStun);
    }

    [Fact]
    public void The_onhit_slow_falls_off_from_half_a_thousand_units_out()
    {
        // The remap runs on the squared distance: full at 512, spent by 1536 (:5455).
        var near = new CombatRig();
        TFStunStack nearStuns = near.AttachStun(CombatRig.Victim);
        near.AttackerItem("mult_onhit_enemyspeed", AttributeDescriptionFormat.Additive, 0.6f);
        near.Hit(10f, Ranged);
        Assert.NotNull(nearStuns.ActiveStun);
        Assert.Equal(255f * Math.Clamp(0.6f, 0f, 1f), nearStuns.ActiveStun!.Amount, 3);

        var far = new CombatRig { AttackerAt = new Vector(1600f, 0f, 0f) };
        TFStunStack farStuns = far.AttachStun(CombatRig.Victim);
        far.AttackerItem("mult_onhit_enemyspeed", AttributeDescriptionFormat.Additive, 0.6f);
        far.Hit(10f, Ranged);
        Assert.NotNull(farStuns.ActiveStun); // the stun lands; only its strength is gone
        Assert.Equal(0f, farStuns.ActiveStun!.Amount, 3);
    }

    [Fact]
    public void The_onhit_slow_lands_only_as_often_as_the_attribute_says()
    {
        // The plain uniform roll against the attribute is the whole gate (:5450).
        var lucky = new CombatRig(roll: 0.5f);
        TFStunStack luckyStuns = lucky.AttachStun(CombatRig.Victim);
        lucky.AttackerItem("mult_onhit_enemyspeed", AttributeDescriptionFormat.Additive, 0.6f);
        lucky.Hit(10f, Ranged);
        Assert.NotNull(luckyStuns.ActiveStun); // 0.5 < 0.6: the roll lands

        var unlucky = new CombatRig(roll: 0.7f);
        TFStunStack unluckyStuns = unlucky.AttachStun(CombatRig.Victim);
        unlucky.AttackerItem("mult_onhit_enemyspeed", AttributeDescriptionFormat.Additive, 0.6f);
        unlucky.Hit(10f, Ranged);
        Assert.Null(unluckyStuns.ActiveStun); // 0.7 fails the same ask
    }

    [Fact]
    public void A_medic_healed_victim_keeps_their_feet_under_the_slow()
    {
        // Both slow arms live inside the health-buff gate (:5444): TF_COND_HEALTH_BUFF
        // on the victim exempts them; a plain victim still falls to the stun.
        var healed = new CombatRig();
        TFStunStack healedStuns = healed.AttachStun(CombatRig.Victim);
        healed.VictimState.SetConditions(TFCondMask.Of(TFCond.HealthBuff));
        healed.AttackerItem("mult_onhit_enemyspeed", AttributeDescriptionFormat.Additive, 0.6f);
        healed.Hit(10f, Ranged);
        Assert.Null(healedStuns.ActiveStun);

        var plain = new CombatRig();
        TFStunStack plainStuns = plain.AttachStun(CombatRig.Victim);
        plain.AttackerItem("mult_onhit_enemyspeed", AttributeDescriptionFormat.Additive, 0.6f);
        plain.Hit(10f, Ranged);
        Assert.NotNull(plainStuns.ActiveStun);
    }

    [Fact]
    public void The_major_slow_stuns_for_the_set_seconds_at_a_fixed_strength()
    {
        // No roll gates the major arm (:5461-5466): the attribute is the stun's seconds
        // and the strength the fixed 0.4 — even under a roll that would fail the minor arm.
        var rig = new CombatRig(roll: 0.9f);
        TFStunStack stuns = rig.AttachStun(CombatRig.Victim);
        rig.AttackerItem("mult_onhit_enemyspeed_major", AttributeDescriptionFormat.Additive, 3f);

        rig.Hit(10f, Ranged);

        Assert.NotNull(stuns.ActiveStun);
        Assert.Equal(3f, stuns.ActiveStun!.Duration, 4);
        Assert.Equal(255f * Math.Clamp(0.4f, 0f, 1f), stuns.ActiveStun.Amount, 3);
        Assert.Equal(TFStunFlags.Movement, stuns.ActiveStun.Flags);
    }

    // ---- invulnerability ----------------------------------------------------

    [Fact]
    public void An_ubered_player_takes_nothing_but_a_trigger_hurt()
    {
        var rig = new CombatRig();
        var hit = new DamageContext(CombatRig.Victim, CombatRig.Attacker, 50f, (int)Ranged);
        rig.Engine.Returns("Entities.GetClassName", "player");
        Assert.Equal(Decision.UseStockRule, rig.Rules.Decide(in hit));

        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Invulnerable));
        Assert.Equal(Decision.No, rig.Rules.Decide(in hit));

        rig.Engine.Returns("Entities.GetClassName", TFDamageRules.TriggerHurt);
        Assert.Equal(Decision.UseStockRule, rig.Rules.Decide(in hit));
    }

    [Fact]
    public void An_ubered_player_takes_a_point_hurt_only_with_the_uber_flag()
    {
        // CPointHurt with SF_PHURT_HURT_UBER (pointhurt.h:5) is the second
        // inflictor stock lets through the über (tf_player.cpp:9204-9213);
        // without the flag it is refused like anything else.
        var rig = new CombatRig();
        var hit = new DamageContext(CombatRig.Victim, CombatRig.Attacker, 50f, (int)Ranged);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Invulnerable));
        rig.Engine.Returns("Entities.GetClassName", TFDamageRules.PointHurt)
                  .Returns("Entities.GetSpawnFlags", 0);

        Assert.Equal(Decision.No, rig.Rules.Decide(in hit));

        rig.Engine.Returns("Entities.GetSpawnFlags", TFDamageRules.HurtUberSpawnflag);
        Assert.Equal(Decision.UseStockRule, rig.Rules.Decide(in hit));
    }

    [Fact]
    public void A_telefrag_kills_an_ubered_player()
    {
        // tf_player.cpp:9233-9236: a hit whose custom is TF_DMG_CUSTOM_TELEFRAG
        // — which a scope carries here, as our port carries info's custom —
        // is allowed.
        var rig = new CombatRig();
        var hit = new DamageContext(CombatRig.Victim, CombatRig.Attacker, 50f, (int)Ranged);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Invulnerable));
        rig.Engine.Returns("Entities.GetClassName", "player");

        Assert.Equal(Decision.No, rig.Rules.Decide(in hit));

        using (rig.Scopes.Push(TFDamageCustom.Telefrag, TFWeaponId.None, ""))
            Assert.Equal(Decision.UseStockRule, rig.Rules.Decide(in hit));
    }

    [Fact]
    public void A_flagless_point_hurt_is_refused_even_carrying_the_telefrag_custom()
    {
        // Stock's arms are an else-if chain (tf_player.cpp:9204-9236): a
        // point_hurt answers only for its own spawnflag, so a flagless one
        // never reaches the telefrag arm.
        var rig = new CombatRig();
        var hit = new DamageContext(CombatRig.Victim, CombatRig.Attacker, 50f, (int)Ranged);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Invulnerable));
        rig.Engine.Returns("Entities.GetClassName", TFDamageRules.PointHurt)
                  .Returns("Entities.GetSpawnFlags", 0);

        using (rig.Scopes.Push(TFDamageCustom.Telefrag, TFWeaponId.None, ""))
            Assert.Equal(Decision.No, rig.Rules.Decide(in hit));
    }

    [Fact]
    public void A_trigger_hit_rides_as_the_trigger_hurt_custom()
    {
        // Stock's OnTakeDamage sets the custom before the modify rules
        // (tf_player.cpp:9208-9211, 9218-9221), so the kill record names the
        // trigger. Both arms of SetDamageCustom( TF_DMG_CUSTOM_TRIGGER_HURT ).
        var rig = new CombatRig();

        rig.Engine.Returns("Entities.GetClassName", TFDamageRules.TriggerHurt)
                  .Returns("Entities.GetSpawnFlags", 0);
        rig.Hit(13f, Ranged);
        var trigger = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;

        rig.Engine.Returns("Entities.GetClassName", TFDamageRules.PointHurt)
                  .Returns("Entities.GetSpawnFlags", TFDamageRules.HurtUberSpawnflag);
        rig.Hit(13f, Ranged);
        var pointHurt = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;

        rig.Engine.Returns("Entities.GetClassName", TFDamageRules.PointHurt)
                  .Returns("Entities.GetSpawnFlags", 0);
        rig.Hit(13f, Ranged);
        var flagless = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;

        Assert.Equal(TFDamageCustom.TriggerHurt, trigger.Custom);
        Assert.Equal(TFDamageCustom.TriggerHurt, pointHurt.Custom);
        Assert.NotEqual(TFDamageCustom.TriggerHurt, flagless.Custom);
    }

    //-------------------------------------------------------------------------
    // The game stats: the two numbers the random crit's observed rate reads
    // (CTFGameStats::Event_PlayerDamage, tf_gamestats.cpp:1033-1089, at the
    // tail of CTFPlayer::OnTakeDamage, tf_player.cpp:9659-9661). The rig hits
    // at 512 with the roll mid-band, where a plain hit lands its number
    // exactly and a crit lands 3× it, so the booked amount is the taken one.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_ranged_hit_books_its_taken_damage_to_the_attacker()
    {
        // TFSTAT_DAMAGE_RANGED (tf_gamestats.cpp:1075): the hit's damage, to
        // the attacker — the victim's index holds nothing of it.
        var rig = new CombatRig();

        Assert.Equal(10f, rig.Hit(10f, Ranged), 4f);

        Assert.Equal(10, rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged));
        Assert.Equal(10, rig.Stats.Accumulated(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged));
        Assert.Equal(0, rig.Stats.Round(CombatRig.Victim.EntryIndex, TFStat.DamageRanged));
    }

    [Fact]
    public void A_random_crit_books_its_taken_damage_to_both_the_ranged_and_the_random_stat()
    {
        // A crit the attacker rolled themselves rides both numbers
        // (:1075 and :1086): the ranged total keeps everything ranged, the
        // random-crit one keeps the crit's share of it.
        var rig = new CombatRig();

        rig.Hit(10f, Ranged, critical: true);

        Assert.Equal(30, rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged));
        Assert.Equal(30, rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRangedCritRandom));
    }

    [Fact]
    public void A_crit_boosts_damage_but_books_no_random_crit_damage()
    {
        // The DMG_CRITICAL hit stock does NOT book to the random number is
        // the boosted one (tf_gamestats.cpp:1079-1082; the boosted stat is
        // the scoring lanes'). Were it booked, an observer riding a boost
        // would raise their own observed rate and refuse their next roll —
        // the bug stock's split exists to avoid.
        var rig = new CombatRig();
        rig.AttackerState.SetConditions(TFCondMask.Of(TFCond.CritboostedUserBuff));

        rig.Hit(10f, Ranged, critical: true);

        Assert.Equal(30, rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged));
        Assert.Equal(0, rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRangedCritRandom));
    }

    [Fact]
    public void A_swing_books_no_ranged_damage()
    {
        // The DMG_MELEE guard (tf_gamestats.cpp:1073) reads the CALLER's
        // bits: stock's swing carries DMG_MELEE into OnTakeDamage
        // (tf_weaponbase_melee.cpp:817), where our layer's weapon-table read
        // would have replaced it with the wrench row's Club.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_wrench" };

        rig.Hit(10f, TFDamageBits.Melee | DamageType.NeverGib | DamageType.Club);

        Assert.Equal(0, rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged));
    }

    [Fact]
    public void Damage_to_ones_self_books_nothing()
    {
        // pTarget == pAttacker returns before any stat (tf_gamestats.cpp:1057):
        // your own rockets never raise your observed rate.
        var rig = new CombatRig();

        rig.Hit(10f, Ranged, attacker: CombatRig.Victim);

        Assert.Equal(0, rig.Stats.Round(CombatRig.Victim.EntryIndex, TFStat.DamageRanged));
    }

    [Fact]
    public void A_hit_with_no_player_attacker_books_nothing()
    {
        // The attacker must resolve to a TF player (:1045-1058); a world
        // hazard's damage books no one. (A sentry's shots already fire with
        // its builder as the attacker — TFSentryGun.cs:618 — so stock's
        // builder credit needs no arm of its own here.)
        var rig = new CombatRig();

        rig.Hit(10f, Ranged, attacker: new EntityHandle(0x0001_0009u));

        Assert.Equal(0, rig.Stats.Round(9, TFStat.DamageRanged));
    }

    [Fact]
    public void Damage_outside_the_sane_range_books_nothing()
    {
        // The guard against insanely huge damage (tf_gamestats.cpp:1042):
        // a hit past INSANE_PLAYER_DAMAGE (1500, :1036) is a cheat or a bug,
        // and books nothing.
        var rig = new CombatRig();

        rig.Hit(2000f, Ranged);

        Assert.Equal(0, rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged));
    }

    [Fact]
    public void The_two_stats_reset_when_the_next_round_begins()
    {
        // Stock's round stats — the pair the observed rate measures — clear
        // at the next round's preround (CCTFGameStats::RoundRespawn through
        // the ResetStats arms), the accumulated half carrying across.
        var rig = new CombatRig();
        var at = CombatRig.Attacker.EntryIndex;
        rig.Hit(10f, Ranged);
        rig.Hit(10f, Ranged, critical: true);
        Assert.Equal(40, rig.Stats.Round(at, TFStat.DamageRanged));
        Assert.Equal(30, rig.Stats.Round(at, TFStat.DamageRangedCritRandom));

        ((ITeamRoundListener)rig.Stats).OnStateChanged(RoundState.TeamWin, RoundState.Preround);

        Assert.Equal(0, rig.Stats.Round(at, TFStat.DamageRanged));
        Assert.Equal(0, rig.Stats.Round(at, TFStat.DamageRangedCritRandom));
        Assert.Equal(40, rig.Stats.Accumulated(at, TFStat.DamageRanged));
        Assert.Equal(30, rig.Stats.Accumulated(at, TFStat.DamageRangedCritRandom));
    }

    // ---- the behind tests' geometry -----------------------------------------

    [Fact]
    public void The_axtinguisher_behind_test_reads_the_victims_eye_angles_and_the_origins()
    {
        // tf_gamerules.cpp:6050-6053: the way from the attacker to the victim
        // against the victim's eye forward. The victim stands at the origin
        // facing +X; behind is -X, before +X.
        var behind = new CombatRig { AttackerAt = new Vector(-100f, 0f, 0f), AttackerWeapon = "tf_weapon_fireaxe" };
        behind.Conditions.Add(CombatRig.Victim, TFCond.Burning);
        behind.AttackerItem("axtinguisher_properties", AttributeDescriptionFormat.Additive, 1f);
        Assert.Equal(30f, behind.Hit(10f, TFDamageBits.Melee), 4);

        var before = new CombatRig { AttackerAt = new Vector(100f, 0f, 0f), AttackerWeapon = "tf_weapon_fireaxe" };
        before.Conditions.Add(CombatRig.Victim, TFCond.Burning);
        before.AttackerItem("axtinguisher_properties", AttributeDescriptionFormat.Additive, 1f);
        Assert.Equal(13.5f, before.Hit(10f, TFDamageBits.Melee), 4);
    }

    // ---- the consume-flames arm (tf_gamerules.cpp:6098-6117) ------------------

    /// <summary>
    /// <c>attack_minicrits_and_consumes_burning</c> (:6098-6117): the held weapon's
    /// melee hit on a burning victim consumes their flames — the hit gains the
    /// bonus the RemapValClamped over the burn's seconds left answers (:6105), the
    /// flames come off with the condition (:6107) and the flame-out sound sounds
    /// (:6108); a plain hit becomes a mini-crit (:6110-6114) and the kill is stamped
    /// AxtinguisherBoosted (:6116). Never on oneself (:6096).
    /// </summary>
    [Fact]
    public void TheAxtinguisherMiniCritsAndPutsOutABurningVictim()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
        rig.AttackerWeapon = "tf_weapon_fireaxe";
        rig.AttackerItem("attack_minicrits_and_consumes_burning", AttributeDescriptionFormat.Additive, 1f);

        // The burn's seconds left (tf_player_shared.cpp's GetAfterburnDuration,
        // read at :6105) and the stock formula the bonus takes from it.
        float left = slice.Burn.AfterburnDuration;
        float consumeBonus = TFDamageModifyRules.RemapValClamped(
            left, TFCombatDefs.BurningFrequency, TFCombatDefs.BurningFlameLife, 20f, TFCombatDefs.BurningDamage * 20);
        float afterConsume = 10f + consumeBonus;

        float damage = rig.Hit(10f, TFDamageBits.Melee);

        Assert.Equal(afterConsume + (TFDamage.MinicritMultiplier - 1f) * afterConsume, damage, 4);
        var record = rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value;
        Assert.Equal(TFCritType.Mini, record.Crit);
        Assert.Equal(TFDamageCustom.AxtinguisherBoosted, record.Custom);
        Assert.False(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.Equal("TFPlayer.FlameOut", Assert.Single(rig.Engine.AllOf("Sound.EntityEmitSound")).ArgAs<string>(1));
    }

    /// <summary>
    /// The arm's first gate (:6099-6103): a held weapon whose item asks no
    /// consume — the stock axe — leaves the flames, the number and the kind of
    /// the plain hit alike (:6110's NONE keeps its answer).
    /// </summary>
    [Fact]
    public void AStockAxeLeavesTheFlames()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        rig.AttackerWeapon = "tf_weapon_fireaxe";

        float damage = rig.Hit(10f, TFDamageBits.Melee);

        Assert.Equal(10f, damage, 4);
        Assert.Equal(TFDamageCustom.None, rig.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Custom);
        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.Empty(rig.Engine.AllOf("Sound.EntityEmitSound"));
        Assert.NotNull(slice);
    }

    /// <summary>
    /// The gate's burning half (:6099): the same item's melee hit on a victim
    /// not on fire is plain — the bonus needs the flames to consume.
    /// </summary>
    [Fact]
    public void AnAxtinguisherHitOnANonBurningVictimIsPlain()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_fireaxe" };
        rig.AttackerItem("attack_minicrits_and_consumes_burning", AttributeDescriptionFormat.Additive, 1f);

        float damage = rig.Hit(10f, TFDamageBits.Melee);

        Assert.Equal(10f, damage, 4);
        Assert.False(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.Empty(rig.Engine.AllOf("Sound.EntityEmitSound"));
    }

    // ---- the mad milk's payback (tf_weaponbase.cpp:5172-5177) --------------

    [Fact]
    public void A_hit_on_a_milky_victim_heals_the_attacker_six_parts_in_ten()
    {
        var rig = new CombatRig();
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.MadMilk));

        rig.Hit(10f, Ranged);

        var heal = Assert.Single(rig.Resupply.HealthTaken);
        Assert.Equal(CombatRig.Attacker, heal.Who);
        Assert.Equal(6f, heal.Amount, 4);
    }

    [Fact]
    public void A_hit_on_a_dry_victim_heals_the_attacker_nothing()
    {
        var rig = new CombatRig();

        rig.Hit(10f, Ranged);

        Assert.Empty(rig.Resupply.HealthTaken);
    }

    // ---- the on-hit arms (tf_weaponbase.cpp:5142-5281) ----------------------

    /// <summary>
    /// The medic's melee arm (:5259-5281): the medic's hit fills their own ÜberCharge
    /// meter from the held item's <c>add_onhit_ubercharge</c> — a half-second attribute
    /// onto an empty meter gives the level a half, asked of the medigun service's setter
    /// with its cap (<c>AddCharge</c> caps at one, tf_weapon_medigun.cpp:1061). The hit
    /// itself is untouched: twenty stays twenty. Stock's powerup-mode scaling
    /// (:5269-5277) waits on the mode's carrier.
    /// </summary>
    [Fact]
    public void AUbersawHitAddsToTheMedicChargeMeter()
    {
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Medic;
        rig.AttackerItem("add_onhit_ubercharge", AttributeDescriptionFormat.Additive, 0.5f);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.Equal(0.5f, rig.Mediguns.ChargeLevel(CombatRig.Attacker.EntryIndex), 4);
        Assert.Equal("charge 4 0.5", Assert.Single(rig.Mediguns.Sets));
    }

    /// <summary>
    /// The arm's nonzero gate (:5264): the medic's hit with the stock item — no attribute
    /// on it — leaves the meter where it stands.
    /// </summary>
    [Fact]
    public void AMedicHitWithAPlainItemLeavesTheMeterAlone()
    {
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Medic;

        rig.Hit(20f, Ranged);

        Assert.Equal(0f, rig.Mediguns.ChargeLevel(CombatRig.Attacker.EntryIndex), 4);
        Assert.Empty(rig.Mediguns.Sets);
    }

    /// <summary>
    /// The class gate (:5260): the same attributed item on a non-medic attacker's hit
    /// adds nothing — the arm belongs to the medic alone.
    /// </summary>
    [Fact]
    public void ANonMedicHitWithTheChargeAttributeLeavesTheMeterAlone()
    {
        var rig = new CombatRig();   // the rig's attacker is a sniper
        rig.AttackerItem("add_onhit_ubercharge", AttributeDescriptionFormat.Additive, 0.5f);

        rig.Hit(20f, Ranged);

        Assert.Equal(0f, rig.Mediguns.ChargeLevel(CombatRig.Attacker.EntryIndex), 4);
        Assert.Empty(rig.Mediguns.Sets);
    }

    /// <summary>
    /// The Blutsauger arm (:5144-5149, :5227-5231): a hit whose held item carries
    /// <c>add_onhit_addhealth</c> heals the attacker themselves by that amount — five heals
    /// five through the resupply's <c>TakeHealth</c>, and the hit itself is untouched: twenty
    /// stays twenty at full reach, where the distance scale leaves the number as it stands.
    /// </summary>
    [Fact]
    public void ABlutsaugerHitHealsTheAttackerForTheAttribute()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.AttackerItem("add_onhit_addhealth", AttributeDescriptionFormat.Additive, 5f);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.Equal(5, rig.Resupply.HealthAnswer);
        Assert.Equal("take 4 5 False", Assert.Single(rig.Resupply.Takes));
        // the player_healonhit event (:5244-5255): the signed amount, the healed
        // edict and the held item's defindex — the rig's item has none, stock's -1.
        Assert.Equal(5, rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt"
                                                  && c.ArgAs<string>(1) == "amount").ArgAs<int>(2));
        Assert.Equal(CombatRig.Attacker.EntryIndex,
                     rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt"
                                                && c.ArgAs<string>(1) == "entindex").ArgAs<int>(2));
        Assert.Equal(-1, rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt"
                                                     && c.ArgAs<string>(1) == "weapon_def_index").ArgAs<int>(2));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    /// <summary>
    /// The floor (:5149): one point of the attribute still heals three — stock's minimum
    /// applies whenever the attribute is set at all.
    /// </summary>
    [Fact]
    public void ASmallOnHitHealStillHealsThree()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        rig.AttackerItem("add_onhit_addhealth", AttributeDescriptionFormat.Additive, 1f);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.Equal(3, rig.Resupply.HealthAnswer);
        Assert.Equal("take 4 3 False", Assert.Single(rig.Resupply.Takes));
    }

    /// <summary>
    /// Milk's payback (:5176): a hit on a milked victim pays the attacker sixty percent of
    /// the hit on top of the attribute — no attribute, twelve on twenty, same TakeHealth.
    /// </summary>
    [Fact]
    public void AHitOnAMilkedVictimPaysBackSixtyPercent()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        rig.Conditions.Add(CombatRig.Victim, TFCond.MadMilk);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.Equal(12, rig.Resupply.HealthAnswer);
        Assert.Equal("take 4 12 False", Assert.Single(rig.Resupply.Takes));
    }

    /// <summary>
    /// The heal-or-nothing gate (:5227): a hit with the stock item on a clean victim
    /// heals nobody — no attribute, no milk, no take.
    /// </summary>
    [Fact]
    public void AHitWithAPlainItemHealsNobody()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.Equal(0, rig.Resupply.HealthAnswer);
        Assert.Empty(rig.Resupply.Takes);
    }

    // ---- the kill heal (tf_player.cpp:11545-11564) ---------------------------

    /// <summary>
    /// The Powerjack arm (:11545-11556): a killing hit whose held item carries
    /// <c>heal_on_kill</c> heals the killer through <c>TakeHealth</c> — five heals five while the
    /// headroom under the buffed max allows it (thirty of forty) — and the <c>player_healonhit</c>
    /// event carries the healed amount, the killer's edict and the stock -1 defindex (:11552-11563).
    /// </summary>
    [Fact]
    public void AKillHealsTheKillerFromTheAttribute()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;
        rig.Healing.MaxBuffedAnswer = 40;
        rig.Engine.ReturnsEachCall("Entities.GetHealth", () =>
            (rig.Engine.Last("Entities.GetHealth") ?? default).ArgAs<EntityHandle>(0) == CombatRig.Attacker ? 30 : 15);
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.AttackerItem("heal_on_kill", AttributeDescriptionFormat.Additive, 5f);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);   // twenty over the victim's fifteen: the kill

        Assert.Equal(5, rig.Resupply.HealthAnswer);                                  // MIN(5, 40-30) — the whole attribute
        Assert.Equal("take 4 5 False", Assert.Single(rig.Resupply.Takes));           // :11549's TakeHealth(DMG_GENERIC)
        Assert.Equal(5, rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt"
                                                   && c.ArgAs<string>(1) == "amount").ArgAs<int>(2));
        Assert.Equal(CombatRig.Attacker.EntryIndex,
                     rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt"
                                                && c.ArgAs<string>(1) == "entindex").ArgAs<int>(2));
        Assert.Equal(-1, rig.Engine.Calls.Last(c => c.Member == "GameEvents.EventSetInt"
                                                     && c.ArgAs<string>(1) == "weapon_def_index").ArgAs<int>(2));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    /// <summary>
    /// The headroom cap (:11548): two hundred points of attribute on a killer at thirty of a
    /// buffed forty heals ten — the MIN of attribute and headroom, no more.
    /// </summary>
    [Fact]
    public void AKillHealCapsAtTheBuffedMaxHeadroom()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;
        rig.Healing.MaxBuffedAnswer = 40;
        rig.Engine.ReturnsEachCall("Entities.GetHealth", () =>
            (rig.Engine.Last("Entities.GetHealth") ?? default).ArgAs<EntityHandle>(0) == CombatRig.Attacker ? 30 : 15);
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        rig.AttackerItem("heal_on_kill", AttributeDescriptionFormat.Additive, 200f);

        rig.Hit(20f, Ranged);

        Assert.Equal(10, rig.Resupply.HealthAnswer);                                 // min(200, 40-30)
        Assert.Equal("take 4 10 False", Assert.Single(rig.Resupply.Takes));
    }

    /// <summary>
    /// The kill gate: a hit the victim survives (their fifteen against the survivor's hundred)
    /// heals nobody — the arm belongs to the kill. The killer's health is never even asked.
    /// </summary>
    [Fact]
    public void ASurvivingHitHealsNobodyFromTheKillArm()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;
        rig.Healing.MaxBuffedAnswer = 40;
        rig.Engine.ReturnsEachCall("Entities.GetHealth", () =>
            (rig.Engine.Last("Entities.GetHealth") ?? default).ArgAs<EntityHandle>(0) == CombatRig.Attacker ? 30 : 100);
        rig.AttackerItem("heal_on_kill", AttributeDescriptionFormat.Additive, 5f);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);                                  // twenty under a hundred: survives

        Assert.Equal(0, rig.Resupply.HealthAnswer);
        Assert.Empty(rig.Resupply.Takes);
    }

    /// <summary>
    /// The stock item (:11546): the same kill without the attribute heals nobody.
    /// </summary>
    [Fact]
    public void AKillWithAPlainItemHealsNobody()
    {
        var rig = new CombatRig();
        rig.Resupply.MaxHealthAnswer = 100;

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);   // the victim has no health asked: nothing to cap

        Assert.Equal(0, rig.Resupply.HealthAnswer);
        Assert.Empty(rig.Resupply.Takes);
    }

    // ---- the spy's cloak on hit (tf_gamerules.cpp:6802-6813) ---------------

    [Fact]
    public void A_spy_hit_with_add_cloak_on_hit_fills_the_spy_cloak_meter()
    {
        var rig = new CombatRig();
        rig.AttackerState.SetClass(TFClass.Spy);
        rig.AttackerState.CloakMeter = 30f;
        rig.AttackerItem("add_cloak_on_hit", AttributeDescriptionFormat.Additive, 25f);

        rig.Hit(10f, Ranged);

        Assert.Equal(55f, rig.AttackerState.CloakMeter, 4); // :6810 — the amount lands on the spy's own meter
    }

    [Fact]
    public void The_cloak_on_hit_fills_only_a_spy_cloak_meter()
    {
        var rig = new CombatRig(); // the rig's attacker is a Sniper, and the arm gates on the spy class (:6802)
        rig.AttackerState.CloakMeter = 30f;
        rig.AttackerItem("add_cloak_on_hit", AttributeDescriptionFormat.Additive, 25f);

        rig.Hit(10f, Ranged);

        Assert.Equal(30f, rig.AttackerState.CloakMeter, 4);
    }

    [Fact]
    public void The_cloak_on_hit_caps_the_meter_at_one_hundred()
    {
        var rig = new CombatRig();
        rig.AttackerState.SetClass(TFClass.Spy);
        rig.AttackerState.CloakMeter = 90f;
        rig.AttackerItem("add_cloak_on_hit", AttributeDescriptionFormat.Additive, 25f);

        rig.Hit(10f, Ranged);

        Assert.Equal(100f, rig.AttackerState.CloakMeter, 4); // :9755 — clamp to 0..100
    }
}
