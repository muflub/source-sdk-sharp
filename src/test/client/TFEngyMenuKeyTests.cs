using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

using TF2.Client.Buildings;
using TF2.Client.Ui;
using TF2.Shared.Buildings;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engineer's build and destroy menus, through the adapters the game
/// composes and the keys the client mode's hud walk asks them (stock's
/// HudElementKeyInput, tf_hud_menu_engy_build.cpp:296-444 and
/// tf_hud_menu_engy_destroy.cpp:158-275): the slots send their
/// g_kEngyBuildings row while the menu's PDA is the held weapon, the tenth
/// cancels by lastinv, a bound fifth passes for the engine, and the destroy
/// sends only for a standing building — the mirror's word — with stock's
/// error sound otherwise.
/// </summary>
public class TFEngyMenuKeyTests
{
    private const string BuildPda = "tf_weapon_pda_engineer_build";
    private const string DestroyPda = "tf_weapon_pda_engineer_destroy";
    private const string Shotgun = "tf_weapon_shotgun";

    private sealed class NoLocalisation : IUiLocalisation
    {
        public string? Find(string token) => null;
    }

    private sealed class NoChat : IChatOpener
    {
        public void Open(bool teamOnly) { }
    }

    private sealed class Here : IObserverSource
    {
        public SpectatorMode Mode => SpectatorMode.None;

        public string MapName => "";
    }

    private sealed class TestPlayer(IEngineEntityRender render) : ClientStandaloneEntity(render)
    {
        public override string ClassName => "test_player";
    }

    private sealed class OneStandalone(TestPlayer? player) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => player is null ? [] : [player];

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => entityIndex == 1 ? player : null;
    }

    private static (TFBuildMenuUi Ui, TFEurekaMenuTests.FakeArsenal Arsenal, RecordingEngine Engine,
                    List<string> Sent) BuildMenu(string weapon = BuildPda)
    {
        var engine = TestServices.Fake().Returns("Hud.LocalPlayerIndex", 1);
        var arsenal = new TFEurekaMenuTests.FakeArsenal { Weapon = weapon };
        var sent = new List<string>();
        var ui = new TFBuildMenuUi(arsenal, engine, new NoLocalisation(),
                                   new ChatFeedTests.RecordingEngineClient(sent));
        return (ui, arsenal, engine, sent);
    }

    private static (TFDestroyMenuUi Ui, TFEurekaMenuTests.FakeArsenal Arsenal, RecordingEngine Engine,
                    List<string> Sent, TFClientBuildingResources Buildings) DestroyMenu(string weapon = DestroyPda)
    {
        var engine = TestServices.Fake().Returns("Hud.LocalPlayerIndex", 1);
        var arsenal = new TFEurekaMenuTests.FakeArsenal { Weapon = weapon };
        var buildings = new TFClientBuildingResources();
        var sent = new List<string>();
        var ui = new TFDestroyMenuUi(arsenal, engine, new NoLocalisation(),
                                     new ChatFeedTests.RecordingEngineClient(sent), buildings,
                                     new OneStandalone(new TestPlayer(engine)), engine);
        return (ui, arsenal, engine, sent, buildings);
    }

    /// <summary>A building row in the mirror, the words the destroy gate
    /// reads: the local slot's row for <paramref name="kind"/>.</summary>
    private static void Standing(RecordingEngine engine, TFClientBuildingResources buildings, int kind)
    {
        var resource = new ClientTFBuildingResource(engine, buildings);
        resource.State.BindLocal();                  // a client state needs its own buffer to write into
        resource.State.Flags[1 * TFBuildingResourceNetState.Kinds + kind] = TFBuildingResourceNetState.Present;
        buildings.Live = resource;
    }

    /// <summary>The client mode's walk over the one menu, the ask a
    /// keyboard's press makes (clientmode_tf.cpp:1543-1553).</summary>
    private static ClientKeyInput Walk(IHudMenuKeys menu, List<string> sent)
    {
        var hooks = new Hooks<IHudMenuKeys>(new ServiceContainer().AddInstance<IHudMenuKeys>(menu),
                                            new RecordingLog());
        return new ClientKeyInput(new NoChat(), new Here(),
                                  new ChatFeedTests.RecordingEngineClient(sent), hudMenus: hooks);
    }

    [Fact]
    public void The_slot_keys_send_each_buildings_command_through_the_hud_walk()
    {
        var (ui, _, _, sent) = BuildMenu();
        var keys = Walk(ui, sent);
        Assert.Empty(sent);                          // before: no press, no send

        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot1, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot2, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot3, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot4, false, 8));
        Assert.Equal(["build 2 0", "build 0 0", "build 1 0", "build 1 1"], sent);
    }

    [Fact]
    public void The_tenth_slot_cancels_the_build_by_lastinv()
    {
        var (ui, _, _, sent) = BuildMenu();
        Assert.Equal(ClientKeyInput.Used, Walk(ui, sent).KeyInput(true, ClientKeyBinding.Slot10, false, 8));
        Assert.Equal(["lastinv"], sent);
    }

    [Fact]
    public void A_bound_fifth_slot_passes_the_build_menu()
    {
        var (ui, _, _, sent) = BuildMenu();
        Assert.Equal(ClientKeyInput.NoDecision, Walk(ui, sent).KeyInput(true, ClientKeyBinding.Slot5, false, 8));
        Assert.Empty(sent);
    }

    [Fact]
    public void The_build_keys_pass_under_another_weapon()
    {
        var (ui, _, _, sent) = BuildMenu(Shotgun);
        Assert.False(ui.Key(ClientKeyBinding.Slot1));
        Assert.Equal(ClientKeyInput.NoDecision, Walk(ui, sent).KeyInput(true, ClientKeyBinding.Slot1, false, 8));
        Assert.Empty(sent);
    }

    [Fact]
    public void A_standing_buildings_slot_destroy_sends_its_command()
    {
        var (ui, _, engine, sent, buildings) = DestroyMenu();
        Standing(engine, buildings, TFBuildingResourceNetState.Sentry);

        Assert.True(ui.Key(ClientKeyBinding.Slot1));
        Assert.Equal(["destroy 2 0; lastinv"], sent);
    }

    [Fact]
    public void Each_destroy_slot_names_its_own_building()
    {
        var (ui, _, engine, sent, buildings) = DestroyMenu();
        Standing(engine, buildings, TFBuildingResourceNetState.Exit);

        Assert.True(ui.Key(ClientKeyBinding.Slot4));
        Assert.Equal(["destroy 1 1; lastinv"], sent);
    }

    [Fact]
    public void A_buildingless_slot_is_denied_with_a_sound_and_no_send()
    {
        var (ui, _, engine, sent, _) = DestroyMenu();
        Assert.True(ui.Key(ClientKeyBinding.Slot1));
        Assert.Empty(sent);
        Assert.Contains(engine.Calls,
                        c => c.Member == "Sound.EntityEmitSound" && c.ArgAs<string>(1) == TFDestroyMenuUi.DenySound);
    }

    [Fact]
    public void The_tenth_slot_cancels_the_destroy_by_lastinv()
    {
        var (ui, _, _, sent, _) = DestroyMenu();
        Assert.True(ui.Key(ClientKeyBinding.Slot10));
        Assert.Equal(["lastinv"], sent);
    }

    [Fact]
    public void The_destroy_keys_pass_under_another_weapon()
    {
        var (ui, _, _, sent, _) = DestroyMenu(Shotgun);
        Assert.False(ui.Key(ClientKeyBinding.Slot1));
        Assert.Equal(ClientKeyInput.NoDecision, Walk(ui, sent).KeyInput(true, ClientKeyBinding.Slot1, false, 8));
        Assert.Empty(sent);
    }
}
