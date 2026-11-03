using SourceSharp.Abi;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The level load's dialog, as the engine drives the GameUI through it
/// (plan_gameui_managed.md G1; the stock module's loading dialog): up only
/// for a load started with the progress dialog asked for; each update
/// answers whether the engine should repaint (the drawn bar moved a step of
/// 25, or the step's text changed), since the engine paints inside its load
/// loop only on a yes. A failed load leaves the dialog in its error form,
/// "Disconnected" and the reason, until Close; so do the three Steam login
/// failures a disconnect names while the dialog is up. A finished load,
/// failed or not, runs the engine's <c>gameui_hide</c>, as the stock module
/// did (the engine hides only what a level can be shown under).
/// </summary>
public sealed class GameUiLoading(IUiLocalisation localise, IFwdEngineClient engine)
    : ILoadingProgressSource, IDisconnectMessageSource
{
    /// <summary>The steps the drawn bar moves in: a yes per step.</summary>
    private const int BarSteps = 25;

    // The engine's eSteamLoginFailure values (common/GameUI/IGameUI.h).
    private const int NoSteamLogin = 2, VacBanned = 3, LoggedInElsewhere = 4;

    private int _drawnSteps;
    private string? _statusText;
    private string? _message;

    public bool Showing { get; private set; }

    public float Progress { get; private set; }

    public string Status { get; private set; } = string.Empty;

    bool IDisconnectMessageSource.Showing => _message is not null;

    public string Body => _message ?? string.Empty;

    public void Close() => _message = null;

    /// <summary>OnLevelLoadingStarted.</summary>
    public void Started(bool showProgressDialog)
    {
        if (!showProgressDialog)
            return;
        Showing = true;
        Progress = 0f;
        Status = string.Empty;
        _statusText = null;
        _drawnSteps = 0;
        _message = null;
    }

    /// <summary>UpdateProgressBar: whether the engine should repaint.</summary>
    public bool Update(float progress, string? statusText)
    {
        if (!Showing)
            return false;
        bool redraw = false;
        Progress = Math.Clamp(progress, 0f, 1f);
        int steps = (int)(Progress * BarSteps);
        if (steps != _drawnSteps)
        {
            _drawnSteps = steps;
            redraw = true;
        }
        if (statusText is { Length: > 0 } && !string.Equals(statusText, _statusText, StringComparison.OrdinalIgnoreCase))
        {
            _statusText = statusText;
            Status = Localised(statusText);
            redraw = true;
        }
        return redraw;
    }

    /// <summary>OnLevelLoadingFinished.</summary>
    public void Finished(bool error, string? reason, string? extendedReason)
    {
        Showing = false;
        if (error)
            _message = Reason(reason ?? string.Empty, extendedReason);
        engine.SubmitConsoleLine("gameui_hide");
    }

    /// <summary>OnDisconnectFromServer: the Steam login failures, while the dialog is up.</summary>
    public void Disconnected(int steamLoginFailure)
    {
        if (!Showing)
            return;
        string? token = steamLoginFailure switch
        {
            NoSteamLogin => "#Steam_ValidLoginRequired",
            VacBanned => "#VAC_ConnectionRefusedDetail",
            LoggedInElsewhere => "#VAC_LoggedInElsewhereReason",
            _ => null,
        };
        if (token is null)
            return;
        Showing = false;
        _message = Localised(token);
    }

    /// <summary>The failure's words: the reason, with the extended reason set
    /// into its <c>%s1</c> when there is one; each a token or plain text.</summary>
    private string Reason(string reason, string? extendedReason)
    {
        string text = Localised(reason);
        return extendedReason is { Length: > 0 } ? text.Replace("%s1", Localised(extendedReason), StringComparison.Ordinal) : text;
    }

    private string Localised(string text)
        => text.StartsWith('#') ? localise.Find(text) ?? text : text;
}

/// <summary>
/// Quit's question, as the engine's OnConfirmQuit asks it (the stock
/// module's quit confirmation): Quit runs the engine's <c>quit</c>, Cancel
/// withdraws the question.
/// </summary>
public sealed class ConfirmQuitUi(IFwdEngineClient engine) : IConfirmQuitSource
{
    public bool Asking { get; private set; }

    public void Ask() => Asking = true;

    public void Quit()
    {
        Asking = false;
        engine.SubmitConsoleLine("quit");
    }

    public void Cancel() => Asking = false;
}
