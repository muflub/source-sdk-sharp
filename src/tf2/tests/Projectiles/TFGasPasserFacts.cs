using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Server;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
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
}
