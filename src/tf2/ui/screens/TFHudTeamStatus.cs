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
/// The top team-status bar: stock's <c>CTFTeamStatus</c>
/// (tf_teamstatus.cpp) over the hudmatchstatus.res TeamStatus panel — the
/// two teams' chip rows beside the clock, one chip per player. The local
/// player is always the innermost chip of their team; the rest follow in
/// entity order, enemies after the same-team players
/// (PerformLayout :452-457). A team of six or fewer takes the res's
/// max_size 19; more shrink to share team1/team2_max_expand 133 with the
/// gap RemapValClamped from 6v6_gap 4 to 12v12_gap 1 (:452, :461); blue
/// grows west from c-45, red east c47 (hudmatchstatus.res:374-380).
/// Each chip is the classimagebg tint — the local player's portrait bg,
    /// else alive ? team : dead (hudmatchstatus.res:389-392,
/// tf_teamstatus.h:42-48) — with the class portrait over it
/// (CTFClassImage, the g_pszItemClassImages tables' _alpha materials): the
/// same team sees the class, grey once dead; an enemy's class is hidden
/// (Update :136-139), so theirs is the silhouette alive and the grey
/// variant dead — stock names the dead enemy's class from the
/// class-when-killed column, which this build carries as the live one
/// (docs/plans/tf2/deferred.tsv). The two-unit health bar rides y19 under
/// the portrait, same-team lives only (Update :157-167): the fill the life
/// fraction, its colour over the res's 0.6/0.3 percentage marks, the
/// overheal the white remainder above a full bar; the respawn seconds
/// count over the portrait once the wait is up (the res's respawntime
/// label, :537-553). The match HUD's own backdrop and the clock's centre
/// art hang in <see cref="TFHud"/>; the chips are this panel's z2, over
/// the counter's z1.
/// </summary>
public sealed class TeamStatusPanel(ITFTeamStatus status, ITFRoundTimer timer, IUiTextureLibrary textures) : UiPanel
{
    private const int Red = 2;
    private const int Blue = 3;

    // The res's layout constants (hudmatchstatus.res:369-380).
    private const float MaxSize = 19f;
    private const float Gap6v6 = 4f;
    private const float Gap12v12 = 1f;
    private const float MaxExpand = 133f;
    private const float BlueBase = -45f;
    private const float RedBase = 47f;

    // The chip's rows, in res units from its top: the portrait bg and image
    // 19 tall, the health bars 2 at y19 (hudmatchstatus.res:415-484).
    private const float ChipTall = 19f;
    private const float BarTall = 2f;

    // The portrait bg tints: the local player's pair, then alive and dead
    // per team (tf_teamstatus.h:42-48; the res's dead-red override).
    private static readonly UiColor LocalRed = new(195, 169, 168, 255);
    private static readonly UiColor LocalBlue = new(168, 169, 195, 255);
    private static readonly UiColor AliveRed = new(119, 62, 61, 255);
    private static readonly UiColor AliveBlue = new(62, 81, 101, 255);
    private static readonly UiColor DeadRed = new(79, 54, 52, 255);
    private static readonly UiColor DeadBlue = new(44, 49, 51, 255);

    // The health bar: the res's bgcolor_override and the three fill tints
    // at the 0.6/0.3 percentage marks; the overheal bar's white.
    private static readonly UiColor BarBg = new(80, 80, 80, 255);
    private static readonly UiColor BarHigh = new(84, 191, 58, 255);
    private static readonly UiColor BarMed = new(191, 183, 58, 255);
    private static readonly UiColor BarLow = new(191, 58, 58, 255);
    private const float HealthMed = 0.6f;
    private const float HealthLow = 0.3f;
    private static readonly UiColor Overheal = new(255, 255, 255, 160);

    private const string Portraits = "vgui/class_portraits/";
    private const string Silhouette = Portraits + "silhouette_alpha";

    // The match backdrop and the counter's centre art, in res units:
    // hudmatchstatus.res BGFrame cs-0.5 y-5 365x28 — its 5-unit hang above
    // the screen clipped, the border the visible frame — and
    // hudroundcounter.res Background cs-0.5 y0, tall 35, wide o3.833 of the
    // tall (Panel.cpp:8712), z1 under these chips' z2. The backdrop stands
    // the art's full 35: the band label's row hangs below the BGFrame's 23
    // (hudobjectivetimepanel.res if_match ypos 21), on the strip as the
    // value is.
    private const float FrameWide = 365f;
    private const float FrameTall = 35f;
    private const float ArtTall = 35f;
    private const float ArtWide = 35f * 3.833f;

    /// <summary>Stock's ShouldDraw team arm: the chips stand only while the
    /// walk has a local player on a game team; the match chrome (the
    /// BGFrame backdrop and the round counter's art, hudmatchstatus.res
    /// if_match) stands with the match HUD itself — the clock's value and
    /// these chips read over it — so it shows whenever the timer or the
    /// teams do.</summary>
    public bool Present => status.Present;

    /// <summary>Whether the match chrome is up: the host sub's gate.</summary>
    public bool Visible => timer.Present || status.Present;

    public override void Describe(ref UiFrame ui)
    {
        if (!Visible)
            return;

        // The backdrop and centre art first: the clock value and these
        // chips read over them (the counter art is z1, the chips z2). On a
        // portrait canvas the 365-wide frame is wider than the screen: the
        // row keeps its centre and the screen clips it, every box clamped
        // into the visible width.
        var screen = StockRes.Screen(ui.Screen);
        float top = screen.Bottom + screen.Tall;
        float centre = screen.Left + screen.Wide / 2f;
        float Clamp(float left, float wide)
        {
            float slack = screen.Wide - wide;
            return slack < 0 ? screen.Left + slack / 2f : Math.Clamp(left, screen.Left, screen.Left + slack);
        }
        float frameWide = MathF.Min(FrameWide, screen.Wide);
        var frame = StockRes.Spot(new StockRect(Clamp(centre - frameWide / 2f, frameWide), top - FrameTall, frameWide, FrameTall), screen);
        // Stock's BGFrame is its TFFatLineBorder alone — no fill (hudmatchstatus
        // res:210-228). The Surface fill and the frame line are both the artless
        // fallback: once the material binds, the art itself is the backdrop the
        // clock's digits and these chips read over, and no outline should stand
        // around it (the live shot's thin rectangle over the bar).
        bool artless = StockArt.Missing(textures, "hud/comp_round_timer");
        ui.Rect(artless ? Tone.Surface : Tone.None, Size.Du(frame.Wide), Size.Du(frame.Tall))
            .AtRes(UiAnchor.BottomLeft, frame.Left, frame.Bottom)
            .Border(artless ? Tone.Line : Tone.None, 1).Layered().Key("match bg");
        var art = StockRes.Spot(new StockRect(centre - ArtWide / 2f, top - ArtTall, ArtWide, ArtTall), screen);
        StockArt.Draw(ref ui, textures, "round counter", "hud/comp_round_timer",
            art.Left, art.Bottom, art.Wide, art.Tall);

        int local = status.LocalPlayerIndex;
        int localTeam = TeamOf(local);
        int count = status.Count;
        if (localTeam == 0 || count == 0 || !status.Present)
            return;

        // The per-team counts set the chip width before the first draw
        // (PerformLayout tallies first, :404-418).
        int blues = 0;
        int reds = 0;
        for (int i = 0; i < count; i++)
        {
            int team = status.Player(i).Team;
            if (team == Blue)
                blues++;
            else if (team == Red)
                reds++;
        }

        float blueWide = ChipWide(blues);
        float redWide = ChipWide(reds);
        int sameSeen = 0;
        int enemySeen = 0;

        for (int i = 0; i < count; i++)
        {
            TFTeamStatusPlayer row = status.Player(i);
            bool sameTeam = row.Team == localTeam;
            bool isLocal = row.EntityIndex == local;

            // The innermost slot is the local player's; a team-mate follows
            // the processed count, an enemy leads its own (:454-457).
            int slot;
            if (isLocal)
                slot = 0;
            else if (sameTeam)
                slot = ++sameSeen;
            else
                slot = enemySeen++;

            float wide = row.Team == Blue ? blueWide : redWide;
            float step = wide + Gap(row.Team == Blue ? blues : reds);
            float baseX = screen.Left + screen.Wide / 2f + (row.Team == Blue ? BlueBase : RedBase);
            float left = row.Team == Blue
                ? baseX - slot * step - wide          // GROW_WEST: the right edge steps out
                : baseX + slot * step;                // GROW_EAST: the left edge does
            left = Clamp(left, wide);

            var bg = new StockRect(left, top - ChipTall, wide, ChipTall);
            var spot = StockRes.Spot(bg, screen);
            ui.Rect(UiPaint.FromGame(ChipTint(row, isLocal)), Size.Du(spot.Wide), Size.Du(spot.Tall))
                .AtRes(UiAnchor.BottomLeft, spot.Left, spot.Bottom).Layered().Key(i).Name("team chip");

            // The class image, centred in the chip; the row's number keys it.
            var portrait = new StockRect(left + (wide - ChipTall) / 2f, top - ChipTall, ChipTall, ChipTall);
            var portraitSpot = StockRes.Spot(portrait, screen);
            string image = Portrait(row, sameTeam);
            if (image.Length > 0)
                _ = StockArt.Draw(ref ui, textures, "", image,
                    portraitSpot.Left, portraitSpot.Bottom, portraitSpot.Wide, portraitSpot.Tall, keyNumber: i);

            // The health bars, same-team lives only, and the respawn count
            // over the portrait (Update :121-167, :537-553's label).
            if (sameTeam && row.Health > 0)
            {
                var bar = new StockRect(left, top - ChipTall - BarTall, wide, BarTall);
                var barSpot = StockRes.Spot(bar, screen);
                float frac = row.MaxHealth > 0 ? Math.Clamp(row.Health / (float)row.MaxHealth, 0f, 1f) : 1f;
                ui.Rect(UiPaint.FromGame(BarBg), Size.Du(barSpot.Wide), Size.Du(barSpot.Tall))
                    .AtRes(UiAnchor.BottomLeft, barSpot.Left, barSpot.Bottom).Layered().Key(100 + i).Name("health bar bg");
                ui.Rect(UiPaint.FromGame(BarTint(frac)), Size.Du(barSpot.Wide * frac), Size.Du(barSpot.Tall))
                    .AtRes(UiAnchor.BottomLeft, barSpot.Left, barSpot.Bottom).Layered().Key(200 + i).Name("health bar");
                if (row.MaxHealth > 0 && row.Health > row.MaxHealth)
                {
                    float over = Math.Clamp(row.Health / (float)row.MaxHealth - 1f, 0f, 1f);
                    ui.Rect(UiPaint.FromGame(Overheal), Size.Du(barSpot.Wide * over), Size.Du(barSpot.Tall))
                        .AtRes(UiAnchor.BottomLeft, barSpot.Left, barSpot.Bottom).Layered().Key(300 + i).Name("overheal bar");
                }
            }

            if (sameTeam && row.RespawnSeconds > 0)
                ui.Number(row.RespawnSeconds, Text.Label, Tone.Text).HudSize()
                    .Width(Size.Du(portraitSpot.Wide)).Height(Size.Du(portraitSpot.Tall)).TextAlign(Align.Centre)
                    .AtRes(UiAnchor.BottomLeft, portraitSpot.Left, portraitSpot.Bottom).Layered().Key(400 + i).Name("respawn");
        }
    }

    /// <summary>The local player's team, the row walk's own read.</summary>
    private int TeamOf(int entityIndex)
    {
        for (int i = 0, count = status.Count; i < count; i++)
        {
            var row = status.Player(i);
            if (row.EntityIndex == entityIndex)
                return row.Team;
        }

        return 0;
    }

    /// <summary>CTFClassImage's g_pszItemClassImages row: the class's
    /// portrait, the grey variant once dead; an enemy's hidden class leaves
    /// the silhouette alive (Update :136-139, :196-227).</summary>
    private static string Portrait(in TFTeamStatusPlayer row, bool sameTeam)
    {
        if (row.PlayerClass is < 1 or > 9)
            return "";
        string name = row.PlayerClass switch
        {
            1 => "scout",
            2 => "sniper",
            3 => "soldier",
            4 => "demoman",
            5 => "medic",
            6 => "heavy",
            7 => "pyro",
            8 => "spy",
            _ => "engineer",
        };
        if (!row.Alive)
            return $"{Portraits}{name}{BlueTeam(row.Team)}_grey_alpha";
        return sameTeam
            ? $"{Portraits}{name}{BlueTeam(row.Team)}_alpha"
            : Silhouette;
    }

    /// <summary>The blue portraits' suffix.</summary>
    private static string BlueTeam(int team) => team == Blue ? "_blue" : "";

    private static UiColor ChipTint(in TFTeamStatusPlayer row, bool isLocal)
    {
        if (isLocal)
            return row.Team == Blue ? LocalBlue : LocalRed;
        bool blue = row.Team == Blue;
        return row.Alive ? (blue ? AliveBlue : AliveRed) : (blue ? DeadBlue : DeadRed);
    }

    private static UiColor BarTint(float frac) => frac > HealthMed ? BarHigh : frac > HealthLow ? BarMed : BarLow;

    /// <summary>RemapValClamped(count, 6, 12, 6v6_gap, 12v12_gap)
    /// (:452): the gap the row's population spaces chips by.</summary>
    private static float Gap(int count) => count switch
    {
        <= 6 => Gap6v6,
        >= 12 => Gap12v12,
        _ => Gap6v6 + (Gap12v12 - Gap6v6) * (count - 6) / 6f,
    };

    /// <summary>Teams of six or fewer take max_size whole; larger share
    /// max_expand 133 less the gaps (:461).</summary>
    private static float ChipWide(int count) => count <= 6
        ? MaxSize
        : (MaxExpand - Gap(count) * (count - 1)) / count;
}
