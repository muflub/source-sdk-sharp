using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

using TF2.Client.Buildings;
using TF2.Client.Ui;
using TF2.Shared;
using TF2.Shared.Buildings;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Eureka Effect's teleport menu, through the adapter the game composes
/// (TFEurekaMenuUi, stock's CHudEurekaEffectTeleportMenu): the wrench's
/// +reload press arms the want and only under the eureka
/// (tf_weapon_wrench.cpp:221-239), the want dies with the wrench
/// (:101-104), a choice is spent before it is answered (:186-189) and the
/// unreachable exit is denied with a sound and no send (:200-204), and the
/// keys reach the menu only through the client mode's hud-menu walk
/// (clientmode_tf.cpp:1543-1553).
/// </summary>
public class TFEurekaMenuTests
{
    private const string Wrench = "tf_weapon_wrench";
    private const string Other = "tf_weapon_shotgun";

    internal sealed class FakeArsenal : ILocalArsenal
    {
        public string Weapon { get; set; } = Wrench;

        public int Buttons { get; set; }

        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            if (Weapon.Length == 0)
            {
                (name, clip, reserve) = ("", -1, -1);
                return false;
            }

            (name, clip, reserve) = (Weapon, 6, 24);
            return true;
        }

        public SourceSharp.Shared.WeaponNetState? ActiveWeaponState
        {
            get
            {
                if (Weapon.Length == 0)
                    return null;
                var state = new SourceSharp.Shared.WeaponNetState();
                state.BindLocal();                   // a client state needs its own buffer to write into
                state.LastButtons = Buttons;
                return state;
            }
        }
    }

    private sealed class FakeOwners : ITFWeaponOwners
    {
        public IAttributeProvider? Active { get; init; }

        public TFClass ClassOf(int playerIndex) => TFClass.Undefined;

        public TFConditions? ConditionsOf(int playerIndex) => null;

        public float CritMult(int playerIndex) => 1f;

        public bool WinsCrits(int playerIndex) => false;

        public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => null;

        public IAttributeProvider? ManagerOf(int playerIndex) => null;

        public bool OffGround(int playerIndex) => false;

        public bool EyesUnderwater(int playerIndex) => false;

        public bool DuckingOf(int playerIndex) => false;

        public IAttributeProvider? ActiveItemOf(int playerIndex) => Active;
    }

    private sealed class NoLocalisation : IUiLocalisation
    {
        public string? Find(string token) => null;
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

    private sealed class NoChat : IChatOpener
    {
        public void Open(bool teamOnly) { }
    }

    private sealed class Here : IObserverSource
    {
        public SpectatorMode Mode => SpectatorMode.None;

        public string MapName => "";
    }

    internal static IAttributeProvider EurekaItem()
    {
        // The eureka's own attribute, alt_fire_teleport_to_spawn, as the
        // wrench's CALL_ATTRIB_HOOK_INT would meet it: an additive word of
        // one (tf_weapon_wrench.cpp:224-225).
        var definition = new AttributeDefinition(5936, "teleport_to_spawn", "alt_fire_teleport_to_spawn",
                                                 AttributeDescriptionFormat.Additive, AttributeEffectType.None,
                                                 storedAsInteger: false, hidden: false, AttributeValueType.Default,
                                                 MergedKeys.Block("attributes"));
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(definition, 1f));
        return item;
    }

    internal static (TFEurekaMenuUi Ui, FakeArsenal Arsenal, RecordingEngine Engine, List<string> Sent,
                    TFClientBuildingResources Buildings) Menu(IAttributeProvider? held = null, bool player = false)
    {
        var engine = TestServices.Fake().Returns("Hud.LocalPlayerIndex", 1);
        var arsenal = new FakeArsenal();
        var buildings = new TFClientBuildingResources();
        var sent = new List<string>();
        var ui = new TFEurekaMenuUi(arsenal, engine, new NoLocalisation(),
                                    new ChatFeedTests.RecordingEngineClient(sent),
                                    new FakeOwners { Active = held },
                                    new OneStandalone(player ? new TestPlayer(engine) : null),
                                    engine, buildings,
                                    new NoPlayerData(), new AttributeHooks());
        return (ui, arsenal, engine, sent, buildings);
    }

    /// <summary>The client's prediction participant as the menu reads it at
    /// rest: no predicted state for anyone, the in-game answer before the
    /// local carrier arrives.</summary>
    internal sealed class NoPlayerData : ITFPlayerData
    {
        public TFPlayerNetState? Predicted(int playerIndex) => null;

        public TFPlayerNetState? Networked(int playerIndex) => null;
    }

    /// <summary>One think with the reload button where <paramref name="down"/> says.</summary>
    private static void Press(TFEurekaMenuUi ui, FakeArsenal arsenal, bool down)
    {
        arsenal.Buttons = down ? (int)InputButtons.Reload : 0;
        ui.ClientThink(1 / 60f);
    }

    /// <summary>The exit's row flags, the carrier words the availability read
    /// stands on: slot 1's teleporter exit is row 1*Kinds+Exit.</summary>
    private static void Carrier(RecordingEngine engine, TFClientBuildingResources buildings, byte flags)
    {
        var resource = new ClientTFBuildingResource(engine, buildings);
        resource.State.BindLocal();                  // a client state needs its own buffer to write into
        resource.State.Flags[1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Exit] = flags;
        buildings.Live = resource;
    }

    [Fact]
    public void The_reload_press_while_the_eureka_wrench_is_held_arms_the_menu()
    {
        var (ui, arsenal, _, _, _) = Menu(EurekaItem());
        Press(ui, arsenal, down: false);
        Assert.False(ui.WantsTeleport);          // before: the button up arms nothing

        Press(ui, arsenal, down: true);
        Assert.True(ui.WantsTeleport);           // the press's edge
    }

    [Fact]
    public void A_wrench_without_the_eureka_arms_nothing()
    {
        var (ui, arsenal, _, _, _) = Menu();
        Press(ui, arsenal, down: true);
        Assert.False(ui.WantsTeleport);
    }

    [Fact]
    public void The_want_dies_with_the_wrench()
    {
        var (ui, arsenal, _, _, _) = Menu(EurekaItem());
        Press(ui, arsenal, down: true);
        Assert.True(ui.WantsTeleport);

        arsenal.Buttons = 0;
        arsenal.Weapon = Other;
        ui.ClientThink(1 / 60f);
        Assert.False(ui.WantsTeleport);          // stock's ShouldDraw clear
    }

    [Fact]
    public void The_home_choice_sends_the_command_and_spends_the_want()
    {
        var (ui, arsenal, _, sent, _) = Menu(EurekaItem());
        Press(ui, arsenal, down: true);
        Assert.Empty(sent);                      // before: the arm sends nothing

        ui.Teleport(TFEurekaTeleportTargets.Home);
        Assert.Equal(["eureka_teleport 0"], sent);
        Assert.False(ui.WantsTeleport);          // spent, refused or not
    }

    [Fact]
    public void A_missing_exit_is_denied_with_a_sound_and_no_send()
    {
        var (ui, arsenal, engine, sent, _) = Menu(EurekaItem(), player: true);
        Press(ui, arsenal, down: true);

        ui.Teleport(TFEurekaTeleportTargets.TeleporterExit);
        Assert.Empty(sent);
        Assert.Contains(engine.Calls,
                        c => c.Member == "Sound.EntityEmitSound" && c.ArgAs<string>(1) == TFEurekaMenuUi.DenySound);
        Assert.False(ui.WantsTeleport);
    }

    [Fact]
    public void A_standing_exits_choice_sends_its_target()
    {
        var (ui, arsenal, engine, sent, buildings) = Menu(EurekaItem());
        Carrier(engine, buildings, TFBuildingResourceNetState.Present | TFBuildingResourceNetState.Built);
        Assert.True(ui.ExitStanding);            // the row the gate reads

        Press(ui, arsenal, down: true);
        ui.Teleport(TFEurekaTeleportTargets.TeleporterExit);
        Assert.Equal(["eureka_teleport 1"], sent);
    }

    [Theory]
    [InlineData(0, false)]                                    // no words at all: not there
    [InlineData(TFBuildingResourceNetState.Present, false)]   // placed, still building
    [InlineData(TFBuildingResourceNetState.Present | TFBuildingResourceNetState.Building, false)]
    [InlineData(TFBuildingResourceNetState.Present | TFBuildingResourceNetState.Carried, false)]
    [InlineData(TFBuildingResourceNetState.Built, false)]     // built words without the entity
    [InlineData(TFBuildingResourceNetState.Present | TFBuildingResourceNetState.Built, true)]
    [InlineData(TFBuildingResourceNetState.Present | TFBuildingResourceNetState.Built | TFBuildingResourceNetState.Carried, false)]
    public void The_exit_stands_only_when_placed_built_and_not_carried(int flags, bool standing)
    {
        var (ui, _, engine, _, buildings) = Menu();
        if (flags != 0)
            Carrier(engine, buildings, (byte)flags);
        Assert.Equal(standing, ui.ExitStanding);
    }

    [Fact]
    public void The_choice_keys_reach_the_menu_through_the_clients_hud_walk()
    {
        var (ui, arsenal, engine, sent, buildings) = Menu(EurekaItem());
        Carrier(engine, buildings, TFBuildingResourceNetState.Present | TFBuildingResourceNetState.Built);

        var hooks = new Hooks<IHudMenuKeys>(new ServiceContainer().AddInstance<IHudMenuKeys>(ui), new RecordingLog());
        var keys = new ClientKeyInput(new NoChat(), new Here(),
                                      new ChatFeedTests.RecordingEngineClient([]), hudMenus: hooks);
        Assert.Equal(ClientKeyInput.NoDecision, keys.KeyInput(true, ClientKeyBinding.Slot1, false, 8));  // nothing armed yet

        Press(ui, arsenal, down: true);
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot1, false, 8));
        Assert.Equal(["eureka_teleport 0"], sent);

        Press(ui, arsenal, down: false);
        Press(ui, arsenal, down: true);
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot2, false, 8));
        Assert.Equal(["eureka_teleport 0", "eureka_teleport 1"], sent);
    }

    [Fact]
    public void The_middle_slots_are_eaten_the_rest_pass_and_releases_are_never_asked()
    {
        var (ui, arsenal, _, sent, _) = Menu(EurekaItem());
        var hooks = new Hooks<IHudMenuKeys>(new ServiceContainer().AddInstance<IHudMenuKeys>(ui), new RecordingLog());
        var keys = new ClientKeyInput(new NoChat(), new Here(),
                                      new ChatFeedTests.RecordingEngineClient(sent), hudMenus: hooks);

        Press(ui, arsenal, down: true);

        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot5, false, 8));
        Assert.Equal(ClientKeyInput.NoDecision, keys.KeyInput(true, ClientKeyBinding.Attack, false, 8));
        Assert.Equal(ClientKeyInput.NoDecision, keys.KeyInput(false, ClientKeyBinding.Slot1, false, 8)); // the walk is presses only
        Assert.Empty(sent);
    }

    [Fact]
    public void The_tenth_slot_cancels_by_lastinv()
    {
        var (ui, arsenal, _, sent, _) = Menu(EurekaItem());
        var hooks = new Hooks<IHudMenuKeys>(new ServiceContainer().AddInstance<IHudMenuKeys>(ui), new RecordingLog());
        var keys = new ClientKeyInput(new NoChat(), new Here(),
                                      new ChatFeedTests.RecordingEngineClient(sent), hudMenus: hooks);

        Press(ui, arsenal, down: true);
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot10, false, 8));
        Assert.Equal(["lastinv"], sent);
        Assert.True(ui.WantsTeleport);           // the lastinv switch clears it, not the key: stock's own order
    }
}
