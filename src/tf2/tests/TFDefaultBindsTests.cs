extern alias client;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// TF2's key actions and the keys a fresh player gets (TFKeyActions). The engine
/// has no built-in binds of its own, and the TF2 menus and HUD name commands by
/// token: the build menu's cancel hint is
/// <c>TF2_BuildMenu_Cancel "Hit '%lastinv%' to Cancel"</c>, which read
/// "&lt; NOT BOUND &gt;" live (ctf_2fort) when the mod gave no defaults. The keys
/// are stock's <c>tf/cfg/config_default.cfg</c>.
/// </summary>
public sealed class TFDefaultBindsTests
{
    /// <summary>The command each UI token names, and stock's key for it
    /// (tf/cfg/config_default.cfg). Keys are compared case-insensitively and
    /// commands with their leading '+' stripped, the way
    /// <c>Key_NameForBinding</c> answers a lookup (the engine's key bindings).</summary>
    private static readonly IReadOnlyDictionary<string, string> Stock = new Dictionary<string, string>
    {
        ["lastinv"] = "q",
        ["jump"] = "space",
        ["changeclass"] = ",",
        ["changeteam"] = ".",
        ["attack"] = "mouse1",
        ["attack2"] = "mouse2",
        ["reload"] = "r",
        ["disguiseteam"] = "-",
        ["voice_menu_1"] = "z",
        ["voice_menu_2"] = "x",
        ["voice_menu_3"] = "c",
        ["voicemenu 0 0"] = "e",
        ["taunt"] = "g",
        ["dropitem"] = "l",
    };

    private static client::SourceSharp.Client.Input.KeyActions Actions()
        => client::SourceSharp.Client.Input.KeyActions.Of(typeof(client::TF2.Client.MoveForwardKey).Assembly.GetTypes(),
                                                          typeof(client::SourceSharp.Client.Input.KeyActions).Assembly);

    private static IReadOnlyDictionary<string, string> Binds()
    {
        var binds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, command) in Actions().DefaultBinds)
            binds[command.TrimStart('+')] = key;
        return binds;
    }

    /// <summary>TF2's page is TF2's list: no HL2 row, TF2's own, and each class skill its row of +attack2.</summary>
    [Fact]
    public void The_keyboard_page_lists_tf2_s_actions_not_the_core_s()
    {
        var rows = Actions().Actions();
        Assert.DoesNotContain(rows, r => r.Command == "phys_swap");
        Assert.Contains(rows, r => r.Command == "+taunt" && r.Label == "#TF_Taunt");
        Assert.Equal(6, rows.Count(r => r.Command == "+attack2"));
        Assert.Contains(rows, r => r.Command == "adminmenu");
        Assert.Equal(71, rows.Count);
    }

    /// <summary>What a fresh player needs to play at all, at stock's keys
    /// (tf/cfg/config_default.cfg): the engine binds nothing itself, so defaults
    /// naming only the UI's commands left no way to move.</summary>
    private static readonly IReadOnlyDictionary<string, string> Play = new Dictionary<string, string>
    {
        ["forward"] = "w",
        ["back"] = "s",
        ["moveleft"] = "a",
        ["moveright"] = "d",
        ["duck"] = "ctrl",
        ["showscores"] = "tab",
        ["toggleconsole"] = "`",
        ["slot1"] = "1",
        ["slot2"] = "2",
        ["slot3"] = "3",
        ["invprev"] = "mwheelup",
        ["invnext"] = "mwheeldown",
        ["say"] = "y",
        ["say_team"] = "u",
        ["voicerecord"] = "v",
    };

    [Fact]
    public void A_fresh_player_can_move_shoot_and_talk_at_stocks_keys()
    {
        var binds = Binds();
        foreach ((string command, string key) in Play)
        {
            Assert.True(binds.TryGetValue(command, out string? bound),
                $"the default config leaves {command} unbound");
            Assert.Equal(key, bound, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_default_config_binds_lastinv_to_q()
    {
        Assert.Equal("q", Binds()["lastinv"]);
    }

    [Fact]
    public void Every_command_the_ui_names_is_bound_to_its_stock_key()
    {
        var binds = Binds();

        foreach ((string command, string key) in Stock)
        {
            Assert.True(binds.TryGetValue(command, out string? bound),
                $"the UI names %{command}% and the default config leaves it unbound");
            Assert.Equal(key, bound, ignoreCase: true);
        }
    }
}
