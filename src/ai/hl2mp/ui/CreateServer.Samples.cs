using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.Hl2mp.Ui;

/// <summary>The create-server screen's state for samples, recording what it was asked.</summary>
public sealed class FakeCreateServer : ICreateServerSource, ICreateServerActions
{
    public string MapName { get; set; } = "dm_lockdown";

    public bool MapFound { get; set; } = true;

    public int MaxPlayers { get; set; } = 8;

    public int MaxPlayersMin => 2;

    public int MaxPlayersMax => 16;

    public int BotCount { get; set; } = 3;

    public int BotCountMax => Math.Max(0, MaxPlayers - 1 - Customs.Count);

    public int Difficulty { get; set; } = 1;

    public int DifficultyStops => 3;

    public string DifficultyToken(int stop) => stop switch
    {
        0 => "#Ai_Bots_Easy",
        1 => "#Ai_Bots_Medium",
        _ => "#Ai_Bots_Hard",
    };

    public int DifficultySpent(int stop) => stop switch
    {
        0 => 20,
        1 => 35,
        _ => 50,
    };

    public List<CustomBotRow> Customs { get; init; } = [];

    public int CustomCount => Customs.Count;

    public CustomBotRow Custom(int i) => Customs[i];

    public string StartRefusalToken { get; set; } = "";

    public List<string> Asked { get; } = [];

    public void SetMapName(string name) => MapName = name;

    public void SetMaxPlayers(int count) => MaxPlayers = count;

    public void SetBotCount(int count) => BotCount = count;

    public void SetDifficulty(int stop) => Difficulty = stop;

    public void AddCustom() => Asked.Add("add");

    public void EditCustom(int i) => Asked.Add($"edit {i}");

    public void RemoveCustom(int i) => Asked.Add($"remove {i}");

    public void Start() => Asked.Add("start");

    /// <summary>Three custom bots: a saved one, an unnamed one, and Expert's full spend.</summary>
    public static List<CustomBotRow> Three() =>
    [
        new("Deadeye", 45, 60, "#Ai_Skill_Accuracy", "#Ai_Skill_Alertness"),
        new("", 35, 60, "#Ai_Skill_Accuracy", "#Ai_Skill_Alertness"),
        new("The_Longest_Profile_Name_Allowed", 60, 60, "#Ai_Skill_Headhunter", "#Ai_Skill_Discipline"),
    ];

    public UiSampleServices Register(UiSampleServices s, MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        return s.Add<ICreateServerSource>(this).Add<ICreateServerActions>(this);
    }
}

internal static class CreateServerSamples
{
    [UiSample("default")]
    private static UiScreen Default(UiSampleServices s) => new FakeCreateServer().Register(s).Create<CreateServer>();

    [UiSample("with custom bots")]
    private static UiScreen WithCustom(UiSampleServices s)
        => new FakeCreateServer { BotCount = 2, Difficulty = 0, Customs = FakeCreateServer.Three() }.Register(s).Create<CreateServer>();

    [UiSample("map not found")]
    private static UiScreen NoMap(UiSampleServices s)
        => new FakeCreateServer { MapName = "dm_nowhere", MapFound = false, StartRefusalToken = "#Ai_Start_NoMap" }
           .Register(s).Create<CreateServer>();

    [UiSample("full server, hard")]
    private static UiScreen Full(UiSampleServices s)
        => new FakeCreateServer { MaxPlayers = 16, BotCount = 12, Difficulty = 2, Customs = FakeCreateServer.Three() }
           .Register(s).Create<CreateServer>();

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => new FakeCreateServer { BotCount = 2, Customs = FakeCreateServer.Three() }
           .Register(s, new MemoryWindowStore().With("createserverbots", new UiWindowPlacement(200, 120, 10, 10))).Create<CreateServer>();
}
