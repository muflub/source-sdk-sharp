using System.Buffers.Binary;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The server browser's source (plan_gameui_managed.md G3): the A2S codec and
/// client (challenges, the LAN broadcast, timeouts), the remembered servers
/// in Steam or a file, and the browser's tabs, joins and favourites, as
/// stock's ServerBrowser.so behaved.
/// </summary>
public class ServerBrowserSourceTests
{
    internal sealed class Wire : IDatagramTransport
    {
        public List<(ServerAddress To, byte[] Data)> Sent { get; } = [];
        public List<(ushort Port, byte[] Data)> Broadcasts { get; } = [];
        public Queue<(ServerAddress From, byte[] Data)> Inbox { get; } = new();

        public void Send(ServerAddress to, ReadOnlySpan<byte> data) => Sent.Add((to, data.ToArray()));
        public void Broadcast(ushort port, ReadOnlySpan<byte> data) => Broadcasts.Add((port, data.ToArray()));
        public int Receive(Span<byte> buffer, out ServerAddress from)
        {
            from = default;
            if (Inbox.Count == 0)
                return -1;
            var (f, d) = Inbox.Dequeue();
            from = f;
            d.CopyTo(buffer);
            return d.Length;
        }
    }

    internal sealed class ManualTime : TimeProvider
    {
        private long _ms;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _ms;
        public void Advance(int ms) => _ms += ms;
    }

    internal static readonly ServerAddress Here = new(0xC0A8010A, 27015);   // 192.168.1.10:27015

    internal static byte[] Packet(byte header, Action<BinaryWriter> body)
    {
        var s = new MemoryStream();
        var w = new BinaryWriter(s);
        w.Write(-1);
        w.Write(header);
        body(w);
        return s.ToArray();
    }

    internal static void Str(BinaryWriter w, string text)
    {
        w.Write(Encoding.UTF8.GetBytes(text));
        w.Write((byte)0);
    }

    internal static byte[] InfoReply(string name = "Lan Party", string map = "dm_lockdown", string folder = "mod_test",
                                     int players = 3, int max = 16, int bots = 1, bool password = false, bool secure = true,
                                     ushort? gamePort = 27016)
        => Packet(0x49, w =>
        {
            w.Write((byte)17);
            Str(w, name);
            Str(w, map);
            Str(w, folder);
            Str(w, "SourceSharp");
            w.Write((ushort)215);   // the A2S app id field is 16 bits: Source SDK Base's
            w.Write((byte)players);
            w.Write((byte)max);
            w.Write((byte)bots);
            w.Write((byte)'l');
            w.Write((byte)'l');
            w.Write((byte)(password ? 1 : 0));
            w.Write((byte)(secure ? 1 : 0));
            Str(w, "1.0.0.0");
            if (gamePort is { } port)
            {
                w.Write((byte)0x80);
                w.Write(port);
            }
        });

    internal static byte[] Challenge(int c) => Packet(0x41, w => w.Write(c));

    // ---- the codec ----

    [Fact]
    public void An_info_request_is_the_query_string_and_then_the_challenge()
    {
        byte[] plain = A2s.InfoRequest();
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(plain));
        Assert.Equal(0x54, plain[4]);
        Assert.Equal("Source Engine Query\0", Encoding.ASCII.GetString(plain, 5, plain.Length - 5));
        byte[] challenged = A2s.InfoRequest(0x12345678);
        Assert.Equal(plain.Length + 4, challenged.Length);
        Assert.Equal(0x12345678, BinaryPrimitives.ReadInt32LittleEndian(challenged.AsSpan(plain.Length)));
    }

    [Fact]
    public void An_info_reply_parses_with_the_game_port_it_names()
    {
        var reply = A2s.Parse(InfoReply(password: true), 27015);
        Assert.Equal(A2sReplyKind.Info, reply.Kind);
        Assert.Equal(new A2sInfo("Lan Party", "dm_lockdown", "mod_test", "SourceSharp", 3, 16, 1, true, true, 27016), reply.Info);
        Assert.Equal((ushort)27015, A2s.Parse(InfoReply(gamePort: null), 27015).Info!.GamePort);   // no EDF port: the query port
    }

    [Fact]
    public void A_challenge_and_a_player_list_parse()
    {
        Assert.Equal((A2sReplyKind.Challenge, 77), (A2s.Parse(Challenge(77), 1).Kind, A2s.Parse(Challenge(77), 1).Challenge));
        var players = A2s.Parse(Packet(0x44, w =>
        {
            w.Write((byte)2);
            w.Write((byte)0); Str(w, "Gordon"); w.Write(12); w.Write(600f);
            w.Write((byte)1); Str(w, "Alyx"); w.Write(-1); w.Write(59f);
        }), 1);
        Assert.Equal([new ServerPlayer("Gordon", 12, 10), new ServerPlayer("Alyx", -1, 0)], players.Players);
    }

    [Fact]
    public void A_truncated_or_split_reply_is_no_reply()
    {
        byte[] whole = InfoReply();
        Assert.Equal(A2sReplyKind.Unknown, A2s.Parse(whole.AsSpan(0, 12), 1).Kind);
        byte[] split = (byte[])whole.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(split, -2);
        Assert.Equal(A2sReplyKind.Unknown, A2s.Parse(split, 1).Kind);
    }

    [Theory]
    [InlineData("192.168.1.10", true, "192.168.1.10:27015")]
    [InlineData(" 10.0.0.1:27020 ", true, "10.0.0.1:27020")]
    [InlineData("10.0.0.256", false, "")]
    [InlineData("example.com:27015", false, "")]
    [InlineData("1.2.3.4:0", false, "")]
    public void An_address_is_four_numbers_and_a_port(string text, bool ok, string shown)
    {
        Assert.Equal(ok, ServerAddress.TryParse(text, out var a));
        if (ok)
            Assert.Equal(shown, a.ToString());
    }

    // ---- the client ----

    [Fact]
    public void A_challenged_info_query_is_sent_again_with_the_challenge_and_answers_with_its_ping()
    {
        var wire = new Wire();
        var time = new ManualTime();
        var a2s = new A2sClient(wire, time);
        (A2sInfo Info, int Ping)? got = null;
        a2s.InfoReceived += (_, info, ping) => got = (info, ping);
        a2s.QueryInfo(Here);
        wire.Inbox.Enqueue((Here, Challenge(99)));
        a2s.Poll();
        Assert.Equal(A2s.InfoRequest(99), wire.Sent[^1].Data);
        time.Advance(40);
        wire.Inbox.Enqueue((Here, InfoReply()));
        a2s.Poll();
        Assert.Equal(40, got!.Value.Ping);
        Assert.Equal(0, a2s.Pending);
    }

    [Fact]
    public void A_player_query_answers_its_challenge_then_its_list()
    {
        var wire = new Wire();
        var a2s = new A2sClient(wire, new ManualTime());
        IReadOnlyList<ServerPlayer>? got = null;
        a2s.PlayersReceived += (_, p) => got = p;
        a2s.QueryPlayers(Here);
        Assert.Equal(A2s.PlayerRequest(A2s.NoChallenge), wire.Sent[^1].Data);
        wire.Inbox.Enqueue((Here, Challenge(5)));
        a2s.Poll();
        Assert.Equal(A2s.PlayerRequest(5), wire.Sent[^1].Data);
        wire.Inbox.Enqueue((Here, Packet(0x44, w => w.Write((byte)0))));
        a2s.Poll();
        Assert.Empty(got!);
    }

    [Fact]
    public void The_lan_broadcast_goes_to_stocks_six_ports_and_takes_answers_only_in_its_window()
    {
        var wire = new Wire();
        var time = new ManualTime();
        var a2s = new A2sClient(wire, time);
        var heard = new List<ServerAddress>();
        a2s.InfoReceived += (from, _, _) => heard.Add(from);
        a2s.BroadcastInfo();
        Assert.Equal([27015, 27016, 27017, 27018, 27019, 27020], wire.Broadcasts.Select(b => (int)b.Port));
        wire.Inbox.Enqueue((Here, InfoReply()));
        a2s.Poll();
        time.Advance(3000);
        wire.Inbox.Enqueue((new ServerAddress(0x0A000001, 27015), InfoReply()));
        a2s.Poll();
        Assert.Equal([Here], heard);
    }

    [Fact]
    public void An_unanswered_query_times_out()
    {
        var time = new ManualTime();
        var a2s = new A2sClient(new Wire(), time);
        var late = new List<ServerAddress>();
        a2s.TimedOut += late.Add;
        a2s.QueryInfo(Here);
        time.Advance(2000);
        a2s.Poll();
        Assert.Empty(late);
        time.Advance(600);
        a2s.Poll();
        Assert.Equal([Here], late);
        Assert.Equal(0, a2s.Pending);
    }

    // ---- the remembered servers ----

    internal sealed class Files : IOptionsFiles
    {
        public Dictionary<string, byte[]> Stored { get; } = [];
        public byte[]? ReadAll(string fileName) => Stored.GetValueOrDefault(fileName);
        public void WriteAll(string fileName, byte[] bytes) => Stored[fileName] = bytes;
    }

    [Fact]
    public void The_file_store_keeps_favourites_and_history_across_loads()
    {
        var files = new Files();
        var store = new FileServerFavourites(files);
        store.Add(ServerListKind.Favourites, new StoredServer(Here, 27016));
        store.Add(ServerListKind.History, new StoredServer(new ServerAddress(0x0A000001, 27015), 27015));
        store.Add(ServerListKind.Favourites, new StoredServer(Here, 27016));         // once only
        var again = new FileServerFavourites(files);
        Assert.Equal([new StoredServer(Here, 27016)], again.List(ServerListKind.Favourites));
        Assert.Single(again.List(ServerListKind.History));
        again.Remove(ServerListKind.Favourites, new StoredServer(Here, 27016));
        Assert.Empty(new FileServerFavourites(files).List(ServerListKind.Favourites));
    }

    [Fact]
    public void The_file_history_keeps_the_newest_hundred()
    {
        var store = new FileServerFavourites(new Files());
        for (uint i = 0; i < 105; i++)
            store.Add(ServerListKind.History, new StoredServer(new ServerAddress(0x0A000000 + i, 27015), 27015));
        var history = store.List(ServerListKind.History);
        Assert.Equal(100, history.Count);
        Assert.Equal(0x0A000005u, history[0].Query.Ip);
    }

    internal sealed class FakeSteam : ISteamServerBrowser
    {
        public bool Available { get; set; } = true;
        public List<SteamFavourite> Favourites { get; } = [];
        public List<(SteamServerItem Item, bool Responded)> Servers { get; } = [];
        public string? RequestedFor { get; private set; }
        public bool Refreshing { get; set; }

        public bool RequestInternet(string gameDir)
        {
            RequestedFor = gameDir;
            Refreshing = true;
            return Available;
        }
        public int ServerCount => Servers.Count;
        public bool Responded(int i) => Servers[i].Responded;
        public bool TryGetServer(int i, out SteamServerItem item)
        {
            item = Servers[i].Item;
            return true;
        }
        public void ReleaseRequest() { }
        public int FavouriteCount => Favourites.Count;
        public bool TryGetFavourite(int i, out SteamFavourite favourite)
        {
            favourite = Favourites[i];
            return true;
        }
        public void AddFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags)
            => Favourites.Add(new SteamFavourite(ip, connectionPort, queryPort, flags));
        public void RemoveFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags)
            => Favourites.RemoveAll(f => f.Ip == ip && f.QueryPort == queryPort && f.Flags == flags);
    }

    [Fact]
    public void Steams_store_reads_each_list_by_its_flag_and_writes_both_ports()
    {
        var steam = new FakeSteam();
        var store = new SteamServerFavourites(steam);
        store.Add(ServerListKind.Favourites, new StoredServer(Here, 27016));
        Assert.Equal(new SteamFavourite(Here.Ip, 27016, 27015, 1), steam.Favourites.Single());
        steam.Favourites.Add(new SteamFavourite(1, 2, 3, 2));
        Assert.Equal([new StoredServer(Here, 27016)], store.List(ServerListKind.Favourites));
        Assert.Equal([new StoredServer(new ServerAddress(1, 3), 2)], store.List(ServerListKind.History));
        store.Remove(ServerListKind.Favourites, new StoredServer(Here, 27016));
        Assert.Single(steam.Favourites);
    }

    [Fact]
    public void The_lists_are_steams_while_steam_is_up_and_the_files_otherwise()
    {
        var steam = new FakeSteam();
        var files = new Files();
        var both = new ServerFavourites(steam, new SteamServerFavourites(steam), new FileServerFavourites(files));
        both.Add(ServerListKind.Favourites, new StoredServer(Here, 27015));
        Assert.Single(steam.Favourites);
        Assert.Empty(files.Stored);
        steam.Available = false;
        both.Add(ServerListKind.Favourites, new StoredServer(Here, 27015));
        Assert.Single(files.Stored);
        Assert.Single(steam.Favourites);
    }

    // ---- the browser ----

    internal sealed class Rig
    {
        public readonly Wire Wire = new();
        public readonly ManualTime Time = new();
        public readonly FakeSteam Steam = new() { Available = false };
        public readonly Files Files = new();
        public readonly RecordingCommandLine Lines = new();
        public readonly ServerBrowserUi Browser;

        public Rig()
        {
            Browser = new ServerBrowserUi(new A2sClient(Wire, Time), Steam,
                new ServerFavourites(Steam, new SteamServerFavourites(Steam), new FileServerFavourites(Files)),
                Lines, Time, "mod_test");
        }
    }

    [Fact]
    public void The_lan_tab_broadcasts_and_lists_this_games_servers_only()
    {
        var r = new Rig();
        r.Browser.SelectTab(ServerTab.Lan);
        r.Browser.Poll();                                                   // first shown: refreshed
        Assert.Equal(6, r.Wire.Broadcasts.Count);
        Assert.True(r.Browser.Refreshing);
        r.Wire.Inbox.Enqueue((Here, InfoReply()));
        r.Wire.Inbox.Enqueue((new ServerAddress(0x0A000001, 27015), InfoReply(folder: "cstrike")));
        r.Browser.Poll();
        Assert.Equal(1, r.Browser.ServerCount);
        Assert.Equal("Lan Party", r.Browser.Server(0).Name);
        r.Time.Advance(3000);
        r.Browser.Poll();
        Assert.False(r.Browser.Refreshing);
    }

    [Fact]
    public void The_favourites_tab_lists_each_by_address_until_it_answers()
    {
        var r = new Rig();
        r.Browser.AddServer("192.168.1.10");
        r.Browser.SelectTab(ServerTab.Favourites);
        r.Browser.Poll();
        Assert.Equal(("192.168.1.10:27015", ServerBrowserUi.NoAnswerPing), (r.Browser.Server(0).Name, r.Browser.Server(0).Ping));
        Assert.Equal(A2s.InfoRequest(), r.Wire.Sent.Single().Data);
        r.Wire.Inbox.Enqueue((Here, InfoReply(folder: "anything")));        // a favourite of any game answers
        r.Browser.Poll();
        Assert.Equal("Lan Party", r.Browser.Server(0).Name);
    }

    [Fact]
    public void A_join_connects_to_the_game_port_and_a_passworded_one_asks_first()
    {
        var r = new Rig();
        r.Browser.SelectTab(ServerTab.Lan);
        r.Browser.Poll();
        r.Wire.Inbox.Enqueue((Here, InfoReply()));
        r.Browser.Poll();
        r.Browser.Select(0);
        r.Browser.Connect();
        Assert.Equal("connect 192.168.1.10:27016", r.Lines.Lines.Single());

        r.Wire.Inbox.Enqueue((Here, InfoReply(password: true)));
        r.Browser.Poll();
        r.Browser.Connect();
        Assert.True(r.Browser.AskingPassword);
        Assert.Single(r.Lines.Lines);
        r.Browser.SubmitPassword("se\"cret");
        Assert.Equal(["password \"secret\"", "connect 192.168.1.10:27016"], r.Lines.Lines.Skip(1));
        Assert.False(r.Browser.AskingPassword);
    }

    [Fact]
    public void A_server_is_added_to_the_favourites_and_removed_on_the_favourites_tab()
    {
        var r = new Rig();
        r.Browser.SelectTab(ServerTab.Lan);
        r.Browser.Poll();
        r.Wire.Inbox.Enqueue((Here, InfoReply()));
        r.Browser.Poll();
        r.Browser.Select(0);
        Assert.False(r.Browser.SelectedIsFavourite);
        r.Browser.ToggleFavourite();
        Assert.True(r.Browser.SelectedIsFavourite);
        r.Browser.SelectTab(ServerTab.Favourites);
        r.Browser.Poll();
        r.Browser.Select(0);
        r.Browser.ToggleFavourite();
        Assert.Equal(0, r.Browser.ServerCount);
        Assert.Empty(new FileServerFavourites(r.Files).List(ServerListKind.Favourites));
    }

    [Fact]
    public void An_address_that_is_not_one_is_refused()
    {
        var r = new Rig();
        Assert.False(r.Browser.AddServer("not a server"));
        Assert.Empty(r.Files.Stored);
    }

    [Fact]
    public void A_join_reported_by_the_engine_is_remembered_in_the_history()
    {
        var r = new Rig();
        ((IServerHistory)r.Browser).Record(Here.Ip, 27016, 27015);
        Assert.Equal([new StoredServer(Here, 27016)], new FileServerFavourites(r.Files).List(ServerListKind.History));
    }

    [Fact]
    public void The_selected_server_lists_its_players()
    {
        var r = new Rig();
        r.Browser.SelectTab(ServerTab.Lan);
        r.Browser.Poll();
        r.Wire.Inbox.Enqueue((Here, InfoReply()));
        r.Browser.Poll();
        r.Browser.Select(0);
        Assert.Equal(A2s.PlayerRequest(A2s.NoChallenge), r.Wire.Sent[^1].Data);
        r.Wire.Inbox.Enqueue((Here, Packet(0x44, w =>
        {
            w.Write((byte)1);
            w.Write((byte)0); Str(w, "Gordon"); w.Write(3); w.Write(120f);
        })));
        r.Browser.Poll();
        Assert.Equal(new ServerPlayer("Gordon", 3, 2), r.Browser.Player(0));
    }

    [Fact]
    public void The_internet_tab_reads_steams_answered_servers_for_this_game()
    {
        var r = new Rig();
        r.Steam.Available = true;
        r.Steam.Servers.Add((new SteamServerItem(Here.Ip, 27015, 27016, 30, true, "mod_test", "dm_runoff", "SourceSharp", 2, 8, 0,
                                                 false, true, "Over the net"), true));
        r.Steam.Servers.Add((default, false));
        r.Browser.Poll();
        Assert.Equal("mod_test", r.Steam.RequestedFor);
        Assert.Equal(new ServerEntry("Over the net", "SourceSharp", "dm_runoff", 2, 8, 0, 30, false, true), r.Browser.Server(0));
        Assert.Equal(1, r.Browser.ServerCount);
        r.Browser.Select(0);
        r.Browser.Connect();
        Assert.Equal("connect 192.168.1.10:27016", r.Lines.Lines.Single());
    }

    [Fact]
    public void Without_steam_the_internet_tab_is_empty_and_not_refreshing()
    {
        var r = new Rig();
        r.Browser.Poll();
        Assert.False(r.Browser.SteamAvailable);
        Assert.False(r.Browser.Refreshing);
        Assert.Equal(0, r.Browser.ServerCount);
    }

    [Fact]
    public void Sorting_by_the_sorted_column_turns_it_over()
    {
        var r = new Rig();
        Assert.Equal((ServerSort.Ping, false), (r.Browser.Sort, r.Browser.Descending));
        r.Browser.SortBy(ServerSort.Ping);
        Assert.True(r.Browser.Descending);
        r.Browser.SortBy(ServerSort.Name);
        Assert.Equal((ServerSort.Name, false), (r.Browser.Sort, r.Browser.Descending));
    }
}
