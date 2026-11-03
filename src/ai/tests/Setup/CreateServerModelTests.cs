extern alias client;

using System.Reflection;
using Xunit;

using CreateServerModel = client::SourceSharp.Ai.Hl2mp.Client.Setup.CreateServerModel;
using CustomBot = client::SourceSharp.Ai.Hl2mp.Client.Setup.CustomBot;
using CustomBots = client::SourceSharp.Ai.Hl2mp.Client.Setup.CustomBots;
using IFwdEngineClient = client::SourceSharp.Abi.IFwdEngineClient;
using BotEditorModel = client::SourceSharp.Ai.Hl2mp.Client.Setup.BotEditorModel;
using IEngineConsole = client::SourceSharp.Abi.IEngineConsole;

namespace SourceSharp.Ai.Tests.Setup;

/// <summary>
/// The bots page's model (plan_hl2mp_ai §3.5, §8.2): the difficulty
/// slider's stops, the bot slots within the window's player count, and the
/// roster line Start runs (the core window runs it after maxplayers and
/// before the map: CreateServerTests holds that half).
/// </summary>
public class CreateServerModelTests
{
    /// <summary>An engine client that records each console line and answers nothing else.</summary>
    public class Lines : DispatchProxy
    {
        public List<string> Sent { get; } = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "SubmitConsoleLine")
            {
                Sent.Add((string)args![0]!);
                return null;
            }

            throw new InvalidOperationException($"asked {method?.Name}");
        }
    }

    /// <summary>A console that holds what a fact gives it.</summary>
    public class ConVars : DispatchProxy
    {
        public Dictionary<string, string> Values { get; } = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == "FindConVarValue" && Values.TryGetValue((string)args![0]!, out string? v) ? v
               : method?.Name == "FindConVarValue" ? null
               : throw new InvalidOperationException($"asked {method?.Name}");
    }

    private readonly IFwdEngineClient _engine = DispatchProxy.Create<IFwdEngineClient, Lines>();
    private readonly IEngineConsole _console = DispatchProxy.Create<IEngineConsole, ConVars>();
    private readonly CustomBots _customs = new();
    private readonly CreateServerModel _model;

    public CreateServerModelTests()
    {
        _model = new CreateServerModel(_engine, _console, _customs, BotEditorModelTests.Editor(_customs));
    }

    private List<string> Sent => ((Lines)(object)_engine).Sent;

    private static SkillProfile Profile(string name, params int[] levels) => new(name, StandardSkills.Ten, [.. levels]);

    [Fact]
    public void TheSlidersStopsAreExactlyEasyMediumHard()
    {
        string[] stops = [.. Enumerable.Range(0, _model.DifficultyStops).Select(CreateServerModel.StopPreset)];
        Assert.Equal(["easy", "normal", "hard"], stops);
    }

    [Fact]
    public void TheStopsSpendThePresetsAllocations()
    {
        SkillPresets.TryGet("easy", out SkillProfile? easy);
        SkillPresets.TryGet("normal", out SkillProfile? normal);
        SkillPresets.TryGet("hard", out SkillProfile? hard);
        Assert.Equal([easy!.Spent, normal!.Spent, hard!.Spent], Enumerable.Range(0, 3).Select(_model.DifficultySpent));
        Assert.Equal([20, 35, 50], Enumerable.Range(0, 3).Select(_model.DifficultySpent));
    }

    [Fact]
    public void ExpertIsNotAStop()
    {
        _model.SetDifficulty(99);
        Assert.Equal("hard", CreateServerModel.StopPreset(_model.Difficulty));
        Assert.DoesNotContain("expert", Enumerable.Range(0, _model.DifficultyStops).Select(CreateServerModel.StopPreset));
    }

    private List<string> BeforeMap()
    {
        var lines = new List<string>();
        _model.BeforeMap(lines);
        return lines;
    }

    [Fact]
    public void TwoEasyAndOneCustomStartWithTheExpectedRoster()
    {
        _model.SetBotCount(2);
        _model.SetDifficulty(0);
        _customs.Add(new CustomBot(Profile("Deadeye", 8, 7, 6, 6, 6, 6, 1, 1, 2, 2), []));
        Assert.Equal(["ai_bot_kick all", "ai_bot_roster \"easy;easy;name=Deadeye:skills=8,7,6,6,6,6,1,1,2,2\"", "ai_bot_reroll_on_map 0"], BeforeMap());
        Assert.Empty(Sent);   // the window sends it, with its own lines
    }

    [Fact]
    public void AnUnnamedCustomBotIsLeftToTheServerToName()
    {
        _model.SetBotCount(0);
        _customs.Add(new CustomBot(Profile("", 4, 4, 4, 3, 3, 3, 4, 3, 4, 3), []));
        Assert.Equal("skills=4,4,4,3,3,3,4,3,4,3", _model.RosterSpecText());
    }

    [Fact]
    public void NoBotsStillClearsTheRoster()
    {
        _model.SetBotCount(0);
        Assert.Equal(["ai_bot_kick all", "ai_bot_roster \"\"", "ai_bot_reroll_on_map 0"], BeforeMap());
    }

    /// <summary>The box starts as the server's archived cvar stands, and Start says what the box says.</summary>
    [Fact]
    public void NewBotsEachMapStartsFromTheCvarAndStartSetsIt()
    {
        ((ConVars)(object)_console).Values[CreateServerModel.RerollConVar] = "1";
        Assert.True(_model.NewBotsEachMap);
        Assert.Contains("ai_bot_reroll_on_map 1", BeforeMap());
        _model.SetNewBotsEachMap(false);
        Assert.False(_model.NewBotsEachMap);
        Assert.Contains("ai_bot_reroll_on_map 0", BeforeMap());
    }

    /// <summary>The window's size bounds the bots: more than its slots less the player's refuse Start, and the page adds no line.</summary>
    [Fact]
    public void MoreBotsThanTheWindowsSlotsRefuseStart()
    {
        _model.SetMaxPlayers(8);
        _model.SetBotCount(5);
        _customs.Add(new CustomBot(Profile("", 4, 4, 4, 3, 3, 3, 4, 3, 4, 3), []));
        _customs.Add(new CustomBot(Profile("", 4, 4, 4, 3, 3, 3, 4, 3, 4, 3), []));
        Assert.Equal("", _model.StartRefusalToken(8));   // the control: 7 bots in 8 slots
        Assert.Equal("#Ai_Start_TooMany", _model.StartRefusalToken(7));
        _model.SetMaxPlayers(2);   // the presets clamp to none; two custom bots in one slot still refuse
        Assert.Equal("#Ai_Start_TooMany", _model.StartRefusalToken(2));
        Assert.Empty(BeforeMap());
    }

    [Fact]
    public void BotsLeaveTheLocalPlayersSlot()
    {
        _model.SetMaxPlayers(4);
        _model.SetBotCount(10);
        Assert.Equal(3, _model.BotCount);
    }

    [Fact]
    public void CustomBotsTakeSlotsFromThePresetBots()
    {
        _model.SetMaxPlayers(4);
        _model.SetBotCount(3);
        _customs.Add(new CustomBot(Profile("", 4, 4, 4, 3, 3, 3, 4, 3, 4, 3), []));
        Assert.Equal((2, 1, 2), (_model.BotCount, _model.CustomCount, _model.BotCountMax));
    }

    [Fact]
    public void AddCustomOpensTheEditorOnANewBot()
    {
        _customs.Add(new CustomBot(Profile("Deadeye", 8, 7, 6, 6, 6, 6, 1, 1, 2, 2), []));
        _model.AddCustom();
        Assert.Equal(-1, _customs.Editing);
        Assert.Equal(["cs_ui_open boteditor"], Sent);
    }

    [Fact]
    public void EditCustomOpensTheEditorOnThatBot()
    {
        _customs.Add(new CustomBot(Profile("Deadeye", 8, 7, 6, 6, 6, 6, 1, 1, 2, 2), []));
        _model.EditCustom(0);
        Assert.Equal(0, _customs.Editing);
        Assert.Equal(["cs_ui_open boteditor"], Sent);
    }

    [Fact]
    public void AFullServerAddsNoCustomBot()
    {
        _model.SetMaxPlayers(2);
        _customs.Add(new CustomBot(Profile("", 4, 4, 4, 3, 3, 3, 4, 3, 4, 3), []));
        _model.AddCustom();
        Assert.Empty(Sent);
    }

    [Fact]
    public void LoweringThePlayersClampsTheBots()
    {
        _model.SetBotCount(7);
        _model.SetMaxPlayers(3);
        Assert.Equal(2, _model.BotCount);
    }

    [Fact]
    public void ACustomRowShowsItsSpendAndStyle()
    {
        _customs.Add(new CustomBot(Profile("Deadeye", 8, 7, 6, 6, 6, 6, 1, 1, 2, 2), []));
        var row = _model.Custom(0);
        Assert.Equal(("Deadeye", 45, 60, "#Ai_Skill_Accuracy", "#Ai_Skill_Alertness"),
                     (row.Name, row.Spent, row.Pool, row.StyleFirstToken, row.StyleSecondToken));
    }
}
