using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The options window (plan_gameui_managed.md G2; stock: GameUI's options
/// dialog): one window with a tab per page, keyboard, mouse, audio, video,
/// voice and multiplayer, as stock orders them. Opened, every page loads from
/// the engine; Apply writes every page's changes, OK applies and closes,
/// Cancel discards them all (the next open loads afresh). Apply and OK wait
/// while any page holds an invalid field.
/// </summary>
public sealed class Options : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IUiOptionsPage[] _pages;
    private readonly string[] _tabs;
    private readonly string[] _pageNames;
    private long _lastFrame = long.MinValue;
    private bool _discarded;

    // Each page's body under its own key, so one page's scroll and open lists
    // are not another's.
    private const int PageKeyBase = 1 << 21;

    public Options(UiWindows windows, IUiOptionsPage[] pages)
    {
        _windows = windows;
        _pages = pages;
        _tabs = new string[pages.Length];
        _pageNames = new string[pages.Length];
        for (int i = 0; i < pages.Length; i++)
        {
            _tabs[i] = pages[i].TabToken;
            _pageNames[i] = pages[i].PageName;
        }
    }

    public override string Name => "options";

    public string WindowId => "options";

    /// <summary>Each page's name opens the window on that page.</summary>
    public override IReadOnlyList<string> Aliases => _pageNames;

    public override void PanelOpened(string panel)
    {
        int page = Array.FindIndex(_pageNames, n => n.Equals(panel, StringComparison.OrdinalIgnoreCase));
        if (page >= 0)
            Tab = page;
    }

    /// <summary>The page shown.</summary>
    public int Tab { get; set; }

    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(960, screenW), MathF.Min(680, screenH), 520, 420, screenW, screenH, 640);

    public bool AnyDirty
    {
        get
        {
            foreach (var page in _pages)
                if (page.AnyDirty)
                    return true;
            return false;
        }
    }

    public bool IsValid
    {
        get
        {
            foreach (var page in _pages)
                if (!page.IsValid)
                    return false;
            return true;
        }
    }

    public void Load()
    {
        foreach (var page in _pages)
            page.Load();
    }

    /// <summary>Every page's changes; nothing while any page is invalid.</summary>
    public bool Apply()
    {
        if (!IsValid)
            return false;
        // Only the changed pages: the video page's Apply is a material system
        // reconfiguration, nothing to run for a tick on another page.
        foreach (var page in _pages)
            if (page.AnyDirty)
                page.Apply();
        return true;
    }

    public void Ok()
    {
        if (Apply())
            _windows.RequestClose(WindowId);
    }

    public void Cancel()
    {
        _discarded = true;
        _windows.RequestClose(WindowId);
    }

    public override void Describe(ref UiFrame ui)
    {
        // Not described last frame: just opened, so every page starts from the engine.
        // So is one Cancel shut, however soon it opens again.
        if (ui.FrameNumber != _lastFrame + 1 || _discarded)
            Load();
        _discarded = false;
        _lastFrame = ui.FrameNumber;
        Tab = Math.Clamp(Tab, 0, _pages.Length - 1);

        var page = _pages[Tab];
        page.TakeKeys(ref ui);
        using (ui.Window(_windows, WindowId, "#SS_UI_Options_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            Span<bool> changed = stackalloc bool[_pages.Length];
            for (int i = 0; i < _pages.Length; i++)
                changed[i] = _pages[i].AnyDirty;
            Tab = ui.TabBar(_tabs, Tab, changed);
            using (ui.Column(Space.None).Size(Size.Fill, Size.Fill).Key(PageKeyBase + Tab))
                page.DescribeBody(ref ui);

            bool valid = IsValid;
            using (ui.DialogButtons().Width(Size.Fill))
            {
                if (ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                    Cancel();
                ui.Flex();
                if (ui.Button("apply", "#SS_UI_Apply", enabled: valid && AnyDirty).Clicked)
                    Apply();
                if (ui.Button("ok", "#SS_UI_Ok", primary: true, enabled: valid).Clicked)
                    Ok();
            }
        }
    }
}
