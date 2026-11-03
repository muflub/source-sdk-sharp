using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <c>tf_use_fixed_weaponspreads</c> (stock <c>IsFixedWeaponSpreadEnabled</c>
/// and the pellet loops, <c>tf_fx_shared.cpp:302-338</c>): a buckshot load
/// past one pellet leaves in the fixed pattern while the convar is set, and
/// the tables are stock's.
/// </summary>
public sealed class TFFixedWeaponSpreadTests
{
    private const int AttackButton = (int)InputButtons.Attack;

    private static TFUseFixedWeaponspreadsConVar Set(string value)
    {
        var convar = new TFUseFixedWeaponspreadsConVar();
        convar.Bind(-1, "tf_use_fixed_weaponspreads", value);
        return convar;
    }

    private static TFWeaponBehaviour Behaviour(TFWeaponWorld world, string weapon) =>
        (TFWeaponBehaviour)world.Behaviour(weapon);

    private static WeaponAttackContext Attack(TFWeaponWorld world, string weapon)
    {
        var (w, vm) = world.Armed(weapon, 6, 32);
        return world.Attack(w, vm, now: 10f);
    }

    [Fact]
    public void The_convar_unset_the_load_stays_random()
    {
        var world = new TFWeaponWorld();
        var attack = Attack(world, "tf_weapon_scattergun");

        Assert.Null(Behaviour(world, "tf_weapon_scattergun").FixedPellets(in attack));
    }

    [Fact]
    public void The_convar_at_zero_too()
    {
        var world = new TFWeaponWorld(fixedSpread: Set("0"));
        var attack = Attack(world, "tf_weapon_scattergun");

        Assert.Null(Behaviour(world, "tf_weapon_scattergun").FixedPellets(in attack));
    }

    [Fact]
    public void A_set_convar_hands_the_scatterguns_load_the_ten_pellet_cross()
    {
        var world = new TFWeaponWorld(fixedSpread: Set("1"));
        var attack = Attack(world, "tf_weapon_scattergun");
        var pattern = Behaviour(world, "tf_weapon_scattergun").FixedPellets(in attack);

        Assert.NotNull(pattern);

        var stream = new SeededUniformStream();
        (float Right, float Up) Of(int pellet) => pattern!(pellet, stream);

        Assert.Equal((0f, 0f), Of(0));           // the first, down the middle
        Assert.Equal((0.5f, 0f), Of(1));
        Assert.Equal((-0.5f, 0f), Of(2));
        Assert.Equal((0f, -0.5f), Of(3));
        Assert.Equal((0f, 0.5f), Of(4));
        Assert.Equal((0.425f, -0.425f), Of(5));  // the diagonals, pulled in to 0.85
        Assert.Equal((-0.425f, 0.425f), Of(8));
        Assert.Equal((0f, 0f), Of(9));           // the last, down the middle
        Assert.Equal((0.5f, 0f), Of(11));        // a wider load wraps onto the table
    }

    [Fact]
    public void Single_pellet_and_non_buckshot_loads_stay_random()
    {
        var world = new TFWeaponWorld(fixedSpread: Set("1"));

        // The pistol: one pellet a shot. The minigun: a bullet stream, not buckshot.
        var pistol = Attack(world, "tf_weapon_pistol");
        var minigun = Attack(world, "tf_weapon_minigun");
        Assert.Null(Behaviour(world, "tf_weapon_pistol").FixedPellets(in pistol));
        Assert.Null(Behaviour(world, "tf_weapon_minigun").FixedPellets(in minigun));
    }

    [Fact]
    public void The_machine_hands_the_pattern_to_the_arms()
    {
        var on = new TFWeaponWorld(fixedSpread: Set("1"));
        var (w, vm) = on.Armed("tf_weapon_scattergun", 6, 32);
        on.Run(w, vm, AttackButton, now: 10f);

        var off = new TFWeaponWorld();
        (w, vm) = off.Armed("tf_weapon_scattergun", 6, 32);
        off.Run(w, vm, AttackButton, now: 10f);

        Assert.NotNull(on.Arms.Bullets[^1].Fixed);
        Assert.Null(off.Arms.Bullets[^1].Fixed);
    }

    [Fact]
    public void The_wide_table_is_the_rectangle_beside_its_own_draw()
    {
        var stream = new SeededUniformStream(7);

        // Table point (0,0), each coordinate beside its own draw of the +-0.07.
        (float right, float up) = TFWeaponBehaviour.WidePattern(0, stream);
        Assert.InRange(right, -0.07f, 0.07f);
        Assert.InRange(up, -0.07f, 0.07f);
        Assert.True(right != 0f || up != 0f, "the jitter never moved the pellet (the positive arm)");

        (right, up) = TFWeaponBehaviour.WidePattern(7, stream);   // (-1, 0.5) beside its draws
        Assert.InRange(right - -1f, -0.07f, 0.07f);
        Assert.InRange(up - 0.5f, -0.07f, 0.07f);

        (right, up) = TFWeaponBehaviour.WidePattern(12, stream);  // (-1, -0.5) beside its draws
        Assert.InRange(right - -1f, -0.07f, 0.07f);
        Assert.InRange(up - -0.5f, -0.07f, 0.07f);
    }
}
