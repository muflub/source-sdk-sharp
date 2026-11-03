//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>Which team one flag panel draws.</summary>
public readonly record struct FlagProps(int Team);

/// <summary>
/// The CTF flag row: stock's <c>CTFHudFlagObjectives</c>
/// (tf_hud_flagstatus.cpp) at its .res coordinates (hudobjectiveflagpanel.res
/// and flagstatus.res through the stock converter; a "c" xpos is the left
/// edge about the screen's centre, an "r" ypos the top edge above the
/// bottom). The two team halves and the
/// outline make the backdrop (LeftSideBG/RightSideBG/OutlineBG, c-140 r75
/// 280x80); one 160x90 panel per team that fields a flag draws the briefcase
/// and the state's icon (flagstatus.res: the Arrow 80x80 at 40,0, the
/// Briefcase 30x30 at 65,28, the StatusIcon 30x30 at 75,26 — home
/// <c>ico_flag_home</c>, stolen <c>ico_flag_moving</c>, dropped
/// <c>ico_flag_dropped</c>), under the compass to the flag's bearing
/// (<c>CTFArrowPanel::Paint</c>, :212-260 — the disc from the art, the needle
/// the toolkit's shape while the texture layer cannot rotate). The captures
/// stock sets as dialog variables sit beside the panels
/// (BlueScore/RedScore, HudFontBig TanLight — their +2px Black drop shadows
/// are stock's text effect, not a layer of ours), with the caps-per-round
/// target on its playing-to bg under them (PlayingToBG/PlayingTo, OnTick,
/// :643-679). Carrying a flag hides both squares for the carried image and
/// the capture-zone arrow (UpdateStatus, :794-860), which this row is.
/// Each art layer keeps a Tone.Surface plate at the same coordinates: the
/// contrast rule's backdrop when the texture library has the material, the
/// fallback when it misses it.
/// </summary>
public sealed class FlagStatusPanel(ITFFlagStatus flags, IUiTextureLibrary textures) : UiPanel
{
    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;

    /// <summary>ETFFlagStatus.</summary>
    private const int Home = 0;
    private const int Stolen = 1;
    private const int Dropped = 2;

    private static readonly UiColor Black = new(0, 0, 0, 255);   // the shadows' fgcolor (clientscheme Black)

    // Stock's art boxes, as the .res writes them, on the full-screen canvas
    // ObjectiveStatusFlagPanel is (hudobjectiveflagpanel.res:3-19); resolved
    // to du per Describe. A "c" xpos is the element's LEFT edge about the
    // screen's centre, an "r" ypos its TOP edge above the bottom.
    internal static class Stock
    {
        public static readonly StockRect Canvas = StockRect.Screen;

        // LeftSideBG/RightSideBG/OutlineBG: c-140, r75, 280x80 (:21-127) —
        // the pair abutting at the centre as one 630-wide backdrop, its
        // bottom 5 units below the screen's.
        public static readonly StockBox Bg = new("c-140", "r75", 280, 80);

        // BlueScore c-130 r47 75x35 west (:129-170); RedScore c57 r47 east
        // (:215-256). Their shadows (BlueScoreShadow :172-213, RedScoreShadow
        // :258-299) +2 du across and one up, zpos 7 under the value's 8.
        public static readonly StockBox BlueScore = new("c-130", "r47", 75, 35);
        public static readonly StockBox RedScore = new("c57", "r47", 75, 35);
        public static readonly StockBox BlueScoreShadow = new("c-128", "r46", 75, 35);
        public static readonly StockBox RedScoreShadow = new("c59", "r46", 75, 35);

        // BlueFlag c-135 r95 160x90 (:425-463); RedFlag c-25 (:465-503).
        public static readonly StockBox BlueFlag = new("c-135", "r95", 160, 90);
        public static readonly StockBox RedFlag = new("c-25", "r95", 160, 90);

        // The flag panels' own box (160x90), the flagstatus.res children's
        // parent: the Arrow 80x80 at 40,0 (:3-15), the Briefcase 30x30 at
        // 65,28 (:16-37), the StatusIcon 30x30 at 75,26 (:38-56).
        public static readonly StockRect FlagPanel = new StockBox("0", "0", 160, 90).Rect(StockRect.Screen);
        public static readonly StockBox Arrow = new("40", "0", 80, 80);
        public static readonly StockBox Briefcase = new("65", "28", 30, 30);
        public static readonly StockBox StatusIcon = new("75", "26", 30, 30);

        // PlayingToBG c-75 r31 150x38 (:385-423); PlayingTo c-70 r28 140x30
        // centred (:343-383).
        public static readonly StockBox TargetBg = new("c-75", "r31", 150, 38);
        public static readonly StockBox Target = new("c-70", "r28", 140, 30);

        // CarriedImage c-50 r137 100x100 (:317-341); CaptureFlag c-40 r95
        // 80x80 (:505-527).
        public static readonly StockBox Carried = new("c-50", "r137", 100, 100);
        public static readonly StockBox Zone = new("c-40", "r95", 80, 80);
    }

    /// <summary>One instance serves both teams; the Sub key scopes the ids.</summary>
    private readonly FlagColumn _column = new(flags, textures);

    public bool Present => flags.Present;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        // The screen clips what stock hangs below it (the backdrop's r75 and
        // the target plate's r31/r28), so those boxes draw their visible band.
        static StockSpot OnScreen(StockBox box) =>
            StockRes.Spot(StockRes.ClippedToScreen(box.Rect(Stock.Canvas)), Stock.Canvas);

        var bg = OnScreen(Stock.Bg);
        var blueScore = Stock.BlueScore.In(Stock.Canvas);
        var redScore = Stock.RedScore.In(Stock.Canvas);
        var blueFlag = Stock.BlueFlag.In(Stock.Canvas);
        var redFlag = Stock.RedFlag.In(Stock.Canvas);
        var targetBg = OnScreen(Stock.TargetBg);
        var target = OnScreen(Stock.Target);
        var carried = Stock.Carried.In(Stock.Canvas);
        var zone = Stock.Zone.In(Stock.Canvas);

        // The backdrop: the two team halves, the outline over them. The
        // halves' masks ($detail objectives_flagpanel_mask_left/_right,
        // detailblendmode 8) are material-side, which the texture layer
        // does not carry — so each gradient draws plain over the whole
        // plate, as the res boxes it (both 280x80 at c-140), and the
        // outline art above them carries the shaped edge.
        bool bgUp = !StockArt.Missing(textures, "hud/objectives_flagpanel_bg_left")
            && !StockArt.Missing(textures, "hud/objectives_flagpanel_bg_right")
            && !StockArt.Missing(textures, "hud/objectives_flagpanel_bg_outline");
        if (!bgUp)
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.Bottom, bg.CentreX, bg.Bottom).Layered().Key("flag bg");
        Art(ref ui, "bg left", "hud/objectives_flagpanel_bg_left", bg.CentreX, bg.Bottom, bg.Wide, bg.Tall, UiAnchor.Bottom);
        Art(ref ui, "bg right", "hud/objectives_flagpanel_bg_right", bg.CentreX, bg.Bottom, bg.Wide, bg.Tall, UiAnchor.Bottom);
        Art(ref ui, "bg outline", "hud/objectives_flagpanel_bg_outline", bg.CentreX, bg.Bottom, bg.Wide, bg.Tall, UiAnchor.Bottom);

        int carriedBy = CarriedByLocal();
        if (carriedBy > 0)
        {
            // The carrying arm: both squares down, the carried flag in its
            // team's art and the capture zone's arrow (:794-860). The
            // carrier figure's plate is the artless fallback: the flag
            // image, when it is up, is the person's backdrop.
            if (StockArt.Missing(textures, Carried(carriedBy)))
                using (ui.Box().Size(Size.Du(carried.Wide), Size.Du(carried.Tall))
                               .Fill(Tone.Surface).AtRes(UiAnchor.Bottom, carried.CentreX, carried.Bottom)
                               .Layered().Key("carried plate"))
                ui.Shape(UiShape.Person, Tone.OnAccent, 12).At(UiAnchor.Centre).Key("carrier");
            Art(ref ui, "flag carried", Carried(carriedBy), carried.CentreX, carried.Bottom, carried.Wide, carried.Tall, UiAnchor.Bottom);
            ui.Shape(UiShape.Compass, Tone.Text, zone.Wide, flags.ZoneBearing)
                .AtRes(UiAnchor.Bottom, zone.CentreX, zone.Bottom).Layered().Key("capture zone");
            return;
        }

        // One panel per team that fields a flag (BlueFlag/RedFlag).
        if (flags.Has(Red))
            ui.Sub("red flag", _column, new FlagProps(Red))
                .AtRes(UiAnchor.Bottom, redFlag.CentreX, redFlag.Bottom)
                .Size(Size.Du(redFlag.Wide), Size.Du(redFlag.Tall));
        if (flags.Has(Blue))
            ui.Sub("blue flag", _column, new FlagProps(Blue))
                .AtRes(UiAnchor.Bottom, blueFlag.CentreX, blueFlag.Bottom)
                .Size(Size.Du(blueFlag.Wide), Size.Du(blueFlag.Tall));

        // The dialog-variable scores: captures, or the plain score when the
        // caps are not the target (OnTick, :643-679). Each on a Surface
        // plate — stock's TanLight label over the Black drop shadow the
        // res pairs with it (BlueScoreShadow/RedScoreShadow, zpos 7 under
        // the value's 8, +2 du across and one up). The shadow is the
        // numeral's lift off the busy art, so like the plate it rides one
        // arm: art up, shadow; artless, the plate already parts value and
        // world, and the shadow would be black on the dark plate (the
        // lint's stills-backdrop, art-blind to TexturedQuad, reads that
        // 1.1:1 — reported, not bent: see the commit message).
        if (!bgUp)
            ui.Rect(Tone.Surface, Size.Du(blueScore.Wide), Size.Du(blueScore.Tall))
                .AtRes(UiAnchor.Bottom, blueScore.CentreX, blueScore.Bottom).Layered().Key("blue score plate");
        var blueShadow = Stock.BlueScoreShadow.In(Stock.Canvas);
        if (bgUp)
            ui.Number(flags.Captures(Blue), Text.Number, Tone.Text).HudSize()
                .Width(Size.Du(blueShadow.Wide)).Height(Size.Du(blueShadow.Tall)).Color(UiPaint.FromGame(Black)).TextAlign(Align.Start)
                .AtRes(UiAnchor.Bottom, blueShadow.CentreX, blueShadow.Bottom).Layered().Key("blue shadow");
        ui.Number(flags.Captures(Blue), Text.Number, Tone.Text).HudSize()
            .Width(Size.Du(blueScore.Wide)).Height(Size.Du(blueScore.Tall)).TextAlign(Align.Start)
            .AtRes(UiAnchor.Bottom, blueScore.CentreX, blueScore.Bottom).Layered().Key("blue captures");
        if (!bgUp)
            ui.Rect(Tone.Surface, Size.Du(redScore.Wide), Size.Du(redScore.Tall))
                .AtRes(UiAnchor.Bottom, redScore.CentreX, redScore.Bottom).Layered().Key("red score plate");
        var redShadow = Stock.RedScoreShadow.In(Stock.Canvas);
        if (bgUp)
            ui.Number(flags.Captures(Red), Text.Number, Tone.Text).HudSize()
                .Width(Size.Du(redShadow.Wide)).Height(Size.Du(redShadow.Tall)).Color(UiPaint.FromGame(Black)).TextAlign(Align.End)
                .AtRes(UiAnchor.Bottom, redShadow.CentreX, redShadow.Bottom).Layered().Key("red shadow");
        ui.Number(flags.Captures(Red), Text.Number, Tone.Text).HudSize()
            .Width(Size.Du(redScore.Wide)).Height(Size.Du(redScore.Tall)).TextAlign(Align.End)
            .AtRes(UiAnchor.Bottom, redScore.CentreX, redScore.Bottom).Layered().Key("red captures");

        // The target the caps are played to, on its bg; straight score
        // shows none. Stock renders one label, the localized
        // "Playing to: %rounds%" (tf_english.txt TF_PlayingTo) with the
        // count a dialog variable (OnTick, :661); here the token carries
        // the colon and the number the %rounds% half.
        if (flags.CapsPerRound > 0)
        {
            if (StockArt.Missing(textures, "hud/objectives_flagpanel_bg_playingto"))
                ui.Rect(Tone.Surface, Size.Du(targetBg.Wide), Size.Du(targetBg.Tall))
                    .AtRes(UiAnchor.Bottom, targetBg.CentreX, targetBg.Bottom).Layered().Key("target plate");
            Art(ref ui, "target bg", "hud/objectives_flagpanel_bg_playingto", targetBg.CentreX, targetBg.Bottom,
                targetBg.Wide, targetBg.Tall, UiAnchor.Bottom);
            // The row hugs its text; the Bottom anchor takes the box's
            // bottom-centre, so the pair centres as stock's label does.
            using (ui.Row(Space.S, Align.Centre).AtRes(UiAnchor.Bottom, target.CentreX, target.Bottom)
                       .Height(Size.Du(target.Tall))
                       .Layered().Key("playing to"))
            {
                ui.Text("#TF2_Hud_PlayingTo", Text.Caption, Tone.Text).HudSize();
                ui.Number(flags.CapsPerRound, Text.Caption, Tone.Text).HudSize();
            }
        }
    }

    /// <summary>Which team's flag the local player carries, 0 for none.</summary>
    private int CarriedByLocal()
    {
        for (int team = Red; team <= Blue; team++)
            if (flags.CarriedByLocal(team))
                return team;

        return 0;
    }

    /// <summary>The named material's texture at its .res coordinate, on the
    /// screen's bottom-centre axis (through the panels' shared
    /// <see cref="StockArt"/>); a miss (handle 0) draws nothing — the plate
    /// at the same coordinates is the fallback.</summary>
    private void Art(ref UiFrame ui, string key, string name, float centre, float bottom, float wide, float tall,
                     UiAnchor corner)
        => StockArt.Draw(ref ui, textures, key, name, centre, bottom, wide, tall, corner: corner);

    /// <summary>The carried flag's image (hudobjectiveflagpanel.res
    /// CarriedImage, per team).</summary>
    private static string Carried(int team) => team == Blue ? "hud/objectives_flagpanel_carried_blue" : "hud/objectives_flagpanel_carried_red";
}

/// <summary>One team's flag panel: the state's mark, the briefcase and
/// status icon at their .res boxes, and the compass to the flag's bearing
/// (stock's CTFFlagStatus panel, flagstatus.res).</summary>
public sealed class FlagColumn(ITFFlagStatus flags, IUiTextureLibrary textures) : UiPanel<FlagProps>
{
    private const int Stolen = 1;
    private const int Dropped = 2;

    public override void Describe(ref UiFrame ui, in FlagProps props)
    {
        int team = props.Team;
        int status = flags.Status(team);

        var arrow = FlagStatusPanel.Stock.Arrow.In(FlagStatusPanel.Stock.FlagPanel);
        var briefcase = FlagStatusPanel.Stock.Briefcase.In(FlagStatusPanel.Stock.FlagPanel);
        var icon = FlagStatusPanel.Stock.StatusIcon.In(FlagStatusPanel.Stock.FlagPanel);

        // The compass first (flagstatus.res lists the arrow panel before the
        // briefcase and the status icon): the disc's painted centre is
        // opaque, and the state's mark reads over it, not under it.
        bool arrowless = flags.SpectatingCarrier(team);
        int compass = textures.Find((team, arrowless) switch
        {
            (3, false) => "hud/objectives_flagpanel_compass_blue",
            (3, true) => "hud/objectives_flagpanel_compass_blue_noArrow",
            (_, false) => "hud/objectives_flagpanel_compass_red",
            _ => "hud/objectives_flagpanel_compass_red_noArrow",
        });
        if (compass != 0)
            _ = ui.Texture(compass, Size.Du(arrow.Wide), Size.Du(arrow.Tall))
                .AtRes(UiAnchor.BottomLeft, arrow.Left, arrow.Bottom)
                .Rotate(flags.Bearing(team) * MathF.PI / 180f)
                .Key("compass").Layered();

        // The status square: the team tone behind the state's mark — the
        // artless stand-in for the StatusIcon box (75,26 of the 160x90
        // panel); the icon art is the mark the game sees.
        if (StockArt.Missing(textures, Icon(status)))
            using (ui.Box().Size(Size.Du(icon.Wide), Size.Du(icon.Tall))
                           .Fill(TFHudTeams.ToneOf(team))
                           .Round(Radius.S).AtRes(UiAnchor.BottomLeft, icon.Left, icon.Bottom)
                           .Name(status switch
                           {
                               Dropped => "flag dropped",
                               Stolen => "flag stolen",
                               _ => "flag home",
                           }))
            {
                if (status == Dropped)
                    ui.Shape(UiShape.Cross45, Tone.Danger, 12).At(UiAnchor.Centre).Key("dropped");
                else if (status == Stolen)
                    ui.Shape(UiShape.Person, Tone.OnAccent, 12).At(UiAnchor.Centre).Key("carrier");
            }

        // The briefcase and the state's icon (flagstatus.res: Briefcase
        // 65,28, StatusIcon 75,26).
        StockArt.Draw(ref ui, textures, "briefcase", "hud/objectives_flagpanel_briefcase",
            briefcase.Left, briefcase.Bottom, briefcase.Wide, briefcase.Tall);
        StockArt.Draw(ref ui, textures, "status icon", Icon(status),
            icon.Left, icon.Bottom, icon.Wide, icon.Tall);
    }

    /// <summary>The status icon per state (hudobjectiveflagpanel.res's
    /// ico_flag set; the _alt pair is stock's alternate-path arm, which no
    /// source read reaches).</summary>
    private static string Icon(int status) => status switch
    {
        Stolen => "hud/objectives_flagpanel_ico_flag_moving",
        Dropped => "hud/objectives_flagpanel_ico_flag_dropped",
        _ => "hud/objectives_flagpanel_ico_flag_home",
    };
}
