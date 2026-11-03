using System.Text.RegularExpressions;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The default key binds the engine seeds a fresh mod cfg with. The engine
/// execs <c>cfg/config_default.cfg</c> when a player's <c>config.cfg</c> is
/// missing and has no built-in binds of its own, and the TF2 menus and HUD
/// name commands by token — the build menu's cancel hint is
/// <c>TF2_BuildMenu_Cancel "Hit '%lastinv%' to Cancel"</c>, which read
/// "&lt; NOT BOUND &gt;" live (ctf_2fort) because the mod seeded no default
/// config. The keys are stock's
/// <c>tf/cfg/config_default.cfg</c>, for the eight commands the UI names.
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
    };

    private static IReadOnlyDictionary<string, string> Binds()
    {
        string text = File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "cfg-seed", "config_default.cfg"));
        var binds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"bind\s+""([^""]+)""\s+""([^""]+)"""))
        {
            binds[m.Groups[2].Value.TrimStart('+')] = m.Groups[1].Value;
        }

        return binds;
    }

    /// <summary>What a fresh player needs to play at all, at stock's keys
    /// (tf/cfg/config_default.cfg): the engine binds nothing itself, so a
    /// config that named only the UI's commands left no way to move.</summary>
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
