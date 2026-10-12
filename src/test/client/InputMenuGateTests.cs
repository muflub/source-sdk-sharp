using SourceSharp.Client.Input;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Tests.Ui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One input event is acted on once (lane c6-input, with plan_client_ui U3's
/// menu layer): while the UI's menu root holds the mouse and keyboard
/// (UiSetMenuInput), the game's input leaves a key press's binding unrun and
/// the mouse's delta unapplied; a key RELEASE still goes through, so a key held
/// before the menu opened cannot stick.
/// </summary>
public class InputMenuGateTests
{
    private const int KeyW = 33;   // any key: the gate does not look at which

    [Fact]
    public void APressWhileTheMenuHoldsTheInputRunsNoBinding()
    {
        var rig = new InputRig();
        Assert.Equal(-1, rig.Input.KeyEvent(1, KeyW, "+forward"));   // open: the client mode decides, then the engine binds

        rig.Menu.MenuHasInput = true;
        Assert.Equal(0, rig.Input.KeyEvent(1, KeyW, "+forward"));    // the menu's: swallowed
    }

    [Fact]
    public void AReleaseWhileTheMenuHoldsTheInputStillGoesThrough()
    {
        var rig = new InputRig();
        rig.Menu.MenuHasInput = true;
        Assert.Equal(-1, rig.Input.KeyEvent(0, KeyW, "+forward"));
    }

    [Fact]
    public void TheMouseDoesNotTurnTheViewWhileTheMenuHoldsIt()
    {
        var rig = new InputRig();
        rig.Menu.MenuHasInput = true;
        rig.Devices.RawX = 40;
        InputCommand held = rig.Move();
        Assert.Equal(0.0f, rig.Host.View.Yaw);
        Assert.Equal(0, held.MouseDx);

        // The positive arm: the same delta, menu closed, turns the view.
        rig.Menu.MenuHasInput = false;
        rig.Devices.RawX = 40;
        InputCommand open = rig.Move(2);
        Assert.Equal(120, open.MouseDx);                     // 40 counts at sensitivity 3
        Assert.NotEqual(0.0f, rig.Host.View.Yaw);
    }

    [Fact]
    public void TheGateIsTheMenuRootsOwnFlag()
    {
        // UiMenuInputGate reads UiHost.MenuInput: what the menu layer asked the
        // native root for (UiSetMenuInput), the same flag the ui route reports.
        var surface = new UiFidelityTests.RecordingSurface();
        var host0 = UiHostKit.Build(surface, []);
        var m = host0.Menus;
        UiScreen[] menus = [new TeamMenu(m.Teams, new TeamCard(), m.Teams)];
        UiHost host = UiHostKit.Build(surface, [], menus, m);
        var gate = new UiMenuInputGate(() => host);

        Assert.False(gate.MenuHasInput);
        host.ViewportPanel("team", true);
        host.Paint(1, 1920, 1080);
        Assert.Equal([(true, true)], surface.MenuInputs);   // the root took both
        Assert.True(gate.MenuHasInput);

        Assert.False(new UiMenuInputGate(() => null).MenuHasInput);
    }
}
