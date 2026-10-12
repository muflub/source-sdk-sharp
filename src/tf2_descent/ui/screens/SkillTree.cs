using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>Which group heading a Mercenary card sits under; the tab answers per group index.
/// The shared <see cref="IDescentSkillTab"/> has no group titles, so the screen carries this
/// narrow extension itself (flagged for the shared interface).</summary>
public interface IDescentSkillGroups
{
    string GroupTitleToken(int group);
}

/// <summary>Which tab the player was last on (host state in the game, sample-fixed in previews).
/// Not on <see cref="IDescentSkills"/>, so the screen carries this narrow extension itself.</summary>
public interface IDescentSkillTabSelection
{
    int SelectedTab { get; }
}

/// <summary>
/// The skill window (docs/plans/descent_ui.md §2.5, Appendix A): <c>ui.Tabs</c>
/// across the top — the class's three trees and the Mercenary tab, all from
/// <see cref="IDescentSkills"/>. The body is the tier ladder: each tier a band
/// with its skills as 200-du <see cref="SkillCard"/> slots, prerequisite links
/// as hairline three-segment <see cref="UiFrame.Rect"/> connectors between the
/// cards (each drawn in its own zero-size wrapper, the AutomapCanvas trick, so
/// the lint reads them as layers and not stray siblings), and the unspent-points
/// chip beside the tab row. The Mercenary tab is the same cards without
/// connectors, grouped under their heading with the rank's level floor beside it.
/// Narrow: the ladder narrows to one card per row, the tier gutter becomes a
/// caption above each band and the connectors go — they only mean something
/// with the ladder's width.
/// </summary>
public sealed class SkillTree : UiScreen, IUiWindowed
{
    private const int MaxTier = 16;
    private const int MaxPrereq = 4;

    // The ladder's grid, in du: cards 200 wide with a 24-du requirement row
    // under the 124-du card; the tier chip column is 84 wide so its right edge
    // meets the first card's left edge exactly.
    private const float CardW = 200, CardH = 124, ReqH = 24, SlotH = CardH + ReqH;
    private const float ChipW = 84, ColGap = 12, RowGap = 12, BandPad = 12, BandGap = 12, RingPad = 24;

    private readonly UiWindows _windows;
    private readonly IDescentSkills _skills;
    private readonly IDescentSkillHover? _hover;
    private readonly HintTip? _hint;
    private readonly SkillTreeBody _body;

    // Tab titles, rebuilt only when the tab count changes (describe allocates
    // nothing): ui.Tabs takes a span, and a string[] is a span.
    private string[] _tabTitles = [];

    public SkillTree(UiWindows windows, IDescentSkills skills,
                     IDescentSkillActions? actions = null,
                     IDescentSkillHover? hover = null, HintTip? hint = null)
    {
        _windows = windows;
        _skills = skills;
        _hover = hover;
        _hint = hint;
        _body = new SkillTreeBody(actions, hover);
    }

    public override string Name => "skilltree";

    public string WindowId => "skilltree";

    /// <summary>
    /// 1040 x 680 du by default — the tier bands of 148-du slots plus the tab
    /// row; at least 720 x 520, which still shows three bands and scrolls the
    /// rest. Narrow below 860 du: the two-column wall (2 x 200-du cards plus the
    /// 84-du tier chip) stops fitting.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(1040, MathF.Round(screenW * 0.92f)), MathF.Min(680, MathF.Round(screenH * 0.92f)),
               720, 520, screenW, screenH, 860);

    public override void Describe(ref UiFrame ui)
    {
        int tabCount = _skills.TabCount;
        if (tabCount <= 0)
            return;
        RefreshTitles(tabCount);

        // The tab row is display-only (the toolkit's input half arrives with U3,
        // as in ServerBrowser); which tab is open is host state, sample-fixed here.
        int tab = _skills is IDescentSkillTabSelection sel
            ? Math.Clamp(sel.SelectedTab, 0, tabCount - 1)
            : 0;

        using (ui.Window(_windows, WindowId, "#Descent_Skill_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            using (ui.Row(Space.L, Align.End).Width(Size.Fill))
            {
                DrawTabs(ref ui, _tabTitles, tab, ui.DesignWidth);
                ui.Flex();
                using (ui.Row(Space.XS, Align.Centre))
                {
                    ui.Text(_skills.ClassToken, Text.Caption, Tone.Text);
                    ui.Text("#Descent_Skill_Level", Text.Caption, Tone.TextMuted);
                    ui.Number(_skills.HeroLevel, Text.Number);
                }
                if (_skills.SkillPoints > 0)
                    using (ui.Row(Space.XS, Align.Centre).Fill(((UiPaint)Tone.Accent).WithOpacity(0.16f)).Round(Radius.M).Pad(Space.S, Space.XS))
                    {
                        ui.Shape(UiShape.Dot, Tone.Accent, 12);
                        ui.Text("#Descent_Skill_Points", Text.Caption, Tone.Text);
                        ui.Number(_skills.SkillPoints, Text.Number);
                    }
            }
            _body.ResetAnchor();
            var bodyBox = ui.Sub("body", _body, new SkillTreeBodyProps(_skills.Tab(tab), tab == tabCount - 1))
                            .Size(Size.Fill, Size.Fill);

            // The card hint last: the overlay draws above the ladder, anchored
            // to the hovered card the body captured during its draw (the
            // Inventory's tooltip pattern). The content is the host's
            // view-model through IDescentSkillHover.
            if (_hover is { Active: true } && _hint is not null)
            {
                var (at, self) = _body.HasAnchor
                    ? (UiAnchor.BottomRight, UiAnchor.TopLeft)
                    : (UiAnchor.BottomRight, UiAnchor.TopRight);
                using (ui.Overlay("hint").AnchorTo(_body.HasAnchor ? _body.Anchor : bodyBox, at, self)
                           .Inset(Space.None, Space.XS))
                    ui.Sub("hint", _hint);
            }
        }
    }

    /// <summary>
    /// The tab row, like <c>ui.Tabs</c> (heading text, accent underline on the
    /// selected one) but each label clamped to a share of the window's width
    /// and elided, so an unlocalised (uppercase-key) or long title shrinks
    /// instead of overflowing the row on a small screen.
    /// </summary>
    private static void DrawTabs(ref UiFrame ui, scoped ReadOnlySpan<string> titles, int selected, float designW)
    {
        int tabCount = titles.Length;
        float cap = MathF.Max(64f, (designW * 0.92f - (tabCount - 1) * (int)Space.XL) / tabCount);
        using (ui.Row(Space.XL, Align.End))
        {
            for (int i = 0; i < tabCount; i++)
            {
                bool on = i == selected;
                using (ui.Column(Space.XS, Align.Stretch).Key(i).MaxWidth(cap))
                {
                    ui.Text(titles[i], Text.Heading, on ? Tone.Text : Tone.TextMuted).Elide();
                    ui.Rect(on ? Tone.Accent : Tone.None, Size.Fill, Size.Du(4));
                }
            }
        }
    }

    /// <summary>Not a describe method (no <c>ref UiFrame</c>): the only place the title buffer grows.</summary>
    private void RefreshTitles(int tabCount)
    {
        if (_tabTitles.Length != tabCount)
            _tabTitles = new string[tabCount];
        for (int t = 0; t < tabCount; t++)
            _tabTitles[t] = _skills.Tab(t).TitleToken;
    }

    /// <summary>
    /// One tab's body: the tier ladder, or the Mercenary heading groups. The
    /// ladder is a computed grid — every card's (band, row, column) is known
    /// before it is drawn — so the prerequisite connectors can be placed from
    /// the same arithmetic, and the scroll's content size is deterministic.
    /// </summary>
    private sealed class SkillTreeBody(IDescentSkillActions? actions, IDescentSkillHover? hover) : UiPanel<SkillTreeBodyProps>
    {
        private readonly SkillCard _card = new();
        private readonly Action<int>? _buy = actions is null ? null : new Action<int>(actions.Buy);
        private readonly Action<int>? _hoverCard = actions is null ? null : new Action<int>(actions.Hover);

        // The hovered card's slot box, captured during the draw for the
        // screen's overlay: the card node lives inside the ladder's scroll,
        // so only the body knows where its card ended up.
        private UiBox _anchor;
        private bool _hasAnchor;

        public bool HasAnchor => _hasAnchor;
        public UiBox Anchor => _anchor;

        /// <summary>Not a describe method (no <c>ref UiFrame</c>): the screen
        /// clears the capture before each body draw.</summary>
        public void ResetAnchor() => _hasAnchor = false;

        // The grid scratch: sized once by EnsureBuffers, read (never grown) by Describe.
        private int[] _bandOf = [];
        private int[] _slotOf = [];
        private float[] _bandTop = [];
        private readonly int[] _bandCount = new int[MaxTier + 1];

        /// <summary>Not a describe method (no <c>ref UiFrame</c>): the only place the buffers grow.</summary>
        private void EnsureBuffers(int n)
        {
            if (_bandOf.Length < n) _bandOf = new int[n];
            if (_slotOf.Length < n) _slotOf = new int[n];
            if (_bandTop.Length < n + 1) _bandTop = new float[n + 1];
        }

        public override void Describe(ref UiFrame ui, in SkillTreeBodyProps p)
        {
            var t = p.Tab;
            int n = t.SkillCount;
            if (n == 0)
            {
                ui.EmptyState(UiShape.Star, "#Descent_Skill_Empty_Title", "#Descent_Skill_Empty_Body");
                return;
            }

            bool narrow = ui.Narrow;
            int usable = (int)Math.Max(240f, ui.DesignWidth - 2 * RingPad);
            int cols = narrow ? 1
                 : Math.Clamp((usable - (int)(ChipW + ColGap) + (int)CardW) / (int)(CardW + ColGap), 1, 4);
            float cardX0 = narrow ? 0 : ChipW;

            // Bucket the tab's skills by tier (clamped): band order = tier order,
            // slot order = draw order inside the band. Field buffers: they grow
            // once in EnsureBuffers (not a describe method), never per frame.
            EnsureBuffers(n);
            Span<int> bandOf = _bandOf;
            Span<int> slotOf = _slotOf;
            Span<int> bandCount = _bandCount;
            bandCount.Clear();
            int topBand = 0;
            for (int i = 0; i < n; i++)
            {
                int band = Math.Clamp(t.Skill(i).Tier, 1, MaxTier);
                bandOf[i] = band;
                if (band > topBand) topBand = band;
            }
            for (int band = 1; band <= topBand; band++)
            {
                for (int i = 0; i < n; i++)
                    if (bandOf[i] == band)
                        slotOf[i] = bandCount[band]++;
            }

            // Band height: rows of slots, padded; Mercenary bands wear their
            // heading as a 28-du row above the first slot row.
            Span<float> bandTop = _bandTop;
            float y = 0;
            for (int band = 1; band <= topBand; band++)
            {
                int rows = (bandCount[band] + cols - 1) / cols;
                int headingH = (p.Mercenary || narrow) && bandCount[band] > 0 ? 28 : 0;
                bandTop[band] = y;
                y += BandPad * 2 + headingH + rows * SlotH + (rows - 1) * RowGap;
                if (band < topBand) y += BandGap;
            }

            using (ui.Scroll("ladder", Space.None).Size(Size.Fill, Size.Fill))
            using (ui.Box().Size(Size.Du(usable), Size.Du(y)))
            {
                // Connectors first (under the cards); they are zero-size wrappers
                // so their hairlines are layers, never lint-visible siblings.
                if (!narrow && !p.Mercenary)
                    DrawConnectors(ref ui, t, cols, cardX0, bandTop, bandOf, slotOf, bandCount, topBand);

                for (int band = 1; band <= topBand; band++)
                {
                    if (bandCount[band] == 0)
                        continue;
                    int rows = (bandCount[band] + cols - 1) / cols;
                    int headingH = (p.Mercenary || narrow) && bandCount[band] > 0 ? 28 : 0;
                    float h = BandPad * 2 + headingH + rows * SlotH + (rows - 1) * RowGap;
                    using (ui.Box().Size(Size.Du(usable), Size.Du(h)).At(UiAnchor.TopLeft)
                             .Inset(new UiLength(0, false), new UiLength(bandTop[band], false))
                             .Fill(Tone.Surface).Round(Radius.M).Border(Tone.Line, 1))
                    {
                        int firstOfBand = -1;
                        for (int i = 0; i < n; i++)
                        {
                            if (bandOf[i] != band)
                                continue;
                            int slot = slotOf[i];
                            float x = cardX0 + (slot % cols) * (CardW + ColGap);
                            float cy = BandPad + headingH + (slot / cols) * (SlotH + RowGap);
                            firstOfBand = firstOfBand < 0 ? i : firstOfBand;
                            DrawSlot(ref ui, t, i, x, cy);
                        }
                        if (p.Mercenary)
                        {
                            // The heading row: the group's token and the level its
                            // rank asks for (the lowest TierUnlockLevel in it).
                            DrawHeading(ref ui, t, band, firstOfBand, h, headingH, narrow ? 0 : BandPad);
                        }
                        else if (!narrow)
                        {
                            // The tier chip column, its right edge meeting the
                            // first card's left edge.
                            DrawTierChip(ref ui, band, rows);
                        }
                        else
                        {
                            DrawTierCaption(ref ui, band, headingH);
                        }
                    }
                }
            }
        }

        /// <summary>One card: the shared <see cref="SkillCard"/> at the top of a 200 x 148 slot,
        /// the requirement row Positive when met (or the first synergy hint) under it.</summary>
        private void DrawSlot(ref UiFrame ui, IDescentSkillTab t, int i, float x, float y)
        {
            var s = t.Skill(i);
            using (var slot = ui.Box().Size(Size.Du(CardW), Size.Du(SlotH)).At(UiAnchor.TopLeft)
                     .Inset(new UiLength(x, false), new UiLength(y, false)))
            {
                // The card reports its own pointer (SkillCard interacts with
                // the card box); the view-model answers on the next frame,
                // and the card it names owns the popup's anchor.
                ui.Sub(s.Id, _card, new SkillCardProps(s, _buy, _hoverCard));
                if (hover is not null && hover.HoveredSkill == s.Id)
                {
                    _anchor = slot;
                    _hasAnchor = true;
                }
                int prereqs = t.PrereqCount(i);
                bool synergy = t.SynergyCount(i) > 0;
                if (prereqs > 0 || synergy)
                {
                    using (ui.Row(Space.XS, Align.Centre).Size(Size.Du(CardW), Size.Du(ReqH))
                             .At(UiAnchor.TopLeft).Inset(new UiLength(0, false), new UiLength(CardH, false)))
                    {
                        if (prereqs > 0)
                        {
                            bool met = s.PrereqMet;
                            ui.Text(met ? "#Descent_Skill_Req_Met" : "#Descent_Skill_Req_Missing",
                                    Text.Caption, s.Unlocked && met ? Tone.Positive : Tone.TextMuted);
                        }
                        if (synergy)
                            ui.Data(t.SynergyText(i, 0), Text.Caption, Tone.TextMuted).Elide();
                    }
                }
            }
        }

        /// <summary>The 84-du tier chip beside the first row: the tier word and number, centred.</summary>
        private static void DrawTierChip(ref UiFrame ui, int band, int rows)
        {
            float chipH = rows == 1 ? SlotH : SlotH + RowGap;
            using (ui.Box().Size(Size.Du(ChipW), Size.Du(chipH)).At(UiAnchor.TopLeft)
                     .Inset(new UiLength(0, false), new UiLength(BandPad, false))
                     .Fill(Tone.SurfaceRaised).Round(Radius.M))
            {
                using (ui.Column(Space.XS, Align.Centre).Size(Size.Fill, Size.Fill).At(UiAnchor.TopLeft))
                {
                    ui.Text("#Descent_Skill_Tier", Text.Caption, Tone.TextMuted).Width(Size.Fill).Elide();
                    ui.Number(band, Text.Number);
                }
            }
        }

        /// <summary>Narrow: the tier as a caption row above the stacked cards.</summary>
        private static void DrawTierCaption(ref UiFrame ui, int band, int headingH)
        {
            using (ui.Row(Space.XS, Align.Centre).Size(Size.Du(300), Size.Du(28)).At(UiAnchor.TopLeft)
                     .Inset(new UiLength(0, false), new UiLength(BandPad, false)))
            {
                ui.Text("#Descent_Skill_Tier", Text.Caption, Tone.TextMuted).Elide();
                ui.Number(band, Text.Number);
            }
        }

        /// <summary>The Mercenary heading: the group's token and its level floor, above the cards.</summary>
        private void DrawHeading(ref UiFrame ui, IDescentSkillTab t, int band, int firstOfBand, float bandH, int headingH, float headingX)
        {
            // The level floor: the lowest TierUnlockLevel the group's cards ask for.
            int floor = int.MaxValue;
            for (int i = 0; i < t.SkillCount; i++)
                if (t.Skill(i).Tier == band && t.Skill(i).TierUnlockLevel < floor)
                    floor = t.Skill(i).TierUnlockLevel;
            if (floor == int.MaxValue)
                floor = 0;
            using (ui.Row(Space.S, Align.Centre).Size(Size.Du(420), Size.Du(headingH)).At(UiAnchor.TopLeft)
                     .Inset(new UiLength(headingX, false), new UiLength(BandPad, false)))
            {
                ui.Text(t is IDescentSkillGroups g ? g.GroupTitleToken(band) : "#Descent_Skill_Group_Generic",
                        Text.Heading, Tone.Text).Elide();
                if (floor > 0)
                {
                    ui.Text("#Descent_Skill_Floor", Text.Caption, Tone.TextMuted);
                    ui.Number(floor, Text.Caption, Tone.TextMuted);
                }
            }
        }

        /// <summary>Prerequisite links: a hairline vertical from the prerequisite's bottom,
        /// a hairline horizontal across the middle, a hairline vertical into the dependent
        /// card's top — each in its own zero-size wrapper (the AutomapCanvas trick).</summary>
        private static void DrawConnectors(ref UiFrame ui, IDescentSkillTab t, int cols, float cardX0,
                                           ReadOnlySpan<float> bandTop, ReadOnlySpan<int> bandOf,
                                           ReadOnlySpan<int> slotOf, ReadOnlySpan<int> bandCount, int topBand)
        {
            int n = bandOf.Length;
            for (int i = 0; i < n; i++)
            {
                int prereqs = t.PrereqCount(i);
                if (prereqs <= 0)
                    continue;
                // Dependent card's geometry.
                int depBand = bandOf[i];
                if (depBand < 2)
                    continue;   // tier 1 has nothing above it
                float depCx = cardX0 + (slotOf[i] % cols) * (CardW + ColGap) + CardW / 2;
                float depTop = bandTop[depBand] + BandPad + (slotOf[i] / cols) * (SlotH + RowGap);
                for (int k = 0; k < prereqs && k < MaxPrereq; k++)
                {
                    int pid = t.PrereqId(i, k);
                    int j = FindById(t, pid, n);
                    if (j < 0 || bandOf[j] >= depBand)
                        continue;   // the link only means something pointing up
                    float prCx = cardX0 + (slotOf[j] % cols) * (CardW + ColGap) + CardW / 2;
                    float prBottom = bandTop[bandOf[j]] + BandPad + (slotOf[j] / cols) * (SlotH + RowGap) + CardH;
                    float midY = (prBottom + depTop) / 2;
                    var ink = t.Skill(i).Buyable ? Tone.Accent : Tone.Line;

                    // Prerequisite side: centre the wrapper on the segment's centre.
                    if (midY - prBottom > 1f)
                        Hairline(ref ui, prCx, (prBottom + midY) / 2, 2, midY - prBottom, ink);
                    // The crossing run.
                    if (MathF.Abs(depCx - prCx) > 1f)
                        Hairline(ref ui, (prCx + depCx) / 2, midY, MathF.Abs(depCx - prCx), 2, ink);
                    // Dependent side.
                    if (depTop - midY > 1f)
                        Hairline(ref ui, depCx, (midY + depTop) / 2, 2, depTop - midY, ink);
                }
            }
            _ = topBand;
        }

        /// <summary>One hairline: a zero-size wrapper at the segment's centre with the rect filling it.</summary>
        private static void Hairline(ref UiFrame ui, float cx, float cy, float w, float h, UiPaint paint)
        {
            using (ui.Box().Size(Size.Du(0), Size.Du(0)).At(UiAnchor.TopLeft)
                     .Inset(new UiLength(cx, false), new UiLength(cy, false)))
                ui.Rect(paint, Size.Du(w), Size.Du(h)).At(UiAnchor.Centre);
        }

        /// <summary>The skill with this id, or -1: tab counts are small, a scan is cheaper than a map.</summary>
        private static int FindById(IDescentSkillTab t, int id, int n)
        {
            for (int i = 0; i < n; i++)
                if (t.Skill(i).Id == id)
                    return i;
            return -1;
        }
    }

    /// <summary>What <see cref="SkillTreeBody"/> shows: the tab's data, and whether it is the Mercenary one.</summary>
    public readonly record struct SkillTreeBodyProps(IDescentSkillTab Tab, bool Mercenary);
}
