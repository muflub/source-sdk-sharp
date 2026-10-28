using System.Reflection;

using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

namespace SourceSharp.Tests;

/// <summary>
/// The weapon-selection binds, the cancel key and the attack buttons, as
/// stock hooks them to CBaseHudWeaponSelection (weapon_selection.cpp's
/// HOOK_COMMANDs, KeyInput and ProcessInput): each reaches the selection.
/// </summary>
public class WeaponSelectionTests
{
    private static readonly string[] Binds =
    [
        "invnext", "invprev", "lastinv", "cancelselect",
        "slot0", "slot1", "slot2", "slot3", "slot4", "slot5", "slot6", "slot7", "slot8", "slot9", "slot10",
    ];

    private sealed class World : IWeaponSelectionPlayer, IWeaponSelectionActions, IWeaponSlots, IGameClock,
                                 IWeaponSelectionCommand
    {
        public bool Exists => true;
        public bool HudAllowed => true;
        public bool FrozenOrDead => false;
        public uint Carried => 0b111;
        public uint Empty => 0;
        public int Active { get; set; } = 1;
        public float Now => 10f;

        public int LastWeapons, Escapes;

        public void Select(int weaponType) { }
        public void LastWeapon() => LastWeapons++;
        public void Sound(string soundName) { }

        public int Count => 3;
        public int Slot(int t) => t;
        public int Position(int t) => 0;

        public void SendSelection(string line) { }
        public void Escape() => Escapes++;

        public WeaponSelectionHud Hud() => new(this, this, this, this, () => 0);
    }

    [Fact]
    public void Each_bind_name_is_declared_once_as_a_managed_command()
    {
        var declared = typeof(InvNextCommand).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<ConCommandAttribute>()?.Name)
            .Where(n => n is not null && Binds.Contains(n))
            .ToList();

        Assert.Equal(Binds.OrderBy(b => b), declared.OrderBy(b => b));
    }

    [Fact]
    public void A_slot_bind_opens_its_bucket()
    {
        var hud = new World().Hud();

        new Slot2Command(hud).Execute("");

        Assert.True(hud.Open);
        Assert.Equal(1, hud.Selected);
    }

    [Fact]
    public void Invnext_and_invprev_open_on_either_side_of_the_weapon_in_hand()
    {
        var world = new World();
        var next = world.Hud();
        var prev = world.Hud();

        new InvNextCommand(next).Execute("");
        new InvPrevCommand(prev).Execute("");

        Assert.True(next.Open && next.Selected == 2);
        Assert.True(prev.Open && prev.Selected == 0);
    }

    [Fact]
    public void Lastinv_asks_for_the_last_weapon()
    {
        var world = new World();

        new LastInvCommand(world.Hud()).Execute("");

        Assert.Equal(1, world.LastWeapons);
    }

    [Fact]
    public void Cancelselect_closes_an_open_selection()
    {
        var world = new World();
        var hud = world.Hud();
        new Slot1Command(hud).Execute("");

        new CancelSelectCommand(hud, world).Execute("");

        Assert.False(hud.Open);
        Assert.Equal(0, world.Escapes);
    }

    [Fact]
    public void Cancelselect_with_nothing_open_is_escape()
    {
        var world = new World();

        new CancelSelectCommand(world.Hud(), world).Execute("");

        Assert.Equal(1, world.Escapes);
    }

    [Fact]
    public void The_cancel_key_is_the_selections_while_it_is_open()
    {
        var hud = new World().Hud();
        var keys = new ClientKeyInput(new NoChat(), new NoObserver(), new ChatFeedTests.RecordingEngineClient([]), hud);
        Assert.Equal(ClientKeyInput.NoDecision, keys.KeyInput(true, ClientKeyBinding.CancelSelect, false, 8));

        new Slot1Command(hud).Execute("");

        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.CancelSelect, false, 8));
        Assert.False(hud.Open);
    }

    [Fact]
    public void The_cancelselect_binding_is_named()
        => Assert.Equal(ClientKeyBinding.CancelSelect, UiClientBinding.BindingOf("cancelselect"u8));

    private sealed class NoChat : IChatOpener
    {
        public void Open(bool teamOnly) { }
    }

    private sealed class NoObserver : IObserverSource
    {
        public SpectatorMode Mode => SpectatorMode.None;
        public string MapName => "";
    }

    private sealed class TakesAttack(bool takes) : IHudProcessInput
    {
        public int Asked;

        public bool AttackPressed()
        {
            Asked++;
            return takes;
        }
    }

    private static int Command(ClientInput input)
    {
        var cmd = new InputCommand();
        input.CreateMove(1, 0.015f, true, ref cmd);
        return cmd.Buttons;
    }

    [Fact]
    public void Attack_the_selection_takes_is_swallowed_until_pressed_again()
    {
        var hud = new TakesAttack(takes: true);
        var input = new ClientInput(new FakeInputHost(), new FakeInputDevices(), new InputCvars(), hud: hud);
        input.Buttons.Attack.KeyDown("1");

        Assert.Equal(0, Command(input) & (int)InputButtons.Attack);
        Assert.Equal(1, hud.Asked);
        Assert.Equal(0, Command(input) & (int)InputButtons.Attack);
    }

    [Fact]
    public void Attack_the_selection_leaves_is_sent()
    {
        var hud = new TakesAttack(takes: false);
        var input = new ClientInput(new FakeInputHost(), new FakeInputDevices(), new InputCvars(), hud: hud);
        input.Buttons.Attack.KeyDown("1");

        Assert.NotEqual(0, Command(input) & (int)InputButtons.Attack);
    }

    [Fact]
    public void No_attack_asks_nothing()
    {
        var hud = new TakesAttack(takes: true);
        var input = new ClientInput(new FakeInputHost(), new FakeInputDevices(), new InputCvars(), hud: hud);

        Command(input);

        Assert.Equal(0, hud.Asked);
    }
}
