using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The key-press route (<c>cs_key</c>): the two asks the shell makes of a key,
/// in its order — the input half, then the client mode's walk on its
/// no-decision — so a press reaches the hud menus the way a keyboard's does.
/// </summary>
public class ClientKeyInjectTests
{
    private sealed class NoChat : IChatOpener
    {
        public void Open(bool teamOnly) { }
    }

    private sealed class Here : IObserverSource
    {
        public SpectatorMode Mode => SpectatorMode.None;

        public string MapName => "";
    }

    private sealed class NoGlobals : IEngineEnvironment
    {
        public nint GetInterface(string name) => 0;

        public Globals Globals => default;    // not handed yet: the route answers a one-player game
    }

    /// <summary>The menu root's key events, as the route's leg forwards them.</summary>
    private sealed class RecordingUi : IUiClientEvents
    {
        public List<(int Kind, int Code)> Keys = [];

        public void Paint(int layer, int screenWidth, int screenHeight) { }

        public void ScreenSizeChanged(int oldWidth, int oldHeight, int newWidth, int newHeight) { }

        public void Input(int kind, int x, int y, int code, int character, float delta) => Keys.Add((kind, code));

        public bool ViewportPanel(string name, bool show) => false;
    }

    [Fact]
    public void The_route_presses_the_key_through_the_input_half_and_the_client_modes_walk()
    {
        var (ui, arsenal, _, sent, _) = TFEurekaMenuTests.Menu(TFEurekaMenuTests.EurekaItem());
        var rig = new InputRig();
        var service = new ClientInputService(rig.Input, null, () => true);
        var hooks = new Hooks<IHudMenuKeys>(new ServiceContainer().AddInstance<IHudMenuKeys>(ui), new RecordingLog());
        var keys = new ClientKeyInput(new NoChat(), new Here(),
                                      new ChatFeedTests.RecordingEngineClient(sent), hudMenus: hooks);
        var log = new RecordingLog();
        var inject = new ClientKeyInject(service, keys, new ServiceContainer(), new NoGlobals(), log);

        inject.Execute("slot1");   // nothing armed: the walk refuses, the bind stays the engine's
        Assert.Empty(sent);
        Assert.Contains("engine runs it", log.Text);

        arsenal.Buttons = (int)InputButtons.Reload;   // the +reload press arms the want, the wrench's own path
        ui.ClientThink(1 / 60f);
        Assert.True(ui.WantsTeleport);

        inject.Execute("slot1");
        Assert.Equal(["eureka_teleport 0"], sent);
        Assert.False(ui.WantsTeleport);   // the choice spent it, the menu's own order
    }

    [Fact]
    public void The_menu_layer_s_claim_presses_the_menu_layer_too()
    {
        var rig = new InputRig();
        var service = new ClientInputService(rig.Input, null, () => true);
        var screen = new RecordingUi();
        var log = new RecordingLog();
        var inject = new ClientKeyInject(service, new ClientKeyInput(new NoChat(), new Here(),
                                                                     new ChatFeedTests.RecordingEngineClient([])),
                                         new ServiceContainer().AddInstance<IUiClientEvents>(screen), new NoGlobals(), log);

        Assert.Empty(screen.Keys);        // before: no press, no forward

        rig.Menu.MenuHasInput = true;     // the radial's up: the menu layer holds the keyboard
        inject.Execute("slot1");

        Assert.Equal([( (int)UiInputKind.Key, (int)UiKey.Key0 + 1 )], screen.Keys);
    }

    [Fact]
    public void The_menu_layer_s_claim_ends_the_walk_s_ask()
    {
        var (menu, arsenal, _, sent, _) = TFEurekaMenuTests.Menu(TFEurekaMenuTests.EurekaItem());
        var rig = new InputRig();
        var service = new ClientInputService(rig.Input, null, () => true);
        var hooks = new Hooks<IHudMenuKeys>(new ServiceContainer().AddInstance<IHudMenuKeys>(menu), new RecordingLog());
        var screen = new RecordingUi();
        var log = new RecordingLog();
        var inject = new ClientKeyInject(service, new ClientKeyInput(new NoChat(), new Here(),
                                                                     new ChatFeedTests.RecordingEngineClient(sent),
                                                                     hudMenus: hooks),
                                         new ServiceContainer().AddInstance<IUiClientEvents>(screen), new NoGlobals(), log);

        arsenal.Buttons = (int)InputButtons.Reload;   // the teleport menu armed and wanting
        menu.ClientThink(1 / 60f);

        rig.Menu.MenuHasInput = true;
        inject.Execute("slot1");

        Assert.Equal([( (int)UiInputKind.Key, (int)UiKey.Key0 + 1 )], screen.Keys);
        Assert.Empty(sent);               // the walk shadowed: the menu layer's claim is final
    }

    [Fact]
    public void The_tenth_slot_s_claim_is_the_number_row_s_zero()
    {
        var rig = new InputRig();
        var service = new ClientInputService(rig.Input, null, () => true);
        var screen = new RecordingUi();
        var inject = new ClientKeyInject(service, new ClientKeyInput(new NoChat(), new Here(),
                                                                     new ChatFeedTests.RecordingEngineClient([])),
                                         new ServiceContainer().AddInstance<IUiClientEvents>(screen), new NoGlobals(), new RecordingLog());

        rig.Menu.MenuHasInput = true;
        inject.Execute("slot10");

        Assert.Equal([( (int)UiInputKind.Key, (int)UiKey.Key0 )], screen.Keys);
    }
}
