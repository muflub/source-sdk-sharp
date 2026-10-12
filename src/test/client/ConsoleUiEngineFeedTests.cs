using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using Xunit;
namespace SourceSharp.Tests;

/// <summary>
/// The console window mirroring the engine's prints, not only typed lines
/// (docs/todo.md's console window follow-up): the lines handed to the side's
/// ConsoleLines — where the display func's feed lands — appear in the window,
/// the echo of a typed command is not shown twice, and disposing the window
/// stops the flow.
/// </summary>
public sealed class ConsoleUiEngineFeedTests
{
    [Fact]
    public void An_engine_line_appears_in_the_window()
    {
        var lines = new ConsoleLines();
        var ui = new ConsoleUi(() => null, lines);

        lines.Line("playershadow: enabled=1\n", isWarning: false);

        Assert.Equal("playershadow: enabled=1", ui.Line(ui.LineCount - 1).Text);
        Assert.Equal(ConsoleSeverity.Message, ui.Line(ui.LineCount - 1).Severity);
    }

    [Fact]
    public void An_engine_warning_is_severity_warning()
    {
        var lines = new ConsoleLines();
        var ui = new ConsoleUi(() => null, lines);

        lines.Line("careful\n", isWarning: true);

        Assert.Equal("careful", ui.Line(ui.LineCount - 1).Text);
        Assert.Equal(ConsoleSeverity.Warning, ui.Line(ui.LineCount - 1).Severity);
    }

    [Fact]
    public void The_echo_of_a_typed_command_is_shown_once()
    {
        // Submit already echoes "] status" locally; the engine echoes the
        // same text back through the feed, and the window shows one row.
        var lines = new ConsoleLines();
        var ui = new ConsoleUi(() => null, lines);

        ui.Char('s');
        ui.Char('t');
        ui.Char('a');
        ui.Char('t');
        ui.Char('u');
        ui.Char('s');
        int before = ui.LineCount;
        ui.Key(UiKey.Enter);
        Assert.Equal("] status", ui.Line(ui.LineCount - 1).Text);

        lines.Line("] status\n", isWarning: false);

        Assert.Equal(before + 1, ui.LineCount);
    }

    [Fact]
    public void Dispose_stops_the_flow()
    {
        var lines = new ConsoleLines();
        var ui = new ConsoleUi(() => null, lines);
        ui.Dispose();

        lines.Line("after teardown\n", isWarning: false);

        Assert.DoesNotContain("after teardown",
            Enumerable.Range(0, ui.LineCount).Select(i => ui.Line(i).Text));
    }

    [Fact]
    public void Without_lines_the_window_keeps_only_typed_rows()
    {
        // The old shape still works: no engine source, typed echoes only.
        var ui = new ConsoleUi(() => null);
        ui.Char('x');
        ui.Key(UiKey.Enter);

        Assert.Equal("] x", ui.Line(ui.LineCount - 1).Text);
    }
}
