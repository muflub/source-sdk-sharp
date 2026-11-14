using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Gear;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Gear;

/// <summary>
/// The gear warning's deltas (HE9): an owner-only message when a worn slot's wear level
/// crosses a line (ok, low at 25 %, broken, unusable), not for each point; again after a
/// level change.
/// </summary>
public sealed class GearWearFacts
{
    private sealed class Channel : IGearWearChannel
    {
        public List<(int Player, GearWearView Wear)> Seen { get; } = [];
        public void Send(int playerIndex, GearWear wear) => Seen.Add((playerIndex, wear.View()));
    }

    private sealed class Rig
    {
        public readonly EquipmentRig Gear = new();
        public readonly Channel Sent = new();
        public readonly GearWearSync Sync;

        public Rig()
        {
            Sync = new GearWearSync(Gear.Rules, Gear.Hero.Heroes, Gear.Keys, Gear.Ledger, Sent, Gear.Hero.Engine);
            Gear.LedgerHooks.Add(Sync);
            ((IClientLifecycle)Sync).OnClientActive(EquipmentRig.Index);
            Frame();
        }

        public void Frame() => ((IFrameLifecycle)Sync).OnPostThink(0.015f);

        public async Task Set(int durability, int max = 60)
        {
            Assert.True((await Gear.Ledger.SetDurability(EquipmentRig.Key, "primary", durability, max)).Ok);
            Frame();
        }
    }

    [Fact]
    public void An_arriving_hero_is_sent_each_worn_slot_as_ok_and_nothing_for_an_empty_one()
    {
        var rig = new Rig();

        Assert.Equal(["primary", "secondary", "melee"], rig.Sent.Seen.Select(s => s.Wear.Slot));
        Assert.All(rig.Sent.Seen, s => Assert.Equal((EquipmentRig.Index, GearWearLevel.Ok), (s.Player, s.Wear.Level)));
        Assert.Equal(("Rocket Launcher", 60, 60), (rig.Sent.Seen[0].Wear.Name, rig.Sent.Seen[0].Wear.Durability, rig.Sent.Seen[0].Wear.MaxDurability));
    }

    [Fact]
    public async Task Wear_above_the_warning_line_sends_nothing_and_the_point_that_crosses_it_sends_low()
    {
        var rig = new Rig();
        rig.Sent.Seen.Clear();

        await rig.Set(16);
        int above = rig.Sent.Seen.Count;
        await rig.Set(15);

        Assert.Equal(0, above);
        var (player, wear) = Assert.Single(rig.Sent.Seen);
        Assert.Equal((EquipmentRig.Index, "primary", GearWearLevel.Low, 15, 60), (player, wear.Slot, wear.Level, wear.Durability, wear.MaxDurability));
    }

    [Fact]
    public async Task Broken_and_then_unusable_are_each_sent_once()
    {
        var rig = new Rig();
        rig.Sent.Seen.Clear();

        await rig.Set(0);
        await rig.Set(0);
        await rig.Set(0, max: 0);

        Assert.Equal([GearWearLevel.Broken, GearWearLevel.Unusable], rig.Sent.Seen.Select(s => s.Wear.Level));
    }

    [Fact]
    public async Task A_level_change_sends_every_worn_slot_again()
    {
        var rig = new Rig();
        await rig.Set(10);
        rig.Sent.Seen.Clear();

        ((IGameLifecycle)rig.Sync).OnLevelShutdown();
        ((IClientLifecycle)rig.Sync).OnClientActive(EquipmentRig.Index);
        rig.Frame();

        Assert.Equal(3, rig.Sent.Seen.Count);
        Assert.Equal(GearWearLevel.Low, rig.Sent.Seen.Single(s => s.Wear.Slot == "primary").Wear.Level);
    }

    [Fact]
    public void Taking_an_item_off_sends_its_slot_empty()
    {
        var rig = new Rig();
        rig.Sent.Seen.Clear();

        Assert.True(rig.Gear.Unequip("melee").Ok);
        rig.Frame();

        Assert.Equal(("melee", GearWearLevel.Empty), (Assert.Single(rig.Sent.Seen).Wear.Slot, rig.Sent.Seen[0].Wear.Level));
    }

    [Fact]
    public void The_gear_wear_bytes_go_to_the_owner_alone()
    {
        var engine = new RecordingEngine();

        new GearWearChannel(engine).Send(EquipmentRig.Index, new GearWear { Slot = "primary", Name = "Rocket Launcher", Level = (byte)GearWearLevel.Low, Durability = 15, MaxDurability = 60 });

        var send = Assert.Single(engine.Calls, c => c.Member == "UserMessages.Send");
        Assert.EndsWith(":" + GearWearMessage.Name, send.ArgAs<string>(0), StringComparison.Ordinal);
        Assert.Equal(EquipmentRig.Index, send.ArgAs<int>(2));
    }
}
