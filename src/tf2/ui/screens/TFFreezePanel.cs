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
/// The freeze panel (stock's <c>CTFFreezePanel</c>,
/// game/client/tf/tf_hud_freezepanel.cpp) at stock's own layout:
/// freezepanel_basic.res's $WIN32 canvas through <see cref="StockRes"/> —
/// the FreezePanelBase at r270 93 267x200 (:224-232), the killer-team
/// colour_panel bar at 8 160 256x33 (:234-253), the "You were killed by"
/// label at 40 162 (:255-273), the killer's name at 61 173 (repositioned
/// beside the image when it drew, tf_hud_freezepanel.cpp:586-598), the
/// killer's class image in the 14x14 Steam-avatar slot (:311-325 — no
/// Steam avatar in this build, the class image takes its place), the
/// killer's health cross per freezepanelkillerhealth.res (bg 5 5 22x22,
/// cross 7 7 18x18, value 6 11 20x10), the nemesis sub-panel at 0 130
/// 306x38 with its brown bar, nemesis icon and label (:327-406), and the
/// freeze-cam screenshot hint at c-83 50 166x40 (:409-467) — hidden when
/// nothing is bound to screenshot, as stock's
/// <c>ShowSnapshotPanel</c> does (:997-1003).
///
/// <para>
/// The label's and nemesis panel's arms read the death's flags
/// (<c>FireGameEvent</c>, :234-246 and the label arms :380-402): a
/// domination names the killer our nemesis ("You were killed again by"),
/// a revenge names theirs ("Revenge!" — the killer settled the score).
/// A dead killer takes the "_Dead" label and the dead-cross material.
/// The nemesis sub-panel exists only when the flags
/// name one: stock also shows it when the killer still dominates us but
/// this kill did not flag it (its live <c>m_bPlayerDominated</c> read,
/// :601-626) — a bitfield this build does not carry
/// (docs/divergences.md). The panel shows at the death, not on entering
/// the freeze cam, and the hint shows at once, not 1.25 s in
/// (<c>ShowSnapshotPanelIn</c>, :969-976): no freeze cam to delay from.
/// </para>
/// </summary>
public sealed class TFFreezePanel(ITFDeathScreen death, IUiTextureLibrary textures, IUiLocalisation localise,
                                  IUiKeyBindingStore keys) : UiScreen
{
    public override string Name => "tf2freezepanel";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Stock's panel shows on entering the freeze cam and hides when
    /// the death is over (<c>hide_freezepanel</c>, :249-252). The freeze cam
    /// is entered only with an observer target that is not us
    /// (tf_player.cpp:13956-13968), and a suicide or a world death leaves
    /// <c>m_hObserverTarget</c> NULL (tf_player.cpp:12487-12490) — so those
    /// deaths never fire <c>show_freezepanel</c> (c_baseplayer.cpp:885-910)
    /// and draw no panel: a killer is the gate, not just the death.</summary>
    public override bool Visible => death.Showing && death.HasKiller;

    public override void Describe(ref UiFrame ui)
    {
        // The panel rides in a host at its .res rect: the "r270" xpos is
        // the panel's right edge 3 units from the screen's, ypos 93 its
        // top 93 from the screen's — edge-anchored, so it tracks the
        // screen it is drawn on. The children hang inside it at their own
        // .res coordinates.
        var panel = Stock.Base.Rect(StockRect.Screen);
        var at = StockRes.Spot(panel, StockRect.Screen);
        using (ui.Box().AtRes(UiAnchor.TopRight, at.Right, at.Top)
                       .Size(Size.Du(at.Wide), Size.Du(at.Tall)).Layered().Key("panel"))
        {
            // The killer's team dyes the panel bar (:580-584). The plate at
            // the same rect is the contrast rule's backdrop and the art's
            // fallback.
            var bg = StockRes.Spot(Stock.Bg.Rect(panel), panel);
            if (StockArt.Missing(textures, death.KillerTeam == Blue ? "hud/color_panel_blu" : "hud/color_panel_red"))
                ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                    .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("panel plate");
            Art(ref ui, "panel bg", death.KillerTeam == Blue ? "hud/color_panel_blu" : "hud/color_panel_red",
                bg.Left, bg.Bottom, bg.Wide, bg.Tall);

            // "You were killed by", the nemesis arm when the kill dominated
            // us (:380-402), and "_Dead" on a dead killer (:383-401); no
            // killer keeps the plain label, stock's pKiller-null arm. The
            // killer's name sits at the avatar's x when no image drew,
            // beside it when one did (:586-598).
            bool dominates = (death.DeathFlags & Domination) != 0;
            string label = !death.HasKiller ? "#TF2_Freeze_Killer"
                : dominates ? (death.KillerAlive ? "#TF2_Freeze_Nemesis" : "#TF2_Freeze_Nemesis_Dead")
                : death.KillerAlive ? "#TF2_Freeze_Killer" : "#TF2_Freeze_Killer_Dead";
            var labelBox = StockRes.Spot(Stock.Label.Rect(panel), panel);
            ui.Text(label, Text.Caption, Tone.Text)
                .Width(Size.Du(Math.Min(labelBox.Wide, at.Wide - labelBox.Left))).TextAlign(Align.Start)
                .AtRes(UiAnchor.BottomLeft, labelBox.Left, labelBox.Bottom).Layered().Key("killed by");

            bool portrait = false;
            if (death.HasKiller)
            {
                var avatar = StockRes.Spot(Stock.Avatar.Rect(panel), panel);
                string image = HealthPanel.Portrait(death.KillerClass, death.KillerTeam);
                if (StockArt.Missing(textures, image))
                    ui.Rect(Tone.Surface, Size.Du(avatar.Wide), Size.Du(avatar.Tall))
                        .AtRes(UiAnchor.BottomLeft, avatar.Left, avatar.Bottom).Layered().Key("portrait plate");
                portrait = image.Length > 0
                    && Art(ref ui, "killer portrait", image, avatar.Left, avatar.Bottom, avatar.Wide, avatar.Tall);
            }

            var nameBox = StockRes.Spot((portrait ? Stock.NameBeside : Stock.NameAlone).Rect(panel), panel);
            if (death.KillerName.Length > 0)
                ui.Data(death.KillerName, Text.Body, Tone.Text)
                    .Width(Size.Du(Math.Min(nameBox.Wide, at.Wide - nameBox.Left))).TextAlign(Align.Start)
                    .AtRes(UiAnchor.BottomLeft, nameBox.Left, nameBox.Bottom).Layered().Key("killer name");

            // The killer's health cross (freezepanelkillerhealth.res), only
            // with a killer: stock's null-killer path leaves it untouched.
            if (death.HasKiller)
                KillerHealth(ref ui, panel);

            // The nemesis sub-panel, when the death's flags name one
            // (:601-664): "New Nemesis!" on a domination, "Revenge!" on a
            // revenge — stock's third arm (the killer still dominates us
            // without this kill flagging it) needs the replicated
            // m_bPlayerDominated this build does not carry.
            string? nemesis = (death.DeathFlags & Domination) != 0 ? "#TF2_Freeze_NewNemesis"
                : (death.DeathFlags & Revenge) != 0 ? "#TF2_Freeze_GotRevenge" : null;
            if (nemesis is { } text)
            {
                var nemesisPanel = Stock.NemesisSub.Rect(panel);
                var nemesisBg = StockRes.Spot(Stock.NemesisBg.Rect(nemesisPanel), panel);
                if (StockArt.Missing(textures, "hud/color_panel_brown"))
                    ui.Rect(Tone.Surface, Size.Du(nemesisBg.Wide), Size.Du(nemesisBg.Tall))
                        .AtRes(UiAnchor.BottomLeft, nemesisBg.Left, nemesisBg.Bottom).Layered().Key("nemesis plate");
                Art(ref ui, "nemesis bg", "hud/color_panel_brown",
                    nemesisBg.Left, nemesisBg.Bottom, nemesisBg.Wide, nemesisBg.Tall);
                var icon = StockRes.Spot(Stock.NemesisIcon.Rect(nemesisPanel), panel);
                Art(ref ui, "nemesis icon", "hud/leaderboard_nemesis_freezecam",
                    icon.Left, icon.Bottom, icon.Wide, icon.Tall);
                var nemesisLabel = StockRes.Spot(Stock.NemesisLabel.Rect(nemesisPanel), panel);
                ui.Text(text, Text.Body, Tone.Text)
                    .Width(Size.Du(nemesisLabel.Wide)).TextAlign(Align.Start)
                    .AtRes(UiAnchor.BottomLeft, nemesisLabel.Left, nemesisLabel.Bottom).Layered().Key("nemesis");
            }
        }

        // The freeze-cam screenshot hint, only while screenshot is bound
        // (ShowSnapshotPanel, :992-1015): stock's token is
        // "[%s1] Save this moment!" (tf_english.txt:519) with the key in
        // place of %s1. Its own host rides centred (the "c-83" xpos).
        string? bound = keys.KeyFor("screenshot");
        if (bound is { } key && key.Length > 0 && key != "(null)")
        {
            var shot = Stock.Screenshot.Rect(StockRect.Screen);
            var shotAt = StockRes.Spot(shot, StockRect.Screen);
            using (ui.Box().AtRes(UiAnchor.Top, shotAt.CentreX, shotAt.Top)
                           .Size(Size.Du(shotAt.Wide), Size.Du(shotAt.Tall)).Layered().Key("hint panel"))
            {
                var shotBg = StockRes.Spot(Stock.ShotBg.Rect(shot), shot);
                if (StockArt.Missing(textures, "hud/freezecam_black_bg"))
                    ui.Rect(Tone.Surface, Size.Du(shotBg.Wide), Size.Du(shotBg.Tall))
                        .AtRes(UiAnchor.BottomLeft, shotBg.Left, shotBg.Bottom).Layered().Key("hint plate");
                Art(ref ui, "hint bg", "hud/freezecam_black_bg",
                    shotBg.Left, shotBg.Bottom, shotBg.Wide, shotBg.Tall);
                var shotIcon = StockRes.Spot(Stock.ShotIcon.Rect(shot), shot);
                Art(ref ui, "camera", "hud/ico_camera", shotIcon.Left, shotIcon.Bottom, shotIcon.Wide, shotIcon.Tall);
                var shotLabel = StockRes.Spot(Stock.ShotLabel.Rect(shot), shot);
                string hint = (localise.Find("#TF2_Freeze_Snapshot") ?? "").Replace("%s1", key);
                ui.Data(hint, Text.Caption, Tone.Text)
                    .Width(Size.Du(shotLabel.Wide)).TextAlign(Align.Start)
                    .AtRes(UiAnchor.BottomLeft, shotLabel.Left, shotLabel.Bottom).Layered().Key("hint");
            }
        }
    }

    /// <summary>The killer's health cross (CTFFreezePanelHealth, the
    /// CTFHudPlayerHealth rules at the freeze panel's own sizes): the bg
    /// and the fg cropped from the bottom up to the life fraction
    /// (CTFHealthPanel::Paint, tf_hud_playerstatus.cpp:601-633), the dead
    /// material whole at no health, the bonus image grown by the overheal
    /// fraction (:789-798) at the freeze panel's own HealthBonusPosAdj 9
    /// (freezepanel_basic.res:286), the warning tint under 0.49 (:819),
    /// and the number only while there is health (:847-849).</summary>
    private void KillerHealth(ref UiFrame ui, StockRect panel)
    {
        var health = Stock.HealthPanel.Rect(panel);
        var crossBg = StockRes.Spot(Stock.CrossBg.Rect(health), panel);
        if (StockArt.Missing(textures, "hud/health_bg"))
            ui.Rect(Tone.Surface, Size.Du(crossBg.Wide), Size.Du(crossBg.Tall))
                .AtRes(UiAnchor.BottomLeft, crossBg.Left, crossBg.Bottom).Layered().Key("cross plate");
        Art(ref ui, "cross bg", "hud/health_bg", crossBg.Left, crossBg.Bottom, crossBg.Wide, crossBg.Tall);

        var cross = StockRes.Spot(Stock.Cross.Rect(health), panel);
        float max = death.KillerMaxHealth;
        bool dying = death.KillerAlive && max >= 1 && death.KillerHealth < max * Stock.DeathWarning;

        if (death.KillerHealth > max && death.KillerMaxBuffedHealth > max)
        {
            float percent = Math.Min((death.KillerHealth - max) / (death.KillerMaxBuffedHealth - max), 1f);
            float centreX = crossBg.Left + crossBg.Wide / 2;
            float centreY = crossBg.Bottom + crossBg.Tall / 2;
            float size = crossBg.Wide + 2f * Stock.BonusAdj * StockRes.Scale * percent;
            Art(ref ui, "health bonus", "hud/health_over_bg", centreX - size / 2, centreY - size / 2, size, size);
        }

        if (death.KillerHealth > 0 && death.KillerAlive)
        {
            float life = Math.Clamp(death.KillerHealth / max, 0f, 1f);
            Art(ref ui, "cross", "hud/health_color", cross.Left, cross.Bottom, cross.Wide, cross.Tall,
                dying ? Tone.Danger : default, new UiRect(0, 1f - life, 1f, life));
        }
        else
        {
            Art(ref ui, "dead cross", "hud/health_dead", cross.Left, cross.Bottom, cross.Wide, cross.Tall);
        }

        if (death.KillerHealth > 0)
        {
            var value = StockRes.Spot(Stock.Value.Rect(health), panel);
            ui.Number(death.KillerHealth, Text.Caption, Tone.Text)
                .Width(Size.Du(value.Wide)).Height(Size.Du(value.Tall)).TextAlign(Align.Centre)
                .AtRes(UiAnchor.BottomLeft, value.Left, value.Bottom).Layered().Key("killer health");
        }
    }

    /// <summary>The named material's texture at its .res coordinate
    /// (through the panels' shared <see cref="StockArt"/>); a miss (handle
    /// 0) draws nothing — the plate at the same coordinates is the
    /// fallback.</summary>
    private bool Art(ref UiFrame ui, string key, string name, float left, float bottom, float wide, float tall,
                     UiPaint tint = default, UiRect uv = default)
        => StockArt.Draw(ref ui, textures, key, name, left, bottom, wide, tall, tint, uv);

    /// <summary>The .res boxes on the 640x480 canvas freezepanel_basic.res
    /// is written in, resolved to du per Describe. The FreezePanelBase at
    /// r270 93 267x200 ($WIN32 :224-232): its "r" xpos reaches its LEFT
    /// edge 270 units from the screen's right; plain ypos 93 its top edge
    /// from the screen's top.</summary>
    internal static class Stock
    {
        public static readonly StockRect Canvas = StockRect.Screen;

        public static readonly StockBox Base = new("r270", "93", 267, 200);

        // The panel bar, the "You were killed by" label, the health child
        // and the killer name/image (:234-325). The label and the name
        // boxes hang 25-50 units past the panel's right edge (40 + 252 >
        // 267); the text is left-aligned and short, so placement clamps
        // them to the panel's edge — stock's visible line is the same.
        public static readonly StockBox Bg = new("8", "160", 256, 33);
        public static readonly StockBox Label = new("40", "162", 252, 12);
        public static readonly StockBox HealthPanel = new("8", "160", 31, 31);
        public static readonly StockBox Avatar = new("49", "174", 14, 14);
        public static readonly StockBox NameAlone = new("49", "173", 252, 18);   // :596 iAvX
        public static readonly StockBox NameBeside = new("65", "173", 252, 18);  // :592 iAvX + wide + XRES(2)

        // The health child's own .res (freezepanelkillerhealth.res): the
        // bg 5 5 22x22, the cross 7 7 18x18, the value 6 11 20x10.
        public static readonly StockBox CrossBg = new("5", "5", 22, 22);
        public static readonly StockBox Cross = new("7", "7", 18, 18);
        public static readonly StockBox Value = new("6", "11", 20, 10);
        public const float BonusAdj = 9f;            // HealthBonusPosAdj (freezepanel_basic.res:286)
        public const float DeathWarning = 0.49f;     // HealthDeathWarning (freezepanel_basic.res:287)

        // The nemesis sub-panel (0 130 306x38) and its children (:327-406).
        public static readonly StockBox NemesisSub = new("0", "130", 306, 38);
        public static readonly StockBox NemesisBg = new("95", "4", 161, 30);
        public static readonly StockBox NemesisIcon = new("95", "-1", 36, 36);
        public static readonly StockBox NemesisLabel = new("134", "10", 112, 18);

        // The screenshot hint on the screen canvas, c-83 50 166x40
        // (:409-467).
        public static readonly StockBox Screenshot = new("c-83", "50", 166, 40);
        public static readonly StockBox ShotBg = new("0", "8", 166, 38);
        public static readonly StockBox ShotIcon = new("3", "0", 36, 36);
        public static readonly StockBox ShotLabel = new("40", "25", 133, 12);
    }

    // The raw TF team numbers (tf_shareddefs.h) and the death flags'
    // domination bit (TF_DEATH_DOMINATION, tf_shareddefs.h:931 — the
    // server's TFDominations carries the family; this side names only the
    // bit it reads).
    private const int Blue = 3;
    private const int Domination = 0x0001;
    private const int Revenge = 0x0004;
}
