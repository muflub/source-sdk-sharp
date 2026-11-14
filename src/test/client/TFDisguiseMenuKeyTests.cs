using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The spy's disguise menu's keys, through the adapter the game composes
/// and the hud walk the client mode's press asks (stock's
/// HudElementKeyInput, tf_hud_menu_spy_disguise.cpp:184-436): a slot
/// disguises its tile's remapped class as the shown team, the
/// disguiseteam binding flips the shown team, the tenth cancels by
/// lastinv, the kit stowed passes every key, and a redeploy re-arms the
/// enemy team (the spy_pda_reset arm, :613-624).
/// </summary>
public class TFDisguiseMenuKeyTests
{
    private const string Kit = "tf_weapon_pda_spy";
    private const string Revolver = "tf_weapon_revolver";

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

    private static (TFDisguiseMenuUi Ui, TFEurekaMenuTests.FakeArsenal Arsenal, List<string> Sent)
        Menu(string weapon = Kit, int team = 2)
    {
        var engine = TestServices.Fake().Returns("Hud.LocalPlayerIndex", 1);
        var arsenal = new TFEurekaMenuTests.FakeArsenal { Weapon = weapon };
        var sent = new List<string>();
        var ui = new TFDisguiseMenuUi(arsenal, engine, new NoLocalisation(),
                                      new ChatFeedTests.RecordingEngineClient(sent),
                                      new FakeLocalPlayer { Team = team });
        return (ui, arsenal, sent);
    }

    /// <summary>The client mode's walk over the one menu, the ask a
    /// keyboard's press makes (clientmode_tf.cpp:1543-1553).</summary>
    private static ClientKeyInput Walk(TFDisguiseMenuUi ui, List<string> sent)
    {
        var hooks = new Hooks<IHudMenuKeys>(new ServiceContainer().AddInstance<IHudMenuKeys>(ui),
                                            new RecordingLog());
        return new ClientKeyInput(new NoChat(), new Here(),
                                  new ChatFeedTests.RecordingEngineClient(sent), hudMenus: hooks);
    }

    [Fact]
    public void Each_slot_disguises_its_remaps_class_as_the_enemy()
    {
        var (ui, _, sent) = Menu();                           // a RED spy: the enemy shows
        var keys = Walk(ui, sent);
        Assert.Empty(sent);                                   // before: no press, no send

        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot1, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot2, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot3, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot4, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot5, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot6, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot7, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot8, false, 8));
        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot9, false, 8));

        int[] remap = [1, 3, 7, 4, 6, 9, 5, 2, 8];            // iRemapKeyToClass, :197-208
        Assert.Equal(remap.Select(c => $"disguise {c} -1"), sent);
    }

    [Fact]
    public void The_shown_team_is_the_enemy_whatever_the_colour()
    {
        var (ui, _, sent) = Menu(team: 3);                    // a BLU spy: RED shows
        Walk(ui, sent).KeyInput(true, ClientKeyBinding.Slot9, false, 8);
        Assert.Equal(["disguise 8 -1"], sent);                // the spy tile, the enemy team
    }

    [Fact]
    public void The_tenth_slot_cancels_by_lastinv()
    {
        var (ui, _, sent) = Menu();
        Assert.Equal(ClientKeyInput.Used, Walk(ui, sent).KeyInput(true, ClientKeyBinding.Slot10, false, 8));
        Assert.Equal(["lastinv"], sent);
    }

    [Fact]
    public void The_disguiseteam_binding_toggles_and_the_choice_names_the_own_side()
    {
        var (ui, _, sent) = Menu();
        var keys = Walk(ui, sent);
        Assert.False(ui.Toggled);                             // the arm: the enemy shows

        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.DisguiseTeam, false, 8));
        Assert.True(ui.Toggled);

        Assert.Equal(ClientKeyInput.Used, keys.KeyInput(true, ClientKeyBinding.Slot1, false, 8));
        Assert.Equal(["disguise 1 -2"], sent);                // the spy's own team now
    }

    [Fact]
    public void The_keys_pass_under_another_weapon()
    {
        var (ui, _, sent) = Menu(Revolver);
        Assert.False(ui.Key(ClientKeyBinding.Slot1));
        Assert.Equal(ClientKeyInput.NoDecision, Walk(ui, sent).KeyInput(true, ClientKeyBinding.Slot1, false, 8));
        Assert.Empty(sent);
    }

    [Fact]
    public void A_redeploy_arms_the_enemy_again()
    {
        var (ui, arsenal, _) = Menu();
        var keys = Walk(ui, []);
        keys.KeyInput(true, ClientKeyBinding.DisguiseTeam, false, 8);
        Assert.True(ui.Toggled);

        arsenal.Weapon = "";                                  // the kit stowed
        ui.ClientThink(0.016f);
        arsenal.Weapon = Kit;                                 // a redeploy
        ui.ClientThink(0.016f);

        Assert.False(ui.Toggled);                             // the arm again: the enemy shows
    }
}
