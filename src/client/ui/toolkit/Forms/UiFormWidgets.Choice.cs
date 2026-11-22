using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

public static partial class UiFormWidgets
{
    /// <summary>
    /// The rows a choice's open list shows before it scrolls: stock vgui's
    /// ComboBox shows a fixed number (SetNumberOfEditLines), and the stock
    /// create-server page asks for 12 (CCreateMultiplayerGameServerPage).
    /// A list longer than this filters as the player types.
    /// </summary>
    public const int ChoiceRows = 12;

    /// <summary>One row of a choice's open list, du.</summary>
    public const float ChoiceRowDu = (float)Space.XXL;

    // The list's frame around its rows (its padding; the border is drawn inside it), and its gap from the field.
    private const float ChoiceFrameDu = 2 * (float)Space.XS, ChoiceGapDu = (float)Space.XS;

    private struct ChoiceState
    {
        public bool Open;

        /// <summary>The highlighted row: a place among the rows the filter keeps.</summary>
        public int Highlight;

        /// <summary>Bring the highlight into view this frame; at once when the list has just opened.</summary>
        public bool Reveal, RevealAtOnce;
    }

    // Held apart from ChoiceState, which carries across a UI reload: a string does not.
    private struct ChoiceFilter
    {
        public string? Text;
    }

    /// <summary>
    /// A choice: the chosen name in a field with a chevron. Clicked, a list of
    /// the choices opens under the field (above it when there is no room below),
    /// as wide as the field and <see cref="ChoiceRows"/> rows at most, scrolling
    /// within, with the chosen row in view; a click on a row chooses it, a click
    /// anywhere else closes the list. Open, the list holds the keyboard: Up,
    /// Down, Page Up, Page Down, Home and End move the highlight, Enter chooses
    /// it, Escape closes without a change. Typing into a list longer than
    /// <see cref="ChoiceRows"/> filters it to the names holding the typed text
    /// (any case), shown in the field, Backspace taking it back, the first match
    /// highlighted; a shorter list jumps to the next name starting with the key,
    /// as stock's menus do. Closed and focused, Up, Down, Page Up, Page Down, Home
    /// and End change the choice (stock ComboBox::OnKeyCodeTyped), and typing
    /// opens the list (filtered, when it filters).
    /// The names are data (shown as they are); with <paramref name="tokens"/>
    /// a name starting with # is a token (localised, as copy), so one list can
    /// hold both (a "random map" token above the maps' names).
    /// </summary>
    public static int Choice<TNames>(this ref UiFrame ui, string key, int chosen, int count, TNames names, bool tokens = false)
        where TNames : IUiChoiceNames
    {
        var ctx = ui.Context;
        uint id = ctx.IdOf(key);
        ref var st = ref ctx.State<ChoiceState>(id);
        ref var filter = ref ctx.State<ChoiceFilter>(id);
        bool filters = count > ChoiceRows;
        if (count == 0)
            st.Open = false;

        var field = ui.Row(Space.S).Key(key).Size(Size.Fill, Size.Du(40)).Fill(Tone.Surface).Round(Radius.M)
                      .Border(st.Open ? Tone.Accent : Tone.Line, st.Open ? 2 : 1).Pad(Space.M, Space.None);
        bool clicked = ui.Interact(field).Clicked;
        bool focused = ui.IsFocused(field);
        ui.TryLastRect(field, out var fieldRect);

        if (clicked && count > 0)
        {
            if (st.Open)
                CloseChoice(ref st, ref filter);
            else
                OpenChoice(ref st, ref filter, chosen);
        }
        else if (!st.Open && focused && count > 0)
        {
            chosen = StepChoice(ctx, id, chosen, count);
            var typed = ctx.Typed;
            if (Printable(typed) > 0)
            {
                if (filters)
                {
                    OpenChoice(ref st, ref filter, chosen);
                    filter.Text = Append("", typed);
                    st.Highlight = 0;
                }
                else
                {
                    chosen = TypeAhead(ref ui, names, tokens, count, chosen, typed);
                }
            }
        }

        // An open list's keys and presses, before the field shows what they typed.
        int matches = st.Open ? OpenKeys(ref ui, id, ref st, ref filter, ref chosen, count, names, tokens, fieldRect) : 0;

        string text = st.Open ? filter.Text ?? "" : "";
        using (field)
        {
            if (count == 0)
                ui.Text("#SS_UI_Choice_None", Text.Body, Tone.TextMuted);
            else if (text.Length > 0)
                ui.Data(text, Text.Body).Caret(text.Length, 0, 0);
            else if (tokens && names.Name(chosen).StartsWith('#'))
                ui.Text(names.Name(chosen), Text.Body, Tone.Text);
            else
                ui.Data(names.Name(chosen), Text.Body);
            ui.Flex();
            ui.Shape(st.Open ? UiShape.ChevronUp : UiShape.ChevronDown, Tone.TextMuted, 12);
        }

        if (!st.Open)
        {
            if (focused && count > 0)
                ctx.ClaimKeys(id, all: false);
            return chosen;
        }
        ctx.ClaimKeys(id, all: true);

        // Where it opens: under the field, else above it, else on the side with more room, shortened.
        int rows = Math.Clamp(matches, 1, ChoiceRows);
        bool below = true;
        float widthDu = 240f;
        float s = ui.PixelScale;
        if (fieldRect.W > 0f)
        {
            var area = ctx.PopupArea();
            widthDu = fieldRect.W / s;
            int fitBelow = (int)MathF.Floor(((area.Y + area.H - fieldRect.Y - fieldRect.H) / s - ChoiceGapDu - ChoiceFrameDu) / ChoiceRowDu);
            int fitAbove = (int)MathF.Floor(((fieldRect.Y - area.Y) / s - ChoiceGapDu - ChoiceFrameDu) / ChoiceRowDu);
            if (fitBelow < rows)
            {
                below = fitAbove < rows && fitBelow >= fitAbove;
                if (fitAbove < rows)
                    rows = Math.Max(1, Math.Max(fitBelow, fitAbove));
            }
        }

        ctx.PushScope(key);
        uint regionId = ctx.IdOf("choice-rows");
        if (st.Reveal)
        {
            float top = st.Highlight * ChoiceRowDu;
            float view = rows * ChoiceRowDu;
            // Opening, the chosen row sits mid-list where it can, the list on a whole row;
            // a step only scrolls as far as it must.
            if (st.RevealAtOnce)
            {
                float first = Math.Max(0, st.Highlight - (rows - 1) / 2) * ChoiceRowDu;
                ctx.RevealInScroll(regionId, first, first + view, view, instant: true);
            }
            ctx.RevealInScroll(regionId, top, top + ChoiceRowDu, view, st.RevealAtOnce);
            st.Reveal = st.RevealAtOnce = false;
        }

        bool mouseMoved = ctx.MouseX != ctx.MouseXBefore || ctx.MouseY != ctx.MouseYBefore;
        using (ui.Overlay("choice-list").AnchorTo(field, below ? UiAnchor.BottomLeft : UiAnchor.TopLeft, below ? UiAnchor.TopLeft : UiAnchor.BottomLeft)
                 .Pad(Space.None, below ? Space.XS : Space.None, Space.None, below ? Space.None : Space.XS))
        {
            // Opaque under the field's own surface: what it covers does not show through.
            var box = ui.Column(Space.None).Key("choice-box").Width(Size.Du(widthDu)).Fill(Tone.Window).Round(Radius.M)
                        .Raise(Elevation.Overlay);
            ui.Interact(box);
            using (box)
            using (ui.Column(Space.None).Width(Size.Fill).Fill(Tone.Surface).Round(Radius.M).Border(Tone.Line, 1).Pad(Space.XS))
            {
                if (matches == 0)
                {
                    using (ui.Row(Space.M).Size(Size.Fill, Size.Du(ChoiceRowDu)).Pad(Space.S, Space.None))
                        ui.Text("#SS_UI_Choice_NoMatch", Text.Body, Tone.TextMuted);
                }
                else
                {
                    using var region = ui.Scroll("choice-rows").Size(Size.Fill, Size.Du(rows * ChoiceRowDu));
                    var (first, last) = ctx.VisibleRows(regionId, ctx.Node(region.Index).ScrollOffset, matches, ChoiceRowDu, false);
                    if (first > 0)
                        ui.Spacer(Size.Fill, Size.Du(first * ChoiceRowDu));
                    int place = 0;
                    for (int i = 0; i < count && place < last; i++)
                    {
                        if (filters && text.Length > 0 && !Matches(ref ui, names, tokens, i, text))
                            continue;
                        int at = place++;
                        if (at < first)
                            continue;
                        bool lit = at == st.Highlight;
                        var row = ui.Row(Space.M).Key(i).Size(Size.Fill, Size.Du(ChoiceRowDu)).Pad(Space.S, Space.None).Round(Radius.S)
                                    .Fill(lit ? Tone.SurfaceRaised : Tone.None);
                        var hit = ui.Interact(row);
                        using (row)
                        {
                            if (tokens && names.Name(i).StartsWith('#'))
                                ui.Text(names.Name(i), Text.Body, i == chosen ? Tone.Accent : Tone.Text);
                            else
                                ui.Data(names.Name(i), Text.Body, i == chosen ? Tone.Accent : Tone.Text).Elide();
                        }
                        if (hit.Hovered && mouseMoved)
                            st.Highlight = at;
                        if (hit.Clicked)
                        {
                            chosen = i;
                            CloseChoice(ref st, ref filter);
                        }
                    }
                    if (last < matches)
                        ui.Spacer(Size.Fill, Size.Du((matches - last) * ChoiceRowDu));
                }
            }
        }
        ctx.PopScope();
        return chosen;
    }


    /// <summary>
    /// An open list's input this frame: a press elsewhere, Tab, Escape and Enter
    /// close it (Enter choosing the highlighted row); typing edits the filter;
    /// the arrows move the highlight. Answers how many names the filter keeps.
    /// </summary>
    private static int OpenKeys<TNames>(ref UiFrame ui, uint id, ref ChoiceState st, ref ChoiceFilter filter, ref int chosen,
                                        int count, TNames names, bool tokens, in UiRect fieldRect)
        where TNames : IUiChoiceNames
    {
        var ctx = ui.Context;
        bool filters = count > ChoiceRows;
        // A press anywhere but the field and the list closes it (stock's menu loses its focus).
        uint boxId = UiContext.Hash(id, "choice-box");
        if (ctx.DownThisFrame && !Inside(fieldRect, ctx.DownX, ctx.DownY)
            && !(ctx.TryLastRect(boxId, out var listRect) && Inside(listRect, ctx.DownX, ctx.DownY)))
        {
            CloseChoice(ref st, ref filter);
            return 0;
        }
        // Tab takes the focus on, and the list goes with it.
        if (ui.ButtonsPressed.Contains((int)UiKey.Tab))
        {
            CloseChoice(ref st, ref filter);
            return 0;
        }

        // The keys, held since the list opened.
        if (ctx.ConsumeOwnedKey(id, (int)UiKey.Escape))
        {
            CloseChoice(ref st, ref filter);
            return 0;
        }
        string text = filter.Text ?? "";
        var owned = ctx.OwnedTyped(id);
        bool edited = false;
        if (filters && ctx.ConsumeOwnedKey(id, (int)UiKey.Backspace) && text.Length > 0)
        {
            text = text[..^1];
            edited = true;
        }
        if (filters && Printable(owned) > 0)
        {
            text = Append(text, owned);
            edited = true;
        }
        if (edited)
        {
            filter.Text = text;
            st.Highlight = text.Length == 0 ? chosen : 0;
            st.Reveal = st.RevealAtOnce = true;
        }

        int matches = filters && text.Length > 0 ? CountMatches(ref ui, names, tokens, count, text) : count;
        if (!filters && Printable(owned) > 0)
        {
            st.Highlight = TypeAhead(ref ui, names, tokens, count, st.Highlight, owned);
            st.Reveal = true;
        }
        int moved = StepHighlight(ctx, id, st.Highlight, matches);
        if (moved != st.Highlight)
        {
            st.Highlight = moved;
            st.Reveal = true;
        }
        st.Highlight = matches == 0 ? 0 : Math.Clamp(st.Highlight, 0, matches - 1);
        if (ctx.ConsumeOwnedKey(id, (int)UiKey.Enter) && matches > 0)
        {
            chosen = Nth(ref ui, names, tokens, count, text, st.Highlight);
            CloseChoice(ref st, ref filter);
            return 0;
        }
        return matches;
    }

    /// <summary>
    /// Opens the list of the choice under <paramref name="key"/> (in the scope
    /// it is described in), as a click on its field does, with
    /// <paramref name="filter"/> typed: what a sample shows the list by.
    /// </summary>
    public static void OpenChoice(this ref UiFrame ui, string key, int chosen, string filter = "")
    {
        uint id = ui.Context.IdOf(key);
        ref var st = ref ui.Context.State<ChoiceState>(id);
        ref var f = ref ui.Context.State<ChoiceFilter>(id);
        OpenChoice(ref st, ref f, chosen);
        if (filter.Length > 0)
        {
            f.Text = filter;
            st.Highlight = 0;
        }
    }

    private static void OpenChoice(ref ChoiceState st, ref ChoiceFilter filter, int chosen)
    {
        st.Open = true;
        st.Highlight = Math.Max(0, chosen);
        st.Reveal = st.RevealAtOnce = true;
        filter.Text = null;
    }

    private static void CloseChoice(ref ChoiceState st, ref ChoiceFilter filter)
    {
        st.Open = false;
        filter.Text = null;
    }

    /// <summary>A closed, focused choice's keys: the choice they step to.</summary>
    private static int StepChoice(UiContext ctx, uint id, int chosen, int count)
    {
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.Down)) chosen++;
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.Up)) chosen--;
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.PageDown)) chosen += ChoiceRows;
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.PageUp)) chosen -= ChoiceRows;
        if (ctx.ConsumeOwnedKey(id, (int)UiKey.Home)) chosen = 0;
        if (ctx.ConsumeOwnedKey(id, (int)UiKey.End)) chosen = count - 1;
        return Math.Clamp(chosen, 0, count - 1);
    }

    /// <summary>An open list's keys: the row the highlight steps to (unclamped when nothing moved).</summary>
    private static int StepHighlight(UiContext ctx, uint id, int at, int matches)
    {
        int was = at;
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.Down)) at++;
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.Up)) at--;
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.PageDown)) at += ChoiceRows - 1;
        while (ctx.ConsumeOwnedKey(id, (int)UiKey.PageUp)) at -= ChoiceRows - 1;
        if (ctx.ConsumeOwnedKey(id, (int)UiKey.Home)) at = 0;
        if (ctx.ConsumeOwnedKey(id, (int)UiKey.End)) at = matches - 1;
        return at == was || matches == 0 ? was : Math.Clamp(at, 0, matches - 1);
    }

    /// <summary>The name after <paramref name="from"/> (wrapping) that starts with the last key typed: stock Menu::OnKeyTyped.</summary>
    private static int TypeAhead<TNames>(ref UiFrame ui, TNames names, bool tokens, int count, int from, ReadOnlySpan<char> typed)
        where TNames : IUiChoiceNames
    {
        char c = '\0';
        foreach (char t in typed)
            if (!char.IsControl(t))
                c = t;
        if (c == '\0' || count == 0)
            return from;
        ReadOnlySpan<char> key = [c];
        for (int k = 1; k <= count; k++)
        {
            int i = (from + k) % count;
            if (Shown(ref ui, names, tokens, i).StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return from;
    }

    private static int CountMatches<TNames>(ref UiFrame ui, TNames names, bool tokens, int count, string text)
        where TNames : IUiChoiceNames
    {
        int n = 0;
        for (int i = 0; i < count; i++)
            if (Matches(ref ui, names, tokens, i, text))
                n++;
        return n;
    }

    /// <summary>The choice at <paramref name="place"/> among the names the filter keeps.</summary>
    private static int Nth<TNames>(ref UiFrame ui, TNames names, bool tokens, int count, string text, int place)
        where TNames : IUiChoiceNames
    {
        for (int i = 0; i < count; i++)
        {
            if (text.Length > 0 && !Matches(ref ui, names, tokens, i, text))
                continue;
            if (place-- == 0)
                return i;
        }
        return 0;
    }

    private static bool Matches<TNames>(ref UiFrame ui, TNames names, bool tokens, int i, string text)
        where TNames : IUiChoiceNames
        => Shown(ref ui, names, tokens, i).Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name as the list shows it: a token localised.</summary>
    private static ReadOnlySpan<char> Shown<TNames>(ref UiFrame ui, TNames names, bool tokens, int i)
        where TNames : IUiChoiceNames
    {
        string name = names.Name(i);
        return tokens && name.StartsWith('#') ? ui.Localised(name) : name;
    }

    private static int Printable(ReadOnlySpan<char> typed)
    {
        int n = 0;
        foreach (char c in typed)
            if (!char.IsControl(c))
                n++;
        return n;
    }

    private static string Append(string text, ReadOnlySpan<char> typed)
    {
        Span<char> add = stackalloc char[Printable(typed)];
        int k = 0;
        foreach (char c in typed)
            if (!char.IsControl(c))
                add[k++] = c;
        return string.Concat(text.AsSpan(), add);
    }

    private static bool Inside(in UiRect r, float x, float y)
        => r.W > 0f && x >= r.X && y >= r.Y && x < r.X + r.W && y < r.Y + r.H;
}
