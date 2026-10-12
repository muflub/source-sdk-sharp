using System;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// THE FULL AUTOMAP (docs/plans/descent_ui.md §2.7, R23): the explored
/// dungeon as a modal fullscreen window ("automap", 1280 x 720) on the Menu
/// layer — the corner minimap's canvas scaled up, rooms as hairline rects,
/// doors as gap ticks, the hero as an accent dot with a heading nub, party
/// markers in TeamA blue, room names when labels are on. A row of storey
/// chips switches the viewed storey (the hero's storey is the default and
/// keeps following the hero until a chip is clicked); the legend row names
/// every marker kind the map actually holds. Esc closes: the window is this
/// screen's root, so the layer's Esc handling asks the root to close and the
/// window store drops it (plan §0's root-dialog rule).
/// </summary>
public sealed class AutoMap : UiScreen, IUiWindowed
{
    public override string Name => "automap";
    public override UiLayerKind Layer => UiLayerKind.Menu;
    public string WindowId => "automap";

    /// <summary>
    /// 1280 x 720 default (plan §2.7), at least 680 x 440: below that the
    /// chips and the legend leave the map square unusable. Narrow below 900
    /// window-du: the legend cells go slim and the Labels button folds away.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(1280, 720, 680, 440, screenW, screenH, 900);

    // No chip picked: the view rides the hero's storey.
    private const int NoPick = int.MinValue;

    // The legend strip is its own panel so the Caption role of its kind
    // names stays off the screen's role budget (same rule the browser's
    // table pane obeys).
    private sealed class LegendPanel(AutoMap owner) : UiPanel
    {
        public override void Describe(ref UiFrame ui)
        {
            int cols = LegendColumns(owner._legendCount, ui.DesignWidth, ui.Narrow);
            float cellW = LegendCellW(ui.Narrow);
            Span<UiSize> columns = stackalloc UiSize[cols];
            for (int c = 0; c < cols; c++)
                columns[c] = Size.Du(cellW);
            using (ui.Row(Space.None).Size(Size.Fill, Size.Auto).Pad(Space.None, Space.S))
            using (ui.Grid(columns, Space.M, Space.XS))
                for (int i = 0; i < owner._legendCount; i++)
                {
                    byte kind = owner._legendKinds[i];
                    using (ui.Row(Space.S, Align.Centre))
                    {
                        var marker = (DescentMapMarker)kind;
                        ui.Shape(AutomapCanvas.ShapeOf(marker), AutomapCanvas.ToneOf(marker), 12f);
                        ui.Text(owner._legendTokens[kind], Text.Caption);
                    }
                }
        }
    }

    private readonly UiWindows _windows;
    private readonly IDescentAutomap _map;
    private readonly AutomapCanvas _canvas;
    private readonly IAutomapPrefs _prefs;
    private readonly IAutomapRoomLabels? _labels;
    private readonly LegendPanel _legend;

    // Which marker kinds the map holds (kinds are 0..10), least first.
    private readonly byte[] _legendKinds = new byte[11];
    private int _legendCount;

    private int _pickedStorey = NoPick;
    private int _heroPin = int.MinValue;
    private int _roomsOnFocus;
    private bool _showLabels = true;
    private bool _prefsRead;

    public AutoMap(UiWindows windows, IDescentAutomap map, AutomapCanvas canvas, IAutomapPrefs prefs,
                   IAutomapRoomLabels? labels = null)
    {
        _windows = windows;
        _map = map;
        _canvas = canvas;
        _prefs = prefs;
        _labels = labels;
        _legend = new LegendPanel(this);
    }

    public override void Describe(ref UiFrame ui)
    {
        // The screen owns the labels toggle, seeded once from the caller's data.
        if (!_prefsRead)
        {
            _showLabels = _prefs.ShowLabelsByDefault;
            _prefsRead = true;
        }

        // The hero's storey is the default view and keeps being it while the
        // hero moves; a clicked chip wins until the hero changes storey.
        if (_map.HeroStorey != _heroPin)
        {
            _heroPin = _map.HeroStorey;
            _pickedStorey = NoPick;
        }
        int focus = _pickedStorey == NoPick ? _map.HeroStorey : _pickedStorey;

        ScanMap(focus);

        using (ui.Window(_windows, WindowId, "#Descent_Map_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            bool narrow = ui.Narrow; // the window's width, not the screen's

            // Storey chips: one per storey, the viewed one accented.
            using (ui.Row(Space.S).Width(Size.Fill).Pad(Space.None, Space.S))
            {
                ui.Text("#Descent_Map_Storey", Text.Label);
                for (int s = 1; s <= _map.StoreyCount; s++)
                {
                    // A 32 du square at least: the number alone is a 12 du target.
                    var chip = ui.Box().Size(Size.Auto, Size.Du(32)).MinWidth(32).Pad(Space.None, Space.S).Round(Radius.S);
                    var hit = ui.Interact(chip);
                    bool viewed = s == focus;
                    chip.Fill(viewed ? ((UiPaint)Tone.Accent).WithOpacity(0.18f) : (UiPaint)Tone.None);
                    chip.Border(viewed ? (UiPaint)Tone.Accent : ((UiPaint)Tone.Line).WithOpacity(0.7f), 1);
                    using (chip)
                        ui.Number(s, viewed ? Text.Heading : Text.Body,
                                  viewed ? Tone.Accent : Tone.Text).At(UiAnchor.Centre);
                    if (hit.Clicked)
                        _pickedStorey = s;
                }
                ui.Flex();
                if (!narrow)
                {
                    // A toggle chip in the storey chips' language: on, the
                    // accent wash, frame and ink; off, the hairline and text.
                    // A button drew the same in both states; the toolkit's
                    // checkbox ticks in OnAccent, a seventh tone in a window
                    // held to six.
                    var labels = ui.Row(Space.None, Align.Centre).Key("labels").Height(Size.Du(32))
                                   .Pad(Space.M, Space.None).Round(Radius.S);
                    var hitLabels = ui.Interact(labels);
                    labels.Fill(_showLabels ? ((UiPaint)Tone.Accent).WithOpacity(0.18f) : (UiPaint)Tone.None);
                    labels.Border(_showLabels ? (UiPaint)Tone.Accent : ((UiPaint)Tone.Line).WithOpacity(0.7f), 1);
                    using (labels)
                        ui.Text("#Descent_Map_Labels", Text.Body, _showLabels ? Tone.Accent : Tone.Text);
                    if (hitLabels.Clicked)
                        _showLabels = !_showLabels;
                }
            }

            // The map: the shared canvas scaled up to the largest square the
            // window keeps after the chip row and the legend.
            float legendH = _legendCount > 0 ? LegendHeightDu(_legendCount, ui.DesignWidth, narrow) : 0f;
            float chipsH = 32f + (float)Space.S * 2;
            float availW = ui.DesignWidth - (float)Space.L * 2;
            float availH = ui.DesignHeight - chipsH - legendH - (float)Space.XS;
            float side = Math.Max(160f, Math.Min(availW, availH));

            using (ui.Row(Space.None).Align(Align.Centre, Align.Centre).Size(Size.Fill, Size.Fill))
                if (_roomsOnFocus > 0)
                {
                    // The box holds the square's natural size for the centring
                    // row: the canvas's own box is a layer and carries none.
                    using (ui.Box().Size(Size.Du(side), Size.Du(side)))
                        ui.Sub("map", _canvas,
                               new AutomapProps(side, side, focus, HeroHeading: true, HeroFollowsStorey: true,
                                                MarkerDu: 16f, Labels: _labels, ShowLabels: _showLabels));
                }
                else
                    ui.EmptyState(UiShape.Crosshair, "#Descent_Map_EmptyTitle", "#Descent_Map_EmptyBody");

            // The legend: the kinds the explored map actually holds.
            if (_legendCount > 0)
                ui.Sub("legend", _legend).Width(Size.Fill);
        }
    }

    /// <summary>One pass over the feed: the present kinds (sorted) and the
    /// focus storey's room count — the reveal rule means unentered rooms
    /// never reach the feed at all.</summary>
    private void ScanMap(int focus)
    {
        _legendCount = 0;
        _roomsOnFocus = 0;
        ulong seen = 0;
        for (int i = 0; i < _map.MarkerCount; i++)
        {
            var m = _map.Marker(i);
            ulong bit = 1UL << (int)m.Kind;
            if ((seen & bit) != 0)
                continue;
            seen |= bit;
            _legendKinds[_legendCount++] = (byte)m.Kind;
            for (int j = _legendCount - 1; j > 0 && _legendKinds[j] < _legendKinds[j - 1]; j--)
                (_legendKinds[j], _legendKinds[j - 1]) = (_legendKinds[j - 1], _legendKinds[j]);
        }
        for (int r = 0; r < _map.RoomCount; r++)
            if (_map.RoomStorey(r) == focus)
                _roomsOnFocus++;
    }

    // Legend sizing: fixed-width cells; columns fill the window, rows take
    // what the map square can spare (24 du rows, hairline gaps, small pads).
    private static float LegendCellW(bool narrow) => narrow ? 128f : 160f;

    private static int LegendColumns(int count, float designW, bool narrow)
    {
        float cellW = LegendCellW(narrow);
        float availW = designW - (float)Space.L * 2;
        int cols = (int)((availW + (float)Space.M) / (cellW + (float)Space.M));
        return Math.Clamp(cols, 1, Math.Max(1, count));
    }

    private static int LegendRows(int count, float designW, bool narrow)
    {
        int cols = LegendColumns(count, designW, narrow);
        return (count + cols - 1) / cols;
    }

    private static float LegendHeightDu(int count, float designW, bool narrow)
    {
        int rows = LegendRows(count, designW, narrow);
        return rows * 24f + (rows - 1) * 4f + (float)Space.S * 2;
    }

    // The 11 marker kinds' legend copy, in enum order. The marks themselves
    // come from the canvas (AutomapCanvas.ShapeOf/ToneOf): a second table here
    // had drifted (the used shrine Warning in the legend, TextMuted on the map).
    // Instance table: a screen's statics survive a UI reload (SSUI005).
    private readonly string[] _legendTokens =
    [
        "#Descent_Map_MStairs", "#Descent_Map_MBoss", "#Descent_Map_MCorpse", "#Descent_Map_MParty",
        "#Descent_Map_MShrine", "#Descent_Map_MShrineUsed", "#Descent_Map_MLoot", "#Descent_Map_MLockedLoot",
        "#Descent_Map_MPortal", "#Descent_Map_MTrap", "#Descent_Map_MFountain",
    ];
}

/// <summary>The automap's viewer prefs: the caller's data says whether room
/// labels start on (the screen owns the toggle after that).</summary>
public interface IAutomapPrefs
{
    bool ShowLabelsByDefault { get; }
}
