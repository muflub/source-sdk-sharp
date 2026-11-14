//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// HL2DM's team deathmatch: stock <c>CHL2MPRules</c> with
/// <c>IsTeamplay()</c> true (<c>mp_teamplay 1</c>, hl2mp_gamerules.h:151),
/// as a mode of the core's registry (plan_bots_teams_tf2 Part A).
///
/// <para>
/// Two teams, Combine (2) and Rebels (3), as stock; with <c>mp_tdm_teams</c>
/// 3 or 4 also Metro Police (4) and Resistance (5), ours. The round is the deathmatch round
/// with the frag limit held against a TEAM's score
/// (<see cref="TeamDeathmatchRoundFlow"/>); the team score is
/// <see cref="TeamDeathmatchScoring"/>'s; <c>say_team</c> reaches teammates
/// (<see cref="Teamplay"/>); the join is <see cref="TeamDeathmatchJoinPolicy"/>'s.
/// </para>
///
/// <para>
/// THE PLAYERS. A player on Unassigned is put on a team the moment they spawn
/// (stock's <c>Spawn</c> calls <c>PickDefaultSpawnTeam</c> before it picks a
/// spawn point, hl2mp_player.cpp:338), and so is everyone on Unassigned when
/// the round is set up — a switch to this mode mid-level, or a level that
/// starts in it. A switch to a mode without teams puts every player of the
/// teams back on Unassigned (stock's deathmatch <c>ChangeTeam</c> forces
/// team 0, hl2mp_player.cpp:971-975).
/// </para>
/// </summary>
public sealed class TeamDeathmatchMode : IGameMode, IModeSetup, IModeTeardown, ITeamScramble
{
    /// <summary>The id <c>sv_mode</c>, the admin menu and the selector name.</summary>
    public const string ModeId = "tdm";

    /// <summary>TEAM_COMBINE (hl2mp_gamerules.h:31).</summary>
    public const int TeamCombine = 2;

    /// <summary>TEAM_REBELS (hl2mp_gamerules.h:32).</summary>
    public const int TeamRebels = 3;

    /// <summary>The third team, ours (mp_tdm_teams 3 and 4).</summary>
    public const int TeamMetroPolice = 4;

    /// <summary>The fourth team, ours (mp_tdm_teams 4).</summary>
    public const int TeamResistance = 5;

    /// <summary>The highest team number team deathmatch ever plays.</summary>
    public const int LastPossibleTeam = TeamResistance;

    private readonly TeamDeathmatchRoundFlow _rounds;
    private readonly TeamDeathmatchScoring _scoring;
    private readonly TeamSpawnSelector _spawns;
    private readonly ModeSpawnArm _spawnArm;
    private readonly TeamDeathmatchJoinPolicy _join;
    private readonly ITeams _teams;
    private readonly ITeamAssignment _assign;
    private readonly TdmTeamsConVar _teamCount;
    private readonly GameModes _modes;
    private readonly IEnginePlayers _players;
    private readonly Random _random;

    public TeamDeathmatchMode(GameModes modes, IEnginePlayers players, SpawnPoints spawnPoints, ITeams teams,
                              ITeamAssignment assign, IEngineDamage damage, TeamsUnbalanceLimitConVar unbalanceLimit,
                              TdmTeamsConVar teamCount, IUserMessageSender messages, IBotPlayers? bots = null,
                              Random? random = null)
    {
        _modes = modes;
        _players = players;
        _random = random ?? new Random();
        _teams = teams;
        _assign = assign;
        _teamCount = teamCount;
        _scoring = new TeamDeathmatchScoring(players, () => TeamCount);
        _rounds = new TeamDeathmatchRoundFlow(modes, _scoring, new TeamMenuOnFirstSpawn(messages, bots));
        _spawns = new TeamSpawnSelector(spawnPoints, players);
        _spawnArm = new ModeSpawnArm(this);
        _join = new TeamDeathmatchJoinPolicy(teams, players, damage, unbalanceLimit, _random, () => TeamCount);

        // The count moving mid-level: heard here, acted on only while this
        // mode plays (TeamCountChanged).
        teamCount.Subscribe((_, _) => TeamCountChanged());
    }

    /// <summary>How many teams play: <see cref="TdmTeamsConVar"/>, clamped to 2..4.</summary>
    public int TeamCount => Math.Clamp(_teamCount.Value, 2, LastPossibleTeam - TeamCombine + 1);

    /// <summary>The highest team in play now.</summary>
    public int LastTeam => TeamCombine + TeamCount - 1;

    public string Id => ModeId;

    public IRoundFlow Rounds => _rounds;

    public ISpawnSelector? Spawns => _spawnArm;

    /// <summary>Stock's teamplay: a team line reaches the speaker's teammates, and teammates are a side.</summary>
    public bool Teamplay => true;

    public IJoinTeamPolicy? JoinPolicy => _join;

    public IScoring? Scoring => _scoring;

    public IModeSetup? Setup => this;

    /// <summary>
    /// Stock <c>CHL2MP_Player::Spawn</c>'s order: <c>PickDefaultSpawnTeam</c>
    /// first, so a player on Unassigned spawns on their new team's points.
    /// </summary>
    public EntityHandle SelectSpawn(EntityHandle player)
    {
        PickDefaultSpawnTeam(player);
        return _spawns.SelectSpawn(player);
    }

    /// <summary>
    /// The mode's spawn arm, handed out only through <see cref="Spawns"/>: the
    /// container registers a type under every interface it implements, so a
    /// mode that was itself an <see cref="ISpawnSelector"/> joined the global
    /// selector chain every mode asks (in TF2 beside <c>TFSpawnSelector</c>, at
    /// the same order, and spawn selection threw there).
    /// </summary>
    private sealed class ModeSpawnArm(TeamDeathmatchMode mode) : ISpawnSelector
    {
        public EntityHandle SelectSpawn(EntityHandle player) => mode.SelectSpawn(player);
    }

    /// <summary>Everyone still on Unassigned is put on a team; the first-spawn menu starts over.</summary>
    public void SetupRound(float now)
    {
        _rounds.Menus.Reset();
        foreach (EntityHandle player in _teams.Players(0))
            PickDefaultSpawnTeam(player);
    }

    /// <summary>Leaving for a mode without teams: every team's players go back to Unassigned.</summary>
    public void TeardownRound(IGameMode next, float now)
    {
        if (next.Teamplay)
            return;

        for (int team = TeamCombine; team <= LastPossibleTeam; team++)
        {
            foreach (EntityHandle player in _teams.Players(team))
                _assign.Assign(player, 0);
        }
    }

    /// <summary>
    /// <c>mp_tdm_teams</c> moved while this mode plays: whoever stands on a
    /// team that is no longer in play is put on one that is (and dies, as any
    /// switch between teams does), then the round restarts as <c>sv_mode</c>
    /// restarts it. Under any other mode the knob waits for the next setup.
    /// </summary>
    public void TeamCountChanged()
    {
        if (!ReferenceEquals(_modes.Active, this))
            return;

        for (int team = LastTeam + 1; team <= LastPossibleTeam; team++)
        {
            foreach (EntityHandle player in _teams.Players(team))
                _assign.Assign(player, _join.AutoTeam(player));
        }

        _modes.SwitchTo(ModeId);
    }

    /// <summary>
    /// The scramble (the vote's and the admin's): every player on a team in
    /// play, or on Unassigned, in a seeded shuffle, dealt across the teams in
    /// turn so no team ends more than one ahead; whoever changes team dies, as
    /// a switch does; then the round restarts. Spectators stay.
    /// </summary>
    public void Scramble()
    {
        var everyone = Enumerable.Range(0, LastTeam + 1).Where(t => t != Teams.TeamSpectator)
                                 .SelectMany(t => _teams.Players(t)).ToList();
        for (int i = everyone.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (everyone[i], everyone[j]) = (everyone[j], everyone[i]);
        }

        for (int i = 0; i < everyone.Count; i++)
            _assign.Assign(everyone[i], TeamCombine + i % TeamCount);

        _modes.SwitchTo(ModeId);
    }

    /// <summary>
    /// Balance now (the admin's): while one team in play has two more players
    /// than another, its newest arrival (the highest user id) moves to the
    /// smallest. Humans move too: an admin asked.
    /// </summary>
    public void Balance()
    {
        while (true)
        {
            var sizes = Enumerable.Range(TeamCombine, TeamCount).Select(t => (Team: t, Players: _teams.Players(t))).ToList();
            var largest = sizes.MaxBy(s => s.Players.Count);
            var smallest = sizes.MinBy(s => s.Players.Count);
            if (largest.Players.Count - smallest.Players.Count <= 1)
                return;

            EntityHandle newest = largest.Players.MaxBy(p => _players.GetUserId(p));
            _assign.Assign(newest, smallest.Team);
        }
    }

    /// <summary>Stock's <c>PickDefaultSpawnTeam</c> under teamplay: only a player on Unassigned moves.</summary>
    private void PickDefaultSpawnTeam(EntityHandle player)
    {
        if (_teams.Team(player) == 0)
            _assign.Assign(player, _join.AutoTeam(player));
    }
}

/// <summary>
/// The deathmatch round under teamplay: the core's time limit and
/// <c>mp_restartgame</c> walk, with the frag limit held against the leading
/// team's score (stock <c>CHL2MPRules::Think</c>, hl2mp_gamerules.cpp:323-331),
/// never a player's.
/// </summary>
public sealed class TeamDeathmatchRoundFlow(GameModes modes, TeamDeathmatchScoring scoring, TeamMenuOnFirstSpawn menus)
    : IRoundFlow
{
    /// <summary>The team menu a player is shown on their first spawn in this mode.</summary>
    public TeamMenuOnFirstSpawn Menus => menus;

    /// <summary>The core's machine; the proxy is attached to it before anything thinks.</summary>
    private GameRules Core => modes.Core ?? throw new InvalidOperationException("the mode proxy has no core rules attached");

    public void Think(float now) => Core.ThinkRound(scoring.Leader);

    /// <summary>The core's own latch: the core's checks ended the round, and a second latch could disagree.</summary>
    public bool RoundOver => Core.RoundOver;

    public void ResetRound() => Core.ResetRound();

    public void OnPlayerSpawned(EntityHandle player, float now) => menus.Spawned(player);

    public void OnPlayerKilled(PlayerDeathArgs death) { }
}

/// <summary>
/// The team menu on a player's first spawn under team deathmatch (plan A.3
/// item 7): <c>VGUIMenu</c> named <c>team</c> (stock's <c>PANEL_TEAM</c>,
/// viewport_panel_names.h), shown, to that player alone — the panel the
/// client maps to its team menu. Stock HL2MP shows none (its players pick a
/// side through <c>cl_playermodel</c>); TF2 raises its own team panel the same
/// way (<c>ShowViewPortPanel( PANEL_TEAM )</c>). Once per player per round
/// set-up, and never to a bot, which reads no panel.
/// </summary>
public sealed class TeamMenuOnFirstSpawn(IUserMessageSender messages, IBotPlayers? bots)
{
    /// <summary>PANEL_TEAM.</summary>
    public const string Panel = "team";

    private readonly HashSet<EntityHandle> _shown = [];

    /// <summary>A player spawned; the first spawn since the round was set up shows the menu.</summary>
    public void Spawned(EntityHandle player)
    {
        if (bots?.IsBot(player) == true || !_shown.Add(player))
            return;

        Show(messages, player);
    }

    /// <summary>The round was set up again: every player's next spawn is a first one.</summary>
    public void Reset() => _shown.Clear();

    /// <summary>The panel, shown, to <paramref name="player"/> alone.</summary>
    public static void Show(IUserMessageSender messages, EntityHandle player)
        => messages.Send(new VguiMenu { Name = Panel, Show = true, Keys = [], Values = [] },
                         player: player.EntryIndex, reliable: true);
}

/// <summary>
/// The teams' scores: stock <c>CHL2MP_Player::Event_Killed</c>'s
/// <c>GetGlobalTeam( pAttacker-&gt;GetTeamNumber() )-&gt;AddScore</c>
/// (hl2mp_player.cpp:1323-1334). The attacker's team gains a point for any
/// kill, a teammate's included (stock's own arithmetic: the killer's frags
/// fall by one through <c>IPointsForKill</c> while the team still gains one);
/// a suicide costs the victim's team a point; a death to the world scores for
/// the world's team, which is no side, so neither team moves.
/// </summary>
public sealed class TeamDeathmatchScoring(IEnginePlayers players, Func<int>? teamCount = null) : IScoring
{
    // Teams 2..5, by team - 2.
    private readonly int[] _scores = new int[TeamDeathmatchMode.LastPossibleTeam - TeamDeathmatchMode.TeamCombine + 1];

    private int LastTeam => TeamDeathmatchMode.TeamCombine + (teamCount?.Invoke() ?? 2) - 1;

    public void OnDeath(PlayerDeathArgs death)
    {
        if (death.AttackerUserId == 0)
            return;

        int team = players.GetTeam(players.FindByUserId(death.AttackerUserId));
        int points = death.AttackerUserId == death.UserId ? -1 : 1;

        if (team >= TeamDeathmatchMode.TeamCombine && team <= TeamDeathmatchMode.LastPossibleTeam)
            _scores[team - TeamDeathmatchMode.TeamCombine] += points;
    }

    /// <summary>
    /// Each of team deathmatch's teams, 2 to 5 (one not in play reads 0); the
    /// two shared teams (Unassigned, Spectator) exist and never score;
    /// anything else is no such team.
    /// </summary>
    public int TeamScore(int team) => team switch
    {
        >= TeamDeathmatchMode.TeamCombine and <= TeamDeathmatchMode.LastPossibleTeam => _scores[team - TeamDeathmatchMode.TeamCombine],
        0 or 1 => 0,
        _ => -1,
    };

    public void ResetRound() => Array.Clear(_scores);

    /// <summary>The leading team in play, for the frag limit; the lowest number on a tie (any reaching it ends the round).</summary>
    public GameRules.FragLeader? Leader()
    {
        int best = TeamDeathmatchMode.TeamCombine;
        for (int team = best + 1; team <= LastTeam; team++)
        {
            if (TeamScore(team) > TeamScore(best))
                best = team;
        }

        return new GameRules.FragLeader($"team {HostSay.TeamName(best)}", TeamScore(best));
    }
}

/// <summary>
/// Team deathmatch's spawn points: stock <c>CHL2MP_Player::EntSelectSpawnPoint</c>
/// under teamplay (hl2mp_player.cpp:1397-1414). A Combine player walks
/// <c>info_player_combine</c>, a Rebel <c>info_player_rebel</c>, and a map with
/// none of their team's points falls back to the deathmatch points (and, with
/// none of those, <c>info_player_start</c>) as stock does: dm_lockdown has no
/// team spawns and plays team deathmatch on its deathmatch points. The walk is
/// the core's own, with the spawn frag sparing teammates (plan A.7).
/// </summary>
public sealed class TeamSpawnSelector(SpawnPoints spawnPoints, IEnginePlayers players) : ISpawnSelector
{
    private static readonly ImmutableArray<string> CombineClasses =
        [EntityNames.InfoPlayerCombine, EntityNames.InfoPlayerDeathmatch, EntityNames.InfoPlayerStart];

    private static readonly ImmutableArray<string> RebelClasses =
        [EntityNames.InfoPlayerRebel, EntityNames.InfoPlayerDeathmatch, EntityNames.InfoPlayerStart];

    public EntityHandle SelectSpawn(EntityHandle player)
    {
        IReadOnlyList<string> classes = players.GetTeam(player) switch
        {
            TeamDeathmatchMode.TeamCombine => CombineClasses,
            TeamDeathmatchMode.TeamRebels => RebelClasses,
            _ => Pickups.SpawnPointClasses,
        };

        return spawnPoints.Walk(player, classes, teamAware: true);
    }
}
