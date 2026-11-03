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

    public void Save(IReadOnlyList<UiScriptEntry> list, IReadOnlyList<string> values) => Saved.Add([.. values]);

    public void Press(UiScriptEntry entry) { }
}

/// <summary>What Start was handed.</summary>
public sealed class RecordingCreateServer : ICreateServerActions
{
    public List<(string Map, int MaxPlayers, string HostName, string Password)> Started { get; } = [];

    public void Start(string map, int maxPlayers, string hostName, string password) => Started.Add((map, maxPlayers, hostName, password));
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
        var screen = s.Create<CreateServer>();
        screen.Tab = tab;
        return screen;
    }

    [UiSample("server page")]
    private static UiScreen Server(UiSampleServices s) => Build(s);

    [UiSample("game page from the mod's script")]
    private static UiScreen Game(UiSampleServices s) => Build(s, 1);

    [UiSample("no script, no maps")]
    private static UiScreen Bare(UiSampleServices s)
    {
        s.Add<IMapListSource>(new FixedMapList("SourceSharp"));
        s.Add<IServerSettingsSource>(new MemoryServerSettings());
        return Build(s, 1);
    }
}
