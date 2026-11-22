extern alias client;

using Xunit;

using ClientKeyActions = client::SourceSharp.Client.Input.KeyActions;
using DescentScreens = client::TF2.Descent.Client.Ui.DescentScreens;
using SkillKeys = client::TF2.Descent.Client.Skills.SkillKeys;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The Descent screens' names and keys on the client (plan_descent_logic UI1):
/// the command's words, and the window keys, C, I, K and the automap's M (UI5),
/// which come after TF2's list and the skill keys and take four of TF2's letters.
/// </summary>
public sealed class DescentScreenKeysTests
{
    private static ClientKeyActions Actions()
    {
        var descent = typeof(DescentScreens).Assembly;
        var tf2 = typeof(client::TF2.Client.MoveForwardKey).Assembly;
        return ClientKeyActions.Of(tf2.GetTypes().Concat(descent.GetTypes()), typeof(ClientKeyActions).Assembly);
    }

    /// <summary>The command each key ends with on a fresh config: the last bind of it wins.</summary>
    private static Dictionary<string, string> Final(ClientKeyActions actions)
    {
        var final = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, command) in actions.DefaultBinds)
            final[key] = command;
        return final;
    }

    [Theory]
    [InlineData("inventory", "inventory")]
    [InlineData("descent_inventory", "inventory")]
    [InlineData("CharSheet", "charsheet")]
    [InlineData("skilltree", "skilltree")]
    public void A_screen_is_named_by_its_name_or_its_panel(string word, string screen)
        => Assert.Equal(screen, DescentScreens.Find(word));

    [Theory]
    [InlineData("")]
    [InlineData("descenthud")]
    [InlineData("scoreboard")]
    [InlineData("descent_")]
    public void A_word_that_names_no_descent_window_is_refused(string word)
        => Assert.Null(DescentScreens.Find(word));

    [Fact]
    public void C_I_and_K_open_the_sheet_the_inventory_and_the_skills()
    {
        var final = Final(Actions());

        Assert.Equal("tf2_descent_ui charsheet", final["c"]);
        Assert.Equal("tf2_descent_ui inventory", final["i"]);
        Assert.Equal("tf2_descent_ui skilltree", final["k"]);
    }

    [Fact]
    public void P_opens_the_party_window()
    {
        var final = Final(Actions());

        Assert.Equal("tf2_descent_ui party", final["p"]);
    }

    /// <summary>
    /// TF2 has no use key (E calls for a Medic and drops the flag), and Descent is played by
    /// using things: a stall, a cage's button, loot, a gravestone, a portal pad, a revive. A
    /// fresh config binds E to +use, and the keyboard page lists it, so a player whose config
    /// predates the row can bind it there.
    /// </summary>
    [Fact]
    public void E_uses_and_the_keyboard_page_lists_use()
    {
        var actions = Actions();

        Assert.Equal("+use", Final(actions)["e"]);
        Assert.Contains(actions.Actions(), a => a.Command == "+use");
    }

    [Fact]
    public void M_toggles_the_automap()
    {
        var final = Final(Actions());

        Assert.Equal("tf2_descent_ui_toggle automap", final["m"]);
    }

    [Fact]
    public void The_window_keys_are_none_of_the_skill_or_belt_keys()
    {
        string[] windows = ["c", "i", "k", "m", "p"];
        var reserved = SkillKeys.SlotKeys.Concat(SkillKeys.BeltKeys).ToArray();

        Assert.Equal(10, reserved.Length);                       // the stimulus: the skills' six and the belt's four
        Assert.Empty(windows.Intersect(reserved, StringComparer.OrdinalIgnoreCase));
        var final = Final(Actions());
        Assert.All(SkillKeys.SlotKeys, k => Assert.StartsWith("+tf2_descent_skill", final[k], StringComparison.Ordinal));
    }

    [Fact]
    public void The_window_keys_and_use_take_exactly_six_of_tf2s_binds()
    {
        var actions = Actions();
        var tf2Only = ClientKeyActions.Of(typeof(client::TF2.Client.MoveForwardKey).Assembly.GetTypes(), typeof(ClientKeyActions).Assembly);
        var before = Final(tf2Only);
        var after = Final(actions);

        // Every key whose command changed is one the skills or these windows took.
        var displaced = before.Where(kv => after[kv.Key] != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (string skillKey in SkillKeys.SlotKeys)
            displaced.Remove(skillKey);
        displaced.Remove("l");   // the miner's lamp's key (HE10): HeroLampLightsTests holds it

        Assert.Equal(new Dictionary<string, string>
        {
            ["c"] = "+voice_menu_3",
            ["e"] = "dropitem",                    // use (+use): TF2 has no use key; its E dropped the flag
            ["i"] = "showmapinfo",
            ["k"] = "cl_decline_first_notification",
            ["m"] = "open_charinfo_direct",        // TF2's loadout, which Descent keeps closed (HE1)
            ["p"] = "say_party",                   // the party window (WO9): heroes are one team, team chat reaches the party
        }, displaced);
    }
}
