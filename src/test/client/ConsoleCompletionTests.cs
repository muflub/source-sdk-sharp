using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The console's autocomplete, as stock's CConsolePanel builds and walks its list.</summary>
public class ConsoleCompletionTests
{
    private sealed class Commands : IConsoleCommands
    {
        public IReadOnlyList<ConsoleCompletion> All() =>
        [
            new("sv_gravity", "600", "World gravity.", false),
            new("sv_cheats", "0", "", false),
            new("map", "", "Start a map.", true),
            new("SV_Alltalk", "0", "", false),
            new("sv_Zeta", "0", "", false),
        ];
    }

    private static ConsoleUi Console_() => new(new DefaultForwarders.NoEngineClient(), null, new Commands());

    private static ConsoleUi Typed(string text)
    {
        var ui = Console_();
        foreach (char c in text)
            ui.Char(c);
        return ui;
    }

    private static List<string> Names(ConsoleUi ui)
        => [.. Enumerable.Range(0, ui.CompletionCount).Select(i => ui.Completion(i).Name)];

    [Fact]
    public void Typing_lists_what_starts_with_it_by_name_ignoring_case()
        => Assert.Equal(["SV_Alltalk", "sv_cheats", "sv_gravity", "sv_Zeta"], Names(Typed("sv")));

    [Fact]
    public void An_empty_line_or_one_past_a_space_lists_nothing()
    {
        Assert.Equal(0, Console_().CompletionCount);
        Assert.Equal(0, Typed("map ").CompletionCount);
        Assert.Equal(-1, Typed("map ").SelectedCompletion);
    }

    [Fact]
    public void Tab_takes_the_first_with_a_space_after_it_and_moves_on()
    {
        var ui = Typed("sv");
        Assert.Equal(0, ui.SelectedCompletion);
        ui.Key(UiKey.Tab);
        Assert.Equal("SV_Alltalk ", ui.Input);
        Assert.Equal(ui.Input.Length, ui.Caret);
        Assert.Equal(1, ui.SelectedCompletion);
        ui.Key(UiKey.Tab);
        Assert.Equal("sv_cheats ", ui.Input);
    }

    [Fact]
    public void Tab_past_the_last_loops_and_the_list_stays()
    {
        var ui = Typed("sv");
        for (int i = 0; i < 5; i++)
            ui.Key(UiKey.Tab);
        Assert.Equal("SV_Alltalk ", ui.Input);
        Assert.Equal(4, ui.CompletionCount);
    }

    [Fact]
    public void The_arrows_walk_the_suggestions_while_they_are_up()
    {
        var ui = Typed("sv");
        ui.Key(UiKey.Down);
        ui.Key(UiKey.Down);
        Assert.Equal("sv_cheats ", ui.Input);
        ui.Key(UiKey.Up);
        Assert.Equal("SV_Alltalk ", ui.Input);
    }

    [Fact]
    public void Typing_again_starts_over_from_what_is_typed()
    {
        var ui = Typed("sv");
        ui.Key(UiKey.Tab);
        ui.Key(UiKey.Backspace);
        ui.Key(UiKey.Backspace);
        ui.Key(UiKey.Backspace);
        Assert.Equal("SV_Allta", ui.Input);
        Assert.Equal(["SV_Alltalk"], Names(ui));
        ui.Char('g');
        Assert.Equal(0, ui.CompletionCount);
    }

    [Fact]
    public void Submit_clears_the_suggestions_and_the_arrows_are_the_history_again()
    {
        var ui = Typed("map");
        Assert.Equal(1, ui.CompletionCount);
        ui.Submit();
        Assert.Equal(0, ui.CompletionCount);
        ui.Key(UiKey.Up);
        Assert.Equal("map", ui.Input);
    }

    [Fact]
    public void With_no_list_Tab_leaves_the_line_alone()
    {
        var ui = new ConsoleUi(new DefaultForwarders.NoEngineClient());
        ui.Char('s');
        ui.Key(UiKey.Tab);
        Assert.Equal("s", ui.Input);
    }

    [Theory]
    [InlineData(ConVarFlags.None, true)]
    [InlineData(ConVarFlags.DevelopmentOnly, false)]
    [InlineData(ConVarFlags.Hidden, false)]
    public void The_engine_s_development_only_and_hidden_entries_are_not_offered(ConVarFlags flags, bool shown)
    {
        var entry = new NativeConsoleEntry("x", "", "", "", (int)flags, false);
        Assert.Equal(shown, EngineConsoleCommands.Shown(entry));
    }
}
