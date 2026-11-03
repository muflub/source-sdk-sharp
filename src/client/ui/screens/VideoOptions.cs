using System.Globalization;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The video options (plan_gameui_managed.md G2; stock: GameUI's video page
/// and its advanced dialog, as one page): the resolution from the engine's
/// modes, filtered by aspect ratio and, as on SDL, never above the desktop;
/// fullscreen or windowed; the gamma; and the advanced settings, each a
/// choice over the convars stock's apply wrote. The antialiasing choices are
/// the modes the card answers it supports. Applied: the advanced convars
/// through the console as stock sent them, <c>mat_setvideomode</c> when the
/// mode changed (fullscreen keeps the desktop's aspect, as SDL's fullscreen
/// does), then <c>mat_savechanges</c>. A page and not a form: its lists come
/// from the engine when it loads.
/// </summary>
public sealed class VideoOptions(IVideoModeSource modes, IUiConVarStore convars, IUiCommandLine commands) : IUiOptionsPage
{
    /// <summary>The settings the page edits, as choice indices (and the gamma).</summary>
    public record struct Settings(
        int Aspect, int Mode, int Display, float Gamma, int Model, int Texture, int Shader, int Water, int Shadow,
        int ColorCorrection, int Antialias, int Filtering, int VSync, int MotionBlur, int Multicore, int Hdr);

    private readonly (int Samples, int Quality, string Token)[] AntialiasModes =
    [
        (2, 0, "#SS_UI_Video_AA2x"), (4, 0, "#SS_UI_Video_AA4x"), (6, 0, "#SS_UI_Video_AA6x"),
        (4, 2, "#SS_UI_Video_AA8xCsaa"), (4, 4, "#SS_UI_Video_AA16xCsaa"), (8, 0, "#SS_UI_Video_AA8x"),
        (8, 2, "#SS_UI_Video_AA16xqCsaa"),
    ];

    private readonly string[] AspectNames = ["#SS_UI_Video_Aspect4x3", "#SS_UI_Video_Aspect16x9", "#SS_UI_Video_Aspect16x10"];
    private readonly string[] DisplayNames = ["#SS_UI_Video_Fullscreen", "#SS_UI_Video_Windowed"];
    private readonly string[] LowMediumHigh = ["#SS_UI_Low", "#SS_UI_Medium", "#SS_UI_High"];
    private readonly string[] TextureNames = ["#SS_UI_Low", "#SS_UI_Medium", "#SS_UI_High", "#SS_UI_VeryHigh"];
    private readonly string[] LowHigh = ["#SS_UI_Low", "#SS_UI_High"];
    private readonly string[] OffOn = ["#SS_UI_Disabled", "#SS_UI_Enabled"];
    private readonly string[] WaterNames = ["#SS_UI_Video_WaterNone", "#SS_UI_Video_WaterWorld", "#SS_UI_Video_WaterAll"];
    private readonly string[] FilteringNames =
        ["#SS_UI_Video_Bilinear", "#SS_UI_Video_Trilinear", "#SS_UI_Video_Aniso2x", "#SS_UI_Video_Aniso4x",
         "#SS_UI_Video_Aniso8x", "#SS_UI_Video_Aniso16x"];
    private readonly string[] HdrNames = ["#SS_UI_Video_HdrNone", "#SS_UI_Video_HdrBloom", "#SS_UI_Video_HdrFull"];

    private IReadOnlyList<UiVideoMode> _all = [];
    private UiVideoMode _desktop;
    private readonly List<UiVideoMode> _listed = [];
    private readonly List<string> _modeNames = [];
    private readonly List<string> _aaNames = [];
    private readonly List<(int Samples, int Quality)> _aaModes = [];
    private (int Aspect, int Display) _listedFor = (-1, -1);

    public Settings Saved { get; private set; }

    public Settings Working { get; set; }

    /// <summary>The resolutions listed for the working aspect and display mode.</summary>
    public IReadOnlyList<UiVideoMode> ListedModes => _listed;

    private const float LabelColumnDu = UiForm<MouseOptions.Model>.LabelColumnDu;

    public string TabToken => "#SS_UI_Video_Title";

    public string PageName => "videooptions";

    public bool AnyDirty => Working != Saved;

    public bool IsValid => true;

    /// <summary>Stock's aspect mode for a size: the nearest of 4:3, 16:9 and 16:10.</summary>
    public static int AspectOf(int width, int height)
    {
        if (height <= 0)
            return 0;
        // Stock's table: 4:3, 16:9, 16:10, and square counted as 16:10.
        float ratio = (float)width / height, best = MathF.Abs(4f / 3f - ratio);
        int mode = 0;
        Nearer(1, 16f / 9f);
        Nearer(2, 16f / 10f);
        Nearer(2, 1f);
        return mode;

        void Nearer(int m, float r)
        {
            if (MathF.Abs(r - ratio) < best)
            {
                best = MathF.Abs(r - ratio);
                mode = m;
            }
        }
    }

    public void Load()
    {
        _all = modes.Modes;
        _desktop = modes.Desktop;
        _aaModes.Clear();
        _aaNames.Clear();
        _aaModes.Add((1, 0));
        _aaNames.Add("#SS_UI_None");
        foreach (var (samples, quality, token) in AntialiasModes)
            if (quality == 0 ? modes.SupportsMsaa(samples) : modes.SupportsCsaa(samples, quality))
            {
                _aaModes.Add((samples, quality));
                _aaNames.Add(token);
            }

        var current = modes.Current;
        int display = modes.Windowed ? 1 : 0;
        int aspect = AspectOf(current.Width, current.Height);
        ListModes(aspect, display);
        int picmip = Math.Clamp(Int("mat_picmip"), -1, 2);
        int aniso = Int("mat_forceaniso");
        int filtering = aniso switch { 2 => 2, 4 => 3, 8 => 4, 16 => 5, _ => Int("mat_trilinear") != 0 ? 1 : 0 };
        Saved = new Settings(
            aspect, IndexOf(current), display, Float("mat_monitorgamma", 2.2f),
            2 - Math.Clamp(Int("r_rootlod"), 0, 2), 2 - picmip, Int("mat_reducefillrate") != 0 ? 0 : 1,
            Int("r_waterforceexpensive") == 0 ? 0 : Int("r_waterforcereflectentities") != 0 ? 2 : 1,
            Int("r_flashlightdepthtexture") != 0 ? 2 : Int("r_shadowrendertotexture") != 0 ? 1 : 0,
            Math.Clamp(Int("mat_colorcorrection"), 0, 1), AntialiasIndex(Int("mat_antialias"), Int("mat_aaquality")), filtering,
            Math.Clamp(Int("mat_vsync"), 0, 1), Math.Clamp(Int("mat_motion_blur_enabled"), 0, 1), Int("mat_queue_mode") == 0 ? 0 : 1,
            Math.Clamp(Int("mat_hdr_level"), 0, 2));
        Working = Saved;
    }

    public bool Apply()
    {
        var w = Working;
        Command("r_rootlod", 2 - w.Model);
        Command("mat_picmip", 2 - w.Texture);
        Command("mat_trilinear", w.Filtering == 1 ? 1 : 0);
        Command("mat_forceaniso", w.Filtering switch { 2 => 2, 3 => 4, 4 => 8, 5 => 16, _ => 1 });
        var (samples, quality) = _aaModes[Math.Clamp(w.Antialias, 0, _aaModes.Count - 1)];
        Command("mat_antialias", samples);
        Command("mat_aaquality", quality);
        convars.Set("mat_hdr_level", w.Hdr.ToString(CultureInfo.InvariantCulture));
        Command("r_shadowrendertotexture", w.Shadow > 0 ? 1 : 0);
        Command("r_flashlightdepthtexture", w.Shadow > 1 ? 1 : 0);
        Command("mat_reducefillrate", w.Shader > 0 ? 0 : 1);
        Command("r_waterforceexpensive", w.Water > 0 ? 1 : 0);
        Command("r_waterforcereflectentities", w.Water > 1 ? 1 : 0);
        Command("mat_vsync", w.VSync);
        Command("mat_queue_mode", w.Multicore == 0 ? 0 : -1);
        Command("mat_colorcorrection", w.ColorCorrection);
        Command("mat_motion_blur_enabled", w.MotionBlur);
        convars.Set("mat_monitorgamma", w.Gamma.ToString("0.##", CultureInfo.InvariantCulture));

        ListModes(w.Aspect, w.Display);
        bool windowed = w.Display == 1;
        var current = modes.Current;
        var chosen = (uint)w.Mode < (uint)_listed.Count ? _listed[w.Mode] : current;
        int width = chosen.Width, height = chosen.Height;
        // SDL's fullscreen is the desktop scaled: the height keeps its aspect.
        if (!windowed && _desktop.Width > 0 && (width != _desktop.Width || height != _desktop.Height))
            height = width * _desktop.Height / _desktop.Width;
        if (width != current.Width || height != current.Height || windowed != modes.Windowed)
            commands.Submit(string.Create(CultureInfo.InvariantCulture, $"mat_setvideomode {width} {height} {(windowed ? 1 : 0)}"));
        commands.Submit("mat_savechanges");
        Saved = Working;
        return true;
    }

    public void TakeKeys(ref UiFrame ui) { }

    public void DescribeBody(ref UiFrame ui)
    {
        var w = Working;
        if (_listedFor != (w.Aspect, w.Display))
        {
            var mode = (uint)w.Mode < (uint)_listed.Count ? _listed[w.Mode] : default;
            ListModes(w.Aspect, w.Display);
            w.Mode = IndexOf(mode);
        }
        using (ui.Scroll("fields", Space.M).Size(Size.Fill, Size.Fill))
        using (ui.FormGrid(LabelColumnDu).Width(Size.Fill))
        {
            Section(ref ui, 0, "#SS_UI_Video_Display");
            w.Mode = Pick(ref ui, 1, "#SS_UI_Video_Resolution", w.Mode, new UiChoiceList(_modeNames), false);
            w.Aspect = Pick(ref ui, 2, "#SS_UI_Video_Aspect", w.Aspect, new UiChoiceList(AspectNames), true);
            w.Display = Pick(ref ui, 3, "#SS_UI_Video_DisplayMode", w.Display, new UiChoiceList(DisplayNames), true);
            ui.Text("#SS_UI_Video_Gamma", Text.Body, Tone.Text).Wrap(LabelColumnDu).Key(4);
            using (ui.Row(Space.M).Key(1004).Width(Size.Fill))
            {
                w.Gamma = ui.Slider("gamma", w.Gamma, 1.6f, 2.6f, 0.05f);
                // Hundredths as two whole numbers: an int formats without allocating.
                int hundredths = (int)MathF.Round(w.Gamma * 100f);
                ui.Text($"{hundredths / 100}.{hundredths % 100:00}", Text.Body, Tone.Text).MinWidth(56);
            }

            Section(ref ui, 5, "#SS_UI_Video_Advanced");
            w.Model = Pick(ref ui, 6, "#SS_UI_Video_Model", w.Model, new UiChoiceList(LowMediumHigh), true);
            w.Texture = Pick(ref ui, 7, "#SS_UI_Video_Texture", w.Texture, new UiChoiceList(TextureNames), true);
            w.Shader = Pick(ref ui, 8, "#SS_UI_Video_Shader", w.Shader, new UiChoiceList(LowHigh), true);
            w.Water = Pick(ref ui, 9, "#SS_UI_Video_Water", w.Water, new UiChoiceList(WaterNames), true);
            w.Shadow = Pick(ref ui, 10, "#SS_UI_Video_Shadow", w.Shadow, new UiChoiceList(LowMediumHigh), true);
            w.ColorCorrection = Pick(ref ui, 11, "#SS_UI_Video_ColorCorrection", w.ColorCorrection, new UiChoiceList(OffOn), true);
            w.Antialias = Pick(ref ui, 12, "#SS_UI_Video_Antialias", w.Antialias, new UiChoiceList(_aaNames), true);
            w.Filtering = Pick(ref ui, 13, "#SS_UI_Video_Filtering", w.Filtering, new UiChoiceList(FilteringNames), true);
            w.VSync = Pick(ref ui, 14, "#SS_UI_Video_VSync", w.VSync, new UiChoiceList(OffOn), true);
            w.MotionBlur = Pick(ref ui, 15, "#SS_UI_Video_MotionBlur", w.MotionBlur, new UiChoiceList(OffOn), true);
            w.Multicore = Pick(ref ui, 16, "#SS_UI_Video_Multicore", w.Multicore, new UiChoiceList(OffOn), true);
            w.Hdr = Pick(ref ui, 17, "#SS_UI_Video_Hdr", w.Hdr, new UiChoiceList(HdrNames), true);
        }
        Working = w;
    }

    private static void Section(ref UiFrame ui, int key, string token)
    {
        ui.Text(token, Text.Label, Tone.Accent).Key(key);
        ui.Spacer(Size.Du(1), Size.Du(1)).Key(1000 + key);
    }

    private static int Pick(ref UiFrame ui, int key, string label, int chosen, UiChoiceList names, bool tokens)
    {
        ui.Text(label, Text.Body, Tone.Text).Wrap(LabelColumnDu).Key(key);
        using (ui.Column(Space.XS, Align.Start).Key(1000 + key).Width(Size.Fill))
            return ui.Choice(label, chosen, names.Count, names, tokens);
    }

    // The resolutions for an aspect and display mode, and their names: not a
    // describe method, so the only place the lists are rebuilt.
    private void ListModes(int aspect, int display)
    {
        _listedFor = (aspect, display);
        _listed.Clear();
        _modeNames.Clear();
        foreach (var m in _all)
        {
            if (_desktop.Width > 0 && (m.Width > _desktop.Width || m.Height > _desktop.Height))
                continue;
            if (AspectOf(m.Width, m.Height) != aspect || _listed.Contains(m))
                continue;
            _listed.Add(m);
            _modeNames.Add(m == _desktop
                ? string.Create(CultureInfo.InvariantCulture, $"{m.Width} x {m.Height} (native)")
                : string.Create(CultureInfo.InvariantCulture, $"{m.Width} x {m.Height}"));
        }
    }

    private int IndexOf(UiVideoMode mode)
    {
        int i = _listed.IndexOf(mode);
        return i >= 0 ? i : Math.Max(0, _listed.Count - 1);
    }

    private int AntialiasIndex(int samples, int quality)
    {
        int i = _aaModes.IndexOf((samples, quality));
        return i >= 0 ? i : 0;
    }

    private void Command(string name, int value)
        => commands.Submit(string.Create(CultureInfo.InvariantCulture, $"{name} {value}"));

    private int Int(string name)
        => float.TryParse(convars.Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? (int)v : 0;

    private float Float(string name, float otherwise)
        => float.TryParse(convars.Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : otherwise;
}
