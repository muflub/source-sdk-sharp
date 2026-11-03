using System.Globalization;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The multiplayer options (plan_gameui_managed.md G2; stock: GameUI's
/// multiplayer page and its advanced dialog, as one page): the player's
/// name, the player model (the .vmt pictures under
/// materials/vgui/playermodels, set as cl_playermodel models/&lt;path&gt;.mdl),
/// the two colours, the spray (a logo under materials/vgui/logos with its
/// .vmt beside it, set as cl_logofile), the download filter, and then the
/// mod's advanced entries from its options script, under their categories.
/// The shown set is the one this repo's mods' gameinfo asks for (models on,
/// the high-model toggle and crosshair off). Spray import is not offered (D4).
/// </summary>
public sealed class MultiplayerOptions(IUiConVarStore convars, IUiCommandLine commands, IUiFileList files,
                                       IOptionsScriptSource script) : IUiOptionsPage
{
    private const float LabelColumnDu = UiForm<MouseOptions.Model>.LabelColumnDu;

    private const string ModelFolder = "materials/vgui/playermodels/", LogoFolder = "materials/vgui/logos/";

    /// <summary>The basic settings, the lists' choices as indices.</summary>
    public record struct Settings(string Name, int Model, float TopColour, float BottomColour, int Logo, int DownloadFilter);

    private readonly string[] _filterNames =
        ["#SS_UI_Mp_DownloadAll", "#SS_UI_Mp_DownloadNoSounds", "#SS_UI_Mp_DownloadMapsOnly", "#SS_UI_Mp_DownloadNone"];
    private readonly string[] _filterValues = ["all", "nosounds", "mapsonly", "none"];

    private readonly List<string> _models = [];       // paths under ModelFolder, with .vmt
    private readonly List<string> _modelNames = [];
    private readonly List<string> _logos = [];        // logo names, no extension
    private readonly ScriptRows _advanced = new();

    public Settings Saved { get; private set; }

    public Settings Working { get; set; }

    /// <summary>The advanced entries, as the script gave them.</summary>
    public IReadOnlyList<UiScriptEntry> Entries => _advanced.Entries;

    /// <summary>The advanced entries' working values, by entry.</summary>
    public string[] Values => _advanced.Values;

    /// <summary>The player models listed, by their path under materials/vgui/playermodels.</summary>
    public IReadOnlyList<string> Models => _models;

    /// <summary>The spray logos listed, by name.</summary>
    public IReadOnlyList<string> Logos => _logos;

    public string TabToken => "#SS_UI_Mp_Title";

    public string PageName => "multiplayeroptions";

    public bool AnyDirty
    {
        get
        {
            return Working != Saved || _advanced.AnyDirty;
        }
    }

    public bool IsValid => true;

    public void Load()
    {
        _models.Clear();
        _modelNames.Clear();
        FindModels("");
        _logos.Clear();
        foreach (var file in files.Find(LogoFolder + "*.vtf"))
        {
            string name = file[..^4];
            if (files.Find(LogoFolder + name + ".vmt").Count > 0)
                _logos.Add(name);
        }

        string model = convars.Get("cl_playermodel") ?? "";
        string logo = convars.Get("cl_logofile") ?? "";
        string filter = convars.Get("cl_downloadfilter") ?? "";
        int filterIndex = Array.FindIndex(_filterValues, v => v.Equals(filter, StringComparison.OrdinalIgnoreCase));
        Saved = new Settings(
            convars.Get("name") ?? "",
            Math.Max(0, _models.FindIndex(m => string.Equals("models/" + m[..^4] + ".mdl", model, StringComparison.OrdinalIgnoreCase))),
            Number("topcolor"), Number("bottomcolor"),
            Math.Max(0, _logos.FindIndex(l => string.Equals(LogoFolder + l + ".vtf", logo, StringComparison.OrdinalIgnoreCase))),
            filterIndex < 0 ? 0 : filterIndex);
        Working = Saved;

        _advanced.Load(script.Load());
    }

    public bool Apply()
    {
        var w = Working;
        if (w.Name.Length > 0)
            convars.Set("name", w.Name);
        convars.Set("topcolor", ((int)w.TopColour).ToString(CultureInfo.InvariantCulture));
        convars.Set("bottomcolor", ((int)w.BottomColour).ToString(CultureInfo.InvariantCulture));
        commands.Submit(_logos.Count > 0 ? "cl_logofile " + LogoFolder + _logos[Math.Clamp(w.Logo, 0, _logos.Count - 1)] + ".vtf"
                                         : "cl_logofile \"\"");
        if (_models.Count > 0)
            commands.Submit("cl_playermodel models/" + _models[Math.Clamp(w.Model, 0, _models.Count - 1)][..^4] + ".mdl");
        convars.Set("cl_downloadfilter", _filterValues[Math.Clamp(w.DownloadFilter, 0, _filterValues.Length - 1)]);
        if (_advanced.Entries.Count > 0)
            script.Save(_advanced.Entries, _advanced.Values);
        _advanced.TakeSaved();
        Saved = Working;
        return true;
    }

    public void TakeKeys(ref UiFrame ui) { }

    public void DescribeBody(ref UiFrame ui)
    {
        var w = Working;
        using (ui.Scroll("fields", Space.M).Size(Size.Fill, Size.Fill))
        using (ui.FormGrid(LabelColumnDu).Width(Size.Fill))
        {
            Heading(ref ui, 0, "#SS_UI_Mp_Player");
            Label(ref ui, 1, "#SS_UI_Mp_Name");
            using (Control(ref ui, 1))
            {
                string name = ui.EditText("name", w.Name, "#SS_UI_Form_TextPlaceholder");
                if (!ReferenceEquals(name, w.Name))
                    w.Name = name;
            }
            Label(ref ui, 2, "#SS_UI_Mp_Model");
            using (Control(ref ui, 2))
                w.Model = ui.Choice("model", w.Model, _modelNames.Count, new UiChoiceList(_modelNames));
            Label(ref ui, 3, "#SS_UI_Mp_TopColour");
            using (Control(ref ui, 3))
                w.TopColour = Colour(ref ui, "top", w.TopColour);
            Label(ref ui, 4, "#SS_UI_Mp_BottomColour");
            using (Control(ref ui, 4))
                w.BottomColour = Colour(ref ui, "bottom", w.BottomColour);
            Label(ref ui, 5, "#SS_UI_Mp_Spray");
            using (Control(ref ui, 5))
                w.Logo = ui.Choice("logo", w.Logo, _logos.Count, new UiChoiceList(_logos));
            Label(ref ui, 6, "#SS_UI_Mp_DownloadFilter");
            using (Control(ref ui, 6))
                w.DownloadFilter = ui.Choice("filter", w.DownloadFilter, _filterNames.Length, new UiChoiceList(_filterNames), tokens: true);

            // The script's own first category heads its entries when it has one.
            if (_advanced.Entries.Count > 0 && _advanced.Entries[0].Kind != UiScriptKind.Category)
                Heading(ref ui, 7, "#SS_UI_Mp_Advanced");
            if (_advanced.Describe(ref ui, EntryKeyBase) is var pressed and >= 0)
                script.Press(_advanced.Entries[pressed]);
        }
        Working = w;
    }

    // Grid keys: a row's label is its key, its control 1000 past it, and the
    // script's entries start past the fixed rows.
    private const int ControlKey = 1000, EntryKeyBase = 100;

    private static void Heading(ref UiFrame ui, int key, string token)
    {
        ui.Text(token, Text.Label, Tone.Accent).Key(key);
        ui.Spacer(Size.Du(1), Size.Du(1)).Key(ControlKey + key);
    }

    private static float Colour(ref UiFrame ui, string key, float value)
    {
        using (ui.Row(Space.M).Width(Size.Fill))
        {
            value = ui.Slider(key, value, 0f, 255f, 1f);
            ui.Number((int)MathF.Round(value), Text.Body, Tone.Text).MinWidth(56);
        }
        return value;
    }

    private static void Label(ref UiFrame ui, int key, string token) => ui.Text(token, Text.Body, Tone.Text).Wrap(LabelColumnDu).Key(key);

    private static UiBox Control(ref UiFrame ui, int key)
        => ui.Column(Space.XS, Align.Start).Key(ControlKey * 10 + key).Width(Size.Fill);

    // Stock's FindVMTFilesInFolder: every .vmt under the folder, into its subfolders.
    private void FindModels(string sub)
    {
        foreach (var file in files.Find(ModelFolder + sub + "*.vmt"))
        {
            _models.Add(sub + file);
            _modelNames.Add(file[..^4]);
        }
        foreach (var dir in files.Directories(ModelFolder + sub + "*"))
            FindModels(sub + dir + "/");
    }

    private float Number(string name) => ScriptRows.Number(convars.Get(name));
}
