extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using Smoke = client::TF2.Client.Player.TFBlastJumpSmoke;
using Link = client::TF2.Client.EventLink;
using Cond = client::TF2.Shared.TFCond;
using Standalones = client::SourceSharp.Client.IClientStandalones;
using Entity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using PlayerEntity = client::SourceSharp.Client.Standalone.ClientSsPlayer;
using Resource = client::SourceSharp.Client.Standalone.ClientPlayerResource;
using JumpArgs = client::SourceSharp.Shared.BlastJumpArgs;
using RocketJump = client::SourceSharp.Shared.RocketJumpEvent;
using StickyJump = client::SourceSharp.Shared.StickyJumpEvent;
using DeathHolder = client::SourceSharp.Shared.PlayerDeathEvent;
using HurtHolder = client::SourceSharp.Shared.PlayerHurtEvent;
using TouchStartHolder = client::SourceSharp.Shared.ControlPointStartTouchEvent;
using TouchEndHolder = client::SourceSharp.Shared.ControlPointEndTouchEvent;
using WinPanelHolder = client::SourceSharp.Shared.TeamplayWinPanelEvent;
using RoundStartHolder = client::SourceSharp.Shared.TeamplayRoundStartEvent;

namespace TF2.Tests.Client;

/// <summary>
/// The blast jump's smoke, stock's <c>TFPlayerThink</c> arm
/// (tf_player.cpp:1509-1515) through the jump events: stock's
/// <c>rocketjump_smoke</c> on <c>foot_L</c> and <c>foot_R</c>, once per
/// jump — the event, not the condition ride — and no restart while it
/// rides, as <c>m_bCreatedRocketJumpParticles</c> holds (:1514, :8709).
/// The gate is stock's <c>RocketJumped() || StickyJumped()</c> (:1509),
/// which stock's <c>SetBlastJumpState</c> fires for exactly those bits
/// (:8679-8689), so an enemy-blasted lift — its condition with no event —
/// starts nothing.
/// </summary>
public sealed class TFBlastJumpSmokeTests
{
    private static readonly EntityHandle Player = new(0x0002_0004u);

    /// <summary>The jumper's userid, as the fake resource's slot 2 row says.</summary>
    private const int UserId = 7;

    [Fact]
    public void The_rocket_jump_smokes_rocketjump_smoke_on_both_feet()
    {
        var rig = New();

        rig.Rocket.Raise(new JumpArgs(UserId));

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
    public void The_sticky_jump_smokes_the_same_as_the_rocket_jump()
    {
        var rig = New();

        rig.Sticky.Raise(new JumpArgs(UserId));

        Assert.Equal(2, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
    }

    [Fact]
    public void The_feet_are_foot_L_and_foot_R()
    {
        var rig = New();
        rig.Engine.Returns("EntityRender.LookupAttachment", 7);

        rig.Rocket.Raise(new JumpArgs(UserId));

        var names = rig.Engine.Calls.Where(c => c.Member == "EntityRender.LookupAttachment")
            .Select(c => c.ArgAs<string>(1)).ToList();
        Assert.Equal(["foot_L", "foot_R"], names);

        // The looked-up attachment rides the follow, per foot.
        var follows = rig.Engine.Calls.Where(c => c.Member == "Effects.ParticleSystemFollowEntitySlot").ToList();
        Assert.Equal(7, follows[0].ArgAs<int>(3));
        Assert.Equal(7, follows[1].ArgAs<int>(3));
    }

    [Fact]
    public void The_enemy_blasted_lift_starts_no_smoke_as_stocks_gate_names_only_the_two_jumps()
    {
        var rig = New();

        // The enemy-blasted rise adds TF_COND_BLASTJUMPING and fires no event
        // (:8679-8689): the condition's ride alone must leave the feet dry.
        rig.Smoke.OnAdded(Player, Cond.Blastjumping);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void A_second_jump_while_it_smokes_starts_nothing()
    {
        var rig = New();

        rig.Rocket.Raise(new JumpArgs(UserId));
        rig.Rocket.Raise(new JumpArgs(UserId));
        rig.Rocket.Raise(new JumpArgs(UserId));

        Assert.Equal(2, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
    }

    [Fact]
    public void The_landing_frees_the_jump_and_the_next_one_smokes_again()
    {
        var rig = New();

        rig.Rocket.Raise(new JumpArgs(UserId));
        rig.Smoke.OnRemoved(Player, Cond.Blastjumping);
        var afterLanding = rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot");
        rig.Rocket.Raise(new JumpArgs(UserId));

        Assert.Equal(2, afterLanding);
        Assert.Equal(4, rig.Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartSlot"));
    }

    [Fact]
    public void The_landing_stops_nothing_as_stocks_clear_touches_no_particle()
    {
        var rig = New();

        rig.Rocket.Raise(new JumpArgs(UserId));
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
    public void A_userid_the_resource_does_not_have_starts_nothing()
    {
        var rig = New();

        rig.Rocket.Raise(new JumpArgs(99));

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
    }

    [Fact]
    public void A_refused_start_follows_nothing()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartSlot", false);
        engine.Returns("Effects.ParticleSystemFollowEntitySlot", true);
        engine.Returns("EntityRender.LookupAttachment", 0);
        var rocket = new RocketJump();
        var link = new Link(new DeathHolder(), new HurtHolder(), new TouchStartHolder(), new TouchEndHolder(),
                            new WinPanelHolder(), new RoundStartHolder(), rocket, new StickyJump());
        var smoke = new Smoke(engine, engine, link, new Ones());

        rocket.Raise(new JumpArgs(UserId));

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Effects.ParticleSystemFollowEntitySlot");
    }

    private static Rig New()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartSlot", true);
        engine.Returns("Effects.ParticleSystemFollowEntitySlot", true);
        engine.Returns("EntityRender.LookupAttachment", 0);
        var rocket = new RocketJump();
        var sticky = new StickyJump();
        var link = new Link(new DeathHolder(), new HurtHolder(), new TouchStartHolder(), new TouchEndHolder(),
                            new WinPanelHolder(), new RoundStartHolder(), rocket, sticky);
        return new Rig(engine, new Smoke(engine, engine, link, new Ones()), rocket, sticky);
    }

    private sealed record Rig(ClientEffects Engine, Smoke Smoke, RocketJump Rocket, StickyJump Sticky);

    /// <summary>The standalones' view: the player resource with slot 2
    /// connected under <see cref="UserId"/>, and the player entity there
    /// whose handle the smoke follows.</summary>
    private sealed class Ones : Standalones
    {
        private readonly Resource _resource = new(new ClientEffects());

        public Ones()
        {
            // The client's own buffer first, so the arrays hold (NetState.cs).
            _resource.State.BindLocal();
            _resource.State.SetRow(2, score: 0, deaths: 0, health: 0, UserId, team: 0,
                                   connected: true, alive: true, valid: true);
        }

        public IReadOnlyList<Entity> Live() => [_resource];

        public Entity? GetByIndex(int entityIndex)
            => entityIndex != 2 ? null : new PlayerEntity(new ClientEffects()) { Handle = Player };
    }
}
