extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using OrderedHook = client::SourceSharp.Abi.IOrderedHook;
using Overlays = client::TF2.Client.Player.TFConditionOverlays;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using Orders = client::TF2.Client.Player.TFClientConditionOrders;
using Cond = client::TF2.Shared.TFCond;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The conditions' screen overlays, stock's local-player halves of
/// OnAdd/OnRemoveBurning, Invulnerable, Urine and Bleeding
/// (tf_player_shared.cpp:7308-7316, 6873-6882, 3442-3467, 3507-3519,
/// 3600-3607, 3629-3638, 4121-4131, 4141-4152) through the condition driver:
/// the name per condition, the team's invuln colour, the local-player guard,
/// and the removal's clear only when the standing overlay is its own.
/// </summary>
public sealed class TFConditionOverlaysTests
{
    private static readonly EntityHandle Player = new(0x0002_0004u);

    private const string Burn = Overlays.Burning;
    private const string Red = Overlays.InvulnerableRed;
    private const string Blue = Overlays.InvulnerableBlue;
    private const string Urine = Overlays.Urine;
    private const string Bleed = Overlays.Bleeding;

    [Fact]
    public void The_local_burn_wears_imcookin()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.Burning);

        Assert.Equal(Burn, rig.LastOverlaySet());
    }

    [Fact]
    public void An_off_screen_burn_wears_nothing()
    {
        var rig = New(localPlayer: 7);

        rig.Arm.OnAdded(Player, Cond.Burning);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Hud.ScreenOverlaySet");
    }

    [Fact]
    public void The_red_teams_invulnerable_wears_red()
    {
        var rig = New(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Invulnerable);

        Assert.Equal(Red, rig.LastOverlaySet());
    }

    [Fact]
    public void Every_other_team_wears_blue()
    {
        var rig = New(team: TFTeam.Blue);

        rig.Arm.OnAdded(Player, Cond.Invulnerable);

        Assert.Equal(Blue, rig.LastOverlaySet());
    }

    [Fact]
    public void A_player_with_no_resource_yet_wears_blue()
    {
        var rig = NoResource();

        rig.Arm.OnAdded(Player, Cond.Invulnerable);

        Assert.Equal(Blue, rig.LastOverlaySet());
    }

    [Fact]
    public void Jarate_wears_the_jarate_overlay()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.Urine);

        Assert.Equal(Urine, rig.LastOverlaySet());
    }

    [Fact]
    public void Bleeding_wears_the_bleed_overlay()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.Bleeding);

        Assert.Equal(Bleed, rig.LastOverlaySet());
    }

    [Fact]
    public void Conditions_without_an_overlay_reach_nothing()
    {
        var rig = New();

        rig.Arm.OnAdded(Player, Cond.Stealthed);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Hud.ScreenOverlaySet");
    }

    [Fact]
    public void A_burns_end_clears_its_own_overlay()
    {
        var rig = New();
        rig.Engine.Returns("Hud.ScreenOverlayGet", Burn);

        rig.Arm.OnRemoved(Player, Cond.Burning);

        var clear = Assert.Single(rig.Engine.Calls, c => c.Member == "Hud.ScreenOverlaySet");
        Assert.Equal(string.Empty, clear.ArgAs<string>(0));
    }

    [Fact]
    public void An_invulnerables_end_clears_either_colour()
    {
        var rigRed = New();
        rigRed.Engine.Returns("Hud.ScreenOverlayGet", Red);
        rigRed.Arm.OnRemoved(Player, Cond.Invulnerable);

        var rigBlue = New();
        rigBlue.Engine.Returns("Hud.ScreenOverlayGet", Blue);
        rigBlue.Arm.OnRemoved(Player, Cond.Invulnerable);

        Assert.DoesNotContain(rigRed.Engine.Calls, c => c.Member == "Hud.ScreenOverlaySet" && c.ArgAs<string>(0) != string.Empty);
        Assert.DoesNotContain(rigBlue.Engine.Calls, c => c.Member == "Hud.ScreenOverlaySet" && c.ArgAs<string>(0) != string.Empty);
    }

    [Fact]
    public void A_foreign_overlay_stands_after_the_clears_guard()
    {
        var rig = New();
        rig.Engine.Returns("Hud.ScreenOverlayGet", Bleed);

        rig.Arm.OnRemoved(Player, Cond.Burning);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Hud.ScreenOverlaySet");
    }

    [Fact]
    public void An_off_screen_removal_clears_nothing()
    {
        var rig = New(localPlayer: 7);
        rig.Engine.Returns("Hud.ScreenOverlayGet", Burn);

        rig.Arm.OnRemoved(Player, Cond.Burning);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Hud.ScreenOverlaySet");
    }

    [Fact]
    public void The_arm_walks_after_the_drips()
    {
        var rig = New();

        Assert.Equal(Orders.Overlays, ((OrderedHook)rig.Arm).HookOrder);
        Assert.True(((OrderedHook)rig.Arm).HookOrder > Orders.Drips);
    }

    /// <summary>The arm over a resource whose team is the red, the local player entity 4.</summary>
    private static Rig New(TFTeam team = TFTeam.Red, int localPlayer = 4)
    {
        var engine = new ClientEffects();
        engine.Returns("Hud.LocalPlayerIndex", localPlayer);
        engine.Returns("Hud.ScreenOverlaySet", true);

        var resources = new PlayerResources();
        var resource = new client::TF2.Client.Player.ClientTFPlayerResource(engine, resources);
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.Team[4] = (byte)team;

        return new Rig(engine, new Overlays(engine, resources));
    }

    private static Rig NoResource()
    {
        var engine = new ClientEffects();
        engine.Returns("Hud.LocalPlayerIndex", 4);
        engine.Returns("Hud.ScreenOverlaySet", true);

        return new Rig(engine, new Overlays(engine, new PlayerResources()));
    }

    private sealed record Rig(ClientEffects Engine, Overlays Arm)
    {
        public string? LastOverlaySet()
        {
            var sets = Engine.Calls.Where(c => c.Member == "Hud.ScreenOverlaySet").ToList();

            return sets.Count == 0 ? null : sets[^1].ArgAs<string>(0);
        }
    }
}
