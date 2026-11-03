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
/// The control point icons as stock's <c>CControlPointIconsPanel</c> draws
/// them (hud_controlpointicons.cpp) with the map's obj_icons art: each icon
/// the owner's material with the <c>_locked</c> variant while no team may
/// cap it (<c>UpdateImage</c>, :294-307 — stock appends the suffix to the
/// map's own name; ours asks by owner, the derivation stock itself uses for
/// the swipe, <c>GetGameSpecificCPCappingSwipe</c>,
/// c_tf_objective_resource.cpp:91-100), a cap in progress drawing the
/// capping team's swipe over the icon (<c>UpdateCapImage</c>, :344-408, the
/// <c>_up</c> one when the layout has several rows,
/// <c>SetSwipeUp(iTotalLines &gt; 1)</c>, :1155) with the running man and
/// the capper count on it (CapPlayerImage and CapNumPlayers of
/// controlpointicon.res; the count from more than one up, :403-406), and a
/// cap with time left and nobody pushing it marked blocked
/// (<c>CapIsBlocked</c>). Each point's timer and unlock wait count down at
/// the icon — stock's CCPTimerLogic digits and the last-five countdown
/// (hud_controlpointicons.cpp:579-641, :1843-1876) — read off the objective
/// table's own per-point times against now, where stock derived them from
/// client-local events over the same networked fields
/// (c_team_objectiveresource.cpp:219-239). The icons sit 33 apart on the res's separators
/// (hudlayout.res: separator_width 9, separator_height 7, height_offset 0
/// of HudControlPointIcons :838-850; the row hangs separator_height 7
/// units above the screen's bottom, hud_controlpointicons.cpp:1114-1155).
/// The map's <c>CapLayoutInHUD</c> names the rows — a comma starts a line, a space
/// separates the points on one line; an empty layout puts every point on
/// one line (the parse stock runs over the same string). Points off the
/// round draw nothing (<c>IsInMiniRound</c> and <c>IsCPVisible</c>). The
/// swipe runs across the icon as the cap fills — the CapArrow's UV window
/// over the icon's quad (:1672-1766). The touched point's icon carries the
/// cap teardrop (stock's <c>CControlPointProgressBar</c>,
/// hud_controlpointicons.cpp:1499-1646, controlpointprogressbar.res at
/// x2.25): the sweep of the cap progress in the capping team's tone over a
/// dim track while it is the local team's cap running
/// (<c>SetupForPoint</c>'s bar arm, :1519), else the blocked mark and the
/// ladder's <c>#Team_Capture_*</c> text — the <see cref="ITFCapProgress"/>
/// service answers both (its doc comment holds the round-word and stun-arm
/// narrowings). The teardrop's own art misses the vpk's icon tables, so
/// the themed plate stands in for it (the same fallback rule the other art
/// layers keep).
/// </summary>
public sealed class ControlPointIconsPanel(ITFCapturePoints points, ITFCapProgress progress,
                                           IUiTextureLibrary textures) : UiPanel
{
    // controlpointicon.res's 33x33 icon and hudlayout.res's separators at
    // the scoreboard's x2.25 (the 640-space x 2.25, StockRes.Scale).
    public const float IconDu = 74.25f;   // 33 x 2.25 (controlpointicon.res:10-12)
    public const float GapXDu = 20.25f;   // separator_width 9 (hudlayout.res:847)
    public const float GapYDu = 15.75f;   // separator_height 7 (hudlayout.res:848)

    /// <summary>The icons' bottom inset: separator_height 7 units above the
    /// screen's bottom (height_offset 0, hudlayout.res:849) — TFHud hangs
    /// the row there.</summary>
    internal static readonly float BottomGapDu = StockRes.Scale * 7f;

    // The res's own image names (materials/sprites/obj_icons, plus the
    // CapPlayerImage's scheme image).
    private const string Capper = "vgui/capture_icon";

    // The teardrop (controlpointprogressbar.res at x2.25): the 100x65 panel
    // centred on the 33-unit icon, fully above it; the bar's 45x45 at
    // (28,5); the art's 54x65 at (24,0); the text's 75x40 at (14,8) and the
    // blocked icon's 50x50 at (26,3).
    internal const float TeardropWideDu = 225f;      // 100 x 2.25
    internal const float TeardropTallDu = 146.25f;   // 65 x 2.25
    internal const float ArtWideDu = 121.5f;         // 54 x 2.25
    internal const float ArtTallDu = 146.25f;        // 65 x 2.25
    internal const float ArtXDu = 54f;               // 24 x 2.25
    internal const float BarDu = 101.25f;            // 45 x 2.25
    internal const float BarXDu = 63f;               // 28 x 2.25
    internal const float BarYDu = 11.25f;            // 5 x 2.25
    internal const float TextWideDu = 168.75f;       // 75 x 2.25
    internal const float TextTallDu = 90f;           // 40 x 2.25
    internal const float TextXDu = 31.5f;            // 14 x 2.25
    internal const float TextYDu = 18f;              // 8 x 2.25
    internal const float BlockedDu = 112.5f;         // 50 x 2.25
    internal const float BlockedXDu = 58.5f;         // 26 x 2.25
    internal const float BlockedYDu = 6.75f;         // 3 x 2.25

    /// <summary>The teardrop icon (controlpointprogressbar.res's
    /// <c>cappoint_progressbar_teardrop</c>): the blocked mark's shape
    /// beside it, the ladder's text over the plate when the art misses.</summary>
    private const string TeardropIcon = "cappoint_progressbar_teardrop";

    /// <summary>The base image's name: the stock maps'
    /// icon_obj_{neutral,red,blu} (tf/team_control_point defaults), the
    /// <c>_locked</c> variant while no team may cap it.</summary>
    private static string BaseName(int owner, bool locked) => (owner, locked) switch
    {
        (Red, false) => "sprites/obj_icons/icon_obj_red",
        (Red, true) => "sprites/obj_icons/icon_obj_red_locked",
        (Blue, false) => "sprites/obj_icons/icon_obj_blu",
        (Blue, true) => "sprites/obj_icons/icon_obj_blu_locked",
        (_, false) => "sprites/obj_icons/icon_obj_neutral",
        _ => "sprites/obj_icons/icon_obj_neutral_locked",
    };

    /// <summary>The swipe's name (GetGameSpecificCPCappingSwipe's red/blu
    /// pair, c_tf_objective_resource.cpp:96-99), the <c>_up</c> one when
    /// the layout has several rows (hud_controlpointicons.cpp:1155).</summary>
    private static string SwipeName(int team, bool up) => (team == Red, up) switch
    {
        (true, false) => "sprites/obj_icons/icon_obj_cap_red",
        (true, true) => "sprites/obj_icons/icon_obj_cap_red_up",
        (false, false) => "sprites/obj_icons/icon_obj_cap_blu",
        _ => "sprites/obj_icons/icon_obj_cap_blu_up",
    };

    /// <summary>CapPlayerImage's 10x20 at the icon's left edge
    /// (controlpointicon.res:33-50), and the count's x15/y4
    /// (CapNumPlayers, :52-75), at x2.25.</summary>
    internal const float ManWideDu = 22.5f;   // 10 x 2.25
    internal const float ManTallDu = 45f;     // 20 x 2.25
    internal const float CountXDu = 33.75f;   // 15 x 2.25
    internal const float CountYDu = 9f;       // 4 x 2.25

    // CPTimerBG's icon_obj_timer art: 66x33 at the icon's own pos, its right
    // half sticking out under the digits (controlpointicon.res:117-132,
    // SetTimerTime hud_controlpointicons.cpp:628-632); CPTimerLabel's 9x20
    // at the icon's right edge, half a height down (:636).
    private const string TimerArt = "sprites/obj_icons/icon_obj_timer";
    internal const float TimerArtWideDu = 148.5f;   // 66 x 2.25
    internal const float TimerWideDu = 20.25f;      // 9 x 2.25
    internal const float TimerTallDu = 45f;         // 20 x 2.25
    internal const float TimerXDu = 72f;            // 33 - 1 x 2.25
    internal const float TimerYDu = 14.625f;        // (33 - 20) / 2 x 2.25
    internal const float CountdownDu = 67.5f;       // CapCountdownLabel's 30 x 2.25

    // The countdown digits' inks: the regular TanLight of stock's
    // m_cRegularColor (hud_controlpointicons.cpp:154) and the per-icon
    // CapCountdownLabel's fgcolor (controlpointcountdown.res:21). The
    // flash is stock's m_cHighlightColor RedSolid (:155) read as the
    // theme's Danger — RedSolid over the dark plate is under the 4.5:1
    // bar (over stock's own tan art it is 4.3:1), so the flash rides the
    // token that passes.
    private static readonly UiPaint TimerInk = UiPaint.FromGame(new UiColor(235, 226, 202, 255));

    public bool Present => points.NumPoints > 0;

    // The layout parse is cached on the string's identity (SSUI001): the
    // map's layout arrives once and never changes after. Null until the
    // first parse — an interned "" must not read as already parsed.
    private string? _layout;
    private readonly List<List<int>> _rows = [];
    private readonly List<int> _line = [];

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        Parse();
        bool swipeUp = _rows.Count > 1;

        // The icons on one line sit separator_width apart (hudlayout.res's
        // 9) and each line carries a trailing separator — the code's line
        // width counts n+1 gaps, so the icons sit half a gap left of the
        // centre (hud_controlpointicons.cpp:1114-1152); the lines are
        // separator_height (7) apart. The Space scale's steps miss those,
        // so the gaps are explicit spacers.
        using (ui.Column(Space.None, Align.Centre).Key("cp points"))
        {
            for (int row = 0; row < _rows.Count; row++)
            {
                if (row > 0)
                    using (ui.Box().Size(Size.Du(0), Size.Du(GapYDu)))
                    {
                    }
                List<int> line = _rows[row];
                // No key on the row: a numeric one would land on an icon's
                // key (one scope per panel) and share its id.
                using (ui.Row(Space.None, Align.Centre))
                {
                    foreach (int point in line)
                    {
                        Icon(ref ui, point, swipeUp);
                        using (ui.Box().Size(Size.Du(GapXDu), Size.Du(0)))
                        {
                        }
                    }
                }
            }
        }
    }

    /// <summary>One icon: the owner's art (the locked variant while no team
    /// may cap it), the capping team's swipe over it while a cap runs, the
    /// running man and the capper count on it, the blocked mark. A 0
    /// handle draws the plain panel so the state still reads.</summary>
    private void Icon(ref UiFrame ui, int point, bool swipeUp)
    {
        if (point >= points.NumPoints || !points.Visible(point) || !points.InMiniRound(point))
            return;

        int owner = points.OwningTeam(point);
        int cappingTeam = points.CappingTeam(point);
        bool capping = cappingTeam != Unassigned && cappingTeam != owner;
        bool blocked = capping && points.Blocked(point);
        bool locked = points.Locked(point);

        using (ui.Box().Size(Size.Du(IconDu), Size.Du(IconDu)).Key(point)
                       .Name(blocked ? "cp blocked" : "cp"))
        {
            int art = textures.Find(BaseName(owner, locked));
            if (art != 0)
                ui.Texture(art, Size.Fill, Size.Fill).Name("cp art");
            else
                using (ui.Box().Size(Size.Fill, Size.Fill)
                               .Fill(Tone.SurfaceRaised).Border(blocked ? Tone.Danger : Tone.Line, 2)
                               .Name("cp fallback"))
                {
                    if (locked)
                        ui.Shape(UiShape.Lock, Tone.Text, ManTallDu / 2).At(UiAnchor.Centre).Key("locked");
                }

            if (capping)
            {
                int swipe = textures.Find(SwipeName(cappingTeam, swipeUp));
                if (swipe != 0)
                    ui.Texture(swipe, Size.Fill, Size.Fill,
                               SwipeWindow(points.CapProgress(point), cappingTeam, swipeUp))
                      .Layered().Name("cap swipe");

                // CapPlayerImage: the running man on the point's left edge.
                int man = textures.Find(Capper);
                if (man != 0)
                    ui.Texture(man, Size.Du(ManWideDu), Size.Du(ManTallDu)).Layered().Key("cap man");

                // The cappers on the point, from more than one up (:403-406),
                // at the res's x15/y4 (CapNumPlayers, textAlignment west):
                // a box whose left/top edge lands there (its own size is the
                // complement, so the fraction of the leftover is 1).
                int cappers = points.Cappers(point, cappingTeam);
                if (cappers > 1)
                    using (ui.Box().Size(Size.Du(IconDu - CountXDu), Size.Du(IconDu - CountYDu))
                                   .PlaceFraction(1f, 1f).Layered().Key("cappers"))
                        ui.Number(cappers, Text.Body, Tone.Text).HudSize();
            }

            Timer(ref ui, point);
            Unlock(ref ui, point);
            Teardrop(ref ui, point);
        }
    }

    /// <summary>The cap teardrop over the touched point's icon
    /// (<c>UpdateProgressBarFor(m_iCurrentCP)</c>, hud_controlpointicons.cpp:874-891
    /// and <c>SetupForPoint</c> :1499-1541): the sweep of the cap in the
    /// capping team's tone over a dim track while it is the local team's
    /// cap running, else the blocked mark and the ladder's text. Stock's
    /// win-state gate folds into the ladder's #Team_Capture_NotNow arm.</summary>
    private void Teardrop(ref UiFrame ui, int point)
    {
        if (progress.Touched != point)
            return;

        using (ui.Box().Size(Size.Du(TeardropWideDu), Size.Du(TeardropTallDu))
                       .AtRes(UiAnchor.TopLeft, -(TeardropWideDu - IconDu) / 2, -TeardropTallDu)
                       .Layered().Key("cap progress"))
        {
            // The teardrop art first (zpos 21); the bar and text sit over it
            // (zpos 23, the res's own order).
            if (textures.Icon(TeardropIcon) is { } art)
                ui.Texture(art.Handle, Size.Du(ArtWideDu), Size.Du(ArtTallDu), art.Uv)
                  .AtRes(UiAnchor.TopLeft, ArtXDu, 0f).Name("cap teardrop");
            else
                ui.Rect(Tone.Surface, Size.Du(ArtWideDu), Size.Du(ArtTallDu))
                  .AtRes(UiAnchor.TopLeft, ArtXDu, 0f).Round(Radius.L).Name("cap teardrop plate");

            if (progress.ShowsBar(point))
            {
                // The track first, the fill over it (stock's
                // CircularProgressBar has a bg and an fg image).
                ui.Shape(UiShape.Sweep, Tone.Line, BarDu, 1f)
                  .AtRes(UiAnchor.TopLeft, BarXDu, BarYDu).Layered().Name("cap bar track");
                ui.Shape(UiShape.Sweep, TFHudTeams.ToneOf(points.CappingTeam(point)), BarDu, progress.Progress(point))
                  .AtRes(UiAnchor.TopLeft, BarXDu, BarYDu).Layered().Name("cap bar");
            }
            else
            {
                ui.Shape(UiShape.Cross45, Tone.Danger, BlockedDu)
                  .AtRes(UiAnchor.TopLeft, BlockedXDu, BlockedYDu).Layered().Name("cap blocked");
                ui.Text(progress.Text(point), Text.Caption, Tone.Text).TextSize(TFHudType.ProgressText)
                  .Width(Size.Du(TextWideDu)).Height(Size.Du(TextTallDu)).TextAlign(Align.Centre)
                  .AtRes(UiAnchor.TopLeft, TextXDu, TextYDu).Layered().Name("cap text");
            }
        }
    }

    /// <summary>The point's timer countdown (CControlPointIcon::SetTimerTime
    /// and OnTick, hud_controlpointicons.cpp:579-641): icon_obj_timer behind
    /// the icon's own art (its zpos -1, at the icon's pos) and the whole
    /// seconds left just right of the icon, centred in its 9x20, flashing
    /// TanLight/RedSolid with ten or fewer left — stock flips the label's
    /// colour on each 100 ms tick (:593-604), so the parity of the tenth
    /// now. Nothing draws with no time on the point.</summary>
    private void Timer(ref UiFrame ui, int point)
    {
        float left = points.TimerSeconds(point);
        if (left <= 0f)
            return;                                   // no time named on the point

        int art = textures.Find(TimerArt);
        if (art != 0)
            ui.Texture(art, Size.Du(TimerArtWideDu), Size.Du(IconDu))
              .At(UiAnchor.TopLeft).Layered().Name("cp timer bg");
        else
            using (ui.Box().Size(Size.Du(TimerArtWideDu), Size.Du(IconDu))
                           .Fill(Tone.Surface).At(UiAnchor.TopLeft).Layered().Name("cp timer bg fallback"))
            {
            }

        int seconds = (int)MathF.Ceiling(left);
        var ink = seconds <= 10 && (int)(ui.Now * 10) % 2 == 1 ? (UiPaint)Tone.Danger : TimerInk;
        // Stock's ControlPointTimer face (10 tall, 22.5 du, TF2 Secondary —
        // clientscheme.res ControlPointTimer).
        ui.Number(seconds, Text.Caption, Tone.Text).TextSize(TFHudType.ControlPointTimer).Color(ink)
            .Width(Size.Du(TimerWideDu)).Height(Size.Du(TimerTallDu)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.TopLeft, TimerXDu, TimerYDu).Layered().Key("cp timer");
    }

    /// <summary>The per-icon unlock countdown (CControlPointCountdown::OnTick,
    /// hud_controlpointicons.cpp:1843-1876): the whole seconds left centred
    /// over the icon (CapCountdownLabel, 30x30 of the res's Countdown panel),
    /// visible only for the last five seconds of the wait — and the last-5
    /// announcer lines and the waiting-for-players gate live elsewhere (the
    /// round state is deferred with the clock's other arms).</summary>
    private void Unlock(ref UiFrame ui, int point)
    {
        float left = points.UnlockSeconds(point);
        if (left <= 0f || left > 5f)
            return;

        ui.Number((int)left, Text.Heading, Tone.Text).HudSize().Color(TimerInk)
            .Width(Size.Du(CountdownDu)).Height(Size.Du(CountdownDu)).TextAlign(Align.Centre)
            .At(UiAnchor.Centre).Layered().Key("cp unlock");
    }

    /// <summary>The swipe's UV window (CControlPointIconCapArrow::Paint,
    /// hud_controlpointicons.cpp:1672-1731): the arrow is a 15/69 head
    /// before a 54/69 body, and the window slides across the 33-unit icon
    /// with the cap percentage — left to right for the red team (its even
    /// parity), the other way for blue, and bottom-up over the rows'
    /// ShouldSwipeUp. A little indent keeps a cap's start and end off the
    /// icon's edges, so the window reaches past the art's own.</summary>
    private static UiRect SwipeWindow(float progress, int cappingTeam, bool up)
    {
        const float head = 15f / 69f;   // flArrowHeadPixelWidth 15 over flImageSize 69
        const float move = 48f / 69f;   // (flBoxSize 33 + head 15) over 69
        const float upIndent = 0.07f;
        if (up)
        {
            float ya = Map(progress, -move - upIndent, head - upIndent);
            float yb = Map(progress, upIndent, move - upIndent);
            return new UiRect(0f, ya, 1f, yb - ya);
        }

        const float indent = 0.1f;
        float start = 1f - indent, end = 1f + indent;
        bool leftToRight = cappingTeam % 2 == 0;
        float xa = leftToRight ? Map(progress, start + move, end - head) : Map(progress, start, 0f);
        float xb = leftToRight ? Map(progress, start, 0f) : Map(progress, start + move, end - head);
        return new UiRect(xa, 0f, xb - xa, 1f);

        static float Map(float progress, float at, float to) => at + progress * (to - at);
    }

    /// <summary>The map's layout into rows of point indices; an empty
    /// layout is every point on one line.</summary>
    private void Parse()
    {
        string layout = points.CapLayout;
        if (ReferenceEquals(layout, _layout))
            return;

        _layout = layout;
        _rows.Clear();
        _line.Clear();

        int value = 0;
        bool have = false;
        foreach (char c in layout)
        {
            if (c is >= '0' and <= '9')
            {
                value = value * 10 + (c - '0');
                have = true;
                continue;
            }

            if (have)
            {
                _line.Add(value);
                value = 0;
                have = false;
            }

            // A comma starts the next line; a space only ends the number
            // (the "3,1 2" pyramid: 3 on one line, 1 and 2 on the next).
            if (c == ',')
                NextRow();
        }

        if (have)
            _line.Add(value);
        NextRow();

        // No layout named: every point on one line, in index order.
        if (_rows.Count == 0)
        {
            for (int point = 0; point < points.NumPoints; point++)
                _line.Add(point);
            NextRow();
        }

        void NextRow()
        {
            if (_line.Count > 0)
            {
                _rows.Add([.. _line]);
                _line.Clear();
            }
        }
    }

    /// <summary>TEAM_UNASSIGNED: no team.</summary>
    private const int Unassigned = 0;

    /// <summary>TF_TEAM_RED.</summary>
    private const int Red = 2;

    /// <summary>TF_TEAM_BLUE.</summary>
    private const int Blue = 3;
}
