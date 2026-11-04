extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using OrderedHook = client::SourceSharp.Abi.IOrderedHook;
using Glows = client::TF2.Client.Player.TFPlayerGlows;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using Orders = client::TF2.Client.Player.TFClientConditionOrders;
using Cond = client::TF2.Shared.TFCond;
using TFTeam = client::TF2.Shared.TFTeam;
using GlowRegistry = client::SourceSharp.Client.Render.GlowObjectManager;

namespace TF2.Tests.Client;

/// <summary>
/// The teammate glows under <c>TF_COND_TEAM_GLOWS</c>, stock's
/// <c>C_TFPlayer::UpdateGlowEffect</c> teammate halves
/// (<c>c_tf_player.cpp:11349</c>, the base gate
/// <c>c_basecombatcharacter.cpp:136-152</c>) through the condition driver:
/// the add registers the player's glow in the team colour with occluded on,
/// the removal takes it out, and neither touches another condition's entity.
/// </summary>
public sealed class TFPlayerGlowsTests
{
    private static readonly EntityHandle Player = new(0x0002_0004u);
    private static readonly EntityHandle Other = new(0x0002_0005u);

    [Fact]
    public void TeamGlows_puts_the_teammate_in_the_glow_registry()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.TeamGlows);

        Assert.True(rig.Glow.HasGlowEffect(Player));
    }

    [Fact]
    public void The_glow_shows_through_occlusion_only()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.TeamGlows);

        Assert.True(rig.Glow.IsRenderingWhenOccluded(0));
        Assert.False(rig.Glow.IsRenderingWhenUnoccluded(0));
    }

    [Fact]
    public void A_red_teammate_glows_in_the_red_colour()
    {
        var rig = New(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.TeamGlows);

        var color = rig.Glow.GetColor(0);
        Assert.Equal(0.74f, color.X);
        Assert.Equal(0.23f, color.Y);
        Assert.Equal(0.23f, color.Z);
    }

    [Fact]
    public void A_blue_teammate_glows_in_the_blue_colour()
    {
        var rig = New(team: TFTeam.Blue);

        rig.Arm.OnAdded(Player, Cond.TeamGlows);

        var color = rig.Glow.GetColor(0);
        Assert.Equal(0.49f, color.X);
        Assert.Equal(0.66f, color.Y);
        Assert.Equal(0.77f, color.Z);
    }

    [Fact]
    public void A_teammate_with_no_resource_yet_glows_grey()
    {
        var rig = NoResource();

        rig.Arm.OnAdded(Player, Cond.TeamGlows);

        var color = rig.Glow.GetColor(0);
        Assert.Equal(0.76f, color.X);
        Assert.Equal(0.76f, color.Y);
        Assert.Equal(0.76f, color.Z);
    }

    [Fact]
    public void TeamGlows_off_takes_the_teammate_out_again()
    {
        var rig = New();
        rig.Arm.OnAdded(Player, Cond.TeamGlows);

        rig.Arm.OnRemoved(Player, Cond.TeamGlows);

        Assert.False(rig.Glow.HasGlowEffect(Player));
    }

    [Fact]
    public void Each_teammate_holds_a_slot_of_its_own()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.TeamGlows);
        rig.Arm.OnAdded(Other, Cond.TeamGlows);
        Assert.Equal(2, rig.Glow.Count);

        rig.Arm.OnRemoved(Player, Cond.TeamGlows);

        Assert.False(rig.Glow.HasGlowEffect(Player));
        Assert.True(rig.Glow.HasGlowEffect(Other));
    }

    [Fact]
    public void Other_conditions_leave_the_glow_alone()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.Burning);

        Assert.Equal(0, rig.Glow.Count);
        Assert.False(rig.Glow.HasGlowEffect(Player));
    }

    [Fact]
    public void A_removal_without_an_add_registers_nothing()
    {
        var rig = New();

        rig.Arm.OnRemoved(Player, Cond.TeamGlows);

        Assert.Equal(0, rig.Glow.Count);
    }

    [Fact]
    public void The_arm_walks_after_the_overlays()
    {
        var rig = New();

        Assert.Equal(Orders.TeamGlows, ((OrderedHook)rig.Arm).HookOrder);
        Assert.True(((OrderedHook)rig.Arm).HookOrder > Orders.Overlays);
    }

    /// <summary>The arm over a registry and a resource whose team four is the given one.</summary>
    private static Rig New(TFTeam team = TFTeam.Red)
    {
        var engine = new ClientEffects();
        engine.Returns("Hud.LocalPlayerIndex", 4);

        var resources = new PlayerResources();
        var resource = new client::TF2.Client.Player.ClientTFPlayerResource(engine, resources);
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.Team[4] = (byte)team;
        resource.State.Team[5] = (byte)TFTeam.Blue;

        var glow = new GlowRegistry(_ => false, DeclaredClientConVars.Fresh<client::SourceSharp.Client.GlowOutlineEffectEnableConVar>(), _ => 7);

        return new Rig(glow, new Glows(glow, resources));
    }

    private static Rig NoResource()
    {
        var engine = new ClientEffects();
        engine.Returns("Hud.LocalPlayerIndex", 4);

        var glow = new GlowRegistry(_ => false, DeclaredClientConVars.Fresh<client::SourceSharp.Client.GlowOutlineEffectEnableConVar>(), _ => 7);

        return new Rig(glow, new Glows(glow, new PlayerResources()));
    }

    private sealed record Rig(GlowRegistry Glow, Glows Arm);
}
