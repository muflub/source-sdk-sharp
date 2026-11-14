using SourceSharp.Abi;

using TF2.Descent.Rules.Items;
using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Descent.Tests.Gear;

/// <summary>
/// The hero's kit from his equipped items (<see cref="TF2.Descent.Server.Gear.HeroKitSource"/>):
/// each weapon slot answers the equipped base's TF definition with the rules' TF attributes;
/// a weapon that does not count is its Stock self; everyone else is TF2's.
/// </summary>
public sealed class HeroKitSourceFacts
{
    /// <summary>The stock rocket launcher's defindex in TF's schema.</summary>
    private const int RocketLauncher = 18;

    [Fact]
    public void The_first_kits_launcher_is_the_primary_TF2_hands_out()
    {
        var rig = new EquipmentRig();

        var answer = rig.Answer(TFLoadoutSlot.Primary);

        Assert.True(answer.HasOpinion);
        Assert.Equal(RocketLauncher, answer.Item!.Defindex);
        Assert.Equal((ulong)rig.Held.Inventory.Equipped["primary"].Id, answer.Item.Id);
    }

    [Fact]
    public void A_rolled_TF_affix_rides_the_item_as_its_attribute()
    {
        var rig = new EquipmentRig();
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        var value = Assert.Single(rig.Answer(TFLoadoutSlot.Primary).Item!.Attributes);
        Assert.Equal("mult_dmg", value.Definition.AttributeClass);
        Assert.Equal(1.2f, value.AsFloat, 0.0001f);
    }

    [Fact]
    public void A_faster_refire_rolls_as_the_schemas_bonus_not_its_penalty()
    {
        // Rapid: mult_postfiredelay below 1 is a bonus (lower is better for this class).
        // Seen live 2026-11-11: a Rapid Pain Train's 0.94 went out as "fire rate penalty".
        var rig = new EquipmentRig();
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("rapid", 0)));
        Assert.True(rig.Equip(from, "primary").Ok);

        var value = Assert.Single(rig.Answer(TFLoadoutSlot.Primary).Item!.Attributes);

        Assert.True(value.AsFloat < 1f, $"rapid rolled {value.AsFloat}");
        Assert.Equal(AttributeEffectType.Positive, value.Definition.EffectType);
    }

    [Fact]
    public void A_rolled_damage_launchers_damage_hook_answers_its_multiple()
    {
        // The weapon's container is the item's as the kit builds it
        // (TFLoadoutProvider.Attributes): 100 through mult_dmg 1.2 is 120.
        var rig = new EquipmentRig();
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));
        Assert.True(rig.Equip(from, "primary").Ok);

        var weapon = TFLoadoutProvider.Attributes(rig.Answer(TFLoadoutSlot.Primary).Item!);

        Assert.Equal(120f, new AttributeHooks().Float(100f, "mult_dmg", weapon), 0.01f);
    }

    [Fact]
    public void The_stock_launcher_carries_no_damage_bonus()
    {
        // The control for the fact above: the first kit's launcher.
        var rig = new EquipmentRig();

        var weapon = TFLoadoutProvider.Attributes(rig.Answer(TFLoadoutSlot.Primary).Item!);

        Assert.Equal(100f, new AttributeHooks().Float(100f, "mult_dmg", weapon), 0.01f);
    }

    [Fact]
    public void A_broken_weapon_is_handed_over_as_its_stock_self()
    {
        var rig = new EquipmentRig();
        rig.Plant("primary", EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)) with { Durability = 0 });

        var item = rig.Answer(TFLoadoutSlot.Primary).Item!;

        Assert.Equal(RocketLauncher, item.Defindex);
        Assert.Empty(item.Attributes);
        Assert.Equal(rig.Schema.Quality("unique"), item.Quality);
    }

    [Fact]
    public void An_inactive_weapon_is_handed_over_as_its_stock_self()
    {
        // The Black Box asks level 9 and brawn 35 of a level 1 soldier with 20.
        var rig = new EquipmentRig();
        rig.Plant("primary", EquipmentRig.Item("black_box", new RolledAffix("devastating", 0)));
        Assert.True(rig.Equipment.Of(EquipmentRig.Index)!.Result.State("primary").FiresAsStock);

        var item = rig.Answer(TFLoadoutSlot.Primary).Item!;

        Assert.Equal(rig.Schema.Item("The Black Box")!.Defindex, item.Defindex);
        Assert.Empty(item.Attributes);
    }

    [Fact]
    public void An_empty_weapon_slot_is_empty_in_TF2()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Unequip("secondary").Ok);

        var answer = rig.Answer(TFLoadoutSlot.Secondary);

        Assert.True(answer.HasOpinion);
        Assert.Null(answer.Item);
    }

    /// <summary>The stock shovel's defindex in TF's schema.</summary>
    private const int Shovel = 6;

    [Fact]
    public void An_empty_melee_slot_swings_the_classs_stock_melee()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Unequip("melee").Ok);

        var answer = rig.Answer(TFLoadoutSlot.Melee);

        Assert.Equal(Shovel, answer.Item!.Defindex);
        Assert.Empty(answer.Item.Attributes);
        Assert.DoesNotContain(rig.Held.Inventory.Items, h => (ulong)h.Id == answer.Item.Id);
    }

    [Fact]
    public void A_melee_equipped_replaces_the_fallback()
    {
        var rig = new EquipmentRig();
        long id = rig.Held.Inventory.Equipped["melee"].Id;
        Assert.True(rig.Unequip("melee").Ok);
        var fallback = rig.Answer(TFLoadoutSlot.Melee).Item!;
        int from = rig.Held.Inventory.Backpack.Single(p => p.Value.Id == id).Key;

        Assert.True(rig.Equip(from, "melee").Ok);

        var answer = rig.Answer(TFLoadoutSlot.Melee).Item!;
        Assert.Equal((ulong)id, answer.Id);
        Assert.False(answer.SameAs(fallback));
    }

    [Fact]
    public async Task A_hero_stripped_by_a_death_keeps_a_melee_and_nothing_else()
    {
        var rig = new EquipmentRig();
        Assert.True(await rig.Ledger.RecordDeath(EquipmentRig.Key, default));

        Assert.Null(rig.Answer(TFLoadoutSlot.Primary).Item);
        Assert.Null(rig.Answer(TFLoadoutSlot.Secondary).Item);
        Assert.Equal(Shovel, rig.Answer(TFLoadoutSlot.Melee).Item!.Defindex);
    }

    [Fact]
    public void A_slot_that_is_no_weapon_slot_is_TF2s()
    {
        var rig = new EquipmentRig();

        Assert.False(rig.Answer(TFLoadoutSlot.Head).HasOpinion);
        Assert.True(rig.Answer(TFLoadoutSlot.Primary).HasOpinion); // the stimulus: the hero is claimed
    }

    [Fact]
    public void A_player_who_is_no_hero_gets_no_opinion()
    {
        var rig = new EquipmentRig();

        Assert.False(rig.Answer(TFLoadoutSlot.Primary, index: EquipmentRig.Index + 1).HasOpinion);
    }

    [Fact]
    public void A_bot_gets_no_opinion()
    {
        var rig = new EquipmentRig();
        rig.BotList.Handles.Add(EquipmentRig.Player);

        Assert.False(rig.Answer(TFLoadoutSlot.Primary).HasOpinion);
    }

    [Fact]
    public void Outside_the_Descent_mode_there_is_no_opinion()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Answer(TFLoadoutSlot.Primary).HasOpinion);

        rig.ModeActive = false;

        Assert.False(rig.Answer(TFLoadoutSlot.Primary).HasOpinion);
    }

    [Fact]
    public void An_item_the_class_may_not_use_leaves_its_slot_empty()
    {
        // A planted pyro flamethrower in a soldier's primary (the rules refuse to equip it).
        var rig = new EquipmentRig();
        rig.Plant("primary", EquipmentRig.Item("flamethrower"));

        var answer = rig.Answer(TFLoadoutSlot.Primary);

        Assert.True(answer.HasOpinion);
        Assert.Null(answer.Item);
    }

    [Fact]
    public void A_fresh_answer_for_the_same_item_is_the_same_item_to_a_kit()
    {
        var rig = new EquipmentRig();
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));
        Assert.True(rig.Equip(from, "primary").Ok);

        var first = rig.Answer(TFLoadoutSlot.Primary).Item!;
        var second = rig.Answer(TFLoadoutSlot.Primary).Item!;

        Assert.NotSame(first, second);
        Assert.True(first.SameAs(second));
    }
}
