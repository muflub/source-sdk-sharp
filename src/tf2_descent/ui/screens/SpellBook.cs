using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The spellbook window (docs/plans/descent_ui.md §2.6, R27): read-only state —
/// casts are belt/keys, the book only shows what is known. Two columns: the
/// known spells as list rows (rank pips, the Smarts requirement caption in
/// Danger tone when current Smarts dips below, mana cost, an Inactive chip on
/// such a row) and the scroll stacks as slot chips with <c>n / max</c> counts.
/// Nothing known and no scrolls gives the empty state; scrolls alone leave the
/// known column at its "no tomes read yet" caption.
/// Narrow (or a window under 560 du): the columns stack, the scroll chips keep
/// their row grid.
/// </summary>
public sealed class SpellBook : UiScreen, IUiWindowed
{
    // The book's grid, in du. A known-spell row carries name / pips / meta;
    // a scroll chip carries name (wrapped) / rank / count.
    private const float RowH = 100, RowGap = 8;
    private const float ChipW = 104, ChipH = 108, ChipGap = 12;
    private const float HeaderH = 32, ColGap = 16, HeadGap = 8;

    private readonly UiWindows _windows;
    private readonly IDescentSpells _spells;
    private readonly IDescentSpellActions? _actions;
    private readonly IDescentSpellHover? _hover;
    private readonly HintTip? _hint;
    private readonly RankPips _pips = new();

    // The hovered row's box, captured during the content draw for the
    // screen's overlay (the Inventory's tooltip pattern).
    private UiBox _anchor;
    private bool _hasAnchor;

    public SpellBook(UiWindows windows, IDescentSpells spells,
                     IDescentSpellActions? actions = null,
                     IDescentSpellHover? hover = null, HintTip? hint = null)
    {
        _windows = windows;
        _spells = spells;
        _actions = actions;
        _hover = hover;
        _hint = hint;
    }

    public override string Name => "spellbook";

    public string WindowId => "spellbook";

    /// <summary>
    /// 720 x 520 du by default, at least 460 x 360; narrow below 560 du, where
    /// two 330-du columns no longer fit side by side.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(720, MathF.Round(screenW * 0.92f)), MathF.Min(520, MathF.Round(screenH * 0.92f)),
               460, 360, screenW, screenH, 560);

    public override void Describe(ref UiFrame ui)
    {
        _hasAnchor = false;
        using (ui.Window(_windows, WindowId, "#Descent_Spell_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            // Count the two kinds once (no allocation in describe): the lists are
            // known spells (!IsScroll) and scroll stacks (IsScroll).
            int known = 0, scrolls = 0;
            for (int i = 0; i < _spells.SpellCount; i++)
            {
                if (_spells.Spell(i).IsScroll) scrolls++;
                else known++;
            }

            if (known == 0 && scrolls == 0)
            {
                using (ui.Column(Space.L, Align.Centre).Size(Size.Fill, Size.Fill).At(UiAnchor.TopLeft))
                    ui.EmptyState(UiShape.Dot, "#Descent_Spell_Empty_Title", "#Descent_Spell_Empty_Body")
                      .Width(Size.Fill);
                return;
            }

            bool narrow = ui.Narrow;
            // Same sizing rule as the window spec: the content column matches the
            // window's default width, so the grid never spills the frame.
            float usable = MathF.Min(720, MathF.Round(ui.DesignWidth * 0.92f)) - 2 * (int)Space.M;
            float colW = narrow ? usable : (usable - ColGap) / 2f;
            float knownX = 0;
            float scrollX = narrow ? 0 : colW + ColGap;
            float scrollBlockTop = narrow ? HeaderH + knownRowsH(known) + (int)Space.L : 0;
            float knownH = HeaderH + knownRowsH(known);
            float scrollsH = HeaderH + scrollRowsH(scrolls, colW);
            float total = MathF.Max(knownH, scrollBlockTop + scrollsH) + HeadGap;

            using (ui.Scroll("book", Space.None).Size(Size.Fill, Size.Fill))
            using (ui.Box().Size(Size.Du(usable), Size.Du(total)))
            {
                // Known spells: the list rows.
                using (ui.Box().Key("known").Size(Size.Du(colW), Size.Du(knownH)).At(UiAnchor.TopLeft)
                         .Inset(new UiLength(knownX, false), new UiLength(0, false)))
                {
                    DrawColumnHeader(ref ui, "#Descent_Spell_Known", HeaderH);
                    if (known == 0)
                        ui.Text("#Descent_Spell_NoKnown", Text.Caption, Tone.TextMuted)
                          .At(UiAnchor.TopLeft).Inset(new UiLength(0, false), new UiLength(HeaderH + HeadGap, false));
                    int row = 0;
                    for (int i = 0; i < _spells.SpellCount; i++)
                    {
                        var s = _spells.Spell(i);
                        if (s.IsScroll) continue;
                        DrawSpellRow(ref ui, s, i, colW, HeaderH + HeadGap + row * (RowH + RowGap));
                        row++;
                    }
                }
                // Scroll stacks: the chip grid.
                using (ui.Box().Key("scrolls").Size(Size.Du(colW), Size.Du(scrollsH)).At(UiAnchor.TopLeft)
                         .Inset(new UiLength(scrollX, false), new UiLength(scrollBlockTop, false)))
                {
                    DrawColumnHeader(ref ui, "#Descent_Spell_Scrolls", HeaderH);
                    if (scrolls == 0)
                        ui.Text("#Descent_Spell_Scrolls_Empty", Text.Caption, Tone.TextMuted)
                          .At(UiAnchor.TopLeft).Inset(new UiLength(0, false), new UiLength(HeaderH + HeadGap, false));
                    int slot = 0;
                    int cols = Math.Max(1, (int)((colW + ChipGap) / (ChipW + ChipGap)));
                    for (int i = 0; i < _spells.SpellCount; i++)
                    {
                        var s = _spells.Spell(i);
                        if (!s.IsScroll) continue;
                        DrawScrollChip(ref ui, s, i, (slot % cols) * (ChipW + ChipGap),
                                       HeaderH + HeadGap + (slot / cols) * (ChipH + ChipGap));
                        slot++;
                    }
                }
            }

            // The hint last: the overlay draws above the scrolled book,
            // anchored to the row or chip the view-model names (the fake
            // forces it for the preview; a real pointer reports through
            // IDescentSpellActions.Hover).
            if (_hover is { Active: true } && _hint is not null)
            {
                var (at, self) = _hasAnchor
                    ? (UiAnchor.BottomRight, UiAnchor.TopLeft)
                    : (UiAnchor.BottomRight, UiAnchor.TopRight);
                using (ui.Overlay("hint").AnchorTo(_anchor, at, self)
                           .Inset(Space.None, Space.XS))
                    ui.Sub("hint", _hint);
            }
        }
    }

    /// <summary>The column's heading row (Heading role, left aligned).</summary>
    private static void DrawColumnHeader(ref UiFrame ui, string token, float h)
    {
        using (ui.Row(Space.XS, Align.Centre).Size(Size.Fill, Size.Du(h)).At(UiAnchor.TopLeft))
            ui.Text(token, Text.Heading, Tone.Text).Elide();
    }

    /// <summary>One known-spell row: the name, the rank pips, the mana and Smarts captions.
    /// A row whose Smarts requirement is unmet dims and wears an Inactive chip.</summary>
    private void DrawSpellRow(ref UiFrame ui, in DescentSpell s, int ordinal, float colW, float y)
    {
        var box = ui.Box().Key(ordinal).Size(Size.Du(colW), Size.Du(RowH)).At(UiAnchor.TopLeft)
                    .Inset(new UiLength(0, false), new UiLength(y, false))
                    .Fill(Tone.Surface).Round(Radius.M).Border(Tone.Line, 1);
        if (!s.SmartsMet)
            box = box.Opacity(0.72f);
        var hit = ui.Interact(box);
        if (hit.Hovered)
            _actions?.Hover(ordinal);
        if (_hover is not null && _hover.HoveredSpell == ordinal)
        {
            _anchor = box;
            _hasAnchor = true;
        }
        using (box.Pad(Space.S, Space.S))
        {
            using (ui.Column(Space.S, Align.Start).Size(Size.Fill, Size.Fill).At(UiAnchor.TopLeft))
            {
                // The name and the Inactive chip share the top row (the BeltStrip's
                // rule): the name takes the width the chip leaves.
                using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
                {
                    ui.Text(s.NameToken, Text.Heading, s.SmartsMet ? Tone.Text : Tone.TextMuted)
                      .Width(Size.Fill).Elide();
                    if (!s.SmartsMet)
                        ui.Text("#Descent_Tip_Inactive", Text.Caption, Tone.Danger);
                }
                // -ordinal - 1000: a key space of its own. The int space is
                // the rows' and chips' hover keying (their ids answer the
                // view-model), and a string key would allocate per frame
                // (the part's key must not land in either space: the lint's
                // ids rule; describe allocates nothing: SSUI001).
                ui.Sub(-ordinal - 1000, _pips, new RankPipsProps(s.Rank, s.MaxRank, 0));
                using (ui.Row(Space.S, Align.Centre))
                {
                    if (s.ManaCost.Length > 0)
                    {
                        ui.Data(s.ManaCost, Text.Caption, Tone.TextMuted);
                        ui.Text("#Descent_Hud_Mana", Text.Caption, Tone.TextMuted);
                    }
                    var tone = s.SmartsMet ? Tone.TextMuted : Tone.Danger;
                    ui.Text("#Descent_Spell_Req_Smarts", Text.Caption, tone);
                    ui.Number(s.SmartsRequirement, Text.Caption, tone);
                }
            }
        }
    }

    /// <summary>One scroll stack as a slot chip: the name wrapped, its fixed rank,
    /// the stack count over the maximum. A hover hit target like the spell
    /// rows: the pointer reports a chip as <c>-ordinal - 1</c> so row and chip
    /// never share a number, and the chip the view-model names owns the
    /// popup's anchor.</summary>
    private void DrawScrollChip(ref UiFrame ui, in DescentSpell s, int ordinal, float x, float y)
    {
        using (var chip = ui.Box().Key(-ordinal - 1).Size(Size.Du(ChipW), Size.Du(ChipH)).At(UiAnchor.TopLeft)
                 .Inset(new UiLength(x, false), new UiLength(y, false))
                 .Fill(Tone.SurfaceRaised).Round(Radius.M).Border(Tone.Line, 1).Pad(Space.S, Space.S))
        {
            var hit = ui.Interact(chip);
            if (hit.Hovered)
                _actions?.Hover(-ordinal - 1);
            if (_hover is not null && _hover.HoveredSpell == -ordinal - 1)
            {
                _anchor = chip;
                _hasAnchor = true;
            }
        {
            using (ui.Column(Space.XS, Align.Centre).Size(Size.Fill, Size.Fill).At(UiAnchor.TopLeft))
            {
                ui.Text(s.NameToken, Text.Caption, Tone.Text).Width(Size.Fill)
                  .TextAlign(Align.Centre).Wrap(ChipW - 2 * (int)Space.S);
                using (ui.Row(Space.XS, Align.Centre))
                {
                    ui.Text("#Descent_Spell_Rank", Text.Caption, Tone.TextMuted);
                    ui.Number(s.Rank, Text.Caption, Tone.Text);
                }
                ui.Text($"{s.ScrollCount} / {s.ScrollStackMax}", Text.Caption, Tone.TextMuted);
            }
        }
        }
    }

    private static float knownRowsH(int known)
        => known == 0 ? 28 : known * RowH + (known - 1) * RowGap;

    private static float scrollRowsH(int scrolls, float colW)
    {
        if (scrolls == 0)
            return 28;
        int cols = Math.Max(1, (int)((colW + ChipGap) / (ChipW + ChipGap)));
        int rows = (scrolls + cols - 1) / cols;
        return rows * ChipH + (rows - 1) * ChipGap;
    }
}
