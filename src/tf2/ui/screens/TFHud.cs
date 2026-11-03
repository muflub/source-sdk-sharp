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
/// The TF2 in-game HUD (docs/plans/tf2/stage3_client.md Stage 3.1), in the
/// stock layout: the class art and cross bottom-left, the ammo bottom-right,
/// the round clock top-centre. One screen, one budget: its whole life is
/// the in-game look. This screen owns all anchoring; the sub-panels draw
/// content only, so nothing can drift onto anything else.
/// </summary>
public sealed class TFHud(ITFLocalPlayer player, HealthPanel health, AmmoPanel ammo,
                          ITFRoundTimer timer, RoundTimerPanel clock, ControlPointIconsPanel icons,
                          EscortPanel escort, FlagStatusPanel flags, MedicChargePanel medic,
                          TeamStatusPanel teams, CloakPanel cloak, DisguisePanel disguise, ChatPanel chat) : UiScreen
{
    public override string Name => "tfhud";
    public override UiLayerKind Layer => UiLayerKind.Hud;

    // The core HUD's vitals, ammo and chat: this screen's cross, ammo row
    // and chat corner replace them.
    private readonly string[] _takes = ["hud.vitals", "hud.ammo", "chat"];
    public override IReadOnlyList<string> Takes => _takes;

    public override void Describe(ref UiFrame ui)
    {
        // The corners ride over each other by intent (stock's panels overlap
        // at the small .res sizes), so each is .Layered(): the alignment rule
        // does not ask edges of differently-anchored corners to meet.
        //
        // Bottom-left, where stock's CTFHudPlayerStatus puts the class art
        // and the cross (HudPlayerStatus, HudLayout.res). The canvas is the
        // .res union of HudPlayerClass (f0 x 480) and HudPlayerHealth
        // (0, r120, 250x120): the whole screen, and the art inside it sits
        // at its stock coordinates (the panel's AtRes children). The
        // corners ride over each other by intent, so the canvas is .Layered().
        ui.Sub("player status", health).At(UiAnchor.BottomLeft)
            .Size(Size.Percent(100), Size.Percent(100)).Layered().Key("player corner");

        // Stock hangs the item-effect meters over the ammo corner. The
        // invis watch rides the spy's override (huditemeffectmeter_spy.res:
        // xpos r162 ypos r92, 100x50 — hudlayout.res:57-72's base row r52 is
        // the sandman's); the medic rides HudMedicCharge (hudlayout.res:74-87:
        // r138 r69, 200x100) whose visible Background is 130x65 at the panel's
        // origin (hudmediccharge.res) — the panel's far side hangs 62 units
        // past the screen, the background is what shows. The medic keeps its
        // plate until its stock art lands, as the player status's layers do;
        // the cloak's art is its own background, a bare host.
        if (medic.Present)
            using (ui.Box().AtRes(UiAnchor.BottomRight, StockRes.Spot(HealthPanel.MedicBg, HealthPanel.MedicPanel, ui.Screen).Right,
                                      StockRes.Spot(HealthPanel.MedicBg, HealthPanel.MedicPanel, ui.Screen).Bottom).Layered()
                     .Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.M).Key("medic corner"))
                ui.Sub("medic charge", medic);

        // The cloak meter is a bare host: huditemeffectmeter_spy.res's own
        // art is the background, the panel r162 r92 (the base row's r52 is
        // the sandman's) — the spy res's override.
        if (cloak.Present)
        {
            var cloakAt = StockRes.Spot(CloakPanel.Panel, ui.Screen);
            using (ui.Box().AtRes(UiAnchor.BottomRight, cloakAt.Right, cloakAt.Bottom)
                               .Size(Size.Du(cloakAt.Wide), Size.Du(cloakAt.Tall)).Layered().Key("cloak corner"))
                ui.Sub("cloak", cloak);
        }

        // Bottom-left above the corners, stock's HudChat itself
        // (hudlayout.res:980-992): the feed's box 10 in, its bottom 85 up —
        // read bottom-referenced in the panel, as the disguise's is.
        if (chat.Present)
            ui.Sub("chat corner", chat).Size(Size.Percent(100), Size.Percent(100)).Layered();

        // Bottom-centre, stock's CTFHudFlagObjectives: the flag row is a
        // full-screen canvas (ObjectiveStatusFlagPanel, wide f0 tall 480 of
        // hudobjectiveflagpanel.res), so the panel's children sit at its
        // c-/r- coordinates (tf_hud_flagstatus.cpp).
        if (flags.Present)
            ui.Sub("flag corner", flags).Size(Size.Percent(100), Size.Percent(100)).Layered();

        // Bottom-right, stock's CTFHudWeaponAmmo corner: HudWeaponAmmo at
        // xpos r95 ypos r55, 94x45 (hudlayout.res:14-27) — an "r" xpos
        // reaches the element's LEFT edge 95 units from the right, so the
        // RIGHT edge is 1 unit in (2.25 du), and r55 tall 45 puts the
        // bottom 10 units up (22.5 du). The corner exists only when a
        // weapon has a readout (ShouldDraw, tf_hud_ammostatus.cpp:120-163):
        // an empty box would draw a stray panel.
        var ammoAt = StockRes.Spot(new StockBox("r95", "r55", 94, 45), ui.Screen);
        if (player.ShowsAmmo)
            ui.Sub("ammo corner", ammo).AtRes(UiAnchor.BottomRight, ammoAt.Right, ammoAt.Bottom)
                .Size(Size.Du(ammoAt.Wide), Size.Du(ammoAt.Tall));

        // Top-centre, under the clock: stock's CTFTeamStatus (hudmatchstatus
        // TeamStatus, z2) — the match backdrop, the round counter's centre
        // art (z1) and the two teams' player chips. Full-screen canvas, the
        // chips at their c- bases; drawn before the clock so the value
        // (TimePanelValue z3) keeps the art under it.
        if (teams.Visible)
            ui.Sub("team status", teams).Size(Size.Percent(100), Size.Percent(100)).Layered();

        // Top-centre, stock's CTFHudTimeStatus: HudMatchStatus is full-screen
        // (hudlayout.res:1491-1501) and its ObjectiveStatusTimePanel child is
        // xpos c-55 ypos 0 [$WIN32], 110x150 (hudmatchstatus.res:276-292) —
        // centred, its top at the screen's. The match HUD strips the panel's
        // own art (RoundTimerPanel), so the corner is a bare host.
        if (timer.Present)
        {
            var at = StockRes.Spot(RoundTimerPanel.Panel, ui.Screen);
            using (ui.Box().AtRes(UiAnchor.Top, at.CentreX, at.Top)
                           .Size(Size.Du(at.Wide), Size.Du(at.Tall)).Layered().Key("clock corner"))
                ui.Sub("clock", clock);
        }

        // Bottom-centre, stock's objective row (the game-type gating of
        // tf_hud_objectivestatus.cpp:259-279). The escort's
        // ObjectiveStatusEscort is xpos c-200 ypos r150, 400x150
        // (objectivestatusescort.res:3-22) — centred, its bottom 150 units
        // above the screen's; stock draws the res at XRES, so the row keeps
        // the screen's proportion (the du scale with the design width, as
        // the escort panel's own du do). The point icons draw their widest
        // line centred with the icons' bottoms separator_height 7 units
        // above the screen's bottom (height_offset 0, hudlayout.res:838-850,
        // hud_controlpointicons.cpp:1114-1155). Each row exists only while
        // the map has its objective; each carries its own art (the res's
        // cart_track and cart images, the map's obj_icons materials), so
        // both are bare hosts, no surface ring.
        if (escort.Present)
        {
            float k = ui.DesignWidth / 1920f;
            var at = StockRes.Spot(EscortPanel.Root, ui.Screen);
            using (ui.Box().AtRes(UiAnchor.Bottom, at.CentreX * k, at.Bottom * k)
                           .Size(Size.Du(at.Wide * k), Size.Du(at.Tall * k)).Layered().Key("escort corner"))
                ui.Sub("escort", escort);
        }
        else if (icons.Present)
            using (ui.Box().AtRes(UiAnchor.Bottom, 0f, ControlPointIconsPanel.BottomGapDu)
                           .Layered().Key("cp corner"))
                ui.Sub("cp icons", icons);

        // Bottom-left, stock's DisguiseStatus: xpos 10 ypos r70, 500x200
        // (hudlayout.res:241-253) — the status's top rides 70 units above
        // the screen's bottom, so the plate hangs from that line whatever
        // the canvas's height (an "r" ypos is bottom-referenced).
        if (disguise.Present)
        {
            var line = new StockBox("10", "r70", 500, 200).Rect(StockRect.Screen);
            using (ui.Box().AtRes(UiAnchor.TopLeft, line.Left * StockRes.Scale,
                                   ui.DesignHeight - (line.Bottom + line.Tall) * StockRes.Scale)
                           .Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M).Pad(Space.S, Space.M)
                           .Layered().Key("disguise corner"))
                ui.Sub("disguise", disguise);
        }
    }
}

/// <summary>
/// The player status: stock's class art and health cross at their .res
/// coordinates (hudplayerclass.res and hudplayerhealth.res inside
/// HudPlayerStatus of HudLayout.res), the 640×480 .res space × 2.25
/// (1080/480) into du. The cross follows
/// <c>CTFHudPlayerHealth::SetHealth</c> (tf_hud_playerstatus.cpp:736-865):
/// the fg is cropped from the bottom up to the life fraction
/// (CTFHealthPanel::Paint, :615-628); the bonus image grows
/// <c>HealthBonusPosAdj</c> 35 px each way about the cross's centre by the
/// overheal fraction <c>(health − max)/(maxbuffed − max)</c> clamped to 1
/// (:789-798), or, dying, by the distance under <c>HealthDeathWarning</c>
/// 0.49 of max in the warning colour (:802-831); the image always draws
/// while a state holds it and stock pulses its alpha instead
/// (hudanimations_tf.txt: HudHealthBonusPulse, HudHealthDyingPulse); the
/// max number shows only from 5 missing on
/// (:851-858); at zero health only <c>hud/health_dead</c> paints, full
/// white, no numbers and no bonus (:748-759, :589-634, :860-864). The
/// class image is stock's per-team table (tf_hud_playerstatus.cpp:47-75)
/// over its team bg (hudplayerclass.res teambg_2/3); no hero yet, nothing
/// paints. Each art layer keeps a Tone.Surface plate at the same
/// coordinates: the contrast rule's backdrop when the texture library has
/// the material, the fallback when it misses it.
/// </summary>
public sealed class HealthPanel(ITFLocalPlayer player, IUiTextureLibrary textures) : UiPanel
{
    // Stock's art boxes, as the .res writes them; resolved to du per Describe.
    private static class Stock
    {
        // HudPlayerHealth: xpos 0, ypos r120, 250x120 (hudplayerhealth.res:2-22)
        // — the health children's parent panel, spanning the screen's bottom
        // 120 units. Its "r" ypos reaches the panel's TOP edge.
        public static readonly StockRect Panel = new StockBox("0", "r120", 250, 120).Rect(StockRect.Screen);

        // PlayerStatusClassImageBG: xpos 9, ypos r60, 100x50 (hudplayerclass.res:79-100).
        public static readonly StockBox ClassBg = new("9", "r60", 100, 50);

        // classmodelpanelBG: xpos -1, ypos r60, 109x50, the clipped plate over
        // the model area, drawn after the first so the two stack (hudplayerclass.res:102-123).
        public static readonly StockBox ClassBgClipped = new("-1", "r60", 109, 50);

        // PlayerStatusClassImage: xpos 25, ypos r88, 75x75 (hudplayerclass.res:17-36).
        public static readonly StockBox Portrait = new("25", "r88", 75, 75);

        // PlayerStatusHealthImageBG: xpos 73, ypos 33, 55x55 (hudplayerhealth.res:37-51).
        public static readonly StockBox CrossBg = new("73", "33", 55, 55);

        // PlayerStatusHealthImage: xpos 75, ypos 35, 51x51 (hudplayerhealth.res:23-36).
        public static readonly StockBox Cross = new("75", "35", 51, 51);

        // PlayerStatusHealthValue: xpos 76, ypos 52, 50x18 (hudplayerhealth.res:74-91);
        // PlayerStatusMaxHealthValue: ypos 20 (:92-109).
        public static readonly StockBox Number = new("76", "52", 50, 18);
        public static readonly StockBox Max = new("76", "20", 50, 18);

        // HealthBonusPosAdj 35 (hudplayerhealth.res:19).
        public const float BonusAdj = 35f;
    }

    // The item-effect meters' stock boxes (TFHud.Describe's corners),
    // resolved at the frame's screen: HudMedicCharge's panel is r138 r69
    // 200x100 (hudlayout.res:74-87) and its visible Background is 130x65
    // at the panel's origin (hudmediccharge.res).
    internal static readonly StockBox MedicPanel = new("r138", "r69", 200, 100);

    internal static readonly StockBox MedicBg = new("0", "0", 130, 65);

    // The health labels' inks: TanDark (clientscheme.res:99) and the dying
    // warning HUDDeathWarning (:68).
    private static readonly UiColor TanDark = new(117, 107, 94, 255);
    private static readonly UiColor DeathWarning = new(255, 0, 0, 255);

    public override void Describe(ref UiFrame ui)
    {
        bool hasClass = player.ClassName.Length > 0;
        if (!hasClass && player.Health <= 0)
            return;                                   // no hero yet: stock's cross has nothing to paint

        float max = player.MaxHealth;
        bool dead = player.Health <= 0;
        bool dying = !dead && max >= 5 && player.Health < max * 0.49f;

        var classBg = Stock.ClassBg.In(StockRect.Screen);
        var portrait = Stock.Portrait.In(StockRect.Screen);
        var crossBg = Stock.CrossBg.In(Stock.Panel);
        var cross = Stock.Cross.In(Stock.Panel);
        var number = Stock.Number.In(Stock.Panel);
        var maxNumber = Stock.Max.In(Stock.Panel);

        if (hasClass)
        {
            if (StockArt.Missing(textures, ClassBg(player.Team)))
                ui.Rect(Tone.Surface, Size.Du(classBg.Wide), Size.Du(classBg.Tall))
                    .AtRes(UiAnchor.BottomLeft, classBg.Left, classBg.Bottom).Key("class plate");
            Art(ref ui, "class bg", ClassBg(player.Team), classBg.Left, classBg.Bottom, classBg.Wide, classBg.Tall);
            var classBgClipped = Stock.ClassBgClipped.In(StockRect.Screen);
            Art(ref ui, "class bg clipped", ClassBgClipped(player.Team), classBgClipped.Left, classBgClipped.Bottom,
                classBgClipped.Wide, classBgClipped.Tall);
            Art(ref ui, "portrait", Portrait(player.ClassName, player.Team), portrait.Left, portrait.Bottom,
                portrait.Wide, portrait.Tall);
        }

        if (dead)
        {
            // Dead: the dead material, full, and nothing else (:748-759).
            Art(ref ui, "dead cross", "hud/health_dead", cross.Left, cross.Bottom, cross.Wide, cross.Tall);
            return;
        }

        // The backdrop plates: the cross's, for the health number's contrast,
        // and the max number's chip, which stock's label hangs above the
        // cross (zpos 6 over the cross's 3).
        if (StockArt.Missing(textures, "hud/health_bg"))
            ui.Rect(Tone.Surface, Size.Du(crossBg.Wide), Size.Du(crossBg.Tall))
                .AtRes(UiAnchor.BottomLeft, crossBg.Left, crossBg.Bottom).Layered().Key("cross plate");
        bool showMax = max - player.Health >= 5;
        if (showMax)
            ui.Rect(Tone.Window, Size.Du(number.Wide), Size.Du(number.Tall))
                .AtRes(UiAnchor.BottomLeft, number.Left, maxNumber.Bottom).Layered().Key("max chip");

        // The bonus image, grown about the cross's centre by its percent
        // (:792-798, :825-831): the overheal fraction of the buff, or the
        // distance under the death warning in the warning colour, blinking
        // at the wheel's interval (:1032-1038).
        float centreX = crossBg.Left + crossBg.Wide / 2;
        float centreY = crossBg.Bottom + crossBg.Tall / 2;
        float percent = -1f;
        UiPaint tint = default;
        if (player.Health > max && player.MaxBuffedHealth > max)
            percent = System.Math.Min((player.Health - max) / (player.MaxBuffedHealth - max), 1f);
        else if (dying)
        {
            float warn = max * 0.49f;
            percent = (warn - player.Health) / warn;
            tint = Tone.Danger;
        }

        // The bonus image draws whenever the state holds it; stock pulses
        // its alpha (hudanimations_tf.txt): HudHealthBonusPulse on a 0.4 s
        // loop while overhealed, HudHealthDyingPulse on a 0.25 s one while
        // dying, tinted the warning colour there (:819).
        if (percent >= 0f)
        {
            float size = crossBg.Wide + 2f * Stock.BonusAdj * StockRes.Scale * percent;
            Art(ref ui, "bonus", "hud/health_over_bg", centreX - size / 2, centreY - size / 2, size, size,
                tint, default, dying ? DyingPulse(ui.Now) : BonusPulse(ui.Now));
        }

        // The cross: its bg, then the fg cropped from the bottom up to the
        // life fraction (CTFHealthPanel::Paint, :615-628).
        Art(ref ui, "cross bg", "hud/health_bg", crossBg.Left, crossBg.Bottom, crossBg.Wide, crossBg.Tall);
        float life = max >= 1 ? System.Math.Clamp(player.Health / max, 0f, 1f) : 1f;
        // The dest slice and its uv crop in proportion, bottom held (:615-628):
        // stretching the uv slice over the whole box squashes the cross when
        // hurt, where stock keeps the art's aspect.
        Art(ref ui, "cross", "hud/health_color", cross.Left, cross.Bottom, cross.Wide, cross.Tall * life,
            default, new UiRect(0, 1f - life, 1f, life));

        // The numbers, centred in stock's label boxes: the max one only from
        // 5 missing on (:851-858), and the health one in the warning colour
        // when dying (:834-837 recolours the image, and the label with it).
        // Both labels' ink is the res's fgcolor TanDark
        // (hudplayerhealth.res:90, :108) — dark on the cream cross — the
        // warning HUDDeathWarning (clientscheme.res:68).
        ui.Number(player.Health, Text.Body).HudSize()
            .Color(UiPaint.FromGame(dying ? DeathWarning : TanDark))
            .Width(Size.Du(number.Wide)).Height(Size.Du(number.Tall)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.BottomLeft, number.Left, number.Bottom).Layered().Key("health number");
        if (showMax)
            ui.Number((int)max, Text.Label).HudSize().Color(UiPaint.FromGame(TanDark))
                .Width(Size.Du(number.Wide)).Height(Size.Du(number.Tall)).TextAlign(Align.Centre)
                .AtRes(UiAnchor.BottomLeft, number.Left, maxNumber.Bottom).Layered().Key("max number");
    }

    /// <summary>
    /// The named material's texture at its .res coordinate (through the
    /// panels' shared <see cref="StockArt"/>); a miss (handle 0) draws
    /// nothing — the plate at the same coordinates is the fallback.
    /// </summary>
    private void Art(ref UiFrame ui, string key, string name, float left, float bottom, float wide, float tall,
                     UiPaint tint = default, UiRect uv = default, float opacity = 1f)
        => StockArt.Draw(ref ui, textures, key, name, left, bottom, wide, tall, tint, uv, opacity);

    /// <summary>HudHealthBonusPulse (hudanimations_tf.txt): alpha up over
    /// the 0.4 s loop's first half, down over the rest.</summary>
    private static float BonusPulse(double now)
    {
        float p = (float)(now % 0.4);
        return p < 0.2f ? p / 0.2f : 1f - (p - 0.2f) / 0.2f;
    }

    /// <summary>HudHealthDyingPulse: the sharper 0.25 s cycle — up by
    /// 0.075, held to 0.125, down by 0.2, dark to the loop's end.</summary>
    private static float DyingPulse(double now) => StockArt.Pulse025(now);

    /// <summary>The team bg (hudplayerclass.res teambg_2/3: the raw TF
    /// team numbers, 2 RED, 3 BLU).</summary>
    private static string ClassBg(int team) => team == 3 ? "hud/character_blue_bg" : "hud/character_red_bg";

    /// <summary>The clipped plate over the model area, teambg_2/3's second half
    /// (hudplayerclass.res:119,122).</summary>
    private static string ClassBgClipped(int team)
        => team == 3 ? "hud/character_blue_bg_clipped" : "hud/character_red_bg_clipped";

    /// <summary>Stock's class image table (tf_hud_playerstatus.cpp:47-75),
    /// the per-team pair behind teambg_2/3 — the scoreboard's ClassImage
    /// reads the same table (tf_clientscoreboard.cpp:2011).</summary>
    internal static string Portrait(string className, int team) => (className, team) switch
    {
        ("Scout", 3) => "hud/class_scoutblue",
        ("Sniper", 3) => "hud/class_sniperblue",
        ("Soldier", 3) => "hud/class_soldierblue",
        ("Demoman", 3) => "hud/class_demoblue",
        ("Medic", 3) => "hud/class_medicblue",
        ("Heavy", 3) => "hud/class_heavyblue",
        ("Pyro", 3) => "hud/class_pyroblue",
        ("Spy", 3) => "hud/class_spyblue",
        ("Engineer", 3) => "hud/class_engiblue",
        ("Scout", _) => "hud/class_scoutred",
        ("Sniper", _) => "hud/class_sniperred",
        ("Soldier", _) => "hud/class_soldierred",
        ("Demoman", _) => "hud/class_demored",
        ("Medic", _) => "hud/class_medicred",
        ("Heavy", _) => "hud/class_heavyred",
        ("Pyro", _) => "hud/class_pyrored",
        ("Spy", _) => "hud/class_spyred",
        ("Engineer", _) => "hud/class_engired",
        _ => "",
    };
}

/// <summary>
/// The HUD-art layer the panels share: the named material's texture at its
/// .res coordinate; a miss (handle 0) draws nothing — the plate at the
/// same coordinates is the fallback. The layer is allowed over its
/// siblings (the art overlaps by design) and keyed for the facts.
/// </summary>
internal static class StockArt
{
    /// <summary>Stock's 0.25 s alpha pulse, the shape HudHealthDyingPulse
    /// and HudLowAmmoPulse share (hudanimations_tf.txt:252-264 and the
    /// dying pulse beside them): up over the first 0.075, held to 0.125,
    /// down to 0 by 0.2, dark to the loop's end.</summary>
    public static float Pulse025(double now)
    {
        float p = (float)(now % 0.25);
        if (p < 0.075f)
            return p / 0.075f;
        if (p < 0.125f)
            return 1f;
        return p < 0.2f ? 1f - (p - 0.125f) / 0.075f : 0f;
    }

    /// <summary>Whether the named material misses the library: the gate that
    /// holds a backdrop plate to the artless case. The game sees the art
    /// alone — no dark box under a texture's transparent margins — and the
    /// artless preview keeps the plate its contrast rule reads.</summary>
    public static bool Missing(IUiTextureLibrary textures, string name) => textures.Find(name) == 0;

    /// <returns>Whether the material was found (a miss draws nothing — the
    /// caller's fallback stands).</returns>
    public static bool Draw(ref UiFrame ui, IUiTextureLibrary textures, string key, string name,
                            float left, float bottom, float wide, float tall,
                            UiPaint tint = default, UiRect uv = default, float opacity = 1f,
                            UiAnchor corner = UiAnchor.BottomLeft, int keyNumber = -1)
    {
        int handle = textures.Find(name);
        if (handle == 0)
            return false;
        return Draw(ref ui, handle, key, left, bottom, wide, tall, tint,
                    uv == default ? new UiRect(0, 0, 1f, 1f) : uv, opacity, corner, keyNumber);
    }

    /// <summary>An icon key's draw: stock resolves a CIconPanel's
    /// <c>"icon"</c> value through the icon tables
    /// (scripts/hud_textures.txt, then scripts/mod_textures.txt), not as a
    /// material path — the answer carries its own UV rectangle.</summary>
    /// <returns>Whether the icon was found (a miss draws nothing — the
    /// caller's fallback stands).</returns>
    public static bool DrawIcon(ref UiFrame ui, IUiTextureLibrary textures, string key, string name,
                                float left, float bottom, float wide, float tall,
                                UiPaint tint = default, float opacity = 1f,
                                UiAnchor corner = UiAnchor.BottomLeft, int keyNumber = -1)
        => textures.Icon(name) is { } icon
           && Draw(ref ui, icon.Handle, key, left, bottom, wide, tall, tint, icon.Uv, opacity, corner, keyNumber);

    private static bool Draw(ref UiFrame ui, int handle, string key,
                             float left, float bottom, float wide, float tall,
                             UiPaint tint, UiRect uv, float opacity,
                             UiAnchor corner, int keyNumber)
    {
        var box = ui.Texture(handle, Size.Du(wide), Size.Du(tall), uv)
            .AtRes(corner, left, bottom).Layered().Opacity(opacity);
        // A per-instance key (a row's own icon) numbers the element; a named
        // one names it.
        box = key.Length > 0 ? box.Key(key) : keyNumber >= 0 ? box.Key(keyNumber) : box;
        if (tint != default)
            box.Tint(tint);
        return true;
    }
}
