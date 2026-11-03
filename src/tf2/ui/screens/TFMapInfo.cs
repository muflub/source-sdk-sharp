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
/// The TF2 map info menu (stock's <c>CTFMapInfoMenu</c>,
/// game/client/tf/vgui/tf_mapinfomenu.cpp) at stock's own layout:
/// mapinfomenu.res's canvas through <see cref="StockRes"/> — the map's
/// display name at c-184 95 and its game type at c-184 125 (the
/// <c>%mapname%</c>/<c>%gamemode%</c> labels, :18-60), the briefing text
/// at c-184 160 195x200 (MapInfoText, :62-86), the menu photo at c10 75
/// 275x275 (MapImage, :88-109, drawn only when the library answers the
/// map's <c>vgui/maps/menu_photos_&lt;map&gt;</c>, as stock's
/// <c>LoadMapPage</c> hides it when the material misses, :349-377), the
/// CONTINUE at r190 r40 (MapInfoContinue, :111-136) and the BACK at 40
/// r40 (MapInfoBack, :163-186) over the loadout gradient's shaded bar
/// (0 r50 f0x50, :218-239). The BACK and the CONTINUE-vs-CLOSE label are
/// stock's arms (<c>CheckBackContinueButtons</c>, :230-250: no team yet
/// shows BACK and CONTINUE; on one, CLOSE and no BACK). A narrow screen
/// cannot hold the 640-unit page, so the title, type, photo and briefing
/// stack and the gates take a bottom row.
///
/// <para>
/// Stock's MapInfoWatchIntro (:138-161) rides the video system's intro
/// movies (<c>CheckForIntroMovie</c>, :153-169) — deferred with the
/// MenuBG model panel (docs/plans/tf2/deferred.tsv), so the intro arm
/// never draws in this port, exactly as stock's own gate hides it when
/// the map has no movie.
/// </para>
/// </summary>
public sealed class TFMapInfo : UiScreen
{
    private readonly ITFMapInfoSource _source;
    private readonly IUiTextureLibrary _textures;
    private readonly ITFMapInfoActions? _actions;

    /// <param name="textures">The art library the shaded bar and the map
    /// photo come from; a miss leaves the layout without them.</param>
    /// <param name="actions">What CONTINUE and BACK do; none in a
    /// sample.</param>
    public TFMapInfo(ITFMapInfoSource source, IUiTextureLibrary textures, ITFMapInfoActions? actions = null)
    {
        _source = source;
        _textures = textures;
        _actions = actions;
    }

    public override string Name => "tf2mapinfo";

    /// <summary>The panel stock's map info comes in under
    /// (<c>PANEL_MAPINFO</c>, tf_shareddefs.h:164).</summary>
    public override string? Panel => "mapinfo";

    public override void Describe(ref UiFrame ui)
    {
        // MenuBG (CModelPanel, models/vgui/UI_welcome01_screen.mdl,
        // :188-216) is a deferred 3D model; the window surface is the
        // backdrop under the layout until the model panels are ported
        // (docs/plans/tf2/deferred.tsv).
        ui.Rect(Tone.Window, Size.Percent(100), Size.Percent(100))
            .AtRes(UiAnchor.BottomLeft, 0, 0).Layered().Key("backdrop");

        ShadedBar(ref ui);

        if (ui.Narrow)
            DescribeStacked(ref ui);
        else
            DescribeStock(ref ui);
    }

    // ---- the stock canvas (wide screens) ----

    private void DescribeStock(ref UiFrame ui)
    {
        // The title and the game type (MapInfoTitle :18-38, MapInfoType
        // :40-60).
        var title = Stock.Title.In(Stock.Canvas);
        ui.Data(_source.MapName, Text.Title, Tone.Text)
            .Width(Size.Du(title.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, title.Left, title.Bottom).Layered().Key("map title");
        var type = Stock.Type.In(Stock.Canvas);
        ui.Data(_source.GameMode, Text.Label, Tone.TextMuted)
            .Width(Size.Du(type.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, type.Left, type.Bottom).Layered().Key("map type");

        // The briefing (MapInfoText :62-86): one flowing block the wrap
        // breaks at the box's width — stock's CExRichText, not a row per
        // line (a wrapped row's box stays one line tall, so stacked rows
        // would overlap).
        if (_source.Briefing.Length > 0)
        {
            var text = Stock.Text.In(Stock.Canvas);
            ui.Data(_source.Briefing, Text.Body, Tone.Text)
                .Width(Size.Du(text.Wide)).Wrap(text.Wide * StockRes.Scale)
                .AtRes(UiAnchor.BottomLeft, text.Left, text.Bottom).Layered().Key("briefing");
        }

        // The menu photo (MapImage :88-109), drawn only when the library
        // answers the name (stock's IsErrorMaterial arm, :354-377).
        string? photo = _source.Photo;
        if (photo is { } name)
        {
            int handle = _textures.Find(name);
            if (handle != 0)
            {
                var image = Stock.Image.In(Stock.Canvas);
                ui.Texture(handle, Size.Du(image.Wide), Size.Du(image.Tall))
                    .AtRes(UiAnchor.BottomLeft, image.Left, image.Bottom).Layered().Key("map photo");
            }
        }

        // BACK, only without a team (:230-240).
        if (_source.Team <= 1 && Gate(ref ui, Stock.Back, "#TF2_MapInfo_Back", "back") && _actions is { } acts)
            acts.Back();

        // CONTINUE — CLOSE once a team is chosen (:241-250).
        var label = _source.Team <= 1 ? "#TF2_MapInfo_Continue" : "#TF2_MapInfo_Close";
        if (Gate(ref ui, Stock.Continue, label, "continue") && _actions is { } acts2)
            acts2.Continue();
    }

    // ---- the stacked canvas (narrow screens) ----

    /// <summary>The 640-unit page cannot fit a portrait screen: the
    /// title, game type, photo and briefing stack in stock's order and
    /// the two gates take a bottom row (the shaded bar stays).</summary>
    private void DescribeStacked(ref UiFrame ui)
    {
        using (ui.Column(Space.M, Align.Start).Size(Size.Percent(100), Size.Percent(100))
                   .Pad(Space.L, Space.L))
        {
            ui.Data(_source.MapName, Text.Title, Tone.Text);
            if (_source.GameMode.Length > 0)
                ui.Data(_source.GameMode, Text.Label, Tone.TextMuted);

            // The photo at its stock width (MapImage's 275 du) or what
            // the screen holds, square like the stock box.
            string? photo = _source.Photo;
            if (photo is { } name)
            {
                int handle = _textures.Find(name);
                if (handle != 0)
                    ui.Texture(handle, Size.Fill,
                               Size.Du(MathF.Min(Stock.Image.Rect(Stock.Canvas).Wide * StockRes.Scale,
                                                 ui.DesignWidth - 64f)))
                        .Key("map photo");
            }

            if (_source.Briefing.Length > 0)
                ui.Data(_source.Briefing, Text.Body, Tone.Text)
                    .Wrap(ui.DesignWidth - 64f).Key("briefing");

            ui.Flex();

            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill).Height(Size.Du(48f)))
            {
                if (_source.Team <= 1 && ui.Button("back", "#TF2_MapInfo_Back").Clicked)
                    _actions?.Back();
                ui.Flex();
                if (ui.Button("continue", _source.Team <= 1 ? "#TF2_MapInfo_Continue" : "#TF2_MapInfo_Close").Clicked)
                    _actions?.Continue();
            }
        }
    }

    /// <summary>One of the two gates (stock's CExButtons): a plate with
    /// its label, the whole box the click target; true on the click's
    /// frame.</summary>
    private bool Gate(ref UiFrame ui, StockBox box, string token, string key)
    {
        var s = box.In(Stock.Canvas);
        var gate = ui.Box().Size(Size.Du(s.Wide), Size.Du(s.Tall))
                        .Fill(Tone.SurfaceRaised).Round(Radius.S)
                        .AtRes(UiAnchor.Bottom, s.CentreX, s.Bottom).Layered().Key(key);
        var hit = ui.Interact(gate);
        using (gate)
            ui.Text(token, Text.Body, Tone.Text)
                .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
        return hit.Clicked;
    }

    private void ShadedBar(ref UiFrame ui)
    {
        int shade = _textures.Find("vgui/loadout_bottom_gradient");
        if (shade != 0)
            ui.Texture(shade, Size.Percent(100), Size.Du(Stock.Shaded.Rect(Stock.Canvas).Tall * StockRes.Scale))
                .AtRes(UiAnchor.BottomLeft, 0, 0).Layered().Key("shaded bar");
    }

    /// <summary>The .res boxes on the 640x480 canvas mapinfomenu.res is
    /// written in (:3-16, 0,0 f0x480), resolved to du per Describe. A "c"
    /// xpos is the element's LEFT edge about the screen's centre, an "r"
    /// ypos its TOP edge above the bottom.</summary>
    internal static class Stock
    {
        public static readonly StockRect Canvas = StockRect.Screen;

        // The map name and game type: 225x24 at c-184 95 and c-184 125;
        // the briefing 195x200 at c-184 160; the photo 275x275 at c10 75.
        public static readonly StockBox Title = new("c-184", "95", 225, 24);
        public static readonly StockBox Type = new("c-184", "125", 225, 24);
        public static readonly StockBox Text = new("c-184", "160", 195, 200);
        public static readonly StockBox Image = new("c10", "75", 275, 275);

        // MapInfoContinue r190 r40 150x30 (:111-136); MapInfoBack 40 r40
        // 150x30 (:163-186); ShadedBar 0 r50 f0x50 (:218-239).
        public static readonly StockBox Continue = new("r190", "r40", 150, 30);
        public static readonly StockBox Back = new("40", "r40", 150, 30);
        public static readonly StockBox Shaded = new("0", "r50", 640, 50);
    }
}
