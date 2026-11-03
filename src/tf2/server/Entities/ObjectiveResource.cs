//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using TF2.Shared;
using TF2.Shared.Resources;

namespace TF2.Server.Entities;

/// <summary>
/// The objective table the control points, the capture areas and the master
/// write and the HUD reads: stock's CBaseTeamObjectiveResource's setters and
/// getters (team_objectiveresource.cpp), over a service rather than the
/// <c>g_pObjectiveResource</c> global.
///
/// <para>
/// A SERVICE AND AN ENTITY, because of when each exists. The master finds its
/// points in its Activate, while the map is still being built, and writes the
/// table then; the networked entity is made at level init, after the map
/// (<see cref="ObjectiveResourceSpawner"/>), as the core makes player_manager.
/// So the table lives here, and the entity, once spawned, attaches its net
/// state: everything written so far is published at once and every later
/// write goes through (<see cref="Attach"/>).
/// </para>
///
/// <para>
/// Indices: a point index outside 0..MAX_CONTROL_POINTS and a team outside
/// TF2's four throw; stock asserts (AssertValidIndex, a no-op in release) and
/// writes past its arrays (docs/divergences.md).
/// </para>
/// </summary>
public interface IObjectiveResource
{
    int NumControlPoints { get; }
    bool PlayingMiniRounds { get; }
    int UpdateCapHudParity { get; }

    /// <summary>The table a new level starts from (stock makes a new entity per level, and its Spawn clears).</summary>
    void Clear();

    void ResetControlPoints();
    void SetNumControlPoints(int count);
    void SetCPIcons(int index, int team, int icon);
    void SetCPOverlays(int index, int team, int icon);
    void SetTeamBaseIcons(int team, int icon);
    void SetCPPosition(int index, Vector position);
    void SetCPVisible(int index, bool visible);
    void SetCPRequiredCappers(int index, int team, int required);
    void SetCPCapTime(int index, int team, float time);
    void SetCPCapPercentage(int index, float percentage);
    float GetCPCapPercentage(int index);
    void SetTeamCanCap(int index, int team, bool canCap);
    bool TeamCanCapPoint(int index, int team);
    void SetBaseCP(int index, int team);
    int GetBaseControlPointForTeam(int team);
    void SetPreviousPoint(int index, int team, int previousIndex, int previousPoint);
    int GetPreviousPointForPoint(int index, int team, int previousIndex);
    void SetCapLayoutInHud(string layout);
    void SetCapLayoutCustomPosition(float x, float y);
    void SetWarnOnCap(int index, int warnLevel);
    void SetWarnSound(int index, string sound);
    string GetWarnSound(int index);
    void SetCPGroup(int index, int group);
    void SetCPLocked(int index, bool locked);
    bool GetCPLocked(int index);
    void SetCPUnlockTime(int index, float time);
    void SetCPTimerTime(int index, float time);
    void SetCPCapTimeScalesWithPlayers(int index, bool scales);
    void SetNumPlayers(int index, int team, int players);
    int GetNumPlayers(int index, int team);
    void StartCap(int index, int team);
    void SetOwningTeam(int index, int team);
    int GetOwningTeam(int index);
    void SetCappingTeam(int index, int team);
    int GetCappingTeam(int index);
    void SetTeamInZone(int index, int team);
    int GetTeamInZone(int index);
    void SetCapBlocked(int index, bool blocked);
    bool IsCapBlocked(int index);
    void SetTimerInHud(int timerEntityIndex);
    void SetStopWatchTimer(int timerEntityIndex);
    void SetPlayingMiniRounds(bool playing);
    void SetInMiniRound(int index, bool inRound);
    bool IsInMiniRound(int index);

    /// <summary>ObjectiveThink, every LAZY_UPDATE_TIME: the lazy percentages catch up with a capture in progress.</summary>
    void ObjectiveThink();

    /// <summary>The entity's net state: publishes the table and takes every later write.</summary>
    void Attach(ObjectiveResourceNetState state);

    /// <summary>The net state let go of (its entity was removed).</summary>
    void Detach(ObjectiveResourceNetState state);

    /// <summary>The lazily published percentage (m_flLazyCapPerc), as the HUD reads it.</summary>
    float GetLazyCapPercentage(int index);
}

/// <summary>
/// What a mode writes into the objective table: the train's part (payload)
/// and the flags' part (CTF). The modes (2.5, 2.6) call these; the control
/// points never do.
/// </summary>
public interface IModeObjectives
{
    /// <summary>Whether the map fields more than one train: stock's
    /// SetMultipleTrains from a tf_logic_multiple_escort's presence
    /// (tf_gamerules.cpp:4350-4357), which the escort HUD gates its group
    /// test and alarm on.</summary>
    void SetMultipleTrains(bool multiple);

    /// <summary>CBaseTeamObjectiveResource::SetTrainPathDistance: a point's place along the track.</summary>
    void SetTrainPathDistance(int index, float distance);

    /// <summary>ResetHillData( team ).</summary>
    void ResetHillData(int team);

    /// <summary>SetHillData( team, start, end, downhill ): the next hill of a team's track.</summary>
    void SetHillData(int team, float start, float end, bool downhill);

    /// <summary>SetTrackAlarm( index, alarm ).</summary>
    void SetTrackAlarm(int team, bool alarm);

    /// <summary>A team's train's progress along its track, 0..1.</summary>
    void SetTrainProgress(int team, float progress);

    /// <summary>A team's cart cappers (stock m_nNumCappers): -1 while the cart is blocked.</summary>
    void SetTrainCappers(int team, int cappers);

    /// <summary>A team's train speed level (stock m_iTrainSpeedLevel): -1 backwards, 0 stalled, 1..3 forward.</summary>
    void SetTrainSpeedLevel(int team, int level);

    /// <summary>When a team's waiting cart starts to recede, on the server clock; 0 for none (stock m_flRecedeTime).</summary>
    void SetTrainRecedeTime(int team, float time);

    /// <summary>A team's flag: its status (0 home, 1 stolen, 2 dropped), carrier entity index (0 none), return time.</summary>
    void SetFlag(int team, int status, int carrierIndex, float resetTime);

    /// <summary>A team's flag's world origin at its last status change (home's spawn point, a drop point).</summary>
    void SetFlagOrigin(int team, Vector origin);

    /// <summary>A team's capture zone's centre, for the carried flag's arrow.</summary>
    void SetZoneOrigin(int team, Vector origin);

    /// <summary>Whether a team has a flag in play at all (a status of home is otherwise indistinguishable from no flag).</summary>
    void SetFlagPresent(int team, bool present);
}

/// <summary>The objective table (<see cref="IObjectiveResource"/>, <see cref="IModeObjectives"/>).</summary>
[SideService(Side.Server, As = [typeof(IObjectiveResource), typeof(IModeObjectives)])]
public sealed class ObjectiveResource : IObjectiveResource, IModeObjectives
{
    private const int Points = ObjectiveLimits.MaxControlPoints;
    private const int Teams = ObjectiveLimits.Teams;
    private const int PerTeam = Points * Teams;
    private const int HillFloats = ObjectiveLimits.TrainMaxTeams * ObjectiveLimits.TrainMaxHills * ObjectiveLimits.TrainFloatsPerHill;
    private const int Hills = ObjectiveLimits.TrainMaxTeams * ObjectiveLimits.TrainMaxHills;

    private ObjectiveResourceNetState? _state;

    private int _timerToShowInHud;
    private int _stopWatchTimer;
    private int _numControlPoints;
    private bool _playingMiniRounds;
    private bool _controlPointsReset;
    private int _updateCapHudParity;
    private string _capLayoutInHud = string.Empty;
    private float _customPositionX = -1f;
    private float _customPositionY = -1f;

    private readonly Vector[] _positions = new Vector[Points];
    private readonly bool[] _visible = new bool[Points];
    private readonly float[] _lazyCapPerc = new float[Points];
    private readonly bool[] _inMiniRound = new bool[Points];
    private readonly int[] _warnOnCap = new int[Points];
    private readonly string[] _warnSound = new string[Points];
    private readonly bool[] _locked = new bool[Points];
    private readonly float[] _unlockTimes = new float[Points];
    private readonly float[] _timerTimes = new float[Points];
    private readonly int[] _cappingTeam = new int[Points];
    private readonly int[] _teamInZone = new int[Points];
    private readonly bool[] _blocked = new bool[Points];
    private readonly int[] _owner = new int[Points];
    private readonly bool[] _capRateScales = new bool[Points];
    private readonly int[] _group = new int[Points];
    private readonly float[] _capPercentages = new float[Points];
    private readonly float[] _pathDistance = new float[Points];
    private bool _multipleTrains;

    private readonly int[] _teamIcons = new int[PerTeam];
    private readonly int[] _teamOverlays = new int[PerTeam];
    private readonly int[] _teamReqCappers = new int[PerTeam];
    private readonly float[] _teamCapTime = new float[PerTeam];
    private readonly bool[] _teamCanCap = new bool[PerTeam];
    private readonly int[] _numTeamMembers = new int[PerTeam];
    private readonly int[] _previousPoints = new int[PerTeam * ObjectiveLimits.MaxPreviousPoints];

    private readonly int[] _teamBaseIcons = new int[Teams];
    private readonly int[] _baseControlPoints = new int[Teams];

    private readonly int[] _numNodeHillData = new int[ObjectiveLimits.TrainMaxTeams];
    private readonly float[] _nodeHillData = new float[HillFloats];
    private readonly bool[] _trackAlarm = new bool[ObjectiveLimits.TrainMaxTeams];
    private readonly bool[] _hillIsDownhill = new bool[Hills];
    private readonly float[] _trainProgress = new float[ObjectiveLimits.TrainMaxTeams];
    private readonly int[] _trainCappers = new int[ObjectiveLimits.TrainMaxTeams];
    private readonly int[] _trainSpeedLevel = new int[ObjectiveLimits.TrainMaxTeams];
    private readonly float[] _trainRecedeTime = new float[ObjectiveLimits.TrainMaxTeams];

    private readonly int[] _flagStatus = new int[Teams];
    private readonly int[] _flagCarrier = new int[Teams];
    private readonly float[] _flagResetTime = new float[Teams];
    private readonly bool[] _flagPresent = new bool[Teams];
    private readonly Vector[] _flagOrigin = new Vector[Teams];
    private readonly Vector[] _zoneOrigin = new Vector[Teams];

    /// <summary>Stock's constructor and Spawn: the table a level starts from.</summary>
    public ObjectiveResource()
    {
        Clear();
    }

    /// <summary>
    /// CBaseTeamObjectiveResource::Spawn's defaults (team_objectiveresource.cpp:130-205),
    /// and the constructor's: what a new level starts from.
    /// </summary>
    public void Clear()
    {
        _timerToShowInHud = 0;
        _stopWatchTimer = 0;
        _numControlPoints = 0;
        _playingMiniRounds = false;
        _controlPointsReset = false;
        _updateCapHudParity = 0;
        _capLayoutInHud = string.Empty;
        _customPositionX = -1f;
        _customPositionY = -1f;
        _multipleTrains = false;

        for (int i = 0; i < Points; i++)
        {
            _positions[i] = default;
            _visible[i] = true;
            _blocked[i] = false;
            _owner[i] = (int)TFTeam.Unassigned;
            _cappingTeam[i] = (int)TFTeam.Unassigned;
            _teamInZone[i] = (int)TFTeam.Unassigned;
            _inMiniRound[i] = true;
            _warnOnCap[i] = 0;          // CP_WARN_NORMAL
            _warnSound[i] = string.Empty;
            _group[i] = -1;             // TEAM_INVALID
            _lazyCapPerc[i] = 0f;
            _locked[i] = false;
            _unlockTimes[i] = 0f;
            _timerTimes[i] = -1f;
            _capRateScales[i] = true;
            _capPercentages[i] = 0f;
            _pathDistance[i] = 0f;

            for (int team = 0; team < Teams; team++)
            {
                int slot = ObjectiveLimits.TeamArray(i, team);
                _teamIcons[slot] = 0;
                _teamOverlays[slot] = 0;
                _teamReqCappers[slot] = 0;
                _teamCapTime[slot] = 0f;
                _numTeamMembers[slot] = 0;
                _teamCanCap[slot] = false;

                for (int previous = 0; previous < ObjectiveLimits.MaxPreviousPoints; previous++)
                    _previousPoints[ObjectiveLimits.PreviousArray(i, team, previous)] = -1;
            }
        }

        for (int team = 0; team < Teams; team++)
        {
            _baseControlPoints[team] = -1;
            _teamBaseIcons[team] = 0;
            _flagStatus[team] = 0;
            _flagCarrier[team] = 0;
            _flagResetTime[team] = 0f;
            _flagPresent[team] = false;
            _flagOrigin[team] = default;
            _zoneOrigin[team] = default;
        }

        for (int team = 0; team < ObjectiveLimits.TrainMaxTeams; team++)
        {
            _numNodeHillData[team] = 0;
            _trackAlarm[team] = false;
            _trainProgress[team] = 0f;
            _trainCappers[team] = 0;
            _trainSpeedLevel[team] = 0;
            _trainRecedeTime[team] = 0f;
        }

        Array.Clear(_nodeHillData);
        Array.Clear(_hillIsDownhill);

        if (_state is not null)
            Publish(_state);
    }

    public int NumControlPoints => _numControlPoints;

    public bool PlayingMiniRounds => _playingMiniRounds;

    public int UpdateCapHudParity => _updateCapHudParity;

    // ---- the entity ---------------------------------------------------------

    public void Attach(ObjectiveResourceNetState state)
    {
        _state = state;
        Publish(state);
    }

    public void Detach(ObjectiveResourceNetState state)
    {
        if (ReferenceEquals(_state, state))
            _state = null;
    }

    /// <summary>Every value into the net state, for an entity that has just attached.</summary>
    private void Publish(ObjectiveResourceNetState s)
    {
        s.TimerToShowInHud = _timerToShowInHud;
        s.StopWatchTimer = _stopWatchTimer;
        s.NumControlPoints = (byte)_numControlPoints;
        s.PlayingMiniRounds = _playingMiniRounds;
        s.ControlPointsReset = _controlPointsReset;
        s.UpdateCapHudParity = (byte)_updateCapHudParity;
        s.CapLayoutInHud = _capLayoutInHud;
        s.CustomPositionX = _customPositionX;
        s.CustomPositionY = _customPositionY;
        s.MultipleTrains = _multipleTrains;

        for (int i = 0; i < Points; i++)
        {
            WritePosition(s, i);
            s.Visible[i] = _visible[i];
            s.LazyCapPerc[i] = _lazyCapPerc[i];
            s.InMiniRound[i] = _inMiniRound[i];
            s.WarnOnCap[i] = (byte)_warnOnCap[i];
            s.Locked[i] = _locked[i];
            s.UnlockTimes[i] = _unlockTimes[i];
            s.TimerTimes[i] = _timerTimes[i];
            s.CappingTeam[i] = (byte)_cappingTeam[i];
            s.TeamInZone[i] = (byte)_teamInZone[i];
            s.Blocked[i] = _blocked[i];
            s.Owner[i] = (byte)_owner[i];
            s.CapRateScalesWithPlayers[i] = _capRateScales[i];
            s.Group[i] = (sbyte)_group[i];
            s.PathDistance[i] = _pathDistance[i];
        }

        for (int i = 0; i < PerTeam; i++)
        {
            s.TeamIcons[i] = (short)_teamIcons[i];
            s.TeamOverlays[i] = (short)_teamOverlays[i];
            s.TeamReqCappers[i] = (byte)_teamReqCappers[i];
            s.TeamCapTime[i] = _teamCapTime[i];
            s.TeamCanCap[i] = _teamCanCap[i];
            s.NumTeamMembers[i] = (byte)_numTeamMembers[i];
        }

        for (int i = 0; i < _previousPoints.Length; i++)
            s.PreviousPoints[i] = (sbyte)_previousPoints[i];

        for (int team = 0; team < Teams; team++)
        {
            s.TeamBaseIcons[team] = (short)_teamBaseIcons[team];
            s.BaseControlPoints[team] = (sbyte)_baseControlPoints[team];
            s.FlagStatus[team] = (byte)_flagStatus[team];
            s.FlagCarrier[team] = (short)_flagCarrier[team];
            s.FlagResetTime[team] = _flagResetTime[team];
            s.HasFlag[team] = _flagPresent[team];
        }

        for (int team = 0; team < ObjectiveLimits.TrainMaxTeams; team++)
        {
            s.NumNodeHillData[team] = (byte)_numNodeHillData[team];
            s.TrackAlarm[team] = _trackAlarm[team];
            s.TrainProgress[team] = _trainProgress[team];
            s.TrainCappers[team] = (sbyte)_trainCappers[team];
            s.TrainSpeedLevel[team] = (sbyte)_trainSpeedLevel[team];
            s.TrainRecedeTime[team] = _trainRecedeTime[team];
        }

        for (int i = 0; i < HillFloats; i++)
            s.NodeHillData[i] = _nodeHillData[i];

        for (int i = 0; i < Hills; i++)
            s.HillIsDownhill[i] = _hillIsDownhill[i];
    }

    private void WritePosition(ObjectiveResourceNetState s, int index)
    {
        s.Positions[index * 3] = _positions[index].X;
        s.Positions[index * 3 + 1] = _positions[index].Y;
        s.Positions[index * 3 + 2] = _positions[index].Z;
    }

    // ---- the checks stock asserts -------------------------------------------

    private static int Point(int index)
    {
        if ((uint)index >= Points)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"a control point index is 0..{Points - 1}");
        return index;
    }

    private static int Team(int team)
    {
        if ((uint)team >= Teams)
            throw new ArgumentOutOfRangeException(nameof(team), team, $"a TF2 team is 0..{Teams - 1}");
        return team;
    }

    private static int Slot(int index, int team) => ObjectiveLimits.TeamArray(Point(index), Team(team));

    /// <summary>UpdateCapHudElement: the parity the HUD watches for a change.</summary>
    private void UpdateCapHudElement()
    {
        _updateCapHudParity = (_updateCapHudParity + 1) & ObjectiveLimits.CapHudParityMask;
        if (_state is { } s)
            s.UpdateCapHudParity = (byte)_updateCapHudParity;
    }

    // ---- CBaseTeamObjectiveResource -----------------------------------------

    public void ResetControlPoints()
    {
        for (int i = 0; i < Points; i++)
        {
            _cappingTeam[i] = (int)TFTeam.Unassigned;
            _teamInZone[i] = (int)TFTeam.Unassigned;
            _inMiniRound[i] = true;
            if (_state is { } s)
            {
                s.CappingTeam[i] = 0;
                s.TeamInZone[i] = 0;
                s.InMiniRound[i] = true;
            }

            for (int team = 0; team < Teams; team++)
            {
                int slot = ObjectiveLimits.TeamArray(i, team);
                _numTeamMembers[slot] = 0;
                if (_state is { } st)
                    st.NumTeamMembers[slot] = 0;
            }
        }

        UpdateCapHudElement();
        _controlPointsReset = !_controlPointsReset;
        if (_state is { } state)
            state.ControlPointsReset = _controlPointsReset;
    }

    public void SetNumControlPoints(int count)
    {
        if ((uint)count > Points)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"at most {Points} control points");
        _numControlPoints = count;
        if (_state is { } s)
            s.NumControlPoints = (byte)count;
    }

    public void SetCPIcons(int index, int team, int icon)
    {
        int slot = Slot(index, team);
        _teamIcons[slot] = icon;
        if (_state is { } s)
            s.TeamIcons[slot] = (short)icon;
    }

    public void SetCPOverlays(int index, int team, int icon)
    {
        int slot = Slot(index, team);
        _teamOverlays[slot] = icon;
        if (_state is { } s)
            s.TeamOverlays[slot] = (short)icon;
    }

    public void SetTeamBaseIcons(int team, int icon)
    {
        _teamBaseIcons[Team(team)] = icon;
        if (_state is { } s)
            s.TeamBaseIcons[team] = (short)icon;
    }

    public void SetCPPosition(int index, Vector position)
    {
        _positions[Point(index)] = position;
        if (_state is { } s)
            WritePosition(s, index);
    }

    public void SetCPVisible(int index, bool visible)
    {
        _visible[Point(index)] = visible;
        if (_state is { } s)
            s.Visible[index] = visible;
    }

    public void SetWarnOnCap(int index, int warnLevel)
    {
        _warnOnCap[Point(index)] = warnLevel;
        if (_state is { } s)
            s.WarnOnCap[index] = (byte)warnLevel;
    }

    /// <summary>
    /// Kept on the server only: a run of strings has no wire form here
    /// (NetArray refuses one), and stock's reader is the client's announcer,
    /// which is Stage 3's (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public void SetWarnSound(int index, string sound) => _warnSound[Point(index)] = sound ?? string.Empty;

    public string GetWarnSound(int index) => _warnSound[Point(index)];

    public void SetCPGroup(int index, int group)
    {
        _group[Point(index)] = group;
        if (_state is { } s)
            s.Group[index] = (sbyte)group;
    }

    public void SetCPRequiredCappers(int index, int team, int required)
    {
        int slot = Slot(index, team);
        _teamReqCappers[slot] = required;
        if (_state is { } s)
            s.TeamReqCappers[slot] = (byte)required;
    }

    public void SetCPCapTime(int index, int team, float time)
    {
        int slot = Slot(index, team);
        _teamCapTime[slot] = time;
        if (_state is { } s)
            s.TeamCapTime[slot] = time;
    }

    /// <summary>m_flCapPercentages: server-side, published lazily by <see cref="ObjectiveThink"/>.</summary>
    public void SetCPCapPercentage(int index, float percentage) => _capPercentages[Point(index)] = percentage;

    public float GetCPCapPercentage(int index) => _capPercentages[Point(index)];

    public void SetCPUnlockTime(int index, float time)
    {
        _unlockTimes[Point(index)] = time;
        if (_state is { } s)
            s.UnlockTimes[index] = time;
    }

    public void SetCPTimerTime(int index, float time)
    {
        _timerTimes[Point(index)] = time;
        if (_state is { } s)
            s.TimerTimes[index] = time;
    }

    public void SetCPCapTimeScalesWithPlayers(int index, bool scales)
    {
        _capRateScales[Point(index)] = scales;
        if (_state is { } s)
            s.CapRateScalesWithPlayers[index] = scales;
    }

    public void SetTeamCanCap(int index, int team, bool canCap)
    {
        int slot = Slot(index, team);
        _teamCanCap[slot] = canCap;
        if (_state is { } s)
            s.TeamCanCap[slot] = canCap;
        UpdateCapHudElement();
    }

    public bool TeamCanCapPoint(int index, int team) => _teamCanCap[Slot(index, team)];

    public void SetBaseCP(int index, int team)
    {
        _baseControlPoints[Team(team)] = index;
        if (_state is { } s)
            s.BaseControlPoints[team] = (sbyte)index;
    }

    public int GetBaseControlPointForTeam(int team) => _baseControlPoints[Team(team)];

    public void SetPreviousPoint(int index, int team, int previousIndex, int previousPoint)
    {
        int slot = ObjectiveLimits.PreviousArray(Point(index), Team(team), Previous(previousIndex));
        _previousPoints[slot] = previousPoint;
        if (_state is { } s)
            s.PreviousPoints[slot] = (sbyte)previousPoint;
    }

    public int GetPreviousPointForPoint(int index, int team, int previousIndex)
        => _previousPoints[ObjectiveLimits.PreviousArray(Point(index), Team(team), Previous(previousIndex))];

    private static int Previous(int previousIndex)
    {
        if ((uint)previousIndex >= ObjectiveLimits.MaxPreviousPoints)
            throw new ArgumentOutOfRangeException(nameof(previousIndex), previousIndex,
                                                  $"a previous point is 0..{ObjectiveLimits.MaxPreviousPoints - 1}");
        return previousIndex;
    }

    /// <summary>Q_strncpy into MAX_CAPLAYOUT_LENGTH: at most 31 characters and the terminator.</summary>
    public void SetCapLayoutInHud(string layout)
    {
        layout ??= string.Empty;
        _capLayoutInHud = layout.Length < ObjectiveLimits.MaxCapLayoutLength
            ? layout
            : layout[..(ObjectiveLimits.MaxCapLayoutLength - 1)];
        if (_state is { } s)
            s.CapLayoutInHud = _capLayoutInHud;
    }

    public void SetCapLayoutCustomPosition(float x, float y)
    {
        _customPositionX = x;
        _customPositionY = y;
        if (_state is { } s)
        {
            s.CustomPositionX = x;
            s.CustomPositionY = y;
        }
    }

    /// <summary>No index check, as stock's: "This assert always fires on map load".</summary>
    public void SetCPLocked(int index, bool locked)
    {
        _locked[Point(index)] = locked;
        if (_state is { } s)
            s.Locked[index] = locked;
    }

    public bool GetCPLocked(int index) => _locked[Point(index)];

    public void SetNumPlayers(int index, int team, int players)
    {
        int slot = Slot(index, team);
        _numTeamMembers[slot] = players;
        if (_state is { } s)
            s.NumTeamMembers[slot] = (byte)Math.Clamp(players, 0, byte.MaxValue);
        UpdateCapHudElement();
    }

    public int GetNumPlayers(int index, int team) => _numTeamMembers[Slot(index, team)];

    public void StartCap(int index, int team) => SetCappingTeam(index, team);

    public void SetOwningTeam(int index, int team)
    {
        _owner[Point(index)] = Team(team);
        // clear the capper
        _cappingTeam[index] = (int)TFTeam.Unassigned;
        if (_state is { } s)
        {
            s.Owner[index] = (byte)team;
            s.CappingTeam[index] = 0;
        }
        UpdateCapHudElement();
    }

    public int GetOwningTeam(int index)
    {
        if (index >= _numControlPoints)
            return (int)TFTeam.Unassigned;
        return _owner[Point(index)];
    }

    public void SetCappingTeam(int index, int team)
    {
        if (_cappingTeam[Point(index)] == Team(team))
            return;
        _cappingTeam[index] = team;
        if (_state is { } s)
            s.CappingTeam[index] = (byte)team;
        UpdateCapHudElement();
    }

    public int GetCappingTeam(int index)
    {
        if (index >= _numControlPoints)
            return (int)TFTeam.Unassigned;
        return _cappingTeam[Point(index)];
    }

    public void SetTeamInZone(int index, int team)
    {
        if (_teamInZone[Point(index)] == Team(team))
            return;
        _teamInZone[index] = team;
        if (_state is { } s)
            s.TeamInZone[index] = (byte)team;
        UpdateCapHudElement();
    }

    public int GetTeamInZone(int index) => _teamInZone[Point(index)];

    public void SetCapBlocked(int index, bool blocked)
    {
        if (_blocked[Point(index)] == blocked)
            return;
        _blocked[index] = blocked;
        if (_state is { } s)
            s.Blocked[index] = blocked;
        UpdateCapHudElement();
    }

    public bool IsCapBlocked(int index) => _blocked[Point(index)];

    public void SetTimerInHud(int timerEntityIndex)
    {
        _timerToShowInHud = timerEntityIndex;
        if (_state is { } s)
            s.TimerToShowInHud = timerEntityIndex;
    }

    public void SetStopWatchTimer(int timerEntityIndex)
    {
        _stopWatchTimer = timerEntityIndex;
        if (_state is { } s)
            s.StopWatchTimer = timerEntityIndex;
    }

    public void SetPlayingMiniRounds(bool playing)
    {
        _playingMiniRounds = playing;
        if (_state is { } s)
            s.PlayingMiniRounds = playing;
    }

    public void SetInMiniRound(int index, bool inRound)
    {
        _inMiniRound[Point(index)] = inRound;
        if (_state is { } s)
            s.InMiniRound[index] = inRound;
    }

    public bool IsInMiniRound(int index) => _inMiniRound[Point(index)];

    public void ObjectiveThink()
    {
        for (int i = 0; i < _numControlPoints; i++)
        {
            if (_cappingTeam[i] != 0)
            {
                _lazyCapPerc[i] = _capPercentages[i];
                if (_state is { } s)
                    s.LazyCapPerc[i] = _capPercentages[i];
            }
        }
    }

    /// <summary>The lazily published percentage (m_flLazyCapPerc), as the HUD reads it.</summary>
    public float GetLazyCapPercentage(int index) => _lazyCapPerc[Point(index)];

    // ---- IModeObjectives ----------------------------------------------------

    /// <summary>Stock's SetMultipleTrains on the gamerules; here a plain write
    /// into the objective table, set once per round by the payload mode.</summary>
    public void SetMultipleTrains(bool multiple)
    {
        _multipleTrains = multiple;
        if (_state is { } s)
            s.MultipleTrains = multiple;
    }

    public void SetTrainPathDistance(int index, float distance)
    {
        _pathDistance[Point(index)] = distance;
        if (_state is { } s)
            s.PathDistance[index] = distance;
    }

    /// <summary>ResetHillData: a team out of TEAM_TRAIN_MAX_TEAMS is ignored, as stock's guard does.</summary>
    public void ResetHillData(int team)
    {
        if ((uint)team >= ObjectiveLimits.TrainMaxTeams)
            return;

        int perTeam = ObjectiveLimits.TrainMaxHills * ObjectiveLimits.TrainFloatsPerHill;
        for (int i = 0; i < perTeam; i++)
        {
            _nodeHillData[team * perTeam + i] = 0f;
            if (_state is { } s)
                s.NodeHillData[team * perTeam + i] = 0f;
        }

        for (int i = 0; i < ObjectiveLimits.TrainMaxHills; i++)
        {
            _hillIsDownhill[team * ObjectiveLimits.TrainMaxHills + i] = false;
            if (_state is { } s)
                s.HillIsDownhill[team * ObjectiveLimits.TrainMaxHills + i] = false;
        }

        _numNodeHillData[team] = 0;
        if (_state is { } st)
            st.NumNodeHillData[team] = 0;
    }

    /// <summary>SetHillData (team_objectiveresource.h:152-170): appends while a team has room.</summary>
    public void SetHillData(int team, float start, float end, bool downhill)
    {
        if ((uint)team >= ObjectiveLimits.TrainMaxTeams)
            return;

        int index = _numNodeHillData[team] * ObjectiveLimits.TrainFloatsPerHill
                    + team * ObjectiveLimits.TrainMaxHills * ObjectiveLimits.TrainFloatsPerHill;
        if (index >= HillFloats - 1)
            return;

        _nodeHillData[index] = start;
        _nodeHillData[index + 1] = end;
        if (_state is { } s)
        {
            s.NodeHillData[index] = start;
            s.NodeHillData[index + 1] = end;
        }

        if (_numNodeHillData[team] < ObjectiveLimits.TrainMaxHills)
        {
            int hill = _numNodeHillData[team] + team * ObjectiveLimits.TrainMaxHills;
            _hillIsDownhill[hill] = downhill;
            if (_state is { } st)
                st.HillIsDownhill[hill] = downhill;
        }

        _numNodeHillData[team]++;
        if (_state is { } state)
            state.NumNodeHillData[team] = (byte)_numNodeHillData[team];
    }

    public void SetTrackAlarm(int team, bool alarm)
    {
        if ((uint)team >= ObjectiveLimits.TrainMaxTeams)
            throw new ArgumentOutOfRangeException(nameof(team), team, "a train team is 0..3");
        _trackAlarm[team] = alarm;
        if (_state is { } s)
            s.TrackAlarm[team] = alarm;
    }

    public void SetTrainProgress(int team, float progress)
    {
        if ((uint)team >= ObjectiveLimits.TrainMaxTeams)
            throw new ArgumentOutOfRangeException(nameof(team), team, "a train team is 0..3");
        _trainProgress[team] = progress;
        if (_state is { } s)
            s.TrainProgress[team] = progress;
    }

    public void SetTrainCappers(int team, int cappers)
    {
        if ((uint)team >= ObjectiveLimits.TrainMaxTeams)
            throw new ArgumentOutOfRangeException(nameof(team), team, "a train team is 0..3");
        _trainCappers[team] = cappers;
        if (_state is { } s)
            s.TrainCappers[team] = (sbyte)cappers;
    }

    public void SetTrainSpeedLevel(int team, int level)
    {
        if ((uint)team >= ObjectiveLimits.TrainMaxTeams)
            throw new ArgumentOutOfRangeException(nameof(team), team, "a train team is 0..3");
        _trainSpeedLevel[team] = level;
        if (_state is { } s)
            s.TrainSpeedLevel[team] = (sbyte)level;
    }

    public void SetTrainRecedeTime(int team, float time)
    {
        if ((uint)team >= ObjectiveLimits.TrainMaxTeams)
            throw new ArgumentOutOfRangeException(nameof(team), team, "a train team is 0..3");
        _trainRecedeTime[team] = time;
        if (_state is { } s)
            s.TrainRecedeTime[team] = time;
    }

    public void SetFlag(int team, int status, int carrierIndex, float resetTime)
    {
        _flagStatus[Team(team)] = status;
        _flagCarrier[team] = carrierIndex;
        _flagResetTime[team] = resetTime;
        if (_state is { } s)
        {
            s.FlagStatus[team] = (byte)status;
            s.FlagCarrier[team] = (short)carrierIndex;
            s.FlagResetTime[team] = resetTime;
        }
        UpdateCapHudElement();
    }

    public void SetFlagPresent(int team, bool present)
    {
        _flagPresent[Team(team)] = present;
        if (_state is { } s)
            s.HasFlag[team] = present;
    }

    /// <summary>The flag's origin at its last status change; the mode publishes on every change, so a plain write.</summary>
    public void SetFlagOrigin(int team, Vector origin)
    {
        _flagOrigin[Team(team)] = origin;
        if (_state is { } s)
        {
            s.FlagOrigin[team * 3] = origin.X;
            s.FlagOrigin[team * 3 + 1] = origin.Y;
            s.FlagOrigin[team * 3 + 2] = origin.Z;
        }
    }

    public void SetZoneOrigin(int team, Vector origin)
    {
        _zoneOrigin[Team(team)] = origin;
        if (_state is { } s)
        {
            s.ZoneOrigin[team * 3] = origin.X;
            s.ZoneOrigin[team * 3 + 1] = origin.Y;
            s.ZoneOrigin[team * 3 + 2] = origin.Z;
        }
    }
}
