//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Shared.Resources;

namespace TF2.Server.Entities;

/// <summary>
/// tf_objective_resource: the networked face of <see cref="IObjectiveResource"/>
/// (stock's CTFObjectiveResource over CBaseTeamObjectiveResource). It holds
/// no state of its own: it attaches its net state to the table, and thinks
/// every LAZY_UPDATE_TIME (3 s) to publish the lazy capture percentages.
/// </summary>
[ScriptedEntity(ObjectiveClassNames.ObjectiveResource, ScriptedEntityKind.Effect)]
public sealed class ObjectiveResourceEntity(IObjectiveResource table, IEngineEntityRender render) : ScriptedEntity
{
    /// <summary>LAZY_UPDATE_TIME (team_objectiveresource.cpp:20).</summary>
    public const float LazyUpdateTime = 3f;

    /// <summary>What every client is told. Bound to this entity by the registry.</summary>
    [NetState] public ObjectiveResourceNetState State = new();

    public override void OnSpawn()
    {
        // UpdateTransmitState: FL_EDICT_ALWAYS.
        render.SetTransmitAlways(Handle, true);
        table.Attach(State);
        SetNextThink(LazyUpdateTime);
    }

    public override void OnThink()
    {
        SetNextThink(LazyUpdateTime);
        table.ObjectiveThink();
    }

    public override void OnRemoved() => table.Detach(State);
}

/// <summary>
/// The per-team table (score, rounds won, flag captures, role, player count),
/// written by the round flow and the modes. Like <see cref="IObjectiveResource"/>
/// it outlives its entity's absence: a write before the entity exists is
/// published when it attaches.
/// </summary>
public interface ITeamResource
{
    int Score(int team);
    void SetScore(int team, int score);
    int RoundsWon(int team);
    void SetRoundsWon(int team, int rounds);
    int FlagCaptures(int team);
    void SetFlagCaptures(int team, int captures);
    int Role(int team);
    void SetRole(int team, int role);
    int PlayerCount(int team);
    void SetPlayerCount(int team, int count);

    /// <summary>
    /// The team's standing objects (tf_team.cpp:328 AddObject's list), as
    /// entity indices; the run is truncated at
    /// <see cref="ObjectiveLimits.TeamObjects"/>.
    /// </summary>
    void SetObjects(int team, IReadOnlyList<int> indices);

    /// <summary>The team's leader (m_hLeader), as an entity index; 0 for none.</summary>
    int Leader(int team);

    /// <summary>The team's leader (m_hLeader), as an entity index; 0 for none.</summary>
    void SetLeader(int team, int index);

    /// <summary>A new level's table: every row zero.</summary>
    void Clear();

    void Attach(TeamResourceNetState state);
    void Detach(TeamResourceNetState state);
}

/// <summary>The per-team table (<see cref="ITeamResource"/>).</summary>
[SideService(Side.Server, As = [typeof(ITeamResource)])]
public sealed class TeamResource : ITeamResource
{
    private const int Teams = ObjectiveLimits.Teams;

    private readonly int[] _score = new int[Teams];
    private readonly int[] _roundsWon = new int[Teams];
    private readonly int[] _flagCaptures = new int[Teams];
    private readonly int[] _role = new int[Teams];
    private readonly int[] _playerCount = new int[Teams];
    private readonly List<int>[] _objects = Enumerable.Range(0, Teams).Select(_ => new List<int>()).ToArray();
    private readonly int[] _leader = new int[Teams];
    private TeamResourceNetState? _state;

    private static int Team(int team)
    {
        if ((uint)team >= Teams)
            throw new ArgumentOutOfRangeException(nameof(team), team, $"a TF2 team is 0..{Teams - 1}");
        return team;
    }

    public int Score(int team) => _score[Team(team)];

    public void SetScore(int team, int score)
    {
        _score[Team(team)] = score;
        if (_state is { } s)
            s.Score[team] = (short)score;
    }

    public int RoundsWon(int team) => _roundsWon[Team(team)];

    public void SetRoundsWon(int team, int rounds)
    {
        _roundsWon[Team(team)] = rounds;
        if (_state is { } s)
            s.RoundsWon[team] = (short)rounds;
    }

    public int FlagCaptures(int team) => _flagCaptures[Team(team)];

    public void SetFlagCaptures(int team, int captures)
    {
        _flagCaptures[Team(team)] = captures;
        if (_state is { } s)
            s.FlagCaptures[team] = (byte)captures;
    }

    public int Role(int team) => _role[Team(team)];

    public void SetRole(int team, int role)
    {
        _role[Team(team)] = role;
        if (_state is { } s)
            s.Role[team] = (byte)role;
    }

    public int PlayerCount(int team) => _playerCount[Team(team)];

    public void SetPlayerCount(int team, int count)
    {
        _playerCount[Team(team)] = count;
        if (_state is { } s)
            s.PlayerCount[team] = (byte)Math.Clamp(count, 0, byte.MaxValue);
    }

    public void SetObjects(int team, IReadOnlyList<int> indices)
    {
        int at = Team(team);
        List<int> list = _objects[at];
        list.Clear();
        for (int i = 0; i < indices.Count && i < ObjectiveLimits.TeamObjects; i++)
            list.Add(indices[i]);
        if (_state is { } s)
            PublishObjects(s, at, list);
    }

    public int Leader(int team) => _leader[Team(team)];

    public void SetLeader(int team, int index)
    {
        _leader[Team(team)] = index;
        if (_state is { } s)
            s.Leader[team] = (short)index;
    }

    public void Clear()
    {
        Array.Clear(_score);
        Array.Clear(_roundsWon);
        Array.Clear(_flagCaptures);
        Array.Clear(_role);
        Array.Clear(_playerCount);
        Array.Clear(_leader);
        foreach (List<int> run in _objects)
            run.Clear();
        if (_state is { } s)
            Publish(s);
    }

    public void Attach(TeamResourceNetState state)
    {
        _state = state;
        Publish(state);
    }

    public void Detach(TeamResourceNetState state)
    {
        if (ReferenceEquals(_state, state))
            _state = null;
    }

    private void Publish(TeamResourceNetState s)
    {
        for (int team = 0; team < Teams; team++)
        {
            s.Score[team] = (short)_score[team];
            s.RoundsWon[team] = (short)_roundsWon[team];
            s.FlagCaptures[team] = (byte)_flagCaptures[team];
            s.Role[team] = (byte)_role[team];
            s.PlayerCount[team] = (byte)Math.Clamp(_playerCount[team], 0, byte.MaxValue);
            s.Leader[team] = (short)_leader[team];
            PublishObjects(s, team, _objects[team]);
        }
    }

    /// <summary>The run is fixed-size, so the unused tail is zeroed (SendProxyArrayLength_TeamObjects sends the live length instead).</summary>
    private static void PublishObjects(TeamResourceNetState s, int team, List<int> list)
    {
        int at = team * ObjectiveLimits.TeamObjects;
        int count = Math.Min(list.Count, ObjectiveLimits.TeamObjects);
        for (int i = 0; i < count; i++)
            s.Objects[at + i] = (short)list[i];
        for (int i = count; i < ObjectiveLimits.TeamObjects; i++)
            s.Objects[at + i] = 0;
    }
}

/// <summary>
/// tf_team_resource: the networked face of <see cref="ITeamResource"/>. It
/// counts each team's players and lists each team's standing objects and
/// leader every tenth of a second, as stock's team entities' player and
/// object lists changed; the rest is written by the round flow.
/// </summary>
[ScriptedEntity(ObjectiveClassNames.TeamResource, ScriptedEntityKind.Effect)]
public sealed class TeamResourceEntity(ITeamResource table, ITeams teams, ITFBuildings buildings,
                                        IUniformRandom random, IEngineEntities entities,
                                        IEngineEntityRender render) : ScriptedEntity
{
    /// <summary>How often the player counts are refreshed.</summary>
    public const float ThinkInterval = 0.1f;

    [NetState] public TeamResourceNetState State = new();

    public override void OnSpawn()
    {
        // CTeam::UpdateTransmitState: FL_EDICT_ALWAYS.
        render.SetTransmitAlways(Handle, true);
        table.Attach(State);
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        for (int team = 0; team < ObjectiveLimits.Teams; team++)
        {
            table.SetPlayerCount(team, teams.Count(team));

            // CTFTeam's m_aObjects: every live building of the team.
            List<int> indices = new();
            foreach (TFBaseObject building in buildings.OwnedBy())
                if (building.Team == team)
                    indices.Add(entities.GetIndex(building.Handle));
            table.SetObjects(team, indices);

            // CTFTeam::AddPlayer leads with the first joiner; RemovePlayer
            // hands the team to a random member when the leader leaves. This
            // think cannot see the join itself, so a missing leader is the
            // team's first player.
            IReadOnlyList<EntityHandle> players = teams.Players(team);
            int leader = table.Leader(team);
            if (leader == 0)
            {
                if (players.Count > 0)
                    table.SetLeader(team, entities.GetIndex(players[0]));
            }
            else if (players.All(p => entities.GetIndex(p) != leader))
            {
                // the leader left: a random member, stock tf_team.cpp:465-479,
                // or none if the team emptied
                table.SetLeader(team, players.Count > 0
                    ? entities.GetIndex(players[random.RandomInt(0, players.Count - 1)])
                    : 0);
            }
        }

        SetNextThink(ThinkInterval);
    }

    public override void OnRemoved() => table.Detach(State);
}

/// <summary>
/// Makes the two networked tables at level init, as stock's
/// CTFGameRules::CreateStandardEntities made tf_objective_resource and the
/// team entities, and clears them before the map's entities spawn — the
/// master writes the objective table from its Activate, before the entity
/// exists.
/// </summary>
[SideService(Side.Server)]
public sealed class ObjectiveResourceSpawner(IObjectiveResource objectives, ITeamResource teams,
                                             IEngineEntities entities, ILog log) : IGameLifecycle
{
    /// <summary>
    /// A stage of its own in TF2's thousand (<see cref="TF2.Shared.TFClassTable.LoadOrder"/>'s
    /// note): an order that ties another participant's refuses the side's load.
    /// </summary>
    public const int Order = 1_000_000 - 1_000 + 20;

    public int LifecycleOrder => Order;

    public void OnLevelInitPreEntity(string mapName)
    {
        objectives.Clear();
        teams.Clear();
    }

    public void OnLevelInit(string mapName)
    {
        Create(ObjectiveClassNames.ObjectiveResource);
        Create(ObjectiveClassNames.TeamResource);
    }

    private void Create(string className)
    {
        var entity = entities.Create(className);
        if (entity == EntityHandle.None)
        {
            // Loud: the HUD reads nothing and says nothing (the core's
            // player_manager note).
            log.Warning($"[TF2/Objectives] could not create {className}; the HUD will read no objectives\n");
            return;
        }

        entities.Spawn(entity);
        log.Msg($"[TF2/Objectives] {className} up\n");
    }
}
