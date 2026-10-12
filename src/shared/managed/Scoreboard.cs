using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The tally, and nothing else.
///
/// It used to decide as well as count — it subscribed to the death event and
/// worked out for itself who deserved a point. That put a game rule in Shared,
/// where the client can see it and where nothing else about the game's policy
/// lives, and it meant the number the frag limit read was arrived at by
/// different arithmetic from the number the engine puts on a player. Deciding
/// is now <see cref="SourceSharp.Server"/>'s GameRules; this holds the result.
///
/// Still keyed by USERID rather than by handle, because that is the identity
/// that survives the thing it has to survive: a player who dies, respawns and
/// reconnects mid-round. Handles do not.
///
/// <para>
/// AN INSTANCE, not a static, and it was made one because of what the static
/// did to the tests. A scoreboard nobody owns is a scoreboard every test
/// shares: one suite's kills were still on the board when the next ran, so each
/// test began by calling Reset and any that forgot was reading somebody else's
/// numbers. There is one per side per load, the module owns it, and a test that
/// wants a board makes one.
/// </para>
/// </summary>
public sealed class Scoreboard : IScoreboard
{
    // Where a disconnect this board cannot place is said (statics to zero).
    private readonly ILog _log;

    /// <summary>What one player has done this round.</summary>
    public readonly record struct Entry(int Kills, int Deaths, int Score);

    private readonly IEngineHost _host;
    private readonly IGameEvent<PlayerDisconnectArgs> _disconnects;

    /// <summary>
    /// The host, for the one thing this asks it: which side it is on; and this
    /// side's disconnect event, which <see cref="Subscribe"/> listens to.
    /// </summary>
    public Scoreboard(IEngineHost host, IGameEvent<PlayerDisconnectArgs> disconnects, ILog log)
    {
        _log = log;
        _host = host;
        _disconnects = disconnects;
    }

    private readonly Dictionary<int, Entry> _board = new();
    private bool _subscribed;

    /// <summary>
    /// Kills per attacker userid. A view built on demand, for diagnostics and
    /// tests — the rules read <see cref="Best"/>, which allocates nothing.
    /// </summary>
    public IReadOnlyDictionary<int, int> Kills
        => _board.ToDictionary(pair => pair.Key, pair => pair.Value.Kills);

    /// <summary>Everyone with anything recorded against them.</summary>
    public IEnumerable<(int UserId, Entry Entry)> All
        => _board.Select(pair => (pair.Key, pair.Value));

    /// <summary>
    /// The highest score on the board and whose it is, or a zero userid when
    /// nobody has scored.
    ///
    /// Here rather than as a fold over <see cref="All"/> at the call site
    /// because the frag limit asks it every server frame, and an enumerator
    /// allocated once a tick for a question this small is the kind of cost
    /// that only shows up as a sawtooth in a profile an hour later.
    /// </summary>
    public (int UserId, int Score) Best()
    {
        var best = (UserId: 0, Score: 0);

        foreach (var pair in _board)
        {
            if (best.UserId == 0 || pair.Value.Score > best.Score)
                best = (pair.Key, pair.Value.Score);
        }

        return best;
    }

    /// <summary>
    /// Subscribes to the events it cares about. Guarded so that a second call
    /// cannot add a second subscription and drop a leaver twice.
    /// </summary>
    public void Subscribe()
    {
        if (_subscribed)
            return;

        _subscribed = true;

        _disconnects.Fired += OnPlayerDisconnect;
    }

    public int KillsFor(int userId) => Of(userId).Kills;
    public int DeathsFor(int userId) => Of(userId).Deaths;
    public int ScoreFor(int userId) => Of(userId).Score;

    public Entry Of(int userId)
        => _board.TryGetValue(userId, out var entry) ? entry : default;

    /// <summary>
    /// Adds to what is recorded against a player.
    ///
    /// Deltas rather than absolutes, and three of them rather than one, because
    /// a kill and a point are not the same event: a suicide is a death that
    /// costs a point and is nobody's kill, and a board that stored one number
    /// could not say so.
    /// </summary>
    public void Add(int userId, int kills, int deaths, int score)
    {
        if (userId == 0)
            return;   // the world, which the engine names with userid 0

        var entry = Of(userId);

        _board[userId] = new Entry(entry.Kills + kills,
                                    entry.Deaths + deaths,
                                    entry.Score + score);
    }

    public void Reset() => _board.Clear();

    private void OnPlayerDisconnect(PlayerDisconnectArgs left)
    {
        // Server only. The client sees a networked copy of the same event, and
        // acting on it there as well would drop a board the client is drawing.
        if (_host.ActiveSide != Side.Server)
            return;

        if (_board.Remove(left.UserId))
            _log.Msg($"[SourceSharp/Score] dropped score for userid={left.UserId} ('{left.Name}')\n");
    }
}
