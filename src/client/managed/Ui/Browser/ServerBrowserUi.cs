using SourceSharp.Abi;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>The servers joined, as the GameUI's OnConnectToServer2 reports them (stock's history tab).</summary>
public interface IServerHistory
{
    void Record(uint ip, ushort connectionPort, ushort queryPort);
}

/// <summary>
/// The server browser's data and actions (plan_gameui_managed.md G3; stock's
/// ServerBrowser.so). Internet: Steam's internet list for this game's
/// directory, read as servers answer. LAN: an A2S_INFO broadcast on the LAN
/// ports, servers of this game's directory. Favourites and History: the
/// remembered servers (<see cref="IServerFavourites"/>: Steam's while Steam
/// is up, a file otherwise), each queried with A2S_INFO and shown by its
/// address until it answers. The selected server's players come from
/// A2S_PLAYER. Each tab keeps its list until refreshed; a tab is refreshed
/// the first time it is shown. Join: <c>connect a.b.c.d:port</c>, after
/// <c>password "…"</c> for a passworded server, as stock's password dialog did.
/// </summary>
public sealed class ServerBrowserUi : IServerListSource, IServerQuerySource, IServerFilterSource, IServerDetailsSource,
                                      IServerSortSource, IServerBrowserState, IServerBrowserActions, IServerHistory
{
    /// <summary>How often a refreshing internet list is read again, seconds.</summary>
    public const double SteamReadSeconds = 0.5;

    /// <summary>The ping a server that has not answered is shown and sorted with.</summary>
    public const int NoAnswerPing = 999;

    private readonly A2sClient _a2s;
    private readonly ISteamServerBrowser _steam;
    private readonly IServerFavourites _favourites;
    private readonly IUiCommandLine _commands;
    private readonly TimeProvider _time;
    private readonly string _gameDir;

    private readonly List<(ServerAddress Query, ushort GamePort, ServerEntry Entry)>[] _lists = [[], [], [], []];
    private readonly bool[] _visited = new bool[4];
    private readonly HashSet<int> _steamAdded = [];
    private readonly List<ServerPlayer> _players = [];
    private long _steamReadAt = long.MinValue;
    private bool _refreshing;
    private int _queried;

    public ServerBrowserUi(A2sClient a2s, ISteamServerBrowser steam, IServerFavourites favourites, IUiCommandLine commands,
                           TimeProvider time, string gameDir)
    {
        _a2s = a2s;
        _steam = steam;
        _favourites = favourites;
        _commands = commands;
        _time = time;
        _gameDir = gameDir;
        _a2s.InfoReceived += OnInfo;
        _a2s.PlayersReceived += OnPlayers;
    }

    private List<(ServerAddress Query, ushort GamePort, ServerEntry Entry)> Current => _lists[(int)Tab];

    // ---- the lists ----

    public int ServerCount => Current.Count;

    public ServerEntry Server(int i) => Current[i].Entry;

    public int Version { get; private set; } = 1;

    public ServerTab Tab { get; private set; } = ServerTab.Internet;

    public bool Refreshing => _refreshing;

    public float RefreshProgress
        => Tab == ServerTab.Internet ? (_steam.Refreshing ? 0.5f : 1f)
         : Tab == ServerTab.Lan ? (_a2s.Broadcasting ? 0.5f : 1f)
         : _queried == 0 ? 1f : 1f - (float)_a2s.Pending / _queried;

    public string MapContains { get; private set; } = string.Empty;

    public bool NotFull { get; private set; }

    public bool NotEmpty { get; private set; }

    public bool NoPassword { get; private set; }

    public int MaxPing { get; private set; }

    public ServerSort Sort { get; private set; } = ServerSort.Ping;

    public bool Descending { get; private set; }

    public int Selected { get; private set; } = -1;

    public int PlayerCount => _players.Count;

    public ServerPlayer Player(int i) => _players[i];

    public bool SteamAvailable => _steam.Available;

    public bool SelectedIsFavourite
        => (uint)Selected < (uint)Current.Count && IsFavourite(Current[Selected].Query);

    public bool AskingPassword { get; private set; }

    public string PasswordServer => (uint)Selected < (uint)Current.Count ? Current[Selected].Entry.Name : string.Empty;

    // ---- the actions ----

    public void Poll()
    {
        if (!_visited[(int)Tab])
            Refresh();
        _a2s.Poll();
        if (Tab == ServerTab.Internet && _refreshing)
            ReadSteam(final: !_steam.Refreshing);
        _refreshing = Tab switch
        {
            ServerTab.Internet => _refreshing && _steam.Refreshing,
            ServerTab.Lan => _a2s.Broadcasting,
            _ => _a2s.Pending > 0,
        };
    }

    public void SelectTab(ServerTab tab)
    {
        if (tab == Tab)
            return;
        _a2s.Cancel();
        _refreshing = false;
        Tab = tab;
        Selected = -1;
        _players.Clear();
        AskingPassword = false;
        Version++;
    }

    public void SetMapContains(string text) => MapContains = text;

    public void SetNotFull(bool on) => NotFull = on;

    public void SetNotEmpty(bool on) => NotEmpty = on;

    public void SetNoPassword(bool on) => NoPassword = on;

    public void SetMaxPing(int ms) => MaxPing = ms;

    public void SortBy(ServerSort column)
    {
        if (column == Sort)
            Descending = !Descending;
        else
        {
            Sort = column;
            Descending = false;
        }
    }

    public void Select(int index)
    {
        if ((uint)index >= (uint)Current.Count)
            return;
        Selected = index;
        _players.Clear();
        _a2s.QueryPlayers(Current[index].Query);
    }

    public void Refresh()
    {
        _visited[(int)Tab] = true;
        _a2s.Cancel();
        Current.Clear();
        Selected = -1;
        _players.Clear();
        Version++;
        _refreshing = true;
        switch (Tab)
        {
            case ServerTab.Internet:
                _steamAdded.Clear();
                _refreshing = _steam.RequestInternet(_gameDir);
                _steamReadAt = long.MinValue;
                break;
            case ServerTab.Lan:
                _a2s.BroadcastInfo();
                break;
            default:
                var kind = Tab == ServerTab.Favourites ? ServerListKind.Favourites : ServerListKind.History;
                var stored = _favourites.List(kind);
                _queried = stored.Count;
                foreach (var s in stored)
                {
                    Current.Add((s.Query, s.GamePort, Unanswered(s.Query)));
                    _a2s.QueryInfo(s.Query);
                }
                _refreshing = stored.Count > 0;
                break;
        }
    }

    public void Connect()
    {
        if ((uint)Selected >= (uint)Current.Count)
            return;
        if (Current[Selected].Entry.Password)
        {
            AskingPassword = true;
            return;
        }
        Join();
    }

    public void SubmitPassword(string password)
    {
        if (!AskingPassword)
            return;
        AskingPassword = false;
        _commands.Submit("password \"" + password.Replace("\"", "", StringComparison.Ordinal) + "\"");
        Join();
    }

    public void CancelPassword() => AskingPassword = false;

    public void ToggleFavourite()
    {
        if ((uint)Selected >= (uint)Current.Count)
            return;
        var (query, gamePort, _) = Current[Selected];
        if (Tab == ServerTab.Favourites)
        {
            _favourites.Remove(ServerListKind.Favourites, new StoredServer(query, gamePort));
            Current.RemoveAt(Selected);
            Selected = -1;
            _players.Clear();
            Version++;
        }
        else if (!IsFavourite(query))
            _favourites.Add(ServerListKind.Favourites, new StoredServer(query, gamePort));
    }

    public bool AddServer(string address)
    {
        if (!ServerAddress.TryParse(address, out var query))
            return false;
        _favourites.Add(ServerListKind.Favourites, new StoredServer(query, query.Port));
        if (Tab == ServerTab.Favourites && Current.FindIndex(r => r.Query == query) < 0)
        {
            Current.Add((query, query.Port, Unanswered(query)));
            _a2s.QueryInfo(query);
            _queried++;
            Version++;
        }
        return true;
    }

    public void Record(uint ip, ushort connectionPort, ushort queryPort)
        => _favourites.Add(ServerListKind.History, new StoredServer(new ServerAddress(ip, queryPort), connectionPort));

    // ---- the answers ----

    private void OnInfo(ServerAddress from, A2sInfo info, int ping)
    {
        var list = Current;
        int at = list.FindIndex(r => r.Query == from);
        if (at < 0)
        {
            // Only the LAN tab takes servers nobody named, and only this game's.
            if (Tab != ServerTab.Lan || !string.Equals(info.Folder, _gameDir, StringComparison.OrdinalIgnoreCase))
                return;
            list.Add((from, info.GamePort, Entry(info, ping)));
        }
        else
            list[at] = (from, info.GamePort, Entry(info, ping));
        Version++;
    }

    private void OnPlayers(ServerAddress from, IReadOnlyList<ServerPlayer> players)
    {
        if ((uint)Selected >= (uint)Current.Count || Current[Selected].Query != from)
            return;
        _players.Clear();
        _players.AddRange(players);
    }

    private void ReadSteam(bool final)
    {
        if (!final && _steamReadAt != long.MinValue && _time.GetElapsedTime(_steamReadAt).TotalSeconds < SteamReadSeconds)
            return;
        _steamReadAt = _time.GetTimestamp();
        int count = _steam.ServerCount;
        bool added = false;
        for (int i = 0; i < count; i++)
        {
            if (_steamAdded.Contains(i) || !_steam.Responded(i) || !_steam.TryGetServer(i, out var s))
                continue;
            _steamAdded.Add(i);
            _lists[(int)ServerTab.Internet].Add((new ServerAddress(s.Ip, s.QueryPort), s.ConnectionPort,
                new ServerEntry(s.Name, s.Description, s.Map, s.Players, s.MaxPlayers, s.Bots, s.Ping, s.Password, s.Secure)));
            added = true;
        }
        if (added)
            Version++;
    }

    private void Join()
    {
        var (query, gamePort, _) = Current[Selected];
        _commands.Submit("connect " + new ServerAddress(query.Ip, gamePort == 0 ? query.Port : gamePort));
    }

    private bool IsFavourite(ServerAddress query)
    {
        foreach (var s in _favourites.List(ServerListKind.Favourites))
            if (s.Query == query)
                return true;
        return false;
    }

    private static ServerEntry Entry(A2sInfo info, int ping)
        => new(info.Name, info.Game, info.Map, info.Players, info.MaxPlayers, info.Bots, ping, info.Password, info.Secure);

    private static ServerEntry Unanswered(ServerAddress query)
        => new(query.ToString(), string.Empty, string.Empty, 0, 0, 0, NoAnswerPing, false, false);
}
