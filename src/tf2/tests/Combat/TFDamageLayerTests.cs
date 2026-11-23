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
    public void The_donked_victims_blast_mini_crits_through_the_layer()
    {
        // IsDoubleDonk over the scope's cannon (:6202-6205): the blast on a
        // victim the ball shoved inside the window minicrits — 50 x 1.35 —
        // and past it the same blast is plain. The window is the convar's 0.5.
        var rig = new CombatRig();
        var donks = new TFDonkVictims(rig.Carriers, rig.Clock, rig.ConVars.Declared<TFDoubleDonkWindowConVar>());
        donks.Attach(CombatRig.Attacker);
        donks.Donk(CombatRig.Victim);

        using (rig.Scopes.Push(TFDamageCustom.None, TFWeaponId.Cannon, "tf_weapon_cannonball"))
            Assert.Equal(67.5f, rig.Hit(50f, TFWeaponDamageTypes.Of(TFWeaponId.Cannon)), 4);

        rig.Clock.Now += 0.5f;

        using (rig.Scopes.Push(TFDamageCustom.None, TFWeaponId.Cannon, "tf_weapon_cannonball"))
            Assert.Equal(50f, rig.Hit(50f, TFWeaponDamageTypes.Of(TFWeaponId.Cannon)), 4);
    }

    [Fact]
    public void A_flares_touch_never_takes_the_range()    {
        // g_aWeaponDamageTypes: the flare gun is DMG_BULLET | DMG_IGNITE, no
        // DMG_USEDISTANCEMOD (tf_shareddefs.cpp:775), so the band never leaves
        // the middle and the touch is the shot as dealt, the scope's weapon
        // overriding the attacker's held one as the touch's own scope does.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flaregun" };
        using (rig.Scopes.Push(TFDamageCustom.BurningFlare, TFWeaponId.Flaregun, "tf_weapon_flaregun"))
            Assert.Equal(30f, rig.Hit(30f, TFWeaponDamageTypes.Of(TFWeaponId.Flaregun)), 4);
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

    /// <summary>OnDealtDamage's meter walk (tf_player.cpp:10002-10037), riding
    /// the attacker's held weapon: a COMBO meter fills by the hit's share of
    /// the item's full charge — twenty of a fifty pool is a fifth of the
    /// carried bar.</summary>
    [Fact]
    public void AHeldDamageMeterFillsByTheHitsShareThroughTheLayer()
    {
        var rig = new CombatRig();
        var item = (AttributeContainer)rig.AttackerItem("item_meter_charge_type",
                                                        AttributeDescriptionFormat.Additive, 3f);
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9310, "attr 9310", "item_meter_damage_for_full_charge",
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("x")), 50f));
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();

        rig.Hit(20f, DamageType.Bullet);

        Assert.Equal(0.4f, rig.Entities.WeaponEntity.State.ItemMeter, 4);
    }

    /// <summary>The gate of :10012: a timed meter answers the hit with nothing.</summary>
    [Fact]
    public void AHeldTimedMeterStandsStillUnderTheHit()
    {
        var rig = new CombatRig();
        rig.AttackerItem("item_meter_charge_type", AttributeDescriptionFormat.Additive, 1f);
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();

        rig.Hit(20f, DamageType.Bullet);

        Assert.Equal(0f, rig.Entities.WeaponEntity.State.ItemMeter, 4);
    }

    // ---- the boots' stomp (boots_falling_stomp, tf_player_shared.cpp:7909-7918; tf_player.cpp:8955-8979)

    /// <summary>An item attribute the stock reads as a switch over the player,
    /// the way <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c> reads it.</summary>
    private static AttributeContainer BootsItem()
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9140, "attr 9140", "boots_falling_stomp", AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), 1f));
        return container;
    }

    /// <summary>
    /// <c>boots_falling_stomp</c> (<c>tf_player.cpp:8955-8979</c>): a fall that
    /// lands on another player under the boots' answer hands the blow over —
    /// ten plus thrice the computed fall to the one stood on, nothing left on
    /// the faller. The plain landing beside it keeps its own fall.
    /// </summary>
    [Fact]
    public void LandingOnAnEnemyUnderTheBootsPassesTheBlowOver()
    {
        // The plain landing first: the fall as the layer computes it for a
        // 900 u/s drop over a scout. The stomp's number hangs on it (:8968),
        // so it is the layer's own answer, not an invented constant.
        var plain = new CombatRig();
        var plainPlayer = RigPlayer(plain, CombatRig.Victim);
        plain.Entities.Placed[CombatRig.Victim] = plainPlayer;
        plainPlayer.State.GroundEntity = SsPlayer.GroundWireNone;    // standing on nothing: the gate stands out
        float fall = plain.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);
        Assert.Equal(18.75f, fall, 3);                               // the same answer the plain fall facts hold
        Assert.Empty(plain.Damage.Hits);                             // a plain landing applies through the answer alone

        var rig = new CombatRig();
        var victim = RigPlayer(rig, CombatRig.Victim);
        rig.Entities.Placed[CombatRig.Victim] = victim;
        victim.State.GroundEntity = CombatRig.Third.EntryIndex;     // standing on the third player
        rig.Engine.Returns("Players.GetHandle", CombatRig.Third);
        rig.Owners.Managers[CombatRig.Victim.EntryIndex] = BootsItem();

        Assert.Equal(0f, rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None), 4);

        Assert.Single(rig.Damage.Hits);
        var stomp = rig.Damage.Hits[^1];
        Assert.Equal(CombatRig.Third, stomp.Victim);
        Assert.Equal(10f + fall * 3f, stomp.Damage, 4);              // :8968 — ten plus thrice the fall
    }

    /// <summary>
    /// The same landing without the boots: the gate at :7915 asks its hook over
    /// zero and stands out, so the fall keeps its own answer and nobody else is
    /// struck.
    /// </summary>
    [Fact]
    public void WithoutTheBootsTheFallerKeepsTheirOwnFall()
    {
        var rig = new CombatRig();
        var victim = RigPlayer(rig, CombatRig.Victim);
        rig.Entities.Placed[CombatRig.Victim] = victim;
        victim.State.GroundEntity = CombatRig.Third.EntryIndex;
        rig.Engine.Returns("Players.GetHandle", CombatRig.Third);

        float fall = rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);
        Assert.Equal(18.75f, fall, 3);
        Assert.Empty(rig.Damage.Hits);                               // the plain answer, nobody else struck
    }

    /// <summary>
    /// The cond prong of <c>CanFallStomp</c> (<c>tf_player_shared.cpp:7911-7912</c>):
    /// the rocketpack answers alone, item or no item, and the blow crosses whole
    /// the same way the boots' answer takes it. The thruster's landing runs after
    /// and floors the faller's nothing at one (<c>tf_player.cpp:9037</c>).
    /// </summary>
    [Fact]
    public void TheRocketpackCondStompsWithoutAnyItem()
    {
        var rig = new CombatRig();
        var victim = RigPlayer(rig, CombatRig.Victim);
        rig.Entities.Placed[CombatRig.Victim] = victim;
        victim.State.GroundEntity = CombatRig.Third.EntryIndex;
        rig.Engine.Returns("Players.GetHandle", CombatRig.Third);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Rocketpack));

        Assert.Equal(1f, rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None), 4);

        Assert.Single(rig.Damage.Hits);
        Assert.Equal(CombatRig.Third, rig.Damage.Hits[^1].Victim);
        Assert.Equal(10f + 18.75f * 3f, rig.Damage.Hits[^1].Damage, 4);   // :8968 over the scout's own fall
    }

    /// <summary>
    /// The fall hit lands inside <c>CheckFalling</c>, before the command's end
    /// publishes the ground word: the word still reads nothing, and the move
    /// already stands the faller on the one below.
    /// </summary>
    [Fact]
    public void TheStompReadsTheGroundTheMoveJustFound()
    {
        var rig = new CombatRig();
        var moves = new SourceSharp.Server.Interop.ServerPlayerMoves();
        var victim = new SsPlayer(rig.Engine, rig.Engine, TestServices.Impacts(rig.Engine), TestServices.Fluid(), TestServices.InfiniteAux(), TestServices.PrintDamage(), moves: moves)
            { Handle = CombatRig.Victim };
        victim.State.BindLocal();
        victim.State.GroundEntity = SsPlayer.GroundWireNone;
        moves.For(CombatRig.Victim).Ground = CombatRig.Third;
        rig.Entities.Placed[CombatRig.Victim] = victim;
        rig.Engine.Returns("Players.GetHandle", CombatRig.Third);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Rocketpack));

        rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);

        Assert.Equal(CombatRig.Third, Assert.Single(rig.Damage.Hits).Victim);
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
    public void A_body_shot_takes_the_heldweapons_bodyshot_modifier()
    {
        // OnTakeDamage's tail (tf_player.cpp:8377-8384): a non-headshot scales
        // by the attacker's HELD weapon's bodyshot_damage_modify — x2 here.
        var rig = Sniper(zoomed: true);
        rig.AttackerItem("bodyshot_damage_modify", AttributeDescriptionFormat.Percentage, 2f);
        rig.Owners.Held[CombatRig.Attacker.EntryIndex] = "tf_weapon_sniperrifle";

        Assert.Equal(300f, rig.Hit(150f, DamageType.Bullet, HitGroup.Chest), 3);
    }

    [Fact]
    public void A_headshot_escapes_the_bodyshot_modifier()
    {
        // bIsHeadshot's own arm: the location head takes the headshot path's
        // scales, not the bodyshot's — the x3 alone on a plain 150.
        var rig = Sniper(zoomed: true);
        rig.AttackerItem("bodyshot_damage_modify", AttributeDescriptionFormat.Percentage, 2f);
        rig.Owners.Held[CombatRig.Attacker.EntryIndex] = "tf_weapon_sniperrifle";

        Assert.Equal(450f, rig.Hit(150f, DamageType.Bullet, HitGroup.Head), 3);
    }

    [Fact]
    public void A_body_shot_without_the_attribute_or_a_held_weapon_stands_()
    {
        var rig = Sniper(zoomed: true);
        rig.AttackerItem("bodyshot_damage_modify", AttributeDescriptionFormat.Percentage, 2f);

        Assert.Equal(150f, rig.Hit(150f, DamageType.Bullet, HitGroup.Stomach), 3);
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
        FiredRevolver(revolver);                       // the gate's clock: a shot ages ago
        revolver.Hit(20f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Revolver) | TFDamageBits.UseHitLocations,
                     revolver.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);
    }

    /// <summary>
    /// The attacker's held revolver, stamped long ago: the accuracy clock
    /// (<c>m_flLastAccuracyCheck</c>, the behaviour's kept time) the
    /// freshness gate reads, zeroed as a gun never fired and the clock at
    /// its default 100 — the stale side of :85's one second.
    /// </summary>
    private static void FiredRevolver(CombatRig rig)
    {
        var gun = new SsWeapon { Handle = RigEntities.Weapon };
        gun.State.BindLocal();
        rig.Entities.WeaponEntity = gun;
    }

    [Fact]
    public void The_revolvers_hit_location_bit_waits_for_the_second()
    {
        // CTFRevolver::GetDamageType's gate (tf_weapon_revolver.cpp:85): the
        // bit only once a second has passed since the shot stamped the
        // accuracy clock — a hit 0.4 s on carries the bare row.
        var fresh = new CombatRig { AttackerWeapon = "tf_weapon_revolver" };
        fresh.AttackerItem("set_weapon_mode", AttributeDescriptionFormat.Additive, 1f);
        FiredRevolver(fresh);
        fresh.Entities.WeaponEntity!.State.BehaviourKeptTime = 99.6f;  // 0.4 s ago
        fresh.Hit(20f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Revolver),
                     fresh.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);

        fresh.Entities.WeaponEntity!.State.BehaviourKeptTime = 98.9f;  // 1.1 s: the gate opens
        fresh.Hit(20f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Revolver) | TFDamageBits.UseHitLocations,
                     fresh.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);
    }

    [Fact]
    public void The_revolver_without_the_headshot_mode_never_carries_hit_locations()
    {
        // CanHeadshot's mode arm (tf_weapon_revolver.h:49): no set_weapon_mode
        // 1, no bit, however old the clock — the stock spy revolver.
        var plain = new CombatRig { AttackerWeapon = "tf_weapon_revolver" };
        FiredRevolver(plain);
        plain.Hit(20f, DamageType.Bullet, HitGroup.Chest);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Revolver),
                     plain.Records.LastHit(CombatRig.Victim.EntryIndex)!.Value.Bits & ~TFDamageBits.Critical);
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

    // ---- the jarate ---------------------------------------------------------

    /// <summary>The rig's zoomed sniper with a begun charge and the sleeper's seconds.</summary>
    private static CombatRig ChargedSniper(float chargeSeconds, float jarateSeconds)
    {
        var rig = Sniper(zoomed: true);
        rig.AttackerItem("jarate_duration", AttributeDescriptionFormat.Additive, jarateSeconds);
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();
        rig.Entities.WeaponEntity.State.BehaviourMeter = rig.Clock.Now - chargeSeconds;
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        return rig;
    }

    [Fact]
    public void A_charged_zoomed_rifles_hit_jars_the_victim_and_splashes_the_impact()
    {
        // GetJarateTime over OnTakeDamage's jarate arm (tf_player.cpp:10676-10690):
        // half a charge remapped onto 2..jarate_duration is the least, 2 s, and
        // the peejar impact lands where the victim stands.
        var rig = ChargedSniper(chargeSeconds: 1f, jarateSeconds: 8f);

        rig.Hit(75f, DamageType.Bullet, HitGroup.Chest);

        var conds = rig.Conditions.Of(CombatRig.Victim.EntryIndex)!;
        Assert.True(conds.Has(TFCond.Urine));
        Assert.Equal(TFSniperRifleBehaviour.JarateMinimum, conds.Duration(TFCond.Urine), 3);
        var splat = Assert.Single(rig.Temps.Effects);
        Assert.Equal("ParticleEffect", splat.Name);
        Assert.Equal(7, splat.Data.HitBox);
    }

    [Fact]
    public void The_jarate_last_to_the_items_seconds_at_the_full_charge()
    {
        // RemapValClamped's ceiling: the 150 cap lands on jarate_duration itself.
        var rig = ChargedSniper(chargeSeconds: 3.5f, jarateSeconds: 8f);

        rig.Hit(150f, DamageType.Bullet, HitGroup.Chest);

        Assert.Equal(8f, rig.Conditions.Of(CombatRig.Victim.EntryIndex)!.Duration(TFCond.Urine), 3);
    }

    [Fact]
    public void An_unzoomed_rifle_jars_nobody_until_it_is_the_classic()
    {
        // OnTakeDamage :10680: the Classic is jarating with no zoom up; the
        // plain rifle needs IsZoomed.
        var plain = ChargedSniper(chargeSeconds: 1f, jarateSeconds: 8f);
        plain.AttackerState.SetConditions(default);
        plain.Hit(75f, DamageType.Bullet, HitGroup.Chest);

        var classic = ChargedSniper(chargeSeconds: 1f, jarateSeconds: 8f);
        using (classic.Scopes.Push(TFDamageCustom.None, TFWeaponId.SniperrifleClassic,
                                   "tf_weapon_sniperrifle_classic"))
            classic.Hit(75f, DamageType.Bullet, HitGroup.Chest);

        Assert.False(plain.Conditions.Of(CombatRig.Victim.EntryIndex)!.Has(TFCond.Urine));
        Assert.Empty(plain.Temps.Effects);
        Assert.True(classic.Conditions.Of(CombatRig.Victim.EntryIndex)!.Has(TFCond.Urine));
        Assert.Single(classic.Temps.Effects);
    }

    [Fact]
    public void A_rifle_that_never_began_a_charge_jars_nobody()
    {
        // GetJarateTime :1217: no m_flChargedDamage, no urine.
        var rig = Sniper(zoomed: true);
        rig.AttackerItem("jarate_duration", AttributeDescriptionFormat.Additive, 8f);
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();

        rig.Hit(50f, DamageType.Bullet, HitGroup.Chest);

        Assert.False(rig.Conditions.Of(CombatRig.Victim.EntryIndex)!.Has(TFCond.Urine));
        Assert.Empty(rig.Temps.Effects);
    }

    [Fact]
    public void An_invulnerable_victim_is_never_jarated()
    {
        // OnTakeDamage :10685: the übered, phasing or intercepting victim stays dry.
        var rig = ChargedSniper(chargeSeconds: 1f, jarateSeconds: 8f);
        rig.Conditions.Add(CombatRig.Victim, TFCond.Invulnerable);

        rig.Hit(75f, DamageType.Bullet, HitGroup.Chest);

        Assert.False(rig.Conditions.Of(CombatRig.Victim.EntryIndex)!.Has(TFCond.Urine));
        Assert.Empty(rig.Temps.Effects);
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

    /// <summary>Stock's cancel arm (<c>tf_gamerules.cpp:13404-13407</c>):
    /// <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c>'s <c>cancel_falling_damage</c> walks
    /// the player's whole attribute manager; a yes zeroes the computed blow.
    /// Without the manager's answer the landing keeps its own 18.75, as the
    /// plain fact beside it holds.</summary>
    [Fact]
    public void AFallerWhoseManagerCancelsFallingDamageLandsUnhurt()
    {
        var rig = new CombatRig(roll: 0.5f);
        rig.Owners.Managers[CombatRig.Victim.EntryIndex] = ManagerCancelAnswer();

        Assert.Equal(0f, rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None));
    }

    /// <summary>The player manager's answer, the way <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c> reads it.</summary>
    private static AttributeContainer ManagerCancelAnswer()
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9141, "attr 9141", "cancel_falling_damage", AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), 1f));
        return container;
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
    public void The_burning_victims_carrier_carries_the_burns_bookkeeping()
    {
        // The burn's attacker, tick clock and fire life ride the victim's
        // networked state for the attacker's client mirror (the fields'
        // story): stamped at the ignition, kept current by every think.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        Assert.Equal(CombatRig.Attacker.EntryIndex, rig.VictimState.BurnAttackerIndex);
        Assert.Equal(100.5f, rig.VictimState.BurnNextTickTime, 4);   // the first tick, TF_BURNING_FREQUENCY on
        Assert.Equal(3.4f, rig.VictimState.BurnDuration, 4);

        slice.Tick(100.5f);
        Assert.Equal(101f, rig.VictimState.BurnNextTickTime, 4);     // the tick ran, the next half second on
        Assert.Equal(2.9f, rig.VictimState.BurnDuration, 4);
    }

    [Fact]
    public void The_burns_bookkeeping_leaves_with_the_fire()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);
        for (float t = 100f; t <= 104f; t += 1f / 66f)
            slice.Tick(t);                                           // the 3.4 s burn runs out

        Assert.Equal(-1, rig.VictimState.BurnAttackerIndex);
        Assert.Equal(0f, rig.VictimState.BurnNextTickTime, 4);
        Assert.Equal(0f, rig.VictimState.BurnDuration, 4);
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

    /// <summary>
    /// The Dragon's Fury's ball burns for its own seconds (Burn's flBurningTime,
    /// tf_projectile_dragons_fury.cpp:342-351). With no rate the fire went out on the next
    /// think, so a second ball found nobody burning and never earned its triple.
    /// </summary>
    [Fact]
    public void The_balls_fire_has_its_own_seconds()
    {
        var burn = new TFBurn(new AttributeHooks());

        burn.Ignite(1f, true, TFClass.HeavyWeapons, default, CombatRig.Attacker, TFWeaponId.FlameBall,
                    "tf_weapon_rocketlauncher_fireball", [], null);

        Assert.Equal(TFProjectileDefs.BallBurnDuration, burn.AfterburnDuration, 3);
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

    /// <summary>
    /// <c>CTFMinigun::GetInitialAfterburnDuration</c>'s ring arm
    /// (<c>tf_weapon_minigun.cpp:686-695</c>, held at
    /// <see cref="TFBurn.RingOfFireBurn"/>): the first burn of a hit from a
    /// minigun whose item wears <c>ring_of_fire_while_aiming</c> starts at
    /// the ring's eight seconds; the minigun's own rates add nothing to it.
    /// </summary>
    [Fact]
    public void The_huo_longs_ring_of_fire_starts_the_first_burn_at_eight_seconds()
    {
        // The hit lights through the item (the ignite-bit arm beside this
        // section's first fact); the ring's clock rides the same item — one
        // container wears both attributes.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_minigun" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);

        var item = (AttributeContainer)rig.AttackerItem("set_dmgtype_ignite", AttributeDescriptionFormat.Additive, 1f);
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9501, "ring of fire", "ring_of_fire_while_aiming",
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), 1f));

        rig.Hit(13f, DamageType.Bullet);

        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.Equal(TFBurn.RingOfFireBurn, slice.Burn.AfterburnDuration, 4);
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

    // ---- the scattergun's shove (set_scattergun_has_knockback, tf_weapon_shotgun.cpp:369-409) ----

    [Fact]
    public void The_force_a_natures_hit_hurls_the_victim_back_and_stuns_their_feet()
    {
        // 60 damage from 100 units out — inside the 400 (:41) and over the 30
        // (:40): the hull's AirBurstDamageForce at 3x reversed along the
        // centres (:399-400), a jump of lift (:401), the generic pushback's
        // ground floor and flag strip (:3388, :3396), the knocked-into-air
        // cond (:3397), the 0.3 s forward-only stun and the latch (:405-406).
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(100f, 0f, 0f) };
        TFStunStack stuns = rig.AttachStun(CombatRig.Victim);
        rig.AttackerItem(TFScattergunBehaviour.KnockbackAttribute, AttributeDescriptionFormat.Additive, 1f);
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        // A plain bullet: no distance mod, no spread — the damage the rules
        // deal is the one the force scales.
        float dealt = rig.Hit(60f, DamageType.Bullet);
        rig.ShoveFlush.Tick(1f);

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Assert.Equal(CombatRig.Victim, pushed!.ArgAs<EntityHandle>(0));
        Vector impulse = pushed.ArgAs<Vector>(1);
        Assert.Equal(-TFRadiusDamage.DamageForce(new Vector(32f, 32f, 72f), dealt,
                                                 TFDamageLayer.ScattergunKnockbackForceScale), impulse.X, 3f);
        Assert.Equal(TFAirBlastDefs.JumpMinSpeed, impulse.Z, 3f);
        Assert.True(pushed.ArgAs<bool>(2));
        Assert.True(rig.Conditions.InCond(CombatRig.Victim, TFCond.KnockedIntoAir));
        Assert.True(rig.VictimState.WeaponKnockback);
        Assert.NotNull(stuns.ActiveStun);
        Assert.Equal(TFScattergunBehaviour.StunTime, stuns.ActiveStun!.Duration, 4);
        Assert.Equal(TFStunFlags.Movement | TFStunFlags.MovementForwardOnly, stuns.ActiveStun.Flags);
        Assert.Equal(CombatRig.Attacker, stuns.ActiveStun.Attacker);
    }

    [Fact]
    public void A_plain_scatterguns_hit_or_one_past_the_range_shoves_nothing()
    {
        var plain = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(100f, 0f, 0f) };
        TFStunStack plainStuns = plain.AttachStun(CombatRig.Victim);
        plain.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        plain.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        plain.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        plain.Hit(60f, Ranged);
        plain.ShoveFlush.Tick(1f);

        Assert.DoesNotContain(plain.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
        Assert.False(plain.VictimState.WeaponKnockback);
        Assert.Null(plainStuns.ActiveStun);

        var far = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(500f, 0f, 0f) };
        TFStunStack farStuns = far.AttachStun(CombatRig.Victim);
        far.AttackerItem(TFScattergunBehaviour.KnockbackAttribute, AttributeDescriptionFormat.Additive, 1f);
        far.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        far.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        far.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        far.Hit(60f, Ranged);
        far.ShoveFlush.Tick(1f);

        Assert.DoesNotContain(far.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
        Assert.Null(farStuns.ActiveStun);
    }

    // ---- the aim's brace on the shove (tf_player.cpp:3339-3363) -------------

    /// <summary>One shove's whole impulse, as the arm scales it.</summary>
    private static Vector ShovedImpulse(CombatRig rig, float dealt, float brace)
        => new(-TFRadiusDamage.DamageForce(new Vector(32f, 32f, 72f), dealt,
                                           TFDamageLayer.ScattergunKnockbackForceScale) * brace,
               0f, TFAirBlastDefs.JumpMinSpeed * brace);

    private static CombatRig ShoveReady(CombatRig rig)
    {
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.AttackerItem(TFScattergunBehaviour.KnockbackAttribute, AttributeDescriptionFormat.Additive, 1f);
        rig.Owners.Held[CombatRig.Victim.EntryIndex] = rig.VictimWeapon;
        return rig;
    }

    [Fact]
    public void An_aiming_sniper_takes_the_shove_through_the_items_scale()
    {
        // The brace arm (tf_player.cpp:3339-3363): a sniper holding their aim
        // takes every impulse through the held item's
        // mult_aiming_knockback_resistance (:3344-3346), the whole shove landing
        // scaled (:3363) — here half its bite on both axes.
        var rig = ShoveReady(new CombatRig { AttackerWeapon = "tf_weapon_scattergun",
                                             AttackerAt = new Vector(100f, 0f, 0f) });
        rig.VictimState.PlayerClass = (int)TFClass.Sniper;
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Aiming));
        rig.Owners.Managers[CombatRig.Victim.EntryIndex] = Worn(9103, "mult_aiming_knockback_resistance", AttributeDescriptionFormat.Percentage, 0.5f);

        float dealt = rig.Hit(60f, DamageType.Bullet);
        rig.ShoveFlush.Tick(1f);

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Vector scaled = ShovedImpulse(rig, dealt, 0.5f);
        Assert.Equal(scaled.X, impulse.X, 3f);
        Assert.Equal(scaled.Z, impulse.Z, 3f);
    }

    [Fact]
    public void The_brace_needs_the_class_and_the_aim()
    {
        // Both gates, stock's pair at :3344: no class match leaves the shove
        // whole, and so does aiming without matching the class.
        var wrongClass = ShoveReady(new CombatRig { AttackerWeapon = "tf_weapon_scattergun",
                                                    AttackerAt = new Vector(100f, 0f, 0f) });
        wrongClass.VictimState.SetConditions(TFCondMask.Of(TFCond.Aiming));
        wrongClass.Owners.Managers[CombatRig.Victim.EntryIndex] = Worn(9103, "mult_aiming_knockback_resistance", AttributeDescriptionFormat.Percentage, 0.5f);
        float wrongDealt = wrongClass.Hit(60f, DamageType.Bullet);
        wrongClass.ShoveFlush.Tick(1f);
        Vector plain = ShovedImpulse(wrongClass, wrongDealt, 1f);
        Assert.Equal(plain.X, wrongClass.Engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1).X, 3f);

        var unaimed = ShoveReady(new CombatRig { AttackerWeapon = "tf_weapon_scattergun",
                                                 AttackerAt = new Vector(100f, 0f, 0f) });
        unaimed.VictimState.PlayerClass = (int)TFClass.Sniper;
        unaimed.Owners.Managers[CombatRig.Victim.EntryIndex] = Worn(9103, "mult_aiming_knockback_resistance", AttributeDescriptionFormat.Percentage, 0.5f);
        float unaimedDealt = unaimed.Hit(60f, DamageType.Bullet);
        unaimed.ShoveFlush.Tick(1f);
        Vector plainAgain = ShovedImpulse(unaimed, unaimedDealt, 1f);
        Assert.Equal(plainAgain.X, unaimed.Engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1).X, 3f);
    }

    [Fact]
    public void A_megahealed_victim_or_a_shoved_one_keeps_their_place()
    {
        // The generic pushback's immunity (:3372's IsImmuneToPushback, its
        // megaheal and immune arms) and the once-per-airtime latch (:383).
        var healed = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(100f, 0f, 0f) };
        TFStunStack healedStuns = healed.AttachStun(CombatRig.Victim);
        healed.VictimState.SetConditions(TFCondMask.Of(TFCond.Megaheal));
        healed.AttackerItem(TFScattergunBehaviour.KnockbackAttribute, AttributeDescriptionFormat.Additive, 1f);
        healed.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        healed.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        healed.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        healed.Hit(60f, Ranged);
        healed.ShoveFlush.Tick(1f);

        Assert.DoesNotContain(healed.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
        Assert.Null(healedStuns.ActiveStun);

        var latched = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(100f, 0f, 0f) };
        TFStunStack latchedStuns = latched.AttachStun(CombatRig.Victim);
        latched.VictimState.WeaponKnockback = true;
        latched.AttackerItem(TFScattergunBehaviour.KnockbackAttribute, AttributeDescriptionFormat.Additive, 1f);
        latched.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        latched.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        latched.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        latched.Hit(60f, Ranged);
        latched.ShoveFlush.Tick(1f);

        Assert.DoesNotContain(latched.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
        Assert.Null(latchedStuns.ActiveStun);
    }

    [Fact]
    public void The_shove_gate_reads_the_whole_shot_not_one_pellet()
    {
        // Stock's OnTakeDamage carries the shot's whole multi-damage
        // (tf_fx_shared.cpp:396's ApplyMultiDamage), so the :52 gate reads the
        // blast: the Force-a-Nature's close shot is ten pellets of 9.6 — no
        // pellet the 30 asks — and the banked 96 shoves once, at the flush.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(100f, 0f, 0f) };
        TFStunStack stuns = rig.AttachStun(CombatRig.Victim);
        rig.AttackerItem(TFScattergunBehaviour.KnockbackAttribute, AttributeDescriptionFormat.Additive, 1f);
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        float total = 0f;
        for (int pellet = 0; pellet < 10; pellet++)
            total += rig.Hit(9.6f, DamageType.Bullet);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");

        rig.ShoveFlush.Tick(1f);

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(-TFRadiusDamage.DamageForce(new Vector(32f, 32f, 72f), total,
                                                 TFDamageLayer.ScattergunKnockbackForceScale), impulse.X, 3f);
        Assert.True(rig.VictimState.WeaponKnockback);
        Assert.NotNull(stuns.ActiveStun);

        // The airtime's shot is spent (:383's latch): a second blast's pellets
        // bank nothing and the next flush shoves again for nobody.
        int shoves = rig.Engine.Calls.Count(c => c.Member == "Entities.ApplyVelocityImpulse");
        for (int pellet = 0; pellet < 10; pellet++)
            rig.Hit(9.6f, DamageType.Bullet);

        rig.ShoveFlush.Tick(2f);

        Assert.Equal(shoves, rig.Engine.Calls.Count(c => c.Member == "Entities.ApplyVelocityImpulse"));
    }

    [Fact]
    public void A_96_damage_hit_at_150_units_shoves_on_flat_ground()
    {
        // The gate's own margins (tf_weapon_shotgun.cpp:40, 52): the damage a
        // point past 30 and the 150-unit centres' 22500 square inside the
        // 160000 — a flat-ground victim leaves the ground anyway, the hull
        // force reversed with the jump floor (:399-401, 3388-3391).
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(150f, 0f, 0f) };
        TFStunStack stuns = rig.AttachStun(CombatRig.Victim);
        rig.AttackerItem(TFScattergunBehaviour.KnockbackAttribute, AttributeDescriptionFormat.Additive, 1f);
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)));
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        float dealt = rig.Hit(96f, Ranged);
        rig.ShoveFlush.Tick(1f);

        var pushed = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Vector impulse = pushed!.ArgAs<Vector>(1);
        Assert.Equal(-TFRadiusDamage.DamageForce(new Vector(32f, 32f, 72f), dealt,
                                                 TFDamageLayer.ScattergunKnockbackForceScale), impulse.X, 3f);
        Assert.Equal(TFAirBlastDefs.JumpMinSpeed, impulse.Z, 3f);
        Assert.True(rig.Conditions.InCond(CombatRig.Victim, TFCond.KnockedIntoAir));
        Assert.NotNull(stuns.ActiveStun);
    }

    [Fact]
    public void A_soda_poppers_hit_pays_the_scouts_hype()
    {
        // The gamerules arm (tf_gamerules.cpp:7526-7532): the dealt damage
        // remapped 1..200 onto 1..50 over the meter, held at 100 — this hit's
        // damage lands past the 200 ceiling, so the gain is the whole 50.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_soda_popper" };
        rig.AttackerItem("hype_on_damage", AttributeDescriptionFormat.Additive, 1f);
        rig.AttackerState.HypeMeter = 40f;

        float dealt = rig.Hit(300f, Ranged);

        Assert.True(dealt >= TFHype.DamageCeiling);
        Assert.Equal(TFHype.FullMeter - 10f, rig.AttackerState.HypeMeter);
    }

    [Fact]
    public void A_buffed_or_unarmed_scouts_hype_stays_put()
    {
        // SetScoutHypeMeter's IsHypeBuffed out (tf_player_shared.cpp:14126)
        // and the no-attr hit: neither moves the meter.
        var buffed = new CombatRig { AttackerWeapon = "tf_weapon_soda_popper" };
        buffed.AttackerItem("hype_on_damage", AttributeDescriptionFormat.Additive, 1f);
        buffed.AttackerState.HypeMeter = 40f;
        buffed.AttackerState.SetConditions(TFCondMask.Of(TFCond.SodapopperHype));

        buffed.Hit(300f, Ranged);

        Assert.Equal(40f, buffed.AttackerState.HypeMeter);

        var plain = new CombatRig { AttackerWeapon = "tf_weapon_soda_popper" };
        plain.AttackerState.HypeMeter = 40f;

        plain.Hit(300f, Ranged);

        Assert.Equal(40f, plain.AttackerState.HypeMeter);
    }

    [Fact]
    public void A_baby_faces_hit_pays_the_boost_and_the_taken_toll()
    {
        // The on-hit walk's next arm (tf_weaponbase.cpp:5299-5308): the
        // damage over the pep floor, held at the pep max 99 — this hit's
        // damage is past it, so the meter lands on the cap.
        var boost = new CombatRig { AttackerWeapon = "tf_weapon_pep_brawler_blaster" };
        boost.AttackerItem("boost_on_damage", AttributeDescriptionFormat.Additive, 1f);
        boost.AttackerState.HypeMeter = 40f;

        float dealt = boost.Hit(300f, Ranged);

        Assert.True(dealt >= TFHype.PepMinDamage);
        Assert.Equal(TFHype.PepMax, boost.AttackerState.HypeMeter);

        // The taken hit's toll (tf_player.cpp:9762-9774): the scout hit loses
        // the held BFB's value over the damage taken.
        var toll = new CombatRig { AttackerWeapon = "tf_weapon_pistol" };
        var bfb = toll.AttackerItem("lose_hype_on_take_damage", AttributeDescriptionFormat.Additive, 2f);
        toll.Owners.Items[CombatRig.Victim.EntryIndex] = bfb;
        toll.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_pep_brawler_blaster";
        toll.VictimState.HypeMeter = 50f;

        float taken = toll.Hit(100f, Ranged);

        // the setter's clamp floors the toll's negative at zero (:14129).
        Assert.Equal(Math.Clamp(50f - 2f * taken, 0f, TFHype.FullMeter), toll.VictimState.HypeMeter);
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
        Assert.Equal(Decision.EventsOnly, rig.Rules.Decide(in hit));

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

        Assert.Equal(Decision.EventsOnly, rig.Rules.Decide(in hit));

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

        Assert.Equal(Decision.EventsOnly, rig.Rules.Decide(in hit));

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
            Assert.Equal(Decision.EventsOnly, rig.Rules.Decide(in hit));
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
    /// The shipped Ubersaw, item 37's <c>add_onhit_ubercharge</c> 0.25 through the real
    /// definition on the medic's held item: five saw swings on the victim pay the
    /// quarters — four to the full meter, the fifth held by the cap.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_shipped_ubersaws_hits_pay_a_quarter_of_the_meter_apiece()
    {
        var ubersaw = ShippedSchema().Item(37)
            ?? throw new InvalidOperationException("the shipped schema has no item 37");
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Medic;
        rig.Owners.Items[CombatRig.Attacker.EntryIndex] = new AttributeContainer(ubersaw, AttributeProviderType.Weapon);

        for (int hit = 0; hit < 5; hit++)
            rig.Hit(20f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(
            ["charge 4 0.25", "charge 4 0.5", "charge 4 0.75", "charge 4 1", "charge 4 1"],
            rig.Mediguns.Sets);
    }

    /// <summary>The game's own schema, from the content dir the game runs (the shell facts' helper).</summary>
    private static ItemSchema ShippedSchema()
    {
        string path = RepoLayout.Repo("bin", RepoLayout.Config, "content", "tf", "scripts", "items", "items_game.txt");
        Assert.True(File.Exists(path), $"{path} is missing: the game's content dir holds the shipped schema");
        return EconTestFiles.Schema(File.ReadAllText(path));
    }

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
    /// The mark arm (:5469-5488): the Fan O'War's hit gives the victim
    /// TF_COND_MARKEDFORDEATH for the dev lifetime's 15 s, the attacker the
    /// condition's provider; a plain bat's hit marks nothing.
    /// </summary>
    [Fact]
    public void AFanOWarHitMarksTheVictimForDeath()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_bat" };
        rig.AttackerItem("mark_for_death", AttributeDescriptionFormat.Additive, 1f);

        rig.Hit(20f, Ranged);

        var conds = rig.Conditions.Of(CombatRig.Victim.EntryIndex);
        Assert.NotNull(conds);
        Assert.True(conds!.Has(TFCond.Markedfordeath));
        Assert.Equal(TFDamageLayer.MarkForDeathLifetime, conds.Duration(TFCond.Markedfordeath), 4);
        Assert.Equal(CombatRig.Attacker, conds.Provider(TFCond.Markedfordeath));

        var plain = new CombatRig { AttackerWeapon = "tf_weapon_bat" };

        plain.Hit(20f, Ranged);

        Assert.False(plain.Conditions.InCond(CombatRig.Victim, TFCond.Markedfordeath));
    }

    /// <summary>
    /// The mark arm's bookkeeping (:5474-5482): the same attacker's next mark
    /// takes the earlier victim's cond back — the one-target record stock
    /// itself calls imperfect — and the new victim carries it, attacker the
    /// provider still.
    /// </summary>
    [Fact]
    public void ASecondMarkTakesTheFirstVictimsCondBack()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_bat" };
        rig.AttackerItem("mark_for_death", AttributeDescriptionFormat.Additive, 1f);
        rig.Players.Add(CombatRig.Third.EntryIndex, TFClass.Scout);

        rig.Hit(20f, Ranged);
        rig.Hit(20f, Ranged, victim: CombatRig.Third);

        var first = rig.Conditions.Of(CombatRig.Victim.EntryIndex);
        Assert.NotNull(first);
        Assert.False(first!.Has(TFCond.Markedfordeath));
        var second = rig.Conditions.Of(CombatRig.Third.EntryIndex);
        Assert.NotNull(second);
        Assert.True(second!.Has(TFCond.Markedfordeath));
        Assert.Equal(CombatRig.Attacker, second.Provider(TFCond.Markedfordeath));
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

    // ---- the speed boost on hit (tf_weaponbase.cpp:5164-5170) --------------

    /// <summary>
    /// The speed-on-hit arm (:5164-5170): a hit whose held item carries
    /// <c>speed_boost_on_hit</c> puts its owner's feet on the carried speed
    /// boost for the attribute's seconds — the number is the duration stock's
    /// <c>AddCond</c> rides (:10919 consumes it through the walk's arm).
    /// </summary>
    [Fact]
    public void AHittingItemBoostsItsOwnersFeet()
    {
        var rig = new CombatRig();
        rig.AttackerItem("speed_boost_on_hit", AttributeDescriptionFormat.Additive, 3f);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.True(rig.Conditions.InCond(CombatRig.Attacker, TFCond.SpeedBoost));
        Assert.Equal(3f, rig.Conditions.Of(CombatRig.Attacker.EntryIndex)!.Duration(TFCond.SpeedBoost), 4);
    }

    /// <summary>
    /// The plain item: no attribute, no boost — the arm belongs to the
    /// attribute alone.
    /// </summary>
    [Fact]
    public void APlainItemLeavesTheAttackerAtNormalSpeed()
    {
        var rig = new CombatRig();

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.False(rig.Conditions.InCond(CombatRig.Attacker, TFCond.SpeedBoost));
    }

    /// <summary>
    /// The melee enemy arm (tf_weaponbase_melee.cpp:675-683): a melee hit on an enemy-player
    /// victim whose held item carries <c>speed_boost_on_hit_enemy</c> rides the attacker that
    /// many seconds of SpeedBoost — stock passes the float whole as <c>AddCond</c>' duration.
    /// </summary>
    [Fact]
    public void AMeleeHitOnAnEnemyBoostsForTheEnemySeconds()
    {
        var rig = new CombatRig();
        rig.AttackerItem("speed_boost_on_hit_enemy", AttributeDescriptionFormat.Additive, 3f);

        Assert.Equal(10f, rig.Hit(10f, TFDamageBits.Melee), 4);   // the rig's teams already differ

        Assert.True(rig.Conditions.InCond(CombatRig.Attacker, TFCond.SpeedBoost));
        Assert.Equal(3f, rig.Conditions.Of(CombatRig.Attacker.EntryIndex)!.Duration(TFCond.SpeedBoost), 4);
    }

    /// <summary>
    /// The same-team half of stock's check (:646): a melee hit on a teammate takes the enemy
    /// arm's boost from nobody — the ally twin (speed_buff_ally) is its own row.
    /// </summary>
    [Fact]
    public void ASameTeamMeleeHitKeepsTheAttackerAtNormalSpeed()
    {
        var rig = new CombatRig();
        rig.AttackerTeam = (int)TFTeam.Red;   // both players on red
        rig.AttackerItem("speed_boost_on_hit_enemy", AttributeDescriptionFormat.Additive, 3f);

        rig.Hit(10f, TFDamageBits.Melee);

        Assert.False(rig.Conditions.InCond(CombatRig.Attacker, TFCond.SpeedBoost));
    }

    /// <summary>
    /// The melee half (:646's enclosing method): a ranged hit on an enemy answers the enemy
    /// arm's boost from nobody — the arm lives in the melee weapon's own frame.
    /// </summary>
    [Fact]
    public void ARangedHitOnAnEnemyIgnoresTheEnemyArm()
    {
        var rig = new CombatRig();
        rig.AttackerItem("speed_boost_on_hit_enemy", AttributeDescriptionFormat.Additive, 3f);

        rig.Hit(10f, DamageType.Bullet);

        Assert.False(rig.Conditions.InCond(CombatRig.Attacker, TFCond.SpeedBoost));
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

    // ---- the connected damage (tf_player.cpp:9445-9468) ---------------------

    /// <summary>Handles by index, as the server's space lays them out.</summary>
    private static EntityHandle HandleOf(int index) => new(0x0001_0000u | (uint)index);

    /// <summary>The rig answering <c>Players.GetHandle</c> by the index asked.</summary>
    private static void AnswerHandles(CombatRig rig)
        => rig.Engine.ReturnsEachCall("Players.GetHandle", () =>
            rig.Engine.Last("Players.GetHandle") is { } call && call.ArgAs<int>(0) is int index && index > 0
                ? HandleOf(index)
                : EntityHandle.None);

    /// <summary>
    /// The Third Degree's arm (<c>damage_all_connected</c>, the Third Degree's row):
    /// the hit stands on the victim and is dealt again, whole, to the medic
    /// healing them — <c>AddConnectedPlayers</c>' first link (tf_player.cpp:10052-10064).
    /// </summary>
    [Fact]
    public void A_connected_hit_reaches_the_victims_healer()
    {
        var rig = new CombatRig();
        AnswerHandles(rig);
        rig.Healing.Healers[CombatRig.Victim.EntryIndex] = [CombatRig.Third.EntryIndex];
        rig.AttackerItem(TFDamageLayer.DamageAllConnected, AttributeDescriptionFormat.Additive, 1f);

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        var pass = Assert.Single(rig.Damage.Hits);
        Assert.Equal((HandleOf(CombatRig.Third.EntryIndex), CombatRig.Attacker, 20f, Ranged),
                     (pass.Victim, pass.Attacker, pass.Damage, pass.Type));
    }

    /// <summary>
    /// The closure is transitive (:10054-10065): the healer's own beam's
    /// patient and the healer's healer ride too, each once — the mutual pair
    /// answers once, stock's <c>vecPlayers.Find</c> dedup (:10049).
    /// </summary>
    [Fact]
    public void The_connected_pass_walks_the_whole_heal_graph_once()
    {
        var rig = new CombatRig();
        AnswerHandles(rig);
        var medic = rig.Players.Add(CombatRig.Third.EntryIndex, TFClass.Medic);
        medic.HealTargetIndex = 6;                                   // the medic's beam is on patient 6
        rig.Healing.Healers[CombatRig.Victim.EntryIndex] = [CombatRig.Third.EntryIndex];
        rig.Healing.Healers[CombatRig.Third.EntryIndex] = [7];       // and a healer behind the medic
        rig.AttackerItem(TFDamageLayer.DamageAllConnected, AttributeDescriptionFormat.Additive, 1f);

        rig.Hit(20f, Ranged);

        Assert.Equal([HandleOf(CombatRig.Third.EntryIndex), HandleOf(6), HandleOf(7)],
                     rig.Damage.Hits.Select(h => h.Victim).ToList());
    }

    [Fact]
    public void A_connected_graphs_cycle_hits_each_player_once()
    {
        var rig = new CombatRig();
        AnswerHandles(rig);
        var medic = rig.Players.Add(CombatRig.Third.EntryIndex, TFClass.Medic);
        medic.HealTargetIndex = CombatRig.Victim.EntryIndex;         // the beam back on the victim: the cycle
        rig.Healing.Healers[CombatRig.Victim.EntryIndex] = [CombatRig.Third.EntryIndex];
        rig.AttackerItem(TFDamageLayer.DamageAllConnected, AttributeDescriptionFormat.Additive, 1f);

        rig.Hit(20f, Ranged);

        Assert.Equal([HandleOf(CombatRig.Third.EntryIndex)],
                     rig.Damage.Hits.Select(h => h.Victim).ToList());
    }

    [Fact]
    public void A_hit_without_the_attribute_is_dealt_once()
    {
        var rig = new CombatRig();
        AnswerHandles(rig);
        rig.Healing.Healers[CombatRig.Victim.EntryIndex] = [CombatRig.Third.EntryIndex];

        Assert.Equal(20f, rig.Hit(20f, Ranged), 4);

        Assert.Empty(rig.Damage.Hits);
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

    /// <summary>
    /// The Persuader's arm (stock tf_weaponbase.cpp:5152-5162): a hit whose held
    /// item carries <c>charge_meter_on_hit</c> pays that fraction of the charge
    /// meter — twenty points on a half refill — to the attacker's own.
    /// </summary>
    [Fact]
    public void The_charge_on_hit_fills_the_demoman_meter()
    {
        var rig = new CombatRig();
        rig.AttackerState.ChargeMeter = 30f;
        rig.AttackerItem("charge_meter_on_hit", AttributeDescriptionFormat.Additive, 0.2f);

        rig.Hit(10f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(50f, rig.AttackerState.ChargeMeter, 4);
    }

    [Fact]
    public void The_charge_on_hit_caps_the_meter_at_one_hundred()
    {
        var rig = new CombatRig();
        rig.AttackerState.ChargeMeter = 95f;
        rig.AttackerItem("charge_meter_on_hit", AttributeDescriptionFormat.Additive, 0.5f);

        rig.Hit(10f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(100f, rig.AttackerState.ChargeMeter, 4);
    }

    /// <summary>
    /// The carbine's arm (<c>CTFChargedSMG::ApplyOnHitAttributes</c>, stock
    /// tf_weapon_smg.cpp:178-196): while the owner's buff is cold, a hit's
    /// surviving damage times the held item's charge rate rides onto the SMG's
    /// own boost meter (:186-189), capped at the meter's 100 (:190-193).
    /// </summary>
    [Fact]
    public void A_hit_on_the_carbine_charges_the_boost_meter()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_charged_smg" };
        rig.AttackerItem("minicrit_boost_charge_rate", AttributeDescriptionFormat.Additive, 0.5f);
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();
        rig.Entities.WeaponEntity.State.BehaviourMeter = 10f;

        rig.Hit(20f, DamageType.Bullet);

        // Twenty of damage at the half rate: the whole surviving hit lands on
        // the weapon's own meter, not a tenth of it.
        Assert.Equal(20f, rig.Entities.WeaponEntity.State.BehaviourMeter, 4);
    }

    [Fact]
    public void The_carbine_boost_caps_at_the_meters_hundred()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_charged_smg" };
        rig.AttackerItem("minicrit_boost_charge_rate", AttributeDescriptionFormat.Additive, 1f);
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();
        rig.Entities.WeaponEntity.State.BehaviourMeter = 95f;

        rig.Hit(20f, DamageType.Club);

        Assert.Equal(TFWeaponBehaviour.ChargedSmgMeterCap,
                     rig.Entities.WeaponEntity.State.BehaviourMeter, 4);
    }

    [Fact]
    public void The_living_carbine_buff_leaves_the_boost_meter_still()
    {
        // The owner's live buff keeps the meter cold (:184), and the hit it
        // does land is the buff's own mini-crit — the buffed attacker's arm
        // (tf_gamerules.cpp:6177-6182), twenty at the mini multiplier is 27.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_charged_smg" };
        rig.AttackerItem("minicrit_boost_charge_rate", AttributeDescriptionFormat.Additive, 0.5f);
        rig.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        rig.Entities.WeaponEntity.State.BindLocal();
        rig.Entities.WeaponEntity.State.BehaviourMeter = 10f;
        rig.Conditions.Add(CombatRig.Attacker, TFCond.EnergyBuff);

        Assert.Equal(27f, rig.Hit(20f, DamageType.Bullet), 3);
        Assert.Equal(10f, rig.Entities.WeaponEntity.State.BehaviourMeter, 4);
    }

    [Fact]
    public void A_hit_with_no_charge_attribute_moves_no_meter()
    {
        var rig = new CombatRig();
        rig.AttackerState.ChargeMeter = 30f;

        rig.Hit(10f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(30f, rig.AttackerState.ChargeMeter, 4);
    }

    // ---- the Vita-Saw's organs (tf_weapon_bonesaw.cpp:81-118) --------------

    /// <summary>
    /// A melee hit on an enemy player from a bearer of
    /// <c>ubercharge_preserved_on_spawn_max</c> counts one more organ and tells
    /// the medigun to keep the count's charge through the next spawn — one hit,
    /// one organ, 0.15 of a charge (:105-110), the cap asked inside the medigun.
    /// </summary>
    [Fact]
    public void AVitaSawHitKeepsTheOrgansWorthOfCharge()
    {
        var rig = new CombatRig();
        rig.AttackerItem("ubercharge_preserved_on_spawn_max", AttributeDescriptionFormat.Additive, 0.5f);

        rig.Hit(10f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(1, rig.AttackerState.Decapitations);
        Assert.Equal("preserve 4 0.15", Assert.Single(rig.Mediguns.Sets));
    }

    /// <summary>
    /// The second organ stacks: the count is the carrier's, so the next hit asks
    /// for two organs' worth (:90's GetDecapitations() + 1).
    /// </summary>
    [Fact]
    public void TheOrgansCountStacksAcrossHits()
    {
        var rig = new CombatRig();
        rig.AttackerItem("ubercharge_preserved_on_spawn_max", AttributeDescriptionFormat.Additive, 0.5f);
        rig.AttackerState.Decapitations = 1;

        rig.Hit(10f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(2, rig.AttackerState.Decapitations);
        Assert.Equal("preserve 4 0.3", Assert.Single(rig.Mediguns.Sets));
    }

    /// <summary>
    /// A ranged hit keeps nothing: the arm is the bonesaw's melee damage
    /// (:85's IsPlayer gate rides the swing), and the same holds for a plain
    /// bonesaw — no attribute, no organ, no ask.
    /// </summary>
    [Fact]
    public void AHitWithoutTheArmKeepsNothing()
    {
        var ranged = new CombatRig();
        ranged.AttackerItem("ubercharge_preserved_on_spawn_max", AttributeDescriptionFormat.Additive, 0.5f);
        ranged.Hit(10f, Ranged);

        var plain = new CombatRig();
        plain.Hit(10f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(0, ranged.AttackerState.Decapitations);
        Assert.Empty(ranged.Mediguns.Sets);
        Assert.Equal(0, plain.AttackerState.Decapitations);
        Assert.Empty(plain.Mediguns.Sets);
    }

    /// <summary>
    /// The shipped Vita-Saw, item 173's <c>ubercharge_preserved_on_spawn_max</c> through
    /// the real definition on the bearer's held item: the saw's melee hit on the enemy
    /// counts the organ and asks the medigun to keep its 0.15 — the rig facts' ask,
    /// from the item as shipped.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_shipped_vita_saws_melee_hit_banks_an_organ()
    {
        var vita = ShippedSchema().Item(173)
            ?? throw new InvalidOperationException("the shipped schema has no item 173");
        var rig = new CombatRig();
        rig.Owners.Items[CombatRig.Attacker.EntryIndex] = new AttributeContainer(vita, AttributeProviderType.Weapon);

        rig.Hit(10f, TFDamageBits.Melee | DamageType.Club);

        Assert.Equal(1, rig.AttackerState.Decapitations);
        Assert.Equal(["preserve 4 0.15"], rig.Mediguns.Sets);
    }

    // ---- the thruster's landing (tf_player.cpp:8991-9038) -------------------

    /// <summary>One worn item's word on the player's own manager, where stock's
    /// player hooks walk.</summary>
    private static AttributeContainer Worn(int defindex, string attributeClass, AttributeDescriptionFormat format, float value)
    {
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(
            defindex, attributeClass, attributeClass, format,
            AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("w")), value));
        return manager;
    }

    /// <summary>The Cozy Camper worn: a wearable, so its aiming_no_flinch reaches
    /// the player's own manager (stock's CALL_ATTRIB_HOOK_INT on the player,
    /// tf_player.cpp:3419), never the held rifle.</summary>
    private static AttributeContainer CamperWorn()
    {
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(
            9102, "aiming no flinch", TFDamageLayer.NoFlinchAttribute, AttributeDescriptionFormat.Additive,
            AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("n")), 1f));
        return manager;
    }

    /// <summary>Puts the pushback word on the victim's held item, as the
    /// loadout handed it out: a held weapon row, whose item the harness's
    /// <c>ActiveItemOf</c> answers from the items dictionary.</summary>
    private static void GivePushbackWord(CombatRig rig)
    {
        var item = new AttributeContainer(null, AttributeProviderType.Generic);
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(
            9101, "pushback", "falling_impact_radius_pushback", AttributeDescriptionFormat.Additive,
            AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("p")), 1f));
        rig.Owners.Items[CombatRig.Victim.EntryIndex] = item;
        rig.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_pistol";
    }

    [Fact]
    public void AThrusterLandingThrowsEnemiesAlongTheRingAndQuartersTheFall()
    {
        // The ring (:21509-21537): an enemy whose eye sits in it is thrown
        // along the flat line out, z at a full unit, force between the
        // convars' ends at the landing speed's place (:8997). At 900 u/s that
        // is 100 + 200 · 8/9 = 277.78, and the fall keeps a quarter of its
        // bite under the rocket-pack cond (:8937's gate, :9037).
        var rig = new CombatRig();
        GivePushbackWord(rig);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Rocketpack));
        rig.Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 1);
        rig.Engine.FillsSphere(CombatRig.Attacker);

        float damage = rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);

        Assert.Equal(4.6875f, damage, 4);                                              // 18.75 · 0.25 (:9037)
        var push = Assert.Single(rig.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
        Assert.Equal(CombatRig.Attacker, push.Args[0]);
        float expected = rig.ConVars.Declared<TFRocketPackImpactPushMinConVar>().Value
                       + (rig.ConVars.Declared<TFRocketPackImpactPushMaxConVar>().Value
                          - rig.ConVars.Declared<TFRocketPackImpactPushMinConVar>().Value)
                         * (900f - 100f) / (1000f - 100f);                             // :8997/:8998 ends
        var impulse = Assert.IsType<Vector>(push.Args[1]);
        Assert.Equal(0f, impulse.X, 3);
        Assert.Equal(0f, impulse.Y, 3);
        Assert.Equal(expected, impulse.Z, 3);                                          // :21524-21526
    }

    [Fact]
    public void AThrusterWithoutThePushbackWordKeepsTheRingClosedButStillQuartersTheFall()
    {
        // The held item's word opens the ring (:8993); without it the push
        // never asks, yet the quartering (:9037) rides the cond alone.
        var rig = new CombatRig();
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Rocketpack));
        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 1);
        rig.Engine.FillsSphere(CombatRig.Attacker);

        float damage = rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);

        Assert.Equal(4.6875f, damage, 4);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
    }

    [Fact]
    public void ALandingWithoutTheRocketPackCondKeepsItsFullFallAndTheRingShut()
    {
        var rig = new CombatRig();
        GivePushbackWord(rig);
        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 1);
        rig.Engine.FillsSphere(CombatRig.Attacker);

        float damage = rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);

        Assert.Equal(18.75f, damage, 4);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
    }

    [Fact]
    public void ARocketPackLandingPutsOutVisibleBurningTeammatesInTheRing()
    {
        // The teammates' half (:9014-9034): living, of the landing team, in
        // sight — the burning taken off and the flame-out heard. The rig's
        // blue attacker is made the red teammate; at 128 units it sits inside
        // the ring's 212 at the 900 u/s landing.
        var rig = new CombatRig { AttackerAt = new Vector(128f, 0f, 0f), AttackerTeam = (int)TFTeam.Red };
        GivePushbackWord(rig);
        rig.VictimState.SetConditions(TFCondMask.Of(TFCond.Rocketpack));
        rig.AttackerState.SetConditions(TFCondMask.Of(TFCond.Burning));
        Assert.True(rig.Conditions.InCond(CombatRig.Attacker, TFCond.Burning));
        rig.Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, default, default, EntityHandle.None));
        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 1);
        rig.Engine.FillsSphere(CombatRig.Attacker);

        rig.Hit(MoveMath.FallDamage(900f), DamageType.Fall, attacker: EntityHandle.None);

        Assert.False(rig.Conditions.InCond(CombatRig.Attacker, TFCond.Burning));     // :9031
        var flameOut = Assert.Single(rig.Engine.Calls, c => c.Member == "Sound.EmitSoundAtPosition");
        Assert.Equal("TFPlayer.FlameOut", flameOut.Args[0]);                          // :9032
    }

    // ---- the flinch ---------------------------------------------------------

    /// <summary>The rig with a punch already on the victim's view, to see what the hit keeps.</summary>
    private static CombatRig Punched(CombatRig rig)
    {
        rig.Engine.Returns("Players.GetPunch", new Vector(1f, 3f, 4f));
        return rig;
    }

    [Fact]
    public void AHitFlinchesTheVictimsViewTwoDegreesAndPlaysTheChestFlinch()
    {
        // OnTakeDamage's tail (tf_player.cpp:9599-9621): any kinded hit with
        // damage lands punches the view two degrees down through
        // ApplyPunchImpulseX (:3430) — the pitch set, yaw and roll kept — and
        // the chest flinch plays for the generic hitgroup (PlayFlinch
        // :15084-15110, alive and undisguised).
        var rig = Punched(new CombatRig());

        rig.Hit(60f, DamageType.Bullet);

        var punched = rig.Engine.Last("Players.SetPunch");
        Assert.NotNull(punched);
        Assert.Equal(new Vector(-TFDamageLayer.FlinchPunch, 3f, 4f), punched!.ArgAs<Vector>(1));
        Assert.Contains(rig.Temps.Animations,
                        a => a.PackedPlayer == TempEntWriter.PackEHandle(CombatRig.Victim)
                             && a.AnimEvent == (int)SourceSharp.Shared.Anim.PlayerAnimEvent.FlinchChest);
    }

    [Fact]
    public void TheCampersAimedChargedRifleLeavesTheViewAlone()
    {
        // ApplyPunchImpulseX's no-flinch (tf_player.cpp:3418-3423): the sniper
        // holding their aim through a fully charged sniper rifle whose item
        // carries aiming_no_flinch — the Cozy Camper — keeps their view.
        var held = new CombatRig();
        held.VictimState.PlayerClass = (int)TFClass.Sniper;
        held.VictimState.SetConditions(TFCondMask.Of(TFCond.Aiming));
        held.VictimWeapon = "tf_weapon_sniperrifle";
        held.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_sniperrifle";
        held.Owners.Managers[CombatRig.Victim.EntryIndex] = CamperWorn();
        held.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        held.Entities.WeaponEntity.State.BindLocal();
        held.Entities.WeaponEntity.State.BehaviourMeter = held.Clock.Now - 4f;   // past the 150 cap
        Punched(held);

        held.Hit(60f, DamageType.Bullet);

        Assert.DoesNotContain(held.Engine.Calls, c => c.Member == "Players.SetPunch");
        Assert.Empty(held.Temps.Animations);
    }

    [Fact]
    public void TheCamperNeedsTheChargeAndTheAttribute()
    {
        // Both gates of :3418-3423 failing: a charge short of the cap, and the
        // full charge without the item — each leaves the punch on.
        var uncharged = new CombatRig();
        uncharged.VictimState.PlayerClass = (int)TFClass.Sniper;
        uncharged.VictimState.SetConditions(TFCondMask.Of(TFCond.Aiming));
        uncharged.VictimWeapon = "tf_weapon_sniperrifle";
        uncharged.Owners.Held[CombatRig.Victim.EntryIndex] = "tf_weapon_sniperrifle";
        uncharged.Owners.Managers[CombatRig.Victim.EntryIndex] = CamperWorn();
        uncharged.Entities.WeaponEntity = new SsWeapon { Handle = RigEntities.Weapon };
        uncharged.Entities.WeaponEntity.State.BindLocal();
        uncharged.Entities.WeaponEntity.State.BehaviourMeter = uncharged.Clock.Now - 1f;   // 50 of the cap
        Punched(uncharged);

        uncharged.Hit(60f, DamageType.Bullet);
        Assert.NotNull(uncharged.Engine.Last("Players.SetPunch"));

        uncharged.Entities.WeaponEntity.State.BehaviourMeter = uncharged.Clock.Now - 4f;
        uncharged.Owners.Managers.Remove(CombatRig.Victim.EntryIndex);
        uncharged.Engine.Clear();

        uncharged.Hit(60f, DamageType.Bullet);
        var plainPunch = uncharged.Engine.Last("Players.SetPunch");
        Assert.NotNull(plainPunch);
        Assert.Equal(new Vector(-TFDamageLayer.FlinchPunch, 3f, 4f), plainPunch!.ArgAs<Vector>(1));
    }

    [Fact]
    public void TheAimingSniperSparesTheFarMinigun()
    {
        // The courtesy (:9603-9612): an aimed sniper hit by a minigun over 750
        // units away does not flinch; inside it, they do.
        var far = new CombatRig { AttackerWeapon = "tf_weapon_minigun", AttackerAt = new Vector(800f, 0f, 0f) };
        far.VictimState.PlayerClass = (int)TFClass.Sniper;
        far.VictimState.SetConditions(TFCondMask.Of(TFCond.Aiming));
        Punched(far);

        far.Hit(60f, DamageType.Bullet);
        Assert.DoesNotContain(far.Engine.Calls, c => c.Member == "Players.SetPunch");

        var near = new CombatRig { AttackerWeapon = "tf_weapon_minigun", AttackerAt = new Vector(100f, 0f, 0f) };
        near.VictimState.PlayerClass = (int)TFClass.Sniper;
        near.VictimState.SetConditions(TFCondMask.Of(TFCond.Aiming));
        Punched(near);

        near.Hit(60f, DamageType.Bullet);
        Assert.NotNull(near.Engine.Last("Players.SetPunch"));
    }
}
