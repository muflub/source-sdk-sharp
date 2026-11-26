using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Server.Econ;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Tests.Buildings;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Rescue Ranger's shot (<c>items_working.tsv:997</c>): the bolt is the
/// arrow's own fire under the item's override
/// (<c>tf_weaponbase_gun.cpp:314-322</c>) — the claw dressed at spawn
/// (<c>tf_projectile_arrow.cpp:204-207</c>), the shotgun's fixed flight
/// (<c>tf_weapon_shotgun.cpp:507-516</c>) — and the impact that mends the
/// shooter's side (<c>BuildingHealingArrow</c>,
/// <c>tf_projectile_arrow.cpp:651-688</c>).
/// </summary>
public sealed class TFRescueBoltFacts
{
    private const string Rescue = "tf_weapon_shotgun_building_rescue";
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>The fixture's <c>Damage</c> row, the shot the base's <c>GetProjectileDamage</c> hands the bolt.</summary>
    private const float BoltDamage = 40f;

    private static TFWeaponScript RescueScript() => WeaponFixtures.ParseExtra(Rescue);

    private static WeaponAttackContext AttackFrom(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    [Fact]
    public void TheRescueRangersOverrideFiresTheRepairBolt()
    {
        // FireProjectile's override (tf_weaponbase_gun.cpp:260-268): the
        // Rescue Ranger's item names 18, TF_PROJECTILE_BUILDING_REPAIR_BOLT,
        // over the script's arrow — the bolt rides the arrow entity at the
        // shotgun's fixed speed, carrying the shot's plain damage (the base
        // GetProjectileDamage; the ramp is the bow's alone).
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem(TFProjectileEffects.OverrideProjectileAttribute,
                                            AttributeDescriptionFormat.Additive,
                                            (float)TFProjectile.BuildingRepairBolt);
        var arrow = rig.Arrow();
        rig.Directory.Instance = arrow;

        rig.Effects.Projectile(AttackFrom(3), RescueScript(), new ShotDamage(40f), 0f);

        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.Arrow, create!.ArgAs<string>(0));

        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(TFProjectileDefs.RescueBoltSpeed, velocity!.ArgAs<Vector>(1).X, 0.5f);

        Assert.Equal(40f, arrow.ShotDamage, 2f);
        Assert.Equal(TFWeaponId.ShotgunBuildingRescue, arrow.Weapon);
        Assert.Equal(Rescue, arrow.WeaponName);
        Assert.False(arrow.Lit); // no bow touched this shot
    }

    [Fact]
    public void TheRepairBoltSpawnsAsTheClaw()
    {
        // Spawn's arm (tf_projectile_arrow.cpp:204-207): the rescue bolt's
        // MODEL_ARROW_BUILDING_REPAIR where the bow's shaft is the plain one.
        var rig = new ProjectileRig();
        var arrow = rig.Arrow();
        arrow.ArmedBy(TFWeaponId.ShotgunBuildingRescue, Rescue, critical: false, lit: false,
                      BoltDamage, TFProjectileDefs.RescueBoltGravity,
                      TFProjectile.BuildingRepairBolt);

        arrow.OnSpawn();

        var shell = rig.Engine.Last("EntityRender.SetModel");
        Assert.NotNull(shell);
        Assert.Equal(TFProjectileDefs.RepairClawModel, shell!.ArgAs<string>(1));
    }

    [Fact]
    public void TheRepairBoltFallsAtTheShotgunsGravity()
    {
        // GetProjectileGravity's remap held at the shotgun's fixed charge
        // (tf_weapon_shotgun.cpp:511-516): 0.2, not the bow's charge ramp.
        var rig = new ProjectileRig();
        var arrow = rig.Arrow();
        arrow.ArmedBy(TFWeaponId.ShotgunBuildingRescue, Rescue, critical: false, lit: false,
                      BoltDamage, TFProjectileDefs.RescueBoltGravity,
                      TFProjectile.BuildingRepairBolt);

        arrow.OnSpawn();

        var gravity = rig.Engine.Last("EntityPhysics.SetGravity");
        Assert.NotNull(gravity);
        Assert.Equal(TFProjectileDefs.RescueBoltGravity, gravity!.ArgAs<float>(1), 0.001f);
    }

    // --- The impact's mend (BuildingHealingArrow, tf_projectile_arrow.cpp:651-688).

    /// <summary>
    /// The bolt in flight over a buildings rig: an arrow armed as the rescue's
    /// shot by the fire path's own arm, its owner the red builder, the item
    /// carrying <c>arrow_heals_buildings</c> at <paramref name="worth"/>.
    /// </summary>
    private static (BuildingsRig Rig, TFArrow Arrow, TestBuilding Building, EntityHandle Builder)
        Bolt(BuildingsRig rig, float worth, int metal)
    {
        EntityHandle builder = rig.AddBuilder(TFTeam.Red);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = metal;

        int deficit = 40;
        building.Health = building.MaxHealthForCurrentLevel - deficit;

        var motionLog = new RecordingLog();
        var motionDirectory = new EntityDirectory(motionLog);
        var motion = new EntityMotion(motionLog, rig.Engine, rig.Engine,
            DeclaredConVars.Holding<SvGravityConVar>("600"), DeclaredConVars.Fresh<MoversBlockedConVar>(),
            DeclaredConVars.Fresh<MoversPushedConVar>(), rig.Engine, motionDirectory,
            new PhysicsPushedEntities(new EnginePushWorld(rig.Engine, rig.Engine, rig.Engine, motionDirectory),
                                      motionLog));

        var scopes = rig.Services.Resolve<TFDamageScopes>()!;
        var arrow = new TFArrow(rig.Engine, motion, rig.Engine, scopes,
                                rig.Services.Resolve<DamageHitGroups>()!,
                                rig.Services.Resolve<IEntityDirectory>()!, rig.Owners, rig.Hooks,
                                rig.Engine, rig.Effects, rig.Engine, rig.Engine, rig.Engine,
                                rig.Services.Resolve<DamageInflictors>()!);
        arrow.BindAreas(TestServices.Areas(rig.Engine));
        arrow.Handle = rig.NewHandle();
        // The toucher's pass-through ask, as the projectile rig's default.
        rig.Engine.Returns("EntityPhysics.GetSolidFlags", SolidFlags.None);
        // A building is no player: stock's teammate window gates on
        // IsPlayer (tf_projectile_arrow.cpp), so only the builder answers it
        // and the bolt's touch reaches the building's arm from the first
        // frame.
        rig.Engine.ReturnsEachCall("Entities.IsPlayer",
            () => rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == builder);
        if (worth > 0f)
            rig.Owners.Items[builder.EntryIndex] =
                rig.AttributeItem("arrow_heals_buildings", AttributeDescriptionFormat.Additive, worth);
        rig.Engine.Returns("Players.GetTeam", (int)TFTeam.Red);
        arrow.ArmedBy(TFWeaponId.ShotgunBuildingRescue, Rescue, critical: false, lit: false,
                      BoltDamage, TFProjectileDefs.RescueBoltGravity,
                      TFProjectile.BuildingRepairBolt);
        arrow.FiredBy(builder, new Vector(TFProjectileDefs.RescueBoltSpeed, 0f, 0f));
        return (rig, arrow, building, builder);
    }

    [Fact]
    public void TheBoltMendsTheShootersBuildingForItsWorth()
    {
        // BuildingHealingArrow's core (:663-682): the shooter's own side, the
        // item's worth in health at four the metal, paid from the purse —
        // and the claw's particle on the arrow's position telling where.
        var rig = new BuildingsRig();
        var (_, arrow, building, builder) = Bolt(rig, worth: 40f, metal: 100);
        int before = building.Health;

        arrow.OnTouch(building.Handle);

        Assert.Equal(building.MaxHealthForCurrentLevel, building.Health);
        Assert.Equal(before + 40, building.Health);
        Assert.Equal((builder, 10, TFAmmoNames.Name(TFAmmo.Metal)),
                     Assert.Single(rig.Purse.Paid));

        var mend = Assert.Single(rig.Effects.Dispatched);
        Assert.Equal("ParticleEffect", mend.Name);
        Assert.Equal(rig.ParticleIndices["repair_claw_heal_red"], mend.Data.HitBox);
    }

    [Fact]
    public void TheBoltPaysOnlyForTheMetalThePurseHolds()
    {
        // The clamp to the purse (:675's cost): five metal mends twenty, the
        // ratio's four, and no more than the pool held.
        var rig = new BuildingsRig();
        var (_, arrow, building, builder) = Bolt(rig, worth: 40f, metal: 5);
        int before = building.Health;

        arrow.OnTouch(building.Handle);

        Assert.Equal(before + 20, building.Health);
        Assert.Equal((builder, 5, TFAmmoNames.Name(TFAmmo.Metal)),
                     Assert.Single(rig.Purse.Paid));
        // A mend is a mend: the claw tells anyway.
        var partial = Assert.Single(rig.Effects.Dispatched);
        Assert.Equal("ParticleEffect", partial.Name);
    }

    [Fact]
    public void TheBoltLeavesTheEnemysBuildingAlone()
    {
        // The team gate (:663-664): the blue building is the blow's alone —
        // the arrow's damage arm, no mend, no metal.
        var rig = new BuildingsRig();
        var (_, arrow, building, builder) = Bolt(rig, worth: 40f, metal: 100);
        rig.Engine.Returns("Players.GetTeam", (int)TFTeam.Blue); // the shooter reads blue now
        int before = building.Health;

        arrow.OnTouch(building.Handle);

        Assert.Equal(before, building.Health);
        Assert.Empty(rig.Purse.Paid);
        Assert.Empty(rig.Effects.Dispatched);
    }

    [Fact]
    public void TheBoltWantsTheFiredItemsWorth()
    {
        // No arrow_heals_buildings on the fired item (:657's ask): the plain
        // arrow's impact, nothing mended.
        var rig = new BuildingsRig();
        var (_, arrow, building, _) = Bolt(rig, worth: 0f, metal: 100);
        int before = building.Health;

        arrow.OnTouch(building.Handle);

        Assert.Equal(before, building.Health);
        Assert.Empty(rig.Purse.Paid);
    }

    [Fact]
    public void TheBoltSparesASappedBuilding()
    {
        // The sapper's hold (:670's gate): the mended building is one the
        // wrench could mend; a sapped one waits for the spy.
        var rig = new BuildingsRig();
        var (_, arrow, building, _) = Bolt(rig, worth: 40f, metal: 100);
        building.OnAddSapper(rig.NewHandle());
        int before = building.Health;

        arrow.OnTouch(building.Handle);

        Assert.Equal(before, building.Health);
        Assert.Empty(rig.Purse.Paid);
    }

    [Fact]
    public void TheWranglersPlateMendsAtAThird()
    {
        // The shielded sentry's arm (:676-679): SHIELD_NORMAL_VALUE's scale
        // on the worth before the purse sees it — forty asks thirteen.
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(TFTeam.Red);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        rig.Clock.Now = 100f;
        sentry.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();
        sentry.LaserAim(new(600f, 0f, TFSentryGun.EyeHeightForLevel(1)));
        sentry.OnThink("SentryThink");
        Assert.Equal(TFSentryGun.ShieldNormal, sentry.ShieldLevel);

        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 100;
        sentry.Health = sentry.MaxHealthForCurrentLevel - 40;

        var motionLog = new RecordingLog();
        var motionDirectory = new EntityDirectory(motionLog);
        var motion = new EntityMotion(motionLog, rig.Engine, rig.Engine,
            DeclaredConVars.Holding<SvGravityConVar>("600"), DeclaredConVars.Fresh<MoversBlockedConVar>(),
            DeclaredConVars.Fresh<MoversPushedConVar>(), rig.Engine, motionDirectory,
            new PhysicsPushedEntities(new EnginePushWorld(rig.Engine, rig.Engine, rig.Engine, motionDirectory),
                                      motionLog));
        var scopes = rig.Services.Resolve<TFDamageScopes>()!;
        var arrow = new TFArrow(rig.Engine, motion, rig.Engine, scopes,
                                rig.Services.Resolve<DamageHitGroups>()!,
                                rig.Services.Resolve<IEntityDirectory>()!, rig.Owners, rig.Hooks,
                                rig.Engine, rig.Effects, rig.Engine, rig.Engine, rig.Engine,
                                rig.Services.Resolve<DamageInflictors>()!);
        arrow.BindAreas(TestServices.Areas(rig.Engine));
        arrow.Handle = rig.NewHandle();
        rig.Engine.Returns("EntityPhysics.GetSolidFlags", SolidFlags.None);
        // The sentry is no player either, as in Bolt: the bolt's touch must
        // reach the building's arm through the window's IsPlayer gate.
        rig.Engine.ReturnsEachCall("Entities.IsPlayer",
            () => rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == builder);
        rig.Owners.Items[builder.EntryIndex] =
            rig.AttributeItem("arrow_heals_buildings", AttributeDescriptionFormat.Additive, 40f);
        rig.Engine.Returns("Players.GetTeam", (int)TFTeam.Red);
        arrow.ArmedBy(TFWeaponId.ShotgunBuildingRescue, Rescue, critical: false, lit: false,
                      BoltDamage, TFProjectileDefs.RescueBoltGravity,
                      TFProjectile.BuildingRepairBolt);
        arrow.FiredBy(builder, new Vector(TFProjectileDefs.RescueBoltSpeed, 0f, 0f));

        arrow.OnTouch(sentry.Handle);

        // Thirteen asked (the third of forty, floored), the purse's four-metal
        // round of it: sixteen mended for four paid.
        Assert.Equal(sentry.MaxHealthForCurrentLevel - 40 + 16, sentry.Health);
        Assert.Equal(4, Assert.Single(rig.Purse.Paid).Count);
    }
}
