using SourceSharp.Abi;

using TF2.Descent.Server.Loot;

using Xunit;

using static TF2.Descent.Tests.Floor.LootRig;

namespace TF2.Descent.Tests.Floor;

/// <summary>The floor's dev commands: what each prints and does.</summary>
public sealed class LootCommandsTests
{
    private sealed record Kit(LootRig Rig, LootRewards Rewards);

    private static Kit Make()
    {
        var rig = new LootRig();
        return new Kit(rig, new LootRewards(LootRig.Rules, new RigLevel(1, 7), rig.Party, rig.Ledger, new RigGround(), rig.Floor, rig.Log));
    }

    private static string Lines(LootRig r) => r.Log.Text;

    [Fact]
    public void Loot_prints_each_drop_its_seats_room_and_entity_and_the_pool()
    {
        var k = Make();
        var drop = k.Rig.Drop(InRoom(1), australiumB: 9);
        k.Rig.Rooms.Asleep.Add(2);
        var data = k.Rig.Drop(InRoom(2));

        new LootFloorCommand(k.Rig.Floor, k.Rig.Log).Execute();

        string text = Lines(k.Rig);
        Assert.Contains("floor: 2 drops (1 on entities, 1 data), pool 64, free 63", text);
        Assert.Contains($"drop {drop.Number} at (768 256 0) room 1 entity #{drop.Entity!.Handle.EntryIndex}: {A}@{SlotA} shovel", text);
        Assert.Contains("rocket_launcher", text);
        Assert.Contains("+ 9 Australium", text);
        Assert.Contains($"drop {data.Number} at (1280 256 0) room 2 data", text);
    }

    [Fact]
    public async Task Loot_drop_with_a_tier_puts_a_drop_of_it_at_the_players_feet()
    {
        var k = Make();
        var command = new LootDropCommand(k.Rewards, k.Rig.Party, k.Rig.Log);

        command.Execute(SlotA, 3);
        await Task.Yield();

        var drop = Assert.Single(k.Rig.Floor.Drops);
        var feet = k.Rig.Party.Origin(SlotA)!.Value;
        Assert.Equal(new Vector(feet.X, feet.Y, 2), drop.Position);
        Assert.Equal(3, drop.SeatOf(SlotA)!.Item!.Item.Tier);
        Assert.Contains($"dev drop {LootDropCommand.FirstSequence} for slot {SlotA}", Lines(k.Rig));
    }

    [Fact]
    public void Loot_drop_for_a_slot_with_no_hero_says_so_and_drops_nothing()
    {
        var k = Make();

        new LootDropCommand(k.Rewards, k.Rig.Party, k.Rig.Log).Execute(SlotC);

        Assert.Empty(k.Rig.Floor.Drops);
        Assert.Contains(k.Rig.Log.Lines, l => l.Warning && l.Text.Contains("no living hero in slot " + SlotC, StringComparison.Ordinal));
    }

    [Fact]
    public void Take_takes_the_players_seat_of_the_nearest_drop_as_a_use_does()
    {
        var k = Make();
        var near = k.Rig.Floor.Place(new Vector(110, 100, 0), [new LootShare(A, SlotA, k.Rig.Item(A), 0)])!;
        var far = k.Rig.Floor.Place(new Vector(1100, 100, 0), [new LootShare(A, SlotA, k.Rig.Item(A), 0)])!;
        long id = near.SeatOf(SlotA)!.Item!.Id;

        new LootTakeCommand(k.Rig.Floor, k.Rig.Log).Execute(SlotA);

        Assert.Contains(k.Rig.Memory.Find(A)!.Inventory.Backpack.Values, h => h.Id == id);
        Assert.DoesNotContain(near, k.Rig.Floor.Drops);
        Assert.Contains(far, k.Rig.Floor.Drops);
        Assert.Contains($"tf2_descent_take: slot {SlotA}, drop {near.Number}: Taken", Lines(k.Rig));
    }

    [Fact]
    public async Task Fill_backpack_fills_it_and_says_it_is_full()
    {
        var k = Make();

        var keys = Keys();

        new FillBackpackCommand(k.Rig.Ledger, keys, k.Rig.Log).Execute(SlotA);
        await Task.Yield();

        var hero = k.Rig.Memory.Find(keys.Of(SlotA)!)!;
        Assert.Equal(-1, LootRig.Rules.Inventory.FreeSlot(hero.Inventory, hero.Sheet));
        Assert.True(hero.Inventory.Backpack.Count > 3, "the stimulus: the fill gave more than the starting kit");
        Assert.Contains("(full)", Lines(k.Rig));
    }

    /// <summary>The unhosted ledger's names for the slots.</summary>
    private static TF2.Descent.Server.Items.HeroKeys Keys() => new Hero.HeroRig().Keys();
}
