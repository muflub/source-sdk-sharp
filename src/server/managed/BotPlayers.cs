using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>A bot this server added: its player, its slot, and the name the engine gave it.</summary>
public readonly record struct BotPlayer(EntityHandle Player, int EntIndex, string Name);

/// <summary>
/// Bots as players (plan_hl2mp_ai §3.1): a bot is an engine client the server
/// made for itself, so it has a slot, a scoreboard row and the human join
/// path. Generic: what a bot does is its mod's; this only makes, names, marks
/// and removes the client.
/// </summary>
public interface IBotPlayers
{
    /// <summary>
    /// Makes a client called <paramref name="name"/> through the engine and
    /// answers its player, or <see cref="EntityHandle.None"/> with
    /// <paramref name="refusal"/> saying why (an empty name, a full server). The
    /// player carries <c>FL_FAKECLIENT</c>; <c>name</c> and each of
    /// <paramref name="userInfo"/> are set as its USERINFO convars, as stock
    /// HL2MP sets <c>name</c> and <c>cl_playermodel</c> (hl2mp_bot.cpp:256-257).
    /// A non-zero <paramref name="team"/> is the team the bot is brought in
    /// for, which <see cref="IWantedTeams"/> answers from its first spawn on.
    /// </summary>
    EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0);

    /// <summary>Whether <paramref name="player"/> carries <c>FL_FAKECLIENT</c> (stock's CBasePlayer::IsBot).</summary>
    bool IsBot(EntityHandle player);

    /// <summary>Kicks a bot this server added, as <c>kickid</c> does; false for any other player.</summary>
    bool Kick(EntityHandle bot, string reason);

    /// <summary>The bots in the game, in the order they were added.</summary>
    IReadOnlyList<BotPlayer> Bots { get; }
}

/// <summary>
/// <see cref="IBotPlayers"/> over IVEngineServer's fake clients.
///
/// <para>
/// THE PENDING-NAME CLAIM is stock's NextBotCreatePlayerBot back door
/// (NextBotPlayer.h:47-55), in managed: the engine calls ClientPutInServer
/// for the new client before CreateFakeClientEx returns, so the put-in-server
/// that arrives while <see cref="Add"/> waits is the bot's. It is claimed by
/// that moment, not by name, because the engine renames a client whose name is
/// taken; the claim keeps the name the engine gave, and the index
/// IndexOfEdict answers must be the claimed one.
/// </para>
///
/// <para>
/// FL_FAKECLIENT is the game's to set, not the engine's: stock adds
/// <c>FL_CLIENT | FL_FAKECLIENT</c> once the client exists (NextBotPlayer.h:74-75)
/// and CBasePlayer::Spawn keeps it. Here it goes on the player's movement
/// record, which is the player's <c>m_fFlags</c> (ServerPlayerMove).
/// </para>
///
/// <para>
/// A LEVEL CHANGE ENDS EVERY BOT: the engine clears its fake clients when it
/// inactivates the clients (InactivateClients), so the bots are forgotten at the level's shutdown and a
/// later client in a bot's old slot is never taken for one.
/// </para>
///
/// <para>
/// THE WANTED TEAM is claimed with the client, at the put-in-server, because
/// the engine spawns the player before CreateFakeClientEx returns and the
/// first spawn is where a mode assigns a team: set after, the bot had
/// already landed on the auto-assigned team.
/// </para>
/// </summary>
public sealed class BotPlayers(IFakeClients engine, IEnginePlayers players, IConnectedClients clients,
                               ServerPlayerMoves moves, IEngineEnvironment environment)
    : IBotPlayers, IWantedTeams, IClientLifecycle, IGameLifecycle
{
    private readonly List<BotPlayer> _bots = new();
    private readonly Dictionary<int, int> _wantedTeams = new();
    private IReadOnlyList<KeyValuePair<string, string>> _pendingInfo = [];
    private int _pendingTeam;

    // Set while Add waits in CreateFakeClientEx: the claim of the client the
    // engine puts in meanwhile.
    private bool _pending;
    private int _claimedIndex;
    private string _claimedName = "";

    public int ClientLifecycleOrder => (int)LifecycleStage.BotPlayers;

    public int LifecycleOrder => (int)LifecycleStage.BotPlayers;

    public IReadOnlyList<BotPlayer> Bots => _bots;

    public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
    {
        ArgumentNullException.ThrowIfNull(userInfo);
        if (string.IsNullOrWhiteSpace(name))
        {
            refusal = "a bot needs a name";
            return EntityHandle.None;
        }

        if (_pending)
        {
            refusal = $"a bot is already being added; '{name}' was not";
            return EntityHandle.None;
        }

        int index;
        _pending = true;
        _claimedIndex = 0;
        _claimedName = "";
        _pendingInfo = userInfo;
        _pendingTeam = team;
        try
        {
            index = engine.CreateFakeClient(name);
        }
        finally
        {
            _pending = false;
            _pendingInfo = [];
            _pendingTeam = 0;
        }

        if (index <= 0)
        {
            refusal = $"the engine made no client for '{name}' (the server is full)";
            return EntityHandle.None;
        }

        if (_claimedIndex != index)
        {
            refusal = _claimedIndex == 0
                ? $"the engine made client {index} for '{name}' without putting it in the server"
                : $"the engine put client {_claimedIndex} in the server for '{name}' but answered {index}";
            return EntityHandle.None;
        }

        EntityHandle player = players.GetHandle(index);
        if (player == EntityHandle.None)
        {
            refusal = $"client {index} ('{_claimedName}') has no player entity";
            return EntityHandle.None;
        }

        Mark(player);
        engine.SetFakeClientConVar(index, "name", _claimedName);

        _bots.Add(new BotPlayer(player, index, _claimedName));
        refusal = "";
        return player;
    }

    /// <summary>
    /// ClientPutInServer, from the server module: claims the client
    /// <see cref="Add"/> is waiting for. Any other client is not a bot.
    /// </summary>
    public void PutInServer(int entIndex, string name)
    {
        if (_pending && _claimedIndex == 0)
        {
            _claimedIndex = entIndex;
            _claimedName = name;

            // Now, not when CreateFakeClientEx returns: it spawns the player first, and
            // PlayerModels' first spawn reads cl_playermodel and fixes what it wears. Set after,
            // every bot wore the fallback, and the change was "too soon" (user report 2026-11-02).
            foreach (KeyValuePair<string, string> pair in _pendingInfo)
                engine.SetFakeClientConVar(entIndex, pair.Key, pair.Value);

            if (_pendingTeam != 0)
                _wantedTeams[entIndex] = _pendingTeam;
            else
                _wantedTeams.Remove(entIndex);
        }
        else
        {
            // Anyone else put in the slot is not the bot that wanted a team.
            _wantedTeams.Remove(entIndex);
        }
    }

    /// <summary>The team the bot in <paramref name="player"/>'s slot was added for; 0 for none, or for anyone else.</summary>
    public int WantedTeam(EntityHandle player)
    {
        int index = player.EntryIndex;
        return player != EntityHandle.None && _wantedTeams.TryGetValue(index, out int team) && players.GetHandle(index) == player
            ? team
            : 0;
    }

    public bool IsBot(EntityHandle player)
        => moves.Find(player) is { } m && (m.Move.Flags & ServerCommandRules.FlFakeClient) != 0;

    public bool Kick(EntityHandle bot, string reason)
    {
        int at = _bots.FindIndex(b => b.Player == bot);
        if (at < 0)
            return false;

        clients.Kick(_bots[at].EntIndex, reason);
        return true;
    }

    /// <summary>A hot reload came up under a running level: the slots say who the bots are.</summary>
    public void OnLevelCarried(string mapName) => FindBotsInSlots(environment.Globals.MaxClients);

    /// <summary>
    /// The clients a hot reload left in the engine: every slot whose player
    /// carries <c>FL_FAKECLIENT</c> (published for the composition after, so
    /// the record is the same one <see cref="IsBot"/> reads) is a bot of this
    /// composition, called what its client is called. The engine made no new
    /// client and no one was kicked.
    /// </summary>
    internal void FindBotsInSlots(int maxClients)
    {
        for (int i = 1; i <= maxClients; i++)
        {
            EntityHandle player = players.GetHandle(i);
            if (player != EntityHandle.None && (moves.For(player).Move.Flags & ServerCommandRules.FlFakeClient) != 0
                && _bots.FindIndex(b => b.EntIndex == i) < 0)
                _bots.Add(new BotPlayer(player, i, clients.ClientConVar(i, "name") ?? ""));
        }
    }

    /// <summary>A bot's client left (kicked, or the server's own disconnect): it is no longer one of these.</summary>
    public void OnClientDisconnected(int entIndex)
    {
        _bots.RemoveAll(b => b.EntIndex == entIndex);
        _wantedTeams.Remove(entIndex);
    }

    /// <summary>The engine drops its fake clients with the level (InactivateClients).</summary>
    public void OnLevelShutdown()
    {
        _bots.Clear();
        _wantedTeams.Clear();
    }

    private void Mark(EntityHandle player)
        => moves.For(player).Move.Flags |= ServerCommandRules.FlClient | ServerCommandRules.FlFakeClient;
}
