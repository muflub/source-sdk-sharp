extern alias client;

using System.Reflection;
using Xunit;

using CreateServerModel = client::SourceSharp.Ai.Hl2mp.Client.Setup.CreateServerModel;
using CustomBot = client::SourceSharp.Ai.Hl2mp.Client.Setup.CustomBot;
using CustomBots = client::SourceSharp.Ai.Hl2mp.Client.Setup.CustomBots;
using IFwdBaseFileSystem = client::SourceSharp.Abi.IFwdBaseFileSystem;
using IFwdEngineClient = client::SourceSharp.Abi.IFwdEngineClient;
using BotEditorModel = client::SourceSharp.Ai.Hl2mp.Client.Setup.BotEditorModel;

namespace SourceSharp.Ai.Tests.Setup;

/// <summary>
/// The create-server screen's model (plan_hl2mp_ai §3.5, §8.2): the
/// difficulty slider's stops, the bot slots, the map check, and the console
/// lines Start sends.
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

    /// <summary>A file system that has the maps it is given.</summary>
    public class Maps : DispatchProxy
    {
        public HashSet<string> Present { get; } = new();

        public List<string> Asked { get; } = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "FileExists")
            {
                Asked.Add($"{args![0]} {args[1]}");
                return Present.Contains((string)args[0]!);
            }

            throw new InvalidOperationException($"asked {method?.Name}");
        }
    }

    private readonly IFwdEngineClient _engine = DispatchProxy.Create<IFwdEngineClient, Lines>();
    private readonly IFwdBaseFileSystem _files = DispatchProxy.Create<IFwdBaseFileSystem, Maps>();
    private readonly CustomBots _customs = new();
    private readonly CreateServerModel _model;

    public CreateServerModelTests()
    {
        ((Maps)(object)_files).Present.Add("maps/dm_lockdown.bsp");
        _model = new CreateServerModel(_engine, _files, _customs, BotEditorModelTests.Editor(_customs));
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

    [Fact]
    public void TwoEasyAndOneCustomStartWithTheExpectedCommands()
    {
        _model.SetBotCount(2);
        _model.SetDifficulty(0);
        _customs.Add(new CustomBot(Profile("Deadeye", 8, 7, 6, 6, 6, 6, 1, 1, 2, 2), []));
        Assert.Equal(["disconnect", "wait 30", "maxplayers 8",
                      "ai_bot_roster \"easy;easy;name=Deadeye:skills=8,7,6,6,6,6,1,1,2,2\"", "map dm_lockdown"], _model.StartCommands());
    }

    [Fact]
    public void StartSendsTheLinesAsOneTextSoTheWaitsDelayWhatFollows()
    {
        _model.SetBotCount(1);
        _model.Start();
        Assert.Equal(["disconnect\nwait 30\nmaxplayers 8\nai_bot_roster \"normal\"\nmap dm_lockdown"], Sent);
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
        Assert.Contains("ai_bot_roster \"\"", _model.StartCommands());
    }

    [Fact]
    public void AMissingMapRefusesStartAndSendsNothing()
    {
        _model.SetMapName("dm_nowhere");
        Assert.Equal((false, "#Ai_Start_NoMap"), (_model.MapFound, _model.StartRefusalToken));
        _model.Start();
        Assert.Empty(Sent);
    }

    [Fact]
    public void AMapNameThatCouldBreakTheLineIsRefusedWithoutAskingTheDisk()
    {
        _model.SetMapName("dm_lockdown;quit");
        Assert.Equal("#Ai_Start_BadMap", _model.StartRefusalToken);
        Assert.Empty(((Maps)(object)_files).Asked);
    }

    [Fact]
    public void TheMapIsAskedForOncePerName()
    {
        _ = _model.MapFound;
        _ = _model.MapFound;
        Assert.Equal(["maps/dm_lockdown.bsp GAME"], ((Maps)(object)_files).Asked);
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
