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
/// The payload cart row: stock's <c>CTFHudEscort</c> (tf_hud_escort.cpp)
/// with objectivestatusescort.res's art. The track asks the library for
/// <c>hud/cart_track</c> (the LevelBar image; the <c>_neutral_opaque</c>
/// variant when hills ride the track, the res's if_single_with_hills),
/// the home point and each on-track point their 28x28 images at their
/// path distances (<c>UpdateCPImages</c>, :1046-1115 — centred on the bar
/// at <c>iBarX + iBarW * dist</c>; the owner-derived names stock's
/// <c>CSimpleControlPoint::UpdateImage</c> picks, tf_hud_escort.h:126-167),
/// and the cart rides the bar at the animated progress: stock slides the
/// EscortItemPanel <c>SetPos(newX = iBarX + iBarW*progress − w/2)</c>
/// (OnTick, :744-766, the hud_escort_interp smoothing), so the res's
/// EscortItemImage 40x40 is centred on the bar and the running man
/// (CapPlayerImage capture_icon_white), the capper count (CapNumPlayers),
/// the recede countdown when the cart will start receding within
/// <c>TF_ESCORT_RECEDE_COUNTDOWN</c> 20 s (:830-842, the seconds to one
/// more), the backwards mark (Speed_Backwards, :900) and the blocked mark
/// when a cap has time left and nobody pushes (:947-957, only when
/// neither pushers nor the countdown show, :908-911) all ride the panel
/// with it. The alarm paints the danger border — stock's cart_alert image
/// and pulse ride only on m_bMultipleTrains (UpdateAlarmAnimations,
/// :656-691), which the escort source carries. Stock's m_nTeam defaults
/// blue (:445) and the single-train escort follows it (TFHudEscort's
/// read), so the blue variants are the ones asked. The hills draw as
/// banded spans — stock paints a UV-scrolled slope polygon
/// (CEscortHillPanel::Paint), which the toolkit's whole surfaces do not
/// express (docs/plans/tf2/deferred.tsv).
/// </summary>
public sealed class EscortPanel(ITFEscort escort, IUiTextureLibrary textures) : UiPanel
{
    // objectivestatusescort.res's root: ObjectiveStatusEscort (xpos c-200
    // ypos r150, 400x150, :3-22) — TFHud places the panel there; the
    // children's coordinates are its. On a portrait canvas the du keep the
    // screen proportion (stock draws the res at XRES), scaled by k.
    internal static readonly StockBox Root = new("c-200", "r150", 400, 150);

    /// <summary>The root's own box on the frame's screen.</summary>
    private static StockRect At(ref UiFrame ui) => Root.Rect(StockRes.Screen(ui.Screen));

    // The art boxes, as the .res writes them, against the root.
    private static class Stock
    {
        // LevelBar: 73,123 254x4 (:24-55).
        public static readonly StockBox Track = new("73", "123", 254, 4);

        // HomeCPIcon: 59,111 28x28 (:79-144).
        public static readonly StockBox Home = new("59", "111", 28, 28);

        // SimpleControlPointTemplate: 60,111 28x28 (:146-176); the code
        // centres each point on the bar at its path distance
        // (UpdateCPImages, tf_hud_escort.cpp:1094-1098).
        public const float PointY = 111f, PointUnits = 28f, BarX = 73f, BarWide = 254f;

        // EscortItemPanel: 0,8 80x115 (:178-199) — the panel stock slides
        // with the cart; its children are its coordinates.
        public static readonly StockBox Items = new("0", "8", 80, 115);

        // EscortItemImage: 20,77 40x40 (:244-283).
        public static readonly StockBox Cart = new("20", "77", 40, 40);

        // CapPlayerImage: 33,80 6x12 (:387-421).
        public static readonly StockBox Man = new("33", "80", 6, 12);

        // CapNumPlayers: 39,82 30x10, textAlignment west (:423-464).
        public static readonly StockBox Count = new("39", "82", 30, 10);

        // RecedeTime, Speed_Backwards and Blocked: 35,82 10x10 each
        // (:201-242, :347-385, :466-504).
        public static readonly StockBox Mark = new("35", "82", 10, 10);
    }

    private const float RecedeCountdown = 20f;   // TF_ESCORT_RECEDE_COUNTDOWN

    // The res's own image names (materials/hud; the home and cart ones
    // stock picks by m_nTeam — blue, tf_hud_escort.cpp:445).
    private const string TrackArt = "hud/cart_track";
    private const string TrackArtOpaque = "hud/cart_track_neutral_opaque";
    private const string HomeArt = "hud/cart_home_blue";
    private const string HomeArtOpaque = "hud/cart_home_blue_opaque";
    private const string CartArt = "hud/cart_blue";
    private const string BackwardsArt = "hud/cart_arrow_left";
    private const string BlockedArt = "hud/cart_blocked";
    private const string ManArt = "capture_icon_white";

    /// <summary>TEAM_UNASSIGNED.</summary>
    private const int Unassigned = 0;

    /// <summary>TF_TEAM_RED.</summary>
    private const int Red = 2;

    /// <summary>TF_TEAM_BLUE.</summary>
    private const int Blue = 3;

    /// <summary>The on-track points' image (CSimpleControlPoint::UpdateImage,
    /// tf_hud_escort.h:126-167): the owner's colour, the opaque variant
    /// when the track carries hills.</summary>
    private static string PointName(int owner, bool opaque) => (owner, opaque) switch
    {
        (Red, false) => "hud/cart_point_red",
        (Red, true) => "hud/cart_point_red_opaque",
        (Blue, false) => "hud/cart_point_blue",
        (Blue, true) => "hud/cart_point_blue_opaque",
        (Unassigned, false) => "hud/cart_point_neutral",
        _ => "hud/cart_point_neutral_opaque",
    };

    public bool Present => escort.Present;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        // Stock draws the res at XRES — the panel scales with the screen.
        // The du here are the 1080p design's; the same proportion is kept
        // by scaling them against the design width.
        float k = ui.DesignWidth / 1920f;

        Hills(ref ui, k);

        // The LevelBar: the track art, or the plain rail when it is
        // missing so the distances still read.
        var track = Stock.Track.In(At(ref ui));
        string trackName = escort.NumHills > 0 || escort.MultipleTrains ? TrackArtOpaque : TrackArt;
        if (textures.Find(trackName) != 0)
            StockArt.Draw(ref ui, textures, "track art", trackName,
                          track.Left * k, track.Bottom * k, track.Wide * k, track.Tall * k);
        else
            using (ui.Box().Size(Size.Du(track.Wide * k), Size.Du(track.Tall * k)).Fill(Tone.Line)
                           .AtRes(UiAnchor.BottomLeft, track.Left * k, track.Bottom * k).Layered().Key("rail"))
            {
            }

        Home(ref ui, k);
        Checkpoints(ref ui, k);
        Cart(ref ui, k);
    }

    /// <summary>The hills: a band over the rail across each span, its
    /// chevron at the span's middle — uphill, or downhill when the hill
    /// reads downhill (stock paints the slope texture instead).</summary>
    private void Hills(ref UiFrame ui, float k)
    {
        var track = Stock.Track.In(At(ref ui));
        for (int hill = 0; hill < escort.NumHills; hill++)
        {
            if (!escort.Hill(hill, out float start, out float end))
                continue;

            float width = (end - start) * track.Wide * k;
            if (width <= 0f)
                continue;

            // No key: a numeric one would land on a checkpoint's point key
            // (one scope per panel) and share its id.
            using (ui.Rect(Tone.SurfaceRaised, Size.Du(width), Size.Du(track.Tall * k))
                             .AtRes(UiAnchor.BottomLeft, (track.Left + start * track.Wide) * k, track.Bottom * k)
                             .Round(Radius.S).Layered().Name("hill"))
                ui.Shape(escort.HillDownhill(hill) ? UiShape.ChevronDown : UiShape.ChevronUp,
                         Tone.TextMuted, 24 * k).At(UiAnchor.Centre);
        }
    }

    /// <summary>The home point: stock's HomeCPIcon, centred on the bar's
    /// left end (the res's 28x28 @59 against the bar at 73).</summary>
    private void Home(ref UiFrame ui, float k)
    {
        bool opaque = escort.NumHills > 0 || escort.MultipleTrains;
        string name = opaque ? HomeArtOpaque : HomeArt;
        if (textures.Find(name) == 0)
            return;

        var spot = Stock.Home.In(At(ref ui));
        StockArt.Draw(ref ui, textures, "home", name, spot.Left * k, spot.Bottom * k, spot.Wide * k, spot.Tall * k);
    }

    /// <summary>The checkpoints: one 28x28 image per on-track point,
    /// centred on the bar at its path distance (UpdateCPImages :1094-1098)
    /// — the owner's colour, the danger border when its cap is blocked
    /// when the art is missing.</summary>
    private void Checkpoints(ref UiFrame ui, float k)
    {
        bool opaque = escort.NumHills > 0 || escort.MultipleTrains;
        float size = Stock.PointUnits * StockRes.Scale;
        for (int point = 0; point < escort.Points; point++)
        {
            if (!escort.PointOnTrack(point))
                continue;

            bool blocked = escort.Blocked(point);
            string name = PointName(escort.OwningTeam(point), opaque);
            using (ui.Box().Size(Size.Du(size * k), Size.Du(size * k))
                             .AtRes(UiAnchor.BottomLeft,
                                    (Stock.BarX + escort.PathDistance(point) * Stock.BarWide - Stock.PointUnits / 2f) * StockRes.Scale * k,
                                    (At(ref ui).Tall - (Stock.PointY + Stock.PointUnits)) * StockRes.Scale * k)
                             .Layered().Key(point).Name(blocked ? "checkpoint blocked" : "checkpoint"))
            {
                if (textures.Find(name) != 0)
                    StockArt.Draw(ref ui, textures, "checkpoint art", name, 0f, 0f, size * k, size * k);
                else
                    using (ui.Box().Size(Size.Fill, Size.Fill).Fill(TFHudTeams.ToneOf(escort.OwningTeam(point)))
                                 .Border(blocked ? Tone.Danger : Tone.None, 2).Round(Radius.S))
                    {
                    }
            }
        }
    }

    /// <summary>The cart riding the track at the animated progress
    /// (centred on the bar at it, OnTick :760-766), with its pushers, its
    /// recede countdown and its marks. The whole EscortItemPanel slides:
    /// its origin is the bar centre at the progress minus its own half
    /// width (:760-766), so the children keep their res offsets.</summary>
    private void Cart(ref UiFrame ui, float k)
    {
        // hud_escort_interp 0.2 (the OnTick smoothing, :748).
        float progress = ui.Animate("escort progress", escort.Progress, Motion.Standard);
        bool pushing = escort.SpeedLevel > 0 && escort.Cappers > 0;
        bool backwards = escort.SpeedLevel < 0;
        float recede = escort.RecedeSeconds;
        bool counting = recede > 0f && recede <= RecedeCountdown;

        // The alarm: stock's alert and HudCartAlarmPulse run only on a
        // multiple-train map (UpdateAlarmAnimations, :656-691); a
        // single-train map hides the alert and stops the pulse.
        bool alarm = escort.Alarm && escort.MultipleTrains;

        var items = Stock.Items.Rect(At(ref ui));    // root units
        var cart = Stock.Cart.In(items);
        var man = Stock.Man.In(items);
        var count = Stock.Count.In(items);
        var mark = Stock.Mark.In(items);

        using (ui.Box().Size(Size.Du(items.Wide * StockRes.Scale * k), Size.Du(items.Tall * StockRes.Scale * k))
                         .AtRes(UiAnchor.BottomLeft,
                                (Stock.BarX + progress * Stock.BarWide - items.Wide / 2f) * StockRes.Scale * k,
                                items.Bottom * StockRes.Scale * k)
                         .Layered().Key("cart").Name(alarm ? "cart alarm" : "cart"))
        {
            if (textures.Find(CartArt) != 0)
                StockArt.Draw(ref ui, textures, "cart art", CartArt, cart.Left * k, cart.Bottom * k,
                              cart.Wide * k, cart.Tall * k);
            else
                ui.Rect(Tone.TeamA, Size.Du(cart.Wide * k), Size.Du(cart.Tall * k))
                    .AtRes(UiAnchor.BottomLeft, cart.Left * k, cart.Bottom * k)
                    .Border(alarm ? Tone.Danger : Tone.None, 2).Round(Radius.S).Key("cart fallback");

            if (counting)
            {
                // The seconds to one more (VarArgs("%d", iDisplaySeconds), :833),
                // centred on the res's 10x10 RecedeTime box — the count runs to
                // two digits, wider than the box, so the number sizes itself.
                using (ui.Box().Size(Size.Du(mark.Wide * k), Size.Du(mark.Tall * k))
                               .AtRes(UiAnchor.BottomLeft, mark.Left * k, mark.Bottom * k).Layered())
                    ui.Number((int)recede + 1, Text.Number, Tone.OnAccent)
                        .At(UiAnchor.Centre).Layered().Key("recede");
            }
            else if (backwards)
            {
                if (textures.Find(BackwardsArt) != 0)
                    StockArt.Draw(ref ui, textures, "backwards art", BackwardsArt,
                                  mark.Left * k, mark.Bottom * k, mark.Wide * k, mark.Tall * k);
                else
                    ui.Shape(UiShape.ChevronLeft, Tone.OnAccent, 24 * k).At(UiAnchor.Centre).Layered().Key("backwards");
            }
            else if (pushing)
            {
                // The running man and the cappers (CapPlayerImage/CapNumPlayers, :903-905).
                StockArt.Draw(ref ui, textures, "cap man art", ManArt, man.Left * k, man.Bottom * k,
                              man.Wide * k, man.Tall * k);

                // The cappers on the point, from more than one up (:403-406),
                // west-aligned at the res's CapNumPlayers box (HudFontSmallest,
                // objectivestatusescort.res:427).
                if (escort.Cappers > 1)
                    ui.Number(escort.Cappers, Text.Number, Tone.OnAccent).TextSize(TFHudType.Smallest)
                        .Width(Size.Du(count.Wide * k)).Height(Size.Du(count.Tall * k)).TextAlign(Align.Start)
                        .AtRes(UiAnchor.BottomLeft, count.Left * k, count.Bottom * k).Layered().Key("cappers");
            }
            else if (AnyBlocked())
            {
                // The blocked mark (the Blocked image, :957), only when the
                // cart shows neither pushers nor the countdown (:908-911).
                if (textures.Find(BlockedArt) != 0)
                    StockArt.Draw(ref ui, textures, "blocked art", BlockedArt,
                                  mark.Left * k, mark.Bottom * k, mark.Wide * k, mark.Tall * k);
                else
                    ui.Shape(UiShape.Cross, Tone.Danger, 24 * k).At(UiAnchor.Centre).Layered().Key("blocked");
            }
        }
    }

    private bool AnyBlocked()
    {
        for (int point = 0; point < escort.Points; point++)
            if (escort.PointOnTrack(point) && escort.Blocked(point))
                return true;
        return false;
    }
}
