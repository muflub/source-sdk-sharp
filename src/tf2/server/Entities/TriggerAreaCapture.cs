//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Resources;

namespace TF2.Server.Entities;

/// <summary>
/// Who is a capper to an area, per player: alive, on which team, a player at
/// all. One seam so a fact can stand two teams on one point (the engine's
/// accessors answer per call, not per player, in the test doubles).
/// </summary>
public interface ICapturePlayers
{
    bool IsPlayer(EntityHandle entity);
    bool IsAlive(EntityHandle player);
    int Team(EntityHandle player);
}

/// <summary>The engine's answers for <see cref="ICapturePlayers"/>.</summary>
[SideService(Side.Server, As = [typeof(ICapturePlayers)])]
public sealed class EngineCapturePlayers(IEngineEntities entities, IEnginePlayers players, ITeams teams) : ICapturePlayers
{
    public bool IsPlayer(EntityHandle entity) => entities.IsPlayer(entity);

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public int Team(EntityHandle player) => teams.Team(player);
}

/// <summary>
/// What a payload train watcher hears from the area its cart sits in
/// (CTeamTrainWatcher::SetNumTrainCappers): the cappers, -1 while blocked.
/// The payload mode (2.6) sets <see cref="TriggerAreaCapture.TrainWatcher"/>.
/// </summary>
public interface ICaptureAreaWatcher
{
    void SetNumTrainCappers(int cappers, TriggerAreaCapture area);
}

/// <summary>
/// trigger_capture_area: the volume players stand in to capture a point
/// (stock's CTriggerAreaCapture, trigger_area_capture.cpp). Every tenth of a
/// second it counts each team's cappers on the point and moves the capture:
/// <see cref="CaptureThink"/> is stock's L304 line for line, including
/// mp_capstyle's diminishing returns, mp_blockstyle's block, and the decay
/// of an abandoned capture.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public class TriggerAreaCapture(
    IGameClock clock,
    IObjectiveSettings settings,
    IObjectiveResource objectives,
    IObjectiveRoundRules roundRules,
    IControlPointRules rules,
    ICapturePlayerRules playerRules,
    ICapturePlayers players,
    IObjectiveEntities entities,
    IObjectiveEvents events,
    ITFPlayerObjectiveTimes? objectiveTimes = null,
    ITFPlayerStats? stats = null,
    IEngineGameEvents? gameEvents = null) : TriggerVolume(clock)
{
    public const string MapClass = "trigger_capture_area";

    /// <summary>AREA_THINK_TIME.</summary>
    public const float AreaThinkTime = 0.1f;

    /// <summary>CAPTURE_NORMAL.</summary>
    public const int CaptureNormal = 0;

    /// <summary>MAX_AREA_CAPPERS: the null-terminated list holds eight.</summary>
    public const int MaxAreaCappers = 9;

    private const int Teams = ObjectiveLimits.Teams;
    private const int FirstGameTeam = (int)TFTeam.Red;

    public readonly Output OnStartTeam1 = new();
    public readonly Output OnStartTeam2 = new();
    public readonly Output OnBreakTeam1 = new();
    public readonly Output OnBreakTeam2 = new();
    public readonly Output OnCapTeam1 = new();
    public readonly Output OnCapTeam2 = new();
    public readonly Output OnStartCap = new();
    public readonly Output OnBreakCap = new();
    public readonly Output OnEndCap = new();
    public readonly Output OnNumCappersChanged = new();
    public readonly Output OnNumCappersChanged2 = new();

    [ScriptVars] public TriggerAreaCaptureVars AreaVars = new();

    /// <summary>perteamdata_t.</summary>
    private struct TeamData
    {
        public int NumRequiredToCap;
        public int NumTouching;
        public int BlockedTouching; // Number of capping players on the cap while it's being blocked
        public bool CanCap;
        public int SpawnAdjust;
        public int NumRequiredToStartCap;
    }

    /// <summary>blockers_t.</summary>
    private struct Blocker
    {
        public EntityHandle Player;
        public int CapAttemptNumber;
        public float NextBlockTime;
    }

    private readonly TeamData[] _teamData = new TeamData[Teams];

    private readonly List<Blocker> _blockers = new();

    private int _capMode;
    private bool _capturing;
    private int _capturingTeam;
    private int _owningTeam;
    private int _teamInZone;
    private float _timeRemaining;
    private float _lastReductionTime;
    private bool _blocked;
    private int _capAttemptNumber;
    private bool _startTouch;
    private string _capPointName = string.Empty;

    // ---- what the rules, the round and the mode read ------------------------

    public TeamControlPoint? ControlPoint { get; private set; }

    public bool IsActive => !Disabled;

    public bool IsCapturing => _capturing;

    public int CapturingTeam => _capturingTeam;

    public int OwningTeam => _owningTeam;

    public int TeamInZone => _teamInZone;

    public bool IsBlocked => _blocked;

    public float TimeRemaining => _timeRemaining;

    public float CapTime => AreaVars.CapTime;

    public int CapAttemptNumber => _capAttemptNumber;

    public bool TeamCanCap(int team) => _teamData[team].CanCap;

    public int NumTouching(int team) => _teamData[team].NumTouching;

    public int NumRequiredToCap(int team) => _teamData[team].NumRequiredToCap;

    /// <summary>The payload watcher this area drives, if the mode set one.</summary>
    public ICaptureAreaWatcher? TrainWatcher { get; set; }

    /// <summary>CaptureModeScalesWithPlayers: mp_capstyle. The timer door pins it off.</summary>
    protected virtual bool CaptureModeScalesWithPlayers => settings.CapStyle != 0;

    // ---- keys and spawn -----------------------------------------------------

    /// <summary>The per-team keys (L119-151): team_numcap_N, team_cancap_N, team_spawn_N, team_startcap_N.</summary>
    public override bool OnKeyValue(string key, string value)
    {
        if (TeamKey(key, "team_numcap_") is int numcap) { _teamData[numcap].NumRequiredToCap = ParseInt(value); return true; }
        if (TeamKey(key, "team_cancap_") is int cancap) { _teamData[cancap].CanCap = ParseInt(value) != 0; return true; }
        if (TeamKey(key, "team_spawn_") is int spawn) { _teamData[spawn].SpawnAdjust = ParseInt(value); return true; }
        if (TeamKey(key, "team_startcap_") is int startcap) { _teamData[startcap].NumRequiredToStartCap = ParseInt(value); return true; }
        return false;
    }

    private int? TeamKey(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        int team = ParseInt(key[prefix.Length..], -1);
        if ((uint)team < Teams)
            return team;

        Log.Warning($"[TF2] {MapClass} '{Name}': {key} names no team\n");
        return null;
    }

    /// <summary>Clients always (AddSpawnFlags( SF_TRIGGER_ALLOW_CLIENTS )), then the volume's own kinds.</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.Clients;

    protected override bool Passes(EntityHandle toucher)
        => players.IsPlayer(toucher) ? PassesFilter(toucher) : base.Passes(toucher);

    public override void OnSpawn()
    {
        base.OnSpawn();

        _capPointName = AreaVars.CapPointName;

        for (int i = 0; i < Teams; i++)
        {
            if (_teamData[i].NumRequiredToCap < 1)
                _teamData[i].NumRequiredToCap = 1;

            if (_teamData[i].NumRequiredToStartCap < 1)
                _teamData[i].NumRequiredToStartCap = 1;
        }

        SetNextThink(AreaThinkTime);
    }

    // ---- touch --------------------------------------------------------------

    /// <summary>
    /// Who is standing in the zone. Stock's <c>CTeamTrainWatcher</c> asks its
    /// bound area this same question — <c>m_hAreaCap-&gt;IsTouching(pPlayer)</c>
    /// (team_train_watcher.cpp:1268) — to decide who has been pushed.
    /// </summary>
    public IReadOnlyCollection<EntityHandle> TouchingEntities => Inside;

    public override void OnTouch(EntityHandle toucher)
    {
        bool wasInside = Inside.Contains(toucher);
        base.OnTouch(toucher);

        if (!wasInside && Inside.Contains(toucher))
            AreaStartTouch(toucher);
    }

    /// <summary>A capture area acts on its occupants in its think; entering fires nothing more.</summary>
    protected override void Fire(EntityHandle toucher) { }

    public override void OnEndTouch(EntityHandle toucher)
    {
        if (Inside.Contains(toucher))
            AreaEndTouch(toucher);

        base.OnEndTouch(toucher);
    }

    /// <summary>StartTouch (L175): the event, then CaptureThink at once so the table counts the newcomer.</summary>
    private void AreaStartTouch(EntityHandle toucher)
    {
        if (ControlPoint is not { } point)
            return;

        _owningTeam = point.Owner;
        events.StartTouch(toucher, point.PointIndex);

        // Call capture think immediately to make it update our area's player counts.
        _startTouch = true;
        CaptureThink();
        _startTouch = false;

        // The newcomer escorts the capture if it is on and the master pays
        // for partial-cap duty (L198-214).
        if (_capturing && entities.Master is { } master)
        {
            float rate = master.PartialCapturePointRate;
            if (rate > 0f && players.IsPlayer(toucher) && players.Team(toucher) == _capturingTeam)
                StartScoringEscortPoints(toucher, rate);
        }
    }

    private void AreaEndTouch(EntityHandle toucher)
    {
        if (ControlPoint is not { } point)
            return;

        events.EndTouch(toucher, point.PointIndex);

        // In case we leave but the area keeps capturing (L233-238): the
        // leaving player stops escorting, whatever team they were on.
        StopScoringEscortPoints(toucher);
    }

    // ---- the capture --------------------------------------------------------

    public override void OnThink() => CaptureThink();

    /// <summary>
    /// CaptureThink (trigger_area_capture.cpp L304-660). Stock's AreaTouch,
    /// which refreshed the owning team every tick a living player stood in
    /// the area, runs here at the top: a brush reports only its edges
    /// (docs/divergences.md).
    /// </summary>
    public void CaptureThink()
    {
        SetNextThink(AreaThinkTime);

        // Escort scoring runs on the players' own think list and answers to
        // neither the round nor the point's cappable state: a player still
        // standing in the area when the capture ends keeps racking up points
        // (L909 and :976 read the capturing team after its reset). Ticked
        // ahead of those early returns for the same reason, and only on the
        // scheduled think: stock's player think keeps its own tenth-second
        // cadence, so the immediate call of StartTouch (m_bStartTouch) ticks
        // the counts, not the escorts.
        if (!_startTouch)
            EscortScoringThink();

        // make sure this point is in the round being played (if we're playing one)
        var master = entities.Master;
        if (master is not null && ControlPoint is { } inRound && !master.IsInRound(inRound))
            return;

        if (!rules.PointsMayBeCaptured)
        {
            // Points aren't allowed to be captured. If we were
            // being captured, we need to clean up and reset.
            if (_capturing)
            {
                BreakCapture(false);
                UpdateNumPlayers();
            }
            return;
        }

        // go through our list of players
        Span<int> numPlayers = stackalloc int[Teams];
        Span<int> numBlockablePlayers = stackalloc int[Teams]; // Players in the zone who can't cap, but can block / pause caps
        var firstPlayerTouching = new EntityHandle[Teams];

        if (ControlPoint is { } point)
        {
            int pointIndex = point.PointIndex;

            // Loop through the entities we're touching, and find players.
            foreach (var entity in Inside)
            {
                if (!players.IsPlayer(entity) || !players.IsAlive(entity))
                    continue;

                // AreaTouch's owner refresh.
                _owningTeam = point.Owner;

                int team = players.Team(entity);
                if ((uint)team >= Teams)
                    continue;

                // If a team's not allowed to cap a point, don't count players in it at all.
                if (!rules.TeamMayCapturePoint(team, pointIndex))
                    continue;

                if (!playerRules.PlayerMayCapturePoint(entity, pointIndex))
                {
                    if (playerRules.PlayerMayBlockPoint(entity, pointIndex))
                    {
                        if (numPlayers[team] == 0 && numBlockablePlayers[team] == 0)
                            firstPlayerTouching[team] = entity;

                        numBlockablePlayers[team] += playerRules.CaptureValueForPlayer(entity);

                        // The blocker touched the objective (L369).
                        objectiveTimes?.SetLastObjectiveTime(entity, Clock.Now);
                    }
                    continue;
                }

                if (team >= FirstGameTeam)
                {
                    if (numPlayers[team] == 0 && numBlockablePlayers[team] == 0)
                        firstPlayerTouching[team] = entity;

                    numPlayers[team] += playerRules.CaptureValueForPlayer(entity);

                    // The capper touched the objective (L382).
                    objectiveTimes?.SetLastObjectiveTime(entity, Clock.Now);
                }
            }
        }

        int teamsInZone = 0;
        bool updatePlayers = false;
        _teamInZone = (int)TFTeam.Unassigned;
        for (int i = FirstGameTeam; i < Teams; i++)
        {
            numPlayers[i] *= settings.SimulateMultipleCappers;

            if (_teamData[i].NumTouching != numPlayers[i])
            {
                _teamData[i].NumTouching = numPlayers[i];
                updatePlayers = true;
            }
            _teamData[i].BlockedTouching = _teamData[i].NumTouching;

            if (_teamData[i].NumTouching != 0)
            {
                teamsInZone++;
                _teamInZone = i;
            }
        }

        if (teamsInZone > 1)
        {
            _teamInZone = (int)TFTeam.Unassigned;
        }
        else
        {
            // If we've got non-cappable, yet blockable players here for the team that's defending, they
            // need to block the cap. This catches cases like the TF invulnerability, which needs to block
            // caps, but isn't allowed to contribute to a cap.
            for (int i = FirstGameTeam; i < Teams; i++)
            {
                if (numBlockablePlayers[i] == 0 || _teamInZone == i)
                    continue;

                teamsInZone++;
            }
        }

        UpdateTeamInZone();

        bool blocked = false;

        // If the cap is being blocked, reset the number of players so the client
        // knows to stop the capture as well.
        if (settings.BlockStyle == 1 && _capturing && teamsInZone > 1)
        {
            blocked = true;

            for (int i = FirstGameTeam; i < Teams; i++)
            {
                numPlayers[i] = 0;
                if (_teamData[i].NumTouching != numPlayers[i])
                {
                    _teamData[i].NumTouching = numPlayers[i];
                    updatePlayers = true;
                }
            }
        }

        if (updatePlayers)
            UpdateNumPlayers(blocked);

        // When a player blocks, tell them the cap index and attempt number
        // only give successive blocks to them if the attempt number is different.
        if (_capturing)
        {
            float now = Clock.Now;

            if (ControlPoint is { } contested)
                contested.LastContestedAt = now;

            // Calculate the amount of modification to the cap time.
            float timeDelta = now - _lastReductionTime;

            float reduction = timeDelta;
            if (CaptureModeScalesWithPlayers)
            {
                // Diminishing returns for successive players.
                for (int i = 1; i < _teamData[_teamInZone].NumTouching; i++)
                    reduction += timeDelta / (float)(i + 1);
            }
            _lastReductionTime = now;

            // if more than one team is in the zone
            if (teamsInZone > 1)
            {
                if (!_blocked)
                {
                    _blocked = true;
                    UpdateBlocked();
                }

                // See if anyone gets credit for the block.
                float percentToGo = _timeRemaining / AreaVars.CapTime;
                if (CaptureModeScalesWithPlayers)
                    percentToGo = _timeRemaining / (AreaVars.CapTime * 2 * _teamData[_capturingTeam].NumRequiredToCap);

                if ((percentToGo <= 0.5f || roundRules.PointsMayAlwaysBeBlocked) && ControlPoint is { } blockedPoint)
                    CreditBlock(blockedPoint, firstPlayerTouching, now);

                if (settings.BlockStyle == 0)
                    BreakCapture(false);
                return;
            }

            if (_blocked)
            {
                _blocked = false;
                UpdateBlocked();
            }

            float totalTimeToCap = AreaVars.CapTime;
            if (CaptureModeScalesWithPlayers)
                totalTimeToCap = AreaVars.CapTime * 2 * _teamData[_capturingTeam].NumRequiredToCap;

            // Now remove the reduction amount after we've determined there's only 1 team in the area.
            if (_capturingTeam == _teamInZone)
            {
                SetCapTimeRemaining(_timeRemaining - reduction);
            }
            else if (_owningTeam == (int)TFTeam.Unassigned && _teamInZone != (int)TFTeam.Unassigned)
            {
                SetCapTimeRemaining(_timeRemaining + reduction);
            }
            else
            {
                // Caps deteriorate over time.
                if (ControlPoint is { } decaying && rules.TeamMayCapturePoint(_capturingTeam, decaying.PointIndex))
                {
                    float decreaseScale = CaptureModeScalesWithPlayers ? settings.CapDeteriorateTime : totalTimeToCap;
                    float decrease = totalTimeToCap / decreaseScale * timeDelta;
                    if (roundRules.InOvertime)
                        decrease *= 6;
                    SetCapTimeRemaining(_timeRemaining + decrease);
                }
                else
                {
                    SetCapTimeRemaining(totalTimeToCap);
                }
            }

            // if the cap is done
            if (_timeRemaining <= 0)
            {
                EndCapture(_capturingTeam);
                return; // we're done
            }

            // We may get several simultaneous CaptureThink calls from StartTouch if there are several
            // players on the trigger when it is enabled. Breaking the capture from the StartTouch call
            // would break it at once; if it should really be broken it happens on the next own think.
            if (!_startTouch && _timeRemaining >= totalTimeToCap)
            {
                BreakCapture(false);
                return;
            }
        }
        else
        {
            // If there are any teams in the zone that aren't the owner, try to start capping.
            if (teamsInZone > 0)
            {
                for (int i = FirstGameTeam; i < Teams; i++)
                {
                    if (!_teamData[i].CanCap || _owningTeam == i)
                        continue;

                    if (_teamData[i].NumTouching == 0)
                        continue;

                    if (_teamData[i].NumTouching < _teamData[i].NumRequiredToStartCap)
                        continue;

                    if (!CaptureModeScalesWithPlayers && _teamData[i].NumTouching < _teamData[i].NumRequiredToCap)
                        continue;

                    StartCapture(i, CaptureNormal);
                    break;
                }
            }
        }
    }

    /// <summary>The first non-capping toucher just broke a capture: credit them, once per attempt (L497-546).</summary>
    private void CreditBlock(TeamControlPoint point, EntityHandle[] firstPlayerTouching, float now)
    {
        // find the first player that is not on the capturing team
        var blocking = EntityHandle.None;
        for (int i = FirstGameTeam; i < Teams; i++)
        {
            if (_capturingTeam == i)
                continue;

            if (firstPlayerTouching[i] != EntityHandle.None)
            {
                blocking = firstPlayerTouching[i];
                break;
            }
        }

        if (blocking == EntityHandle.None)
            return;

        bool repeatBlocker = false;
        for (int i = _blockers.Count - 1; i >= 0; i--)
        {
            if (_blockers[i].Player != blocking)
                continue;

            // If this guy's was a blocker, but not valid now, remove him from the list.
            if (_blockers[i].CapAttemptNumber != _capAttemptNumber || !Inside.Contains(_blockers[i].Player)
                || (roundRules.PointsMayAlwaysBeBlocked && _blockers[i].NextBlockTime < now && _startTouch))
            {
                _blockers.RemoveAt(i);
                continue;
            }

            repeatBlocker = true;
            break;
        }

        if (repeatBlocker)
            return;

        point.CaptureBlocked(blocking, EntityHandle.None);

        // Add this guy to our blocker list.
        _blockers.Add(new Blocker { Player = blocking, CapAttemptNumber = _capAttemptNumber, NextBlockTime = now + 10f });
    }

    /// <summary>SetCapTimeRemaining: the time left, and the table's percentage of it.</summary>
    private void SetCapTimeRemaining(float time)
    {
        _timeRemaining = time;

        float capPercentage = 0f;
        if (_capturingTeam != 0)
        {
            capPercentage = _timeRemaining / AreaVars.CapTime;
            if (CaptureModeScalesWithPlayers)
                capPercentage = _timeRemaining / (AreaVars.CapTime * 2 * _teamData[_capturingTeam].NumRequiredToCap);
        }

        // Stock writes the table through m_hPoint before testing it.
        if (ControlPoint is { } point)
        {
            objectives.SetCPCapPercentage(point.PointIndex, capPercentage);
            point.UpdateCapPercentage();
        }
    }

    /// <summary>SetOwner: break any capture and set the area's owner (resetting all to unassigned).</summary>
    private void SetOwner(int team)
    {
        // break any current capturing
        BreakCapture(false);

        HandleRespawnTimeAdjustments(_owningTeam, team);

        // set the owner to the passed value
        _owningTeam = team;

        UpdateOwningTeam();
    }

    /// <summary>ForceOwner: by the control_point_round to force an owner of this point (so we can play a specific round).</summary>
    public void ForceOwner(int team)
    {
        SetOwner(team);
        ControlPoint?.ForceOwner(team);
    }

    private void HandleRespawnTimeAdjustments(int oldTeam, int newTeam)
    {
        // reverse the adjust made when the old team captured this point (if we made one)
        if (oldTeam > (int)TFTeam.Spectator && _teamData[oldTeam].SpawnAdjust != 0)
            roundRules.AddTeamRespawnWaveTime((TFTeam)oldTeam, -_teamData[oldTeam].SpawnAdjust);

        if (newTeam > (int)TFTeam.Spectator && _teamData[newTeam].SpawnAdjust != 0)
            roundRules.AddTeamRespawnWaveTime((TFTeam)newTeam, _teamData[newTeam].SpawnAdjust);
    }

    private void StartCapture(int team, int capMode)
    {
        // Remap team to get first game team = 1.
        if (team == (int)TFTeam.Red)
            OnStartTeam1.Fire();
        else if (team == (int)TFTeam.Blue)
            OnStartTeam2.Fire();

        OnStartCap.Fire();

        _capturingTeam = team;

        UpdateNumPlayers();

        if (CaptureModeScalesWithPlayers)
            SetCapTimeRemaining(AreaVars.CapTime * 2 * _teamData[team].NumRequiredToCap);
        else
            SetCapTimeRemaining(AreaVars.CapTime);

        _capturing = true;
        _blocked = false;
        _capMode = capMode;

        _lastReductionTime = Clock.Now;

        UpdateCappingTeam(_capturingTeam);
        UpdateBlocked();

        ControlPoint?.CaptureStart(_capturingTeam, GetNumCappingPlayers(_capturingTeam));

        // Tell all touching players to start racking up capture points
        // (L793-815). Stock walks the capturing team's players and asks
        // IsTouching; the touching players of that team are the same set, so
        // this walks the inside list instead.
        if (entities.Master is { } master)
        {
            float rate = master.PartialCapturePointRate;
            if (rate > 0f)
            {
                foreach (var entity in Inside)
                {
                    if (players.IsPlayer(entity) && players.Team(entity) == team)
                        StartScoringEscortPoints(entity, rate);
                }
            }
        }
    }

    /// <summary>
    /// GetNumCappingPlayers: the touching players of the team by player index,
    /// at most MAX_AREA_CAPPERS-1 (disguised spies are on their own team).
    /// </summary>
    private List<int> GetNumCappingPlayers(int team)
    {
        var cappers = Inside.Where(e => players.IsPlayer(e) && players.Team(e) == team)
                            .Select(e => e.EntryIndex)
                            .Order()
                            .Take(MaxAreaCappers - 1)
                            .ToList();
        return cappers;
    }

    private void EndCapture(int team)
    {
        IncrementCapAttemptNumber();

        // Remap team to get first game team = 1.
        if (team == (int)TFTeam.Red)
            OnCapTeam1.Fire();
        else if (team == (int)TFTeam.Blue)
            OnCapTeam2.Fire();

        OnEndCap.Fire();

        var cappers = GetNumCappingPlayers(team);

        // Handle this before we assign the new team as the owner of this area.
        HandleRespawnTimeAdjustments(_owningTeam, team);

        _owningTeam = team;
        _capturing = false;
        _capturingTeam = (int)TFTeam.Unassigned;
        SetCapTimeRemaining(0);

        // play any special cap sounds. need to do this before we update the owner of the point.
        if (ControlPoint is { } point)
        {
            roundRules.PlaySpecialCapSounds((TFTeam)_owningTeam, point);

            UpdateOwningTeam();
            point.SetOwner(_owningTeam, makeSound: true, cappers);
            point.CaptureEnd();
        }

        SetNumCappers(0);

        // Tell all touching players to stop racking up capture points. Stock
        // reads the capturing team after its reset above, so this walks the
        // unassigned team's players and stops nobody who was capping
        // (L908-920): they keep escorting, and thus keep scoring, until the
        // end touch. Ported as stock.
        StopScoringOnTeam(_capturingTeam);
    }

    private void BreakCapture(bool notEnoughPlayers)
    {
        if (!_capturing)
            return;

        // Remap team to get first game team = 1.
        if (_capturingTeam == (int)TFTeam.Red)
            OnBreakTeam1.Fire();
        else if (_capturingTeam == (int)TFTeam.Blue)
            OnBreakTeam2.Fire();

        OnBreakCap.Fire();

        _capturing = false;
        _capturingTeam = (int)TFTeam.Unassigned;

        UpdateCappingTeam((int)TFTeam.Unassigned);

        if (notEnoughPlayers)
            IncrementCapAttemptNumber();

        SetCapTimeRemaining(0);

        if (ControlPoint is { } point)
        {
            point.CaptureEnd();

            // The point reverted to it's previous owner.
            events.CaptureBroken(point.PointIndex, point.PrintName, _timeRemaining);
        }

        SetNumCappers(0);

        // The stop loop again reads the capturing team after its reset, so
        // the players who were escorting the break keep their schedule until
        // the end touch (L975-987). Ported as stock.
        StopScoringOnTeam(_capturingTeam);
    }

    private void IncrementCapAttemptNumber()
    {
        _capAttemptNumber++;
        _blockers.Clear();
    }

    // ---- escort scoring (partial_cap_points_rate) ----------------------------

    /// <summary>ESCORT_SCORE_INTERVAL (basemultiplayerplayer.cpp:300).</summary>
    private const float EscortScoreInterval = 0.1f;

    /// <summary>
    /// <c>CBaseMultiplayerPlayer</c>'s escort fields and its scheduled
    /// <c>EscortScoringThink</c> (<c>m_flCapPointScoreRate</c> /
    /// <c>m_flAreaCaptureScoreAccumulator</c>, basemultiplayerplayer.h:86-87;
    /// the think's context schedule, :333 and :351). Stock keeps them on the
    /// player; <c>CTriggerAreaCapture</c> is their only writer, so they live
    /// here, and the area's own tenth-second think ticks what stock ticks from
    /// the player's think list — same interval. The stock
    /// <c>StopScoringEscortPoints</c> from <c>Spawn</c>
    /// (basemultiplayerplayer.cpp:227), which clears the schedule when the
    /// player respawns, is <see cref="TFEscortSpawnStop"/>'s call.
    /// </summary>
    private sealed class EscortState
    {
        public float Rate;
        public float Accumulator;
        public bool Scheduled;
    }

    private readonly Dictionary<EntityHandle, EscortState> _escort = new();

    /// <summary><c>StartScoringEscortPoints(flRate)</c> (basemultiplayerplayer.cpp:339-344): set the rate, schedule the think. The accumulator is kept, as stock keeps it.</summary>
    private void StartScoringEscortPoints(EntityHandle player, float rate)
    {
        if (!_escort.TryGetValue(player, out var state))
            _escort[player] = state = new EscortState();
        state.Rate = rate;
        state.Scheduled = true;
    }

    /// <summary><c>StopScoringEscortPoints()</c> (:349-352): unschedule; the rate and the accumulator stand. The player's spawn asks it too (<see cref="TFEscortSpawnStop"/>: stock's <c>Spawn</c>, basemultiplayerplayer.cpp:234).</summary>
    public void StopScoringEscortPoints(EntityHandle player)
    {
        if (_escort.TryGetValue(player, out var state))
            state.Scheduled = false;
    }

    /// <summary><c>EscortScoringThink()</c> (:305-334) over every scheduled player.</summary>
    private void EscortScoringThink()
    {
        foreach (var (player, state) in _escort)
        {
            if (!state.Scheduled)
                continue;

            state.Accumulator += EscortScoreInterval;

            if (state.Rate <= 0f)
                continue;

            float timeForOnePoint = 1f / state.Rate;

            int points = 0;
            while (state.Accumulator >= timeForOnePoint)
            {
                state.Accumulator -= timeForOnePoint;
                points++;
            }

            if (points == 0)
                continue;

            // CTFGameRules' player_escort_score handler (tf_gamerules.cpp:18263-18287)
            // over the fired event: the stats arm, which is what the score is
            // made of, lands here (CTFGameStats::Event_PlayerScoresEscortPoints,
            // tf_gamestats.cpp:1482-1485); the currency and heavy achievement
            // arms are deferred (docs/plans/tf2/deferred.tsv).
            stats?.Increment(player, TFStat.Captures, points);

            // The event is fired server-only (FireEvent(event, true), :328).
            if (gameEvents is { } ge)
            {
                IntPtr ev = ge.EventCreate("player_escort_score");
                if (ev != IntPtr.Zero)
                {
                    ge.EventSetInt(ev, "player", player.EntryIndex);
                    ge.EventSetInt(ev, "points", points);
                    ge.EventFire(ev);
                }
            }
        }
    }

    /// <summary>The stop loop of EndCapture and BreakCapture (L908-920, L975-987), walked over the team the stock code reads after its reset.</summary>
    private void StopScoringOnTeam(int team)
    {
        foreach (var entity in Inside)
        {
            if (players.IsPlayer(entity) && players.Team(entity) == team)
                StopScoringEscortPoints(entity);
        }
    }

    // ---- inputs -------------------------------------------------------------

    /// <summary>InputRoundSpawn: find the point we're linked to, and tell the table its cappers and cap times.</summary>
    [ScriptedEvent]
    private void OnRoundSpawn() => FindControlPoint();

    private void FindControlPoint()
    {
        if (ControlPoint is not null)
            return;

        ControlPoint = entities.FindByName<TeamControlPoint>(_capPointName);
        if (ControlPoint is not { } point)
            return;

        _owningTeam = point.Owner;

        for (int i = FirstGameTeam; i < Teams; i++)
        {
            point.SetCappersRequiredForTeam(i, _teamData[i].NumRequiredToCap);

            objectives.SetCPRequiredCappers(point.PointIndex, i, _teamData[i].NumRequiredToCap);
            objectives.SetTeamCanCap(point.PointIndex, i, _teamData[i].CanCap);

            if (CaptureModeScalesWithPlayers)
                objectives.SetCPCapTime(point.PointIndex, i, AreaVars.CapTime * 2 * _teamData[i].NumRequiredToCap);
            else
                objectives.SetCPCapTime(point.PointIndex, i, AreaVars.CapTime);

            objectives.SetCPCapTimeScalesWithPlayers(point.PointIndex, CaptureModeScalesWithPlayers);
        }
    }

    /// <summary>InputSetTeamCanCap: "&lt;team number&gt; &lt;can cap (0/1)&gt;".</summary>
    [ScriptedEvent]
    private void OnSetTeamCanCap(string value)
    {
        string[] parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            int team = ParseInt(parts[0]);
            bool canCap = ParseInt(parts[1]) != 0;

            if (team >= 0 && team < Teams)
            {
                _teamData[team].CanCap = canCap;
                if (ControlPoint is { } point)
                    objectives.SetTeamCanCap(point.PointIndex, team, canCap);
                return;
            }
        }

        Log.Warning($"{MapClass}({Name}) received SetTeamCanCap input with invalid format. Format should be: <team number> <can cap (0/1)>.\n");
    }

    [ScriptedEvent]
    private void OnCaptureCurrentCP()
    {
        if (_capturing)
            EndCapture(_capturingTeam);
    }

    [ScriptedEvent]
    private void OnSetControlPoint(string name)
    {
        BreakCapture(false); // clear the capping for the previous point, forces us to recalc on the new one

        _capPointName = name ?? string.Empty;
        ControlPoint = null; // force a reset of this
        FindControlPoint();

        // force everyone touching to re-touch so the hud gets set up properly
        foreach (var entity in Inside.ToList())
        {
            if (!players.IsPlayer(entity))
                continue;
            AreaEndTouch(entity);
            AreaStartTouch(entity);
        }
    }

    /// <summary>
    /// CheckIfDeathCausesBlock (L1107): false when the victim is not on this
    /// area, else true, crediting the killer with a block when the death
    /// leaves the capture short. The round flow's death handling calls it.
    /// </summary>
    public bool CheckIfDeathCausesBlock(EntityHandle victim, EntityHandle killer)
    {
        if (victim == EntityHandle.None || killer == EntityHandle.None)
            return false;

        // make sure this player is in this area
        if (!Inside.Contains(victim))
            return false;

        // Teamkills shouldn't give a block reward
        if (players.Team(victim) == players.Team(killer))
            return true;

        // return if the area is not being capped
        if (!_capturing)
            return true;

        // return if this player's team is not capping the area
        int team = players.Team(victim);
        if (team != _capturingTeam)
            return true;

        // break early incase we kill multiple people in the same frame
        bool breakCap = CaptureModeScalesWithPlayers
            ? _teamData[_capturingTeam].BlockedTouching - 1 <= 0
            : _teamData[_capturingTeam].BlockedTouching - 1 < _teamData[_capturingTeam].NumRequiredToCap;

        // For TF2's contracts, fire a special event when killing anyone on the cap.
        events.CapperKilled(killer.EntryIndex, victim.EntryIndex);

        if (breakCap)
            ControlPoint?.CaptureBlocked(killer, victim);

        return true;
    }

    // ---- the table ----------------------------------------------------------

    private void UpdateNumPlayers(bool blocked = false)
    {
        if (ControlPoint is not { } point)
            return;

        int index = point.PointIndex;
        for (int i = 0; i < Teams; i++)
        {
            if (i >= FirstGameTeam && i == _capturingTeam)
                SetNumCappers(_teamData[i].NumTouching, blocked);

            objectives.SetNumPlayers(index, i, _teamData[i].NumTouching);
        }
    }

    private void UpdateOwningTeam()
    {
        if (ControlPoint is { } point)
            objectives.SetOwningTeam(point.PointIndex, _owningTeam);
    }

    private void UpdateCappingTeam(int team)
    {
        if (ControlPoint is { } point)
            objectives.SetCappingTeam(point.PointIndex, team);
    }

    private void UpdateTeamInZone()
    {
        if (ControlPoint is { } point)
            objectives.SetTeamInZone(point.PointIndex, _teamInZone);
    }

    private void UpdateBlocked()
    {
        if (ControlPoint is { } point)
        {
            objectives.SetCapBlocked(point.PointIndex, _blocked);
            point.CaptureInterrupted(_blocked);
        }
    }

    private void SetNumCappers(int numCappers, bool blocked = false)
    {
        OnNumCappersChanged.Fire(numCappers.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // m_OnNumCappersChanged2 sets -1 for a blocked cart (for movement decisions on hills)
        if (blocked)
            numCappers = -1;

        OnNumCappersChanged2.Fire(numCappers.ToString(System.Globalization.CultureInfo.InvariantCulture));

        TrainWatcher?.SetNumTrainCappers(numCappers, this);
    }
}

/// <summary>The map keys <see cref="TriggerAreaCapture"/> declares (trigger_area_capture.cpp:32-33).</summary>
[ScriptVars]
public partial class TriggerAreaCaptureVars
{
    [VarName("area_cap_point")] public string CapPointName { get; set; } = string.Empty;
    [VarName("area_time_to_cap")] public float CapTime { get; set; }
}
