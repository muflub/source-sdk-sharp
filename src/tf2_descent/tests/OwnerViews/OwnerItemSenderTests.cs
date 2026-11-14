using SourceSharp.Tests;
using SourceSharp.Abi;

using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Gear;

using Xunit;

namespace TF2.Descent.Tests.OwnerViews;

/// <summary>
/// The owner's item views (plan_descent_logic UI1, UI3): every change to a hero's items
/// reaches his own client, one item a message, an unidentified item without its affixes,
/// each with what the server decides about it now; a resync at each spawn.
/// </summary>
public sealed class OwnerItemSenderTests
{
    private static int Viewable(OwnerRig rig) => rig.Gear.Held.Items.Count(i => i.Place != ItemPlace.BuyBack);

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_load_sends_every_item_the_hero_holds_to_his_client()
    {
        var rig = new OwnerRig();

        var views = rig.Of<OwnerItem>();
        Assert.True(Viewable(rig) >= 6, "the starting kit is held");   // three weapons, three potions
        Assert.Equal(rig.Gear.Held.Items.Where(i => i.Place != ItemPlace.BuyBack).Select(i => i.Id).Order(), views.Select(v => v.Id).Distinct().Order());
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Every_message_goes_to_the_owner_and_no_one_else()
    {
        var rig = new OwnerRig();
        rig.Items.Resync(OwnerRig.Index);
        rig.Gear.Give(EquipmentRig.Item("shotgun"));

        Assert.NotEmpty(rig.Sent);
        Assert.All(rig.Sent, s => Assert.Equal(OwnerRig.Index, s.Player));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_resync_is_its_begin_every_item_and_its_end_with_the_count()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Items.Resync(OwnerRig.Index);

        var sent = rig.Sent.Skip(from).ToList();
        int n = Viewable(rig);
        var bracket = rig.Of<OwnerItems>(from);
        Assert.Equal(OwnerRig.KeyOf<OwnerItems>(), sent[0].Key);
        Assert.Equal((byte)OwnerItemsPhase.Begin, bracket[0].Phase);
        Assert.Equal(n, bracket[0].Count);
        Assert.All(sent.Skip(1).Take(n), s => Assert.Equal(OwnerRig.KeyOf<OwnerItem>(), s.Key));
        Assert.Equal((byte)OwnerItemsPhase.End, bracket[1].Phase);
        Assert.Equal(n, bracket[1].Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_resync_sends_each_item_in_a_message_of_its_own_under_the_engines_limit()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Items.Resync(OwnerRig.Index);

        var items = rig.Sent.Skip(from).Where(s => s.Key == OwnerRig.KeyOf<OwnerItem>()).ToList();
        Assert.Equal(Viewable(rig), items.Count);
        Assert.True(items.Count > 1);
        Assert.All(items, s => Assert.InRange(s.Bytes.Length, 1, 255));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_resync_for_a_player_with_no_hero_sends_nothing()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Items.Resync(OwnerRig.Index + 1);

        Assert.Equal(from, rig.Sent.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_spawn_resyncs_the_heros_items()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Items.OnSpawned(OwnerRig.Player, 0f);

        Assert.Equal((byte)OwnerItemsPhase.Begin, rig.Of<OwnerItems>(from)[0].Phase);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task An_unidentified_items_message_carries_none_of_its_affixes()
    {
        var rig = new OwnerRig();
        var given = await rig.Gear.Ledger.Give(EquipmentRig.Key, "rocket_launcher", 2, 12);
        var added = Assert.Single(given.Change!.Added);
        Assert.False(added.Item.Identified);
        Assert.NotEmpty(added.Item.Affixes);   // the stimulus: it has affixes to hide

        var view = rig.Of<OwnerItem>().Last(v => v.Id == added.Id);

        Assert.Empty(view.Affixes);
        Assert.Empty(view.Values);
        Assert.Equal(0, view.Flags & (byte)ItemViewFlags.Identified);
        Assert.Equal(0, view.Durability);
        Assert.Equal(0, view.Unusual);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_identified_items_message_names_each_affix_and_its_rolled_value()
    {
        var rig = new OwnerRig();
        var item = EquipmentRig.Item("rocket_launcher", new RolledAffix("heavy", 5000), new RolledAffix("of_aim", 10000));
        rig.Gear.Give(item);
        var held = rig.Gear.Held.Inventory.Backpack.Values.Single(h => h.Item.BaseId == "rocket_launcher" && h.Item.Affixes.Length == 2);

        var view = rig.Of<OwnerItem>().Last(v => v.Id == held.Id);

        var affixes = rig.Rules.Content.Affixes;
        Assert.Equal([ContentKeys.IndexOf(affixes, "heavy"), ContentKeys.IndexOf(affixes, "of_aim")], view.Affixes.Select(a => (int)a));
        Assert.Equal(held.Item.Affixes.Select(a => (float)rig.Rules.ItemRules.Roller.Value(held.Item, a)), view.Values);
        Assert.Equal((byte)ItemViewFlags.Identified, view.Flags & (byte)ItemViewFlags.Identified);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_dropped_item_is_told_gone()
    {
        var rig = new OwnerRig();
        int slot = rig.Gear.Give(EquipmentRig.Item("shotgun"));
        var held = rig.Gear.Held.Inventory.Backpack[slot];

        Assert.True((await rig.Gear.Ledger.Drop(EquipmentRig.Key, slot, default)).Ok);

        Assert.Equal(held.Id, rig.Of<OwnerItemGone>().Single().Id);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_item_the_hero_fails_marks_each_unmet_requirement()
    {
        var rig = new OwnerRig();
        rig.Gear.Give(EquipmentRig.Item("direct_hit"));   // level 6, aim 30; the hero is level 1 with aim 20
        var held = rig.Gear.Held.Inventory.Backpack.Values.Single(h => h.Item.BaseId == "direct_hit");

        var view = rig.Of<OwnerItem>().Last(v => v.Id == held.Id);

        Assert.Equal(0b11, view.Unmet);
        Assert.Equal(0, view.Flags & (byte)ItemViewFlags.MeetsRequirements);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Meeting_a_requirement_later_sends_the_item_again_as_met()
    {
        var rig = new OwnerRig();
        rig.Gear.Give(EquipmentRig.Item("direct_hit"));
        var held = rig.Gear.Held.Inventory.Backpack.Values.Single(h => h.Item.BaseId == "direct_hit");
        rig.Level(6);
        int from = rig.Sent.Count;

        rig.Hero.Apply(s => rig.Rules.Heroes.Spend(s, "aim", 10).Sheet);

        var view = rig.Of<OwnerItem>(from).Single(v => v.Id == held.Id);
        Assert.Equal(0, view.Unmet);
        Assert.Equal((byte)ItemViewFlags.MeetsRequirements, view.Flags & (byte)ItemViewFlags.MeetsRequirements);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_stats_change_that_changes_no_item_sends_no_item_again()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Hero.Recompute();

        Assert.Empty(rig.Of<OwnerItem>(from));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_equipped_item_that_counts_for_nothing_is_sent_inactive_and_broken()
    {
        var rig = new OwnerRig();
        var broken = EquipmentRig.Item("shovel") with { Durability = 0 };
        rig.Gear.Plant("melee", broken, id: 901);

        rig.Hero.Recompute();

        var view = rig.Of<OwnerItem>().Last(v => v.Id == 901);
        Assert.Equal((byte)ItemContainer.Equipped, view.Container);
        Assert.Equal(rig.Rules.Content.InventorySettings.Equipment.ToList().FindIndex(e => e.Id == "melee"), view.Slot);
        Assert.Equal((byte)(ItemViewFlags.Inactive | ItemViewFlags.Broken),
                     view.Flags & (byte)(ItemViewFlags.Inactive | ItemViewFlags.Broken));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_equipped_item_that_counts_is_not_inactive()
    {
        var rig = new OwnerRig();
        var view = rig.Of<OwnerItem>().Last(v => v.Container == (byte)ItemContainer.Equipped);

        Assert.Equal(0, view.Flags & (byte)ItemViewFlags.Inactive);
        Assert.Equal((byte)ItemViewFlags.MeetsRequirements, view.Flags & (byte)ItemViewFlags.MeetsRequirements);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_purse_and_the_backpack_capacity_go_as_a_state()
    {
        var rig = new OwnerRig();

        var state = rig.Of<OwnerItems>().Last(s => s.Phase == (byte)OwnerItemsPhase.State);

        Assert.Equal(rig.Rules.Inventory.BackpackSlots(rig.Hero.Sheet!), state.BackpackCapacity);
        Assert.Equal(rig.Gear.Held.Inventory.Australium, state.Australium);
        Assert.Equal(ContentKeys.Fingerprint(rig.Rules.Content), state.Fingerprint);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Equipping_an_item_sends_it_in_its_equipment_slot()
    {
        var rig = new OwnerRig();
        Assert.True(rig.Gear.Unequip("primary").Ok);
        var held = rig.Gear.Held.Inventory.Backpack.First(p => p.Value.Item.BaseId == "rocket_launcher");
        int from = rig.Sent.Count;

        Assert.True(rig.Gear.Equip(held.Key, "primary").Ok);

        var view = rig.Of<OwnerItem>(from).Single(v => v.Id == held.Value.Id);
        Assert.Equal((byte)ItemContainer.Equipped, view.Container);
        Assert.Equal(0, view.Slot);   // primary is inventory.json's first equipment slot
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_view_names_the_items_base_by_its_row()
    {
        var rig = new OwnerRig();
        var held = rig.Gear.Held.Inventory.Equipped["melee"];

        var view = rig.Of<OwnerItem>().Last(v => v.Id == held.Id);

        Assert.Equal(ContentKeys.IndexOf(rig.Rules.Content.ItemBases, held.Item.BaseId), view.Base);
    }

    [Fact]
    public void The_fullest_view_fits_one_message()
    {
        var full = new OwnerItem
        {
            Id = long.MaxValue,
            Affixes = new byte[OwnerItem.MaxAffixes],
            Values = new float[OwnerItem.MaxAffixes],
        };

        Assert.InRange(full.Write(new byte[255]), 1, 255);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_item_given_to_a_hero_this_sender_cannot_place_is_sent_to_nobody()
    {
        var rig = new OwnerRig();
        int from = rig.Sent.Count;

        rig.Items.OnItemsChanged(new ItemsChanged("someone_else", "give", [], [], [], [], 0, 0));

        Assert.Equal(from, rig.Sent.Count);
    }
}
