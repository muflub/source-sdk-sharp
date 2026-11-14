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
}
