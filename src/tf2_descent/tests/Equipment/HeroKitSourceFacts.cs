using SourceSharp.Tests;
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

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_first_kits_launcher_is_the_primary_TF2_hands_out()
    {
        var rig = new EquipmentRig();

        var answer = rig.Answer(TFLoadoutSlot.Primary);

        Assert.True(answer.HasOpinion);
        Assert.Equal(RocketLauncher, answer.Item!.Defindex);
        Assert.Equal((ulong)rig.Held.Inventory.Equipped["primary"].Id, answer.Item.Id);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_rolled_TF_affix_rides_the_item_as_its_attribute()
    {
        var rig = new EquipmentRig();
        int from = rig.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));

        Assert.True(rig.Equip(from, "primary").Ok);

        var value = Assert.Single(rig.Answer(TFLoadoutSlot.Primary).Item!.Attributes);
        Assert.Equal("mult_dmg", value.Definition.AttributeClass);
        Assert.Equal(1.2f, value.AsFloat, 0.0001f);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
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

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
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

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_stock_launcher_carries_no_damage_bonus()
    {
        // The control for the fact above: the first kit's launcher.
        var rig = new EquipmentRig();

        var weapon = TFLoadoutProvider.Attributes(rig.Answer(TFLoadoutSlot.Primary).Item!);

        Assert.Equal(100f, new AttributeHooks().Float(100f, "mult_dmg", weapon), 0.01f);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_broken_weapon_is_handed_over_as_its_stock_self()
    {
        var rig = new EquipmentRig();
        rig.Plant("primary", EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)) with { Durability = 0 });

        var item = rig.Answer(TFLoadoutSlot.Primary).Item!;

        Assert.Equal(RocketLauncher, item.Defindex);
        Assert.Empty(item.Attributes);
        Assert.Equal(rig.Schema.Quality("unique"), item.Quality);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
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

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_empty_secondary_slot_carries_the_classs_stock_secondary()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Unequip("secondary").Ok);

        var answer = rig.Answer(TFLoadoutSlot.Secondary);

        Assert.Equal(SoldierShotgun, answer.Item!.Defindex);
        Assert.Empty(answer.Item.Attributes);
        Assert.DoesNotContain(rig.Held.Inventory.Items, h => (ulong)h.Id == answer.Item.Id);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_empty_primary_slot_carries_the_classs_stock_primary()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Unequip("primary").Ok);

        var answer = rig.Answer(TFLoadoutSlot.Primary);

        Assert.Equal(RocketLauncher, answer.Item!.Defindex);
        Assert.Empty(answer.Item.Attributes);
        Assert.DoesNotContain(rig.Held.Inventory.Items, h => (ulong)h.Id == answer.Item.Id);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_secondary_equipped_replaces_the_stock_one()
    {
        var rig = new EquipmentRig();
        long id = rig.Held.Inventory.Equipped["secondary"].Id;
        Assert.True(rig.Unequip("secondary").Ok);
        var stock = rig.Answer(TFLoadoutSlot.Secondary).Item!;
        int from = rig.Held.Inventory.Backpack.Single(p => p.Value.Id == id).Key;

        Assert.True(rig.Equip(from, "secondary").Ok);

        var answer = rig.Answer(TFLoadoutSlot.Secondary).Item!;
        Assert.Equal((ulong)id, answer.Id);
        Assert.False(answer.SameAs(stock));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_equipped_weapon_the_class_may_not_use_is_still_empty()
    {
        var rig = new EquipmentRig();
        rig.Plant("primary", EquipmentRig.Item("grenade_launcher"));

        var answer = rig.Answer(TFLoadoutSlot.Primary);

        Assert.True(answer.HasOpinion);
        Assert.Null(answer.Item);
    }

    /// <summary>The Soldier's stock shotgun's defindex in TF's schema.</summary>
    private const int SoldierShotgun = 10;

    /// <summary>The stock shovel's defindex in TF's schema.</summary>
    private const int Shovel = 6;

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_empty_melee_slot_swings_the_classs_stock_melee()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Unequip("melee").Ok);

        var answer = rig.Answer(TFLoadoutSlot.Melee);

        Assert.Equal(Shovel, answer.Item!.Defindex);
        Assert.Empty(answer.Item.Attributes);
        Assert.DoesNotContain(rig.Held.Inventory.Items, h => (ulong)h.Id == answer.Item.Id);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
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

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hero_with_empty_weapon_slots_carries_the_classs_three_stock_weapons()
    {
        // The user, 2026-11-15: "when you die you should be respawned with default weapons".
        var rig = new EquipmentRig();
        rig.Ledger.Replace(rig.Held with { Inventory = new TF2.Descent.Rules.Inventory.HeroInventory() });
        Assert.Empty(rig.Held.Inventory.Equipped);

        Assert.Equal(RocketLauncher, rig.Answer(TFLoadoutSlot.Primary).Item!.Defindex);
        Assert.Equal(SoldierShotgun, rig.Answer(TFLoadoutSlot.Secondary).Item!.Defindex);
        Assert.Equal(Shovel, rig.Answer(TFLoadoutSlot.Melee).Item!.Defindex);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_hero_after_a_death_carries_the_starting_kits_weapons_as_descent_items()
    {
        // The user, 2026-11-22 (plan L26): a death gives the class's starting kit, equipped.
        var rig = new EquipmentRig();
        Assert.True(await rig.Ledger.RecordDeath(EquipmentRig.Key, default));

        var primary = rig.Answer(TFLoadoutSlot.Primary).Item!;
        Assert.Equal(RocketLauncher, primary.Defindex);
        Assert.Equal((ulong)rig.Held.Inventory.Equipped["primary"].Id, primary.Id);   // a ledger item, not the stock stand-in
        Assert.Equal(SoldierShotgun, rig.Answer(TFLoadoutSlot.Secondary).Item!.Defindex);
        Assert.Equal(Shovel, rig.Answer(TFLoadoutSlot.Melee).Item!.Defindex);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_slot_that_is_no_weapon_slot_is_TF2s()
    {
        var rig = new EquipmentRig();

        Assert.False(rig.Answer(TFLoadoutSlot.Head).HasOpinion);
        Assert.True(rig.Answer(TFLoadoutSlot.Primary).HasOpinion); // the stimulus: the hero is claimed
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_player_who_is_no_hero_gets_no_opinion()
    {
        var rig = new EquipmentRig();

        Assert.False(rig.Answer(TFLoadoutSlot.Primary, index: EquipmentRig.Index + 1).HasOpinion);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_bot_gets_no_opinion()
    {
        var rig = new EquipmentRig();
        rig.BotList.Handles.Add(EquipmentRig.Player);

        Assert.False(rig.Answer(TFLoadoutSlot.Primary).HasOpinion);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Outside_the_Descent_mode_there_is_no_opinion()
    {
        var rig = new EquipmentRig();
        Assert.True(rig.Answer(TFLoadoutSlot.Primary).HasOpinion);

        rig.ModeActive = false;

        Assert.False(rig.Answer(TFLoadoutSlot.Primary).HasOpinion);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_item_the_class_may_not_use_leaves_its_slot_empty()
    {
        // A planted pyro flamethrower in a soldier's primary (the rules refuse to equip it).
        var rig = new EquipmentRig();
        rig.Plant("primary", EquipmentRig.Item("flamethrower"));

        var answer = rig.Answer(TFLoadoutSlot.Primary);

        Assert.True(answer.HasOpinion);
        Assert.Null(answer.Item);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
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
