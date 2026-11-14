using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Tests.Projectiles;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The energy family's behaviour (<see cref="TFEnergyBehaviour"/>, the stock
/// <c>CTFRaygun</c> family): the press that spends one round and launches the
/// ring, the penetrating item's half hit, the no-drain item's free shot, the
/// whole-shot tank and its upgrade, and the idle frames that top the meter
/// back up — the pomson rides the ring arms, the mangler's charge its own
/// facts below (<c>tf_weapon_raygun.cpp</c>, the citations there).
/// </summary>
public sealed class TFEnergyBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Owner = TFWeaponWorld.Owner;

    /// <summary>A seed whose gun roll draws no crit: the ring's hit then is its damage alone.</summary>
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    private static int _nextDef = 5700;

    /// <summary>An item holding one attribute under the named hook class.</summary>
    private static IAttributeProvider ItemWith(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>A world with the extras fixtures and one armed bison.</summary>
    private static TFWeaponWorld Armed(out WeaponNetState w, out ViewModelNetState vm,
                                        int clip = 4, int reserve = 0, string weapon = "tf_weapon_raygun")
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        (w, vm) = world.Armed(weapon, clip: clip, reserve: reserve);
        return world;
    }

    [Fact]
    public void The_press_spends_one_round_and_launches_the_ring()
    {
        var world = Armed(out var w, out var vm, clip: 4);

        world.Run(w, vm, Attack, 10f, NoCrit);

        // One ring at the script's delay, its hit the single-hit sixty the
        // plain press rolls (GetDamage, tf_weapon_raygun.cpp:359-370); the
        // round spent, the next shot half a second off (fixture TimeFireDelay),
        // the single sound and the flash the script names.
        var shot = Assert.Single(world.Effects.Projectiles);
        Assert.Equal("tf_weapon_raygun", shot.Weapon);
        Assert.Equal(60f, shot.Shot.Damage, 3);
        Assert.False(shot.Shot.Critical);
        Assert.Equal(0f, shot.Charge, 3);
        Assert.Equal(3, w.Clip);
        Assert.Equal(10.5f, w.NextPrimaryAttack, 3);
        Assert.Equal(10.5f, w.TimeWeaponIdle, 3);
        Assert.Single(world.Arms.Sounds, "Weapon_Bison.Single");
        Assert.Single(world.Arms.Flashes, ("tf_weapon_raygun", "muzzle_flare"));

        // The behaviour launches through the effects itself; the machine's own
        // launch arm ran none of it.
        Assert.Empty(world.Arms.Launches);
    }

    [Fact]
    public void The_penetrating_item_halves_the_ring_hit()
    {
        var world = Armed(out var w, out var vm);
        world.Owners.Items[Owner] = ItemWith("energy_weapon_penetration", 1f);

        world.Run(w, vm, Attack, 10f, NoCrit);

        // ShouldPenetrate answers through CALL_ATTRIB_HOOK_INT_ON_OTHER
        // (tf_projectile_energy_ring.cpp:367): the penetrating twenty.
        var shot = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(20f, shot.Shot.Damage, 3);
    }

    [Fact]
    public void A_no_drain_item_spends_nothing_but_still_fires()
    {
        var world = Armed(out var w, out var vm, clip: 2);
        world.Owners.Items[Owner] = ItemWith("energy_weapon_no_drain", 1f);

        world.Run(w, vm, Attack, 10f, NoCrit);

        // The shot cost goes to nothing (tf_weapon_raygun.h:52-63's
        // energy_weapon_no_drain): the ring still leaves, the tank stands.
        Assert.Single(world.Effects.Projectiles);
        Assert.Equal(2, w.Clip);
    }

    [Fact]
    public void The_tank_holds_four_whole_shots()
    {
        var world = Armed(out var w, out var vm);

        // ENERGY_WEAPON_MAX_CHARGE over the shot's five: four rounds through
        // the itemless hook (Energy_GetMaxEnergy, tf_weaponbase.cpp:6357-6367).
        var behaviour = (TFEnergyBehaviour)world.Behaviour("tf_weapon_raygun");
        Assert.Equal(4, behaviour.ModifyClipSize(world.Row("tf_weapon_raygun"), 0, Owner));
    }

    [Fact]
    public void The_clip_size_upgrade_doubles_the_tank()
    {
        var world = Armed(out var w, out var vm);
        world.Owners.Items[Owner] = ItemWith("mult_clipsize_upgrade", 1f);

        // The scaled count is stock's own cast into the int member (:6357-6367
        // reads mult_clipsize_upgrade over the shot count): four rounds times
        // two.
        var behaviour = (TFEnergyBehaviour)world.Behaviour("tf_weapon_raygun");
        Assert.Equal(8, behaviour.ModifyClipSize(world.Row("tf_weapon_raygun"), 0, Owner));
    }

    [Fact]
    public void The_idle_frames_top_the_meter_back_up_one_round_at_a_time()
    {
        var world = Armed(out var w, out var vm, clip: 2, reserve: 5);
        world.Arms.ReloadSeconds = 0.5f;

        world.Run(w, vm, Attack, 10f, NoCrit);
        Assert.Equal(1, w.Clip);

        // The first idle tick arms the recharge clock for the reload anim's
        // half second (Energy_Recharge's tick, tf_weaponbase.cpp:2043-2074);
        // nothing spends and nothing tops up until it runs out.
        world.Run(w, vm, 0, 10.6f, NoCrit);
        Assert.Equal(1, w.Clip);
        Assert.Equal(5, w.Reserve);

        // Past the clock one whole round rides back onto the meter, and the
        // reserve pays for it round for round.
        world.Run(w, vm, 0, 11.15f, NoCrit);
        Assert.Equal(2, w.Clip);
        Assert.Equal(4, w.Reserve);
    }

    [Fact]
    public void The_no_ammo_item_tops_up_without_the_reserve()
    {
        var world = Armed(out var w, out var vm, clip: 2, reserve: 5);
        world.Owners.Items[Owner] = ItemWith("energy_weapon_no_ammo", 1f);
        world.Arms.ReloadSeconds = 0.5f;

        world.Run(w, vm, Attack, 10f, NoCrit);
        world.Run(w, vm, 0, 10.6f, NoCrit);
        world.Run(w, vm, 0, 11.15f, NoCrit);

        // energy_weapon_no_ammo: the top-up draws on nothing — the meter still
        // refills, the reserve never pays.
        Assert.Equal(2, w.Clip);
        Assert.Equal(5, w.Reserve);
    }

    /// <summary>A ring over the projectile rig, armed for the pomson's shot. The
    /// telling's reads stand answered: the owner red, the victim's centre the origin.</summary>
    private static ProjectileRig PomsonRing(out TFProjectileEnergyRing ring, string hook = "", float amount = 0f)
    {
        var rig = new ProjectileRig();
        if (hook.Length > 0)
            rig.Owners.Item = rig.AttributeItem(hook, AttributeDescriptionFormat.Additive, amount);

        rig.Engine.Returns("Entities.GetWorldSpaceCenter", default(Vector));
        rig.Engine.Returns("Players.GetHandle", ProjectileRig.Player);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 9);
        ring = rig.Ring();
        ring.ArmedBy(60f, TFWeaponId.DrgPomson, "tf_weapon_drg_pomson", false, Owner);
        return rig;
    }

    /// <summary>The bison's ring, its owner's item piercing, inside a body at the origin.</summary>
    private static ProjectileRig BisonRing(out TFProjectileEnergyRing ring, Vector mins, Vector maxs)
    {
        var rig = new ProjectileRig();
        rig.Owners.Item = rig.AttributeItem("energy_weapon_penetration", AttributeDescriptionFormat.Additive, 1f);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.Returns("Entities.GetBounds", (mins, maxs));
        var engine = rig.Engine;
        engine.Returns("Clock.Now", 10f);
        engine.ReturnsEachCall("Players.GetTeam",
            () => engine.Calls[^1].ArgAs<EntityHandle>(0) == ProjectileRig.Victim ? 3 : 2);
        ring = rig.Ring();
        ring.Handle = ProjectileRig.ProjectileHandle;
        ring.ArmedBy(20f, TFWeaponId.Raygun, "tf_weapon_raygun", false, Owner);
        ring.FiredBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f));
        return rig;
    }

    private static readonly Vector AroundMins = new(-24f, -24f, -24f);
    private static readonly Vector AroundMaxs = new(24f, 24f, 24f);

    [Fact]
    public void The_piercing_ring_hits_and_flies_on()
    {
        var rig = BisonRing(out var ring, AroundMins, AroundMaxs);

        ring.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));
        Assert.Equal(20f, rig.Engine.Last("Damage.TakeDamage")!.ArgAs<float>(2));
        Assert.True(rig.Motion.PassesCharacters(ring.Handle));
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void A_ring_without_the_item_stops_on_the_first_body()
    {
        var rig = PomsonRing(out var ring);
        rig.Engine.Returns("Entities.IsPlayer", true);
        ring.Handle = ProjectileRig.ProjectileHandle;
        ring.FiredBy(ProjectileRig.Player, new Vector(1200f, 0f, 0f));

        ring.OnTouch(ProjectileRig.Victim);

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));
        Assert.False(rig.Motion.PassesCharacters(ring.Handle));
    }

    [Fact]
    public void The_piercing_ring_hits_again_only_past_the_tick_time()
    {
        var rig = BisonRing(out var ring, AroundMins, AroundMaxs);
        ring.OnTouch(ProjectileRig.Victim);

        // One tick on (tf_bison_tick_time 0.025, one server tick 0.015): held off.
        rig.Engine.Returns("Clock.Now", 10.015f);
        ring.OnThink();
        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));

        // Two ticks on: stock's Touch lands again.
        rig.Engine.Returns("Clock.Now", 10.03f);
        ring.OnThink();
        Assert.Equal(2, rig.Engine.CountOf("Damage.TakeDamage"));
    }

    [Fact]
    public void The_piercing_ring_is_done_with_a_body_once_outside_it()
    {
        var rig = BisonRing(out var ring, new Vector(100f, 100f, 100f), new Vector(148f, 148f, 148f));
        ring.OnTouch(ProjectileRig.Victim);
        Assert.True(rig.Motion.PassesCharacters(ring.Handle));

        rig.Engine.Returns("Clock.Now", 10.03f);
        ring.OnThink();

        Assert.Equal(1, rig.Engine.CountOf("Damage.TakeDamage"));
        Assert.False(rig.Motion.PassesCharacters(ring.Handle));
    }

    [Fact]
    public void A_ring_stopped_on_the_hulls_face_still_counts_as_inside()
    {
        // The sweep leaves the box touching the face, its origin a unit outside
        // the bounds. Called out there, the next sweep hits the face again and
        // the ring stays put, hitting until the body dies.
        var rig = BisonRing(out var ring, new Vector(1f, -24f, -24f), new Vector(49f, 24f, 24f));
        ring.OnTouch(ProjectileRig.Victim);

        rig.Engine.Returns("Clock.Now", 10.015f);
        ring.OnThink();

        Assert.True(rig.Motion.PassesCharacters(ring.Handle));
    }

    [Fact]
    public void The_piercing_ring_flies_through_a_teammate_unhurt()
    {
        var rig = BisonRing(out var ring, AroundMins, AroundMaxs);
        var engine = rig.Engine;
        engine.ReturnsEachCall("Players.GetTeam", () => 2);

        ring.OnTouch(ProjectileRig.Victim);
        rig.Engine.Returns("Clock.Now", 10.03f);
        ring.OnThink();

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamage"));
        Assert.True(rig.Motion.PassesCharacters(ring.Handle));
    }

    [Fact]
    public void The_ring_hit_drains_the_medic_meter_by_the_item_amount()
    {
        var rig = PomsonRing(out var ring, "subtract_victim_medigun_charge_onhit", 40f);

        ring.OnTouch(ProjectileRig.Victim);

        // ApplyPostHitEffects (:5569, :5582): the attribute's forty hundredths
        // of the victim'sÜberCharge meter, at full weight up close.
        var drain = Assert.Single(rig.Owners.Drains);
        Assert.Equal(1, drain.Player);
        Assert.Equal(0.4f, drain.Medic, 3);
        Assert.Equal(0f, drain.Spy, 3);
    }

    [Fact]
    public void The_ring_hit_drains_the_spy_meter_by_the_item_points()
    {
        var rig = PomsonRing(out var ring, "subtract_victim_cloak_on_hit", 35f);

        ring.OnTouch(ProjectileRig.Victim);

        // Stock's cloak arm subtracts the raw attribute from the hundred-point
        // meter (:5603-5610) — thirty-five points is the fraction of one.
        var drain = Assert.Single(rig.Owners.Drains);
        Assert.Equal(0f, drain.Medic, 3);
        Assert.Equal(0.35f, drain.Spy, 3);
    }

    [Fact]
    public void The_drain_fades_with_the_distance_between_the_two()
    {
        var rig = PomsonRing(out var ring, "subtract_victim_medigun_charge_onhit", 40f);

        // Stock's scale (:5577-5579) walks the squared eye-to-victim from 512
        // to 1536 Hu; at 1024 Hu it answers 0.625, so the forty hundredths
        // lands as a quarter. Two answers in the rig: victim out, shooter in.
        int[] xs = [1024, 0];
        int call = 0;
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () => new Vector(xs[call++ % 2], 0f, 0f));

        ring.OnTouch(ProjectileRig.Victim);

        var drain = Assert.Single(rig.Owners.Drains);
        Assert.Equal(0.25f, drain.Medic, 3);
    }

    [Fact]
    public void The_drain_is_gone_by_fifteen_hundred_sixteen_hu()
    {
        var rig = PomsonRing(out var ring, "subtract_victim_medigun_charge_onhit", 40f);

        // Past 1536 Hu the remap clamps at zero (:5579) — the drain stands
        // nowhere: stock's comment's far end of the scale.
        int[] xs = [2000, 0];
        int call = 0;
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () => new Vector(xs[call++ % 2], 0f, 0f));

        ring.OnTouch(ProjectileRig.Victim);

        var drain = Assert.Single(rig.Owners.Drains);
        Assert.Equal(0f, drain.Medic, 3);
    }

    [Fact]
    public void An_item_without_the_attributes_drains_nothing()
    {
        var rig = PomsonRing(out var ring);

        ring.OnTouch(ProjectileRig.Victim);

        // The arms only run on a positive attribute (:5569, :5593): no item,
        // no drain call at all.
        Assert.Empty(rig.Owners.Drains);
    }

    [Fact]
    public void A_draining_hit_tells_its_particle_and_sound()
    {
        var rig = PomsonRing(out var ring, "subtract_victim_medigun_charge_onhit", 40f);
        rig.Engine.Returns("Players.GetTeam", (int)TFTeam.Red);

        ring.OnTouch(ProjectileRig.Victim);

        // The telling (ApplyPostHitEffects' tail, tf_weaponbase.cpp:5619-5635):
        // the drain particle over the victim in the owner's team's colours,
        // the sound on the victim.
        var sent = Assert.Single(rig.Temps.Effects);
        Assert.Equal("ParticleEffect", sent.Name);
        Assert.True(sent.Data.CustomColors);
        Assert.Equal(TFProjectileDefs.ParticleWeaponRed1, sent.Data.Color1);
        Assert.Equal(TFProjectileDefs.ParticleWeaponRed2, sent.Data.Color2);
        Assert.Equal(sent.Data.Origin, sent.Data.Start);
        var sound = rig.Engine.Last("Sound.EntityEmitSound")!;
        Assert.Equal(ProjectileRig.Victim, sound.ArgAs<EntityHandle>(0));
        Assert.Equal(TFProjectileDefs.EnergyDrainSound, sound.ArgAs<string>(1));
    }

    [Fact]
    public void A_hidden_victim_hears_no_drain()
    {
        var rig = PomsonRing(out var ring, "subtract_victim_cloak_on_hit", 35f);
        rig.Owners.Conditions = new TFConditions(true);
        rig.Owners.Conditions.Add(TFCond.Stealthed);

        ring.OnTouch(ProjectileRig.Victim);

        // The cloak eats the telling, not the drain (:5616-5617).
        Assert.Single(rig.Owners.Drains);
        Assert.Empty(rig.Temps.Effects);
    }

    [Fact]
    public void A_meter_that_stood_nowhere_tells_nothing()
    {
        var rig = PomsonRing(out var ring, "subtract_victim_medigun_charge_onhit", 40f);
        rig.Owners.MeterStood = false;

        ring.OnTouch(ProjectileRig.Victim);

        // No meter moved, no bDidDrain (:5555, :5619): the record stands, the
        // telling does not.
        Assert.Single(rig.Owners.Drains);
        Assert.Empty(rig.Temps.Effects);
        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    private const int Attack2 = (int)InputButtons.Attack2;

    /// <summary>The held charge press: ATTACK holds the aim, ATTACK2 begins the charge
    /// (the shape stock's ItemPostFrame reads — tf_weapon_particle_cannon.cpp:149-165).</summary>
    private const int Hold = (int)(InputButtons.Attack | InputButtons.Attack2);

    /// <summary>A world with a full mangler tank — the charge's fully-charged door.</summary>
    private static TFWeaponWorld Mangler(out WeaponNetState w, out ViewModelNetState vm)
    {
        var world = Armed(out w, out vm, clip: 4, weapon: "tf_weapon_particle_cannon");
        world.Owners.Of(Owner);
        return world;
    }

    [Fact]
    public void The_secondary_press_begins_the_charge_and_holds_the_aim()
    {
        var world = Mangler(out var w, out var vm);

        world.Run(w, vm, Hold, 10f, NoCrit);

        // SecondaryAttack's stamp (:190-197): the meter, the aim cond, and a
        // held trigger spending nothing yet. The ATTACK bit holds the charge
        // open through the tail's release check; the secondary bit on the
        // same press begins it.
        Assert.Equal(10f, w.BehaviourMeter, 4);
        Assert.True(world.Owners.Of(Owner).Has(TFCond.Aiming));
        Assert.Equal(4, w.Clip);
        Assert.Empty(world.Effects.Projectiles);
    }

    [Fact]
    public void Letting_go_of_the_button_does_not_fire_the_charge()
    {
        // Stock has no release (:153-165): right-click alone, every tick up.
        var world = Mangler(out var w, out var vm);
        world.Run(w, vm, Hold, 10f, NoCrit);

        for (float t = 10.015f; t < 11.9f; t += 0.015f)
            world.Run(w, vm, 0, t, NoCrit);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(4, w.Clip);
        Assert.Equal(10f, w.BehaviourMeter, 4);
    }

    [Fact]
    public void The_charge_fires_itself_at_two_seconds_with_the_whole_tank()
    {
        var world = Mangler(out var w, out var vm);
        world.Run(w, vm, Hold, 10f, NoCrit);

        world.Run(w, vm, 0, 12.0f, NoCrit);

        var shot = Assert.Single(world.Effects.Projectiles);
        Assert.Equal("tf_weapon_particle_cannon", shot.Weapon);
        Assert.Equal(2f, shot.Charge, 3);
        Assert.Equal(0, w.Clip);
        Assert.Equal(0f, w.BehaviourMeter, 4);
        Assert.False(world.Owners.Of(Owner).Has(TFCond.Aiming));
        Assert.Equal(12.5f, w.NextPrimaryAttack, 3);
        Assert.DoesNotContain("Weapon_Mangler.Single", world.Arms.Sounds);
        Assert.Single(world.Arms.Flashes, ("tf_weapon_particle_cannon", "muzzle_flare"));
    }

    [Fact]
    public void Holding_on_past_the_cap_adds_no_second_shot()
    {
        var world = Mangler(out var w, out var vm);
        world.Run(w, vm, Hold, 10f, NoCrit);

        world.Run(w, vm, Attack, 12.1f, NoCrit);

        var shot = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(2f, shot.Charge, 3);
        Assert.Equal(0, w.Clip);
    }

    [Fact]
    public void The_plain_press_while_charging_fires_nothing()
    {
        var world = Mangler(out var w, out var vm);
        world.Run(w, vm, Hold, 10f, NoCrit);

        // BeforePrimaryAttack's charging door (:124's early return while
        // m_flChargeBeginTime runs): the held ATTACK bit stands quiet until
        // the release, and the held charge stays open through the tail.
        world.Run(w, vm, Attack, 10.5f, NoCrit);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(4, w.Clip);
        Assert.Equal(10f, w.BehaviourMeter, 4);
    }
}
