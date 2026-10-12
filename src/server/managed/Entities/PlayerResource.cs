//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// player_manager: the scoreboard every client is told about, once a tick in ten.
///
/// <para>
/// The managed half of lane U cluster 14. It replaces
/// <c>CPlayerResource</c> in <c>game/server/player_resource.cpp</c>, which was a
/// <c>CBaseEntity</c> with a SendTable of its own; this is an entity on the
/// parallel hierarchy with a <see cref="PlayerResourceNetState"/>, which means
/// no table of its own at all — it rides the tier ServerClass its declaration
/// size picks, and that pair already exists on both shells.
/// </para>
///
/// <para>
/// THE CLASSNAME IS STOCK'S. <c>player_manager</c> is what
/// <c>LINK_ENTITY_TO_CLASS</c> registered and what
/// <c>CGameRules::CreateStandardEntities</c> asked for, and it moved here with
/// the behaviour. No map contains one — it has always been created by the rules
/// — and it is created now by <see cref="ServerModule"/> at level init, which is
/// after the map's entities and before any client has connected.
/// </para>
///
/// <para>
/// WHAT IT SAYS, AND WHY THAT IS DIFFERENT FROM WHAT STOCK SAID. Stock read
/// each row off a <c>CBasePlayer</c>: <c>FragCount</c>, <c>DeathCount</c>,
/// <c>GetTeamNumber</c>, <c>IsAlive</c>, <c>GetHealth</c>. There is no
/// <c>CBasePlayer</c> left to read — <c>UTIL_PlayerByIndex</c> has answered NULL
/// for every index since 13c, which is why stock's own
/// <c>UpdateConnectedPlayer</c> was stubbed out and why the table has carried
/// nothing but zeroes ever since. The numbers exist; they are just managed now.
/// Kills and deaths come from <see cref="IPlayerDirectory"/> — see the note on
/// <see cref="Roster"/> for why that and not <see cref="IScoreboard"/> — and
/// the rest from the engine's own player accessors.
/// </para>
///
/// <para>
/// ALWAYS TRANSMITTED. An entity on the hierarchy asks to be PVS-checked, which
/// for a scoreboard sitting at the origin means it reaches a client only when
/// that client happens to be able to see the origin. Stock said the same thing
/// as <c>UpdateTransmitState</c> returning <c>FL_EDICT_ALWAYS</c>.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PlayerManager, ScriptedEntityKind.Effect)]
public sealed class PlayerResource : ScriptedEntity
{
    public PlayerResource(IEngineEntityRender render,
                          IEngineEntities entities,
                          IEnginePlayers players,
                          IPlayerDirectory roster,
                          IClientInfo? clients = null,
                          IEngineEnvironment? environment = null)
    {
        Clients = clients;
        Environment = environment;
        Render = render;
        EntityApi = entities;
        PlayerApi = players;
        Roster = roster;
    }

    /// <summary>Each client's channel latency and cl_cmdrate, for the ping; none in a test without an engine.</summary>
    private readonly IClientInfo? Clients;

    /// <summary>The tick interval the ping's correction subtracts.</summary>
    private readonly IEngineEnvironment? Environment;

    /// <summary>Stock's m_nUpdateCounter: thinks so far, counted before the update asks.</summary>
    private int _thinks;

    /// <summary>Whether this entity is sent to everyone. The one thing it needs drawn-side.</summary>
    private readonly IEngineEntityRender Render;

    /// <summary>Health, which is an entity property rather than a player one.</summary>
    private readonly IEngineEntities EntityApi;

    /// <summary>Team, userid and whether they are playing.</summary>
    private readonly IEnginePlayers PlayerApi;

    /// <summary>
    /// Kills and deaths per player, and it is <see cref="IPlayerDirectory"/>
    /// rather than <see cref="IScoreboard"/> — which is not the obvious choice
    /// and was settled by a live run.
    ///
    /// <para>
    /// STOCK READ <c>pPlayer-&gt;FragCount()</c> AND <c>DeathCount()</c>, the
    /// engine's per-player counters, into <c>m_iScore</c> and <c>m_iDeaths</c>.
    /// <see cref="PlayerRecord"/>'s Kills and Deaths are the exact managed
    /// analogue: one pair per player, incremented by
    /// <c>ServerModule.OnPlayerKilled</c> on every kill.
    /// </para>
    ///
    /// <para>
    /// <see cref="IScoreboard"/> is a different quantity and, in this build, an
    /// inert one. It holds the POINTS the frag limit reads, and GameRules feeds
    /// it from the engine's <c>player_death</c> event — which is fired by
    /// <c>CMultiplayRules::PlayerKilled( CBasePlayer * )</c> and by nothing
    /// else. There is no CBasePlayer in this build, so that function cannot run
    /// and the event never fires: the first live run killed a player, the
    /// roster logged "1 death(s)", and the scoreboard's own tally stayed at
    /// zero. Reading it here would have networked a column of zeroes that
    /// looked like a working scoreboard.
    /// </para>
    ///
    /// <para>
    /// When the death pipeline is wired to raise <c>player_death</c> for a
    /// standalone player, the points tally becomes the better source for the
    /// SCORE column specifically — a suicide costs a point there and does not
    /// here. Deaths stay this one's either way.
    /// </para>
    /// </summary>
    private readonly IPlayerDirectory Roster;

    /// <summary>What every client is told. Bound to this entity by the registry.</summary>
    [NetState] public PlayerResourceNetState State = new();

    /// <summary>
    /// Stock's <c>PLAYER_RESOURCE_THINK_INTERVAL</c> on the server side: ten
    /// times a second, which is <c>ResourceThink</c>'s <c>curtime + 0.1f</c>.
    /// A scoreboard is read when a key is held down, so a slower tick would show
    /// a stale number for as long as somebody looked at it.
    /// </summary>
    public const float ThinkInterval = 0.1f;

    public override void OnSpawn()
    {
        // NOTHING IS HIDDEN HERE, deliberately. ScriptedEntityKind.Effect is
        // already SOLID_NONE with no model, and CStandaloneEntity::SetVisible
        // returns immediately for an entity that has no model name — so a
        // SetVisible(false) call would read as if it did something and would
        // do nothing at all. Stock's player_manager was invisible for the same
        // reason: it never had a model to begin with.

        // Every row empty before the first think, so a client that joins in the
        // same tick reads zeroes rather than whatever the blob was allocated
        // with. The allocation is zeroed, so this is belt and braces -- and it
        // is also what makes a MAP CHANGE clean, because the entity is new but
        // the reader's expectations are not.
        State.ClearAll();

        // See the note on the class. Stock's FL_EDICT_ALWAYS.
        Render.SetTransmitAlways(Handle, true);

        SetNextThink(0f);
    }

    /// <summary>
    /// One pass over every slot a player's entity index can occupy.
    ///
    /// <para>
    /// The walk is <c>1..Slots-1</c>, which is stock's <c>1..MAX_PLAYERS</c> and
    /// costs what stock's cost: <c>GetPlayerHandle</c> bounds-checks against the
    /// server's real maxclients and answers None past it, exactly as stock's
    /// <c>UTIL_PlayerByIndex</c> did. Slot zero is never a player and is left
    /// cleared.
    /// </para>
    /// </summary>
    public override void OnThink()
    {
        // Stock: ResourceThink counts, then UpdatePlayerData, which sets the
        // pings only on every PlayerPing.ThinksPerUpdate'th count (2 s).
        _thinks++;
        bool pings = PlayerPing.IsUpdate(_thinks);
        for (int slot = 1; slot < PlayerResourceNetState.Slots; slot++)
        {
            UpdateSlot(slot);
            if (pings)
                UpdatePing(slot);
        }

        SetNextThink(ThinkInterval);
    }

    /// <summary>
    /// One connected slot's ping: stock's sample of the channel's latency,
    /// smoothed into the last ping. A slot with no channel (a bot, an empty
    /// slot) reads 0, as stock's UTIL_GetPlayerConnectionInfo answers.
    /// </summary>
    public void UpdatePing(int slot)
    {
        if (!State.HasFlag(slot, PlayerResourceNetState.FlagConnected))
            return;

        int sample = 0;
        if (Clients?.AvgLatency(slot) is { } latency)
        {
            int cmdRate = int.TryParse(Clients.ClientConVar(slot, "cl_cmdrate"), out int r) ? r : 0;
            float tick = Environment?.Globals is { IsAvailable: true } g ? g.IntervalPerTick : ServerGameDll.DefaultTickInterval;
            sample = PlayerPing.Sample(latency, cmdRate, tick);
        }
        State.Ping[slot] = (short)PlayerPing.Smooth(State.Ping[slot], sample);
    }

    /// <summary>
    /// One row. Separate from the walk so that a test can drive it a slot at a
    /// time without a running engine deciding how many slots there are.
    /// </summary>
    public void UpdateSlot(int slot)
    {
        var player = PlayerApi.GetHandle(slot);

        if (player == EntityHandle.None)
        {
            // Stock cleared only the connected/valid pair here and left score,
            // deaths, team and health at whatever the departed player had. Every
            // accessor asks IsConnected || IsValid before answering, so those
            // numbers were unreachable either way -- and a slot somebody else
            // has since joined into would have been showing them. Cleared whole.
            State.ClearRow(slot);
            return;
        }

        // Knows() rather than the indexer, which CREATES a record on first use.
        // A slot the engine has a handle for but the roster has never seen is a
        // player mid-connect; inventing a record for them here would put an
        // empty row in the directory the respawn cycle reads.
        var record = Roster.Knows(player) ? Roster[player] : null;

        int userId = PlayerApi.GetUserId(player);

        State.SetRow(slot,
                     score:  record?.Kills ?? 0,
                     deaths: record?.Deaths ?? 0,
                     health: EntityApi.GetHealth(player),
                     userId: userId,
                     team:   PlayerApi.GetTeam(player),
                     connected: true,
                     alive:     PlayerApi.IsAlive(player),
                     valid:     true);
    }
}
