using System;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The character sheet (plan_tf2_rpg C.1, descent_ui.md §2.3): the class/level
/// heading, the four attribute rows with their + buttons, the derived number
/// plates, the resist bars, Australium, and the points-to-distribute chip.
/// Gold is the base (at cap; a cursed affix dips the now-value below base into
/// red), blue is a raised now-value — the <see cref="AttributeRow"/> part owns
/// that ink rule. The window is the plan's 880x560 spec, min 640x480; below
/// 700 du wide the two columns become one and the derived stats follow the
/// attributes (the plan's narrow rule). C.1's shift = 5 bulk spend has no
/// modifier-key surface on this toolkit, so the points bar carries a
/// "spend per click" toggle that multiplies every + button (1 / 5).
/// </summary>
public sealed class CharSheet : UiScreen, IUiWindowed
{
    /// <summary>The design width under which the sheet drops to a single column.</summary>
    public const float NarrowBelowDu = 740;
    private const float ColDu = 328;
    private const int PlateDu = 112;
    private const int RowDu = 32;
    private const int DerivedRowDu = 24;
    private const int ButtonDu = 40;
    private const int SpendButtonKey = 3001;

    private readonly UiWindows _windows;
    private readonly IDescentSheet _sheet;
    private readonly IDescentSheetActions _actions;
    private readonly CoinLabel _coin;
    private readonly IDescentSheetHover? _hover;
    private readonly HintTip? _hint;

    private int _spend = 1;
    private float _pulsePhase;
    private UiBox _anchor;
    private bool _hasAnchor;

    public CharSheet(
        UiWindows windows,
        IDescentSheet sheet,
        IDescentSheetActions actions,
        CoinLabel coin,
        IDescentSheetHover? hover = null,
        HintTip? hint = null)
    {
        _windows = windows;
        _sheet = sheet;
        _actions = actions;
        _coin = coin;
        _hover = hover;
        _hint = hint;
    }

    public override string Name => "charsheet";
    public override UiLayerKind Layer => UiLayerKind.Menu;
    public string WindowId => "charsheet";

    /// <summary>The plan's spec at a design size: 880x560 (min 640x480), clamped to a small desk.</summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(
            MathF.Min(880, MathF.Round(screenW * 0.9f)), MathF.Min(560, MathF.Round(screenH * 0.9f)),
            640, 480, screenW, screenH, NarrowBelowDu);

    public override void Describe(ref UiFrame ui)
    {
        _hasAnchor = false;
        using (ui.Window(_windows, WindowId, "#Descent_Sheet_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            // The scroll holds the sheet; the commit footer is its sibling
            // under it, not scrolled content: Apply/Reset must be reachable
            // without dragging the body first (a footer you have to scroll
            // to is a button you never press).
            UiBox bodyBox;
            using (var body = ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Fill))
            {
                bodyBox = body;
                using (ui.Scroll("body", Space.None).Size(Size.Fill, Size.Fill))
                using (ui.Column(Space.None, Align.Stretch).Pad(Space.M, Space.None))
                {
                    Header(ref ui);
                    ui.Divider();
                    if (ui.Narrow)
                    {
                        using (ui.Column(Space.M, Align.Stretch))
                        {
                            Attributes(ref ui);
                            Derived(ref ui);
                        }
                    }
                    else
                    {
                        // The columns sit at their fixed spec widths, one left and one
                        // right: the derived plates' right edge is the window's, where
                        // the Australium coin sits, so the numbers read as one column.
                        using (ui.Row(Space.M, Align.Start).Width(Size.Fill))
                        {
                            using (ui.Column(Space.S, Align.Stretch).Width(Size.Du(ColDu)))
                                Attributes(ref ui);
                            ui.Flex();
                            using (ui.Column(Space.S, Align.Stretch).Width(Size.Du(ColDu)))
                                Derived(ref ui);
                        }
                    }
                    PointsBar(ref ui);
                }
                CommitFooter(ref ui);
            }

            // The hover hint last: an overlay above the scrolled body,
            // anchored to the row the view-model reports hovered (the
            // Inventory's tooltip pattern; the sample fake forces the report,
            // so a pointer-less preview still draws it). The hint is the
            // shared HintTip part; its content is the host's view-model.
            if (_hover is { Active: true } && _hint is not null)
            {
                var (at, self) = _hasAnchor
                    ? (UiAnchor.BottomRight, UiAnchor.TopLeft)
                    : (UiAnchor.BottomRight, UiAnchor.TopRight);
                using (ui.Overlay("hint").AnchorTo(_hasAnchor ? _anchor : bodyBox, at, self)
                           .Inset(Space.None, Space.XS))
                    ui.Sub("hint", _hint);
            }
        }
    }

    /// <summary>Heading: hero name, class, the revive hint, Australium at the right (C.1's left-to-right).</summary>
    private void Header(ref UiFrame ui)
    {
        using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
        {
            ui.Data(_sheet.Name, Text.Heading).Elide();
            ui.Text(_sheet.ClassToken, Text.Body, Tone.TextMuted).Elide();
            if (_sheet.HasPartyReviveHint)
                ui.Text("#Descent_Sheet_Revive_Hint", Text.Caption, Tone.TextMuted).Width(Size.Du(300)).Elide();
            ui.Flex();
            ui.Text("#Descent_Sheet_Australium", Text.Caption, Tone.TextMuted);
            ui.Sub("coin", _coin, new CoinLabelProps(_sheet.Australium, false));
        }
    }

    /// <summary>Left column: the attribute rows under their caption, then the level/experience plates.</summary>
    private void Attributes(ref UiFrame ui)
    {
        ui.Text("#Descent_Sheet_Attributes", Text.Caption, Tone.TextMuted);
        int points = _sheet.AttributePoints;
        for (int i = 0; i < _sheet.AttributeCount; i++)
        {
            var a = _sheet.Attribute(i);
            // Zero points, or a row at its cap counting its staged points,
            // hides the + rather than shows it dead (C.1).
            bool showPlus = points > 0 && a.Base + a.Pending < a.Cap;
            // The sheet's own row, not the AttributeRow part: the part sets its
            // label at a fixed 120du with no .Elide(), which the ids/clipped lint
            // flags when copy is measured as the raw token key. Same ink rule as
            // the part: Now blue above base, red below, Base gold at cap. The
            // staged points ride the Now plate at once — the preview that lets a
            // hero see what a level's points do before the apply below commits.
            var nowPaint = a.Now + a.Pending > a.Base ? DescentPaints.Raised
                         : a.Now < a.Base ? DescentPaints.Lowered : (UiPaint)Tone.Text;
            var basePaint = a.Base >= a.Cap ? DescentPaints.Capped : (UiPaint)Tone.TextMuted;
            using (var row = ui.Row(Space.S, Align.Centre).Key(800 + i))
            {
                // The row is the hover hit target (its 32-du plates make it a
                // tall enough one). The pointer reports it to the view-model;
                // the view-model answers with the hint content, and the row
                // the content names is what the popup anchors to — so the
                // preview, which forces the content with no pointer, hangs
                // the tip on the right row too.
                var rowHit = ui.Interact(row);
                if (rowHit.Hovered)
                    _actions.Hover(i, -1);
                if (_hover is not null && _hover.HoverAttribute == i)
                {
                    _anchor = row;
                    _hasAnchor = true;
                }
                ui.Text(a.NameToken, Text.Body).Width(Size.Du(120)).Elide();
                AttrPlate(ref ui, 700 + 2 * i, a.Base, basePaint);
                AttrPlate(ref ui, 701 + 2 * i, a.Now + a.Pending, nowPaint);
                if (showPlus)
                {
                    using var plus = ui.Box().Key(500 + i).Size(Size.Du(ButtonDu), Size.Du(RowDu))
                                 .Fill(Tone.SurfaceRaised).Border(Tone.Line, 1).Round(Radius.M);
                    var hit = ui.Interact(plus);
                    ui.Shape(UiShape.Cross, Tone.Accent, 16).At(UiAnchor.Centre);
                    if (hit.Clicked)
                        _actions.Stage(i, _spend);
                }
            }
        }
        Plate(ref ui, "#Descent_Sheet_Level", (UiPaint)Tone.Text, _sheet.Level, 0, PlateKind.Plain);
        Plate(ref ui, "#Descent_Sheet_Experience", (UiPaint)Tone.Text, _sheet.Xp, 0, PlateKind.Plain);
        Plate(ref ui, "#Descent_Sheet_Next_Level", (UiPaint)Tone.Text, _sheet.XpToNext, 0, PlateKind.Plain);
    }

    /// <summary>Right column: the derived plates (Life/Mana red under max, C.1) and the resist bars.</summary>
    private void Derived(ref UiFrame ui)
    {
        ui.Text("#Descent_Sheet_Combat", Text.Caption, Tone.TextMuted);
        // The vitals plates show the plan's answer: with points staged they
        // read the projected maxima in the preview ink (the view-model owns
        // the formula; the screen only prints it), so the hero sees the whole
        // shape of a level's points before applying them.
        int lifeMax = _sheet.PendingLifeMax;
        int manaMax = _sheet.PendingManaMax;
        Plate(ref ui, "#Descent_Hud_Life", lifeMax != _sheet.LifeMax ? DescentPaints.Raised
              : _sheet.Life < _sheet.LifeMax ? DescentPaints.Lowered : (UiPaint)Tone.Text,
            _sheet.Life, lifeMax, PlateKind.Ratio, 0);
        Plate(ref ui, "#Descent_Hud_Mana", manaMax != _sheet.ManaMax ? DescentPaints.Raised
              : _sheet.Mana < _sheet.ManaMax ? DescentPaints.Lowered : (UiPaint)Tone.Text,
            _sheet.Mana, manaMax, PlateKind.Ratio, 1);
        Plate(ref ui, "#Descent_Sheet_Armour", (UiPaint)Tone.Text, _sheet.Armour, 0, PlateKind.Plain, 2);
        Plate(ref ui, "#Descent_Sheet_Crit", (UiPaint)Tone.Text, _sheet.CritPercent, 0, PlateKind.Percent, 3);
        Plate(ref ui, "#Descent_Sheet_Damage", (UiPaint)Tone.Text, _sheet.DamageMin, _sheet.DamageMax, PlateKind.Range, 4);
        bool anyAtCap = false;
        for (int i = 0; i < _sheet.ResistCount; i++)
            anyAtCap |= _sheet.ResistPercent(i) >= _sheet.ResistCap;
        // The at-cap note rides the caption row: a row of its own at the bottom
        // is the line the design-height window can't spare.
        using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
        {
            ui.Text("#Descent_Sheet_Resists", Text.Caption, Tone.TextMuted);
            ui.Flex();
            if (anyAtCap)
                ui.Text("#Descent_Sheet_Resists_At_Cap", Text.Caption).Color(DescentPaints.Capped);
        }
        for (int i = 0; i < _sheet.ResistCount; i++)
        {
            int percent = _sheet.ResistPercent(i);
            bool capped = percent >= _sheet.ResistCap;
            // The sheet's own resist row, not the ResistRow part: the part sets its
            // percent at the Number role, which makes the row 59du and pushes the
            // plates past the plan's 560du window (they scrolled at the design
            // size). Same pieces — label, meter, gold at cap — at Body role.
            using (ui.Row(Space.S, Align.Centre).Size(Size.Fill, Size.Du(DerivedRowDu)))
            {
                ui.Text(_sheet.ResistLabel(i), Text.Body).Width(Size.Du(120)).Elide();
                ui.Meter(Math.Clamp(percent / 100f, 0f, 1f), capped ? DescentPaints.Capped : Tone.Accent, Size.Du(88), 4);
                ui.Flex();
                ui.Text($"{percent} %", Text.Body)
                    .Color(capped ? DescentPaints.Capped : (UiPaint)Tone.Text).TextAlign(Align.End);
            }
        }
    }

    /// <summary>Bottom row: the points number, a pulsing chip while points remain (§2.3), and the spend-per-click toggle.</summary>
    private void PointsBar(ref UiFrame ui)
    {
        int points = _sheet.AttributePoints;
        using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
        {
            ui.Text("#Descent_Sheet_Points", Text.Caption, Tone.TextMuted);
            using (ui.Box().Size(Size.Du(PlateDu), Size.Du(RowDu)).Fill(Tone.Surface)
                     .Border(Tone.Line, 1).Round(Radius.S).Pad(Space.S, Space.None))
                ui.Number(points, Text.Body).Color(points > 0 ? DescentPaints.Raised : (UiPaint)Tone.TextMuted)
                    .At(UiAnchor.Right).Size(Size.Fill, Size.Fill).TextAlign(Align.End);
            if (points > 0)
            {
                _pulsePhase += ui.Dt;
                if (_pulsePhase > 2_000f)
                    _pulsePhase -= 2_000f;
                float wave = 0.55f + 0.45f * MathF.Sin(_pulsePhase * 3f);
                ui.Shape(UiShape.Dot, DescentPaints.Raised, 12).Opacity(ui.Animate("pts", wave));
            }
            ui.Flex();
            ui.Text("#Descent_Sheet_Spend", Text.Caption, Tone.TextMuted);
            using (var spend = ui.Box().Key(SpendButtonKey).Size(Size.Du(ButtonDu), Size.Du(RowDu))
                         .Fill(Tone.SurfaceRaised).Border(_spend > 1 ? Tone.Accent : Tone.Line, 1).Round(Radius.S))
            {
                var hit = ui.Interact(spend);
                using (spend)
                    ui.Number(_spend, Text.Body).Color(Tone.Text).At(UiAnchor.Centre)
                        .Size(Size.Fill, Size.Fill).TextAlign(Align.Centre);
                if (hit.Clicked)
                    _spend = _spend == 1 ? 5 : 1;
            }
        }
    }

    /// <summary>
    /// The commit footer: a divider, then the staged count and the two
    /// buttons that end the plan, at the window's bottom-right — where
    /// confirmations sit in every other panel of this kind. + buttons only
    /// stage — the server is Apply's endpoint — so the whole window is a
    /// preview until Apply: the Now plates read base+pending, and Life/Mana
    /// their projected maxima, the moment a + is clicked. Reset hands every
    /// staged point back. The footer hides only in the sheet's resting state
    /// (C.1's plain sheet: no points left, nothing staged); with points left
    /// but nothing staged the buttons sit disabled, which is what teaches
    /// that Apply commits a plan.
    /// </summary>
    private void CommitFooter(ref UiFrame ui)
    {
        int staged = 0;
        for (int i = 0; i < _sheet.AttributeCount; i++)
            staged += _sheet.Attribute(i).Pending;
        if (staged == 0 && _sheet.AttributePoints == 0)
            return;
        ui.Divider();
        using (ui.Row(Space.S, Align.Centre).Width(Size.Fill).Pad(Space.M, Space.None))
        {
            if (staged > 0)
            {
                // The count at Body size, like every other value on the
                // sheet: the count is one number among the rows' numbers,
                // not a second headline beside the 46-du buttons. The token
                // is its own ui.Text: an interpolated string would hide it
                // from the localiser.
                ui.Number(staged, Text.Body).Color(DescentPaints.Raised);
                ui.Text("#Descent_Sheet_Staged", Text.Caption, Tone.TextMuted).Elide();
            }
            ui.Flex();
            if (ui.Button("reset", "#Descent_Sheet_Reset", enabled: staged > 0).Clicked)
                _actions.Reset();
            if (ui.Button("apply", "#Descent_Sheet_Apply", primary: true, enabled: staged > 0).Clicked)
                _actions.Apply();
        }
    }

    private enum PlateKind : byte { Plain, Ratio, Percent, Range }

    /// <summary>
    /// One derived row — caption at the left, value in a recessed plate at the
    /// right (the C.1 plate row). Drawn, not subbed: the parts' NumberPlate
    /// takes a view-model string, and these are live numbers, so the value
    /// formats straight into the frame (AttributeRow's own plates do this).
    /// Every plate in the column takes the Body role: one column is one
    /// scale. Number-role digits made Level and Armour twice the size of
    /// their neighbours' 41300 and 17 % — read as two different tables,
    /// not one sheet. The attribute plates keep the big numerals: they are
    /// the sheet's headline ladder, all four alike.
    private void Plate(ref UiFrame ui, string labelToken, UiPaint paint, long a, long b, PlateKind kind,
                       int hoverIndex = -1)
    {
        using (var row = ui.Row(Space.S, Align.Centre).Width(Size.Fill))
        {
            // The five combat plates (hoverIndex 0..4) are hover hit targets
            // like the attribute rows; the Level/Exp/Next plates (-1) are not
            // — their meaning is their caption. Resist rows stay un-hovered:
            // at 24 du they are under the lint's interactive hit target, and
            // a labelled meter needs no popup to read.
            if (hoverIndex >= 0)
            {
                row.Key(900 + hoverIndex);
                var hit = ui.Interact(row);
                if (hit.Hovered)
                    _actions.Hover(-1, hoverIndex);
                if (_hover is not null && _hover.HoverDerived == hoverIndex)
                {
                    _anchor = row;
                    _hasAnchor = true;
                }
            }
            ui.Text(labelToken, Text.Body, Tone.TextMuted);
            ui.Flex();
            using (ui.Box().Size(Size.Du(PlateDu), Size.Du(RowDu)).Fill(Tone.Surface)
                     .Border(Tone.Line, 1).Round(Radius.S).Pad(Space.S, Space.None))
            {
                var value = kind switch
                {
                    PlateKind.Ratio => ui.Text($"{(int)a} / {(int)b}", Text.Body),
                    PlateKind.Percent => ui.Text($"{(int)a} %", Text.Body),
                    PlateKind.Range => ui.Text($"{(int)a} - {(int)b}", Text.Body),
                    _ => ui.Text($"{a}", Text.Body),
                };
                value.Color(paint).At(UiAnchor.Right).Size(Size.Fill, Size.Fill).TextAlign(Align.End).Elide();
            }
        }
    }

    /// <summary>One [value] plate of an attribute row: the recessed field at the
    /// part's own size (72du), holding a live number.</summary>
    private static void AttrPlate(ref UiFrame ui, int key, int value, UiPaint ink)
    {
        using (ui.Box().Key(key).Size(Size.Du(72), Size.Du(RowDu)).Fill(Tone.Surface).Border(Tone.Line, 1)
                 .Round(Radius.S).Pad(Space.S, Space.None))
            ui.Number(value, Text.Number).Color(ink).At(UiAnchor.Right).Size(Size.Fill, Size.Fill).TextAlign(Align.End);
    }
}
