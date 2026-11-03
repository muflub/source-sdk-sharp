//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>
/// TF2's text window (stock's <c>CTFTextWindow</c>,
/// game/client/tf/vgui/tf_textwindow.cpp): the server's message of the
/// day as TF2 raises it — the same title, scrolling text and OK as the
/// core's window, over the same data (the host's VGUIMenu "info" arms
/// reach it because it claims the <c>info</c> panel, and stock's own
/// text window answers that name). The one thing it does differently is
/// its OK: stock's hides the window and raises the map info panel
/// (<c>PANEL_MAPINFO</c>, :220-229, tf_shareddefs.h:164), not the close
/// the core's does — so OK goes there, through
/// <see cref="ITFTextWindowActions"/>, and the map info screen
/// (<see cref="TFMapInfo"/>) takes the flow on to the team panel.
///
/// <para>
/// Stock's OK guard — the custom server page raises nothing
/// (:220-229's <c>m_bCustomURL</c> arm) — is the web arm the core
/// window already leaves out; this window is stock's text kind only.
/// </para>
/// </summary>
public sealed class TFTextWindow : UiScreen
{
    private readonly IMotdSource _source;
    private readonly ITFTextWindowActions? _actions;

    /// <param name="actions">What OK does (stock's okay: hide the window
    /// and raise); none in a sample.</param>
    public TFTextWindow(IMotdSource source, ITFTextWindowActions? actions = null)
    {
        _source = source;
        _actions = actions;
    }

    public override string Name => "tf2textwindow";

    /// <summary>The panel stock's server message comes in under
    /// (<c>PANEL_INFO</c>): the claim answers its shows, and the core's
    /// motd stays behind it.</summary>
    public override string? Panel => "info";

    private string? _text;
    private string[] _lines = [];

    public override void Describe(ref UiFrame ui)
    {
        bool wide = !ui.Narrow;
        float width = wide ? 720 : MathF.Min(560, ui.DesignWidth - 64);
        using (ui.Dialog("tf2textwindow", "#TF2_TextWindow_Title", width))
        {
            ui.Data(_source.Title, Text.Heading);
            // A row per line, so the scroll clips rows and not one tall block
            // (the core window's reading of the same data).
            if (!ReferenceEquals(_text, _source.Text))
            {
                _text = _source.Text;
                _lines = _text.Split('\n');
            }
            using (ui.Scroll("text", Space.S).Width(Size.Fill).Height(Size.Du(wide ? 360 : 280)))
                for (int i = 0; i < _lines.Length; i++)
                    ui.Data(_lines[i], Text.Body).Wrap(width - 64).Key(i);

            using (ui.DialogButtons().Width(Size.Fill))
            {
                ui.Flex();
                if (ui.Button("ok", "#SS_UI_Ok", primary: true).Clicked)
                    _actions?.Okay();
            }
        }
    }
}
