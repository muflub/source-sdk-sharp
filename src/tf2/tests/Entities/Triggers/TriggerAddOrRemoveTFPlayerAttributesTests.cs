using SourceSharp.Abi;

using TF2.Server.Entities.Triggers;
using TF2.Server.Player;
using TF2.Shared.Econ;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Entities.Triggers;

/// <summary>
/// trigger_add_or_remove_tf_player_attributes (stock's
/// <c>CTriggerAddOrRemoveTFPlayerAttributes</c>, tf_triggers.cpp:873-1010):
/// the entry arms and the way out, against the player's runtime list.
/// </summary>
public sealed class TriggerAddOrRemoveTFPlayerAttributesTests
{
    private static (MapItemRig Rig, EntityHandle Player, TriggerAddOrRemoveTFPlayerAttributes Trigger,
                    AttributeContainer Manager, TFPlayerAttributes Attributes) Setup(
        params (string Key, string Value)[] keys)
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var items = new TFPlayerItems();
        var schema = EconTestFiles.TrimmedSchema();
        var attributes = new TFPlayerAttributes(carriers, items, schema, rig.Clock);
        rig.Services.AddInstance(carriers);
        rig.Services.AddInstance(items);
        rig.Services.AddInstance(schema);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer",
            () => rig.Players.All.ContainsKey((EntityHandle)rig.Engine.Last("Entities.IsPlayer")!.Value.Arg(0)!));

        var (player, _) = rig.Players.Add();
        var manager = new AttributeContainer();
        items.SetManager(player.EntryIndex, manager);
        attributes.Attach(player);

        var trigger = rig.Spawn<TriggerAddOrRemoveTFPlayerAttributes>(
            TriggerAddOrRemoveTFPlayerAttributes.MapClass, keys);
        return (rig, player, trigger, manager, attributes);
    }

    [Fact]
    public void Entering_adds_the_attribute_to_the_players_list()
    {
        var (rig, player, trigger, manager, _) =
            Setup(("attribute_name", "crit mod disabled"), ("value", "0.5"), ("duration", "4"));

        rig.Touch(trigger, player);

        var entry = Assert.Single(List(manager));
        Assert.Equal(15, entry.Definition.Defindex);
        Assert.Equal(0.5f, entry.AsFloat);
    }

    [Fact]
    public void A_timed_attribute_leaves_at_its_expiry_not_on_the_way_out()
    {
        var (rig, player, trigger, manager, attributes) =
            Setup(("attribute_name", "crit mod disabled"), ("value", "2"), ("duration", "1"));

        rig.Clock.Now = 5f;
        rig.Touch(trigger, player);
        rig.Registry.EndTouch(trigger.Handle, player);
        Assert.Single(List(manager)); // the expiry owns it now

        attributes.Tick(5.5f);
        Assert.Single(List(manager));

        attributes.Tick(6.5f); // 5 + 1 passed

        Assert.Empty(List(manager));
    }

    [Fact]
    public void A_permanent_attribute_is_taken_back_on_the_way_out()
    {
        var (rig, player, trigger, manager, _) =
            Setup(("attribute_name", "crit mod disabled"), ("value", "2"));

        rig.Touch(trigger, player);
        Assert.Single(List(manager));

        rig.Registry.EndTouch(trigger.Handle, player);

        Assert.Empty(List(manager));
    }

    [Fact]
    public void The_remove_arm_takes_it_on_entry_and_the_way_out_leaves_it()
    {
        var (rig, player, trigger, manager, attributes) =
            Setup(("add_or_remove", "1"), ("attribute_name", "crit mod disabled"), ("value", "2"));
        attributes.Add("crit mod disabled", 2f);

        rig.Touch(trigger, player);

        Assert.Empty(List(manager));

        rig.Registry.EndTouch(trigger.Handle, player);

        Assert.Empty(List(manager));
    }

    [Fact]
    public void An_unknown_name_arms_nothing()
    {
        var (rig, player, trigger, manager, attributes) = Setup(("attribute_name", "no such attribute"), ("value", "2"));
        attributes.Add("crit mod disabled", 2f);

        rig.Touch(trigger, player);
        rig.Registry.EndTouch(trigger.Handle, player);

        var entry = Assert.Single(List(manager));
        Assert.Equal(2f, entry.AsFloat); // the pre-existing one, untouched by either arm
    }

    [Fact]
    public void A_disabled_trigger_adds_nothing()
    {
        var (rig, player, trigger, manager, _) =
            Setup(("attribute_name", "crit mod disabled"), ("value", "2"), ("StartDisabled", "1"));

        rig.Touch(trigger, player);

        Assert.Empty(List(manager));
    }

    [Fact]
    public void Every_entry_counts_whatever_the_volumes_wait()
    {
        var (rig, player, trigger, manager, _) =
            Setup(("attribute_name", "crit mod disabled"), ("value", "2"), ("wait", "10"));

        rig.Touch(trigger, player);
        rig.Registry.EndTouch(trigger.Handle, player);
        rig.Touch(trigger, player);

        Assert.Single(List(manager));
    }

    private static AttributeValue[] List(AttributeContainer manager) => manager.Attributes.ToArray();
}
