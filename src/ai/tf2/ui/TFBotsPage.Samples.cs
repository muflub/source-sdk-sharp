using SourceSharp.Ai.Ui;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.TF2.Ui;

/// <summary>TF2's bots page's state for samples, recording what it was asked: RED and BLU, the Soldier, and names from TF2's pool.</summary>
public sealed class FakeTFBots : IBotSetupSource, IBotSetupActions, IBotClassSource, IBotNamePoolSource
{
    public int MaxPlayers { get; set; } = 24;

    public int BotCount { get; set; } = 6;

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

    public bool TeamsPicked { get; set; }

    public int Teams => 2;

    /// <summary>RED's bots when picked; BLU takes the rest.</summary>
    public int Red { get; set; }

    public int BotsOn(int index) => index == 0 ? Math.Min(Red, BotCount) : BotCount - Math.Min(Red, BotCount);

    public string TeamToken(int index) => index == 0 ? "#TF_Name_Red" : "#TF_Name_Blue";

    public void SetTeamsPicked(bool on) => TeamsPicked = on;

    public void SetBotsOn(int index, int count) => Asked.Add($"team {index} {count}");

    public void BeforeMap(List<string> lines)
    {
        Asked.Add("before map");
        lines.Add("ai_bot_roster \"\"");
    }

    public int ClassCount => 1;

    public string ClassToken(int i) => "#TF_Class_Name_Soldier";

    public int ChosenClass => 0;

    /// <summary>Names drawn from game/mod_tf2/scripts/ai/tf2_bot_names.json as the client draws them.</summary>
    public string[] Names { get; set; } = ["Felix Hawkeye", "Lars Bounty", "Gwen Steady"];

    public int ExampleCount => Names.Length;

    public string Example(int i) => Names[i];

    /// <summary>Three custom bots: a saved one, one TF2's pool names, and Expert's full spend.</summary>
    public static List<CustomBotRow> Three() =>
    [
        new("Rocketeer", 45, 60, "#Ai_Skill_Agility", "#Ai_Skill_Aggression"),
        new("", 35, 60, "#Ai_Skill_Accuracy", "#Ai_Skill_Alertness"),
        new("The_Longest_Profile_Name_Allowed", 60, 60, "#Ai_Skill_Headhunter", "#Ai_Skill_Discipline"),
    ];

    public UiSampleServices Register(UiSampleServices s, MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        return s.Add<IBotSetupSource>(this).Add<IBotSetupActions>(this).Add<IBotClassSource>(this).Add<IBotNamePoolSource>(this);
    }
}

internal static class TFBotsPageSamples
{
    /// <summary>The page in the create-server window's frame, on its tab, as the window hands it 24 slots (TF2's default).</summary>
    private static UiScreen Page(UiSampleServices s, FakeTFBots fake, int maxPlayers = 24, MemoryWindowStore? store = null)
    {
        fake.MaxPlayers = maxPlayers;
        fake.Register(s, store);
        return new CreateServerPagePreview(s.Create<UiWindows>(), s.Create<TFBotsPage>(), new CreateServerChoices("ctf_2fort", maxPlayers));
    }

    [UiSample("default")]
    private static UiScreen Default(UiSampleServices s) => Page(s, new FakeTFBots());

    /// <summary>The teams picked: 4 on RED, 2 on BLU.</summary>
    [UiSample("four red two blu")]
    private static UiScreen Picked(UiSampleServices s)
        => Page(s, new FakeTFBots { TeamsPicked = true, Red = 4 });

    [UiSample("with custom bots")]
    private static UiScreen WithCustom(UiSampleServices s)
        => Page(s, new FakeTFBots { BotCount = 4, Difficulty = 2, TeamsPicked = true, Red = 2, Customs = FakeTFBots.Three() });

    [UiSample("too many bots")]
    private static UiScreen TooMany(UiSampleServices s)
        => Page(s, new FakeTFBots { BotCount = 7, Customs = FakeTFBots.Three(), Refusal = "#Ai_Start_TooMany" }, maxPlayers: 8);

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Page(s, new FakeTFBots { BotCount = 2, Customs = FakeTFBots.Three() },
                store: new MemoryWindowStore().With(CreateServerWindow.Id, new UiWindowPlacement(200, 120, 10, 10)));
}
