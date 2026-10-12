using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Parts;

// The descent UI's shared vocabulary (docs/plans/descent_ui.md §3): the parts
// every panel is assembled from, each gallery-tested so it looks right before
// anything places it. ItemSlot, the orbs, the belt, party pills, number
// plates, rank pips, the XP bar. Strings on props are view-model-built (the
// 2i projection shape); Describe never builds one. HUD surfaces are opaque
// (the core Hud's rule): text over the world needs the theme's contrast, not
// a translucent scrim.

/// <summary>What an <see cref="ItemSlot"/> shows. The inspect triple is
/// optional (defaulted so every existing positional construction stands):
/// <see cref="Inspectable"/> cells answer their own corner button, opening
/// the modal for <see cref="InspectCell"/> (the host's owner-screen keying)
/// through <see cref="InspectActions"/>.</summary>
public readonly record struct ItemSlotProps(
    DescentItemView? Item,        // null: empty
    bool Locked,                  // unearned equipment slot: Lock outline
    bool WithdrawOnly,            // 2d's overflow: dimmed, a lock chip
    bool Equipped,                // accent frame instead of the rarity edge
    bool Hovered,
    bool Inspectable = false,     // the corner button opens the inspect modal
    int InspectCell = -1,         // the owner screen's cell key for the button
    IDescentInspectActions? InspectActions = null);

/// <summary>
/// One item cell (D4's one-cell slots): a recessed well, the item's base name
/// along the top in a rarity-coloured edge (D5), a durability hairline along
/// the bottom, and chips — Star for Unusual/Australium,
/// <see cref="UiShape.Cross45"/> for broken, Lock for withdraw-only, "?" for
/// unidentified (D22: the view carries no affixes at all).
/// </summary>
public sealed class ItemSlot : UiPanel<ItemSlotProps>
{
    public const float SizeDu = 48;

    /// <summary>A bottom-row chip: three fill the cell between the rail and the corner inset.</summary>
    private const float ChipDu = 14;

    public override void Describe(ref UiFrame ui, in ItemSlotProps p)
    {
        var frame = p.Hovered ? ((UiPaint)Tone.Accent).WithOpacity(0.9f)
                  : p.Equipped ? (UiPaint)Tone.Accent
                  : (UiPaint)Tone.Line;
        var box = ui.Box().Size(Size.Du(SizeDu), Size.Du(SizeDu)).Round(Radius.M)
                    .Fill(Tone.SurfaceRaised).Border(frame, p.Hovered ? 2 : 1);
        using (box)
        {
            if (p.Locked)
            {
                ui.Shape(UiShape.Lock, Tone.TextMuted, 20).At(UiAnchor.Centre);
                return;
            }
            if (p.Item is not { } item)
                return;

            // The inspect button: a bordered mini-box at the well's right
            // edge, keyed, answering its own ui.Interact (the SkillCard's +
            // badge's language). Only for inspectable cells, and only when
            // the host wired the inspect subject: a screen without it draws
            // no button.
            if (p.Inspectable && p.InspectActions is { } inspect)
            {
                // The button sits at the well's right edge, below the name's
                // band: a 32-du chip cannot fit the corner above it, and a
                // chip whose quads ran under the name's pixels would read as
                // the label's own background (the contrast lint composites
                // the ops drawn before the text). Layered: an intended badge
                // over the corner (the cell's rail/meter/chips precede it),
                // as the SkillCard's + badge is.
                var btn = ui.Box("inspect").Size(Size.Du(32), Size.Du(32)).Round(Radius.M)
                            .At(UiAnchor.TopRight).Inset(Space.XS, Space.S).Layered();
                var btnHit = ui.Interact(btn);
                float btnHover = ui.Animate(btn, btnHit.Hovered ? 1f : 0f, Motion.Quick);
                // Opaque Surface at rest (an opaque base under any name text
                // that runs across the corner: a translucent wash over the
                // busy world reads 1.9:1, the lint says); the accent wash and
                // frame are the hover state, seen only with a pointer there.
                btn.Border(btnHit.Hovered ? Tone.Accent : Tone.Line, 1)
                   .Fill(btnHover > 0.5f
                         ? ((UiPaint)Tone.Accent).WithOpacity(0.14f + 0.21f * btnHover)
                         : (UiPaint)Tone.Window);
                using (btn)
                    ui.Shape(UiShape.Crosshair, btnHit.Hovered ? Tone.Text : Tone.TextMuted, 16)
                      .At(UiAnchor.Centre);
                if (btnHit.Clicked) inspect.OpenInspect(p.InspectCell);
            }

            var paint = DescentPaints.Of(item.Rarity);
            // The rail is an accent under the cell's content, so it is an
            // intended layer (the durability meter's precedent): the alignment
            // and overlap rules read free elements only, and the rail-vs-name
            // pair was the inventory lane's 44 findings - at small scales the
            // 8-du name inset is under 3 px from the rail's flush edge, which
            // reads as near-misaligned. Inset the rail instead and it overlaps
            // the name row: 435 findings. Layered is the honest state.
            ui.Rect(paint, Size.Du(2), Size.Fill).At(UiAnchor.Left).Layered();
            if (!item.Identified)
            {
                // D22: an unidentified item carries no affixes, so it shows as "?" only
                // — a mark beside the name, not a title (the HUD/sheet type budget is 4).
                ui.Data("?", Text.Caption, Tone.Text).At(UiAnchor.Centre).Layered();
                if (p.WithdrawOnly)
                    ui.Shape(UiShape.Lock, Tone.Warning, 16).At(UiAnchor.TopRight).Inset(Space.XS, Space.XS).Layered();
                return;
            }
            // The name runs the cell's full label width (the plan's Elide
            // budget, descent_ui.md 2.4); the chips share one bottom row under
            // it. A lock inside the name's row collapsed "Medigun" to "M...",
            // and a centred broken X ran 6 du into the name's band (the
            // restarted review). The row runs from the bottom-right corner,
            // so the Unusual star keeps its corner; three 14-du chips fill the
            // 42 du between the rail (x 2) and the corner inset (x 44) exactly.
            if (item.BaseName.Length > 0)
                ui.Data(item.BaseName, Text.Caption, Tone.Text).Width(Size.Fill).Elide()
                  .At(UiAnchor.Top).Inset(Space.XS, Space.XS);
            if (p.WithdrawOnly || item.Broken || item.HasUnusual)
                using (ui.Row(Space.None, Align.Centre).At(UiAnchor.BottomRight).Inset(Space.XS, Space.XS))
                {
                    if (p.WithdrawOnly)
                        ui.Shape(UiShape.Lock, Tone.Warning, ChipDu);
                    if (item.Broken)
                        ui.Shape(UiShape.Cross45, Tone.Danger, ChipDu);
                    if (item.HasUnusual)
                        ui.Shape(UiShape.Star, paint, ChipDu);
                }
            if (item.MaxDurability > 0)
            {
                float f = System.Math.Clamp(item.Durability / (float)item.MaxDurability, 0f, 1f);
                // The hairline rides over the rarity rail's corner: an intended layer.
                ui.Meter(f, f <= 0.25f ? Tone.Danger : Tone.Line, Size.Fill, 2).At(UiAnchor.Bottom).Layered();
            }
        }
    }
}

/// <summary>The rarity dot beside a name: colour alone is the ladder (D5).</summary>
public sealed class RarityChip : UiPanel<DescentRarity>
{
    public override void Describe(ref UiFrame ui, in DescentRarity rarity)
        => ui.Shape(UiShape.Dot, DescentPaints.Of(rarity), 12);
}

/// <summary>Where a <see cref="NumberPlate"/> shows its value.</summary>
public readonly record struct NumberPlateProps(string Value, UiPaint Paint, Text Role, float WidthDu);

/// <summary>A Diablo-style number plate: a recessed field, the value right-aligned (C.1's [ 45 ] cells).</summary>
public sealed class NumberPlate : UiPanel<NumberPlateProps>
{
    public override void Describe(ref UiFrame ui, in NumberPlateProps p)
    {
        using (ui.Box().Size(Size.Du(p.WidthDu), Size.Du(32)).Fill(Tone.Surface).Border(Tone.Line, 1)
                 .Round(Radius.S).Pad(Space.S, Space.None))
            ui.Data(p.Value, p.Role).Color(p.Paint).At(UiAnchor.Right).Size(Size.Fill, Size.Fill).TextAlign(Align.End).Elide();
    }
}

/// <summary>
/// Life and mana as the twin orbs, Diablo's bottom-left: an opaque disc that
/// drains from the bottom with the value, the label as a chip at its top, the
/// big number over the maximum at its centre. Under a quarter life the ring
/// reads danger; the number keeps its contrast on the disc.
/// </summary>
public sealed class OrbPair : UiPanel
{
    private readonly IDescentVitals _vitals;

    public OrbPair(IDescentVitals vitals) => _vitals = vitals;

    public override void Describe(ref UiFrame ui)
    {
        // No hero yet (the party screen's join, the first spawn): the orbs have
        // no numbers to show, so the HUD's vitals corner stays empty.
        if (!_vitals.HasHero)
            return;
        float du = ui.Narrow ? 76 : 96;
        using (ui.Row(Space.S, Align.End).Key("orbs"))
        {
            Orb(ref ui, 0, "life", _vitals.Life, _vitals.LifeMax, DescentPaints.LifeOrb, du, "#Descent_Hud_Life");
            Orb(ref ui, 1, "mana", _vitals.Mana, _vitals.ManaMax, DescentPaints.ManaOrb, du, "#Descent_Hud_Mana");
        }
    }

    private static void Orb(ref UiFrame ui, int slot, string key, int cur, int max, UiPaint body, float du, string labelToken)
    {
        bool low = max > 0 && cur * 4 <= max;                       // under 25 %: the ring reads danger
        float target = max > 0 ? System.Math.Clamp(cur / (float)max, 0f, 1f) : 0f;
        float fill = ui.Animate(key, target, Motion.Standard);

        using (ui.Column(Space.XS, Align.Centre).Key(slot))
        {
            var orb = ui.Box().Size(Size.Du(du), Size.Du(du)).Round(Radius.L)
                        .Fill(Tone.SurfaceRaised).Border(low ? Tone.Danger : Tone.Line, low ? 2 : 1);
            using (orb)
            {
                // Keys are numeric and orb-scoped: the two orbs share this panel's scope,
                // and two keyed elements with one id would share state (lint `ids`).
                // The liquid is the disc's background layer: the number and caption
                // sit over it on purpose (.Layered(), lint `overlap`).
                using (ui.Box().Size(Size.Fill, Size.Fill).Clip().Round(Radius.L).Layered().Key(20 + slot * 4))
                    ui.Rect(body, Size.Fill, Size.Percent(100 * fill)).At(UiAnchor.Bottom).Layered().Key(21 + slot * 4);
                // The value stacked over the disc: Text on the coloured disc. The
                // max caption sits end-aligned under the number, so their left edges
                // never "nearly align" (lint `alignment`), and the stack is a
                // full-size layer over the liquid (.Layered(), lint `overlap`).
                // The low ring already marks danger; the number stays Text so it
                // keeps its contrast on the coloured liquid.
                using (ui.Column(Space.None, Align.End).Size(Size.Du(du), Size.Du(du)).Align(Align.Centre, Align.End).Layered().Key(22 + slot * 4))
                {
                    ui.Number(cur, Text.Number, Tone.Text);
                    ui.Text($"/ {max}", Text.Caption, Tone.Text);
                }
            }
            // The label is a chip on the opaque window tone: the gallery previews
            // an orb pair over the world itself, where a translucent surface or
            // muted Text would have nothing solid behind it (lint `contrast`).
            using (ui.Box().Fill(Tone.Window).Round(Radius.S).Pad(Space.XS, Space.None))
                ui.Label(labelToken, Tone.TextMuted);
        }
    }
}

/// <summary>
/// The potion belt: four keycaps with counts, and the shared-cooldown wipe
/// (D16). The keycap sits in a fixed chip at the slot's top-left so the
/// number row aligns across slots; the name takes the rest of the top row.
/// Narrow, the belt joins the HUD's fixed-width corner column, so it wraps to
/// two columns there (2 x 112 + 8 du): one row of four hung 224 du past the panel.
/// </summary>
public sealed class BeltStrip : UiPanel
{
    public const float SlotDu = 112;

    private readonly IDescentBelt _belt;
    private readonly UiSize[] _pair = [Size.Du(SlotDu), Size.Du(SlotDu)];

    public BeltStrip(IDescentBelt belt) => _belt = belt;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Narrow ? ui.Grid(_pair, Space.S, Space.S).Key("belt") : ui.Row(Space.S, Align.Centre).Key("belt"))
        {
            for (int i = 0; i < _belt.SlotCount; i++)
            {
                using (ui.Box().Size(Size.Du(SlotDu), Size.Du(48)).Fill(Tone.SurfaceRaised).Round(Radius.M)
                         .Border(_belt.SlotReady ? Tone.Accent : Tone.Line, 1).Key(i))
                {
                    if (!_belt.SlotReady)
                        ui.Rect(Tone.Scrim, Size.Fill, Size.Fill).Layered();   // the shared-cooldown wipe
                    string name = _belt.SlotName(i);
                    using (ui.Column(Space.None, Align.Start).Size(Size.Fill, Size.Fill).Pad(Space.XS, Space.XS))
                    {
                        using (ui.Row(Space.XS, Align.Centre))
                        {
                            using (ui.Box().Size(Size.Du(16), Size.Du(16)).Fill(Tone.Surface).Round(Radius.S))
                                ui.Text($"{i + 1}", Text.Caption, Tone.TextMuted).Size(Size.Fill, Size.Fill).TextAlign(Align.Centre);
                            if (name.Length > 0)
                                ui.Data(name, Text.Caption, Tone.Text).Elide().Width(Size.Fill);
                        }
                        if (name.Length > 0)
                            using (ui.Row(Space.None, Align.End))
                                ui.Number(_belt.SlotCountOf(i), Text.Label);
                    }
                }
            }
        }
    }
}

/// <summary>What one <see cref="PartyPill"/> shows.</summary>
public readonly record struct PartyPillProps(DescentPartyMember Member, bool Focus, bool Reviving);

/// <summary>
/// A party frame (R26): person glyph, name, life meter; a downed member turns
/// to the danger tone with the dead mark and a distance caption; the member
/// doing the reviving answers with an accent check. The HUD's left column
/// (compact) and the party window (Focus) share it.
/// </summary>
public sealed class PartyPill : UiPanel<PartyPillProps>
{
    public override void Describe(ref UiFrame ui, in PartyPillProps p)
    {
        var m = p.Member;
        var tone = m.Downed ? Tone.Danger : Tone.TeamA;
        bool compact = ui.Narrow && !p.Focus;

        using (ui.Row(Space.S, Align.Centre))
        {
            ui.Icon(m.Downed ? UiShape.Cross45 : UiShape.Person, tone, compact ? 12 : 16);
            ui.Data(m.Name, compact ? Text.Caption : Text.Body).Elide().Width(Size.Du(compact ? 96 : 160));
            if (m.Hardcore)
                // Warning, not Danger: Danger measures 4.4:1 on an opaque
                // SurfaceRaised (the downed lane's measurement), and HC is a
                // mode flag, not an alert — Danger stays with the downed icon.
                ui.Text("#Descent_Hardcore_Chip", Text.Caption, Tone.Warning);
            if (m.Downed)
            {
                ui.Text($"{m.DistanceUnits}u", Text.Caption, Tone.Warning);
            }
            else
            {
                float f = m.LifeMax > 0 ? System.Math.Clamp(m.Life / (float)m.LifeMax, 0f, 1f) : 1f;
                ui.Meter(ui.Animate("m", f, Motion.Standard), f <= 0.25f ? Tone.Danger : tone, Size.Du(compact ? 48 : 96), 4);
            }
            if (p.Reviving)
                ui.Shape(UiShape.Check, Tone.Accent, 16);
        }
    }
}

/// <summary>Rank dots beside a skill or spell name: filled, hollow, and item-bonus in warning.</summary>
public readonly record struct RankPipsProps(int Rank, int MaxRank, int BonusRanks);

public sealed class RankPips : UiPanel<RankPipsProps>
{
    public override void Describe(ref UiFrame ui, in RankPipsProps p)
    {
        using (ui.Row(Space.S, Align.Centre).Key("pips"))
        {
            for (int i = 0; i < p.MaxRank; i++)
            {
                bool bonus = i >= p.Rank && i < p.Rank + p.BonusRanks;
                var t = i < p.Rank ? Tone.Accent : bonus ? Tone.Warning : Tone.Line;
                ui.Shape(UiShape.Dot, t, 8);
            }
            ui.Text($"{p.Rank + p.BonusRanks} / {p.MaxRank}", Text.Caption, Tone.TextMuted);
        }
    }
}

/// <summary>The XP bar with its level beside it and the unspent-points chip.</summary>
public sealed class XpBar : UiPanel
{
    private readonly IDescentXp _xp;

    public XpBar(IDescentXp xp) => _xp = xp;

    public override void Describe(ref UiFrame ui)
    {
        float f = _xp.XpToNext > 0 ? System.Math.Clamp(_xp.Xp / (float)_xp.XpToNext, 0f, 1f) : 1f;
        using (ui.Row(Space.S, Align.Centre).Key("xp"))
        {
            ui.Meter(ui.Animate("xp", f, Motion.Standard), Tone.Accent, Size.Du(ui.Narrow ? 160 : 208), 4);
            // The level reads as a chip: over the world (a gallery, a bare HUD
            // corner) muted Text would have nothing behind it.
            using (ui.Box().Fill(Tone.SurfaceRaised).Round(Radius.S).Pad(Space.XS, Space.None))
                ui.Text($"Lv {_xp.Level}", Text.Caption, Tone.TextMuted);
            int points = _xp.AttributePoints + _xp.SkillPoints;
            if (points > 0)
            {
                using (ui.Box().Fill(Tone.SurfaceRaised).Border(Tone.Accent, 1).Round(Radius.S).Pad(Space.S, Space.XS))
                    ui.Text($"{points}+", Text.Caption, Tone.Accent);
            }
        }
    }
}

/// <summary>What an <see cref="ItemGrid"/> shows: whose cells, how many columns, and the owner's callbacks.</summary>
public readonly record struct ItemGridProps(
    IDescentItemGridSource? Grid,                 // the backpack or the stash, by shape
    int Columns,
    Action<int>? OnHover,                         // cell index, when the pointer lands on one
    Action<int>? OnUse,                           // cell index, on click
    IDescentInspectActions? InspectActions = null);  // the slot's button opens the modal

/// <summary>
/// The one-cell-item grid (D4): a grid of <see cref="ItemSlot"/> cells,
/// empty wells to the rolled capacity, and the withdraw-only tail (2d) past
/// it. One panel instance serves both grids of the stash screen: the two
/// <c>ui.Sub</c> keys keep their hover ids apart.
/// </summary>
public sealed class ItemGrid : UiPanel<ItemGridProps>
{
    private readonly ItemSlot _slot = new();
    private UiSize[] _cols = [];
    private int _colsFor = -1;

    public override void Describe(ref UiFrame ui, in ItemGridProps p)
    {
        if (p.Grid is not { } grid)
            return;
        int cells = Math.Max(grid.CellCount, grid.Capacity);
        if (_colsFor != p.Columns)
            SetColumns(p.Columns);
        using (ui.Grid(_cols, Space.XS, Space.XS).Key("grid"))
        {
            for (int i = 0; i < cells; i++)
            {
                bool past = i >= grid.Capacity;                    // the 2d overflow tail
                var item = grid.Cell(i);
                var cell = ui.Box().Size(Size.Du(ItemSlot.SizeDu), Size.Du(ItemSlot.SizeDu)).Key(i);
                var hit = p.OnUse is null && p.OnHover is null ? default : ui.Interact(cell);
                if (p.OnHover is { } hv && hit.Hovered && item is not null) hv(i);
                using (cell)
                    ui.Sub(-1 - i, _slot,
                           new ItemSlotProps(item, false, past, false, hit.Hovered,
                                             item is not null && p.InspectActions is not null, i,
                                             p.InspectActions));
                if (p.OnUse is { } use && hit.Clicked && item is not null) use(i);
            }
        }
    }
    /// <summary>Outside a Describe method: the analyzer's rule is per-frame allocation,
    /// and the column array is built once when the column count changes, not per frame.</summary>
    private void SetColumns(int columns)
    {
        _cols = new UiSize[columns];
        for (int c = 0; c < columns; c++) _cols[c] = Size.Du(ItemSlot.SizeDu);
        _colsFor = columns;
    }
}

/// <summary>
/// The item tooltip (R17/D22): the name in its rarity colour, ilvl and family,
/// the affix rows with right-aligned numbers (a curse reads red, R28), the
/// durability line, the requirement, the broken/inactive banners, and the
/// compare block against the equipped item — deltas positive/red. The parent
/// places it in a <c>ui.Overlay</c> anchored like a dropdown.
/// </summary>
public sealed class ItemTooltip : UiPanel
{
    private readonly IDescentItemHover _hover;
    private readonly RarityChip _chip = new();

    public ItemTooltip(IDescentItemHover hover) => _hover = hover;

    public override void Describe(ref UiFrame ui)
    {
        if (!_hover.Hovering)
            return;
        var item = _hover.Item;
        // The HintTip's treatment: an Accent edge, since the tooltip also
        // floats over the world backdrop at portrait scales. Its content
        // stays a left-aligned table — affix rows are scanned, not read as
        // one block.
        using (ui.Box().Fill(Tone.Window).Border(Tone.Accent, 1).Round(Radius.M)
                 .Pad(Space.M, Space.L).Size(Size.Du(300), Size.Auto).Clip())
        {
            using (ui.Column(Space.XS, Align.Start))
            {
                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Sub("chip", _chip, item.Rarity);
                    ui.Data(item.BaseName, Text.Heading).Color(DescentPaints.Of(item.Rarity)).Elide().Width(Size.Fill);
                }
                if (item.HasUnusual && item.Identified && _hover.UnusualEffect.Length > 0)
                    ui.Data(_hover.UnusualEffect, Text.Caption).Color(DescentPaints.Unusual).Elide();
                ui.Text($"ilvl {_hover.Item.ItemLevel} {_hover.Family}", Text.Caption, Tone.TextMuted).Elide();
                if (!item.Identified)
                {
                    // D22: the view carries no affixes; "?" and the caption are all it may say.
                    ui.Text("#Descent_Tip_Unidentified", Text.Caption, Tone.Warning).Wrap(268);
                }
                else
                {
                    for (int i = 0; i < _hover.AffixLineCount; i++)
                    {
                        var a = _hover.AffixLine(i);
                        using (ui.Row(Space.S, Align.Centre))
                        {
                            ui.Data(a.Name, Text.Body).Elide().Width(Size.Fill);
                            ui.Number(a.Value, Text.Number).Color(a.Value < 0 ? DescentPaints.Lowered : (UiPaint)Tone.Text);
                        }
                    }
                }
                if (item.Broken)
                    ui.Text("#Descent_Tip_Broken", Text.Caption, Tone.Danger);
                else if (item.Inactive)
                    ui.Text("#Descent_Tip_Inactive", Text.Caption, Tone.Warning);
                if (item.MaxDurability > 0)
                {
                    float f = Math.Clamp(item.Durability / (float)item.MaxDurability, 0f, 1f);
                    using (ui.Row(Space.S, Align.Centre))
                    {
                        ui.Text("#Descent_Tip_Durability", Text.Caption, Tone.TextMuted);
                        ui.Text($"{item.Durability} / {item.MaxDurability}", Text.Caption, Tone.TextMuted);
                        ui.Meter(f, f <= 0.25f ? Tone.Danger : Tone.Line, Size.Du(96), 4);
                    }
                }
                if (_hover.RequirementToken.Length > 0)
                    ui.Text(_hover.RequirementToken, Text.Caption,
                        _hover.RequirementMet ? Tone.TextMuted : Tone.Danger);
                if (_hover.CompareHas)
                {
                    ui.Divider();
                    // Name the compare target: the delta rows below read as a
                    // diff only once you see what they are a diff against (R17).
                    // Token and name are separate elements — one interpolated
                    // string would ask the localiser for a key that never exists.
                    using (ui.Row(Space.XS, Align.Centre))
                    {
                        ui.Text("#Descent_Tip_Compare", Text.Caption, Tone.TextMuted);
                        ui.Data(_hover.CompareItem.BaseName, Text.Caption, Tone.Text).Elide();
                    }
                    for (int i = 0; i < _hover.CompareAffixCount; i++)
                    {
                        var mine = _hover.AffixLine(i);
                        var theirs = _hover.CompareAffix(i);
                        int delta = mine.Value - theirs.Value;
                        if (delta == 0) continue;
                        using (ui.Row(Space.S, Align.Centre))
                        {
                            ui.Data(mine.Name, Text.Body).Elide().Width(Size.Fill);
                            ui.Text($"{(delta > 0 ? "+" : "")}{delta}", Text.Number)
                              .Color(delta > 0 ? (UiPaint)Tone.Positive : DescentPaints.Lowered);
                        }
                    }
                }
            }
        }
    }
}

/// <summary>What an <see cref="AttributeRow"/> shows, plus the sheet's staged-plus callback.</summary>
public readonly record struct AttributeRowProps(
    int Index, DescentAttribute Attr, bool ShowPlus, Action<int, int>? OnPlus);

/// <summary>
/// One attribute row of the sheet (C.1): label, the <c>[base][now]</c> pair as
/// two number plates — Now blue above base, red below, Base gold at cap —
/// and the <c>+</c> button only while points remain (the parent decides
/// <c>ShowPlus</c>; shift-click is the parent's ×5 rule).
/// </summary>
public sealed class AttributeRow : UiPanel<AttributeRowProps>
{
    public override void Describe(ref UiFrame ui, in AttributeRowProps p)
    {
        var a = p.Attr;
        var nowPaint = a.Now > a.Base ? DescentPaints.Raised
                     : a.Now < a.Base ? DescentPaints.Lowered : (UiPaint)Tone.Text;
        var basePaint = a.Base >= a.Cap ? DescentPaints.Capped : (UiPaint)Tone.TextMuted;
        using (ui.Row(Space.S, Align.Centre))
        {
            ui.Text(a.NameToken, Text.Body).Width(Size.Du(120));
            // The plates are drawn, not subbed: a plate's value is a pre-built
            // string, and these values are live numbers (ui.Number formats without one).
            Plate(ref ui, 0, a.Base, basePaint);
            Plate(ref ui, 1, a.Now, nowPaint);
            if (p.ShowPlus)
            {
                var plus = ui.Box().Key(p.Index).Size(Size.Du(40), Size.Du(40))
                               .Fill(Tone.SurfaceRaised).Border(Tone.Line, 1).Round(Radius.M);
                var hit = ui.Interact(plus);
                using (plus)
                    ui.Shape(UiShape.Cross, Tone.Accent, 16).At(UiAnchor.Centre);
                if (hit.Clicked) p.OnPlus?.Invoke(p.Index, 1);
            }
        }
    }
    /// <summary>A [base][now] plate: the same recessed field as <see cref="NumberPlate"/>,
    /// holding a live number instead of a view-model string.</summary>
    private static void Plate(ref UiFrame ui, int key, int value, UiPaint ink)
    {
        using (ui.Box().Key(key).Size(Size.Du(72), Size.Du(32)).Fill(Tone.Surface).Border(Tone.Line, 1)
                 .Round(Radius.S).Pad(Space.S, Space.None))
            ui.Number(value, Text.Number).Color(ink).At(UiAnchor.Right).Size(Size.Fill, Size.Fill).TextAlign(Align.End);
    }
}

/// <summary>What a <see cref="ResistRow"/> shows.</summary>
public readonly record struct ResistRowProps(string LabelToken, int Percent, int Cap);

/// <summary>A resistance row (C.1): label, meter, percent — gold at the cap.</summary>
public sealed class ResistRow : UiPanel<ResistRowProps>
{
    public override void Describe(ref UiFrame ui, in ResistRowProps p)
    {
        bool capped = p.Percent >= p.Cap;
        using (ui.Row(Space.S, Align.Centre))
        {
            ui.Text(p.LabelToken, Text.Body).Width(Size.Du(120));
            ui.Meter(Math.Clamp(p.Percent / 100f, 0f, 1f), capped ? DescentPaints.Capped : Tone.Accent, Size.Du(88), 4);
            ui.Text($"{p.Percent} %", Text.Number).Color(capped ? DescentPaints.Capped : (UiPaint)Tone.Text).TextAlign(Align.End);
        }
    }
}

/// <summary>What a <see cref="NumberField"/> shows.</summary>
public readonly record struct NumberFieldProps(string LabelToken, string Value, UiPaint Paint);

/// <summary>The Diablo plate row: a caption at the left, the value in a recessed plate at the right (C.1, the vendor's fee).</summary>
public sealed class NumberField : UiPanel<NumberFieldProps>
{
    private readonly NumberPlate _plate = new();

    public override void Describe(ref UiFrame ui, in NumberFieldProps p)
    {
        using (ui.Row(Space.S, Align.Centre))
        {
            ui.Text(p.LabelToken, Text.Body, Tone.TextMuted).Width(Size.Du(160));
            ui.Sub(p.LabelToken, _plate, new NumberPlateProps(p.Value, p.Paint, Text.Number, 96));
        }
    }
}

/// <summary>What a <see cref="SkillCard"/> shows and what its owner answers with.</summary>
public readonly record struct SkillCardProps(
    DescentSkill Skill, Action<int>? OnBuy, Action<int>? OnHover);

/// <summary>
/// One skill on the tier ladder (Appendix A): the name in Heading, the rank as
/// pips and <c>n / max</c>, the kind caption, the mana/cooldown copy; the
/// family tag (an accent dot, then the family) for family upgrades. A buyable card wears an accent frame and
/// a <c>+</c>; a card locked by prerequisite dims with a <see cref="UiShape.Lock"/>,
/// a card locked by tier says the level it wants.
/// </summary>
public sealed class SkillCard : UiPanel<SkillCardProps>
{
    private readonly RankPips _pips = new();

    private const float CardDu = 200;

    /// <summary>The buy badge: the inspect chip's size, inset like it.</summary>
    private const float BadgeDu = 32;

    /// <summary>
    /// How wide a copy row may run while the badge shows: from the left padding
    /// to a gap before the badge. The badge is an intended layer (the lint does
    /// not read it), and the family row ran under it, cutting "Bullet Family"
    /// to "Bullet Famil" (the restarted review).
    /// </summary>
    private const float BadgeClearDu = CardDu - (float)Space.S - (float)Space.XS - BadgeDu - (float)Space.S;

    public override void Describe(ref UiFrame ui, in SkillCardProps p)
    {
        var s = p.Skill;
        bool buyable = s.Buyable;
        bool badge = buyable && s.Unlocked && s.PrereqMet;
        var rowW = badge ? Size.Du(BadgeClearDu) : Size.Auto;
        var card = ui.Box().Key(s.Id).Size(Size.Du(CardDu), Size.Du(ui.Narrow ? 116 : 124))
                     .Fill(Tone.Surface).Round(Radius.M)
                     .Border(buyable ? Tone.Accent : Tone.Line, buyable ? 2 : 1)
                     .Pad(Space.S, Space.S);
        var hit = ui.Interact(card);
        if (hit.Hovered) p.OnHover?.Invoke(s.Id);
        using (card)
        {
            using (ui.Column(Space.S, Align.Start))
            {
                ui.Text(s.NameToken, Text.Heading, s.Unlocked ? Tone.Text : Tone.TextMuted).Elide();
                using (ui.Row(Space.S, Align.Centre))
                    ui.Sub("pips", _pips, new RankPipsProps(s.Rank, s.MaxRank, s.BonusRanks));
                using (ui.Row(Space.XS, Align.Centre).Width(rowW))   // XS: a separator dot sits tight
                {
                    ui.Text(KindToken(s.Kind), Text.Caption, Tone.TextMuted);
                    if (s.FamilyTag.Length > 0)
                    {
                        // A drawn dot: the plan's U+25C6 is in no shipped face and
                        // drew as the missing-glyph box (UiGlyphCoverageTests).
                        ui.Shape(UiShape.Dot, Tone.Accent, 8);
                        ui.Text(s.FamilyTag, Text.Caption, Tone.Accent).Elide().Width(badge ? Size.Fill : Size.Auto);
                    }
                }
                if (s.ManaCost.Length > 0 || s.Cooldown.Length > 0)
                    ui.Text($"{s.ManaCost}{(s.ManaCost.Length > 0 && s.Cooldown.Length > 0 ? " · " : "")}{s.Cooldown}",
                        Text.Caption, Tone.TextMuted).Elide().Width(rowW);
                if (!s.Unlocked)
                    ui.Text($"Lv {s.TierUnlockLevel}", Text.Caption, Tone.Text).Elide();   // locked by tier
                else if (!s.PrereqMet)
                    ui.Shape(UiShape.Lock, Tone.TextMuted, 16);                           // locked by prereq
            }
            if (badge)
            {
                // A + badge in the card's corner (drawn, not ui.Button: the key
                // would need a per-skill string, and the Cross icon needs no token).
                var plus = ui.Box().Key(-1 - s.Id).Size(Size.Du(BadgeDu), Size.Du(BadgeDu))
                               .Fill(Tone.Accent).Round(Radius.M)
                               .At(UiAnchor.BottomRight).Inset(Space.XS, Space.XS).Layered();
                var hitPlus = ui.Interact(plus);
                using (plus)
                    ui.Shape(UiShape.Cross, Tone.Text, 16).At(UiAnchor.Centre);
                if (hitPlus.Clicked) p.OnBuy?.Invoke(s.Id);
            }
        }
    }

    private static string KindToken(DescentSkillKind kind) => kind switch
    {
        DescentSkillKind.Action => "#Descent_Kind_Action",
        DescentSkillKind.Upgrade => "#Descent_Kind_Upgrade",
        DescentSkillKind.UpgradeFamily => "#Descent_Kind_Family",
        DescentSkillKind.UpgradeTriggered => "#Descent_Kind_Triggered",
        DescentSkillKind.Toggle => "#Descent_Kind_Toggle",
        DescentSkillKind.Capstone => "#Descent_Kind_Capstone",
        _ => "#Descent_Kind_Utility",
    };
}

/// <summary>What <see cref="MetalPockets"/> shows: the Engineer's three stacks (D24).</summary>
public readonly record struct MetalPocketsProps(int Scrap, int Reclaimed, int Refined);

/// <summary>The crafting metal pockets: three stacks of metal, each a number over its label.</summary>
public sealed class MetalPockets : UiPanel<MetalPocketsProps>
{
    public override void Describe(ref UiFrame ui, in MetalPocketsProps p)
    {
        using (ui.Row(Space.M, Align.Centre))
        {
            Stack(ref ui, 0, "#Descent_Craft_Scrap", p.Scrap, Tone.TextMuted);
            Stack(ref ui, 1, "#Descent_Craft_Reclaimed", p.Reclaimed, Tone.Warning);
            Stack(ref ui, 2, "#Descent_Craft_Refined", p.Refined, DescentPaints.Capped);
        }
    }

    private static void Stack(ref UiFrame ui, int key, string labelToken, int count, UiPaint ink)
    {
        using (ui.Box().Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.S).Key(key))
        {
            using (ui.Column(Space.XS, Align.Centre))
            {
                using (ui.Row(Space.XS, Align.Centre))
                {
                    ui.Shape(UiShape.Dot, ink, 12);
                    ui.Number(count, Text.Number);
                }
                ui.Text(labelToken, Text.Caption, Tone.TextMuted);
            }
        }
    }
}

/// <summary>What a <see cref="CoinLabel"/> shows.</summary>
public readonly record struct CoinLabelProps(long Amount, bool Short);

/// <summary>The Australium amount (D12): a gold coin dot, the number, red when the player is short.</summary>
public sealed class CoinLabel : UiPanel<CoinLabelProps>
{
    public override void Describe(ref UiFrame ui, in CoinLabelProps p)
    {
        using (ui.Row(Space.XS, Align.Centre).Key("coin"))
        {
            ui.Shape(UiShape.Dot, DescentPaints.Australium, 12);
            ui.Number((int)p.Amount, Text.Number).Color(p.Short ? DescentPaints.Lowered : (UiPaint)Tone.Text);
        }
    }
}

/// <summary>
/// The small hover popup (docs/plans/descent_ui.md §3): the raised panel a
/// stat, skill or spell name wears when the pointer rests on it. Narrower
/// than <see cref="ItemTooltip"/>, one row taller than wide: a title with a
/// muted subtitle beside it, then copy lines with values pinned right, then
/// the emphasised value line. Content is the host's view-model through
/// <see cref="IDescentHintTip"/> — the part never decides what a row means,
/// and draws nothing while the tip is inactive. Screens open it in an
/// overlay anchored to the hovered row (the Inventory's tooltip pattern).
/// </summary>
public sealed class HintTip : UiPanel
{
    private readonly IDescentHintTip _tip;

    public HintTip(IDescentHintTip tip) => _tip = tip;

    public override void Describe(ref UiFrame ui)
    {
        if (!_tip.Active)
            return;
        // 300, the ItemTooltip's width: the same kind of popup, and the
        // value row (Text.Number) needs the panel to fit its largest
        // figure — "90 Australium" elides at 264. Copy wraps to the inner
        // width below.
        // Tone.Accent, not the Tone.Line hairline: the popup floats over the
        // world backdrop as well as the window, and one amber hairline is
        // what separates its dark panel from a bright wall (the toolkit's
        // own dialogs edge with the house colour).
        using (ui.Box().Fill(Tone.Window).Border(Tone.Accent, 1).Round(Radius.M)
                 .Pad(Space.S, Space.M).Size(Size.Du(300), Size.Auto).Clip())
        {
            // Width(Fill) clamps the column to the panel: an Auto column
            // shrinks to its widest child's natural width, the Fill texts
            // then measure at that natural width, and long copy overflows
            // the clip (and the window, onto the backdrop, at portrait
            // scales) instead of eliding. The Inventory's tooltip rows rely
            // on the same clamp. Centre: a popup is read as one block, not
            // scanned as a table, like Diablo's hint boxes.
            using (ui.Column(Space.XS, Align.Centre).Width(Size.Fill))
            {
                using (ui.Row(Space.S, Align.Centre))
                {
                    // Wrap, not Fill: a Fill title would eat the row and
                    // pin the subtitle to the right edge, splitting the
                    // "Fireball  rank 2 / 3" pair. Capped at 240 the title
                    // wraps (a long skill name) and the pair centres together.
                    if (_tip.TitleToken.Length > 0)
                        ui.Text(_tip.TitleToken, Text.Heading).Wrap(240).Elide().TextAlign(Align.Centre);
                    else
                        ui.Data(_tip.TitleData, Text.Heading).Wrap(240).Elide().TextAlign(Align.Centre);
                    if (_tip.SubtitleData is { Length: > 0 } sub)
                        ui.Data(sub, Text.Caption, Tone.TextMuted);
                }
                // Copy wraps, like the ItemTooltip's unidentified caption:
                // the hint exists to explain, so eliding its line would
                // lose the message. 284 = the 300 panel minus the Space.S
                // side padding. Width(Fill) is still required: a natural-
                // width copy plus a pinned value measures wider than the
                // panel and the clip cuts the value (the lint's clipped
                // rule). Fill + Wrap centres each run within that width.
                // Elide stays as the guard for a word longer than the wrap
                // width. Titles and values are short: they elide.
                for (int i = 0; i < _tip.LineCount; i++)
                {
                    using (ui.Box().Width(Size.Fill))
                    {
                        if (_tip.LineToken(i) is { Length: > 0 } lt)
                            ui.Text(lt, Text.Body).Wrap(284).Elide().Width(Size.Fill).TextAlign(Align.Centre);
                        else
                            ui.Data(_tip.LineData(i), Text.Body).Wrap(284).Elide().Width(Size.Fill).TextAlign(Align.Centre);
                    }
                    // The line's pinned value is its own centred row, not a
                    // sibling in the copy's row: wrapped copy at full
                    // measure plus a natural-width value measured wider
                    // than the panel and the clip cut the value (the lint's
                    // clipped rule), and the figure reads better big and
                    // centred under its sentence than pinned at the edge.
                    if (_tip.LineGood(i) is { Length: > 0 } v)
                        ui.Data(v, Text.Number).Width(Size.Fill).TextAlign(Align.Centre)
                          .Color(_tip.LineGoodNegative(i) ? DescentPaints.Lowered : DescentPaints.Raised);
                }
                bool valueLine = _tip.ValueToken(0) is { Length: > 0 } || _tip.ValueData(0) is { Length: > 0 };
                if (valueLine)
                {
                    if (_tip.LineCount > 0)
                        ui.Divider();
                    if (_tip.ValueToken(0) is { Length: > 0 } vt)
                        ui.Text(vt, Text.Number).Color(_tip.ValueGood(0) ? Tone.Positive : (UiPaint)Tone.Text)
                          .Width(Size.Fill).TextAlign(Align.Centre).Elide();
                    else
                        ui.Data(_tip.ValueData(0), Text.Number).Color(_tip.ValueGood(0) ? Tone.Positive : (UiPaint)Tone.Text)
                          .Width(Size.Fill).TextAlign(Align.Centre).Elide();
                }
            }
        }
    }
}
