using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Server;
using TF2.Server.Combat;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Projectiles;
using TF2.Tests.Combat;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Gas Passer (<c>CTFJarGas</c>, <c>tf_weapon_jar_gas.cpp</c>), the gas
/// face of the jars (<see cref="TFJarFacts"/>): the throw under its own
/// projectile name, and the splash that gasses the enemy and lights them at
/// the item's afterburn rate — <see cref="TFBurn.AfterburnRateOnHit"/>'s ten.
/// Over the shipped script's gas row.
/// </summary>
public sealed class TFGasPasserFacts
{
    private const int Attack = (int)InputButtons.Attack;

    private static TFWeaponScript GasScript()
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get("tf_weapon_jar_gas")
            ?? throw new InvalidOperationException("no script tf_weapon_jar_gas");
    }

    private static WeaponAttackContext Throw(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    [Fact]
    public void The_gas_passer_wires_the_projectile_row()
    {
        Assert.Equal(TFWeaponKind.Projectile, TFWeaponKinds.Of(GasScript()));

        var world = new TFWeaponWorld(extras: true);
        Assert.Equal(TFWeaponKinds.Key(TFWeaponKind.Projectile), world.Row("tf_weapon_jar_gas").Behaviour);
        Assert.NotNull(world.Behaviour("tf_weapon_jar_gas"));
    }

    [Fact]
    public void The_gas_passer_throws_under_its_own_name()
    {
        var rig = new ProjectileRig();
        rig.Directory.Instance = null; // the mapping is what the fact holds

        rig.Effects.Projectile(Throw(3), GasScript(), default, 0f);

        Assert.Equal(TFProjectileEntityNames.JarGas, rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
    }

    [Fact]
    public void The_gas_jar_leaves_under_its_own_name_with_the_upward_lob()
    {
        // GetVelocityVector (tf_weapon_jar.cpp:153-157): the jar at its
        // launch speed along the aim, +200 up beneath it, the two ±10 draws
        // cancelling at the stream's midpoint — the gas jar shares the plain
        // jar's arm and its launch numbers.
        var rig = new ProjectileRig();
        rig.Directory.Instance = rig.Jar();

        rig.Effects.Projectile(Throw(3), GasScript(), default, 0f);

        // The gas row's own name, beside the lob the launch carries: the
        // literals are stock's own numbers (TF_JAR_LAUNCH_SPEED 1000, the
        // +200 up), so the drift of a constant cannot hide behind itself.
        Assert.Equal(TFProjectileEntityNames.JarGas, rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(1000f, velocity.X, 0.5f);
        Assert.Equal(0f, velocity.Y, 0.5f);
        Assert.Equal(200f, velocity.Z, 0.5f);
    }

    [Fact]
    public void The_gas_splash_wets_the_enemy_with_gas_and_the_ten_second_burn()
    {
        var rig = new ProjectileRig();
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
        var jar = rig.Jar();
        jar.ClassName = TFProjectileEntityNames.JarGas;
        jar.OnSpawn();
        jar.ArmedBy(critical: false, ownerIndex: 3);
        jar.FiredBy(ProjectileRig.Player, new Vector(TFProjectileDefs.JarSpeed, 0f, 0f));

        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Players.IsAlive", true);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 64f));
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Player ? 3 : 2);

        jar.OnTouch(ProjectileRig.Victim);

        // The enemy wears the gas from the thrower and the burning the
        // item's afterburn rate sets (TF_GAS_AFTERBURN_RATE,
        // tf_weapon_jar_gas.h:30): two additions, the gas first.
        Assert.Collection(rig.Wet.Added,
            first =>
            {
                Assert.Equal(TFCond.Gas, first.Cond);
                Assert.Equal(TFProjectileDefs.JarDuration, first.Duration);
            },
            second =>
            {
                Assert.Equal(TFCond.Burning, second.Cond);
                Assert.Equal(10f, second.Duration, 2);
            });
    }

    /// <summary>
    /// The gas ignition (<c>CTFPlayer::OnTakeDamage</c>, tf_player.cpp:9869-9896):
    /// a gassed victim who takes damage is lit from the thrower's gas — the burn
    /// credited to the tosser, the gas gone behind it — and no ignite bit is
    /// asked for: a plain bullet lights them too.
    /// </summary>
    [Fact]
    public void A_hit_on_the_gassed_lights_them_and_the_gas_goes()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        rig.Conditions.Add(CombatRig.Victim, TFCond.Gas, 10f, CombatRig.Third);

        rig.Hit(13f, DamageType.Bullet);

        Assert.True(rig.VictimState.Conditions().Has(TFCond.Burning));
        Assert.False(rig.VictimState.Conditions().Has(TFCond.Gas));
        // The tosser burns them with their can, not the hitter's flamethrower (stock :9889-9895).
        Assert.Equal(CombatRig.Third, slice.Burn.Attacker);
        Assert.Equal(TFWeaponId.JarGas, slice.Burn.Weapon);
    }

    /// <summary>
    /// The gas burn's length (<c>m_Shared.Burn(..., tf_afterburn_max_duration)</c>,
    /// tf_player.cpp:9895, the constant's ten at tf_player_shared.cpp:188): a
    /// flame hit on a gassed victim rides the afterburn max, not the weapon's
    /// rate-on-hit — the flame's own 3.4 lands under the ten's clamp.
    /// </summary>
    [Fact]
    public void The_gas_burn_rides_the_afterburn_max_to_ten()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_flamethrower" };
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(CombatRig.Victim));
        slice.Attach(CombatRig.Victim);
        rig.Conditions.Add(CombatRig.Victim, TFCond.Gas, 10f, CombatRig.Attacker);

        rig.Hit(13f, DamageType.Burn | TFDamageBits.Ignite);

        Assert.Equal(10f, slice.Burn.AfterburnDuration, 4);      // 3.4 + 10 clamped at the max's ten
    }

    // ------------------------------------------------------------------
    // The can itself: the held meter (item 1180's shipped static_attrs —
    // charge type 3 COMBO, charge rate 60, damage for full charge 750, bin/
    // debug/content/tf/scripts/items/items_game.txt:63109-63120). The three
    // facts press through the machine — the held blob's head refill, the
    // press's veto and launch, the command tail's timed walk — over the
    // shipped-shaped harness item.
    // ------------------------------------------------------------------

    /// <summary>The command's own cadence, the hold facts'.</summary>
    private const float MeterStep = 0.015f;

    /// <summary>The held pyro world: the gas row's item naming its shipped COMBO meter.</summary>
    private static TFWeaponWorld HeldGasWorld()
    {
        var world = new TFWeaponWorld(extras: true);

        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9301, "attr 9301", TFItemMeter.ChargeRateAttribute,
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("gasmeter")),
            60f));
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9302, "attr 9302", TFItemMeter.ChargeTypeAttribute,
                                    AttributeDescriptionFormat.Or, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("gasmeter")),
            (float)TFItemMeter.ChargeTypeCombo));

        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Pyro;
        world.Owners.ByName[TFWeaponWorld.Owner] = new() { ["tf_weapon_jar_gas"] = item };
        return world;
    }

    /// <summary>The held gas: the purse stock's deploy leaves behind (clipless,
    /// one grenade), the meter empty as <c>item_meter_starts_empty</c> says
    /// (<c>GetDefaultItemChargeMeterValue</c>, tf_weapon_jar_gas.h:54).</summary>
    private static (TFWeaponWorld World, WeaponNetState Gun, ViewModelNetState ViewModel) HeldGas()
    {
        var world = HeldGasWorld();
        var (gun, viewModel) = world.Armed("tf_weapon_jar_gas", clip: -1, reserve: 1);
        return (world, gun, viewModel);
    }

    /// <summary>One command at the next step; returns the new clock.</summary>
    private static float Command(TFWeaponWorld world, WeaponNetState gun, ViewModelNetState vm,
                                 int buttons, float now)
    {
        float next = now + MeterStep;
        world.Run(gun, vm, buttons, next);
        return next;
    }

    /// <summary>Idle commands until the timed walk reaches full; false past the window's bounds.</summary>
    private static bool RefillHold(TFWeaponWorld world, WeaponNetState gun, ViewModelNetState vm, ref float now)
    {
        for (int k = 0; k < 4_400 && gun.ItemMeter < 1f; k++)
            now = Command(world, gun, vm, 0, now);
        return gun.ItemMeter >= 1f;
    }

    /// <summary>
    /// The empty refuse (stock's <c>CTFJarGas::CanAttack</c>,
    /// tf_weapon_jar_gas.cpp:278-290): the deploy leaves the bar at the
    /// starts-empty zero, so the first press answers nothing — and the
    /// sampled walks between presses show the bar climbing under the
    /// refusal, never reaching it. Stock's timed fill walks the COMBO type
    /// (:7882) over the item's sixty (<c>item_meter_charge_rate</c>, :7885).
    /// </summary>
    [Fact]
    public void AHeldGasPasserRefusesTheThrowWhileTheMeterHasNotReachedFull()
    {
        var (world, gun, vm) = HeldGas();
        float now = Command(world, gun, vm, Attack, 0f);       // the deploy's first command

        Assert.Empty(world.Effects.Projectiles);               // refused: the bar is short of full
        Assert.True(gun.ItemMeter > 0f && gun.ItemMeter < 1f); // sampled on the tail: filling, not yet full

        float before = gun.ItemMeter;
        now = Command(world, gun, vm, 0, now);
        Assert.True(gun.ItemMeter > before);                   // every idle command walks the bar
        Command(world, gun, vm, Attack, now);
        Assert.Empty(world.Effects.Projectiles);               // still refused on the partial bar
    }

    /// <summary>
    /// The full bar's throw and spend: once the sixty seconds' walk is past
    /// (stock's fill under the COMBO type, :7882), the press throws under the
    /// gas name and spends the bar to zero with the round — stock's
    /// <c>RemoveJarGas</c> setting the secondary meter back to zero beside the
    /// grenade (:261-262).
    /// </summary>
    [Fact]
    public void AFullyRefilledGasPasserThrowsAndSpendsItsMeter()
    {
        var (world, gun, vm) = HeldGas();
        gun.ItemMeter = 1f;                               // where fact A's walk leaves the bar

        world.Run(gun, vm, Attack, 10f);

        Assert.Equal("tf_weapon_jar_gas", Assert.Single(world.Effects.Projectiles).Weapon);
        Assert.Equal(0f, gun.ItemMeter, 2);               // spent to zero for the refill's next walk
        Assert.Equal(0, gun.Reserve);                     // the grenade behind it
        Assert.Empty(world.Arms.Bullets);                 // the jar throws, it does not shoot
    }

    /// <summary>
    /// The refill behind the spend: the COMBO's timed walk brings the bar back
    /// and the purse with it (stock's <c>OnResourceMeterFilled</c> granting
    /// the grenade back on the fill's crossing, :102-109), so a held can
    /// throws again after the second walk without a re-equip.
    /// </summary>
    [Fact]
    public void TheRefillHandsTheGasPasserBackForASecondThrow()
    {
        var (world, gun, vm) = HeldGas();

        // The COMBO walk at the shipped rate: one second per command, sixty
        // of them to the bar's full (stock's fill, tf_player_shared.cpp:7882).
        for (int k = 1; k <= 61 && gun.ItemMeter < 1f; k++)
            world.Run(gun, vm, buttons: 0, now: k);
        Assert.True(gun.ItemMeter >= 1f, "the first walk never reached full");

        world.Run(gun, vm, Attack, 100f);
        Assert.Single(world.Effects.Projectiles);

        for (int k = 62; k <= 123 && gun.ItemMeter < 1f; k++)
            world.Run(gun, vm, buttons: 0, now: k);
        Assert.True(gun.ItemMeter >= 1f, "the second walk never reached full");

        world.Run(gun, vm, Attack, 200f);

        Assert.Equal(2, world.Effects.Projectiles.Count);      // the refill walked the throw back
    }

    /// <summary>
    /// The gas jar networks under the jar's table, as the milk does: without
    /// the alias the throw's spawn found no layout for
    /// <c>tf_projectile_jar_gas</c>, was handed a 32-byte block and refused
    /// the bind (BlobTooShortException, live), so no gas jar ever flew.
    /// </summary>
    [Fact]
    public void The_gas_jar_networks_under_the_jars_table()
    {
        var catalog = SourceSharp.Abi.NetStateCatalog.Of(typeof(TFJarNetState).Assembly, new RecordingLog());

        int jar = catalog.FieldOffset(TFJarNetState.ClassName, nameof(TFJarNetState.TeamNumber));
        Assert.NotEqual(-1, jar);
        Assert.Equal(jar, catalog.FieldOffset(TFJarNetState.GasClassName, nameof(TFJarNetState.TeamNumber)));
    }
}
