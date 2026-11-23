extern alias client;

using System.Reflection;

using Xunit;

using ClientHooks = client::SourceSharp.Abi.IHooks<client::TF2.Client.Ui.ITFMapInfoGate>;
using ClientSideServiceAttribute = client::SourceSharp.Abi.SideServiceAttribute;
using Gate = client::TF2.Client.Ui.ITFMapInfoGate;
using HeroMapInfoGate = client::TF2.Descent.Client.Ui.HeroMapInfoGate;
using Nav = client::TF2.Client.Ui.ITFMenuNavigation;
using Side = SourceSharp.Abi.Side;
using TextWindow = client::TF2.Client.Ui.TFTextWindowUi;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// Closing the message of the day leaves a hero in the level (user
/// 2026-11-15: "there is a menu that pops up after the motd"): TF2's text
/// window's OK raises no map info menu, and so no team menu after it, in
/// TF2 Descent.
/// </summary>
public sealed class HeroMapInfoGateTests
{
    [Fact]
    public void The_text_windows_okay_raises_no_map_info_for_a_hero()
    {
        var nav = new Recorder();
        var window = new TextWindow(nav, new Hooks(new HeroMapInfoGate()));

        window.Okay();

        Assert.Equal(["tf2textwindow"], nav.Closed);
        Assert.Empty(nav.Panels);
        Assert.Empty(nav.Opened);
    }

    [Fact]
    public void The_gate_is_a_client_service_in_the_text_windows_chain()
    {
        var mark = Assert.Single(typeof(HeroMapInfoGate).GetCustomAttributes<ClientSideServiceAttribute>());

        Assert.Equal(Side.Client, mark.Side);
        Assert.True(typeof(Gate).IsAssignableFrom(typeof(HeroMapInfoGate)));
    }

    private sealed class Hooks(params Gate[] all) : ClientHooks
    {
        public IReadOnlyList<Gate> All => all;

        public void Each(string what, Action<Gate> call)
        {
            foreach (Gate gate in all)
                call(gate);
        }
    }

    private sealed class Recorder : Nav
    {
        public readonly List<string> Opened = [], Panels = [], Closed = [];

        public bool Has(string screen) => true;

        public void Open(string screen) => Opened.Add(screen);

        public void OpenPanel(string panel) => Panels.Add(panel);

        public void Close(string screen) => Closed.Add(screen);
    }
}
