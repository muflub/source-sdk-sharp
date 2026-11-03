using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>Console lines in a list: the options pages' sample sink, and their facts'.</summary>
public sealed class RecordingCommandLine : IUiCommandLine
{
    public List<string> Lines { get; } = [];

    public void Submit(string line) => Lines.Add(line);
}

/// <summary>A fixed card: a desktop, its modes, a current mode, and the antialiasing it does.</summary>
public sealed class FixedVideoModes(UiVideoMode desktop, UiVideoMode current, bool windowed, params UiVideoMode[] modes)
    : IVideoModeSource
{
    public IReadOnlyList<UiVideoMode> Modes => modes;

    public UiVideoMode Desktop => desktop;

    public UiVideoMode Current { get; set; } = current;

    public bool Windowed { get; set; } = windowed;

    public HashSet<int> Msaa { get; } = [2, 4, 8];

    public bool SupportsMsaa(int samples) => Msaa.Contains(samples);

    public bool SupportsCsaa(int samples, int quality) => false;
}

/// <summary>A microphone held in fields.</summary>
public sealed class MemoryMicrophone : IMicrophoneSettings
{
    public bool Available { get; set; } = true;

    public float Volume { get; set; } = 0.6f;

    public bool Boost { get; set; }
}

/// <summary>Files by directory: each wildcard's directory part is looked up, the rest matched as stock's *.ext.</summary>
public sealed class MemoryFileList : IUiFileList
{
    private readonly List<string> _paths = [];

    public MemoryFileList With(params string[] paths)
    {
        _paths.AddRange(paths);
        return this;
    }

    public IReadOnlyList<string> Find(string wildcard) => Match(wildcard, directories: false);

    public IReadOnlyList<string> Directories(string wildcard) => Match(wildcard, directories: true);

    private List<string> Match(string wildcard, bool directories)
    {
        int slash = wildcard.LastIndexOf('/');
        string dir = wildcard[..(slash + 1)], pattern = wildcard[(slash + 1)..];
        string suffix = pattern.StartsWith('*') ? pattern[1..] : pattern;
        var found = new List<string>();
        foreach (var path in _paths)
        {
            if (!path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = path[dir.Length..];
            int sub = rest.IndexOf('/');
            string name = sub >= 0 ? rest[..sub] : rest;
            if ((sub >= 0) != directories || found.Contains(name))
                continue;
            if (pattern.StartsWith('*') ? name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                                        : name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                found.Add(name);
        }
        return found;
    }
}

/// <summary>An options script held as entries: what Load answers, and what Save and Press were handed.</summary>
public sealed class MemoryOptionsScript(params UiScriptEntry[] entries) : IOptionsScriptSource
{
    public List<string[]> Saved { get; } = [];

    public List<string> Pressed { get; } = [];

    public IReadOnlyList<UiScriptEntry> Load() => entries;

    public void Save(IReadOnlyList<UiScriptEntry> list, IReadOnlyList<string> values) => Saved.Add([.. values]);

    public void Press(UiScriptEntry entry) => Pressed.Add(entry.Value);
}

public static class OptionsSamples
{
    public static UiVideoMode Desktop => new(1920, 1080);

    public static FixedVideoModes Card() => new(Desktop, Desktop, false,
        new(640, 480), new(800, 600), new(1024, 768), new(1280, 720), new(1280, 800), new(1600, 900), new(1680, 1050),
        new(1920, 1080), new(2560, 1440));

    public static MemoryConVarStore Convars() => MouseOptionsSamples.Stock()
        .With("volume", "0.8").With("Snd_MusicVolume", "0.5").With("closecaption", "1").With("cc_subtitles", "0")
        .With("Snd_PitchQuality", "1").With("dsp_slow_cpu", "0").With("Snd_Surround_Speakers", "2")
        .With("snd_mute_losefocus", "1").With("voice_modenable", "1").With("voice_scale", "1")
        .With("mat_monitorgamma", "2.2").With("r_rootlod", "0").With("mat_picmip", "0").With("mat_reducefillrate", "0")
        .With("r_waterforceexpensive", "1").With("r_waterforcereflectentities", "0").With("r_shadowrendertotexture", "1")
        .With("mat_colorcorrection", "1").With("mat_antialias", "4").With("mat_aaquality", "0").With("mat_forceaniso", "8")
        .With("mat_vsync", "0").With("mat_motion_blur_enabled", "0").With("mat_queue_mode", "-1").With("mat_hdr_level", "2")
        .With("name", "Gordon").With("cl_playermodel", "models/humans/group03/male_07.mdl")
        .With("cl_logofile", "materials/vgui/logos/lambda.vtf").With("cl_downloadfilter", "nosounds")
        .With("topcolor", "30").With("bottomcolor", "160");

    public static MemoryFileList Files() => new MemoryFileList().With(
        "materials/vgui/playermodels/humans/group03/male_07.vmt", "materials/vgui/playermodels/humans/group03/female_02.vmt",
        "materials/vgui/playermodels/combine/super_soldier.vmt", "materials/vgui/playermodels/police.vmt",
        "materials/vgui/logos/lambda.vtf", "materials/vgui/logos/lambda.vmt",
        "materials/vgui/logos/smiley.vtf", "materials/vgui/logos/smiley.vmt", "materials/vgui/logos/unpaired.vtf");

    public static MemoryOptionsScript Script() => new(
        new UiScriptEntry("category1", "Communication", null, UiScriptKind.Category, 0, 0, [], "", false),
        new UiScriptEntry("hud_fastswitch", "Fast weapon switch", null, UiScriptKind.Bool, 0, 0, [], "1", false),
        new UiScriptEntry("cl_autoreload", "Auto-reload", null, UiScriptKind.Bool, 0, 0, [], "0", true),
        new UiScriptEntry("hud_combattext_style", "Damage numbers", null, UiScriptKind.List, 0, 0,
                          [new("Per hit", "0"), new("Batched", "1")], "1", false),
        new UiScriptEntry("viewmodel_fov", "Viewmodel field of view", null, UiScriptKind.Slider, 54, 70, [], "62", false),
        new UiScriptEntry("chat_filter_settings", "Chat filter settings", null, UiScriptKind.Button, 0, 0, [],
                          "open_chat_filter_settings", false));

    /// <summary>The window over every page, each from the services so a fact can reach the fakes.</summary>
    public static Options Build(UiSampleServices s, int tab = 0)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        if (!s.Has<IUiConVarStore>())
            s.Add<IUiConVarStore>(Convars());
        if (!s.Has<IUiKeyBindingStore>())
            s.Add<IUiKeyBindingStore>(KeyBindingsSamples.Stock());
        if (!s.Has<IUiKeyActions>())
            s.Add<IUiKeyActions>(new FakeKeyActions());
        if (!s.Has<IUiCommandLine>())
            s.Add<IUiCommandLine>(new RecordingCommandLine());
        if (!s.Has<IVideoModeSource>())
            s.Add<IVideoModeSource>(Card());
        if (!s.Has<IMicrophoneSettings>())
            s.Add<IMicrophoneSettings>(new MemoryMicrophone());
        if (!s.Has<IUiFileList>())
            s.Add<IUiFileList>(Files());
        if (!s.Has<IOptionsScriptSource>())
            s.Add<IOptionsScriptSource>(Script());
        var windows = s.Create<UiWindows>();
        return new Options(windows,
        [
            s.Create<KeyBindings>(), s.Create<MouseOptions>(), s.Create<AudioOptions>(), s.Create<VideoOptions>(),
            s.Create<VoiceOptions>(), s.Create<MultiplayerOptions>(), s.Create<AdvancedOptions>(),
        ]) { Tab = tab };
    }

    [UiSample("keyboard")]
    private static UiScreen Keyboard(UiSampleServices s) => Build(s, 0);

    [UiSample("audio")]
    private static UiScreen Audio(UiSampleServices s) => Build(s, 2);

    [UiSample("video")]
    private static UiScreen Video(UiSampleServices s) => Build(s, 3);

    [UiSample("voice")]
    private static UiScreen Voice(UiSampleServices s) => Build(s, 4);

    [UiSample("multiplayer with advanced entries")]
    private static UiScreen Multiplayer(UiSampleServices s) => Build(s, 5);

    [UiSample("multiplayer, no models, logos or script")]
    private static UiScreen MultiplayerBare(UiSampleServices s)
    {
        s.Add<IUiFileList>(new MemoryFileList());
        s.Add<IOptionsScriptSource>(new MemoryOptionsScript());
        return Build(s, 5);
    }
}
