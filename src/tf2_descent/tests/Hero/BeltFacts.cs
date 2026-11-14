using SourceSharp.Tests;
using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The potion belt in the game (HE8): a drink's effect through the hero, scaled by the two
/// potion stats, its cooldown in ticks, the count falling through the ledger, its refusals
/// (each using nothing), the refill; the belt's message to the owner alone, again after a
/// level change.
/// </summary>
public sealed class BeltFacts
{
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_drink_restores_a_quarter_of_max_life_and_the_count_falls_by_one_through_the_ledger()
    {
        var rig = new BeltRig();
        rig.PlantBelt(0, "health_small", 3);

        var r = rig.Drink(1);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(150, rig.Gear.Health);
        Assert.Equal(50, r.Restored);
        Assert.Equal(2, rig.BeltSlot(0)!.Item.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_drink_starts_the_belts_three_second_cooldown_in_ticks()
    {
        var rig = new BeltRig();

        var r = rig.Drink(1);

        Assert.Equal(rig.Clock.Tick + 200, r.ReadyTick);
        Assert.Equal((rig.Clock.Tick + 200, 200), rig.Belt.CooldownOf(BeltRig.Index));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_second_drink_inside_the_cooldown_is_refused_and_uses_nothing_and_one_after_it_goes_down()
    {
        var rig = new BeltRig();
        rig.PlantBelt(0, "health_small", 3);
        rig.Drink(1);
        rig.Gear.Health = 100;

        var early = rig.Drink(1);
        rig.Clock.Tick += 200;
        var late = rig.Drink(1);

        Assert.Equal(BeltRefusals.OnCooldown, early.Refusal);
        Assert.True(late.Ok, late.Refusal);
        Assert.Equal(1, rig.BeltSlot(0)!.Item.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Field_rations_raises_the_restore_and_shortens_the_cooldown()
    {
        var rig = new BeltRig();
        var source = new ContributionSource(SourceKind.Skill, "field_rations");
        rig.Stats.Given.AddRange([new Contribution("potion_restore_pct", 40, source), new Contribution("potion_cooldown_pct", -30, source)]);
        rig.Hero.Recompute();

        var r = rig.Drink(1);

        Assert.Equal(70, r.Restored);
        Assert.Equal(140, rig.Belt.CooldownOf(BeltRig.Index).CooldownTicks);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_mana_potion_restores_mana_through_the_hero()
    {
        var rig = new BeltRig();
        double max = rig.Hero.Stats!.MaxMana;
        rig.Hero.SetMana(0);

        var r = rig.Drink(3);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(PotionResources.Mana, r.Restores);
        Assert.Equal(max * 0.25, rig.Hero.Mana, 6);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_dead_hero_cannot_drink_and_keeps_the_potion()
    {
        var rig = new BeltRig();
        rig.Clock.Alive = false;

        Assert.Equal(BeltRefusals.Dead, rig.Drink(1).Refusal);
        Assert.NotNull(rig.BeltSlot(0));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_stunned_hero_cannot_drink_and_keeps_the_potion()
    {
        var rig = new BeltRig();
        rig.Clock.IsStunned = true;

        Assert.Equal(BeltRefusals.Stunned, rig.Drink(1).Refusal);
        Assert.NotNull(rig.BeltSlot(0));
        Assert.Equal(100, rig.Gear.Health);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_drink_from_an_empty_slot_is_refused()
    {
        var rig = new BeltRig();

        Assert.Equal(BeltRefusals.Empty, rig.Drink(4).Refusal);
        Assert.Equal(BeltRefusals.NoSuchSlot, rig.Drink(5).Refusal);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_drink_at_full_life_is_refused_and_uses_nothing_and_starts_no_cooldown()
    {
        var rig = new BeltRig();
        rig.Gear.Health = rig.Gear.Resupply.MaxHealthNow;

        var r = rig.Drink(1);

        Assert.Equal(BeltRefusals.Full, r.Refusal);
        Assert.NotNull(rig.BeltSlot(0));
        Assert.Equal((0, 0), rig.Belt.CooldownOf(BeltRig.Index));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_refusal_is_told_to_the_hero()
    {
        var rig = new BeltRig();
        rig.Clock.IsStunned = true;

        rig.Drink(1);

        Assert.Contains(rig.Messages.Calls, c => c.Member == "UserMessages.Send" && c.ArgAs<int>(2) == BeltRig.Index
                                                && c.ArgAs<string>(0).EndsWith(":notice", StringComparison.Ordinal));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_last_potion_of_a_slot_drunk_refills_it_from_the_backpack()
    {
        var rig = new BeltRig();
        int bp = rig.Give(BeltRig.Carried("health_small", 2));

        rig.Drink(1);

        Assert.Equal(2, rig.BeltSlot(0)!.Item.Count);
        Assert.False(rig.Held.Inventory.Backpack.ContainsKey(bp));
    }

    //-------------------------------------------------------------------------
    // The belt on the wire
    //-------------------------------------------------------------------------

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void After_a_drink_the_owner_is_sent_his_belt_with_the_count_and_the_cooldown_at_the_frames_end()
    {
        var rig = new BeltRig();
        rig.PlantBelt(0, "health_small", 3);
        rig.Drink(1);

        rig.Frame();

        var (player, belt) = Assert.Single(rig.Sent.Seen);
        var view = belt.View();
        Assert.Equal(BeltRig.Index, player);
        Assert.Equal(["health_small", "health_small", "mana_small", ""], view.Bases);
        Assert.Equal([2, 1, 1, 0], view.Counts);
        Assert.Equal("Sandvich Bite", view.Names[0]);
        Assert.Equal((rig.Clock.Tick + 200, 200), (view.ReadyTick, view.CooldownTicks));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_unchanged_belt_is_not_sent_again_but_a_level_change_sends_it_again()
    {
        var rig = new BeltRig();
        rig.Drink(1);
        rig.Frame();
        ((IClientLifecycle)rig.Sync).OnClientActive(BeltRig.Index);
        rig.Frame();
        int sameLevel = rig.Sent.Seen.Count;

        ((IGameLifecycle)rig.Sync).OnLevelShutdown();
        ((IClientLifecycle)rig.Sync).OnClientActive(BeltRig.Index);
        rig.Frame();

        Assert.Equal(2, sameLevel);
        Assert.Equal(3, rig.Sent.Seen.Count);
        Assert.Equal((0, 0), (rig.Sent.Seen[^1].Belt.ReadyTick, rig.Sent.Seen[^1].Belt.CooldownTicks));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_frame_with_no_change_sends_nothing()
    {
        var rig = new BeltRig();
        rig.Drink(1);
        rig.Frame();

        rig.Frame();

        Assert.Single(rig.Sent.Seen);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_spawn_sends_the_belt_again()
    {
        var rig = new BeltRig();
        rig.Drink(1);
        rig.Frame();

        ((IPlayerSpawnHook)rig.Sync).OnSpawned(HeroRig.PlayerOf(BeltRig.Index), 0);
        rig.Frame();

        Assert.Equal(2, rig.Sent.Seen.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_belts_bytes_go_to_the_owner_alone()
    {
        var rig = new BeltRig();
        var engine = new RecordingEngine();
        var channel = new BeltChannel(engine);

        channel.Send(BeltRig.Index, rig.Sync.BeltOf(BeltRig.Index)!.Value);

        var send = Assert.Single(engine.Calls, c => c.Member == "UserMessages.Send");
        Assert.EndsWith(":" + BeltMessage.Name, send.ArgAs<string>(0), StringComparison.Ordinal);
        Assert.Equal(BeltRig.Index, send.ArgAs<int>(2));
        Assert.InRange(send.ArgAs<int>(1), 1, 255);
    }
}
