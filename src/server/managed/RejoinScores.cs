using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A mod's own per-player totals, kept for a player who leaves and written
/// back when they rejoin (<see cref="RejoinScores"/>): TF2's stats, the score
/// its scoreboard shows. A CHAIN: every registered hook captures its own state
/// and gets it back; the core's frags and deaths are <see cref="RejoinScores"/>'s.
/// </summary>
public interface IRejoinScoreHook
{
    /// <summary>What to keep for <paramref name="player"/>, who is leaving; null keeps nothing.</summary>
    object? Capture(EntityHandle player);

    /// <summary>Writes <paramref name="state"/>, a <see cref="Capture"/> answer, onto the rejoining <paramref name="player"/>.</summary>
    void Restore(EntityHandle player, object state);
}

/// <summary>
/// A player's score survives a rejoin (plan_bots_teams_tf2 A.6, ruled
/// 2026-11-02: "a players score should be remembered if they rejoin for hl2dm
/// and tf2 (last 128 players)"). Stock keeps nothing for a player who left;
/// this is ours.
///
/// <para>
/// When a human leaves, their frags and deaths (the scoreboard row,
/// <see cref="PlayerRecord"/>) and every <see cref="IRejoinScoreHook"/>'s
/// state are kept; when they are next in the game, the same match, they are
/// written back before their row is sent. Their team, class and place are not:
/// those go through the join rules, because a team may have filled. A team's
/// own total (team deathmatch's <see cref="IScoring"/>) is its own counter and
/// does not gain the restored frags a second time.
/// </para>
///
/// <para>
/// The last <see cref="Capacity"/> to leave, oldest dropped first, one entry
/// per person (leaving again replaces it), in memory only. Keyed by SteamID64;
/// a client without one (a LAN game's <c>-hushsteam</c>) by its name, and a
/// restore by name says so on the console. Bots are never kept: the roster
/// owns them. A new match clears the table — a map change
/// (<see cref="IGameLifecycle.OnLevelInit"/>), and every reset of the players'
/// scores the rules raise as <see cref="IMatchResetHook"/>: a mode switch, an
/// <c>mp_restartgame</c> restart, TF2's game restart — so no score is brought
/// into a game that began after it.
/// </para>
/// </summary>
public sealed class RejoinScores(IConnectedClients clients, IEnginePlayers players, IPlayerDirectory roster,
                                 IScoreboard scoreboard, IBotPlayers bots, ILog log,
                                 IHooks<IRejoinScoreHook>? hooks = null)
    : IClientLifecycle, IGameLifecycle, IMatchResetHook, IOrderedHook
{
    /// <summary>How many departures are remembered.</summary>
    public const int Capacity = 128;

    private sealed record Kept(string Key, bool ByName, int Frags, int Deaths, (IRejoinScoreHook Hook, object State)[] Mods);

    // Oldest first: a departure appends, the 129th drops the head.
    private readonly List<Kept> _kept = [];

    /// <summary>How many departures are held now.</summary>
    public int Count => _kept.Count;

    int IClientLifecycle.ClientLifecycleOrder => (int)LifecycleStage.RejoinScores;

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.RejoinScores;

    /// <summary>Its place in <see cref="IMatchResetHook"/>'s chain: its lifecycle stage.</summary>
    int IOrderedHook.HookOrder => (int)LifecycleStage.RejoinScores;

    /// <summary>A map change is a new match.</summary>
    void IGameLifecycle.OnLevelInit(string mapName) => Clear("map change");

    /// <summary>The rules reset the players' scores: a new match.</summary>
    void IMatchResetHook.OnMatchReset(string why) => Clear(why);

    /// <summary>Forgets every departure (a new match began).</summary>
    private void Clear(string why)
    {
        if (_kept.Count > 0)
            log.Msg($"[SourceSharp/Rejoin] {why}: forgot {_kept.Count} departed player score(s)\n");
        _kept.Clear();
    }

    public void OnClientDisconnected(int entIndex)
    {
        EntityHandle player = players.GetHandle(entIndex);
        if (player == EntityHandle.None || bots.IsBot(player) || !roster.Knows(player))
            return;

        if (Key(entIndex) is not { } id)
            return;
        var (key, byName) = id;

        PlayerRecord record = roster[player];
        var mods = new List<(IRejoinScoreHook, object)>();
        if (hooks is not null)
        {
            foreach (IRejoinScoreHook hook in hooks.All)
            {
                try
                {
                    if (hook.Capture(player) is { } state)
                        mods.Add((hook, state));
                }
                catch (Exception e)
                {
                    log.Warning($"[SourceSharp/Rejoin] {hook.GetType().FullName}.Capture threw: {e.Message}\n");
                }
            }
        }

        _kept.RemoveAll(k => k.Key == key);
        _kept.Add(new Kept(key, byName, record.Kills, record.Deaths, [.. mods]));
        if (_kept.Count > Capacity)
            _kept.RemoveAt(0);
    }

    public void OnClientActive(int entIndex)
    {
        if (Key(entIndex) is not { } id)
            return;
        string key = id.Key;

        int at = _kept.FindIndex(k => k.Key == key);
        if (at < 0)
            return;

        EntityHandle player = players.GetHandle(entIndex);
        if (player == EntityHandle.None || bots.IsBot(player))
            return;

        Kept kept = _kept[at];
        _kept.RemoveAt(at);

        PlayerRecord record = roster[player];
        record.Kills = kept.Frags;
        record.Deaths = kept.Deaths;

        // The frag limit's tally, by the same arithmetic: a point a frag.
        scoreboard.Add(clients.UserId(entIndex), Math.Max(0, kept.Frags), kept.Deaths, kept.Frags);

        foreach (var (hook, state) in kept.Mods)
        {
            try
            {
                hook.Restore(player, state);
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp/Rejoin] {hook.GetType().FullName}.Restore threw: {e.Message}\n");
            }
        }

        log.Msg($"[SourceSharp/Rejoin] restored {kept.Frags} frag(s), {kept.Deaths} death(s) to client {entIndex}" +
                (kept.ByName ? $" (keyed by name '{key[5..]}': this client has no SteamID)" : "") + "\n");
    }

    /// <summary>The client's key: its SteamID64, else its name; null with neither.</summary>
    private (string Key, bool ByName)? Key(int entIndex)
    {
        ulong steam = clients.SteamId64(entIndex);
        if (steam != 0)
            return ($"steam:{steam}", false);

        string? name = clients.ClientConVar(entIndex, "name");
        return string.IsNullOrEmpty(name) ? null : ($"name:{name}", true);
    }
}
