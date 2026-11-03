using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.Hl2mp.Ui;

/// <summary>The bots page's state for samples, recording what it was asked.</summary>
public sealed class FakeCreateServer : ICreateServerSource, ICreateServerActions
{
    public int MaxPlayers { get; set; } = 8;

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

    public string Refusal { get; set; } = "";

    public string StartRefusalToken(int maxPlayers) => Refusal;

    public List<string> Asked { get; } = [];

    public void SetMaxPlayers(int count) => MaxPlayers = count;

    public void SetBotCount(int count) => BotCount = count;

    public void SetDifficulty(int stop) => Difficulty = stop;

    public void AddCustom() => Asked.Add("add");

    public void EditCustom(int i) => Asked.Add($"edit {i}");

    public void RemoveCustom(int i) => Asked.Add($"remove {i}");

    public bool NewBotsEachMap { get; set; }

    public void SetNewBotsEachMap(bool on) => NewBotsEachMap = on;

    public void BeforeMap(List<string> lines)
    {
        Asked.Add("before map");
        lines.Add("ai_bot_roster \"\"");
    }

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

internal static class BotsPageSamples
{
    /// <summary>The page in the create-server window's frame, on its tab, as the window hands it 8 slots.</summary>
    private static UiScreen Page(UiSampleServices s, FakeCreateServer fake, int maxPlayers = 8, MemoryWindowStore? store = null)
    {
        fake.MaxPlayers = maxPlayers;
        fake.Register(s, store);
        return new CreateServerPagePreview(s.Create<UiWindows>(), s.Create<BotsPage>(), new CreateServerChoices("dm_lockdown", maxPlayers));
    }

    [UiSample("default")]
    private static UiScreen Default(UiSampleServices s) => Page(s, new FakeCreateServer());

    [UiSample("with custom bots")]
    private static UiScreen WithCustom(UiSampleServices s)
        => Page(s, new FakeCreateServer { BotCount = 2, Difficulty = 0, Customs = FakeCreateServer.Three() });

    [UiSample("too many bots")]
    private static UiScreen TooMany(UiSampleServices s)
        => Page(s, new FakeCreateServer { BotCount = 7, Customs = FakeCreateServer.Three(), Refusal = "#Ai_Start_TooMany" });

    [UiSample("full server, hard")]
    private static UiScreen Full(UiSampleServices s)
        => Page(s, new FakeCreateServer { BotCount = 12, Difficulty = 2, Customs = FakeCreateServer.Three() }, maxPlayers: 16);

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Page(s, new FakeCreateServer { BotCount = 2, Customs = FakeCreateServer.Three() },
                store: new MemoryWindowStore().With(CreateServerWindow.Id, new UiWindowPlacement(200, 120, 10, 10)));
}
