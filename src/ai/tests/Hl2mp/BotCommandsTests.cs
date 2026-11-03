using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Ai.Hl2mp.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

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

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
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
        using var doc = System.Text.Json.JsonDocument.Parse(SourceSharp.Ai.Hl2mp.Senses.AiBotRoutes.BotsJson(_bots.Bots));
        var rows = doc.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(("Alyx", "hard", 50, _bots.Bots[0].Id.Slot, _bots.Bots[0].Id.Serial),
                     (rows[0].GetProperty("name").GetString(), rows[0].GetProperty("profile").GetString(), rows[0].GetProperty("spent").GetInt32(),
                      rows[0].GetProperty("slot").GetInt32(), rows[0].GetProperty("serial").GetInt32()));
        Assert.Equal(_bots.Bots[1].Name, rows[1].GetProperty("name").GetString());
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
        var bots = new Hl2mpBots(_players, new Runner(), DispatchProxy.Create<IEnginePlayers, Defaults>(),
                                 DispatchProxy.Create<IEngineEntities, Defaults>(), WeaponCatalog.Stock(),
                                 DispatchProxy.Create<IEngineFrame, Defaults>(), _nav, DispatchProxy.Create<IBotInventory, Defaults>(),
                                 new DataFileNames(), _tags, _log);
        new AiBotAddCommand(bots, _log).Execute("Alyx hard");
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
    private sealed class Navigation : IHl2mpNavigation
    {
        public string? Refusal { get; set; }

        public LoadedNav? Current => null;

        public NavObstacleState? Obstacles => null;

        public NavSight? Sight => null;
    }

    private readonly Navigation _nav = new();
    private readonly ScoreboardTags _tags = new();

    /// <summary>The pools from the committed data file, as the game reads it.</summary>
    private sealed class DataFileNames : IBotNameSource
    {
        public BotNamePools? Pools { get; } = BotNamePools.Parse(File.ReadAllText(
            SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_bot_names.json")));

        public string Refusal => "";
    }
    private readonly Lines _log = new();
    private readonly Players _players = new();
    private readonly Hl2mpBots _bots;

    public BotCommandsTests()
    {
        _bots = new Hl2mpBots(_players, new Runner(), DispatchProxy.Create<IEnginePlayers, Unasked>(),
                              DispatchProxy.Create<IEngineEntities, Unasked>(), WeaponCatalog.Stock(),
                              DispatchProxy.Create<IEngineFrame, Unasked>(), _nav, DispatchProxy.Create<IBotInventory, Unasked>(),
                              new DataFileNames(), _tags, _log);
    }

    private void Add(string args) => new AiBotAddCommand(_bots, _log).Execute(args);

    [Fact]
    public void APresetAddsABotWithThatPresetsSkills()
    {
        Add("Alyx hard");

        Hl2mpBot bot = Assert.Single(_bots.Bots);
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

        Hl2mpBot bot = Assert.Single(_bots.Bots);
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

    [Fact]
    public void KickRemovesTheNamedBotOrAll()
    {
        Add("Alyx");
        Add("Barney");
        Add("Gordon");

        new AiBotKickCommand(_bots, _log).Execute("barney");
        Assert.Equal([new EntityHandle(0x0003_0003u)], _players.Kicked);

        new AiBotKickCommand(_bots, _log).Execute("all");
        Assert.Empty(_players.Made);
        Assert.Contains("[SourceSharp/Ai] ai_bot_kick all: 2 kicked\n", _log.All);
    }

    [Fact]
    public void CountListsTheBots()
    {
        Add("Alyx easy");
        Add("Barney expert");
        _log.All.Clear();

        new AiBotCountCommand(_bots, _log).Execute("");

        Assert.Equal(["[SourceSharp/Ai] bots: 2\n",
                      "[SourceSharp/Ai]   Alyx: client 2, easy (20 points)\n",
                      "[SourceSharp/Ai]   Barney: client 3, expert (60 points)\n"], _log.All);
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
    public void AnUnnamedBotIsNamedByItsTraitAndTaggedWithItsPreset()
    {
        Add("hard");

        Hl2mpBot bot = Assert.Single(_bots.Bots);
        Assert.Contains(' ', bot.Name);
        Assert.True(bot.Name.Length <= BotNamer.MaxLength);
        Assert.Equal("(Hard)", _tags.TagOf(_players.Made[0].Player));
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
        Assert.False(SourceSharp.Ai.Hl2mp.Senses.Hl2mpSenses.IsNewTick(5000, 5000));
        Assert.True(SourceSharp.Ai.Hl2mp.Senses.Hl2mpSenses.IsNewTick(5001, 5000));
        Assert.True(SourceSharp.Ai.Hl2mp.Senses.Hl2mpSenses.IsNewTick(12, 5000));
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
        var ai = new SourceSharp.Ai.Hl2mp.Senses.Hl2mpAiSession(_nav, WeaponCatalog.Stock(), null!, null!, _log);
        var senses = new SourceSharp.Ai.Hl2mp.Senses.Hl2mpSenses(_bots, ai, new SourceSharp.Ai.Hl2mp.Senses.VisionPass(w.Engine(), w.Entities(), w.Traces()),
                                                                null!, null!, null!, _log);
        Add("Alyx hard");
        Hl2mpBot bot = Assert.Single(_bots.Bots);

        senses.Push(AiEvent.SoundHeard(bot.Id, new Tick(87), SoundKind.Unknown, new AiVector(36, 0, 0), 1f, ActorId.None, -1));

        Assert.Equal((0L, 1L), (ai.Session.Counters.EventsDropped, ai.Session.Counters.EventsIn));
        Assert.NotNull(ai.Session.BrainIn(bot.Id.Slot));
    }
}
