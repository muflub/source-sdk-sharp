extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using Drips = client::TF2.Client.Player.TFConditionDrips;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using Cond = client::TF2.Shared.TFCond;

namespace TF2.Tests.Client;

/// <summary>
/// The jarate's and the milk's drips, stock's <c>OnAddUrine</c> and
/// <c>OnAddMadMilk</c> client halves (tf_player_shared.cpp:3582-3612,
/// 3779-3841) through the condition driver: each cond's name and slot, the
/// guard, and the removal's stop.
/// </summary>
public sealed class TFConditionDripsTests
{
    private static readonly EntityHandle Player = new(0x0002_0004u);

    [Fact]
    public void The_urine_wears_peejar_drips_in_the_third_slot()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.Urine);

        var start = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStartSlot");
        Assert.Equal(Player, start.ArgAs<EntityHandle>(0));
        Assert.Equal("peejar_drips", start.ArgAs<string>(1));
        Assert.Equal(2, start.ArgAs<int>(2));
    }

    [Fact]
    public void The_milk_wears_peejar_drips_milk_in_the_fourth_slot()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.MadMilk);

        var start = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStartSlot");
        Assert.Equal("peejar_drips_milk", start.ArgAs<string>(1));
        Assert.Equal(3, start.ArgAs<int>(2));
    }

    [Fact]
    public void The_two_drips_run_beside_each_other()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.Urine);
        rig.Drips.OnAdded(Player, Cond.MadMilk);

        Assert.Equal(2, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
    }

    [Fact]
    public void Other_conditions_reach_no_drip()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.Burning);
        rig.Drips.OnAdded(Player, Cond.Critboosted);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void A_second_add_for_a_dripping_player_starts_nothing()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.Urine);
        rig.Drips.OnAdded(Player, Cond.Urine);
        rig.Drips.OnAdded(Player, Cond.MadMilk);
        rig.Drips.OnAdded(Player, Cond.MadMilk);

        Assert.Equal(2, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
    }

    [Fact]
    public void The_urines_removal_stops_its_slot()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.Urine);
        rig.Drips.OnRemoved(Player, Cond.Urine);

        var stop = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStopSlot");
        Assert.Equal(Player, stop.ArgAs<EntityHandle>(0));
        Assert.Equal(2, stop.ArgAs<int>(1));
    }

    [Fact]
    public void The_milks_removal_stops_its_slot()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.MadMilk);
        rig.Drips.OnRemoved(Player, Cond.MadMilk);

        var stop = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStopSlot");
        Assert.Equal(3, stop.ArgAs<int>(1));
    }

    [Fact]
    public void A_removal_without_a_drip_stops_nothing()
    {
        var rig = New();

        rig.Drips.OnRemoved(Player, Cond.Urine);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void One_drips_removal_leaves_the_other_running()
    {
        var rig = New();

        rig.Drips.OnAdded(Player, Cond.Urine);
        rig.Drips.OnAdded(Player, Cond.MadMilk);
        rig.Drips.OnRemoved(Player, Cond.Urine);
        rig.Drips.OnAdded(Player, Cond.Urine);

        Assert.Equal(3, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
        Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStopSlot");
    }

    /// <summary>The arm over an engine whose starts succeed.</summary>
    private static Rig New()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartSlot", true);

        return new Rig(engine, new Drips(engine));
    }

    private sealed record Rig(ClientEffects Engine, Drips Drips);
}
