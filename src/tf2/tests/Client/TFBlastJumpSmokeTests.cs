extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using Smoke = client::TF2.Client.Player.TFBlastJumpSmoke;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using Cond = client::TF2.Shared.TFCond;

namespace TF2.Tests.Client;

/// <summary>
/// The blast jump's smoke, stock's <c>TFPlayerThink</c> arm
/// (tf_player.cpp:1509-1515) through the condition driver: stock's
/// <c>rocketjump_smoke</c> on <c>foot_L</c> and <c>foot_R</c>, once per
/// jump — the cond's edge, not its every tick — and no restart while it
/// rides, as <c>m_bCreatedRocketJumpParticles</c> holds (:1514, :8709).
/// </summary>
public sealed class TFBlastJumpSmokeTests
{
    private static readonly EntityHandle Player = new(0x0002_0004u);

    [Fact]
    public void The_jump_smokes_rocketjump_smoke_on_both_feet()
    {
        var rig = New();

        rig.Smoke.OnAdded(Player, Cond.Blastjumping);

        var starts = rig.Engine.Calls.Where(c => c.Member == "Effects.ParticleSystemStartSlot").ToList();
        Assert.Equal(2, starts.Count);
        Assert.Equal(Player, starts[0].ArgAs<EntityHandle>(0));
        Assert.Equal("rocketjump_smoke", starts[0].ArgAs<string>(1));
        Assert.Equal(4, starts[0].ArgAs<int>(2));
        Assert.Equal(5, starts[1].ArgAs<int>(2));

        // Each foot's system follows its attachment, at the system's first point.
        var follows = rig.Engine.Calls.Where(c => c.Member == "Effects.ParticleSystemFollowEntitySlot").ToList();
        Assert.Equal(2, follows.Count);
        Assert.Equal(Player, follows[0].ArgAs<EntityHandle>(0));
        Assert.Equal(Player, follows[0].ArgAs<EntityHandle>(2));
        Assert.Equal(4, follows[0].ArgAs<int>(4));
        Assert.Equal(5, follows[1].ArgAs<int>(4));
        Assert.Equal(0, follows[0].ArgAs<int>(1));
    }

    [Fact]
    public void The_feet_are_foot_L_and_foot_R()
    {
        var rig = New();
        rig.Engine.Returns("EntityRender.LookupAttachment", 7);

        rig.Smoke.OnAdded(Player, Cond.Blastjumping);

        var names = rig.Engine.Calls.Where(c => c.Member == "EntityRender.LookupAttachment")
            .Select(c => c.ArgAs<string>(1)).ToList();
        Assert.Equal(["foot_L", "foot_R"], names);

        // The looked-up attachment rides the follow, per foot.
        var follows = rig.Engine.Calls.Where(c => c.Member == "Effects.ParticleSystemFollowEntitySlot").ToList();
        Assert.Equal(7, follows[0].ArgAs<int>(3));
        Assert.Equal(7, follows[1].ArgAs<int>(3));
    }

    [Fact]
    public void A_second_add_while_it_smokes_starts_nothing()
    {
        var rig = New();

        rig.Smoke.OnAdded(Player, Cond.Blastjumping);
        rig.Smoke.OnAdded(Player, Cond.Blastjumping);
        rig.Smoke.OnAdded(Player, Cond.Blastjumping);

        Assert.Equal(2, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
    }

    [Fact]
    public void The_landing_frees_the_jump_and_the_next_one_smokes_again()
    {
        var rig = New();

        rig.Smoke.OnAdded(Player, Cond.Blastjumping);
        rig.Smoke.OnRemoved(Player, Cond.Blastjumping);
        var afterLanding = rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot");
        rig.Smoke.OnAdded(Player, Cond.Blastjumping);

        Assert.Equal(2, afterLanding);
        Assert.Equal(4, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
    }

    [Fact]
    public void The_landing_stops_nothing_as_stocks_clear_touches_no_particle()
    {
        var rig = New();

        rig.Smoke.OnAdded(Player, Cond.Blastjumping);
        rig.Smoke.OnRemoved(Player, Cond.Blastjumping);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystemStop"));
    }

    [Fact]
    public void Other_conditions_and_a_landing_without_a_jump_reach_no_smoke()
    {
        var rig = New();

        rig.Smoke.OnAdded(Player, Cond.Burning);
        rig.Smoke.OnAdded(Player, Cond.Critboosted);
        rig.Smoke.OnRemoved(Player, Cond.Blastjumping);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void A_refused_start_follows_nothing()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartSlot", false);
        engine.Returns("Effects.ParticleSystemFollowEntitySlot", true);
        engine.Returns("EntityRender.LookupAttachment", 0);
        var smoke = new Smoke(engine, engine);

        smoke.OnAdded(Player, Cond.Blastjumping);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Effects.ParticleSystemFollowEntitySlot");
    }

    private static Rig New()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartSlot", true);
        engine.Returns("Effects.ParticleSystemFollowEntitySlot", true);
        engine.Returns("EntityRender.LookupAttachment", 0);
        return new Rig(engine, new Smoke(engine, engine));
    }

    private sealed record Rig(ClientEffects Engine, Smoke Smoke);
}
