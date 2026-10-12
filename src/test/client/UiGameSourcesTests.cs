using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The game's side of the HUD's data interfaces (plan_client_ui U2): what the
/// core hands the toolkit's panels, read from the HUD's state once a frame.
/// </summary>
public class UiGameSourcesTests
{
    private static string Bind(string command) => command switch
    {
        "+use" => "E",
        "+reload" => "R",
        _ => string.Empty,
    };

    [Fact]
    public void A_hint_s_first_command_is_its_key_and_leaves_the_text()
    {
        Assert.Equal("Use the charger", HudHints.SplitKey("%+use% Use the charger", Bind, out string key));
        Assert.Equal("E", key);
    }

    [Fact]
    public void A_hint_with_no_command_has_no_key()
    {
        Assert.Equal("Look around", HudHints.SplitKey("Look around", Bind, out string key));
        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void An_unbound_command_is_the_not_bound_key_and_later_commands_resolve_in_place()
    {
        Assert.Equal("then R", HudHints.SplitKey("%+jump% then %+reload%", Bind, out string key));
        Assert.Equal(HudHints.Unbound, key);
    }

    private static readonly HudMessageArgs Message = new(
        Channel: 1, X: -1f, Y: 0.3f, Color: new Color32(255, 128, 0, 255), Color2: default, Effect: 0,
        FadeIn: 0f, FadeOut: 1f, HoldTime: 2f, FxTime: 0f, Text: "first line\nsecond line");

    [Fact]
    public void A_shown_message_is_one_line_of_the_map_text_until_its_fade_ends()
    {
        var messages = new HudMessages(new RecordingLog(), Fake(), Fake());
        messages.Show(Message, now: 10f);

        messages.Refresh(11f);
        Assert.Equal(1, messages.MessageCount);
        var m = messages.Message(0);
        Assert.Equal("first line\nsecond line", m.Text);
        Assert.Equal((-1f, 0.3f, 1f), (m.X, m.Y, m.Alpha));
        Assert.Equal(new Client.Ui.UiColor(255, 128, 0, 255), m.Color);

        messages.Refresh(12.5f);
        Assert.Equal(0.5f, messages.Message(0).Alpha, 3);

        messages.Refresh(13.5f);
        Assert.Equal(0, messages.MessageCount);
        Assert.Equal(0, messages.ActiveCount(13.5f));
    }

    private static BuildHalf Half(string hash)
        => new(new BuildStamp(hash, new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), 4),
               new GitProvenance("client-ui", "45e801f2", false), "/mod/bin", false);

    [Fact]
    public void The_stamp_s_line_is_built_once_and_again_only_when_its_inputs_change()
    {
        var fresh = BuildFreshness.Current;
        var stamp = new HudBuildStamp(Fake(), new BuildInfo())
        {
            Native = _ => Half("aaaa1111"),
            Managed = _ => Half("bbbb2222"),
            NativeFreshness = _ => BuildFreshness.Current,
            ManagedFreshness = _ => fresh,
        };
        Client.Ui.IBuildStampSource source = stamp;

        stamp.Refresh(1.0);
        string first = source.Line;
        stamp.Refresh(2.0);
        Assert.Same(first, source.Line);
        Assert.Contains("aaaa", first);
        Assert.False(source.Stale);

        fresh = BuildFreshness.Stale;
        stamp.Refresh(3.0);
        Assert.NotSame(first, source.Line);
        Assert.Contains("REBUILT", source.Line);
        Assert.True(source.Stale);
    }

    [Fact]
    public void The_stamp_s_debug_line_is_read_at_most_four_times_a_second()
    {
        int reads = 0;
        var stamp = new HudBuildStamp(Fake(), new BuildInfo())
        {
            Native = _ => Half("aaaa1111"),
            Managed = _ => Half("bbbb2222"),
            NativeFreshness = _ => BuildFreshness.Current,
            ManagedFreshness = _ => BuildFreshness.Current,
            DebugLine = () => $"read {++reads}",
        };

        for (int frame = 0; frame < 60; frame++)
            stamp.Refresh(frame / 60.0);

        Assert.Equal(4, reads);
        Assert.Equal("read 4", ((Client.Ui.IBuildStampSource)stamp).DebugLine);
    }

    private sealed class Arsenal(bool has, string name, int clip, int reserve) : ILocalArsenal
    {
        public bool TryGetActive(out string n, out int c, out int r)
        {
            (n, c, r) = (name, clip, reserve);
            return has;
        }
    }

    private static Hud NewHud(RecordingEngine engine)
        => new(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
               new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());

    [Fact]
    public void The_local_player_is_read_once_a_frame_whatever_the_panels_ask()
    {
        var engine = Fake().Returns("Hud.LocalPlayerHealth", 57);
        var player = new LocalPlayerUi(engine, new Arsenal(true, "SMG", 30, 90), NewHud(engine));

        player.Refresh();
        for (int i = 0; i < 10; i++)
            _ = (player.Alive, player.Health, player.WeaponName, player.Clip);

        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Hud.LocalPlayerHealth"));
        Assert.Equal((true, 57, "SMG", 30, 90), (player.Alive, player.Health, player.WeaponName, player.Clip, player.Reserve));
        Assert.Equal(-1, player.Secondary);
        Assert.True(float.IsNaN(player.LastDamageYaw));
    }

    [Fact]
    public void No_player_is_not_alive_and_has_no_ammunition()
    {
        var engine = Fake().Returns("Hud.LocalPlayerHealth", -1);
        var player = new LocalPlayerUi(engine, new Arsenal(false, "stale", 5, 5), NewHud(engine));

        player.Refresh();

        Assert.False(player.Alive);
        Assert.Equal((0, -1, false, -1, -1), (player.Health, player.RawHealth, player.HasWeapon, player.Clip, player.Reserve));
    }

    [Fact]
    public void The_localiser_asks_the_mod_file_first_and_the_engine_once_per_token()
    {
        var engine = Fake().ReturnsEachCall("Hud.Localise", () => "Charger");
        var mod = new UiLanguageFile("\"lang\" { \"Tokens\" { \"SS_UI_Health\" \"Health\" } }");
        var localiser = new GameUiLocaliser(mod, engine);

        Assert.Equal("Health", localiser.Find("#SS_UI_Health"));
        Assert.Equal("Charger", localiser.Find("#Valve_Hint_Charger"));
        Assert.Equal("Charger", localiser.Find("#Valve_Hint_Charger"));
        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Hud.Localise"));
    }

    [Fact]
    public void A_token_the_engine_echoes_back_is_not_found()
    {
        var engine = Fake().ReturnsEachCall("Hud.Localise", () => "#Nope");
        Assert.Null(new GameUiLocaliser(null, engine).Find("#Nope"));
    }

    // ---- plan_client_ui U3: the HUD's data gaps ----

    [Fact]
    public void A_hit_from_the_right_is_ninety_and_from_behind_one_eighty()
    {
        var eye = new Vector(0, 0, 64);
        // Looking along +X (yaw 0): Source's +Y is to the LEFT.
        Assert.Equal(90f, LocalPlayerUi.DamageYaw(eye, 0f, new Vector(0, -100, 64)), 3);
        Assert.Equal(-90f, LocalPlayerUi.DamageYaw(eye, 0f, new Vector(0, 100, 64)), 3);
        Assert.Equal(0f, LocalPlayerUi.DamageYaw(eye, 0f, new Vector(100, 0, 64)), 3);
        Assert.Equal(180f, MathF.Abs(LocalPlayerUi.DamageYaw(eye, 0f, new Vector(-100, 0, 64))), 3);
        // Turned to face +Y (yaw 90), the same +Y source is ahead.
        Assert.Equal(0f, LocalPlayerUi.DamageYaw(eye, 90f, new Vector(0, 100, 64)), 3);
    }

    [Fact]
    public void A_hit_with_no_origin_has_no_direction()
        => Assert.True(float.IsNaN(LocalPlayerUi.DamageYaw(new Vector(1, 2, 3), 0f, default)));

    [Fact]
    public void A_weapon_shows_its_localised_print_name()
    {
        var names = new WeaponNames(c => c == "weapon_crowbar" ? "#HL2_Crowbar" : null,
                                    t => t == "#HL2_Crowbar" ? "CROWBAR" : null);
        Assert.Equal("CROWBAR", names.Display("weapon_crowbar"));
        Assert.Same(names.Display("weapon_crowbar"), names.Display("weapon_crowbar"));
    }

    [Fact]
    public void A_weapon_with_no_script_or_no_translation_shows_its_class_name_bare()
    {
        var names = new WeaponNames(c => c == "weapon_smg1" ? "#HL2_SMG1" : null, _ => null);
        Assert.Equal("smg1", names.Display("weapon_smg1"));
        Assert.Equal("ar2", names.Display("weapon_ar2"));
    }
}
