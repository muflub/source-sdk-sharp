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
/// The engineer's building status, stock's
/// <c>CHudBuildingStatusContainer_Engineer</c> (tf_hud_building_status.cpp)
/// in the stock layout: the <c>BuildingStatus_Engineer</c> panel of
/// hudlayout.res (xpos_hidef 32 ypos_hidef 16 over the 640x480 canvas) with
/// the per-kind items stacked from XRES(9)/YRES(9) with no gap
/// (RepositionObjectPanels :1465-1505 — "the fade around the panels gives
/// a gap"), the sentry's item 160x60 (hud_obj_sentrygun.res) and the
/// others 150x31 (hud_obj_dispenser.res, hud_obj_tele_*.res). Each item is
/// the stock switch: the not-built label while placing (NotBuiltPanel),
/// the construction label and progress bar while building
/// (BuildingPanel), the running rows when built (RunningPanel) — the
/// health bar clipping upward like <c>CBuildingHealthBar::Paint</c>
/// (:56-68, PROGRESS_NORTH; under half is the low colour), the level pip
/// (Icon_Upgrade_1..3, 46,4 8x8) and the alert tray on the right
/// (CBuildingStatusAlertTray: the sapper icon for a sapper
/// <c>GetBuildingAlertLevel</c> c_baseobject.cpp:1054-1082, the wrench for
/// the low-health and the sentry's low-shell arms, tray hidden otherwise).
/// The icons are the hud_obj_*.res "icon" names through
/// scripts/mod_textures.txt's files. The per-object rows read the carrier's
/// own columns: the sentry's kill/assist label (KillIcon 0,12 / KillsLabel
/// 12,13) and its third row — the level 3 rockets, else the upgrade metal,
/// stock's mutually exclusive pair at 38/39 (hud_obj_sentrygun.res:360-426)
/// — the dispenser's and teleporters' upgrade meters (16/17 of
/// hud_obj_dispenser.res, hud_obj_tele_*.res) and the entrance's
/// recharge meter against fully-charged times-used (ChargingPanel
/// hud_obj_tele_entrance.res:285-301, FullyChargedPanel :314-331). Not
/// drawn: stock's disposable-sentry arm.
/// </summary>
public sealed class TFBuildingStatus(ITFBuildingStatus buildings, IUiTextureLibrary textures,
                                     IUiLocalisation localise) : UiScreen
{
    private readonly IUiLocalisation _localise = localise;

    public override string Name => "tfbuildingstatus";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    // The stock numbers, in the 640x480 .res space; resolved to du per Describe.
    private static class Stock
    {
        // The container's own spot: hidef 32,16 (hudlayout.res:312-342);
        // the items stack from 9,9 inside it.
        public const float OriginX = 32f;
        public const float OriginY = 16f;
        public const float Margin = 9f;

        // Item frames: the sentry's 160x60 (hud_obj_sentrygun.res:4-21),
        // the others 150x31 (hud_obj_dispenser.res:4-21).
        public const float SentryWide = 160f, SentryTall = 60f;
        public const float ShortWide = 150f, ShortTall = 31f;
    }

    // The res's own image names through mod_textures.txt's "file" entries.
    private const string TallBgBlue = "hud/eng_status_area_sentry_blue";
    private const string TallBgRed = "hud/eng_status_area_sentry_red";
    private const string TallBgOff = "hud/eng_status_area_sentry_disabled";
    private const string ShortBgBlue = "hud/eng_status_area_tele_blue";
    private const string ShortBgRed = "hud/eng_status_area_tele_red";
    private const string ShortBgOff = "hud/eng_status_area_tele_disabled";
    private const string SentryTray = "hud/eng_status_area_sentry_alrt";
    private const string ShortTray = "hud/eng_status_area_tele_alrt";
    private const string Wrench = "hud/eng_status_alert_ico_wrench";
    private const string Sapper = "hud/hud_obj_status_sapper";
    private const string Shell = "hud/hud_obj_status_ammo_64";
    private const string Teleport = "hud/hud_obj_status_teleport_64";
    private const string Kill = "hud/hud_obj_status_kill_64";
    private const string Rocket = "hud/hud_obj_status_rockets_64";
    private const string Metal = "hud/ico_metal";

    // TF_TEAM_RED/BLUE (tf_shareddefs.h), the background selectors.
    private const int Red = 2;
    private const int Blue = 3;

    // DISPENSER_MAX_METAL_AMMO (tf_obj.cpp); the dispenser's metal bar's pool.
    private const float DispenserMaxMetal = 400f;

    public override void Describe(ref UiFrame ui)
    {
        int count = buildings.Count;
        if (count == 0)
            return;

        // The host: the items' stacked extent (the widest item's 160 units
        // over them all), anchored top-left at the container's hidef spot.
        // Top-anchored: the canvas's top is pinned to the design's, and the
        // du ride the design's height when it is denser than the canvas's
        // own (a ui-scaled design is the same picture smaller); a larger
        // design keeps the canvas's du.
        float k = MathF.Min(1f, ui.DesignHeight / (480f * StockRes.Scale));
        float u = StockRes.Scale * k;
        float stack = Stock.Margin;
        bool sentry = false;
        for (int i = 0; i < count; i++)
        {
            bool tall = buildings.Building(i).Kind == TFBuildingLineKind.Sentry;
            sentry |= tall;
            stack += tall ? Stock.SentryTall : Stock.ShortTall;
        }

        using (ui.Box().AtRes(UiAnchor.TopLeft, Stock.OriginX * u, Stock.OriginY * u)
                       .Size(Size.Du((sentry ? Stock.SentryWide : Stock.ShortWide) * u),
                             Size.Du(stack * u)).Key("building status"))
        {
            float top = Stock.Margin;
            for (int i = 0; i < count; i++)
            {
                var line = buildings.Building(i);
                bool isSentry = line.Kind == TFBuildingLineKind.Sentry;
                float tall = isSentry ? Stock.SentryTall : Stock.ShortTall;

                float wide = isSentry ? Stock.SentryWide : Stock.ShortWide;
                using (ui.Box().AtRes(UiAnchor.TopLeft, Stock.Margin * u, top * u)
                               .Size(Size.Du(wide * u), Size.Du(tall * u)).Key(i))
                {
                    Item(ref ui, textures, line, wide, tall, u);
                }

                top += tall;   // no gap: the fade around the panels gives one
            }
        }
    }

    /// <summary>One item's inside, the .res children against the item's own
    /// frame (<paramref name="tall"/>, the frame's height in units).</summary>
    private void Item(ref UiFrame ui, IUiTextureLibrary textures, in TFBuildingStatusLine line,
                             float wide, float tall, float u)
    {
        bool sentry = line.Kind == TFBuildingLineKind.Sentry;

        // The background, by the building's team (GetBackgroundImage's team
        // arms, tf_hud_building_status.cpp:281-313, :755-787) — the
        // disabled art while the building is not built.
        string bg = line.Placing
            ? (sentry ? TallBgOff : ShortBgOff)
            : (sentry ? (line.Team == Blue ? TallBgBlue : TallBgRed) : (line.Team == Blue ? ShortBgBlue : ShortBgRed));
        if (!Art(ref ui, textures, bg, "building bg", 0f, 0f, 120f, tall, tall, u))
            using (ui.Box().Size(Size.Du(120f * u), Size.Du(tall * u)).Fill(Tone.Surface)
                           .AtRes(UiAnchor.BottomLeft, 0f, 0f).Layered().Name("building bg fallback"))
            {
            }

        // The kind's icon: the sentry's per level (Icon_Sentry_1..3, 22,12
        // 36x36), the others' single (24,1 28x28); the level pip right of
        // the bar (Icon_Upgrade_1..3, 46,4 8x8).
        string icon = line.Kind switch
        {
            TFBuildingLineKind.Sentry => line.Level switch
            {
                2 => "hud/hud_obj_status_sentry_2",
                >= 3 => "hud/hud_obj_status_sentry_3",
                _ => "hud/hud_obj_status_sentry_1",
            },
            TFBuildingLineKind.Dispenser => "hud/hud_obj_status_dispenser",
            TFBuildingLineKind.TeleporterEntrance => "hud/hud_obj_status_tele_entrance",
            _ => "hud/hud_obj_status_tele_exit",
        };
        if (sentry)
        {
            string pip = line.Level switch
            {
                2 => "hud/hud_upgrade_2",
                >= 3 => "hud/hud_upgrade_3",
                _ => "hud/hud_upgrade_1",
            };
            if (!Art(ref ui, textures, pip, "level pip", 46f, 4f, 8f, 8f, tall, u))
                Plate(ref ui, 46f, 4f, 8f, tall, u, Tone.Accent, "level pip");
        }

        if (line.Placing)
        {
            // NotBuiltPanel: the not-built label at x60, wrapped inside the
            // 120-wide plate (NotBuiltLabel, hud_obj_*.res — the label rides
            // the item's full height and never draws past the plate).
            ui.Text(NotBuilt(line.Kind), Text.Caption, Tone.Text)
                .Width(Size.Du(60f * u)).Height(Size.Du(tall * u)).TextAlign(Align.Start)
                .Elide().Wrap(60f * u)
                .AtRes(UiAnchor.BottomLeft, 60f * u, 0f).Layered().Name("not built");
            return;
        }

        // The health bar (13,3 8x53 sentry / 8x24 others), filling upward
        // (PROGRESS_NORTH) and clipped like CBuildingHealthBar::Paint
        // (:56-68): under half is the low colour.
        float barTall = (sentry ? 53f : 24f) * u;
        float fraction = line.MaxHealth > 0 ? Math.Clamp(line.Health / (float)line.MaxHealth, 0f, 1f) : 0f;
        var healthTone = fraction < 0.5f ? Tone.Danger : Tone.Positive;
        _ = ui.Rect(Tone.SurfaceRaised, Size.Du(8f * u), Size.Du(barTall))
                .AtRes(UiAnchor.BottomLeft, 13f * u, 3f * u).Layered().Name("health track");
        if (fraction > 0f)
            _ = ui.Rect(healthTone, Size.Du(8f * u), Size.Du(barTall * fraction))
                    .AtRes(UiAnchor.BottomLeft, 13f * u, 3f * u).Layered().Name("health fill");

        if (line.Building)
        {
            // BuildingPanel: the construction label and the progress bar
            // (0,18/0,5 and 0,29/0,16 50x8 in the x60 panel).
            float labelTop = sentry ? 18f : 5f;
            float barTop = sentry ? 29f : 16f;
            ui.Text("#TF2_Building_Building", Text.Caption, Tone.Text)
                .Width(Size.Du(88f * u)).Height(Size.Du(12f * u)).TextAlign(Align.Start)
                .Elide().MaxWidth(88f)
                .AtRes(UiAnchor.BottomLeft, 60f * u, (tall - labelTop - 12f) * u).Layered().Name("building");
            _ = ui.Meter(line.BuildProgress / 100f, Tone.Accent, Size.Du(50f * u), 8f * u)
                    .AtRes(UiAnchor.BottomLeft, 60f * u, (tall - barTop - 8f) * u).Name("progress");
            return;
        }

        // RunningPanel: the rows the carrier carries. The sentry's shells
        // (12,26 38x8) and the dispenser's metal (12,6 38x8); the kill
        // counts, the upgrade meters and the teleporter's recharge read the
        // carrier's own columns.
        switch (line.Kind)
        {
            case TFBuildingLineKind.Sentry:
            {
                float shells = line.MaxShells > 0 ? Math.Clamp(line.Shells / (float)line.MaxShells, 0f, 1f) : 0f;
                if (!Art(ref ui, textures, Shell, "shell icon", 60f, 25f, 10f, 10f, tall, u))
                    Plate(ref ui, 60f, 25f, 10f, tall, u, Tone.SurfaceRaised, "shell plate");
                _ = ui.Meter(shells, shells < 0.25f ? Tone.Danger : Tone.Text, Size.Du(38f * u), 8f * u)
                        .AtRes(UiAnchor.BottomLeft, 72f * u, (tall - 26f - 8f) * u).Name("shells");

                // The score row: the kill and assist counts of KillsLabel
                // (0,12 icon, 12,13 label; #Building_hud_sentry_kills_assists).
                if (!Art(ref ui, textures, Kill, "kill icon", 60f, 12f, 10f, 10f, tall, u))
                    Plate(ref ui, 60f, 12f, 10f, tall, u, Tone.SurfaceRaised, "kill plate");
                ui.Text($"{Localised("#TF2_Building_Kills")} {line.Kills}  {Localised("#TF2_Building_Assists")} {line.Assists}",
                        Text.Caption, Tone.Text)
                    .Width(Size.Du(88f * u)).Height(Size.Du(10f * u)).TextAlign(Align.Start)
                    .Elide()
                    .AtRes(UiAnchor.BottomLeft, 72f * u, (tall - 13f - 10f) * u).Layered().Name("kills");

                // The third row, stock's mutually exclusive pair at 38/39: the
                // level 3's rockets, else the upgrade metal.
                if (line.Level >= 3)
                {
                    if (!Art(ref ui, textures, Rocket, "rocket icon", 60f, 38f, 10f, 10f, tall, u))
                        Plate(ref ui, 60f, 38f, 10f, tall, u, Tone.SurfaceRaised, "rocket plate");
                    float rockets = line.MaxRockets > 0 ? Math.Clamp(line.Rockets / (float)line.MaxRockets, 0f, 1f) : 0f;
                    _ = ui.Meter(rockets, Tone.Text, Size.Du(38f * u), 8f * u)
                            .AtRes(UiAnchor.BottomLeft, 72f * u, (tall - 39f - 8f) * u).Name("rockets");
                }
                else
                    UpgradeRow(ref ui, textures, line.BuildProgress / 100f, 38f, 39f, tall, u);
                break;
            }

            case TFBuildingLineKind.Dispenser:
            {
                if (!Art(ref ui, textures, Shell, "metal icon", 60f, 5f, 10f, 10f, tall, u))
                    Plate(ref ui, 60f, 5f, 10f, tall, u, Tone.SurfaceRaised, "metal plate");
                float metal = Math.Clamp(line.Ammo / DispenserMaxMetal, 0f, 1f);
                _ = ui.Meter(metal, Tone.Text, Size.Du(38f * u), 8f * u)
                        .AtRes(UiAnchor.BottomLeft, 72f * u, (tall - 6f - 8f) * u).Name("metal");
                UpgradeRow(ref ui, textures, line.BuildProgress / 100f, 16f, 17f, tall, u);
                break;
            }

            case TFBuildingLineKind.TeleporterEntrance:
            {
                // The teleport_64 icon (0,5), then the charge state's row
                // (ChargingPanel's Recharge 12,6 against FullyChargedPanel's
                // TimesUsedLabel 12,5) and the upgrade meter.
                if (!Art(ref ui, textures, Teleport, "teleport icon", 60f, 5f, 10f, 10f, tall, u))
                    Plate(ref ui, 60f, 5f, 10f, tall, u, Tone.SurfaceRaised, "teleport plate");
                if (line.Recharge >= 0)
                    _ = ui.Meter(line.Recharge / 100f, Tone.Accent, Size.Du(38f * u), 8f * u)
                            .AtRes(UiAnchor.BottomLeft, 72f * u, (tall - 6f - 8f) * u).Name("recharge");
                else
                    ui.Text($"{line.TimesUsed}", Text.Caption, Tone.Text)
                        .Width(Size.Du(38f * u)).Height(Size.Du(10f * u)).TextAlign(Align.Start)
                        .AtRes(UiAnchor.BottomLeft, 72f * u, (tall - 5f - 10f) * u).Layered().Name("times used");
                if (line.Level < 3)
                    UpgradeRow(ref ui, textures, line.BuildProgress / 100f, 16f, 17f, tall, u);
                break;
            }

            default:
            {
                if (!Art(ref ui, textures, Teleport, "teleport icon", 60f, 5f, 10f, 10f, tall, u))
                    Plate(ref ui, 60f, 5f, 10f, tall, u, Tone.SurfaceRaised, "teleport plate");
                if (line.Level < 3)
                    UpgradeRow(ref ui, textures, line.BuildProgress / 100f, 16f, 17f, tall, u);
                break;
            }
        }

        // The kind icon rides over the rows, at its .res spot.
        float iconWide = sentry ? 36f : 28f;
        float iconTop = sentry ? 12f : 1f;
        if (!Art(ref ui, textures, icon, "kind icon", sentry ? 22f : 24f, iconTop, iconWide, iconWide, tall, u))
            Plate(ref ui, sentry ? 22f : 24f, iconTop, iconWide, tall, u, Tone.SurfaceRaised, "kind icon");

        // The alert tray (AlertTray 114,0 44x60 / 113,0 34x31): the sapper
        // icon for a sapper, else the wrench for the low-health and the
        // sentry's low-shell arms (GetBuildingAlertLevel,
        // c_baseobject.cpp:1054-1082, behind its !IsBuilding gate).
        float shellsFraction = sentry && line.MaxShells > 0 ? line.Shells / (float)line.MaxShells : 1f;
        bool wrench = !line.Sapped && !line.Building && (fraction < 0.66f || shellsFraction < 0.25f);
        if (line.Sapped || wrench)
        {
            if (!Art(ref ui, textures, sentry ? SentryTray : ShortTray, "alert tray",
                     sentry ? 114f : 113f, 0f, sentry ? 44f : 34f, tall, tall, u))
                using (ui.Box().Size(Size.Du((sentry ? 44f : 34f) * u), Size.Du(tall * u)).Fill(Tone.Surface)
                               .AtRes(UiAnchor.BottomLeft, (sentry ? 114f : 113f) * u, 0f)
                               .Layered().Name("alert tray fallback"))
                {
                }

            if (line.Sapped)
            {
                float sapperWide = sentry ? 35f : 25f;
                float sapperTop = sentry ? 12f : 3f;
                if (!Art(ref ui, textures, Sapper, "sapper icon", 117f, sapperTop, sapperWide, sapperWide, tall, u))
                    Plate(ref ui, 117f, sapperTop, sapperWide, tall, u, Tone.Danger, "sapper plate");
            }
            else
            {
                float wrenchWide = sentry ? 27f : 19f;
                float wrenchTop = sentry ? 18f : 5f;
                if (!Art(ref ui, textures, Wrench, "wrench icon", 121f, wrenchTop, wrenchWide, wrenchWide, tall, u))
                    Plate(ref ui, 121f, wrenchTop, wrenchWide, tall, u, Tone.Danger, "wrench plate");
            }
        }
    }

    /// <summary>The named material's texture at its .res spot inside the
    /// item (the panels' art layer, named — the item repeats); a miss draws
    /// nothing and the caller's plate stands.</summary>
    private static bool Art(ref UiFrame ui, IUiTextureLibrary textures, string name, string key,
                            float x, float top, float wide, float tall, float itemTall, float u)
    {
        int handle = textures.Find(name);
        if (handle == 0)
            return false;
        _ = ui.Texture(handle, Size.Du(wide * u), Size.Du(tall * u), new UiRect(0, 0, 1f, 1f))
                .AtRes(UiAnchor.BottomLeft, x * u, (itemTall - top - tall) * u).Layered().Name(key);
        return true;
    }

    /// <summary>A fallback plate where an icon's material missed.</summary>
    private static void Plate(ref UiFrame ui, float x, float top, float wide, float itemTall, float u,
                              UiPaint tone, string key)
    {
        using (ui.Box().Size(Size.Du(wide * u), Size.Du(wide * u)).Fill(tone).Round(Radius.S)
                       .AtRes(UiAnchor.BottomLeft, x * u, (itemTall - top - wide) * u).Layered().Name(key))
        {
        }
    }

    /// <summary>The upgrade row (ico_metal icon over the 38x8 meter): the
    /// built row's BuildProgress is already the upgrade metal's fraction,
    /// the carrier's overload. The sentry's row is the third at 38/39
    /// (hud_obj_sentrygun.res:394-426), the shorts' the second at 16/17
    /// (hud_obj_dispenser.res, hud_obj_tele_*.res).</summary>
    private static void UpgradeRow(ref UiFrame ui, IUiTextureLibrary textures, float fraction,
                                   float iconTop, float barTop, float tall, float u)
    {
        if (!Art(ref ui, textures, Metal, "upgrade icon", 60f, iconTop, 10f, 10f, tall, u))
            Plate(ref ui, 60f, iconTop, 10f, tall, u, Tone.SurfaceRaised, "upgrade plate");
        _ = ui.Meter(fraction, Tone.Accent, Size.Du(38f * u), 8f * u)
                .AtRes(UiAnchor.BottomLeft, 72f * u, (tall - barTop - 8f) * u).Name("upgrade");
    }

    /// <summary>The token's words, or the token itself when no table holds it.</summary>
    private string Localised(string token) => _localise.Find(token) ?? token;

    /// <summary>The not-built labels (the #Building_hud_*_not_built arms of
    /// hud_obj_*.res).</summary>
    private static string NotBuilt(TFBuildingLineKind kind) => kind switch
    {
        TFBuildingLineKind.Sentry => "#TF2_Building_NotBuilt_Sentry",
        TFBuildingLineKind.Dispenser => "#TF2_Building_NotBuilt_Dispenser",
        TFBuildingLineKind.TeleporterEntrance => "#TF2_Building_NotBuilt_Entrance",
        _ => "#TF2_Building_NotBuilt_Exit",
    };
}
