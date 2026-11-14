extern alias client;
using RecordingEngine = client::SourceSharp.Abi.RecordingEngine;
// The client's copies of the shared interop types this file hands to client
// code; each side compiles its own (plan_shim_areas_split phase 3).
using IGameClock = client::SourceSharp.Abi.IGameClock;
using ISteamAuthTickets = client::SourceSharp.Abi.ISteamAuthTickets;
using SteamAuthTicket = client::SourceSharp.Abi.SteamAuthTicket;
// The reading side: the client's own copy of the shared messages (each side
// compiles them, plan_side_isolation §2.4).
using client::TF2.Descent.Client;
using client::TF2.Descent.Shared;
// The HUD contract is the client core's, whose copy of the shared HUD
// vocabulary is its own (plan_side_isolation P7).
using HudMessageArgs = client::SourceSharp.Shared.HudMessageArgs;
using IUserMessage = client::SourceSharp.Abi.IUserMessage;
using UserMessageReader = client::SourceSharp.Abi.UserMessageReader;
using ClientKey = client::SourceSharp.Abi.UserMessageKey;
using Color32 = client::SourceSharp.Abi.Color32;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using SourceSharp.Shared;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// TF2.Descent.Shared's messages, written by the server and read on the client,
/// and the client's hooks that act on them (plan_host_sdk §2.1, §2.11).
/// </summary>
public sealed class ClientMessageTests
{
    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Seen : INoticeHook, IInstanceHook
    {
        public List<string> Calls { get; } = new();
        public void OnNotice(string text, string arg) => Calls.Add(arg.Length == 0 ? "notice " + text : $"notice {text} [{arg}]");
        public void OnInstance(string kind, int depth) => Calls.Add($"instance {kind} {depth}");
    }

    private static UserMessageReader Reader(byte[] bytes) => new(bytes);

    private static byte[] Written<T>(T payload) where T : client::SourceSharp.Abi.IUserMessagePayload<T>
    {
        var buffer = new byte[255];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits a user message");
        return buffer[..n];
    }

    private static void Dispatch(IUserMessage handler, byte[] bytes) => handler.OnReceived(Reader(bytes));

    //-------------------------------------------------------------------------
    // The layouts, each pinned once: a change to one has to change its line.
    //-------------------------------------------------------------------------

    [Fact]
    public void The_notice_layout_is_its_text_then_its_argument()
        => Assert.Equal([(byte)'h', (byte)'i', 0, (byte)'4', 0], Written(new Notice { Text = "hi", Arg = "4" }));

    [Fact]
    public void The_instance_layout_is_kind_then_a_short_depth()
        => Assert.Equal([.. "level"u8, 0, 7, 0], Written(new Instance { Kind = "level", Depth = 7 }));

    [Fact]
    public void The_next_level_layout_is_depth_map_url_then_a_counted_hash()
    {
        Assert.Equal([2, 0, (byte)'m', 0, (byte)'u', 0, 0],
                     Written(new NextLevel { Depth = 2, Map = "m", Url = "u" }));

        byte[] withHash = Written(new NextLevel { Depth = 2, Map = "m", Url = "u", Sha256 = new byte[32] });
        Assert.Equal(6 + 1 + 32, withHash.Length);
        Assert.Equal(32, withHash[6]);
    }

    //-------------------------------------------------------------------------
    // Round trips through the handlers.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_notice_round_trips_and_reaches_the_hook()
    {
        var seen = new Seen();

        Dispatch(new NoticeMessage(new Hooks<INoticeHook>(seen)), Written(new Notice { Text = "the cage is being readied" }));

        Assert.Equal(["notice the cage is being readied"], seen.Calls);
    }

    [Fact]
    public void A_token_notice_round_trips_with_its_argument()
    {
        var seen = new Seen();

        Dispatch(new NoticeMessage(new Hooks<INoticeHook>(seen)), Written(new Notice { Text = "#Descent_Travel_Waiting", Arg = "12" }));

        Assert.Equal(["notice #Descent_Travel_Waiting [12]"], seen.Calls);
    }

    [Fact]
    public void A_long_notice_argument_is_cut_to_its_limit_and_the_message_fits()
    {
        var reader = Reader(Written(new Notice { Text = new string('x', 500), Arg = new string('y', 500) }));

        Assert.Equal(Notice.MaxArg, Notice.Read(ref reader).Arg.Length);
        Assert.False(reader.Overran);
    }

    [Fact]
    public void A_long_notice_is_cut_to_the_limit()
    {
        var reader = Reader(Written(new Notice { Text = new string('x', 500) }));

        Assert.Equal(Notice.MaxText, Notice.Read(ref reader).Text.Length);
        Assert.False(reader.Overran);
    }

    [Fact]
    public void The_instance_round_trips_and_reaches_the_hook()
    {
        var seen = new Seen();

        Dispatch(new InstanceMessage(new Hooks<IInstanceHook>(seen)), Written(new Instance { Kind = "level", Depth = 7 }));

        Assert.Equal(["instance level 7"], seen.Calls);
    }

    [Fact]
    public void A_truncated_instance_message_reaches_no_hook()
    {
        byte[] bytes = Written(new Instance { Kind = "level", Depth = 7 });
        var seen = new Seen();

        Dispatch(new InstanceMessage(new Hooks<IInstanceHook>(seen)), bytes[..^1]);

        Assert.Empty(seen.Calls);
    }

    [Fact]
    public void A_depth_is_clamped_into_a_short()
    {
        Assert.Equal((short)0, Instance.DepthOf(-4));
        Assert.Equal(short.MaxValue, Instance.DepthOf(int.MaxValue));
        Assert.Equal((short)9, Instance.DepthOf(9));
    }

    [Fact]
    public void Each_side_compiles_the_messages_under_the_same_keys()
    {
        // No shared assembly (plan_side_isolation §2.4): each side has its own
        // copy of the type, and what the two must agree on is the key.
        Assert.Equal("TF2.Descent.Server", typeof(global::TF2.Descent.Shared.NoticeMessage).Assembly.GetName().Name);
        Assert.Equal("TF2.Descent.Client", typeof(NoticeMessage).Assembly.GetName().Name);

        // Each side keys its own copy with its own core's UserMessageKey: the
        // core's shared source is per side too (plan_side_isolation P7).
        Assert.Equal("TF2.Descent:notice", ClientKey.Of<NoticeMessage>());
        Assert.Equal(UserMessageKey.Of<global::TF2.Descent.Shared.NoticeMessage>(), ClientKey.Of<NoticeMessage>());
        Assert.Equal(UserMessageKey.Of<global::TF2.Descent.Shared.InstanceMessage>(), ClientKey.Of<InstanceMessage>());
    }

    //-------------------------------------------------------------------------
    // The client's hooks
    //-------------------------------------------------------------------------

    private sealed class NullLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private sealed class Hud : IHudMessages
    {
        public List<HudMessageArgs> Shown { get; } = new();
        public void Show(HudMessageArgs args, float now) => Shown.Add(args);
        public void ShowTitle(string name, TextMessage entry, float now) { }
        public void ShowTitle(string name, string text, string clears, float x, float y, Color32 color,
                              float fadeIn, float fadeOut, float hold, float now) { }
        public int ActiveCount(float now) => Shown.Count;
        public int ActiveTitleCount(float now) => 0;
    }

    private sealed class Clock : IGameClock
    {
        public float Now => 12f;
    }

    /// <summary>
    /// The client's container with the UI composed: it answers the mod's
    /// language file as the UI host does (null for a token it lacks), and
    /// nothing else.
    /// </summary>
    private sealed class Tokens(params (string Token, string Text)[] rows)
        : client::SourceSharp.Client.Ui.IUiLocalisation, client::SourceSharp.Abi.IServiceLocator
    {
        public string? Find(string token) => rows.FirstOrDefault(r => r.Token == token).Text;

        public object? Resolve(Type serviceType) => serviceType == typeof(client::SourceSharp.Client.Ui.IUiLocalisation) ? this : null;
        public object? ResolveNamed(string name) => null;
        public bool IsRegistered(Type serviceType) => Resolve(serviceType) is not null;
        public IReadOnlyCollection<Type> ServiceTypes => [typeof(client::SourceSharp.Client.Ui.IUiLocalisation)];
        public IReadOnlyCollection<string> Names => [];
    }

    /// <summary>The client's container before the UI is composed: no localiser.</summary>
    private sealed class NoUi : client::SourceSharp.Abi.IServiceLocator
    {
        public object? Resolve(Type serviceType) => null;
        public object? ResolveNamed(string name) => null;
        public bool IsRegistered(Type serviceType) => false;
        public IReadOnlyCollection<Type> ServiceTypes => [];
        public IReadOnlyCollection<string> Names => [];
    }

    [Fact]
    public void With_no_ui_composed_a_token_notice_draws_as_itself()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), new NoUi());

        notices.OnNotice("#Descent_Travel_Cage", "");

        Assert.Equal(["#Descent_Travel_Cage"], hud.Shown.Select(s => s.Text));
    }

    [Fact]
    public void Notices_and_the_instance_line_go_to_their_own_hud_channels()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), new Tokens());

        notices.OnNotice("hello", "");
        notices.OnInstance("hub", 0);
        notices.OnInstance("level", 99);

        Assert.Equal([HudNotices.NoticeChannel, HudNotices.InstanceChannel, HudNotices.InstanceChannel], hud.Shown.Select(s => s.Channel));
        Assert.Equal(["hello", "Town", "Depth 99"], hud.Shown.Select(s => s.Text));
    }

    //-------------------------------------------------------------------------
    // The level's name on arrival (mine plan E4).
    //-------------------------------------------------------------------------

    private static Tokens MineNames() => new(
        ("#Descent_Depth_Act_1", "Act I: The Mann Co. Mine"),
        ("#Descent_Depth_Level_3", "Timber Stopes"),
        ("#Descent_Depth_Act_3", "Act III: Below"),
        ("#Descent_Depth_Level_14", "The Rot"),
        ("#Descent_Arrival_Depth", "Depth %s1: %s2"));

    [Fact]
    public void Arriving_at_a_level_shows_its_act_and_its_name()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), MineNames());

        notices.OnInstance("level", 3);

        Assert.Equal(["Act I: The Mann Co. Mine\nDepth 3: Timber Stopes"], hud.Shown.Select(s => s.Text));
        Assert.Equal([HudNotices.InstanceChannel], hud.Shown.Select(s => s.Channel));
    }

    [Fact]
    public void A_re_sent_instance_shows_the_name_no_more()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), MineNames());

        notices.OnInstance("level", 3);
        notices.OnInstance("level", 3);

        Assert.Single(hud.Shown);
    }

    [Fact]
    public void After_the_level_ends_the_next_arrival_is_shown_again()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), MineNames());
        client::SourceSharp.Client.IClientLevelHook level = notices;

        notices.OnInstance("level", 3);
        level.OnLevelShutdown();
        notices.OnInstance("level", 3);

        Assert.Equal(2, hud.Shown.Count);
    }

    [Fact]
    public void A_deeper_level_is_a_new_arrival_named_for_its_own_act()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), MineNames());

        notices.OnInstance("level", 3);
        notices.OnInstance("level", 14);

        Assert.Equal("Act III: Below\nDepth 14: The Rot", hud.Shown[^1].Text);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(15, 3)]
    [InlineData(0, 0)]
    [InlineData(16, 0)]
    public void Five_levels_make_an_act(int depth, int act)
        => Assert.Equal(act, LevelNames.ActOf(depth));

    [Fact]
    public void A_token_notice_is_drawn_from_the_language_file_with_its_argument()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), new Tokens(("#Descent_Travel_Waiting", "Ready in about %s1 s.")));

        notices.OnNotice("#Descent_Travel_Waiting", "12");

        Assert.Equal(["Ready in about 12 s."], hud.Shown.Select(s => s.Text));
    }

    [Fact]
    public void A_token_the_language_file_lacks_is_drawn_as_itself()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), new Tokens());

        notices.OnNotice("#Descent_Travel_Nowhere", "");

        Assert.Equal(["#Descent_Travel_Nowhere"], hud.Shown.Select(s => s.Text));
    }

    [Fact]
    public void A_plain_notice_is_never_looked_up_or_filled()
    {
        var hud = new Hud();
        var notices = new HudNotices(hud, new Clock(), new Tokens(("hello %s1", "looked up")));

        notices.OnNotice("hello %s1", "x");

        Assert.Equal(["hello %s1"], hud.Shown.Select(s => s.Text));
    }

    private sealed class SeenReauth : IReauthHook
    {
        public List<string> Calls { get; } = new();
        public void OnReauth(string nonce, ulong gameServerSteamId) => Calls.Add($"{nonce} {gameServerSteamId}");
    }

    [Fact]
    public void A_reauth_round_trips_and_reaches_the_hook()
    {
        byte[] bytes = Written(ReauthMessage.Of("0123456789abcdef0123456789abcdef", 90071992547409920UL)!.Value);
        var seen = new SeenReauth();

        Dispatch(new ReauthMessage(new Hooks<IReauthHook>(seen)), bytes);

        Assert.Equal(["0123456789abcdef0123456789abcdef 90071992547409920"], seen.Calls);
        // The bytes main's hand-written writer produced: two NUL-terminated strings.
        Assert.Equal(System.Text.Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef\090071992547409920\0"), bytes);
        Assert.Null(ReauthMessage.Of("no spaces allowed", 1));
    }

    [Fact]
    public void A_ticket_reply_round_trips_and_refuses_what_is_not_one()
    {
        byte[] ticket = Enumerable.Range(0, 240).Select(i => (byte)i).ToArray();

        Assert.True(TicketReply.TryRead(TicketReply.Write("abc-123_X", ticket), out string nonce, out byte[] read));
        Assert.Equal("abc-123_X", nonce);
        Assert.Equal(ticket, read);

        Assert.Null(TicketReply.Write("", ticket));
        Assert.Null(TicketReply.Write("abc", []));
        Assert.Null(TicketReply.Write("abc", new byte[TicketReply.MaxTicket + 1]));
        Assert.False(TicketReply.TryRead([], out _, out _));
        Assert.False(TicketReply.TryRead([3, (byte)'a', (byte)'b', (byte)'c'], out _, out _));     // no ticket
        Assert.False(TicketReply.TryRead([9, (byte)'a', 1], out _, out _));                       // nonce past the end
        Assert.False(TicketReply.TryRead([1, (byte)' ', 1], out _, out _));                       // not a nonce
    }

    private sealed class FakeTickets : ISteamAuthTickets
    {
        public List<string> Calls { get; } = new();
        public bool Fail { get; set; }
        private uint _next = 7;
        public SteamAuthTicket? Create(ulong gameServerSteamId, out string why)
        {
            Calls.Add($"create {gameServerSteamId}");
            why = Fail ? "no Steam" : "";
            return Fail ? null : new SteamAuthTicket(_next++, [0xAA, 0xBB]);
        }
        public void Cancel(uint handle) => Calls.Add($"cancel {handle}");
    }

    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    [Fact]
    public void The_client_mints_a_ticket_for_the_gateway_and_sends_it_back_cancelling_the_last()
    {
        var tickets = new FakeTickets();
        var net = new RecordingEngine().Returns("Networking.SendServerMessage", true);
        var responder = new ReauthResponder(tickets, net, new ListLog());

        responder.OnReauth("n1", 90071992547409920UL);
        responder.OnReauth("n2", 90071992547409920UL);

        Assert.Equal(["create 90071992547409920", "cancel 7", "create 90071992547409920"], tickets.Calls);
        var sends = net.Calls.Where(c => c.Member == "Networking.SendServerMessage").ToList();
        Assert.Equal(2, sends.Count);
        Assert.Equal(TicketReply.Key, sends[1].Arg(0));
        Assert.True(TicketReply.TryRead((byte[])sends[1].Arg(1)!, out string nonce, out byte[] bytes));
        Assert.Equal("n2", nonce);
        Assert.Equal([0xAA, 0xBB], bytes);
    }

    [Fact]
    public void A_client_without_a_ticket_sends_nothing_and_says_why()
    {
        var tickets = new FakeTickets { Fail = true };
        var net = new RecordingEngine();
        var log = new ListLog();

        new ReauthResponder(tickets, net, log).OnReauth("n1", 1);

        Assert.DoesNotContain(net.Calls, c => c.Member == "Networking.SendServerMessage");
        Assert.Contains(log.Lines, l => l.Contains("no Steam"));
    }
}
