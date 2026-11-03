using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Anim;

/// <summary>
/// The hands arm of stock's <c>CTFWeaponBase::GetViewModel</c>
/// (tf_weaponbase.cpp:651-676) as the chain's rule: for an item that
/// attaches to hands the viewmodel wears the class's
/// <c>model_hands</c>, and the weapon's own c_model waits for the
/// attachment.
/// </summary>
public sealed class TFViewmodelHandsTests
{
    private const int Player = 3;

    private sealed class Owner(TFClass cls) : ITFWeaponOwners
    {
        public TFClass ClassOf(int playerIndex) => cls;
        public TFConditions? ConditionsOf(int playerIndex) => null;
        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;
        public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => null;
        public IAttributeProvider? ManagerOf(int playerIndex) => null;
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => false;
    }

    private static TFWeaponTypes Types() => WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"));

    private static TFClassTable Classes() => new(new RecordingLog(), WeaponFixtures.ShippedWithClasses());

    private static TFViewmodelHandsRule Rule(TFClass cls) => new(Types(), Classes(), new Owner(cls));

    private static WeaponType Row(TFWeaponTypes types, string name) => types.Rows().First(r => r.Name == name);

    [Fact]
    public void The_rocket_launcher_dresses_the_viewmodel_in_the_holders_hands()
    {
        Assert.Equal("models/weapons/c_models/c_soldier_arms.mdl",
                     Rule(TFClass.Soldier).Model(Player, Row(Types(), "tf_weapon_rocketlauncher"), null));
    }

    [Fact]
    public void Each_class_names_its_own_hands_for_the_same_attach()
    {
        Assert.Equal("models/weapons/c_models/c_heavy_arms.mdl",
                     Rule(TFClass.HeavyWeapons).Model(Player, Row(Types(), "tf_weapon_minigun"), null));
        Assert.Equal("models/weapons/c_models/c_medic_arms.mdl",
                     Rule(TFClass.Medic).Model(Player, Row(Types(), "tf_weapon_medigun"), null));
    }

    [Fact]
    public void The_per_class_spelling_finds_its_base_item_through_the_stem()
    {
        // tf_weapon_shotgun_soldier's item class is tf_weapon_shotgun: the
        // stem walk stock's loadout does, then the same hands arm.
        Assert.Equal("models/weapons/c_models/c_soldier_arms.mdl",
                     Rule(TFClass.Soldier).Model(Player, Row(Types(), "tf_weapon_shotgun_soldier"), null));
    }

    [Fact]
    public void A_weapon_whose_item_does_not_attach_leaves_the_answer()
    {
        // The crowbar is not in the schema's base items: stock's other arm,
        // GetTFWpnData().szViewModel — the core row's own model.
        var rule = Rule(TFClass.Soldier);
        Assert.Null(rule.Model(Player, new WeaponType("weapon_crowbar", Interval: 0.5f), null));
        Assert.Equal("models/weapons/c_models/c_oldschool.mdl",
                     rule.Model(Player, new WeaponType("weapon_crowbar", Interval: 0.5f),
                                "models/weapons/c_models/c_oldschool.mdl"));
    }

    [Fact]
    public void A_classless_player_leaves_the_answer()
    {
        var rule = new TFViewmodelHandsRule(Types(), Classes());

        Assert.Null(rule.Model(Player, Row(Types(), "tf_weapon_rocketlauncher"), null));
        Assert.Equal("models/weapons/c_models/c_oldschool.mdl",
                     rule.Model(Player, Row(Types(), "tf_weapon_rocketlauncher"),
                                "models/weapons/c_models/c_oldschool.mdl"));
    }
}
