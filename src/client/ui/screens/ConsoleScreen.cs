using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The console (stock: GameUI's CGameConsoleDialog, a movable, resizable
/// Frame): a window with the output log (coloured by severity, following the
/// end), the input line (caret, selection, history recall), and autocomplete
/// under it as an overlay: matching convars with their values and commands,
/// with help. Its layout answers to the window's size: a small console window
/// drops the suggestions' help column (ui.Narrow inside the window).
/// </summary>
public sealed class ConsoleScreen : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly ConsoleLogPanel _log;
    private readonly ConsoleInputPanel _input;
    private readonly ConsoleCompletionsPanel _completions;

    public ConsoleScreen(UiWindows windows, ConsoleLogPanel log, ConsoleInputPanel input, ConsoleCompletionsPanel completions)
    {
        _windows = windows;
        _log = log;
        _input = input;
        _completions = completions;
    }

    public override string Name => "console";

    public string WindowId => "console";

    public override int Order => 100;

    /// <summary>
    /// About 60% of the screen by default; at least 480 x 240 du (the input line
    /// and a few log lines), at most the screen; narrow below 880 du, where the
    /// suggestions' help column no longer fits beside the name and value.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Round(screenW * 0.6f), MathF.Round(screenH * 0.6f), 480, 240, screenW, screenH, 880);

    public override void Describe(ref UiFrame ui)
    {
        UiBox field;
        using (ui.Window(_windows, WindowId, "#SS_UI_Console_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            ui.Sub("log", _log).Size(Size.Fill, Size.Fill);
            ui.Divider();
            field = ui.Sub("input", _input).Width(Size.Fill);
        }

        // The suggestions are as wide as the window allows, and narrow with it.
        if (!_windows.Closing(WindowId) && _windows.TryGet(WindowId, out var p))
            ui.Sub("completions", _completions, new ConsoleCompletionsProps(field, p.W, p.W < Spec(0, 0).NarrowBelowDu));
    }
}

/// <summary>The output log: a virtual list of lines that follows the end; an empty console says how to start.</summary>
public sealed class ConsoleLogPanel : UiPanel
{
    private readonly IConsoleLogSource _log;
    private readonly Row _row;

    public ConsoleLogPanel(IConsoleLogSource log)
    {
        _log = log;
        _row = new Row(log);
    }

    /// <summary>A log line's height, du: the Code role's line and a little air.</summary>
    public const float LineDu = 24;

    public override void Describe(ref UiFrame ui)
    {
        if (_log.LineCount == 0)
        {
            using (ui.Box().Size(Size.Fill, Size.Fill))
                ui.Text("#SS_UI_Console_Empty", Text.Caption, Tone.TextMuted).At(UiAnchor.Centre).Wrap(MathF.Min(560, ui.DesignWidth - 96))
                  .TextAlign(Align.Centre);
            return;
        }
        ui.VirtualList("lines", _log.LineCount, LineDu, _row, followEnd: true).Size(Size.Fill, Size.Fill);
    }

    /// <summary>One line, in its severity's tone; errors on a faint danger band.</summary>
    private sealed class Row(IConsoleLogSource log) : UiPanel<int>
    {
        public override void Describe(ref UiFrame ui, in int i)
        {
            var line = log.Line(i);
            var tone = line.Severity switch
            {
                ConsoleSeverity.Warning => Tone.Warning,
                ConsoleSeverity.Error => Tone.Danger,
                ConsoleSeverity.Developer => Tone.TextMuted,
                ConsoleSeverity.Echo => Tone.Accent,
                _ => Tone.Text,
            };
            var band = line.Severity == ConsoleSeverity.Error ? ((UiPaint)Tone.Danger).WithOpacity(0.08f) : (UiPaint)Tone.None;
            using (ui.Row(Space.S).Size(Size.Fill, Size.Fill).Fill(band).Pad(Space.S, Space.None))
                ui.Data(line.Text, Text.Code, tone).Width(Size.Fill).Elide();
        }
    }
}

/// <summary>The input line: a prompt, the field (caret, selection), the history position when recalling, Submit.</summary>
public sealed class ConsoleInputPanel : UiPanel
{
    private readonly IConsoleInputSource _input;
    private readonly IConsoleActions? _actions;

    /// <param name="actions">What Submit does (the core: run the line); none in a sample.</param>
    public ConsoleInputPanel(IConsoleInputSource input, IConsoleActions? actions = null)
    {
        _input = input;
        _actions = actions;
    }

    public override void Describe(ref UiFrame ui)
    {
        var p = _input;
        using (ui.Row(Space.M).Width(Size.Fill))
        {
            ui.Data("]", Text.Code, Tone.Accent);
            ui.TextField("field", p.Input, "#SS_UI_Console_Placeholder", p.Caret, p.SelectionStart, p.SelectionLength,
                         Size.Fill, Text.Code);
            if (p.HistoryIndex >= 0)
            {
                using (ui.Row(Space.XS).Fill(Tone.SurfaceRaised).Round(Radius.M).Pad(Space.S, Space.XS))
                {
                    ui.Shape(UiShape.ChevronUp, Tone.TextMuted, 12);
                    ui.Text("#SS_UI_Console_History", Text.Label, Tone.TextMuted);
                    ui.Text($"{p.HistoryIndex + 1} / {p.HistoryCount}", Text.Label, Tone.Text);
                }
            }
            if (ui.Button("submit", "#SS_UI_Console_Submit").Clicked)
                _actions?.Submit();
        }
    }
}

/// <summary>Where the suggestions go: under the input field.</summary>
public readonly record struct ConsoleCompletionsProps(UiBox Field, float WindowWidthDu, bool Narrow);

/// <summary>
/// Autocomplete: an overlay under the input with the best matches: a CVAR/CMD
/// badge, the name, a convar's value, and its help (dropped when narrow); the
/// one Tab would take is raised; the count of matches at the foot.
/// </summary>
public sealed class ConsoleCompletionsPanel : UiPanel<ConsoleCompletionsProps>
{
    private readonly IConsoleCompletionSource _completions;

    public ConsoleCompletionsPanel(IConsoleCompletionSource completions) => _completions = completions;

    /// <summary>The most suggestions shown at once.</summary>
    public const int Shown = 8;

    public override void Describe(ref UiFrame ui, in ConsoleCompletionsProps props)
    {
        var c = _completions;
        if (c.CompletionCount == 0)
            return;

        bool narrow = props.Narrow;
        float width = MathF.Min(narrow ? 560 : 860, props.WindowWidthDu - 2 * UiWindowWidget.Ring - 2 * (float)Space.L);
        int shown = Math.Min(Shown, c.CompletionCount);
        using (ui.Overlay("suggest").AnchorTo(props.Field, UiAnchor.BottomLeft, UiAnchor.TopLeft).Inset(Space.None, Space.XS))
        using (ui.Column(Space.None).Width(Size.Du(width)).Fill(Tone.Surface).Round(Radius.M)
                 .Border(Tone.Line, 1).Raise(Elevation.Overlay).Pad(Space.XS))
        {
            for (int i = 0; i < shown; i++)
            {
                var item = c.Completion(i);
                bool selected = i == c.SelectedCompletion;
                using (ui.Row(Space.M).Key(i).Size(Size.Fill, Size.Du(32)).Pad(Space.S, Space.None).Round(Radius.S)
                         .Fill(selected ? ((UiPaint)Tone.Accent).WithOpacity(0.18f) : Tone.None))
                {
                    using (ui.Box().Size(Size.Du(40), Size.Du(20)).Round(Radius.S).Fill(Tone.SurfaceRaised))
                        ui.Text(item.IsCommand ? "#SS_UI_Console_Command" : "#SS_UI_Console_Variable", Text.Label, Tone.TextMuted).At(UiAnchor.Centre);
                    ui.Data(item.Name, Text.Code, selected ? Tone.Accent : Tone.Text).Width(narrow ? Size.Fill : Size.Du(280)).Elide();
                    if (!item.IsCommand)
                        ui.Data(item.Value, Text.Code, Tone.Text).Width(Size.Du(88)).Elide();
                    else
                        ui.Spacer(Size.Du(88), Size.Du(1));
                    if (!narrow)
                        ui.Data(item.Help, Text.Caption, Tone.TextMuted).Width(Size.Fill).Elide();
                }
            }
            using (ui.Row(Space.XS).Pad(Space.S, Space.XS))
            {
                ui.Text($"{c.CompletionCount}", Text.Label, Tone.TextMuted);
                ui.Text("#SS_UI_Console_Matches", Text.Label, Tone.TextMuted);
            }
        }
    }
}
