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
/// The win panel (stock's <c>CTFWinPanel</c>, tf_hud_winpanel.cpp) at stock's
/// own layout: hudlayout.res's WinPanel host at c-150 215 300x300 with
/// winpanel.res's children nested inside it through <see cref="StockRes"/> —
/// the two team-score cards on TeamScoresPanel at -41 13 480x60, shown only
/// on a complete round (stock's SetVisible(bRoundComplete), :676), the
/// team-tinted border at cs-0.5 70 (0.94 of the host wide, :209-215), the
/// banner at 15 72, the win reason at 15 89 and the details line at 15 101,
/// the shaded bar at 15 116 268x125 with the top-players heading, the
/// hairline and the three player rows (name 56, class 183, points 243, the
/// 14x14 portrait in the Steam-avatar slot — no avatars in this build, the
/// class image takes its place, as the freeze panel's does), and the
/// killstreak row under the second hairline.
///
/// <para>
/// Stock's drop shadow on the banner and its stopwatch arms (the res's
/// stopWatch labels) are not drawn: the shadow is the theme's own outline
/// rule here, and the server never sends a stopwatch. The scores the panel
/// shows are the source's — the previous ones until the two-second tick
/// (stock's OnThink m_flTimeUpdateTeamScore arm, :714-737).
/// </para>
/// </summary>
public sealed class TFWinPanel(ITFWinPanelSource panel, IUiTextureLibrary textures, ITFRenderGroups? groups = null) : UiScreen
{
    public override string Name => "tf2winpanel";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Up from the win event, down at the next round start
    /// (stock's show/hide listeners, tf_hud_winpanel.cpp:119-126), and
    /// stood down while the scoreboard locks the hud's global group
    /// (ITFRenderGroups, hud.cpp:288-303).</summary>
    public override bool Visible => panel.Showing && groups?.GlobalLocked != true;

    public override void Describe(ref UiFrame ui)
    {
        // The host rides at its .res rect: the "c-150" xpos centres the 300
        // host, ypos 215 its top — it hangs 35 units past the screen's
        // bottom, as stock's does (vgui clips what hangs out), so the drawn
        // box is the visible part. The children hang inside it at their own
        // .res coordinates. On a narrow or short canvas the whole panel
        // scales about the screen centre: its widest reach is the scores
        // panel's 289 units from centre and its full height is 515 (top 215
        // + tall 300), so a canvas that cannot hold either shrinks it — the
        // loadout screen's smaller du-per-unit arm, as stock's proportional
        // space does.
        var screen = StockRes.Screen(ui.Screen);
        float k = MathF.Min(1f, MathF.Min(screen.Wide * 0.5f / 289f, screen.Tall / 515f));
        var root = StockRes.ClippedToScreen(Stock.Root.Rect(screen));
        var at = Scaled(StockRes.Spot(root, screen), k);
        using (ui.Box().AtRes(UiAnchor.Top, at.CentreX, at.Top)
                       .Size(Size.Du(at.Wide), Size.Du(at.Tall)).Layered().Key("panel"))
        {
            Describe(ref ui, root, k);
        }
    }

    private void Describe(ref UiFrame ui, StockRect root, float k)
    {
        if (panel.RoundComplete)
        {
            var scores = Stock.Scores.Rect(root);
            var scoreAt = Scaled(StockRes.Spot(scores, root), k);
            using (ui.Box().AtRes(UiAnchor.BottomLeft, scoreAt.Left, scoreAt.Bottom)
                           .Size(Size.Du(scoreAt.Wide), Size.Du(scoreAt.Tall)).Layered().Key("team scores"))
            {
                Card(ref ui, scores, 3, k);
                Card(ref ui, scores, 2, k);
            }
        }

        // The border is stock's WinPanelBGBorder: the team's fat-line border
        // (plain TFFatLineBorder on a stalemate, :241) whose bg fill is the
        // dark translucent color_panel_red — the team tone under a scrim.
        var border = Scaled(StockRes.Spot(Stock.Border.Rect(root), root), k);
        Tone frame = panel.WinningTeam switch { 2 => Tone.TeamA, 3 => Tone.TeamB, _ => Tone.Surface };
        ui.Rect(frame, Size.Du(border.Wide), Size.Du(border.Tall))
            .AtRes(UiAnchor.Top, border.CentreX, border.Top).Layered().Key("border");
        ui.Rect(Tone.Scrim, Size.Du(border.Wide), Size.Du(border.Tall))
            .AtRes(UiAnchor.Top, border.CentreX, border.Top).Layered().Key("border shade");

        // Texts hang from their .res ypos (a vgui label flows from the top of
        // its box): TopLeft, not the box's bottom — the win reason's 30-tall
        // box would otherwise carry its line down onto the details'.
        var banner = Scaled(StockRes.Spot(Stock.Banner.Rect(root), root), k);
        ui.Data(panel.Banner, Text.Heading, Tone.Text)
            .Width(Size.Du(banner.Wide)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.TopLeft, banner.Left, banner.Top).Layered().Key("banner");

        var reason = Scaled(StockRes.Spot(Stock.WinReason.Rect(root), root), k);
        ui.Data(panel.WinReason, Text.Body, Tone.Text)
            .Width(Size.Du(reason.Wide)).TextAlign(Align.Centre).Wrap(reason.Wide)
            .AtRes(UiAnchor.TopLeft, reason.Left, reason.Top).Layered().Key("win reason");

        // The details line sits 12 units under the win reason — a body line
        // clears that only while the panel is near full size, so under a
        // heavy narrow-canvas scale it yields rather than print over it.
        var details = Scaled(StockRes.Spot(Stock.Details.Rect(root), root), k);
        if (12f * k * StockRes.Scale >= 22f)
            ui.Data(panel.Details, Text.Body, Tone.Text)
                .Width(Size.Du(details.Wide)).TextAlign(Align.Centre)
                .AtRes(UiAnchor.TopLeft, details.Left, details.Top).Layered().Key("details");

        var bar = Scaled(StockRes.Spot(Stock.ShadedBar.Rect(root), root), k);
        ui.Rect(Tone.Scrim, Size.Du(bar.Wide), Size.Du(bar.Tall))
            .AtRes(UiAnchor.BottomLeft, bar.Left, bar.Bottom).Layered().Key("shaded bar");

        var top = Scaled(StockRes.Spot(Stock.TopPlayers.Rect(root), root), k);
        ui.Data(panel.TopPlayersLabel, Text.Label, Tone.Text)
            .Width(Size.Du(top.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.TopLeft, top.Left, top.Top).Layered().Key("top players");

        var points = Scaled(StockRes.Spot(Stock.PointsThisRound.Rect(root), root), k);
        ui.Text("#Winpanel_PointsThisRound", Text.Label, Tone.TextMuted)
            .Width(Size.Du(points.Wide)).TextAlign(Align.End)
            .AtRes(UiAnchor.TopLeft, points.Left, points.Top).Layered().Key("points this round");

        for (int i = 0; i < 3; i++)
        {
            var row = panel.Player(i);
            if (row.Points > 0)
                PlayerRow(ref ui, root, row, i, k);
        }

        var streak = panel.KillStreak;
        if (streak.Points > 0)
        {
            var line2 = Scaled(StockRes.Spot(Stock.Line2.Rect(root), root), k);
            ui.Rect(Tone.Line, Size.Du(line2.Wide), Size.Du(line2.Tall))
                .AtRes(UiAnchor.BottomLeft, line2.Left, line2.Bottom).Layered().Key("hairline 2");

            var leader = Scaled(StockRes.Spot(Stock.StreakLeader.Rect(root), root), k);
            ui.Text("#Winpanel_KillStreakLeader", Text.Label, Tone.Text)
                .Width(Size.Du(leader.Wide)).TextAlign(Align.Start)
                .AtRes(UiAnchor.TopLeft, leader.Left, leader.Top).Layered().Key("streak leader");

            var count = Scaled(StockRes.Spot(Stock.StreakCount.Rect(root), root), k);
            ui.Text("#Winpanel_KillStreakMaxCount", Text.Label, Tone.TextMuted)
                .Width(Size.Du(count.Wide)).TextAlign(Align.End)
                .AtRes(UiAnchor.TopLeft, count.Left, count.Top).Layered().Key("streak count");

            PlayerRow(ref ui, root, streak, 3, k);
        }
        else
        {
            var line1 = Scaled(StockRes.Spot(Stock.Line1.Rect(root), root), k);
            ui.Rect(Tone.Line, Size.Du(line1.Wide), Size.Du(line1.Tall))
                .AtRes(UiAnchor.BottomLeft, line1.Left, line1.Bottom).Layered().Key("hairline");
        }
    }

    /// <summary>One card (winpanel.res's Blue/RedScoreBG): the plate, the
    /// team name and the score the tick answers — the previous ones until
    /// it lands, stock's m_flTimeUpdateTeamScore arm
    /// (tf_hud_winpanel.cpp:704-737). The name and score swap edges
    /// between the cards, stock's west/east alignments. The plate draws on
    /// Surface, not stock's TanDark override (winpanel.res's 117 107 94):
    /// that tan is under the contrast gate for its TanLight labels, and the
    /// cards float over the world with no dark fill behind them.</summary>
    private void Card(ref UiFrame ui, StockRect scores, int team, float k)
    {
        bool blue = team == 3;
        var plate = Scaled(StockRes.Spot(Stock.Box(blue ? Col.BlueBg : Col.RedBg, 0).Rect(scores), scores), k);
        ui.Rect(Tone.Surface, Size.Du(plate.Wide), Size.Du(plate.Tall))
            .AtRes(UiAnchor.BottomLeft, plate.Left, plate.Bottom).Layered().Key(blue ? "blue plate" : "red plate");

        var name = Scaled(StockRes.Spot(Stock.Box(blue ? Col.BlueName : Col.RedName, 0).Rect(scores), scores), k);
        ui.Text(blue ? "#TF_Name_Blue" : "#TF_Name_Red", Text.Heading, Tone.Text)
            .Width(Size.Du(name.Wide)).TextAlign(blue ? Align.Start : Align.End)
            .AtRes(UiAnchor.TopLeft, name.Left, name.Top).Layered().Key(blue ? "blue name" : "red name");

        var score = Scaled(StockRes.Spot(Stock.Box(blue ? Col.BlueScore : Col.RedScore, 0).Rect(scores), scores), k);

        // vgui centres a label's line in its box (its a_west default), and
        // stock's score box rides 15 units above its plate — so centre the
        // Display line (72 du) on the plate the eye reads, on its own
        // Surface chip: the line's size is fixed while the plate scales
        // with the panel, and under a heavy narrow-canvas scale the glyphs
        // would otherwise reach past the plate onto the world.
        float centre = plate.Top + plate.Tall / 2f;
        if (plate.Tall < 72f)
            ui.Rect(Tone.Surface, Size.Du(plate.Wide), Size.Du(72f))
                .AtRes(UiAnchor.Top, plate.CentreX, centre - 36f)
                .Layered().Key(blue ? "blue score chip" : "red score chip");

        ui.Number(blue ? panel.BlueScore : panel.RedScore, Text.Display, Tone.Text)
            .Width(Size.Du(score.Wide)).TextAlign(blue ? Align.End : Align.Start)
            .AtRes(UiAnchor.TopLeft, score.Left, centre - 36f)
            .Layered().Key(blue ? "blue score" : "red score");
    }

    /// <summary>One player row (stock's :491-585 and :587-658): the name and
    /// the points in TanLight cream, the class dull, and the portrait in the
    /// avatar slot. <paramref name="index"/> 3 is the killstreak row. The
    /// rows key apart by index (the ids rule) and name for the lint; long
    /// names and classes elide, as the kill feed's do.</summary>
    private void PlayerRow(ref UiFrame ui, StockRect root, TFWinPanelPlayerView row, int index, float k)
    {
        var name = Scaled(StockRes.Spot(Stock.Box(Col.Name, index).Rect(root), root), k);
        ui.Data(row.Name, Text.Body, Tone.Text)
            .Width(Size.Du(name.Wide)).TextAlign(Align.Start).Elide()
            .AtRes(UiAnchor.TopLeft, name.Left, name.Top).Layered().Key(index * 4).Name("row name");

        var className = Scaled(StockRes.Spot(Stock.Box(Col.Class, index).Rect(root), root), k);
        ui.Data(row.Class, Text.Body, Tone.TextMuted)
            .Width(Size.Du(className.Wide)).TextAlign(Align.Start).Elide()
            .AtRes(UiAnchor.TopLeft, className.Left, className.Top).Layered().Key(index * 4 + 1).Name("row class");

        var points = Scaled(StockRes.Spot(Stock.Box(Col.Score, index).Rect(root), root), k);
        ui.Number(row.Points, Text.Body, Tone.Text)
            .Width(Size.Du(points.Wide)).TextAlign(Align.End)
            .AtRes(UiAnchor.TopLeft, points.Left, points.Top).Layered().Key(index * 4 + 2).Name("row points");

        var avatar = Scaled(StockRes.Spot(Stock.Box(Col.Avatar, index).Rect(root), root), k);
        string image = HealthPanel.Portrait(row.Class, row.Team);
        if (image.Length > 0)
            StockArt.Draw(ref ui, textures, "row portrait", image,
                avatar.Left, avatar.Bottom, avatar.Wide, avatar.Tall, keyNumber: index * 4 + 3);
    }

    /// <summary>Every .res coordinate through the narrow-canvas scale, about
    /// the screen centre the host hangs on.</summary>
    private static StockSpot Scaled(StockSpot s, float k) =>
        new(s.Left * k, s.Right * k, s.Bottom * k, s.Top * k, s.CentreX * k, s.Wide * k, s.Tall * k);

    /// <summary>The .res boxes on the 300x300 host hudlayout.res places at
    /// c-150 215 (winpanel.res, its proportionaltoparent children): the
    /// scores panel's -41 xpos reaches left past the host so its two cards
    /// centre on it, and the border's "cs-0.5" is a centred box 0.94 of the
    /// host's width. The three player rows stand 22 units apart.</summary>
    internal static class Stock
    {
        public static readonly StockBox Root = new("c-150", "215", 300, 300);
        public static readonly StockBox Scores = new("-41", "13", 480, 60);
        public static readonly StockBox Border = new("c-141", "70", 282, 185);
        public static readonly StockBox Banner = new("15", "72", 268, 24);
        public static readonly StockBox WinReason = new("15", "89", 268, 30);
        public static readonly StockBox Details = new("15", "101", 268, 20);
        public static readonly StockBox ShadedBar = new("15", "116", 268, 125);
        public static readonly StockBox TopPlayers = new("18", "114", 200, 20);
        public static readonly StockBox PointsThisRound = new("139", "114", 140, 20);
        public static readonly StockBox Line1 = new("18", "129", 260, 1);
        public static readonly StockBox Line2 = new("18", "210", 260, 1);
        public static readonly StockBox StreakLeader = new("18", "195", 200, 20);
        public static readonly StockBox StreakCount = new("139", "195", 140, 20);

        /// <summary>The player rows, by index — 0..2 the top players, 3 the
        /// killstreak row; the avatars hang 3 units below their names.</summary>
        public static StockBox Box(Col column, int index) => column switch
        {
            Col.BlueBg => new("50", "15", 135, 40),
            Col.RedBg => new("197", "15", 135, 40),
            Col.BlueName => new("56", "25", 100, 20),
            Col.BlueScore => new("80", "0", 100, 50),
            Col.RedName => new("224", "25", 100, 20),
            Col.RedScore => new("202", "0", 100, 50),
            Col.Name => new("56", NameY(index), 125, 20),
            Col.Class => new("183", NameY(index), 200, 20),
            Col.Score => new("243", NameY(index), 30, 20),
            _ => new("39", AvatarY(index), 14, 14),
        };

        private static string NameY(int index) => index switch
        {
            0 => "132",
            1 => "154",
            2 => "176",
            _ => "213",
        };

        private static string AvatarY(int index) => index switch
        {
            0 => "135",
            1 => "157",
            2 => "179",
            _ => "216",
        };
    }

    /// <summary>The row columns of <see cref="Stock.Rect"/>.</summary>
    internal enum Col
    {
        BlueBg, RedBg, BlueName, BlueScore, RedName, RedScore,
        Name, Class, Score, Avatar,
    }
}
