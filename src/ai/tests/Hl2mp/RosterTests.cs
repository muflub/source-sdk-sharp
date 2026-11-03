using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// The server's wanted bots (plan_hl2mp_ai §3.5; user rulings 2026-11-02:
/// "Bots should stay on map change, also on player join, bots should leave if
/// not enough free slots and add back on player leave"; "add an cvar to
/// randomise the bots on map load with new traits and names"; "all the bot
/// skills are exactly the same for med bots"). The engine is a fake that
/// makes, kicks and drops clients as the real one does.
/// </summary>
public sealed class RosterTests
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
        public readonly List<string> Added = new();
        public readonly List<string> Kicked = new();
        private int _next = 2;

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
        {
            int index = _next++;
            var player = new EntityHandle((uint)(0x0001_0000 * index + index));
            Made.Add(new BotPlayer(player, index, name));
            Added.Add(name);
            refusal = "";
            return player;
        }

        public bool IsBot(EntityHandle player) => Made.Any(b => b.Player == player);

        public bool Kick(EntityHandle bot, string reason)
        {
            BotPlayer found = Made.FirstOrDefault(b => b.Player == bot);
            if (found.Player == EntityHandle.None)
                return false;
            Kicked.Add(found.Name);
            Made.Remove(found);
            return true;
        }

        public IReadOnlyList<BotPlayer> Bots => Made;
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public void Queue(EntityHandle player, in UserCmd cmd) { }
        public int RunQueued(EntityHandle player) => 0;
    }

    public class Defaults : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null;
    }

    private sealed class Navigation : IHl2mpNavigation
    {
        public string? Refusal { get; set; }
        public LoadedNav? Current => null;
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;
        public NavSight? Sight => null;

        public ChokeSlot? Chokes => null;
    }

    private sealed class Names : IBotNameSource
    {
        public BotNamePools? Pools { get; } = BotNamePools.Parse(File.ReadAllText(
            SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_bot_names.json")));

        public string Refusal => "";
    }

    private sealed class NoProfiles : IBotProfileSource
    {
        public byte[]? Read(string path) => null;
    }

    private sealed class Rig
    {
        public readonly Lines Log = new();
        public readonly Players Players;
        public readonly BotRosterTests.Slots Slots = new() { MaxClients = 8, Humans = 1 };
        public readonly ScoreboardTags Tags = new();
        public readonly Navigation Nav = new();
        public readonly Hl2mpBots Bots;
        public readonly Hl2mpRoster Roster;

        public Rig(Players? players = null)
        {
            Players = players ?? new Players();
            Bots = new Hl2mpBots(Players, new Runner(), DispatchProxy.Create<IEnginePlayers, Defaults>(), DispatchProxy.Create<IEngineEntities, Defaults>(),
                                 WeaponCatalog.Stock(), DispatchProxy.Create<IEngineFrame, Defaults>(), Nav, DispatchProxy.Create<IBotInventory, Defaults>(),
                                 new Names(), Tags, Log);
            Roster = new Hl2mpRoster(Bots, new NoProfiles(), Slots, Log);
        }

        public string? Add(string? name, string? spec) => Roster.Add(name, spec);

        /// <summary>A frame: the binding follows the engine, then the roster reconciles.</summary>
        public void Frame(int tick)
        {
            Bots.OnPreThink(0.015f);
            Roster.Reconcile(tick);
        }

        /// <summary>The engine drops its fake clients with the level; the new level starts.</summary>
        public void ChangeLevel(string map, bool reroll = false)
        {
            Players.Made.Clear();
            Roster.LevelStarted(map, reroll);
            Frame(0);
        }

        public IEnumerable<string> ActiveNames => Roster.Bots.Where(b => b.Active).Select(b => b.Name);
    }

    private static (string Name, string Levels, int Seed, string Tag)[] Facts(Rig r)
        => [.. r.Roster.Bots.Select(b => (b.Name, string.Join(",", b.Skills.Levels), b.SeedIndex, BotSkillLabel.For(b.Skills)))];

    // ---- a hot reload (user ruling 2026-11-02) ---------------------------------

    /// <summary>The swap: the old build writes its record, and a new rig over the same engine clients takes it back.</summary>
    private static Rig Reloaded(Rig before)
    {
        var record = new SourceSharp.Abi.ReloadRecord("roster");
        before.Roster.Save(new SourceSharp.Abi.SaveRecordAccess(record, record.Owner, before.Log));
        var after = new Rig(before.Players);
        after.Slots.Humans = before.Slots.Humans;
        Assert.Empty(after.Bots.Bots);
        after.Roster.Resume(new SourceSharp.Abi.SaveRecordAccess(record, record.Owner, before.Log));
        return after;
    }

    private static Rig TwoBots()
    {
        var r = new Rig();
        Assert.Null(r.Add("Alyx", "hard"));
        Assert.Null(r.Add("Barney", "normal"));
        r.Frame(8);
        return r;
    }

    [Fact]
    public void BotsInSlotsArePlayedAfterAReloadWithNoneAddedOrKicked()
    {
        Rig before = TwoBots();
        var slots = before.Players.Made.Select(b => (b.EntIndex, b.Player, b.Name)).ToArray();

        Rig after = Reloaded(before);
        after.Frame(16);

        Assert.Equal(["Alyx", "Barney"], before.Players.Added);
        Assert.Empty(before.Players.Kicked);
        Assert.Equal((2, 0), (after.Bots.Adopted, after.Bots.Added));
        Assert.Equal(slots, after.Bots.Bots.OrderBy(b => b.Id.Slot).Select(b => (b.Id.Slot, after.Bots.Applier.PlayerOf(b.Id), b.Name)));
        Assert.Equal(["Alyx", "Barney"], after.ActiveNames);
    }

    [Fact]
    public void AReloadKeepsEveryBotsNameSkillsSeedAndTag()
    {
        Rig before = TwoBots();
        Assert.NotEqual(Facts(before)[0].Levels, Facts(before)[1].Levels);

        Rig after = Reloaded(before);

        Assert.Equal(Facts(before), Facts(after));
        Assert.Equal(before.Bots.Bots.OrderBy(b => b.Id.Slot).Select(b => (b.Skills.Levels.Sum(), b.SeedIndex, b.Skills.Name)),
                     after.Bots.Bots.OrderBy(b => b.Id.Slot).Select(b => (b.Skills.Levels.Sum(), b.SeedIndex, b.Skills.Name)));
        foreach (BotPlayer live in before.Players.Made)
            Assert.Equal(before.Tags.TagOf(live.Player), after.Tags.TagOf(live.Player));
    }

    [Fact]
    public void AfterAReloadAKickClearsThem()
    {
        Rig after = Reloaded(TwoBots());

        Assert.Equal(2, after.Roster.Remove("all"));
        after.Frame(16);

        Assert.Equal(["Alyx", "Barney"], after.Players.Kicked);
        Assert.Empty(after.Roster.Bots);
        Assert.Empty(after.Bots.Bots);
    }

    [Fact]
    public void AfterAReloadAMapChangeBringsTheSameBotsBack()
    {
        Rig before = TwoBots();
        var facts = Facts(before);
        Rig after = Reloaded(before);

        after.ChangeLevel("dm_other");

        Assert.Equal(facts, Facts(after));
        Assert.Equal(["Alyx", "Barney"], after.ActiveNames);
        Assert.Equal(["Alyx", "Barney", "Alyx", "Barney"], after.Players.Added);
    }

    [Fact]
    public void AfterAReloadABotGivesItsSlotToAPerson()
    {
        Rig after = Reloaded(TwoBots());
        after.Slots.MaxClients = 3;
        after.Roster.Reserve = 0;
        after.Slots.Humans = 2;

        after.Frame(16);

        Assert.Equal(["Barney"], after.Players.Kicked);
        Assert.Equal(["Alyx"], after.ActiveNames);
        Assert.Equal("Barney", Assert.Single(after.Roster.Bots, b => !b.Active).Name);
    }

    [Fact]
    public void ABotWithNoRecordPlaysAsTheDefaultAndSaysSo()
    {
        Rig before = TwoBots();
        var after = new Rig(before.Players);

        after.Roster.Resume(null);

        Assert.Equal(["Alyx", "Barney"], after.ActiveNames);
        Assert.All(after.Roster.Bots, b => Assert.Equal(Hl2mpRoster.ReloadDefault, b.Shape.Name));
        Assert.Equal(2, after.Log.All.Count(l => l.Contains("has no record through the reload", StringComparison.Ordinal)));
        Assert.Empty(before.Players.Kicked);
    }

    [Fact]
    public void AWantedBotWhoseSlotHasGoneComesBackOnce()
    {
        Rig before = TwoBots();
        before.Players.Made.RemoveAt(1);

        Rig after = Reloaded(before);
        Assert.Equal(["Alyx"], after.ActiveNames);
        after.Frame(16);
        after.Frame(24);

        Assert.Equal(["Alyx", "Barney"], after.ActiveNames);
        Assert.Equal(["Alyx", "Barney", "Barney"], after.Players.Added);
    }

    [Fact]
    public void ABotALevelCannotPlayIsKickedNotStranded()
    {
        Rig before = TwoBots();
        var record = new SourceSharp.Abi.ReloadRecord("roster");
        before.Roster.Save(new SourceSharp.Abi.SaveRecordAccess(record, record.Owner, before.Log));
        var after = new Rig(before.Players);
        after.Nav.Refusal = "no nav for this map";

        after.Roster.Resume(new SourceSharp.Abi.SaveRecordAccess(record, record.Owner, before.Log));

        Assert.Equal(["Alyx", "Barney"], after.Players.Kicked);
        Assert.Empty(after.Players.Made);
        Assert.Equal(2, after.Roster.Benched);
    }

    // ---- map change ---------------------------------------------------------

    [Fact]
    public void ANameAPersonHasGetsANumeral()
    {
        var r = new Rig();
        r.Slots.People.Add("Alyx");
        Assert.Null(r.Add("Alyx", "easy"));
        Assert.Equal("Alyx 2", r.Roster.Bots.Single().Name);
    }

    /// <summary>A bot on the bench holds its first name as one in the game does.</summary>
    [Fact]
    public void ABenchedBotKeepsItsFirstNameFromOthers()
    {
        var r = new Rig();
        r.Slots.MaxClients = 2;
        for (int i = 0; i < 12; i++)
            Assert.Null(r.Add(null, "normal"));
        string[] firsts = [.. r.Roster.Bots.Select(b => b.Name.Split(' ')[0])];
        Assert.Equal(firsts.Length, firsts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ALevelChangeBringsEveryBotBackWithItsNameSkillsAndSeed()
    {
        var r = new Rig();
        r.Add(null, "easy");
        r.Add(null, "normal");
        r.Add("Deadeye", "skills=8,7,6,4,3,3,4,3,4,3");
        var before = Facts(r);
        Assert.Equal(3, r.Players.Made.Count);

        r.ChangeLevel("ss_ai_duel");

        Assert.Equal(before, Facts(r));
        Assert.Equal(3, r.Players.Made.Count);
        Assert.Equal(before.Select(b => b.Name), r.Players.Made.Select(p => p.Name));
        Assert.Equal(before.Select(b => b.Seed), r.Bots.Bots.Select(b => b.SeedIndex));
        Assert.Equal(6, r.Players.Added.Count);   // the control: they really were made again
    }

    /// <summary>The level-start path (Phase 5c's roster) and the level-change path are one: a second level adds no second copy.</summary>
    [Fact]
    public void TheRosterConVarsBotsAreNotAddedTwice()
    {
        var r = new Rig();
        r.Roster.Take("easy;easy;name=Deadeye:skills=8,7,6,4,3,3,4,3,4,3");
        r.Frame(0);
        Assert.Equal(3, r.Players.Made.Count);
        r.ChangeLevel("ss_ai_duel");
        Assert.Equal(3, r.Players.Made.Count);

        // server.cfg sets the same spec at every level: the same bots, not six.
        r.Players.Made.Clear();
        r.Roster.Take("easy;easy;name=Deadeye:skills=8,7,6,4,3,3,4,3,4,3");
        r.Frame(0);
        Assert.Equal(3, r.Players.Made.Count);
        Assert.Equal(3, r.Roster.Bots.Count);
    }

    // ---- free slots ---------------------------------------------------------

    private static Rig ThreeBotsInFiveSlots()
    {
        var r = new Rig();
        r.Slots.MaxClients = 5;
        r.Add("Oldest", "easy");
        r.Add("Middle", "easy");
        r.Add("Newest", "easy");
        Assert.Equal(3, r.ActiveNames.Count());
        return r;
    }

    [Fact]
    public void APlayerJoiningBenchesTheNewestBotAndSaysSo()
    {
        Rig r = ThreeBotsInFiveSlots();
        r.Slots.Humans = 2;
        r.Frame(8);
        Assert.Equal(["Oldest", "Middle"], r.ActiveNames);
        Assert.Equal(["Newest"], r.Players.Kicked);
        Assert.Contains(r.Log.All, l => l.Contains("bot Newest benched: no free slot (2 human(s) of 5 slots, 1 kept free)", StringComparison.Ordinal));
        RosterBot benched = r.Roster.Bots.Single(b => !b.Active);
        Assert.Contains("no free slot", benched.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void APlayerLeavingBringsThatBotBackAfterTheHoldWithItsSeed()
    {
        Rig r = ThreeBotsInFiveSlots();
        var before = Facts(r);
        r.Slots.Humans = 2;
        r.Frame(8);
        r.Slots.Humans = 1;
        r.Frame(16);
        Assert.Equal(2, r.ActiveNames.Count());   // still held
        r.Frame(8 + Hl2mpRoster.RestoreHoldTicks);
        Assert.Equal(["Oldest", "Middle", "Newest"], r.ActiveNames);
        Assert.Equal(before, Facts(r));
    }

    [Fact]
    public void TheOldestBenchedComesBackFirst()
    {
        Rig r = ThreeBotsInFiveSlots();
        r.Slots.Humans = 2;
        r.Frame(8);     // Newest goes
        r.Slots.Humans = 3;
        r.Frame(16);    // Middle goes
        Assert.Equal(["Newest", "Middle"], r.Players.Kicked);
        r.Slots.Humans = 2;
        r.Frame(16 + Hl2mpRoster.RestoreHoldTicks);
        Assert.Equal(["Oldest", "Newest"], r.ActiveNames);
    }

    /// <summary>The ceiling whatever the numbers: bots + people never fill the last slot.</summary>
    [Fact]
    public void TheReserveSlotAlwaysStaysFree()
    {
        int tick = 0, seen = 0;
        var r = new Rig();
        for (int i = 0; i < 6; i++)
            r.Add(null, "easy");
        for (int max = 2; max <= 8; max++)
        {
            for (int humans = 0; humans <= max; humans++)
            {
                r.Slots.MaxClients = max;
                r.Slots.Humans = humans;
                tick += Hl2mpRoster.RestoreHoldTicks + Hl2mpRoster.RecountTicks;
                tick -= tick % Hl2mpRoster.RecountTicks;
                r.Frame(tick);
                int active = r.ActiveNames.Count();
                seen += active;
                Assert.True(active == Math.Min(6, Math.Max(0, max - humans - 1)), $"{active} bots with {humans} people in {max} slots");
                Assert.Equal(6, r.Roster.Bots.Count);
            }
        }

        Assert.True(seen > 0, "the control: some bots were in the game");
    }

    /// <summary>The gate's minute on four slots gives the reserve up by the convar; the default is the ruling's 1.</summary>
    [Fact]
    public void TheReserveIsAConVarThatDefaultsToOne()
    {
        Assert.Equal("1", typeof(AiBotReserveConVar).GetCustomAttribute<ConVarAttribute>()!.DefaultValue);
        Rig r = ThreeBotsInFiveSlots();
        r.Slots.Humans = 2;
        r.Roster.Reserve = 0;
        r.Frame(8);
        Assert.Equal(3, r.ActiveNames.Count());
        r.Roster.Reserve = 1;
        r.Frame(16);
        Assert.Equal(2, r.ActiveNames.Count());
    }

    [Fact]
    public void ABotAddedWithNoRoomWaitsBenchedRatherThanRefused()
    {
        var r = new Rig();
        r.Slots.MaxClients = 3;
        Assert.Null(r.Add("First", "easy"));
        Assert.Null(r.Add("Second", "easy"));
        Assert.Equal(["First"], r.ActiveNames);
        Assert.Equal(1, r.Roster.Benched);
    }

    /// <summary>A quick connect and drop, again and again, costs one kick and one return: the hold (5 s) is the hysteresis.</summary>
    [Fact]
    public void AQuickConnectAndDropDoesNotThrash()
    {
        Rig r = ThreeBotsInFiveSlots();
        int madeBefore = r.Players.Added.Count;
        for (int t = 8; t < 300; t += 8)
        {
            r.Slots.Humans = t / 8 % 2 == 1 ? 2 : 1;
            r.Frame(t);
        }

        Assert.True(r.Players.Kicked.Count == 1, $"kicks: {string.Join(", ", r.Players.Kicked)}");
        Assert.Equal(madeBefore, r.Players.Added.Count);   // none came back inside the hold
        r.Slots.Humans = 1;
        r.Frame(304 + Hl2mpRoster.RestoreHoldTicks);
        Assert.Equal(madeBefore + 1, r.Players.Added.Count);
    }

    [Fact]
    public void AnActiveBotKickedByNameLeavesTheRoster()
    {
        Rig r = ThreeBotsInFiveSlots();
        Assert.Equal(1, r.Roster.Remove("middle"));
        r.Frame(8);
        r.ChangeLevel("ss_ai_duel");
        Assert.Equal(["Oldest", "Newest"], r.Roster.Bots.Select(b => b.Name));
        Assert.Equal(["Oldest", "Newest"], r.ActiveNames);
    }

    [Fact]
    public void ABenchedBotKickedByNameLeavesTheRoster()
    {
        Rig r = ThreeBotsInFiveSlots();
        r.Slots.Humans = 2;
        r.Frame(8);
        Assert.Equal(1, r.Roster.Remove("Newest"));
        r.Slots.Humans = 1;
        r.Frame(8 + Hl2mpRoster.RestoreHoldTicks);
        Assert.Equal(["Oldest", "Middle"], r.ActiveNames);
    }

    [Fact]
    public void KickAllEmptiesTheRoster()
    {
        Rig r = ThreeBotsInFiveSlots();
        r.Slots.Humans = 2;
        r.Frame(8);
        Assert.Equal(3, r.Roster.Remove("all"));
        r.ChangeLevel("ss_ai_duel");
        Assert.Empty(r.Roster.Bots);
        Assert.Empty(r.Players.Made);
    }

    /// <summary>Kicked from the console by another route, a bot is still wanted, so it comes back.</summary>
    [Fact]
    public void ABotKickedFromTheConsoleIsStillWanted()
    {
        Rig r = ThreeBotsInFiveSlots();
        r.Players.Made.RemoveAt(1);
        r.Frame(8);
        Assert.Equal(3, r.ActiveNames.Count());
        Assert.Contains("Middle", r.ActiveNames);
    }

    /// <summary>A level with no nav refuses bots: they stay wanted, say so once, and come back on the next level.</summary>
    [Fact]
    public void ALevelWithNoNavBenchesTheBotsAndTheNextLevelBringsThemBack()
    {
        Rig r = ThreeBotsInFiveSlots();
        r.Nav.Refusal = "no navigation for dm_x";
        r.ChangeLevel("dm_x");
        for (int t = 8; t < 40; t += 8)
            r.Frame(t);
        Assert.Empty(r.ActiveNames);
        Assert.Equal(3, r.Log.All.Count(l => l.Contains("stays benched: no navigation for dm_x", StringComparison.Ordinal)));
        r.Nav.Refusal = null;
        r.ChangeLevel("dm_lockdown");
        Assert.Equal(3, r.ActiveNames.Count());
    }

    // ---- people -------------------------------------------------------------

    /// <summary>The engine's clients: <paramref name="connected"/> have a channel; a player entity, so a user id, comes only at the spawn.</summary>
    private static IClientInfo Clients(int[] connected, params int[] spawned) => SourceSharp.Ai.Tests.Senses.FakeEngine.Of<IConnectedClients>((m, x) => m.Name switch
    {
        "Address" => connected.Contains((int)x[0]!) ? "127.0.0.1:27005" : null,
        "UserId" => spawned.Contains((int)x[0]!) ? 100 + (int)x[0]! : -1,
        _ => throw SourceSharp.Ai.Tests.Senses.FakeEngine.Unasked(m),
    });

    /// <summary>A person counts from the connect: slot 3 has a channel and no player entity yet (found live: the engine's status said 2 humans, the user id 1). A bot has no channel.</summary>
    [Fact]
    public void PeopleAreTheClientsWithAChannelConnectingOrSpawned()
    {
        Assert.Equal(2, EngineServerSlots.CountHumans(4, Clients([1, 3], 1), dedicated: false));
    }

    /// <summary>A spectator holds a slot as anyone does (§4.1): the count is of channels, and asks no team.</summary>
    [Fact]
    public void ASpectatorCountsAsAPersonForTheSlots()
    {
        Assert.Equal(1, EngineServerSlots.CountHumans(4, Clients([2], 1), dedicated: true));
    }

    /// <summary>A listen server's host takes a slot only after the level is up: counted first, or a bot takes it at the level's start and is benched a second later.</summary>
    [Fact]
    public void AListenServerCountsItsHostBeforeItConnectsADedicatedOneNobody()
    {
        Assert.Equal(1, EngineServerSlots.CountHumans(4, Clients([]), dedicated: false));
        Assert.Equal(0, EngineServerSlots.CountHumans(4, Clients([]), dedicated: true));
        Assert.Equal(1, EngineServerSlots.CountHumans(4, Clients([2]), dedicated: true));   // the control: a dedicated server counts who is there
    }

    // ---- the draw -----------------------------------------------------------

    [Fact]
    public void TwoMediumBotsDrawDifferentLevelsAndBothReadMedium()
    {
        var r = new Rig();
        r.Add(null, "normal");
        r.Add(null, "normal");
        RosterBot a = r.Roster.Bots[0], b = r.Roster.Bots[1];
        Assert.False(a.Skills.SameLevels(b.Skills), string.Join(",", a.Skills.Levels));
        Assert.Equal((35, 35, "(Medium)", "(Medium)"), (a.Skills.Spent, b.Skills.Spent, BotSkillLabel.For(a.Skills), BotSkillLabel.For(b.Skills)));
        Assert.Equal("(Medium)", r.Tags.TagOf(r.Players.Made[0].Player));
    }

    [Fact]
    public void ABotsDrawnLevelsAreWhatItPlaysBy()
    {
        var r = new Rig();
        r.Add(null, "normal");
        Assert.True(r.Roster.Bots[0].Skills.SameLevels(r.Bots.Bots[0].Skills));
        SkillPresets.TryGet("normal", out SkillProfile? mean);
        Assert.False(r.Bots.Bots[0].Skills.SameLevels(mean!), "the bot plays its draw, not the preset's mean");
    }

    [Fact]
    public void ANamedCustomAllocationIsNotDrawn()
    {
        var r = new Rig();
        r.Add("Deadeye", "skills=8,7,6,4,3,3,4,3,4,3");
        Assert.Equal("8,7,6,4,3,3,4,3,4,3", string.Join(",", r.Roster.Bots[0].Skills.Levels));
        Assert.Equal("(Custom 45)", BotSkillLabel.For(r.Roster.Bots[0].Skills));
    }

    [Fact]
    public void ThePoolsNameABotByItsDrawnTrait()
    {
        var r = new Rig();
        var names = new HashSet<string>();
        for (int i = 0; i < 6; i++)
            r.Add(null, "normal");
        foreach (RosterBot b in r.Roster.Bots)
            names.Add(b.Name.Split(' ')[^1]);
        Assert.True(names.Count >= 2, $"six Medium bots share the trait word: {string.Join(", ", names)}");
    }

    // ---- re-roll ------------------------------------------------------------

    private static Rig FourKinds()
    {
        var r = new Rig();
        r.Add(null, "easy");
        r.Add(null, "hard");
        r.Add("Deadeye", "skills=8,7,6,4,3,3,4,3,4,3");
        r.Add(null, "skills=4,4,4,3,3,3,4,3,4,3");
        return r;
    }

    [Fact]
    public void WithRerollOffBotsComeBackUnchanged()
    {
        Rig r = FourKinds();
        var before = Facts(r);
        r.ChangeLevel("ss_ai_duel", reroll: false);
        Assert.Equal(before, Facts(r));
    }

    [Fact]
    public void WithRerollOnPresetBotsChangeNameSeedAndLevelsAndKeepTotalAndTag()
    {
        Rig r = FourKinds();
        var before = Facts(r);
        r.ChangeLevel("ss_ai_duel", reroll: true);
        var after = Facts(r);
        foreach (int i in new[] { 0, 1 })
        {
            Assert.NotEqual(before[i].Seed, after[i].Seed);
            Assert.NotEqual(before[i].Levels, after[i].Levels);
            Assert.NotEqual(before[i].Name, after[i].Name);
            Assert.Equal(before[i].Tag, after[i].Tag);
        }

        Assert.Equal((20, 50), (r.Roster.Bots[0].Skills.Spent, r.Roster.Bots[1].Skills.Spent));
        Assert.Equal(after.Select(a => a.Name), r.Players.Made.Select(p => p.Name));
    }

    [Fact]
    public void EveryRerolledAllocationIsLegal()
    {
        Rig r = FourKinds();
        SkillPool pool = StandardSkills.StandardPool();
        for (int level = 0; level < 40; level++)
        {
            r.ChangeLevel("dm_lockdown", reroll: true);
            Assert.All(r.Roster.Bots, b =>
            {
                Assert.True(pool.Judge(b.Skills).Accepted, string.Join(",", b.Skills.Levels));
                Assert.Equal(b.Shape.Spent, b.Skills.Spent);
            });
        }
    }

    [Fact]
    public void RerolledNamesStayUnique()
    {
        var r = new Rig();
        for (int i = 0; i < 12; i++)
            r.Add(null, "normal");
        for (int level = 0; level < 20; level++)
        {
            r.ChangeLevel("dm_lockdown", reroll: true);
            Assert.Equal(12, r.Roster.Bots.Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void ANamedCustomBotIsUntouchedByAReroll()
    {
        Rig r = FourKinds();
        var before = Facts(r)[2];
        r.ChangeLevel("ss_ai_duel", reroll: true);
        Assert.Equal(before, Facts(r)[2]);
    }

    [Fact]
    public void AnUnnamedCustomBotKeepsItsTotalAndIsSpreadAnew()
    {
        Rig r = FourKinds();
        var before = Facts(r)[3];
        Assert.Equal("4,4,4,3,3,3,4,3,4,3", before.Levels);
        r.ChangeLevel("ss_ai_duel", reroll: true);
        var after = Facts(r)[3];
        Assert.NotEqual(before.Levels, after.Levels);
        Assert.Equal("(Custom 35)", after.Tag);
    }

    /// <summary>A name given to a preset bot is the user's: its levels are spread anew, its name stays.</summary>
    [Fact]
    public void AGivenNameSurvivesAReroll()
    {
        var r = new Rig();
        r.Add("Alyx", "normal");
        string levels = Facts(r)[0].Levels;
        r.ChangeLevel("ss_ai_duel", reroll: true);
        Assert.Equal("Alyx", r.Roster.Bots[0].Name);
        Assert.NotEqual(levels, Facts(r)[0].Levels);
    }

    [Fact]
    public void ABenchedBotIsRerolledToo()
    {
        Rig r = ThreeBotsInFiveSlots();
        r.Slots.Humans = 2;
        r.Frame(8);
        var benched = r.Roster.Bots.Single(b => !b.Active);
        string levels = string.Join(",", benched.Skills.Levels);
        r.ChangeLevel("ss_ai_duel", reroll: true);
        Assert.False(benched.Active);
        Assert.NotEqual(levels, string.Join(",", benched.Skills.Levels));
    }

    /// <summary>The re-roll is the match seed's and the level's: the same session draws the same bots, so a recording replays.</summary>
    [Fact]
    public void TheSameMatchAndLevelGiveTheSameReroll()
    {
        Rig a = FourKinds(), b = FourKinds();
        a.ChangeLevel("ss_ai_duel", reroll: true);
        b.ChangeLevel("ss_ai_duel", reroll: true);
        Assert.Equal(Facts(a), Facts(b));
        // The controls: another map, and the same map at another level, draw otherwise.
        Rig c = FourKinds();
        c.ChangeLevel("dm_lockdown", reroll: true);
        Assert.NotEqual(Facts(a)[0].Levels, Facts(c)[0].Levels);
        a.ChangeLevel("ss_ai_duel", reroll: true);
        Assert.NotEqual(Facts(a)[0].Levels, Facts(b)[0].Levels);
        Assert.Equal(Hl2mpRoster.RerollIndex("dm_lockdown", 2, 1), Hl2mpRoster.RerollIndex("dm_lockdown", 2, 1));
    }

    [Fact]
    public void TheCvarIsArchivedAndOffByDefault()
    {
        ConVarAttribute a = typeof(AiBotRerollConVar).GetCustomAttribute<ConVarAttribute>()!;
        Assert.Equal(("ai_bot_reroll_on_map", "0", ConVarFlags.Archive), (a.Name, a.DefaultValue, a.Flags & ConVarFlags.Archive));
    }
}
