extern alias client;

using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2;
using TF2.Shared;
using Xunit;

using AiBotRerollOnMapConVar = client::SourceSharp.Ai.Client.Setup.AiBotRerollOnMapConVar;
using BotProfileStore = client::SourceSharp.Ai.Client.Profiles.BotProfileStore;
using ClientBotNames = client::SourceSharp.Ai.Client.Setup.ClientBotNames;
using CreateServerModel = client::SourceSharp.Ai.Client.Setup.CreateServerModel;
using CustomBot = client::SourceSharp.Ai.Client.Setup.CustomBot;
using CustomBots = client::SourceSharp.Ai.Client.Setup.CustomBots;
using IBotSetupGame = client::SourceSharp.Ai.Client.Setup.IBotSetupGame;
using IEngineFiles = client::SourceSharp.Abi.IEngineFiles;
using IFwdEngineClient = client::SourceSharp.Abi.IFwdEngineClient;
using IProfileDisk = client::SourceSharp.Ai.Client.Profiles.IProfileDisk;
using TFAiClient = client::SourceSharp.Ai.TF2.Client.TFAiClient;
using TFBotClasses = client::SourceSharp.Ai.TF2.Client.TFBotClasses;
using TFBotSetup = client::SourceSharp.Ai.TF2.Client.TFBotSetup;

namespace SourceSharp.Ai.Tests.Setup;

/// <summary>
/// TF2's bots page's model (plan_bots_teams_tf2 P6 step 8): the game-neutral create-server
/// model over TF2's answers, the roster line Start runs for a choice and the counts it puts
/// on RED and BLU, read back as the server reads it (RosterSpec, ai_bot_roster), the class
/// the page shows against the one the server's bots pick, the names from TF2's pool, and the
/// profile store's round trip under TF2's composition.
/// </summary>
public class TFBotSetupTests
{
    private readonly IFwdEngineClient _engine = DispatchProxy.Create<IFwdEngineClient, CreateServerModelTests.Lines>();
    private readonly CustomBots _customs = new();
    private readonly CreateServerModel _model;

    public TFBotSetupTests()
    {
        _model = new CreateServerModel(_engine, DeclaredClientConVars.Fresh<AiBotRerollOnMapConVar>(), _customs,
                                       BotEditorModelTests.Editor(_customs), new TFBotSetup());
        _model.SetMaxPlayers(24);
    }

    private List<string> BeforeMap()
    {
        var lines = new List<string>();
        _model.BeforeMap(lines);
        return lines;
    }

    /// <summary>
    /// Six Hard bots, four on RED and two on BLU: the lines Start runs, the roster written with
    /// TF2's team numbers, and before it the unbalance check turned off, since TF2 refuses a
    /// team two ahead at its default (coordinator ruling 2026-11-11, option b).
    /// </summary>
    [Fact]
    public void FourRedTwoBluHardAreTheLinesStartRuns()
    {
        _model.SetBotCount(6);
        _model.SetDifficulty(2);
        _model.SetTeamsPicked(true);
        _model.SetBotsOn(0, 4);
        Assert.Equal(["ai_bot_kick all", "mp_teams_unbalance_limit 0", "ai_bot_roster \"hard:team=2;hard:team=2;hard:team=2;hard:team=2;hard:team=3;hard:team=3\"", "ai_bot_reroll_on_map 0"],
                     BeforeMap());
    }

    /// <summary>Three on RED and two on BLU are one apart, which TF2's default allows: no unbalance line.</summary>
    [Fact]
    public void ASplitOneApartLeavesTheUnbalanceLimit()
    {
        _model.SetBotCount(5);
        _model.SetTeamsPicked(true);
        _model.SetBotsOn(0, 3);
        Assert.Equal((3, 2), (_model.BotsOn(0), _model.BotsOn(1)));
        Assert.DoesNotContain(BeforeMap(), l => l.StartsWith("mp_teams_unbalance_limit", StringComparison.Ordinal));
        Assert.Contains(BeforeMap(), l => l.StartsWith("ai_bot_roster", StringComparison.Ordinal));   // the lines were written
    }

    /// <summary>An even split, and each bot joining the smaller team, leave stock's default.</summary>
    [Fact]
    public void AnEvenSplitOrEvenTeamsLeaveTheUnbalanceLimit()
    {
        _model.SetBotCount(6);
        Assert.DoesNotContain(BeforeMap(), l => l.StartsWith("mp_teams_unbalance_limit", StringComparison.Ordinal));   // each joins the smaller team
        _model.SetTeamsPicked(true);
        Assert.Equal((3, 3), (_model.BotsOn(0), _model.BotsOn(1)));
        Assert.DoesNotContain(BeforeMap(), l => l.StartsWith("mp_teams_unbalance_limit", StringComparison.Ordinal));
        _model.SetBotsOn(0, 6);   // the control: six against none is uneven
        Assert.Contains("mp_teams_unbalance_limit 0", BeforeMap());
    }

    /// <summary>Even: no team is written, so each bot joins as TF2's auto-assign puts a player.</summary>
    [Fact]
    public void EvenTeamsWriteNoTeam()
    {
        _model.SetBotCount(5);
        _model.SetDifficulty(0);
        Assert.Equal("easy;easy;easy;easy;easy", _model.RosterSpecText());
    }

    /// <summary>The per-team counts: two teams, picking starts even (the odd bot on RED), and BLU takes what RED leaves.</summary>
    [Fact]
    public void PickingSplitsTheBotsBetweenRedAndBlu()
    {
        _model.SetBotCount(7);
        _model.SetTeamsPicked(true);
        Assert.Equal((2, 4, 3), (_model.Teams, _model.BotsOn(0), _model.BotsOn(1)));
        _model.SetBotsOn(0, 1);
        Assert.Equal((1, 6), (_model.BotsOn(0), _model.BotsOn(1)));
        Assert.Equal(0, _model.BotsOn(2));   // no third team in TF2
    }

    /// <summary>A custom bot rides after the preset ones with its skills, on no team of its own.</summary>
    [Fact]
    public void ACustomBotFollowsThePresetOnes()
    {
        _model.SetBotCount(1);
        _customs.Add(new CustomBot(new SkillProfile("Rocketeer", StandardSkills.Ten, [8, 7, 6, 6, 6, 6, 1, 1, 2, 2]), []));
        Assert.Equal("normal;name=Rocketeer:skills=8,7,6,6,6,6,1,1,2,2", _model.RosterSpecText());
    }

    /// <summary>The server reads the page's line as the page meant it: six entries, four for TF2's RED and two for its BLU, each a preset.</summary>
    [Fact]
    public void TheServerReadsTheLineIntoRedAndBluBots()
    {
        _model.SetBotCount(6);
        _model.SetDifficulty(2);
        _model.SetTeamsPicked(true);
        _model.SetBotsOn(0, 4);
        var refusals = new List<string>();
        var entries = RosterSpec.Parse(_model.RosterSpecText(), refusals);
        Assert.Empty(refusals);
        Assert.Equal((4, 2), (entries.Count(e => e.Team == (int)TFTeam.Red), entries.Count(e => e.Team == (int)TFTeam.Blue)));
        Assert.All(entries, e => Assert.True(SkillPresets.TryGet(e.Skills, out _), e.Skills));
    }

    /// <summary>TF2's teams are the mod's RED and BLU, named as its HUD names them.</summary>
    [Fact]
    public void TheTeamsAreTF2sRedAndBlu()
    {
        IBotSetupGame tf2 = new TFBotSetup();
        Assert.Equal((2, (int)TFTeam.Red, (int)TFTeam.Blue), (tf2.Teams, CreateServerModel.FirstTeam, CreateServerModel.FirstTeam + 1));
        Assert.Equal(["#TF_Name_Red", "#TF_Name_Blue"], [_model.TeamToken(0), _model.TeamToken(1)]);
    }

    /// <summary>The client reads the files the server's TF2 binding reads.</summary>
    [Fact]
    public void TheFilesAreTheServersTF2Files()
    {
        IBotSetupGame tf2 = new TFBotSetup();
        Assert.Equal((TFSkillCurves.Path, TFGameFileBotNames.Path), (tf2.SkillCurvesPath, tf2.BotNamesPath));
    }

    /// <summary>The page offers exactly the class the server's bots pick (TFBotClassPick), lit: no class that does not play.</summary>
    [Fact]
    public void ThePageShowsTheClassTheServersBotsPick()
    {
        var classes = new TFBotClasses();
        Assert.Equal([TFClassNames.Raw(TFBotClassPick.Class)], classes.Playable.Select(c => c.Raw));
        Assert.Equal((1, "#TF_Class_Name_Soldier", 0), (classes.ClassCount, classes.ClassToken(0), classes.ChosenClass));
    }

    /// <summary>TF2's game files as the client reads them: the committed bot names and curves, nothing else.</summary>
    public class TF2Files : DispatchProxy
    {
        public List<string> Asked { get; } = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "ReadGameFile")
                throw new InvalidOperationException($"asked {method?.Name}");
            string path = (string)args![0]!;
            Asked.Add(path);
            string file = SourceSharp.Tests.RepoLayout.Repo("game", "mod_tf2", path);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
    }

    /// <summary>The page's names come from TF2's pool: three, distinct, each a first name and a word of TF2's file, the file read once.</summary>
    [Fact]
    public void TheNamesAreDrawnFromTF2sPool()
    {
        var files = DispatchProxy.Create<IEngineFiles, TF2Files>();
        var names = new ClientBotNames(files, new TFBotSetup());
        BotNamePools pool = BotNamePools.Parse(File.ReadAllText(SourceSharp.Tests.RepoLayout.Repo("game", "mod_tf2", "scripts", "ai", "tf2_bot_names.json")));
        string[] drawn = [.. Enumerable.Range(0, names.ExampleCount).Select(names.Example)];
        Assert.Equal(ClientBotNames.ExamplesPerPage, drawn.Length);
        Assert.Equal(drawn.Length, drawn.Distinct().Count());
        Assert.All(drawn, n => Assert.Contains(pool.First, f => n.StartsWith(f + " ", StringComparison.Ordinal)));
        Assert.All(drawn, n => Assert.Contains(pool.Traits.Values.SelectMany(t => t.High.Concat(t.Low)), d => n.EndsWith(" " + d, StringComparison.Ordinal)));
        Assert.Equal(ClientBotNames.Draw(pool, ClientBotNames.ExamplePreset), drawn);
        Assert.Equal([TFGameFileBotNames.Path], ((TF2Files)(object)files).Asked);
    }

    /// <summary>The control: without TF2's file there are no names, never another game's.</summary>
    [Fact]
    public void NoPoolNoNames()
        => Assert.Equal(0, new ClientBotNames(DispatchProxy.Create<IEngineFiles, BotEditorModelTests.GameFiles>(), new TFBotSetup()).ExampleCount);

    /// <summary>The profile store as TF2's container builds it (over a memory disk in the mod disk's place) saves a custom bot under cfg/ai/bots and loads it back the same.</summary>
    [Fact]
    public void TheProfileStoreRoundTripsUnderTF2sComposition()
    {
        var services = new client::SourceSharp.Abi.ServiceContainer();
        var registered = client::SourceSharp.Abi.SideServices.Register(services, Side.Client,
            [.. typeof(CreateServerModel).Assembly.GetTypes(), .. typeof(TFBotSetup).Assembly.GetTypes()]);
        Assert.Contains(typeof(TFBotSetup), registered);
        Assert.Contains(typeof(TFAiClient), registered);
        Assert.True(services.IsRegistered(typeof(IBotSetupGame)));
        Assert.True(services.IsRegistered(typeof(SourceSharp.Ai.Ui.IBotClassSource)));
        Assert.True(services.IsRegistered(typeof(SourceSharp.Ai.Ui.IBotNamePoolSource)));
        Assert.True(services.IsRegistered(typeof(IProfileDisk)));

        // The engine's file system stays out: the disk is a memory one, put where TF2's ModProfileDisk was.
        var disk = new BotProfileStoreTests.MemoryDisk();
        services.AddInstance<IProfileDisk>(disk);
        var store = (BotProfileStore)services.Resolve(typeof(BotProfileStore))!;
        var profile = StandardSkills.WithTeamwork(new SkillProfile("Rocketeer", StandardSkills.Ten, [8, 7, 6, 6, 6, 6, 1, 1, 2, 2]));
        Assert.True(store.Save(profile, []).Accepted);
        Assert.Equal(["cfg/ai/bots/Rocketeer.json"], disk.Files.Keys);
        Assert.True(store.Load("Rocketeer", out BotProfileDocument? doc).Accepted);
        Assert.Equal(profile.Levels.ToArray(), doc!.Profile.Levels.ToArray());
        Assert.Equal(["Rocketeer"], store.SavedNames());
    }
}
