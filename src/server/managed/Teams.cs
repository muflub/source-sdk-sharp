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
/// Which team a player is on, and what it takes to change it.
///
/// <para>
/// Stock's teams live in <c>CTeamplayRules</c> and on <c>CBaseEntity.m_iTeamNum</c>
/// (baseentity.cpp:4947 — one assignment). The rules class is not in this build
/// and the standalone player answered <c>GetPlayerTeam</c> with a flat <c>0</c>;
/// the store is native now (the <c>PlayerSetTeam</c> slot), and it is a store
/// and not a decision — it validates nothing, exactly as stock's did. The
/// decisions are here.
/// </para>
///
/// <para>
/// WHAT THE SERVICE IS NOT. It does not remove items, switch models, or kill a
/// player who changes sides mid-round: those are HL2MP's
/// <c>CHL2MP_Player::ChangeTeam</c> behaviours around a <c>CBasePlayer</c> this
/// build has no equivalent of. The observer half of a join stays
/// <see cref="Observers.JoinTeam"/>'s; the absence of the suicide is a
/// divergence row, not a silent gap.
/// </para>
/// </summary>
public interface ITeams
{
    /// <summary>
    /// The team a player is on, or -1 when the handle is not a player
    /// (<see cref="IEnginePlayers.GetTeam"/>'s answer).
    /// </summary>
    int Team(EntityHandle player);

    /// <summary>Every tracked player on a team.</summary>
    IReadOnlyList<EntityHandle> Players(int team);

    /// <summary>How many tracked players are on a team.</summary>
    int Count(int team);

    /// <summary>
    /// Moves a player, asking <see cref="IJoinTeamPolicy"/> first. Nothing
    /// changes when the policy refuses. With no policy any real team number
    /// is accepted and none is chosen for anybody — NOT stock HL2DM, whose
    /// deathmatch forces every non-spectator join to team 0
    /// (CHL2MP_Player::ChangeTeam with teamplay off): a divergence row in
    /// docs/divergences.md, because the team is the seam this service opens.
    /// </summary>
    JoinTeamResult Join(EntityHandle player, int requested);

    /// <summary>
    /// The active mode's <see cref="IGameMode.Spawns"/> answer, else the first
    /// spawn point any registered <see cref="ISpawnSelector"/> names, else
    /// <see cref="EntityHandle.None"/> for the core's walk.
    /// </summary>
    EntityHandle SelectSpawn(EntityHandle player);
}

/// <summary>
/// The one mod that decides who may join which team: auto-assign, balance,
/// refuse. <see cref="ITeams.Join"/> asks it before anything changes.
///
/// <para>
/// SINGLE-OWNER, on purpose: two mods balancing teams differently is not a
/// chain — two answers to "may this player join" cannot both be obeyed — so
/// more than one is a startup error naming both, the same rule as the
/// movement constants (and resolved the same way: from the container's
/// registrations once the mods' scan has run, never through an
/// <see cref="IHooks{T}"/> chain, whose order-uniqueness refusal would name
/// the wrong fix). Zero owners is the normal state, and it means any real
/// team is accepted (<see cref="ITeams.Join"/> names how that differs from
/// stock HL2DM). A mod registers one by marking the class
/// <c>[SideService(Side.Server)]</c>.
/// </para>
/// </summary>
public interface IJoinTeamPolicy
{
    /// <summary>
    /// Whether the player may go to <paramref name="requested"/> now. A
    /// refusal means <see cref="ITeams.Join"/> sets nothing and says
    /// <paramref name="reason"/> out loud.
    /// </summary>
    bool MayJoin(EntityHandle player, int requested, out string reason);
}

/// <summary>
/// Where a player spawns, as a hook: asked before the core's walk over the
/// map's spawn-point classes (<see cref="ServerModule.SelectSpawnPoint"/>).
///
/// <para>
/// A CHAIN, not a single owner: every registered selector is asked in
/// <see cref="IOrderedHook.HookOrder"/>'s order and the first one to name an
/// entity wins — so a mod's team spawns sit beside a map script that owns one
/// special round. The ACTIVE MODE's own selector (<see cref="IGameMode.Spawns"/>)
/// is asked before the whole chain (<see cref="Teams.SelectSpawn"/> argues
/// why). Returning <see cref="EntityHandle.None"/> falls through, and so does
/// a selector that throws (logged): a broken mod must not strand a player on
/// the origin.
/// </para>
/// </summary>
public interface ISpawnSelector
{
    /// <summary>
    /// The spawn point for a player, or <see cref="EntityHandle.None"/> for
    /// "the core's walk". Team-blind by default: a selector that cares reads
    /// the team through <see cref="ITeams.Team"/>.
    /// </summary>
    EntityHandle SelectSpawn(EntityHandle player);
}

/// <summary>
/// The team roster, the join, and the spawn ask, server side (tf2 stage 0.5).
///
/// <para>
/// The membership it publishes walks <see cref="IPlayerDirectory.All"/> — the
/// same set the scoreboard reads — so the team and the score of one player
/// can never be reported by two directories that disagree about who is here.
/// A team of untracked players still reads correctly through
/// <see cref="Team"/>, which is the engine's own answer for any handle.
/// </para>
/// </summary>
public sealed class Teams : ITeams
{
    /// <summary>HL2MP's spectator team (TEAM_SPECTATOR).</summary>
    public const int TeamSpectator = Observers.TeamSpectator;

    private readonly ILog _log;
    private readonly IEnginePlayers _players;
    private readonly IEngineEntities _entities;
    private readonly IEntityDirectory _directory;
    private readonly IPlayerDirectory _roster;
    private readonly Observers _observers;
    private readonly IGameClock _clock;
    private readonly IEngineGameEvents _events;
    private readonly PlayerNames? _names;
    private readonly IServiceLocator? _services;
    private readonly IHooks<ISpawnSelector>? _selectors;
    private readonly GameModes? _modes;
    private readonly IChat? _chat;

    // The join policy, resolved ONCE and not before it is asked (see Policy).
    private bool _policyResolved;
    private IJoinTeamPolicy? _policy;

    public Teams(ILog log, IEnginePlayers players, IEngineEntities entities, IEntityDirectory directory,
                 IPlayerDirectory roster, Observers observers, IGameClock clock, IEngineGameEvents events,
                 PlayerNames? names = null, IServiceLocator? services = null,
                 IHooks<ISpawnSelector>? selectors = null, GameModes? modes = null, IChat? chat = null)
    {
        _log = log;
        _players = players;
        _entities = entities;
        _directory = directory;
        _roster = roster;
        _observers = observers;
        _clock = clock;
        _events = events;
        _names = names;
        _services = services;
        _selectors = selectors;
        _modes = modes;
        _chat = chat;

        // NOTHING IS RESOLVED HERE. The module builds this service before the
        // mods' scan registers anything (mods inject ITeams, so it must exist
        // first): a policy list collected now is empty forever, and no mod's
        // policy would ever be asked.
    }

    /// <summary>
    /// The one registered <see cref="IJoinTeamPolicy"/>, or null (any real
    /// team accepted). Resolved the first time it is asked and frozen: the
    /// module asks right after the mods' scan (which is what makes two owners
    /// a STARTUP error and logs the owner), so every join sees the same one.
    /// More than one registration throws, naming every owner.
    /// </summary>
    public IJoinTeamPolicy? Policy
    {
        get
        {
            if (!_policyResolved)
            {
                _policy = ResolvePolicy();
                _policyResolved = true;
            }

            return _policy;
        }
    }

    /// <summary>
    /// The container's registrations, deduplicated by instance (a class
    /// marked under its own type and <c>As</c> the contract is one owner), and
    /// only the ownership rule applied — MovementRulesResolver's walk.
    /// </summary>
    private IJoinTeamPolicy? ResolvePolicy()
    {
        if (_services is not ServiceContainer container)
            return null;

        var all = container.ResolveAll<IJoinTeamPolicy>();
        if (all.Count > 1)
        {
            throw new InvalidOperationException(
                $"IJoinTeamPolicy has {all.Count} owners (" +
                string.Join(" and ", all.Select(h => $"{h.GetType().FullName} ({h.GetType().Assembly.GetName().Name})")) +
                "): the join policy is single-owner — two mods cannot both decide who joins, " +
                "and no order makes two answers coherent. Only one assembly may register IJoinTeamPolicy.");
        }

        return all.Count == 1 ? all[0] : null;
    }

    /// <inheritdoc/>
    public int Team(EntityHandle player) => _players.GetTeam(player);

    /// <inheritdoc/>
    public IReadOnlyList<EntityHandle> Players(int team)
    {
        var found = new List<EntityHandle>();
        foreach (var (player, _) in _roster.All)
        {
            if (_players.GetTeam(player) == team)
                found.Add(player);
        }

        return found;
    }

    /// <inheritdoc/>
    public int Count(int team) => Players(team).Count;

    /// <inheritdoc/>
    public JoinTeamResult Join(EntityHandle player, int requested)
    {
        // Only a managed player has a join to make: the observer half needs
        // its record, and a handle that is not one has no team to be on.
        if (_directory.Get(player) is not SsPlayer joiner)
        {
            _log.Warning($"HandleCommand_JoinTeam( {requested} ) - not a managed player.\n");
            return JoinTeamResult.Invalid;
        }

        float now = _clock.Now;
        string arg = requested.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // An out-of-range number (team 0 included) is the observers' stock
        // warning: one refusal point, and Observers keeps it.
        if (!Observers.IsValidTeamNumber(requested))
            return _observers.JoinTeam(joiner, arg, now);

        // Team 1, stock's HandleCommand_JoinTeam(TEAM_SPECTATOR): the
        // mp_allowspectators gate, then ChangeTeam(TEAM_SPECTATOR) — the team
        // stored — and only then CHL2MP_Player::ChangeTeam's spectator arm
        // (RemoveAllItems, STATE_OBSERVER_MODE), the observers' Spectate. The
        // order is load-bearing: the observer mode is chosen for a player
        // already on team 1 (mp_forcecamera reads the team). No policy: a
        // mod's balance rule is about the playing teams, as stock's was.
        if (requested == TeamSpectator)
        {
            if (!_observers.AllowsSpectators())
                return JoinTeamResult.SpectatorsNotAllowed;

            ChangeTeam(player, TeamSpectator);
            return _observers.JoinTeam(joiner, arg, now);
        }

        // A playing team: the policy first, while nothing has changed yet —
        // stock's order (CHL2MP_Player::HandleCommand_JoinTeam asks
        // g_pGameRules before ChangeTeam). Zero policies accepts the team.
        if (Policy is { } policy && !policy.MayJoin(player, requested, out string why))
        {
            _log.Msg($"[SourceSharp/Teams] {player} refused team {requested}: {why}\n");

            // And to the player who asked, who otherwise sees a menu that did
            // nothing (stock's refusals are ClientPrint to that player).
            _chat?.Print(_entities.GetIndex(player),
                         why.Length > 0 ? why : $"You cannot join team {requested}.");
            return JoinTeamResult.Refused;
        }

        // The observer half — stop watching, and come back if watching was
        // what this player was doing (stock's StopObserverMode and
        // STATE_ACTIVE before ChangeTeam) — then the team itself.
        JoinTeamResult outcome = _observers.JoinTeam(joiner, arg, now);
        ChangeTeam(player, requested);
        return outcome;
    }

    /// <summary>
    /// The team change itself, CBasePlayer::ChangeTeam's order
    /// (player.cpp:7649-7693): the player's current team is no change at all,
    /// then the player_team event, then the one ABI store.
    /// </summary>
    private void ChangeTeam(EntityHandle player, int team)
    {
        int oldTeam = _players.GetTeam(player);

        // "if this is our current team, just abort": no event, no store.
        if (team == oldTeam)
            return;

        // Stock announces a team change through the player_team game event,
        // and FIRST — "so that all user messages that follow as a result of
        // the team change come after this one" — and the client's ChatNotices
        // already listens: join with the event unraised and the HUD never
        // hears about the move.
        FirePlayerTeam(player, team, oldTeam);

        _players.SetTeam(player, team);

        _log.Msg($"[SourceSharp/Teams] {player} joined team {team} (was {oldTeam})\n");
    }

    /// <summary>
    /// The active mode's spawn point, else the first one any registered
    /// selector names, else None for the core's walk. With no mode spawns and
    /// no selectors this asks nothing.
    ///
    /// <para>
    /// THE MODE FIRST, then the chain. The active mode owns the round (the
    /// plan: SelectSpawnPoint consults <see cref="GameModes.Active"/>), and its
    /// concern is scoped to the round it is playing; a registered selector is
    /// mod-wide and on for every mode. Asked the other way round, an
    /// always-registered selector (a mod's team spawns) would shadow every
    /// mode's own spawns and a mode switch could not move anyone. A mode that
    /// has no opinion for this player returns None and the chain answers.
    /// </para>
    /// </summary>
    public EntityHandle SelectSpawn(EntityHandle player)
    {
        if (_modes?.Active?.Spawns is { } modeSpawns && Ask(modeSpawns, player) is var fromMode
            && fromMode != EntityHandle.None)
            return fromMode;

        if (_selectors is not { } selectors)
            return EntityHandle.None;

        foreach (var selector in selectors.All)
        {
            EntityHandle spot = Ask(selector, player);
            if (spot != EntityHandle.None)
                return spot;
        }

        return EntityHandle.None;
    }

    /// <summary>One selector's answer; a throw is logged and is None (falls through).</summary>
    private EntityHandle Ask(ISpawnSelector selector, EntityHandle player)
    {
        try
        {
            return selector.SelectSpawn(player);
        }
        catch (Exception e)
        {
            // The next answer — the chain, then the core's walk — stands
            // without a broken selector.
            _log.Warning($"[SourceSharp/Teams] spawn selector {selector.GetType().FullName} threw: {e.Message}\n");
            return EntityHandle.None;
        }
    }

    /// <summary>
    /// player_team, the stock spelling: userid, the new team, the old one, no
    /// disconnect, not auto, not silent, and the player's name as the server
    /// knows it. Same create/set/fire sequence as DeathNotice and PlayerNames.
    /// </summary>
    private void FirePlayerTeam(EntityHandle player, int team, int oldTeam)
    {
        IntPtr e = _events.EventCreate("player_team");
        if (e == IntPtr.Zero)
            return;

        int userId = _players.GetUserId(player);
        string name = _names?.NameOf(_entities.GetIndex(player)) ?? string.Empty;

        _events.EventSetInt(e, "userid", userId);
        _events.EventSetInt(e, "team", team);
        _events.EventSetInt(e, "oldteam", oldTeam);
        _events.EventSetBool(e, "disconnect", false);
        _events.EventSetBool(e, "autoteam", false);
        _events.EventSetBool(e, "silent", false);
        _events.EventSetString(e, "name", name);
        _events.EventFire(e);
    }
}
