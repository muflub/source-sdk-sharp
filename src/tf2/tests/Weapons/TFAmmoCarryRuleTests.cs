using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary><see cref="TFAmmoCarryRule"/>: <c>CTFPlayer::GetMaxAmmo</c> from the class scripts' <c>AmmoMax</c>.</summary>
public sealed class TFAmmoCarryRuleTests
{
    private const int Scout = 3;
    private const int Heavy = 4;
    private const int Engineer = 5;
    private const int Soldier = 6;

    private static (TFAmmoCarryRule Rule, FakeOwners Owners) Rule()
    {
        var files = TFClassTableTests.Files.Shipped();
        var owners = new FakeOwners();
        owners.Classes[Scout] = TFClass.Scout;
        owners.Classes[Heavy] = TFClass.HeavyWeapons;
        return (new TFAmmoCarryRule(new TFClassTable(new RecordingLog(), files), owners), owners);
    }

    private static AmmoType Row(TFAmmo ammo) => new TFAmmoTypes().Rows().Single(r => r.Name == TFAmmoNames.Name(ammo));

    [Fact]
    public void A_players_primary_limit_is_their_classs_and_not_the_next_players()
    {
        var (rule, _) = Rule();
        var primary = Row(TFAmmo.Primary);

        // scout.txt AmmoMax "tf_ammo_primary" "32"; heavyweapons.txt "200".
        WeaponFixturesSay("scout", "tf_ammo_primary", "32");
        WeaponFixturesSay("heavyweapons", "tf_ammo_primary", "200");
        Assert.Equal(32, rule.MaxCarry(Scout, in primary, primary.MaxCarry));
        Assert.Equal(200, rule.MaxCarry(Heavy, in primary, primary.MaxCarry));
    }

    [Fact]
    public void Every_class_carries_one_grenade3()
    {
        var (rule, _) = Rule();
        var grenades3 = Row(TFAmmo.Grenades3);

        Assert.Equal(1, rule.MaxCarry(Scout, in grenades3, grenades3.MaxCarry));
        Assert.Equal(1, rule.MaxCarry(Heavy, in grenades3, grenades3.MaxCarry));
    }

    [Fact]
    public void A_classless_player_keeps_the_limit_handed_in()
    {
        var (rule, _) = Rule();
        var metal = Row(TFAmmo.Metal);

        Assert.Equal(TFAmmoTypes.AmmoMax, rule.MaxCarry(99, in metal, metal.MaxCarry));
        Assert.Equal(17, rule.MaxCarry(99, in metal, 17));
    }

    [Fact]
    public void A_row_that_is_not_tf_ammo_keeps_the_limit_handed_in()
    {
        var (rule, _) = Rule();
        var pistol = AmmoTypes.All.Single(a => a.Name == "Pistol");

        Assert.Equal(pistol.MaxCarry, rule.MaxCarry(Scout, in pistol, pistol.MaxCarry));
    }

    [Fact]
    public void Without_a_carrier_every_player_is_classless()
    {
        var rule = new TFAmmoCarryRule(new TFClassTable(new RecordingLog(), TFClassTableTests.Files.Shipped()));
        var primary = Row(TFAmmo.Primary);

        Assert.Equal(primary.MaxCarry, rule.MaxCarry(Scout, in primary, primary.MaxCarry));
    }

    [Fact]
    public void Through_the_cores_fold_the_tf_rule_is_the_players_limit()
    {
        var (rule, _) = Rule();
        var carry = new AmmoCarry(new ListHooks<IAmmoCarryRule>(rule));

        Assert.Equal(32, carry.MaxFor(Scout, Row(TFAmmo.Primary)));
        Assert.Equal(TFAmmoTypes.AmmoMax, carry.MaxFor(77, Row(TFAmmo.Primary)));
    }

    /// <summary>
    /// The <c>mult_maxammo_primary</c> arm (<c>tf_player_shared.cpp:13001-13004</c>):
    /// the player's own manager (<c>tf_player.h:637-642</c>) scales the class's
    /// maximum, and a player with no manager keeps it.
    /// </summary>
    [Fact]
    public void The_primary_hook_scales_the_classs_limit()
    {
        var (rule, owners) = Rule();
        var primary = Row(TFAmmo.Primary);

        // scout.txt AmmoMax "tf_ammo_primary" "32" → ×1.5; heavy keeps 200.
        owners.Managers[Scout] = PlayerManager("mult_maxammo_primary", AttributeDescriptionFormat.Percentage, 1.5f);

        Assert.Equal(48, rule.MaxCarry(Scout, in primary, primary.MaxCarry));
        Assert.Equal(200, rule.MaxCarry(Heavy, in primary, primary.MaxCarry));
    }

    /// <summary>
    /// The hook class is chosen by the ammo row: a manager holding another
    /// row's hook (<c>mult_maxammo_metal</c> asked of primary) changes nothing
    /// (<c>tf_player_shared.cpp:13001-13016</c>).
    /// </summary>
    [Fact]
    public void The_hook_of_another_ammo_leaves_the_limit_alone()
    {
        var (rule, owners) = Rule();
        var primary = Row(TFAmmo.Primary);
        owners.Managers[Scout] = PlayerManager("mult_maxammo_metal", AttributeDescriptionFormat.Percentage, 3f);

        Assert.Equal(32, rule.MaxCarry(Scout, in primary, primary.MaxCarry));
    }

    /// <summary>The <c>mult_maxammo_metal</c> arm (<c>tf_player_shared.cpp:13009-13012</c>).</summary>
    [Fact]
    public void The_metal_hook_scales_the_engineers_metal()
    {
        var (rule, owners) = Rule();
        owners.Classes[Engineer] = TFClass.Engineer;
        var metal = Row(TFAmmo.Metal);

        // engineer.txt AmmoMax "tf_ammo_metal" "200" → ×1.5.
        owners.Managers[Engineer] = PlayerManager("mult_maxammo_metal", AttributeDescriptionFormat.Percentage, 1.5f);

        Assert.Equal(300, rule.MaxCarry(Engineer, in metal, metal.MaxCarry));
    }

    /// <summary>The <c>mult_maxammo_grenades1</c> arm (<c>tf_player_shared.cpp:13013-13016</c>).</summary>
    [Fact]
    public void The_grenades1_hook_scales_the_soldiers_grenades()
    {
        var (rule, owners) = Rule();
        owners.Classes[Soldier] = TFClass.Soldier;
        var grenades1 = Row(TFAmmo.Grenades1);

        // soldier.txt AmmoMax "tf_ammo_grenades1" "1" → ×8.
        owners.Managers[Soldier] = PlayerManager("mult_maxammo_grenades1", AttributeDescriptionFormat.Percentage, 8f);

        Assert.Equal(8, rule.MaxCarry(Soldier, in grenades1, grenades1.MaxCarry));
    }

    /// <summary>
    /// The <c>GRENADES3</c> arm (<c>tf_player_shared.cpp:13017-13021</c>) is
    /// pinned to one and carries no hook: a manager holding hooks changes
    /// nothing.
    /// </summary>
    [Fact]
    public void Grenades3_stays_one_whatever_the_manager_holds()
    {
        var (rule, owners) = Rule();
        var grenades3 = Row(TFAmmo.Grenades3);
        owners.Managers[Scout] = PlayerManager("mult_maxammo_primary", AttributeDescriptionFormat.Percentage, 4f);

        Assert.Equal(1, rule.MaxCarry(Scout, in grenades3, grenades3.MaxCarry));
    }

    private int _nextDef = 5000;

    /// <summary>The player's own attribute manager (stock's m_AttributeManager) holding one float attribute.</summary>
    private IAttributeProvider PlayerManager(string hook, AttributeDescriptionFormat format, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    private static void WeaponFixturesSay(string cls, string key, string value) =>
        Assert.Matches(new System.Text.RegularExpressions.Regex(
            "\"" + key + "\"\\s+\"" + value + "\""), TFClassDataTests.Fixture(cls));

    private sealed class ListHooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => all;
        public void Each(string what, Action<T> call) { foreach (var h in all) call(h); }
    }
}
