using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The parts screens are assembled from (plan_client_ui §5.3, §5.8.2): each
/// already looks right with its defaults, so a screen is a few calls rather than
/// a pile of rectangles. Extension methods on the frame; stateless.
/// </summary>
public static class UiWidgets
{
    /// <summary>
    /// A panel: the translucent dark surface every group of HUD or menu content
    /// sits on, rounded, padded and raised. Answers an open vertical stack to fill.
    /// Use for any block of content over the world.
    /// </summary>
    public static UiBox Panel(this ref UiFrame ui, UiLength? pad = null, UiLength? gap = null,
                              Elevation elevation = Elevation.Raised)
        => ui.Column(gap ?? Space.S)
             .Fill(Tone.Surface).Round(Radius.L).Pad(pad ?? Space.L).Raise(elevation);

    /// <summary>A small upper-case label: column headers, "HEALTH" under a number.</summary>
    public static UiBox Label(this ref UiFrame ui, string token, Tone tone = Tone.TextMuted)
        => ui.Text(token, Text.Label, tone);

    /// <summary>A vector icon in a tone.</summary>
    public static UiBox Icon(this ref UiFrame ui, UiShape shape, Tone tone = Tone.Text, float sizeDu = 20)
        => ui.Shape(shape, tone, sizeDu);

    /// <summary>A thin rule across a stack.</summary>
    public static UiBox Divider(this ref UiFrame ui) => ui.Rect(Tone.Line, Size.Fill, Size.Du(1));

    /// <summary>
    /// A horizontal meter: a track and a fill of <paramref name="fraction"/> (0..1).
    /// Use for health, armour, progress; pair it with a number, never alone.
    /// </summary>
    public static UiBox Meter(this ref UiFrame ui, float fraction, UiPaint fill, UiSize width, float heightDu = 6)
    {
        var track = ui.Box().Size(width, Size.Du(heightDu)).Fill(Tone.SurfaceRaised).Round(Radius.S);
        using (track)
        {
            float f = Math.Clamp(fraction, 0f, 1f);
            if (f > 0f)
                ui.Rect(fill, Size.Percent(f * 100f), Size.Fill).Round(Radius.S);
        }
        return track;
    }

    /// <summary>
    /// A button: a raised fill with a centred label, at least 40 du tall (the click
    /// target rule is 32), lighter on hover. <paramref name="primary"/> fills with
    /// the accent. Answers what the pointer did.
    /// </summary>
    public static UiInteraction Button(this ref UiFrame ui, string key, string token, bool primary = false,
                                       UiSize? width = null, bool enabled = true)
    {
        var box = ui.Box(key).Size(width ?? Size.Auto, Size.Du(40)).MinWidth(120).Round(Radius.M)
                    .Pad(Space.L, Space.None);
        var hit = ui.Interact(box);
        if (!enabled)
        {
            // Disabled: drawn flat and muted, and a click does nothing.
            box.Fill(Tone.SurfaceRaised);
            using (box)
                ui.Text(token, Text.Heading, Tone.TextMuted).At(UiAnchor.Centre).TextAlign(Align.Centre);
            return new UiInteraction(hit.Hovered, false, false);
        }
        float hover = ui.Animate(box, hit.Hovered ? 1f : 0f, Motion.Quick);
        box.Fill(primary ? Tone.Accent : ((UiPaint)Tone.SurfaceRaised).WithOpacity(0.85f + 0.15f * hover));
        if (!primary)
            box.Border(((UiPaint)Tone.Accent).WithOpacity(hover));
        using (box)
            ui.Text(token, Text.Heading, primary ? Tone.OnAccent : Tone.Text).At(UiAnchor.Centre).TextAlign(Align.Centre);
        return hit;
    }

    /// <summary>
    /// A key hint: the bound key as a keycap, then what it does.
    /// Use for "press E to use"-style instructions.
    /// </summary>
    public static UiBox KeyHint(this ref UiFrame ui, string key, string token)
    {
        var row = ui.Row(Space.S);
        using (row)
        {
            using (ui.Box().Fill(Tone.SurfaceRaised).Border(Tone.TextMuted).Round(Radius.M).Pad(Space.S, Space.XS).MinWidth(28))
                ui.Data(key, Text.Label, Tone.Text).At(UiAnchor.Centre).TextAlign(Align.Centre);
            ui.Text(token, Text.Body);
        }
        return row;
    }

    /// <summary>The same with the instruction already resolved (a map's hint text is data).</summary>
    public static UiBox KeyHintData(this ref UiFrame ui, string key, string text)
    {
        var row = ui.Row(Space.S);
        using (row)
        {
            if (key.Length > 0)
                using (ui.Box().Fill(Tone.SurfaceRaised).Border(Tone.TextMuted).Round(Radius.M).Pad(Space.S, Space.XS).MinWidth(28))
                    ui.Data(key, Text.Label, Tone.Text).At(UiAnchor.Centre).TextAlign(Align.Centre);
            ui.Data(text, Text.Body);
        }
        return row;
    }

    /// <summary>
    /// A stat: a big number over a small label. Use in summaries (score, kills),
    /// in rows of two to four.
    /// </summary>
    public static UiBox StatBlock(this ref UiFrame ui, int value, string labelToken, Tone tone = Tone.Text)
    {
        var col = ui.Column(Space.None, Align.Start);
        using (col)
        {
            ui.Number(value, Text.Number, tone);
            ui.Label(labelToken);
        }
        return col;
    }

    /// <summary>
    /// A transient notice: a panel with an accent stripe on its left edge. Fades
    /// with <paramref name="alpha"/>. Use for pick-ups, kill notices, short alerts.
    /// </summary>
    public static UiBox Toast(this ref UiFrame ui, string text, Tone stripe = Tone.Accent, float alpha = 1f)
    {
        var row = ui.Row(Space.M).Fill(Tone.Surface).Round(Radius.M).Pad(Space.None, Space.None, Space.L, Space.None)
                    .Opacity(alpha).Height(Size.Du(40));
        using (row)
        {
            ui.Rect(stripe, Size.Du(4), Size.Fill);
            ui.Data(text, Text.Body);
        }
        return row;
    }

    /// <summary>
    /// A row of tabs with the selected one underlined in the accent. Display
    /// half (input arrives in U3): answers the box.
    /// </summary>
    public static UiBox Tabs(this ref UiFrame ui, scoped ReadOnlySpan<string> tokens, int selected)
    {
        var row = ui.Row(Space.XL, Align.End);
        using (row)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                bool on = i == selected;
                using (ui.Column(Space.XS, Align.Stretch).Key(i))
                {
                    ui.Text(tokens[i], Text.Heading, on ? Tone.Text : Tone.TextMuted);
                    ui.Rect(on ? Tone.Accent : Tone.None, Size.Fill, Size.Du(4));
                }
            }
        }
        return row;
    }

    /// <summary>
    /// A form's layout: a label column and a field column. Put a label then its
    /// field, row after row. Use for options dialogs (fields arrive in U4).
    /// </summary>
    public static UiBox FormGrid(this ref UiFrame ui, float labelWidthDu = 200)
        => ui.Grid([Size.Du(labelWidthDu), Size.Fr()], Space.L, Space.M);

    /// <summary>
    /// An empty state: a muted icon, a heading and a line of body text, centred.
    /// Use wherever a list can be empty; never leave a blank panel.
    /// </summary>
    public static UiBox EmptyState(this ref UiFrame ui, UiShape icon, string titleToken, string bodyToken)
    {
        var col = ui.Column(Space.S, Align.Centre).Pad(Space.XL);
        using (col)
        {
            ui.Icon(icon, Tone.TextMuted, 36);
            ui.Spacer(Size.Du(1), Size.Du(4));
            ui.Text(titleToken, Text.Heading, Tone.Text).TextAlign(Align.Centre);
            ui.Text(bodyToken, Text.Body, Tone.TextMuted).TextAlign(Align.Centre).Wrap(400);
        }
        return col;
    }

    /// <summary>
    /// A dialog: a modal overlay with a centred panel, a title bar and a body.
    /// Answers the open body (a vertical stack); closing it closes the dialog.
    /// Put the button row last with <see cref="DialogButtons"/>.
    /// </summary>
    public static UiBox Dialog(this ref UiFrame ui, string key, string titleToken, float widthDu = 640, bool modal = true)
    {
        ui.Overlay(key, modal);
        var panel = ui.Column(Space.None).At(UiAnchor.Centre).Width(Size.Du(widthDu))
                      .Fill(Tone.Surface).Round(Radius.L).Raise(Elevation.Overlay).Clip();
        using (ui.Row(Space.M).Pad(Space.XL, Space.L).Fill(Tone.SurfaceRaised))
        {
            ui.Rect(Tone.Accent, Size.Du(4), Size.Du(24));
            ui.Text(titleToken, Text.Title);
        }
        var body = ui.Column(Space.L).Pad(Space.XL);
        ui.Context.Node(body.Index).ClosesOuter = 2;
        _ = panel;
        return body;
    }

    /// <summary>A right-aligned row for a dialog's buttons: put the primary action last.</summary>
    public static UiBox DialogButtons(this ref UiFrame ui) => ui.Row(Space.M).Align(Align.End, Align.Centre);

    // ---- fields (display and hit-testing; editing arrives with input in U3) ----

    /// <summary>
    /// A single-line text field: a recessed box, the text (game data or the
    /// player's typing), a caret and a selection when <paramref name="caret"/>
    /// is 0 or more, the accent border when focused, and a muted placeholder
    /// when empty. Answers what the pointer did.
    /// </summary>
    public static UiInteraction TextField(this ref UiFrame ui, string key, string text, string placeholderToken,
                                          int caret = -1, int selStart = 0, int selLength = 0, UiSize? width = null,
                                          Text role = Text.Body)
    {
        bool focused = caret >= 0;
        var box = ui.Box(key).Size(width ?? Size.Fill, Size.Du(40)).Fill(Tone.Surface).Round(Radius.M)
                    .Border(focused ? Tone.Accent : Tone.Line, focused ? 2 : 1).Pad(Space.M, Space.None).Clip();
        var hit = ui.Interact(box);
        using (box)
        {
            if (text.Length == 0 && !focused)
                ui.Text(placeholderToken, role, Tone.TextMuted).At(UiAnchor.Left).Height(Size.Fill);
            else
                ui.Data(text, role).At(UiAnchor.Left).Size(Size.Fill, Size.Fill).Caret(caret, selStart, selLength);
        }
        return hit;
    }

    /// <summary>A checkbox and its label; the whole row is the click target.</summary>
    public static UiInteraction Checkbox(this ref UiFrame ui, string key, string labelToken, bool on)
    {
        var row = ui.Row(Space.S).Key(key).Height(Size.Du(32));
        var hit = ui.Interact(row);
        using (row)
        {
            using (ui.Box().Size(Size.Du(20), Size.Du(20)).Round(Radius.S)
                     .Fill(on ? Tone.Accent : Tone.Surface).Border(on ? Tone.Accent : Tone.TextMuted, 1))
            {
                if (on)
                    ui.Shape(UiShape.Check, Tone.OnAccent, 16).At(UiAnchor.Centre);
            }
            ui.Text(labelToken, Text.Body, Tone.Text);
        }
        return hit;
    }

    /// <summary>
    /// A dropdown, closed: the chosen value (game data, e.g. "150 ms") in a field
    /// with a chevron. Open its list with an overlay anchored to the answer.
    /// </summary>
    public static UiBox Dropdown(this ref UiFrame ui, string key, string labelToken, string value, UiSize? width = null)
    {
        var col = ui.Column(Space.XS, Align.Stretch).Width(width ?? Size.Du(160));
        UiBox field;
        using (col)
        {
            ui.Label(labelToken);
            field = ui.Row(Space.S).Key(key).Size(Size.Fill, Size.Du(40)).Fill(Tone.Surface).Round(Radius.M)
                      .Border(Tone.Line, 1).Pad(Space.M, Space.None);
            ui.Interact(field);
            using (field)
            {
                ui.Data(value, Text.Body);
                ui.Flex();
                ui.Shape(UiShape.ChevronDown, Tone.TextMuted, 12);
            }
        }
        return field;
    }

    /// <summary>A determinate progress line with a label and a percentage: refreshes, downloads.</summary>
    public static UiBox Progress(this ref UiFrame ui, float fraction, string labelToken)
    {
        var col = ui.Column(Space.XS, Align.Stretch).Width(Size.Fill);
        using (col)
        {
            using (ui.Row(Space.S).Width(Size.Fill))
            {
                ui.Label(labelToken);
                ui.Flex();
                ui.Text($"{(int)MathF.Round(Math.Clamp(fraction, 0f, 1f) * 100)}%", Text.Label, Tone.Text);
            }
            ui.Meter(fraction, Tone.Accent, Size.Fill, 4);
        }
        return col;
    }

    /// <summary>Stock vgui's tooltip delay: the pointer rests this long before a tooltip shows.</summary>
    public const float TooltipDelaySeconds = 0.5f;

    /// <summary>
    /// A tooltip: a short note (a <c>#Token</c>) on a small raised surface under
    /// <paramref name="target"/>, shown once the pointer has rested on it for
    /// <paramref name="delaySeconds"/> (stock vgui's 500 ms), fading in, and gone
    /// when the pointer leaves; coming back waits the delay again. Pass the
    /// target's own hover (<c>ui.Interact(box).Hovered</c>, a button's answer).
    /// It is an overlay, so no clip of the target's parents cuts it. Answers
    /// whether it drew. Previews pass a delay of 0 to show it at once.
    /// </summary>
    public static bool Tooltip(this ref UiFrame ui, string key, UiBox target, bool hovered, string token,
                               float delaySeconds = TooltipDelaySeconds)
    {
        ref float rested = ref ui.State<float>(key);
        rested = hovered ? rested + ui.Context.Dt : 0f;
        bool due = hovered && rested >= delaySeconds;
        // Keyed apart from the target's own hover fade (a button animates its box's id).
        float shown = ui.Context.Animate(ui.Context.IdOf(key) ^ 0x7007u, due ? 1f : 0f, Motion.Quick);
        if (!due && shown < 0.02f)
            return false;
        using (ui.Overlay(key).AnchorTo(target, UiAnchor.BottomLeft, UiAnchor.TopLeft).Inset(Space.None, Space.XS))
        {
            using (ui.Box().Fill(Tone.SurfaceRaised).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.M, Space.S)
                     .Raise(Elevation.Overlay).Opacity(shown).Name("tooltip"))
                ui.Text(token, Text.Body).Wrap(360);
        }
        return true;
    }
}
