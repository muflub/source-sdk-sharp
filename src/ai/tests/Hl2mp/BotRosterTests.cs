using System.Reflection;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// <c>ai_bot_roster</c>'s bots (plan_hl2mp_ai §3.5, §8.2): presets,
/// <c>skills=</c> and a dedicated server's <c>profile=</c>, each judged by
/// this server's pool; an overspent entry is a refusal line and no bot.
/// </summary>
public class BotRosterTests
{
    private sealed class Lines : ILog
    {
        public readonly List<string> All = new();
        public void Msg(string message) => All.Add(message);
        public void Warning(string message) => All.Add(message);
    }

    private sealed class Players : IBotPlayers
    {
        public readonly List<BotPlayer> Made = new();

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
        {
            int index = Made.Count + 2;
            var player = new EntityHandle((uint)(0x0001_0000 * index + index));
            Made.Add(new BotPlayer(player, index, name));
            refusal = "";
            return player;
        }

        public bool IsBot(EntityHandle player) => Made.Any(b => b.Player == player);

        public bool Kick(EntityHandle bot, string reason) => Made.RemoveAll(b => b.Player == bot) > 0;

        public IReadOnlyList<BotPlayer> Bots => Made;
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public void Queue(EntityHandle player, in UserCmd cmd) { }
        public int RunQueued(EntityHandle player) => 0;
    }

    public class Unasked : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => throw new InvalidOperationException($"asked {method?.DeclaringType?.Name}.{method?.Name}");
    }

    private sealed class Navigation : IHl2mpNavigation
    {
        public string? Refusal => null;
        public LoadedNav? Current => null;
        public NavObstacleState? Obstacles => null;
        public NavSight? Sight => null;
    }

    private sealed class DataFileNames : IBotNameSource
    {
        public BotNamePools? Pools { get; } = BotNamePools.Parse(File.ReadAllText(
            SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_bot_names.json")));

        public string Refusal => "";
    }

    private sealed class Disk : IBotProfileSource
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public byte[]? Read(string path) => Files.TryGetValue(path, out byte[]? b) ? b : null;
    }

    /// <summary>The server's slots as a fact sets them.</summary>
    internal sealed class Slots : IServerSlots
    {
        public int MaxClients { get; set; } = 8;
        public int Humans { get; set; } = 1;
    }

    private readonly Slots _slots = new();
    private readonly Lines _log = new();
    private readonly Players _players = new();
    private readonly ScoreboardTags _tags = new();
    private readonly Disk _disk = new();
    private readonly Hl2mpBots _bots;
    private readonly Hl2mpRoster _roster;

    public BotRosterTests()
    {
        _bots = new Hl2mpBots(_players, new Runner(), DispatchProxy.Create<IEnginePlayers, Unasked>(),
                              DispatchProxy.Create<IEngineEntities, Unasked>(), WeaponCatalog.Stock(),
                              DispatchProxy.Create<IEngineFrame, Unasked>(), new Navigation(), DispatchProxy.Create<IBotInventory, Unasked>(),
                              new DataFileNames(), _tags, _log);
        _roster = new Hl2mpRoster(_bots, _disk, _slots, _log);
    }

    private string[] Labels() => [.. _bots.Bots.Select(b => $"{b.Skills.Name} {b.Skills.Spent}")];

    [Fact]
    public void TwoEasyAndACustomBotAreAdded()
    {
        _roster.Take("easy;easy;name=Deadeye:skills=8,7,6,6,6,6,1,1,2,2");
        _roster.Reconcile(0);
        Assert.Equal(["easy 20", "easy 20", "Deadeye 45"], Labels());
        Assert.Equal("Deadeye", _bots.Bots[2].Name);
    }

    [Fact]
    public void ACustomBotIsLabelledWithItsSpend()
    {
        _roster.Take("name=Deadeye:skills=8,7,6,6,6,6,1,1,2,2");
        _roster.Reconcile(0);
        Assert.Equal("(Custom 45)", BotSkillLabel.For(Assert.Single(_bots.Bots).Skills));
    }

    [Fact]
    public void AnOverspentEntryIsRefusedOnTheServerAndAddsNoBot()
    {
        _roster.Take("name=Cheater:skills=10,10,10,10,10,10,10,10,10,10");
        _roster.Reconcile(0);
        Assert.Empty(_players.Made);
        Assert.Equal(["[SourceSharp/Ai] ai_bot_roster refused entry 1 'skills=10,10,10,10,10,10,10,10,10,10': profile 'Cheater' spends 100 of a 60-point pool\n"],
                     _log.All);
    }

    [Fact]
    public void ALevelAboveTheCapIsRefusedNamingTheSkill()
    {
        _roster.Take("skills=11,0,0,0,0,0,0,0,0,0");
        Assert.Empty(_players.Made);
        Assert.Contains("accuracy is 11, above the cap of 10", Assert.Single(_log.All), StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedEntryLeavesTheOthers()
    {
        _roster.Take("hard;skills=11,0,0,0,0,0,0,0,0,0;normal");
        _roster.Reconcile(0);
        Assert.Equal(["hard 50", "normal 35"], Labels());
    }

    [Fact]
    public void AProfileOnTheServersDiskIsAddedUnderItsOwnName()
    {
        _disk.Files["cfg/ai/bots/Sniper.json"] = ProfileFile.Write(new BotProfileDocument(
            new SkillProfile("Sniper", StandardSkills.Ten, [10, 10, 2, 2, 2, 2, 10, 0, 0, 2]), 60, []));
        _roster.Take("profile=Sniper");
        _roster.Reconcile(0);
        Assert.Equal(("Sniper", 40), (Assert.Single(_bots.Bots).Name, _bots.Bots[0].Skills.Spent));
    }

    [Fact]
    public void AMissingProfileIsRefusedNamingItsPath()
    {
        _roster.Take("profile=Nobody");
        Assert.Empty(_players.Made);
        Assert.Contains("no profile 'Nobody' on this server: expected cfg/ai/bots/Nobody.json", Assert.Single(_log.All), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverspentProfileFileIsRefused()
    {
        _disk.Files["cfg/ai/bots/Greedy.json"] = Encoding.UTF8.GetBytes(
            """{ "format": 1, "name": "Greedy", "pool": 100, "skills": { "accuracy": 10, "alertness": 10, "aggression": 10, "cover": 10, "tenacity": 10, "cunning": 10, "discipline": 10, "agility": 0, "nerve": 0, "headhunter": 0 }, "revisions": [] }""");
        _roster.Take("profile=Greedy");
        Assert.Empty(_players.Made);
        Assert.Contains("spends 70 of a 60-point pool", Assert.Single(_log.All), StringComparison.Ordinal);
    }

    /// <summary>A roster larger than the room waits, benched: it is the server's wanted set, nothing is dropped (ruling 2026-11-02).</summary>
    [Fact]
    public void ARosterLargerThanTheRoomBenchesTheRest()
    {
        _slots.MaxClients = 4;
        _roster.Take("easy;easy;easy;easy");
        _roster.Reconcile(0);
        Assert.Equal(2, _bots.Bots.Count);
        Assert.Equal(2, _roster.Benched);
        Assert.Equal(4, _roster.Bots.Count);
        Assert.All(_roster.Bots.Where(b => !b.Active), b => Assert.Contains("no free slot", b.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRosterIsAConVarOfItsOwnName()
        => Assert.Equal("ai_bot_roster", typeof(AiBotRosterConVar).GetCustomAttribute<ConVarAttribute>()?.Name);

    [Fact]
    public void TheRosterAddsAfterTheBindingHasDroppedTheLastLevelsBots()
    {
        var roster = (AiBotRosterService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AiBotRosterService));
        Assert.True(roster.FrameOrder > _bots.FrameOrder, $"roster {roster.FrameOrder}, bots {_bots.FrameOrder}");
    }
}
