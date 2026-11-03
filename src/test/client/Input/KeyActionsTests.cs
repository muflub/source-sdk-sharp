using SourceSharp.Client.Input;
using SourceSharp.Client.Ui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>Key actions declared on classes: the keyboard page's rows and a fresh player's keys.</summary>
public class KeyActionsTests
{
    private static readonly System.Reflection.Assembly Core = typeof(KeyActions).Assembly;

    [KeyAction("second", "Second row", Section = "S", Order = 20, Keys = ["B"])]
    private sealed class SecondRow;

    [KeyAction("first", "First row", Section = "S", Order = 10, Keys = ["A", ";"])]
    private sealed class FirstRow;

    [KeyAction("unlisted", Keys = ["C"])]
    private sealed class NoRow;

    private static KeyActions Mod() => KeyActions.Of([typeof(SecondRow), typeof(FirstRow), typeof(NoRow)], Core);

    [Fact]
    public void Rows_are_the_labelled_actions_in_order()
        => Assert.Equal([new UiKeyAction("first", "First row", "S"), new UiKeyAction("second", "Second row", "S")], Mod().Actions());

    [Fact]
    public void An_action_with_no_label_is_bound_and_not_listed()
    {
        Assert.Contains(("C", "unlisted"), Mod().DefaultBinds);
        Assert.DoesNotContain(Mod().Actions(), r => r.Command == "unlisted");
    }

    [Fact]
    public void A_mod_s_actions_replace_the_core_s()
    {
        var both = KeyActions.Of(Core.GetTypes().Append(typeof(FirstRow)), Core);
        Assert.Equal(["first"], both.Actions().Select(r => r.Command));
    }

    [Fact]
    public void With_no_mod_s_actions_the_core_s_are_the_list()
    {
        var core = KeyActions.Of(Core.GetTypes(), Core);
        Assert.Contains(core.Actions(), r => r.Command == "impulse 100");
        Assert.Contains(core.Actions(), r => r.Command == "phys_swap");
        Assert.Contains(("f", "impulse 100"), core.DefaultBinds);
    }

    [Fact]
    public void A_player_with_no_config_gets_each_default_as_a_quoted_bind()
    {
        var lines = new List<string>();
        Mod().BindDefaults(hasConfig: false, lines.Add);
        Assert.Equal(["bind \"C\" \"unlisted\"", "bind \"A\" \"first\"", "bind \";\" \"first\"", "bind \"B\" \"second\""], lines);
    }

    [Fact]
    public void A_player_with_a_config_keeps_it()
    {
        var lines = new List<string>();
        Mod().BindDefaults(hasConfig: true, lines.Add);
        Assert.Empty(lines);
    }

    [Fact]
    public void No_key_is_given_to_two_of_the_core_s_actions()
    {
        var keys = KeyActions.Of(Core.GetTypes(), Core).DefaultBinds.Select(b => b.Key.ToLowerInvariant()).ToList();
        Assert.True(keys.Count > 50, $"{keys.Count} keys");
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
