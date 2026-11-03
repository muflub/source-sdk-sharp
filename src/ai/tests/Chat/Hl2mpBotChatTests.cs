using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Chat;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>
/// Bot chat's place in the game (plan_hl2mp_ai §3.6): off, it starts nothing;
/// on, a bot hears only what a player in its place would (the say path's own
/// rule), says its line through that path, and a server that refuses is one
/// console line and silence.
/// </summary>
public sealed class Hl2mpBotChatTests
{
    private const int Human = 1;

    private static readonly EntityHandle HumanHandle = new(0x0001_0000 + Human);

    /// <summary>An engine interface that answers every call with its type's default, an empty list where it wants one.</summary>
    public class Defaults : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Type? t = method?.ReturnType;
            if (t is { IsGenericType: true } && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return Array.CreateInstance(t.GetGenericArguments()[0], 0);
            return t is { IsValueType: true } && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }

    private static T Any<T>() where T : class => DispatchProxy.Create<T, Defaults>();

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
            var player = new EntityHandle((uint)(0x0001_0000 + index));
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

    private sealed class Navigation : IHl2mpNavigation
    {
        public string? Refusal => null;
        public LoadedNav? Current => null;
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;
        public NavSight? Sight => null;

        public ChokeSlot? Chokes => null;
    }

    private sealed class Names : IBotNameSource
    {
        public BotNamePools? Pools => null;
        public string Refusal => "no pools";
    }

    private sealed class Tags : IScoreboardTags
    {
        public void Set(EntityHandle player, string tag) { }
        public string TagOf(EntityHandle player) => "";
    }

    /// <summary>The say path's sender: a generated proxy cannot take the span it is handed.</summary>
    private sealed class Sender : IUserMessageSender
    {
        public void Send(string name, ReadOnlySpan<byte> payload, int player, bool reliable) { }
    }

    private sealed class Inventory : IBotInventory
    {
        public IReadOnlyList<string> Held(EntityHandle player) => [];
    }

    private sealed class FightClock : IBotFightClock
    {
        public int LastHitTick(int slot) => -1;
    }

    private sealed class Clients : IConnectedClients
    {
        public float? AvgLatency(int entIndex) => null;
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => "loopback";
        public string? ClientConVar(int entIndex, string name) => name == "name" ? (entIndex == Human ? "lodle" : "bot" + entIndex) : null;
        public int UserId(int entIndex) => entIndex + 10;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private sealed class Words : IChatWordsSource
    {
        public ChatWords? Words_ { get; set; } = ChatRig.Words();
        ChatWords? IChatWordsSource.Words => Words_;
        public string Refusal { get; set; } = "";
    }

    private sealed class Config : IChatConfig
    {
        public bool Enabled { get; set; } = true;
        public ChatTarget Target { get; set; } = new("http://127.0.0.1:1", "fake");
        public float TimeoutSeconds => 5f;
        public ChatSettings Settings { get; set; } = new()
        {
            ReactChance = 1f, MomentChance = 1f, ReplyChance = 0f, ReactionMinSeconds = 0f, ReactionMaxSeconds = 0f, CharsPerSecond = 1e6f,
        };
    }

    private sealed class Writers : IBotChatWriterFactory
    {
        public readonly ChatRig.Writer Writer = new();
        public int Made;

        public IBotChatWriter Create()
        {
            Made++;
            return Writer;
        }
    }

    /// <summary>The frame seam, answering the rig's tick and defaults else.</summary>
    public class Ticking : Defaults
    {
        public int Tick { get; set; } = 1000;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IEngineFrame.TickCount) ? Tick : base.Invoke(method, args);
    }

    /// <summary>Every registered audience, and the one chain the say path runs.</summary>
    private sealed class Chain<T>(params T[] all) : IHooks<T>
        where T : class
    {
        public IReadOnlyList<T> All => all;

        public void Each(string what, Action<T> call)
        {
            foreach (T hook in all)
                call(hook);
        }
    }

    private sealed class Audience(bool hears) : IChatAudience
    {
        public bool CanHear(EntityHandle listener, EntityHandle speaker) => hears;
    }

    private sealed class Rig
    {
        public readonly Lines Log = new();
        public readonly Players Players = new();
        public readonly Writers Writers = new();
        public readonly Config Config = new();
        public readonly Words WordsSource = new();
        public readonly Hl2mpBots Bots;
        public readonly Hl2mpBotChat Chat;
        public readonly HostSay Say;
        private readonly Ticking _frame;

        public Rig(bool audience = true)
        {
            var entities = DispatchProxy.Create<IEngineEntities, Indexes>();
            IEngineFrame engineFrame = DispatchProxy.Create<IEngineFrame, Ticking>();
            _frame = (Ticking)(object)engineFrame;
            Bots = new Hl2mpBots(Players, new Runner(), Any<IEnginePlayers>(), entities, WeaponCatalog.Stock(), engineFrame, new Navigation(), Any<IBotInventory>(), new Names(), new Tags(), Log);
            Chat = null!;
            var chain = new ChatHookForwarder();
            Say = new HostSay(Log, Any<IEnginePlayers>(), entities, Any<IEntityDirectory>(), new Sender(), new Clients(), Any<IEngineGameEvents>(), Any<IEngineHost>(),
                              chatHooks: new Chain<IPlayerChatHook>(chain), audience: new Chain<IChatAudience>(new Audience(audience)));
            Chat = new Hl2mpBotChat(Bots, Say, Any<IPlayerDirectory>(), new Clients(), entities, engineFrame, new Inventory(), new FightClock(), WordsSource, Writers, Log, Config, Any<IEnginePlayers>());
            chain.Target = Chat;
            Assert.True(SkillPresets.TryGet("normal", out SkillProfile? normal));
            Assert.Null(Bots.Add("Pia Bullseye", normal!));
            Assert.Null(Bots.Add("Tommy Camps Alot", normal!));
        }

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                _frame.Tick++;
                Chat.OnPostThink(0.015f);
            }
        }

        /// <summary>
        /// Ticks until the worker has answered everything it was asked and a
        /// further pass of ticks asks nothing new, then the line, due at once, is
        /// said. A wish is posted some ticks after it is made, so "nothing is
        /// outstanding" on the first tick says only that nothing has been asked yet.
        /// </summary>
        public void Settle()
        {
            long stop = Environment.TickCount64 + 5000;
            long asked = -1;
            while (Environment.TickCount64 < stop)
            {
                Tick(8);
                while (Chat.Counters.Requests != Chat.Counters.Finished && Environment.TickCount64 < stop)
                {
                    Tick();
                    Thread.Sleep(1);
                }

                if (Chat.Counters.Requests == asked)
                    break;
                asked = Chat.Counters.Requests;
            }

            Tick(8);
        }

        public void HumanSays(string text, bool teamOnly = false) => Say.Say(HumanHandle, text, teamOnly);
    }

    /// <summary>The handle's low bits are the fake's entity index.</summary>
    public class Indexes : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IEngineEntities.GetIndex))
                return (int)(((EntityHandle)args![0]!).Value & 0xFFFF);
            Type? t = method?.ReturnType;
            return t is { IsValueType: true } && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }

    /// <summary>The chain is built before the hook it calls.</summary>
    private sealed class ChatHookForwarder : IPlayerChatHook
    {
        public IPlayerChatHook? Target { get; set; }

        public void OnPlayerChat(PlayerChat chat) => Target?.OnPlayerChat(chat);
    }

    [Fact]
    public void OffStartsNothing()
    {
        var r = new Rig();
        r.Config.Enabled = false;
        r.Tick(100);
        r.HumanSays("hey pia");
        r.Chat.Fill(new DeathNoticeFacts(HumanHandle, EntityHandle.None, EntityHandle.None, EntityHandle.None, "x"), Any<IDeathNoticeFields>());
        Assert.Equal((0, 0, 0L), (r.Writers.Made, r.Writers.Writer.Calls, r.Chat.Counters.MainTicks));
        Assert.Null(r.Chat.Director);
    }

    [Fact]
    public void TurningItOffStopsTheWorker()
    {
        var r = new Rig();
        r.Tick(2);
        Assert.NotNull(r.Chat.Director);
        r.Config.Enabled = false;
        r.Tick();
        Assert.Null(r.Chat.Director);
    }

    [Fact]
    public void ANamedBotHearsAPlayerAndAnswersThroughTheSayPath()
    {
        var r = new Rig();
        r.Tick(2);
        int said = r.Say.Said;
        r.HumanSays("hey pia");
        r.Settle();
        Assert.Contains(r.Writers.Writer.Asked, p => p.User.Contains("<chat>hey pia</chat>", StringComparison.Ordinal));
        Assert.Equal(1L, r.Chat.Counters.Said);
        Assert.Contains(r.Log.All, l => l.Contains("\"bot2<12>", StringComparison.Ordinal) && l.Contains("say \"nice one\"", StringComparison.Ordinal));
        Assert.True(r.Say.Said > said, "the say path relayed it");
    }

    [Fact]
    public void ATeamLineIsNotHeardWithoutTeammates()
    {
        var r = new Rig();
        r.Tick(2);
        r.HumanSays("hey pia", teamOnly: true);
        r.Settle();
        Assert.Equal(0, r.Writers.Writer.Calls);
    }

    /// <summary>The dead-chat rule is whatever the say path's audience says (TF2's; HL2MP has none): a listener it vetoes hears nothing.</summary>
    [Fact]
    public void ALineTheAudienceVetoesIsNotHeard()
    {
        var r = new Rig(audience: false);
        r.Tick(2);
        r.HumanSays("hey pia");
        r.Settle();
        Assert.Equal(0, r.Writers.Writer.Calls);
    }

    [Fact]
    public void AKillOnTheFeedIsTheKillersReactionWithTheFeedsWeapon()
    {
        var r = new Rig();
        r.Tick(2);
        EntityHandle pia = r.Players.Made[0].Player;
        r.Chat.Fill(new DeathNoticeFacts(HumanHandle, pia, pia, pia, "357"), Any<IDeathNoticeFields>());
        r.Settle();
        Assert.Contains(r.Writers.Writer.Asked, p => p.User.Contains("You just killed lodle with the magnum.", StringComparison.Ordinal));
    }

    [Fact]
    public void ARefusingServerIsOneConsoleLineAndSilence()
    {
        var r = new Rig();
        r.Writers.Writer.Fails = ChatFailure.ServerDown;
        r.Tick(2);
        for (int i = 0; i < 5; i++)
        {
            r.HumanSays("hey pia");
            r.Settle();
        }

        Assert.Single(r.Log.All, l => l.Contains("ai_chat:", StringComparison.Ordinal));
        Assert.Equal(0L, r.Chat.Counters.Said);
    }

    /// <summary>A new endpoint ends the outage at once: the live run's fix of the URL still sat out the old pause, and its line would not have come back.</summary>
    [Fact]
    public void ANewEndpointIsAskedAtOnce()
    {
        var r = new Rig();
        r.Writers.Writer.Fails = ChatFailure.ServerDown;
        r.Tick(2);
        // The worker's warm-up finds the outage on its own thread. Saying a line before
        // that raced it: the line could be asked for first, and its reply interval then
        // held the second line back, 3 s of ticks this fact never runs.
        long stop = Environment.TickCount64 + 5000;
        while (r.Chat.Director!.PausedUntil <= 0f && Environment.TickCount64 < stop)
        {
            r.Tick();
            Thread.Sleep(1);
        }

        Assert.True(r.Chat.Director.PausedUntil > 0f, "the outage paused the bots");
        int asked = r.Writers.Writer.Calls;

        r.Writers.Writer.Fails = ChatFailure.None;
        r.Config.Target = new ChatTarget("http://127.0.0.1:2", "fake");
        r.Tick(Hl2mpBotChat.ScanTicks + 1);
        Assert.Equal(0f, r.Chat.Director.PausedUntil);
        r.HumanSays("hey pia");
        r.Settle();
        Assert.Equal(asked + 1, r.Writers.Writer.Calls);
        Assert.Equal(1L, r.Chat.Counters.Said);
    }

    [Fact]
    public void ALevelsEndStopsTheWorkerAndTheNextStartsOver()
    {
        var r = new Rig();
        r.Tick(2);
        r.HumanSays("hey pia");
        r.Settle();
        r.Chat.OnLevelShutdownPreEntity();
        Assert.Null(r.Chat.Director);
        r.Chat.OnLevelInitPostEntity("next");
        r.Tick(2);
        Assert.NotNull(r.Chat.Director);
        Assert.Equal(0, r.Chat.Director!.ConversationsOf(2));
    }

    [Fact]
    public void ABadDataFileIsOneConsoleLineAndNoChat()
    {
        var r = new Rig();
        r.WordsSource.Words_ = null;
        r.WordsSource.Refusal = "no chat words";
        r.Tick(5);
        Assert.Single(r.Log.All, l => l.Contains("ai_chat: no chat words", StringComparison.Ordinal));
        Assert.Equal(0, r.Writers.Made);
    }

    /// <summary>
    /// The main thread's bookkeeping near zero, by the counter the route shows:
    /// 300-tick means once warm, with a line going through each. The counter is
    /// wall time, so a busy machine stretches it: the least of five, as
    /// <c>SenseCostTests</c> takes its least.
    /// </summary>
    [Fact]
    public void TheMainThreadsCostIsSmall()
    {
        var r = new Rig();
        r.Tick(2);
        r.HumanSays("hey pia");
        r.Settle();
        Assert.True(r.Chat.Counters.MainTicks > 2, "the counter counts");
        double least = double.MaxValue;
        for (int trial = 0; trial < 5 && least >= 300.0; trial++)
        {
            double total = r.Chat.Counters.MainTotalUs;
            long ticks = r.Chat.Counters.MainTicks;
            r.HumanSays("hey pia");
            r.Tick(300);
            Assert.Equal(ticks + 300, r.Chat.Counters.MainTicks);
            least = Math.Min(least, (r.Chat.Counters.MainTotalUs - total) / 300.0);
        }

        Assert.InRange(least, 0.0001, 300.0);
    }
}
