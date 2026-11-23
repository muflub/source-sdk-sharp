using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Teams;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// <c>ai_bot_add</c>, <c>ai_bot_kick</c> and <c>ai_bot_count</c> (plan_hl2mp_ai
/// §3.1.4, §3.5, Phase 3 step 4): the preset and <c>skills=</c> forms, and the
/// server's own pool judgement: an overspent <c>skills=</c> prints a refusal
/// naming the sum and adds no bot, whatever a client's screen did.
/// </summary>
public class BotCommandsTests
{
    private sealed class Lines : ILog
    {
        public readonly List<string> All = new();
        public void Msg(string message) => All.Add(message);
        public void Warning(string message) => All.Add(message);
    }

    /// <summary>The core's bots, as an engine with free slots answers them.</summary>
    private sealed class Players : IBotPlayers
    {
        public readonly List<BotPlayer> Made = new();
        public readonly List<(string Name, KeyValuePair<string, string>[] UserInfo)> Asked = new();
        public readonly List<EntityHandle> Kicked = new();

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            Asked.Add((name, userInfo.ToArray()));
            int index = Made.Count + 2;
            var player = new EntityHandle((uint)(0x0001_0000 * index + index));
            Made.Add(new BotPlayer(player, index, name));
            refusal = "";
            return player;
        }

        public bool IsBot(EntityHandle player) => Made.Any(b => b.Player == player);

        public bool Kick(EntityHandle bot, string reason)
        {
            Kicked.Add(bot);
            return Made.RemoveAll(b => b.Player == bot) > 0;
        }

        public IReadOnlyList<BotPlayer> Bots => Made;
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public void Queue(EntityHandle player, in UserCmd cmd) { }
        public int RunQueued(EntityHandle player) => 0;
    }

    /// <summary>/ai/bots, written by hand (System.Text.Json kept the server's load context from unloading): the same fields, a name quoted safely.</summary>
    [Fact]
    public void TheBotsListIsJsonWithEachBotsFields()
    {
        Add("Alyx hard");
        Add("Bo\"b skills=8,7,6,6,6,6,7,5,5,4");
        using var doc = System.Text.Json.JsonDocument.Parse(SourceSharp.Ai.Server.Senses.AiBotRoutes.BotsJson(_roster.Bots));
        var rows = doc.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(("Alyx", "hard", 50, _bots.Bots[0].Id.Slot, _bots.Bots[0].Id.Serial),
                     (rows[0].GetProperty("name").GetString(), rows[0].GetProperty("profile").GetString(), rows[0].GetProperty("spent").GetInt32(),
                      rows[0].GetProperty("slot").GetInt32(), rows[0].GetProperty("serial").GetInt32()));
        Assert.Equal(_bots.Bots[1].Name, rows[1].GetProperty("name").GetString());
        // The drawn levels, the tag, the style and the state: what the inspector shows.
        Assert.Equal(_bots.Bots[0].Skills.Levels, rows[0].GetProperty("levels").EnumerateArray().Select(l => l.GetInt32()));
        Assert.Equal(("(Hard)", "active", _bots.Bots[0].Skills.StyleName),
                     (rows[0].GetProperty("tag").GetString(), rows[0].GetProperty("state").GetString(), rows[0].GetProperty("style").GetString()));
    }

    /// <summary>An engine interface that answers every call with its type's default.</summary>
    public class Defaults : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null;
    }

    /// <summary>
    /// A bot that leaves takes its scripted act (and walk and goal) with it.
    /// Before, they stayed and were submitted every tick for an actor that was
    /// gone: the live gate's second pass refused 82 intents as a stale handle
    /// and 6 as no bot, the ss_ai_nav walker's goal outliving its map.
    /// </summary>
    [Fact]
    public void ABotThatLeavesTakesItsScriptsWithIt()
    {
        var bots = new AiBots(_players, new Runner(), DispatchProxy.Create<IEnginePlayers, Defaults>(),
                                 DispatchProxy.Create<IEngineEntities, Defaults>(), WeaponCatalog.Stock(),
                                 DispatchProxy.Create<IEngineFrame, Defaults>(), _nav, TestGames.Hl2mp(), DispatchProxy.Create<IBotInventory, Defaults>(),
                                 new DataFileNames(), _tags, _log);
        new AiBotAddCommand(new AiRoster(bots, new NoProfiles(), new BotRosterTests.Slots(), _log, new FakeRosterTeams()), _log, new RecordingNotices()).RunLine("Alyx hard");
        Assert.Null(bots.Act("Alyx", ActionKind.Fire, 5f, 0));
        bots.OnPreThink(0.015f);
        Assert.Equal(0L, bots.Applier.Refused(IntentRefusal.UnknownBot) + bots.Applier.Refused(IntentRefusal.StaleSerial));

        _players.Made.Clear();   // the engine dropped it (a level change, a kick)
        for (int i = 0; i < 5; i++)
            bots.OnPreThink(0.015f);
        Assert.Empty(bots.Bots);
        Assert.Equal(0L, bots.Applier.Refused(IntentRefusal.UnknownBot) + bots.Applier.Refused(IntentRefusal.StaleSerial));
    }

    /// <summary>An engine interface the commands must not reach: any call is a failure.</summary>
    public class Unasked : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => throw new InvalidOperationException($"asked {method?.DeclaringType?.Name}.{method?.Name}");
    }

    /// <summary>A level's navigation as the bots ask it: loaded, or refused with a line.</summary>
    private sealed class Navigation : IAiNavigation
    {
        public string? Refusal { get; set; }

        public string? MissingFile { get; set; }

        public bool Building { get; set; }

        public LoadedNav? Current => null;

        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(SourceSharp.Abi.EntityHandle player) => Obstacles;

        public NavSight? Sight => null;


        public ChokeSlot? Chokes => null;
    }

    private readonly Navigation _nav = new();
    private readonly ScoreboardTags _tags = new();

    private sealed class NoProfiles : IBotProfileSource
    {
        public byte[]? Read(string path) => null;
    }

    /// <summary>The pools from the committed data file, as the game reads it.</summary>
    private sealed class DataFileNames : IBotNameSource
    {
        public BotNamePools? Pools { get; } = BotNamePools.Parse(File.ReadAllText(
            SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_bot_names.json")));

        public string Refusal => "";
    }
    private readonly Lines _log = new();
    private readonly RecordingNotices _notices = new();
    private readonly Players _players = new();
    private readonly AiBots _bots;
    private readonly AiRoster _roster;
    private readonly FakeRosterTeams _teams = new();
    private readonly AiBotOrdersConVar _orders = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>())).Declare<AiBotOrdersConVar>();

    public BotCommandsTests()
    {
        _bots = new AiBots(_players, new Runner(), DispatchProxy.Create<IEnginePlayers, Unasked>(),
                              DispatchProxy.Create<IEngineEntities, Unasked>(), WeaponCatalog.Stock(),
                              DispatchProxy.Create<IEngineFrame, Unasked>(), _nav, TestGames.Hl2mp(), DispatchProxy.Create<IBotInventory, Unasked>(),
                              new DataFileNames(), _tags, _log);
        _roster = new AiRoster(_bots, new NoProfiles(), new BotRosterTests.Slots(), _log, _teams);
    }

    private void Add(string args) => new AiBotAddCommand(_roster, _log, _notices).RunLine(args, _log.Msg);

    [Fact]
    public void APresetAddsABotWithThatPresetsSkills()
    {
        Add("Alyx hard");

        AiBot bot = Assert.Single(_bots.Bots);
        Assert.Equal("Alyx", bot.Name);
        Assert.Equal("hard", bot.Skills.Name);
        Assert.Equal(50, bot.Skills.Spent);
    }

    [Fact]
    public void NoSkillsIsNormal()
    {
        Add("Alyx");

        Assert.Equal("normal", Assert.Single(_bots.Bots).Skills.Name);
    }

    [Fact]
    public void AnExactSpendIsAdded()
    {
        Add("Deadeye skills=8,7,6,6,6,6,7,5,5,4");

        AiBot bot = Assert.Single(_bots.Bots);
        Assert.Equal(60, bot.Skills.Spent);
        Assert.Equal(8, bot.Skills["accuracy"]);
    }

    [Fact]
    public void AnOverspentSkillsListIsRefusedOnTheServerAndAddsNoBot()
    {
        Add("Cheater skills=10,10,10,10,10,10,10,10,10,10");

        Assert.Empty(_players.Asked);
        Assert.Empty(_bots.Bots);
        string line = Assert.Single(_log.All);
        Assert.StartsWith("[SourceSharp/Ai] ai_bot_add Cheater refused:", line, StringComparison.Ordinal);
        Assert.Contains("spends 100 of a 60-point pool", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ALevelAboveTheCapIsRefusedNamingTheSkill()
    {
        Add("Cheater skills=11,0,0,0,0,0,0,0,0,0");

        Assert.Empty(_players.Asked);
        Assert.Contains("accuracy is 11, above the cap of 10", Assert.Single(_log.All), StringComparison.Ordinal);
    }

    [Fact]
    public void AddCompletesThePresets()
        => Assert.Equal(["ai_bot_add hard"], new AiBotAddCommand(_roster, _log, _notices).SuggestFor("ha"));

    [Fact]
    public void KickCompletesTheRosterAndAll()
    {
        Add("Alyx hard");

        Assert.Equal(["ai_bot_kick all", "ai_bot_kick Alyx"], new AiBotKickCommand(_roster, _log).SuggestFor(""));
    }

    [Theory]
    [InlineData("Alyx 2")]
    [InlineData("\"Alyx 2\"")]
    public void KickTakesASecondBotsNameWithItsSpaceQuotedOrNot(string line)
    {
        Add("Alyx hard");
        Add("Alyx hard");

        new AiBotKickCommand(_roster, _log).RunLine(line);

        Assert.Contains(_log.All, l => l.Contains("ai_bot_kick Alyx 2: 1 kicked", StringComparison.Ordinal));
    }

    [Fact]
    public void AddTakesAtMostANameASpecAndATeam()
    {
        new AiBotAddCommand(_roster, _log, _notices).RunLine("Alyx hard team=2 extra");

        Assert.Contains(_log.All, l => l.Contains("'extra' is one argument too many", StringComparison.Ordinal));
        Assert.Empty(_players.Asked);
    }

    /// <summary>A game's options (P7): the keys it reads, wherever they stand.</summary>
    private sealed class Options : IBotOptions
    {
        public IReadOnlyCollection<string> Keys { get; } = ["class"];
        public List<RosterBot> Joined { get; } = [];

        public string? Check(IReadOnlyDictionary<string, string> options) => options["class"] == "tank" ? "'tank' is no class" : null;

        void IBotOptions.Joined(RosterBot bot) => Joined.Add(bot);
    }

    /// <summary>A game's own key=value, anywhere on the line, reaches the bot's roster entry, and the game hears the bot join with it.</summary>
    [Fact]
    public void AGamesOptionRidesTheBotIntoTheGame()
    {
        var options = new Options();
        var roster = new AiRoster(_bots, new NoProfiles(), new BotRosterTests.Slots(), _log, _teams, options);
        new AiBotAddCommand(roster, _log, _notices, options).RunLine("class=medic Alyx hard team=2");

        RosterBot joined = Assert.Single(options.Joined);
        Assert.Equal(("Alyx", "medic"), (joined.Name, joined.Options["class"]));
    }

    /// <summary>A key the game does not read is refused, naming it and the keys there are; with no game reading any, so is class=.</summary>
    [Fact]
    public void AnOptionNoGameReadsIsRefused()
    {
        new AiBotAddCommand(_roster, _log, _notices, new Options()).RunLine("Alyx hard colour=red");
        new AiBotAddCommand(_roster, _log, _notices).RunLine("Gordon hard class=medic");

        Assert.Contains(_log.All, l => l.Contains("'colour=red' is no option (team=, class=)", StringComparison.Ordinal));
        Assert.Contains(_log.All, l => l.Contains("'class=medic' is no option (team=)", StringComparison.Ordinal));
        Assert.Empty(_players.Asked);
    }

    /// <summary>The game's check refuses a value it does not take, and nobody is added.</summary>
    [Fact]
    public void AnOptionTheGameRefusesAddsNobody()
    {
        var options = new Options();
        var roster = new AiRoster(_bots, new NoProfiles(), new BotRosterTests.Slots(), _log, _teams, options);
        new AiBotAddCommand(roster, _log, _notices, options).RunLine("Alyx hard class=tank");

        Assert.Contains(_log.All, l => l.Contains("'tank' is no class", StringComparison.Ordinal));
        Assert.Empty(options.Joined);
    }

    /// <summary>A third word that is no team (plan_bots_teams_tf2 B.6) is refused, naming it, and adds nobody.</summary>
    [Fact]
    public void AThirdWordThatIsNoTeamIsRefused()
    {
        new AiBotAddCommand(_roster, _log, _notices).RunLine("Alyx hard extra");

        Assert.Contains(_log.All, l => l.Contains("team 'extra' is not auto or 2 to 5", StringComparison.Ordinal));
        Assert.Empty(_players.Asked);
    }

    [Fact]
    public void AnUnknownPresetIsRefused()
    {
        Add("Alyx godlike");

        Assert.Empty(_players.Asked);
        Assert.Contains("'godlike' is not a preset", Assert.Single(_log.All), StringComparison.Ordinal);
    }

    [Fact]
    public void ABotJoinsWithAStockModelAndTheDefaultWeapon()
    {
        Add("Alyx easy");

        KeyValuePair<string, string>[] info = Assert.Single(_players.Asked).UserInfo;
        string model = info.Single(p => p.Key == "cl_playermodel").Value;
        Assert.Contains(model, Enumerable.Range(0, BotAppearance.ModelCount).Select(BotAppearance.Model));
        Assert.Equal(BotAppearance.DefaultWeapon, info.Single(p => p.Key == "cl_defaultweapon").Value);
    }

    /// <summary>The engine a <see cref="HostSay"/> reaches: every call its type's default, an enumerable empty.</summary>
    public class Quiet : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Type? t = method?.ReturnType;
            if (t is { IsGenericType: true } && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return Array.CreateInstance(t.GetGenericArguments()[0], 0);
            return t is { IsValueType: true } && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }

    /// <summary>The game's chat hook: every line a player said, as the say path hands it on.</summary>
    private sealed class HeardChat : IPlayerChatHook, IHooks<IPlayerChatHook>
    {
        public readonly List<PlayerChat> Lines = new();
        public IReadOnlyList<IPlayerChatHook> All => [this];
        public void Each(string what, Action<IPlayerChatHook> call) => call(this);
        public void OnPlayerChat(PlayerChat chat) => Lines.Add(chat);
    }

    private static T Quietly<T>() where T : class => DispatchProxy.Create<T, Quiet>();

    /// <summary>User messages to nobody (a span argument no proxy can take).</summary>
    private sealed class NoMessages : IUserMessageSender
    {
        public void Send(string name, ReadOnlySpan<byte> payload, int player, bool reliable) { }
    }

    private HostSay SayPath(HeardChat heard)
        => new(_log, Quietly<IEnginePlayers>(), Quietly<IEngineEntities>(), Quietly<IEntityDirectory>(), new NoMessages(),
               Quietly<IConnectedClients>(), Quietly<IEngineGameEvents>(), Quietly<IEngineHost>(), chatHooks: heard);

    [Theory]
    [InlineData("team", true)]
    [InlineData("all", false)]
    public void ABotSaysItsLineThroughThePlayersSayPathToTheAudienceNamed(string to, bool teamOnly)
    {
        Add("Alyx");
        var heard = new HeardChat();

        new AiBotSayCommand(_bots, SayPath(heard), _log).RunLine($"alyx {to} hold the door");

        Assert.Equal(new PlayerChat(new EntityHandle(0x0002_0002u), "hold the door", teamOnly), Assert.Single(heard.Lines));
    }

    [Fact]
    public void ASayForNoSuchBotIsRefusedAndNothingIsSaid()
    {
        Add("Alyx");
        var heard = new HeardChat();

        new AiBotSayCommand(_bots, SayPath(heard), _log).RunLine("barney team hold the door");

        Assert.Empty(heard.Lines);
        Assert.Contains("[SourceSharp/Ai] ai_bot_say refused: no bot called 'barney'\n", _log.All);
    }

    [Fact]
    public void KickRemovesTheNamedBotOrAll()
    {
        Add("Alyx");
        Add("Barney");
        Add("Gordon");

        new AiBotKickCommand(_roster, _log).RunLine("barney");
        Assert.Equal([new EntityHandle(0x0003_0003u)], _players.Kicked);

        new AiBotKickCommand(_roster, _log).RunLine("all");
        Assert.Empty(_players.Made);
        Assert.Contains("[SourceSharp/Ai] ai_bot_kick all: 2 kicked\n", _log.All);
    }

    [Fact]
    public void CountListsTheBots()
    {
        Add("Alyx easy");
        Add("Barney expert");
        _log.All.Clear();

        new AiBotCountCommand(_roster, _log).Execute();

        Assert.Equal("[SourceSharp/Ai] bots: 2 active, 0 benched of 2 wanted (1 human(s), 8 slots, 1 kept free)\n", _log.All[0]);
        Assert.StartsWith("[SourceSharp/Ai]   Alyx: client 2, (Easy) easy (20 points, ", _log.All[1], StringComparison.Ordinal);
        Assert.Contains(string.Join(",", _bots.Bots[0].Skills.Levels), _log.All[1], StringComparison.Ordinal);
        Assert.StartsWith("[SourceSharp/Ai]   Barney: client 3, (Expert) expert (60 points, ", _log.All[2], StringComparison.Ordinal);
        Assert.Equal(3, _log.All.Count);
    }

    [Fact]
    public void WithNoNavigationNoBotIsAddedAndTheRefusalIsSaid()
    {
        _nav.Refusal = "no navigation for dm_lockdown: expected maps/dm_lockdown.nav3d";

        Add("Alyx hard");

        Assert.Empty(_bots.Bots);
        Assert.Empty(_players.Asked);
        Assert.Contains("[SourceSharp/Ai] ai_bot_add Alyx refused: no navigation for dm_lockdown: expected maps/dm_lockdown.nav3d\n", _log.All);
    }

    [Fact]
    public void ARefusedAddTellsThePlayerWhoTypedItWhy()
    {
        _nav.Refusal = "no navigation for dm_lockdown: expected maps/dm_lockdown.nav3d";
        _notices.Issuer = 3;

        Add("Alyx hard");

        Assert.Equal((3, "ai_bot_add Alyx refused: no navigation for dm_lockdown: expected maps/dm_lockdown.nav3d"), Assert.Single(_notices.Told));
    }

    [Fact]
    public void ABadTeamIsToldToThePlayerWhoTypedIt()
    {
        _notices.Issuer = 2;

        Add("Alyx hard team=9");

        var (slot, line) = Assert.Single(_notices.Told);
        Assert.Equal(2, slot);
        Assert.StartsWith("ai_bot_add refused: ", line, StringComparison.Ordinal);
        Assert.Empty(_bots.Bots);
    }

    [Fact]
    public void AnAddThatWorksTellsNobodyAnything()
    {
        _notices.Issuer = 3;

        Add("Alyx hard");

        Assert.Single(_bots.Bots);
        Assert.Empty(_notices.Told);
    }

    private AiAdminMenu AdminMenu() => new(_roster, _nav, _teams, _orders);

    private static List<AdminNode> BotsFolder(AiAdminMenu menu) => [.. Assert.Single(menu.AdminItems(1)).Children!];

    [Fact]
    public void WithoutTeamsTheBotsFolderHasNoTeamAddsAndNoOrders()
        => Assert.Equal(["#SS_UI_Admin_BotAdd", "#SS_UI_Admin_BotReroll", "#SS_UI_Admin_BotRemoveAll"], BotsFolder(AdminMenu()).Select(n => n.Label));

    [Fact]
    public void WithTeamsABotIsAddedToTheTeamChosen()
    {
        _teams.Teamplay = true;
        _teams.Teams = 3;
        var menu = AdminMenu();
        var byTeam = Assert.Single(BotsFolder(menu), n => n.Label == "#SS_UI_Admin_BotTeam").Children!;

        Assert.Equal(["#SS_UI_Team_2", "#SS_UI_Team_3", "#SS_UI_Team_4"], byTeam.Select(n => n.Label));
        Assert.Null(AdminNode.Find(menu.AdminItems(1), "bots:add:hard:team4")!.Take());

        Assert.Equal(4, Assert.Single(_teams.Joins).Asked);
        Assert.Equal(4, Assert.Single(_roster.Bots).Team);
    }

    [Fact]
    public void TheOrdersItemTurnsTheOrdersOffAndThenOn()
    {
        _teams.Teamplay = true;
        Assert.True(_orders.Value);

        AdminNode.Find(AdminMenu().AdminItems(1), "bots:orders:0")!.Run!();
        Assert.False(_orders.Value);
        AdminNode.Find(AdminMenu().AdminItems(1), "bots:orders:1")!.Run!();

        Assert.True(_orders.Value);
    }

    [Fact]
    public void TheRerollItemSaysWhyWithNoBots()
        => Assert.Equal("there are no bots", AdminNode.Find(AdminMenu().AdminItems(1), "bots:reroll")!.Take());

    private static List<AdminNode> AddItems(AiAdminMenu menu)
        => [.. Assert.Single(Assert.Single(menu.AdminItems(1)).Children!, n => n.Label == "#SS_UI_Admin_BotAdd").Children!];

    [Fact]
    public void WithTheNavigationMissingTheMenusAddItemsSayWhy()
    {
        _nav.Refusal = "no navigation for dm_lockdown: expected maps/dm_lockdown.nav3d";
        _nav.MissingFile = "maps/dm_lockdown.nav3d";

        var items = AddItems(AdminMenu());

        Assert.Equal(4, items.Count);
        Assert.All(items, n => Assert.Equal("Bots need navigation: maps/dm_lockdown.nav3d is missing (make map MAP=dm_lockdown builds it)", n.Unavailable));
    }

    [Fact]
    public void ChoosingAnUnavailableAddAddsNoBotAndSaysWhy()
    {
        _nav.Refusal = "no navigation for dm_lockdown: expected maps/dm_lockdown.nav3d";
        _nav.MissingFile = "maps/dm_lockdown.nav3d";

        string? why = AddItems(AdminMenu())[1].Take();

        Assert.StartsWith("Bots need navigation: ", why, StringComparison.Ordinal);
        Assert.Empty(_players.Asked);
    }

    [Fact]
    public void WhileTheNavigationIsBuiltTheMenusAddItemsAreLiveAndTheBotWaits()
    {
        (_nav.Refusal, _nav.MissingFile, _nav.Building) = ("navigation for dm_lockdown: building", "maps/dm_lockdown.nav3d", true);

        var items = AddItems(AdminMenu());

        Assert.All(items, n => Assert.Null(n.Unavailable));
        Assert.Null(items[1].Take());
        Assert.Empty(_players.Asked);
        Assert.StartsWith(AiRoster.WaitsForNav, Assert.Single(_roster.Bots).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNavigationTheMenusAddItemsAreLiveAndAddABot()
    {
        var items = AddItems(AdminMenu());

        Assert.All(items, n => Assert.Null(n.Unavailable));
        Assert.Null(items[1].Take());
        Assert.Single(_bots.Bots);
    }

    [Fact]
    public void AnotherNavigationRefusalIsTheMenusReasonAsItIs()
        => Assert.Equal("Bots need navigation: maps/dm_x.nav3d was built from another map",
                        AiAdminMenu.Unavailable("maps/dm_x.nav3d was built from another map", null));

    [Fact]
    public void AnUnnamedBotIsNamedByItsTraitAndTaggedWithItsPreset()
    {
        Add("hard");

        AiBot bot = Assert.Single(_bots.Bots);
        Assert.Contains(' ', bot.Name);
        Assert.True(bot.Name.Length <= BotNamer.MaxLength);
        Assert.Equal("(Hard)", _tags.TagOf(_players.Made[0].Player));
    }

    [Fact]
    public void EveryModelABotDrawsIsOneAPlayerMayWear()
    {
        for (int i = 0; i < BotAppearance.ModelCount; i++)
            Assert.True(SourceSharp.Server.PlayerModels.IsValid(BotAppearance.Model(i)), BotAppearance.Model(i));
    }

    [Fact]
    public void MediumIsAPresetNotAName()
    {
        Add("medium");
        AiBot bot = Assert.Single(_bots.Bots);
        Assert.NotEqual("medium", bot.Name);
        Assert.Equal("normal", bot.Skills.Name);
    }

    /// <summary>The same words for the other way of asking, any case.</summary>
    [Theory]
    [InlineData("medium", "normal")]
    [InlineData("Medium", "normal")]
    [InlineData("normal", "normal")]
    [InlineData("EASY", "easy")]
    public void APresetsWordIsRead(string word, string preset)
    {
        Assert.True(BotSkillSpec.Parse("x", word, out SkillProfile? p).Accepted);
        Assert.Equal(preset, p!.Name);
    }

    /// <summary>A name given twice comes in with our numeral, never the engine's "(1)".</summary>
    [Fact]
    public void ANameGivenTwiceGetsANumeral()
    {
        Add("Alyx easy");
        Add("Alyx hard");
        Assert.Equal(new[] { "Alyx", "Alyx 2" }, _bots.Bots.Select(b => b.Name));
    }

    [Fact]
    public void AGivenNameIsUsedAsGiven()
    {
        Add("Alyx hard");
        Assert.Equal(("Alyx", "(Hard)"), (_players.Asked[0].Name, _tags.TagOf(_players.Made[0].Player)));
    }

    [Fact]
    public void ACustomBotIsTaggedWithItsPoints()
    {
        Add("skills=8,7,6,6,6,6,7,5,5,4");
        Assert.Equal("(Custom 60)", _tags.TagOf(_players.Made[0].Player));
        Assert.NotEqual("custom", _players.Asked[0].Name);
    }

    [Fact]
    public void NormalIsTaggedMedium() => Assert.Equal("(Medium)", BotSkillLabel.For(SkillPresets.TryGet("normal", out SkillProfile? p) ? p! : null!));

    /// <summary>
    /// The senses run once a tick, and on the next level from its first tick:
    /// its count starts again below the last level's (found by the live gate's
    /// second pass, where bots added after two map changes never decided).
    /// </summary>
    [Fact]
    public void TheSensesRunOnANewLevelsFirstTicks()
    {
        Assert.False(SourceSharp.Ai.Server.Senses.AiSenses.IsNewTick(5000, 5000));
        Assert.True(SourceSharp.Ai.Server.Senses.AiSenses.IsNewTick(5001, 5000));
        Assert.True(SourceSharp.Ai.Server.Senses.AiSenses.IsNewTick(12, 5000));
    }

    /// <summary>
    /// A bot hears from the tick it joins: an event pushed before the next
    /// senses pass reaches the session, which learns of the bot first (the
    /// hearing scenario's spawn-time claw sounds were dropped that way).
    /// </summary>
    [Fact]
    public void AnEventInTheTickABotJoinsReachesItsBrain()
    {
        var w = new SourceSharp.Ai.Tests.Senses.SenseWorld();
        var ai = new SourceSharp.Ai.Server.Senses.ServerAiSession(_nav, TestGames.Hl2mp(), null!, _log,
            DeclaredConVars.Fresh<SourceSharp.Ai.Server.Senses.AiThreadsConVar>(), DeclaredConVars.Fresh<SourceSharp.Ai.Server.Senses.AiSyncConVar>());
        var vision = new SourceSharp.Ai.Server.Senses.VisionPass(w.Engine(), w.Entities(), w.Traces());
        var senses = new SourceSharp.Ai.Server.Senses.AiSenses(_bots, ai, vision, null!, null!, null!, _log,
                                                                new SourceSharp.Ai.Server.Senses.ReferenceRecorder(ai, vision));
        Add("Alyx hard");
        AiBot bot = Assert.Single(_bots.Bots);

        senses.Push(AiEvent.SoundHeard(bot.Id, new Tick(87), SoundKind.Unknown, new AiVector(36, 0, 0), 1f, ActorId.None, -1));

        Assert.Equal((0L, 1L), (ai.Session.Counters.EventsDropped, ai.Session.Counters.EventsIn));
        Assert.NotNull(ai.Session.BrainIn(bot.Id.Slot));
    }
}
