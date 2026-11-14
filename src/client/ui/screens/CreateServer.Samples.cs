using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>A fixed map list and game name.</summary>
public sealed class FixedMapList(string gameName, params string[] maps) : IMapListSource
{
    public string GameName => gameName;

    public IReadOnlyList<string> Maps() => maps;
}

/// <summary>A server script held as entries.</summary>
public sealed class MemoryServerSettings(params UiScriptEntry[] entries) : IServerSettingsSource
{
    public List<string[]> Saved { get; } = [];

    public IReadOnlyList<UiScriptEntry> Load() => entries;

    /// <summary>Each save's entries by name, beside <see cref="Saved"/>'s values.</summary>
    public List<string[]> SavedNames { get; } = [];

    public void Save(IReadOnlyList<UiScriptEntry> list, IReadOnlyList<string> values)
    {
        Saved.Add([.. values]);
        SavedNames.Add([.. list.Select(e => e.Name)]);
    }

    public void Press(UiScriptEntry entry) { }
}

/// <summary>A catalog held as modes: the client's own build, before a server's.</summary>
public sealed class FakeCatalog(params CatalogMode[] modes) : ICatalogSource
{
    public IReadOnlyList<CatalogMode> Modes => modes;

    private static SettingSpec Choice(string key, string value, params (string Value, string Label)[] choices)
        => new(key, SettingKind.Choice, [.. choices.Select(c => new SettingChoice(c.Value, c.Label))], value);

    private static SettingSpec Bool(string key, string value)
        => new(key, SettingKind.Bool, [new("0", "#SS_UI_Vote_Off"), new("1", "#SS_UI_Vote_On")], value);

    /// <summary>The core's two modes, as the client builds them (CoreModeSpecs).</summary>
    public static FakeCatalog Core()
    {
        SettingSpec time = Choice("mp_timelimit", "0", ("0", "#SS_UI_Vote_NoLimit"), ("10", "#SS_UI_Vote_Min10"), ("20", "#SS_UI_Vote_Min20"));
        SettingSpec frags = Choice("mp_fraglimit", "0", ("0", "#SS_UI_Vote_NoLimit"), ("10", "10"), ("20", "20"), ("30", "30"));
        var ok = new MapSupport(true, "");
        return new FakeCatalog(
            new CatalogMode(new ModeSpec("null", "#SS_UI_Mode_Deathmatch", false, [time, frags], []), ok),
            new CatalogMode(new ModeSpec("tdm", "#SS_UI_Mode_TeamDeathmatch", true,
                [Bool("mp_friendlyfire", "0"), time, frags, Choice("mp_tdm_teams", "2", ("2", "2"), ("3", "3"), ("4", "4")),
                 Bool("mp_teammates_collide", "0")], []), ok));
    }
}

/// <summary>What Start was handed.</summary>
public sealed class RecordingCreateServer : ICreateServerActions
{
    public List<(string Map, int MaxPlayers, string HostName, string Password)> Started { get; } = [];

    /// <summary>Each Start's lines from the mod's pages.</summary>
    public List<string[]> BeforeMap { get; } = [];

    public void Start(string map, int maxPlayers, string hostName, string password, IReadOnlyList<string> beforeMap)
    {
        Started.Add((map, maxPlayers, hostName, password));
        BeforeMap.Add([.. beforeMap]);
    }
}

public static class CreateServerSamples
{
    public static FixedMapList Maps() => new("Half-Life 2: Deathmatch",
        "dm_lockdown", "dm_overwatch", "dm_powerhouse", "dm_resistance", "dm_runoff", "dm_steamlab", "dm_underpass", "halls3");

    /// <summary>The shape of TF2's cfg/settings_default.scr.</summary>
    public static MemoryServerSettings Script() => new(
        new UiScriptEntry("hostname", "#SS_UI_Create_HostName", null, UiScriptKind.String, 0, 0, [], "Team Fortress 2", false),
        new UiScriptEntry("maxplayers", "#SS_UI_Create_MaxPlayers", null, UiScriptKind.Number, 1, 24, [], "24", false),
        new UiScriptEntry("sv_password", "#SS_UI_Create_Password", null, UiScriptKind.String, 0, 0, [], "", false),
        new UiScriptEntry("mp_timelimit", "Time limit (minutes)", null, UiScriptKind.Number, 0, -1, [], "30", false),
        new UiScriptEntry("mp_winlimit", "Win limit (rounds)", null, UiScriptKind.Number, 0, -1, [], "0", false),
        new UiScriptEntry("mp_autoteambalance", "Auto team balance", null, UiScriptKind.Bool, 0, 0, [], "1", false),
        new UiScriptEntry("mp_friendlyfire", "Friendly fire", null, UiScriptKind.Bool, 0, 0, [], "0", false));

    public static CreateServer Build(UiSampleServices s, int tab = 0)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        if (!s.Has<IMapListSource>())
            s.Add<IMapListSource>(Maps());
        if (!s.Has<IServerSettingsSource>())
            s.Add<IServerSettingsSource>(Script());
        if (!s.Has<ICreateServerActions>())
            s.Add<ICreateServerActions>(new RecordingCreateServer());
        if (!s.Has<IReadOnlyList<CreateServerPage>>())
            s.Add<IReadOnlyList<CreateServerPage>>([]);
        if (!s.Has<ICatalogSource>())
            s.Add<ICatalogSource>(FakeCatalog.Core());
        var screen = s.Create<CreateServer>();
        screen.Tab = tab;
        return screen;
    }

    [UiSample("server page")]
    private static UiScreen Server(UiSampleServices s) => Build(s);

    [UiSample("game page from the mod's script")]
    private static UiScreen Game(UiSampleServices s) => Build(s, 1);

    /// <summary>A script whose last start chose <paramref name="mode"/> (cfg/settings.scr's sv_mode_next).</summary>
    public static MemoryServerSettings ScriptWithMode(string mode)
        => new([.. Script().Load(), new UiScriptEntry(CreateServer.ModeVar, "#SS_UI_Create_Mode", null, UiScriptKind.String, 0, 0, [], mode, false)]);

    [UiSample("game page, team deathmatch chosen")]
    private static UiScreen TeamDeathmatch(UiSampleServices s)
    {
        s.Add<IServerSettingsSource>(ScriptWithMode("tdm"));
        return Build(s, 1);
    }

    [UiSample("game page, deathmatch chosen")]
    private static UiScreen Deathmatch(UiSampleServices s)
    {
        s.Add<IServerSettingsSource>(ScriptWithMode("null"));
        return Build(s, 1);
    }

    [UiSample("no script, no maps")]
    private static UiScreen Bare(UiSampleServices s)
    {
        s.Add<IMapListSource>(new FixedMapList("SourceSharp"));
        s.Add<IServerSettingsSource>(new MemoryServerSettings());
        return Build(s, 1);
    }
}
