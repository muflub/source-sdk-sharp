using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The pyro's flare gun (<c>CTFFlareGun</c>, <c>tf_weapon_flaregun.cpp</c>) and
/// its projectile (<c>CTFProjectile_Flare</c>, <c>tf_projectile_flare.cpp</c>):
/// the kind the flare gun's row runs, the shot it fires and the touch that
/// burns.
/// </summary>
public sealed class TFFlareFacts
{
    /// <summary>The flare gun's script, from the extras set (the fixture lives beside its README).</summary>
    private static TFWeaponScript FlareScript()
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get("tf_weapon_flaregun")
            ?? throw new InvalidOperationException("no script tf_weapon_flaregun");
    }

    [Fact]
    public void The_flaregun_runs_the_projectile_behaviour()
    {
        Assert.Equal(TFWeaponKind.Projectile, TFWeaponKinds.Of(FlareScript()));
    }

    [Fact]
    public void The_armed_flare_writes_its_team_and_crit_to_the_wire()
    {
        // The trail's pair (GetFlareTrailParticleName's inputs): the arm
        // writes what the client's mirror reads, the rocket ArmedBy's rule.
        var rig = new ProjectileRig();
        var flare = rig.Flare();
        rig.Engine.Returns("Players.GetTeam", 3);

        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: true,
                      TFProjectileDefs.FlareGravity, ownerIndex: 3,
                      flareMode: TFProjectileBehaviour.FlareNormal);

        Assert.True(flare.State.Critical);
        Assert.Equal(3, flare.State.TeamNumber);
    }

    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    [Fact]
    public void The_flare_gun_fires_a_flare_from_its_muzzle()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null; // the mapping is what the fact holds

        rig.Effects.Projectile(Attack(3), FlareScript(), default, 0f);

        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.Flare, create!.ArgAs<string>(0));

        // Born at FireFlare's own muzzle (vecOffset from the eye,
        // tf_weaponbase_gun.cpp:746) and brought to life before the arming
        // asks for its managed half.
        var placed = rig.Engine.Last("Entities.SetOrigin");
        Assert.NotNull(placed);
        var muzzle = placed!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.FlareMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.FlareMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.FlareMuzzleUp, muzzle.Z, 0.5f);
        var spawned = rig.Engine.Last("Entities.Spawn");
        Assert.NotNull(spawned);
        Assert.Equal(ProjectileRig.ProjectileHandle, spawned!.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void The_flare_leaves_at_the_stock_speed()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = rig.Flare();

        rig.Effects.Projectile(Attack(3), FlareScript(), default, 0f);

        // GetProjectileSpeed's base: FLARE_SPEED (tf_projectile_flare.cpp:617-623);
        // the shipped script names no speed of its own.
        var velocity = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(velocity);
        Assert.Equal(TFProjectileDefs.FlareSpeed, velocity!.ArgAs<Vector>(1).X, 0.5f);
    }

    private static float SeenDamage(ProjectileRig rig) => EngineCallFacts.ArgAs<float>(rig.Engine.Last("Damage.TakeDamage"), 2);

    private static DamageType SeenBits(ProjectileRig rig) => EngineCallFacts.ArgAs<DamageType>(rig.Engine.Last("Damage.TakeDamage"), 3);

    /// <summary>A launched flare whose touch is about to be asked.</summary>
    private static (ProjectileRig Rig, TFFlare Flare) FiredFlare(bool burning, bool critShot,
        int flareMode = TFProjectileBehaviour.FlareNormal)
    {
        var rig = new ProjectileRig();
        rig.Conditions.EveryoneBurns = burning;
        // The victim is a player; the world (the stick arm's handle) is not.
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
        var flare = rig.Flare();
        flare.OnSpawn();
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: critShot,
                      TFProjectileDefs.FlareGravity, ownerIndex: 3, flareMode);
        flare.FiredBy(ProjectileRig.Player, new Vector(TFProjectileDefs.FlareSpeed, 0f, 0f));
        return (rig, flare);
    }

    [Fact]
    public void The_touch_takes_the_shot_the_hooks_answered()
    {
        // FireFlare arms the projectile with GetProjectileDamage
        // (tf_weaponbase_gun.cpp:758), whose first arm is the item's mult_dmg
        // (:909-910): the Detonator's 0.75 damage penalty lands 22.5 of the
        // script's 30, and the touch is worth the hooked number.
        var rig = new ProjectileRig();
        var flare = rig.Flare();
        rig.Directory.Instance = flare;
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);

        rig.Effects.Projectile(Attack(3), FlareScript(), new ShotDamage(22.5f), 0f);

        flare.OnTouch(ProjectileRig.Victim);

        Assert.Equal(22.5f, SeenDamage(rig), 0.01f);
    }

    [Fact]
    public void The_flares_touch_is_the_flare_guns_igniting_shot()
    {
        var (rig, flare) = FiredFlare(burning: false, critShot: false);

        // The scope's shape as the damage lands, read inside the call.
        (TFDamageCustom, TFWeaponId)? seenScope = null;
        rig.Engine.WhenCalled("Damage.TakeDamage", call =>
        {
            rig.Scopes.TryCurrent(out var custom, out var weapon, out _);
            seenScope = (custom, weapon);
        });

        flare.OnTouch(ProjectileRig.Victim);

        // The full shot, the flare gun's own row: a bullet that ignites
        // (g_aWeaponDamageTypes, TF_DMG_CUSTOM_BURNING_FLARE, :346).
        Assert.Equal(30f, SeenDamage(rig), 1f);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Flaregun), SeenBits(rig));
        Assert.True(SeenBits(rig).HasFlag(TFDamageBits.Ignite));
        Assert.Equal((TFDamageCustom.BurningFlare, TFWeaponId.Flaregun), seenScope);

        // A player hit is the end (the else's UTIL_REMOVE, :370-376).
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Theory]
    [InlineData(true, false, true)] // the burning victim's hit crits
    [InlineData(false, false, false)]
    [InlineData(false, true, true)] // the shot's own crit stands
    [InlineData(true, true, true)]
    public void The_burning_victims_hit_crits(bool burning, bool critShot, bool expected)
    {
        var (rig, flare) = FiredFlare(burning, critShot);

        // Read as the damage lands: stock sets the flag before
        // CTakeDamageInfo, so the hit itself carries it (:340-347).
        bool? seen = null;
        rig.Engine.WhenCalled("Damage.TakeDamage", _ => seen = rig.HitGroups.Critical);

        flare.OnTouch(ProjectileRig.Victim);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public void The_world_hit_sticks_for_a_tenth_then_goes()
    {
        var (rig, flare) = FiredFlare(burning: false, critShot: false);

        flare.OnTouch(EntityHandle.None); // the wall

        // Sticks: no hurt, no removal yet (m_bImpact's arm, :350-359).
        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamage"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));

        flare.OnThink(); // ImpactThink's expiry

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_detonators_world_hit_blasts_instead_of_sticking()
    {
        var (rig, flare) = FiredFlare(burning: false, critShot: false, TFProjectileBehaviour.FlareDetonate);
        rig.Engine.FillsSphere();

        flare.OnTouch(EntityHandle.None); // the wall

        // Detonate's own removal (:362-366 through :436-447): gone at once,
        // the bare radius leaving the main sweep to the self pass alone.
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void The_scorch_shots_world_hit_blasts_at_the_full_radius()
    {
        var (rig, flare) = FiredFlare(burning: false, critShot: false, TFProjectileBehaviour.FlareScorch);
        rig.Engine.FillsSphere();

        flare.OnTouch(EntityHandle.None); // the wall

        // GetRadius's base, TF_FLARE_DET_RADIUS (tf_weaponbase_rocket.h:27):
        // the main sweep's ask, the self pass's own 100 beside it.
        var sweep = rig.Engine.Calls.First(c => c.Member == "WorldQuery.EntitiesInSphere");
        Assert.Equal(TFProjectileDefs.FlareDetRadius, EngineCallFacts.ArgAs<float>(sweep, 1), 3);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Theory]
    [InlineData(TFProjectileBehaviour.FlareDetonate)]
    [InlineData(TFProjectileBehaviour.FlareGrordbort)]
    [InlineData(TFProjectileBehaviour.FlareScorch)]
    public void The_detonating_modes_hit_spares_the_burning_crit(int flareMode)
    {
        // :341's !bDetonate && !bNoRandomCrit — every mode past the plain
        // one, the shot's own crit the only one left (the Scorch's hit is
        // its own arm, the flag there the shot's).
        var (rig, flare) = FiredFlare(burning: true, critShot: false, flareMode);

        bool? seen = null;
        rig.Engine.WhenCalled("Damage.TakeDamage", _ => seen = rig.HitGroups.Critical);

        flare.OnTouch(ProjectileRig.Victim);

        Assert.NotNull(seen);
        Assert.False(seen!.Value);
    }

    [Fact]
    public void A_detonators_shot_enrols_in_the_owners_tray()
    {
        // FireFlare's list (tf_weaponbase_gun.cpp:760-763): the mode is the
        // gate, so the Detonator's shot is the alt-fire's fuel.
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem(TFProjectileBehaviour.FlareModeAttribute,
                                            AttributeDescriptionFormat.Additive, 1f);
        rig.Directory.Instance = rig.Flare();

        rig.Effects.Projectile(Attack(3), FlareScript(), default, 0f);

        rig.Engine.FillsSphere();
        rig.FlareTray.DetonateAll(3);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));

        rig.FlareTray.DetonateAll(3); // the entry left with the flare

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void A_plain_shots_stays_out_of_the_tray()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = rig.Flare();

        rig.Effects.Projectile(Attack(3), FlareScript(), default, 0f);

        rig.FlareTray.DetonateAll(3);

        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }

    /// <summary>A tray entry under <paramref name="owner"/>, armed as the fire arms.</summary>
    private static TFFlare Enrolled(ProjectileRig rig, int owner)
    {
        var flare = rig.Flare();
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false,
                      TFProjectileDefs.FlareGravity, ownerIndex: owner,
                      flareMode: TFProjectileBehaviour.FlareDetonate);
        rig.FlareTray.Register(owner, flare);
        return flare;
    }

    [Fact]
    public void The_alt_fire_takes_the_owners_flares_and_leaves_the_others()
    {
        // SecondaryAttack's walk (tf_weapon_flaregun.cpp:160-171) is the
        // firing player's list; another pyro's flares are not in it.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere();
        Enrolled(rig, 3);
        Enrolled(rig, 4);

        rig.Effects.Detonate(Attack(3), FlareScript());

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));

        rig.Effects.Detonate(Attack(4), FlareScript());

        Assert.Equal(2, rig.Engine.CountOf("Entities.Remove"));
    }

    // -------------------------------------------------------------------------
    // The Scorch Shot's player hit (Explode, tf_projectile_flare.cpp:228-296).
    // -------------------------------------------------------------------------

    [Fact]
    public void A_scorch_shots_hit_is_the_penalty_the_item_names()
    {
        // FireFlare arms the touch with GetProjectileDamage
        // (tf_weaponbase_gun.cpp:758, mult_dmg at :909-910): the Scorch
        // Shot's 0.65 damage penalty (item 740) puts the script's 30 at 19.5,
        // and the arm's own TakeDamage is the hit's whole worth (:246-247) —
        // no blast beside it until the landing (:295's return).
        var rig = new ProjectileRig();
        var flare = rig.Flare();
        rig.Directory.Instance = flare;
        rig.Owners.Item = rig.AttributeItem(TFProjectileBehaviour.FlareModeAttribute,
                                            AttributeDescriptionFormat.Additive,
                                            TFProjectileBehaviour.FlareScorch);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);

        rig.Effects.Projectile(Attack(3), FlareScript(), new ShotDamage(19.5f), 0f);

        flare.OnTouch(ProjectileRig.Victim);

        Assert.Equal(19.5f, SeenDamage(rig), 0.01f);
        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));
    }

    [Fact]
    public void The_scorch_shots_landing_blasts_the_full_radius_at_the_victim_again()
    {
        // The hit's return (:295) leaves the touch armed, so the bounced
        // debris's world contact re-enters Explode past the arm (:300-313)
        // and m_bImpact's Detonate runs with bSelfOnly false for the Scorch
        // (:362-366) — the full radius (:411, HalfFalloff :419): the victim
        // the hit shoved takes the landing too, stock's double.
        var (rig, flare) = FiredFlare(burning: false, critShot: false, TFProjectileBehaviour.FlareScorch);
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", default(Vector));

        flare.OnTouch(ProjectileRig.Victim);

        // The hit itself is no blast: the arm's damage alone.
        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));
        Assert.Equal(0, rig.Engine.CountOf("WorldQuery.EntitiesInSphere"));

        flare.OnTouch(EntityHandle.None); // the landing

        var sweep = rig.Engine.Calls.First(c => c.Member == "WorldQuery.EntitiesInSphere");
        Assert.Equal(TFProjectileDefs.FlareDetRadius, EngineCallFacts.ArgAs<float>(sweep, 1), 3);

        // The hit's void TakeDamage, the landing's answered one (the sweep's
        // Deliver): the victim the shove threw takes the second.
        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));
        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Theory]
    [InlineData(true, 400f)] // the burning victim's shove
    [InlineData(false, 100f)]
    public void The_scorch_shot_shoves_its_victim_and_bounces(bool burning, float force)
    {
        var (rig, flare) = FiredFlare(burning, critShot: false, TFProjectileBehaviour.FlareScorch);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(2000f, 0f, 0f));
        // Attacker and victim on their own sides: the shove is an enemy's.
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim ? 3 : 2);

        flare.OnTouch(ProjectileRig.Victim);

        // The shot without its push force (:243): the shove below is all.
        var hurt = rig.Engine.Last("Damage.TakeDamage");
        Assert.NotNull(hurt);
        Assert.True(EngineCallFacts.ArgAs<DamageType>(hurt!, 3).HasFlag(DamageType.PreventPhysicsForce));

        // Along the flight's line, z pinned to 1 (:253-256), 400 burning.
        var push = rig.Engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(push);
        Assert.Equal(ProjectileRig.Victim, push!.ArgAs<EntityHandle>(0));
        var along = new Vector(2000f, 0f, 0f).Normalized;
        Assert.Equal(new Vector(along.X, along.Y, 1f) * force, push.ArgAs<Vector>(1));

        // Debris, and the pop: a seventh of the speed, 100 up, the dial's
        // mid jitter of zero (:234, :268-272) — alive, gone nowhere.
        var group = rig.Engine.Last("EntityPhysics.SetCollisionGroup");
        Assert.NotNull(group);
        Assert.Equal(CollisionGroup.Debris, group!.ArgAs<CollisionGroup>(1));
        var bounce = rig.Engine.Last("Entities.SetVelocity");
        Assert.NotNull(bounce);
        Assert.Equal(new Vector(140f, 0f, 100f), bounce!.ArgAs<Vector>(1));
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void A_teammate_in_the_scorch_shot_s_way_is_not_shoved()
    {
        var rig = new ProjectileRig();
        rig.Conditions.EveryoneBurns = true;
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetVelocity", new Vector(2000f, 0f, 0f));
        var flare = rig.Flare();
        flare.OnSpawn();
        flare.ArmedBy(30f, TFWeaponId.Flaregun, "tf_weapon_flaregun", critical: false,
                      TFProjectileDefs.FlareGravity, ownerIndex: 3, TFProjectileBehaviour.FlareScorch);
        flare.FiredBy(ProjectileRig.Player, new Vector(TFProjectileDefs.FlareSpeed, 0f, 0f));
        rig.Engine.Returns("Players.GetTeam", 2); // the same side as the shooter's

        flare.OnTouch(ProjectileRig.Victim);

        Assert.Equal(0, rig.Engine.CountOf("Entities.ApplyVelocityImpulse"));
        // The bounce itself is not the shove's to give: still debris, still gone up.
        var group = rig.Engine.Last("EntityPhysics.SetCollisionGroup");
        Assert.NotNull(group);
    }

    // -------------------------------------------------------------------------
    // The frame tail (ItemPostFrame, tf_weapon_flaregun.cpp:147-173 through the
    // behaviour shell): the Detonator's trigger is the detonation ask.
    // -------------------------------------------------------------------------

    private const int Attack2 = (int)InputButtons.Attack2;
    private const float Tick = 0.015f;

    /// <summary>The launcher item as the schema carries one float: the gun's mode.</summary>
    private static IAttributeProvider ModeItem(int mode)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(961, TFProjectileBehaviour.FlareModeAttribute, TFProjectileBehaviour.FlareModeAttribute,
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), mode));
        return container;
    }

    [Fact]
    public void The_detonators_trigger_asks_the_detonation()
    {
        var world = new TFWeaponWorld(extras: true);
        world.Owners.Items[TFWeaponWorld.Owner] = ModeItem(TFProjectileBehaviour.FlareDetonate);
        var (w, vm) = world.Armed("tf_weapon_flaregun", clip: 1, reserve: 16);

        world.Run(w, vm, Attack2, 10f);

        Assert.Equal(1, world.Effects.Detonations);
    }

    [Theory]
    [InlineData(TFProjectileBehaviour.FlareNormal)]
    [InlineData(TFProjectileBehaviour.FlareGrordbort)]
    [InlineData(TFProjectileBehaviour.FlareScorch)]
    public void The_other_guns_trigger_asks_nothing(int flareMode)
    {
        // SecondaryAttack's own gate (:149): FLAREGUN_DETONATE alone.
        var world = new TFWeaponWorld(extras: true);
        world.Owners.Items[TFWeaponWorld.Owner] = ModeItem(flareMode);
        var (w, vm) = world.Armed("tf_weapon_flaregun", clip: 1, reserve: 16);

        world.Run(w, vm, Attack2, 10f);

        Assert.Equal(0, world.Effects.Detonations);
    }
}
