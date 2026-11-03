//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Server.Voice;
using TF2.Shared;
using TF2.Shared.Resources;
using TF2.Shared.Rules;

namespace TF2.Server.Entities;

/// <summary>
/// The payload lane's ordered hooks (Stage 2.6's band, +600..+699 of TF2's
/// thousand): every ordered hook of this lane takes one of these, so none ties
/// another lane's.
/// </summary>
public static class TFPayloadOrders
{
    private const int Band = 1_000_000 - 1_000 + 600;

    /// <summary>The train watchers' timer-expiry veto (<see cref="TrainWatcherTimerVeto"/>).</summary>
    public const int TimerVeto = Band + 0;
}

/// <summary>
/// <c>CTeamplayRoundBasedRules::TimerMayExpire</c>'s train-watcher walk
/// (teamplayroundbased_gamerules.cpp): the round timer may not expire while
/// any <see cref="TeamTrainWatcher"/> says so — a cart waiting to recede, or a
/// blocked one, holds the round open.
/// </summary>
// A chain member: the registrar (TeamRoundRegistrar) names the
// ITimerExpiryVeto chain and the machine collects its members through
// ResolveAll, so this claims only its own type.
[SideService(Side.Server)]
public sealed class TrainWatcherTimerVeto(IEntityDirectory directory) : ITimerExpiryVeto, IOrderedHook
{
    public int HookOrder => TFPayloadOrders.TimerVeto;

    public bool TimerMayExpire()
    {
        foreach (var watcher in directory.OfClass(TeamTrainWatcher.MapClass).OfType<TeamTrainWatcher>())
        {
            if (!watcher.TimerMayExpire())
                return false;
        }

        return true;
    }
}

/// <summary>
/// team_train_watcher: the entity that watches a payload cart along its
/// path_tracks and tells everyone how far it has got (stock's
/// <c>CTeamTrainWatcher</c>, team_train_watcher.cpp). It computes the track's
/// total length and each checkpoint's place on it at round activate, then on a
/// tenth-second think reports the progress, the speed level and the alarm state
/// to the objective table, arms the recede once nobody is capping, and — when
/// the map gives it <c>handle_train_movement</c> — drives the train itself.
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class TeamTrainWatcher(
    IGameClock clock,
    IObjectiveEntities entities,
    IModeObjectives mode,
    IObjectiveRoundRules roundRules,
    ITeamRoundState round,
    ITeamRoundControl control,
    IEngineEntities engine,
    IEngineSound sound,
    IEnginePrecache precache,
    ISoundPatchRamps soundRamps,
    IEntityDirectory directory,
    IEnginePlayers players,
    IEngineGameEvents gameEvents,
    ConVarRegistry convars,
    TFConceptSpeech? speech = null) : ScriptedEntity, ICaptureAreaWatcher, IPathTrackPassListener
{
    public const string MapClass = "team_train_watcher";

    /// <summary>TEAM_TRAIN_ALERT_DISTANCE: the announcer's warning distance.</summary>
    public const float AlertDistance = 750f;

    /// <summary>TEAM_TRAIN_ALARM_DISTANCE: the looping alarm distance.</summary>
    public const float AlarmDistance = 200f;

    /// <summary>MAX_ALARM_TIME_NO_RECEDE, for a cart that will not recede.</summary>
    public const float MaxAlarmTimeNoRecede = 18f;

    /// <summary>TW_ALARM_THINK_INTERVAL: the single-shot alarm's re-arming.</summary>
    public const float AlarmThinkInterval = 8f;

    /// <summary>TEAM_TRAIN_ALARM.</summary>
    public const string AlarmSound = "Cart.Warning";

    /// <summary>TEAM_TRAIN_ALARM_SINGLE.</summary>
    public const string AlarmSingleSound = "Cart.WarningSingle";

    /// <summary>TW_ALARM_THINK: the named context the alarm think books into.</summary>
    public const string AlarmThinkContext = "CTeamTrainWatcherAlarmThink";

    /// <summary>HILL_TYPE_NONE.</summary>
    public const int HillTypeNone = 0;

    /// <summary>HILL_TYPE_UPHILL.</summary>
    public const int HillTypeUpHill = 1;

    /// <summary>HILL_TYPE_DOWNHILL.</summary>
    public const int HillTypeDownHill = 2;

    /// <summary>nMinProgressToReport: 16 hammer units per foot, before stock fires payload_pushed.</summary>
    public const int MinProgressToReport = 16;

    /// <summary>The speed each capper count asks for (HandleTrainMovement's switch).</summary>
    public const float SpeedOneCapper = 0.55f;

    public const float SpeedTwoCappers = 0.77f;

    public const float SpeedThreeCappers = 1.0f;

    /// <summary>The recede speed stock hands <c>SetSpeedDirAccel</c>.</summary>
    public const float RecedeSpeed = -0.1f;

    private const int ChanStatic = 6;

    /// <summary>ATTN_NORM, as the sound level the patch is created at.</summary>
    private const int SndLvlNorm = 75;

    /// <summary>PITCH_NORM.</summary>
    private const float PitchNorm = 100f;

    /// <summary>TW_THINK's interval.</summary>
    private const float ThinkInterval = 0.1f;

    /// <summary>The escort line's repeat, stock's literal (team_train_watcher.cpp:1195).</summary>
    public const float ForwardConceptInterval = 3f;

    public readonly Output OnTrainStartRecede = new();

    /// <summary>What a map may set on this entity. See <see cref="TeamTrainWatcherVars"/>.</summary>
    [ScriptVars] public TeamTrainWatcherVars Vars = new();

    /// <summary>node_cp_pair_t.</summary>
    private sealed class NodeCPLink
    {
        public PathTrack? Node;
        public TeamControlPoint? Point;
        public float DistanceFromStart;
        public bool AlertPlayed;
    }

    private readonly NodeCPLink[] _cpLinks = new NodeCPLink[ObjectiveLimits.MaxControlPoints];
    private int _numCPLinks;

    private readonly float[] _speedLevels = new float[3];

    private FuncTrackTrain? _train;
    private PathTrack? _startNode;
    private PathTrack? _goalNode;

    private TriggerAreaCapture? _areaCap;

    // The nodes this watcher subscribed to for passes — stock hears them as
    // path_track_passed game events. The list is what unsubscribes them when
    // the watcher (which no round preserves) goes away.
    private readonly List<PathTrack> _subscribed = [];

    // m_Sparks: the env_sparks named by spark_name, gathered at activation and
    // burned while the cart runs backwards (HandleSparks, team_train_watcher.cpp:498).
    private readonly List<EnvSpark> _sparks = [];

    private bool _disabled;
    private bool _trainCanRecede = true;
    private bool _capBlocked;
    private bool _waitingToRecede;
    private bool _alarmPlayed;
    private bool _receding;
    private bool _handleTrainMovement;

    private float _totalPathDistance;
    private float _trainDistanceAccumulator;
    private float _trainDistanceFromStart;
    private float _totalProgress;
    private float _prevThinkProgress;
    private float _recedeTime;
    private float _recedeTotalTime;
    private float _recedeStartTime;
    private float _currentSpeed;
    private float _speedForwardModifier = 1f;
    private float _alarmEndTime = -1f;
    private int _alarmPatch;
    private int _trainSpeedLevel;
    private float _nextSpeakForwardConceptTime;

    /// <summary>The response rules the escort's lines are asked through.</summary>
    private readonly TFConceptSpeech? Speech = speech;

    /// <summary>m_iTrainSpeedLevel: which HUD speed pip the train's speed is (−1 stalled, 1..3).</summary>
    public int TrainSpeedLevel => _trainSpeedLevel;

    /// <summary>Whether the CP link's capture warning has played (stock's bAlertPlayed, tests read it).</summary>
    public bool AlertPlayed(int index) => _cpLinks[index].AlertPlayed;
    private int _numCappers;
    private int _currentHillType = HillTypeNone;
    private int _trainRecedeTime;

    private int Team => Vars.TeamNum;

    /// <summary>Stock reads these live; a missing declaration is a startup bug.</summary>
    private T ConVar<T>() where T : ConVarBase
        => convars.Get<T>() ?? throw new InvalidOperationException(
            $"team_train_watcher reads {typeof(T).Name}, which is not registered");

    private float EscortRecedeTime => ConVar<TFEscortRecedeTimeConVar>().Value;

    private float EscortRecedeTimeOvertime => ConVar<TFEscortRecedeTimeOvertimeConVar>().Value;

    /// <summary>Whether the alarm's patch exists.</summary>
    private bool HasAlarm => _alarmPatch != 0;

    public bool IsDisabled => _disabled;

    /// <summary>The capture area the train's cart sits in, as the last capper report named it.</summary>
    public TriggerAreaCapture? AreaCap => _areaCap;

    /// <summary>Stock's m_flTrainDistanceAccumulator, readable for the pushed-accumulator facts.</summary>
    public float GetPushedDistanceAccumulator() => _trainDistanceAccumulator;

    /// <summary>Stock's m_flRecedeTotalTime, readable for the recede-window facts.</summary>
    public float GetRecedeTotalTime() => _recedeTotalTime;

    /// <summary><c>GetTrainProgress</c>: the 0..1 progress the last think computed.</summary>
    public float GetTrainProgress() => _totalProgress;

    /// <summary><c>GetCapturerCount</c>: the cappers, or -1 while the cart is blocked.</summary>
    public int GetCapturerCount() => _numCappers;

    /// <summary><c>GetTrainDistanceAlongTrack</c>: the train's distance from the start node.</summary>
    public float GetTrainDistanceAlongTrack() => _trainDistanceFromStart;

    /// <summary><c>GetTrainEntity</c>.</summary>
    public FuncTrackTrain? GetTrainEntity() => _train;

    // ---- the map's keys -----------------------------------------------------

    public override void OnSpawn()
    {
        _disabled = Vars.StartDisabled;
        _trainCanRecede = Vars.TrainCanRecede;
        _handleTrainMovement = Vars.HandleTrainMovement;
        _trainRecedeTime = Vars.TrainRecedeTime;
        _speedLevels[0] = Vars.HudMinSpeedLevel1;
        _speedLevels[1] = Vars.HudMinSpeedLevel2;
        _speedLevels[2] = Vars.HudMinSpeedLevel3;

        precache.PrecacheSound(AlarmSound);
        precache.PrecacheSound(AlarmSingleSound);
    }

    public override void OnRemoved()
    {
        StopCaptureAlarm();
        UnsubscribeNodes();
    }

    // ---- inputs -------------------------------------------------------------

    /// <summary>InputRoundActivate: re-arm for the round.</summary>
    [ScriptedEvent]
    private void OnRoundActivate()
    {
        StopCaptureAlarm();

        if (!_disabled)
            WatcherActivate();
    }

    [ScriptedEvent]
    private void OnEnable()
    {
        StopCaptureAlarm();

        _disabled = false;

        WatcherActivate();
    }

    [ScriptedEvent]
    private void OnDisable()
    {
        StopCaptureAlarm();

        _disabled = true;
        StopThinking();

        _waitingToRecede = false;

        // m_Sparks.Purge(), stock :390
        _sparks.Clear();

        // if we're moving the train, let's shut it down
        if (_handleTrainMovement)
        {
            _currentSpeed = 0f;

            if (_train is not null)
                _train.SetSpeedDirAccel(_currentSpeed);

            // HandleSparks(false), stock :407 — and stock's guard reads the
            // m_bDisabled set three lines up (:385/:500), so this call does
            // NOTHING: disabling a watcher with the sparks burning leaves
            // them burning. The purge above only drops the handles; it is
            // not a stop either. Ported as stock.
            HandleSparks(false);
        }
    }

    /// <summary>InputSetNumTrainCappers: the map's own report of a capper count.</summary>
    [ScriptedEvent]
    private void OnSetNumTrainCappers(InputData data)
    {
        // Stock takes inputdata.pCaller and dynamic_casts it to the capture
        // area; the activator here is the same entity.
        var area = directory.Get<TriggerAreaCapture>(data.Activator);
        InternalSetNumTrainCappers((int)data.Number, area);
    }

    /// <summary>InputOnStartOvertime: drop a running recede to the overtime length.</summary>
    [ScriptedEvent]
    private void OnStartOvertime()
    {
        if (!_waitingToRecede)
            return;

        float recedeTimeRemaining = _recedeTime - clock.Now;
        float overtimeRecedeLen = EscortRecedeTimeOvertime;

        // drop to overtime recede time if it's more than that
        if (recedeTimeRemaining > overtimeRecedeLen)
        {
            _recedeTotalTime = overtimeRecedeLen;
            _recedeStartTime = clock.Now;
            _recedeTime = _recedeStartTime + _recedeTotalTime;
        }
    }

    [ScriptedEvent]
    private void OnSetSpeedForwardModifier(float value) => InternalSetSpeedForwardModifier(value);

    [ScriptedEvent]
    private void OnSetTrainRecedeTime(float value)
    {
        int seconds = (int)value;
        if (seconds >= 0)
            _trainRecedeTime = seconds;
        else
            _trainRecedeTime = 0;
    }

    [ScriptedEvent]
    private void OnSetTrainCanRecede(float value) => _trainCanRecede = value != 0f;

    [ScriptedEvent]
    private void OnSetTrainRecedeTimeAndUpdate(float value)
    {
        OnSetTrainRecedeTime(value);

        // update our time if we're already counting down
        if (_recedeTime > 0)
        {
            _recedeTotalTime = EscortRecedeTime;
            if (_trainRecedeTime > 0)
                _recedeTotalTime = _trainRecedeTime;

            _recedeStartTime = clock.Now;
            _recedeTime = _recedeStartTime + _recedeTotalTime;
        }
    }

    // ---- the capture area's reports (only train-movement watchers take them) --

    /// <summary>Stock's public SetNumTrainCappers: only a watcher that drives the train listens.</summary>
    public void SetNumTrainCappers(int cappers, TriggerAreaCapture area)
    {
        if (_disabled || !_handleTrainMovement)
            return;

        InternalSetNumTrainCappers(cappers, area);
    }

    private void InternalSetNumTrainCappers(int cappers, TriggerAreaCapture? area)
    {
        if (_disabled)
            return;

        _numCappers = cappers;

        // inputdata.pCaller is hopefully an area capture
        // lets see if its blocked, and not start receding if it is
        if (area is not null)
        {
            _capBlocked = area.IsBlocked;
            _areaCap = area;
        }

        if (cappers <= 0 && !_capBlocked && _trainCanRecede)
        {
            if (!_waitingToRecede)
            {
                // start receding in [tf_escort_recede_time] seconds
                _waitingToRecede = true;

                if (roundRules.InOvertime)
                {
                    _recedeTotalTime = EscortRecedeTimeOvertime;
                }
                else
                {
                    _recedeTotalTime = EscortRecedeTime;
                    if (_trainRecedeTime > 0)
                        _recedeTotalTime = _trainRecedeTime;
                }

                _recedeStartTime = clock.Now;
                _recedeTime = _recedeStartTime + _recedeTotalTime;
            }
        }
        else
        {
            // cancel receding
            _waitingToRecede = false;
            _recedeTime = 0;
        }

        HandleTrainMovement();
    }

    private void InternalSetSpeedForwardModifier(float modifier)
    {
        if (_disabled || !_handleTrainMovement)
            return;

        // store the passed value
        float speedForwardModifier = MathF.Abs(modifier);

        _speedForwardModifier = Math.Clamp(speedForwardModifier, 0f, 1f);

        if (_train is not null)
            _train.SetSpeedForwardModifier(_speedForwardModifier);
    }

    /// <summary>
    /// The path_track_passed stand-in: a node on this track was passed, and the
    /// hill state may have changed (stock's FireGameEvent). A node passed while
    /// receding whose previous node is disabled is skipped — the train will
    /// never pass it again going forward, so it must not set the hill state.
    /// </summary>
    void IPathTrackPassListener.OnPathTrackPassed(PathTrack node) => OnNodePassed(node);

    private void OnNodePassed(PathTrack node)
    {
        if (_disabled || !_handleTrainMovement)
            return;

        if (!_subscribed.Contains(node))
            return;

        if (_receding && node.GetPrevious()?.IsDisabled == true)
            return;

        int hillType = node.HillType;
        bool update = _currentHillType != hillType;

        if (!update && _currentHillType != HillTypeNone)
        {
            // the hill settings are the same, but are we leaving an uphill or downhill segment?
            PathTrack? nextNode = _currentSpeed < 0 ? node.GetPrevious() : node.GetNext();
            if (nextNode is not null && _currentHillType != nextNode.HillType)
            {
                // we're leaving an uphill or downhill segment...so reset our state until we pass the next node
                update = true;
                hillType = HillTypeNone;
            }
        }

        if (update)
        {
            _currentHillType = hillType;
            HandleTrainMovement();
        }
    }

    private void HandleTrainMovement(bool startReceding = false)
    {
        if (_disabled || !_handleTrainMovement)
            return;

        if (_train is null)
            return;

        float speed = 0f;

        if (startReceding)
        {
            speed = RecedeSpeed;
            _receding = true;
        }
        else
        {
            // do we have cappers on the train?
            if (_numCappers > 0)
            {
                _receding = false;

                if (_currentHillType == HillTypeDownHill)
                {
                    speed = SpeedThreeCappers;
                }
                else
                {
                    speed = _numCappers switch
                    {
                        1 => SpeedOneCapper,
                        2 => SpeedTwoCappers,
                        _ => SpeedThreeCappers,
                    };
                }
            }
            else if (_numCappers == -1)
            {
                // we'll get a -1 for a blocked cart (speed should be 0 for that unless we're on a hill)
                if (_currentHillType == HillTypeDownHill)
                    speed = SpeedThreeCappers;
            }
            else
            {
                // there's nobody on the train, what should it be doing?
                if (_currentSpeed > 0)
                {
                    if (_currentHillType == HillTypeDownHill)
                        speed = SpeedThreeCappers;
                }
                else
                {
                    // we're rolling backwards
                    if (_currentHillType == HillTypeUpHill)
                    {
                        speed = -SpeedThreeCappers;
                    }
                    else if (_receding)
                    {
                        // resume our previous backup speed
                        speed = RecedeSpeed;
                    }
                }
            }
        }

        // only need to update the train if our speed has changed
        if (_currentSpeed != speed)
        {
            if (speed >= 0f)
                _receding = false;

            _currentSpeed = speed;
            _train.SetSpeedDirAccel(_currentSpeed);

            // handle the sparks under the train: they burn while the cart
            // runs backwards (team_train_watcher.cpp:610-617)
            HandleSparks(_currentSpeed < 0f);
        }
    }

    /// <summary>
    /// <c>CTeamTrainWatcher::HandleSparks</c> (team_train_watcher.cpp:498-518):
    /// put the named env_sparks into the asked-for state, touching only the
    /// ones not already there. A disabled or non-driving watcher says nothing.
    /// </summary>
    private void HandleSparks(bool sparks)
    {
        if (_disabled || !_handleTrainMovement)
            return;

        foreach (var spark in _sparks)
        {
            if (spark.IsSparking() != sparks)
            {
                if (sparks)
                    spark.StartSpark();
                else
                    spark.StopSpark();
            }
        }
    }

    // ==========================================================
    // given a start node and a list of goal nodes
    // calculate the distance between each
    // ==========================================================
    public void WatcherActivate()
    {
        _recedeTime = 0;
        _waitingToRecede = false;
        _capBlocked = false;
        _areaCap = null;
        _trainDistanceFromStart = 0f;

        _alarmPlayed = false;

        // m_Sparks.Purge(), stock :837 — an activation forgets last round's sparks
        _sparks.Clear();

        UnsubscribeNodes();
        StopCaptureAlarm();

        // init our train
        _train = entities.FindByName<FuncTrackTrain>(Vars.Train);
        if (_train is null)
            Log.Warning($"{MapClass} failed to find train named '{Vars.Train}'\n");

        // find the trigger area that will give us movement updates and find the
        // sparks (if we're going to handle the train movement)
        if (_handleTrainMovement)
        {
            if (_train is not null)
            {
                foreach (var area in entities.Areas)
                {
                    if (engine.GetParent(area.Handle) == _train.Handle)
                    {
                        // this is the capture area we care about, so let it know that we want updates on the capture numbers
                        area.TrainWatcher = this;
                        break;
                    }
                }
            }

            // Stock finds every entity of the name and keeps the sparks
            // (:866-871); the empty spark_name keeps stock's quirk of matching
            // the UNNAMED sparks, since an unnamed entity has exactly that name.
            foreach (var spark in directory.OfClass(EntityNames.EnvSpark).OfType<EnvSpark>())
            {
                if (spark.Name == Vars.SparkName)
                    _sparks.Add(spark);
            }
        }

        // init our array of path_tracks linked to control points
        _numCPLinks = 0;

        for (int i = 0; i < ObjectiveLimits.MaxControlPoints; i++)
        {
            var pathTrack = entities.FindByName<PathTrack>(Vars.LinkedPathTrack(i));
            var cp = entities.FindByName<TeamControlPoint>(Vars.LinkedCP(i));
            if (pathTrack is not null && cp is not null)
            {
                _cpLinks[_numCPLinks] = new NodeCPLink
                {
                    Node = pathTrack,
                    Point = cp,
                    DistanceFromStart = 0f, // filled in when we parse the nodes
                    AlertPlayed = false,
                };
                _numCPLinks++;
            }
        }

        // init our start and goal nodes
        _startNode = entities.FindByName<PathTrack>(Vars.StartNode);
        if (_startNode is null)
            Log.Warning($"{MapClass} failed to find path_track named '{Vars.StartNode}'\n");

        _goalNode = entities.FindByName<PathTrack>(Vars.GoalNode);
        if (_goalNode is null)
            Log.Warning($"{MapClass} failed to find path_track named '{Vars.GoalNode}'\n");

        _totalPathDistance = 0f;

        List<float> hillData = [];
        bool onHill = false;
        Span<bool> downHillData = stackalloc bool[ObjectiveLimits.TrainMaxHills];
        int hillCount = 0;

        if (_startNode is not null && _goalNode is not null)
        {
            PathTrack? pNode = _startNode.GetNext();
            PathTrack pPrev = _startNode;
            PathTrack? pHillStart = null;
            int hillType = HillTypeNone;

            // don't check the start node for links. If it's linked, it will have 0 distance anyway
            while (pNode is not null)
            {
                Vector dir = pNode.Position - pPrev.Position;
                float length = dir.Length;

                _totalPathDistance += length;

                // gather our hill data for the HUD
                if (pNode.HillType != hillType)
                {
                    if (!onHill) // we're at the start of a hill
                    {
                        hillData.Add(_totalPathDistance);
                        onHill = true;
                        pHillStart = pNode;

                        if (hillCount < ObjectiveLimits.TrainMaxHills)
                        {
                            downHillData[hillCount] = pNode.HillType == HillTypeDownHill;
                            hillCount++;
                        }
                    }
                    else // we're at the end of a hill
                    {
                        float distance = _totalPathDistance - length; // subtract length because the prev node was the end of the hill (not this one)

                        if (pHillStart is not null && pHillStart == pPrev)
                        {
                            distance = _totalPathDistance; // we had a single node marked as a hill, so we'll use the current distance as the next marker
                        }

                        hillData.Add(distance);

                        // is our current node the start of another hill?
                        if (pNode.HillType != HillTypeNone)
                        {
                            hillData.Add(_totalPathDistance);
                            onHill = true;
                            pHillStart = pNode;

                            if (hillCount < ObjectiveLimits.TrainMaxHills)
                            {
                                downHillData[hillCount] = pNode.HillType == HillTypeDownHill;
                                hillCount++;
                            }
                        }
                        else
                        {
                            onHill = false;
                            pHillStart = null;
                        }
                    }

                    hillType = pNode.HillType;
                }

                // if pNode is one of our cp nodes, store its distance from m_hStartNode
                for (int i = 0; i < _numCPLinks; i++)
                {
                    if (_cpLinks[i].Node == pNode)
                    {
                        _cpLinks[i].DistanceFromStart = _totalPathDistance;
                        break;
                    }
                }

                if (pNode == _goalNode)
                    break;

                PathTrack? next = pNode.GetNext();
                if (next is null || next == pNode)
                    break;

                pPrev = pNode;
                pNode = next;
            }
        }

        // if we don't have an even number of entries in our hill data (beginning/end) add the final distance
        if (hillData.Count % 2 != 0)
            hillData.Add(_totalPathDistance);

        mode.ResetHillData(Team);

        // convert our hill data into 0-1 percentages for networking
        if (_totalPathDistance > 0 && hillData.Count > 0)
        {
            int i = 0;
            while (i < hillData.Count)
            {
                if (i < ObjectiveLimits.TrainMaxHills * ObjectiveLimits.TrainFloatsPerHill - 1) // - 1 because we want to use 2 entries
                {
                    // add/subtract to the hill start/end to fix rounding errors in the HUD when the train
                    // stops at the bottom/top of a hill but the HUD thinks the train is still on the hill
                    mode.SetHillData(Team, (hillData[i] / _totalPathDistance) + 0.005f,
                                     (hillData[i + 1] / _totalPathDistance) - 0.005f,
                                     downHillData[i / 2]);
                }

                i += 2;
            }
        }

        // We have total distance and increments in our links array
        for (int i = 0; i < _numCPLinks; i++)
        {
            int cpIndex = _cpLinks[i].Point!.PointIndex;
            mode.SetTrainPathDistance(cpIndex, _cpLinks[i].DistanceFromStart / _totalPathDistance);
        }

        SubscribeNodes();

        InternalSetSpeedForwardModifier(Vars.SpeedForwardModifier);

        SetNextThink(ThinkInterval);
    }

    /// <summary>
    /// The pass listener (stock listens for path_track_passed): every node from
    /// the start of the track to the goal is subscribed. The visited set bounds
    /// the walk, where stock's own membership walk relies on the goal ending it.
    /// </summary>
    private void SubscribeNodes()
    {
        HashSet<PathTrack> visited = [];
        PathTrack? node = _startNode;

        while (node is not null && visited.Add(node))
        {
            node.AddPassListener(this);
            _subscribed.Add(node);
            node = node.GetNext();
        }
    }

    private void UnsubscribeNodes()
    {
        foreach (var node in _subscribed)
            node.RemovePassListener(this);

        _subscribed.Clear();
    }

    private void StopCaptureAlarm()
    {
        if (HasAlarm)
        {
            soundRamps.Destroy(_alarmPatch);
            sound.SoundPatchFree(_alarmPatch);
            _alarmPatch = 0;
            _alarmEndTime = -1f;
        }

        StopThinking(AlarmThinkContext);
    }

    private void StartCaptureAlarm(TeamControlPoint? point)
    {
        StopCaptureAlarm();

        if (point is null)
            return;

        _alarmPatch = sound.SoundPatchCreate(point.Handle, ChanStatic, AlarmSound, SndLvlNorm);
        if (_alarmPatch != 0)
            soundRamps.Play(_alarmPatch, 1f, PitchNorm, 0f);

        _alarmEndTime = clock.Now + MaxAlarmTimeNoRecede;
    }

    public override void OnThink() => WatcherThink();

    public override void OnThink(string context)
    {
        if (context == AlarmThinkContext)
            WatcherAlarmThink();
    }

    public void WatcherThink()
    {
        if (_waitingToRecede)
        {
            if (_recedeTime < clock.Now)
            {
                _waitingToRecede = false;

                // don't actually recede in overtime
                if (!roundRules.InOvertime)
                {
                    // fire recede output
                    OnTrainStartRecede.Fire(Handle);
                    HandleTrainMovement(true);
                }
            }
        }

        bool disableAlarm = round.State != RoundState.RoundRunning;
        if (disableAlarm)
            StopCaptureAlarm();

        // given its next node, we can walk the nodes and find the linear
        // distance to the next cp node, or to the goal node

        if (_train is not null)
        {
            int oldTrainSpeedLevel = _trainSpeedLevel;   // stock's iOldTrainSpeedLevel, :1114

            // how fast is the train moving? The managed train holds one signed
            // speed — the desired one — so stock's dead-end check of the
            // ACTUAL speed against zero is folded into it (see divergences).
            float speed = _train.CurrentSpeed;

            // divide speed into regions
            // anything negative is -1
            if (speed < 0)
            {
                _trainSpeedLevel = -1;
            }
            else if (speed > _speedLevels[2])
            {
                _trainSpeedLevel = 3;
            }
            else if (speed > _speedLevels[1])
            {
                _trainSpeedLevel = 2;
            }
            else if (speed > _speedLevels[0])
            {
                _trainSpeedLevel = 1;
            }
            else
            {
                _trainSpeedLevel = 0;
            }

            if (_trainSpeedLevel != oldTrainSpeedLevel)
            {
                // make sure the sparks are off if we're not moving backwards
                // anymore (team_train_watcher.cpp:1151-1160)
                if (_handleTrainMovement && _trainSpeedLevel == 0 && oldTrainSpeedLevel != 0)
                    HandleSparks(false);

                // play any concepts that we might need to play
                // (team_train_watcher.cpp:1162-1175)
                if (_trainSpeedLevel == 0 && oldTrainSpeedLevel != 0)
                {
                    Speech?.SpeakAll(TFConceptSpeech.CartStop);
                    _nextSpeakForwardConceptTime = 0;
                }
                else if (_trainSpeedLevel < 0 && oldTrainSpeedLevel == 0)
                {
                    Speech?.SpeakAll(TFConceptSpeech.CartMovingBackward);
                    _nextSpeakForwardConceptTime = 0;
                }
            }

            // the escort line, under the push, to whoever stands in the capture
            // area, at most once every three seconds — the timer runs whether
            // or not there is an area to ask (team_train_watcher.cpp:1178-1196)
            if (_trainSpeedLevel > 0 && _nextSpeakForwardConceptTime < clock.Now)
            {
                if (_areaCap is { } area)
                {
                    foreach (var entity in area.TouchingEntities)
                    {
                        if (engine.IsPlayer(entity))
                            Speech?.Speak(entity, TFConceptSpeech.CartMovingForward);
                    }
                }

                _nextSpeakForwardConceptTime = clock.Now + ForwardConceptInterval;
            }

            // what percent progress are we at? Stock's node is the train's
            // m_ppath->GetNext() — the end of the leg the cart is on — and
            // m_ppath itself only when moving backwards.
            PathTrack? pNode = speed < 0
                ? _train.CurrentNode
                : (_train.TargetNode ?? _train.CurrentNode?.GetNext());

            if (pNode is not null)
            {
                float distanceToGoal;

                // distance to next node
                Vector vecDir = pNode.Position - engine.GetOrigin(_train.Handle);
                distanceToGoal = vecDir.Length;

                // distance of next node to goal node
                if (pNode != _goalNode)
                {
                    // walk this until we get to goal node, or a dead end
                    PathTrack pPrev = pNode;
                    pNode = pNode.GetNext();
                    while (pNode is not null)
                    {
                        vecDir = pNode.Position - pPrev.Position;
                        distanceToGoal += vecDir.Length;

                        if (pNode == _goalNode)
                            break;

                        pPrev = pNode;
                        pNode = pNode.GetNext();
                    }
                }

                if (_totalPathDistance <= 0)
                {
                    Log.Warning("[TF2] No path distance in team_train_watcher\n");
                    _totalPathDistance = 1;
                }

                _totalProgress = Math.Clamp(1.0f - (distanceToGoal / _totalPathDistance), 0f, 1f);

                // The race shape: the first cart to reach its goal wins that team — the completion
                // chain a stock PLR map wires into game_round_win, here as a direct call. Before
                // declaring it, this cart books itself as stock's SetWinningTeam race arm does
                // (tf_gamerules.cpp:9633-9650): its own team's track total takes its progress.
                // The edge re-arms when the cart leaves its goal again (the next round). The flow
                // keeps the first winning call it sees (TeamRoundFlow's TeamWin guard).
                if (!_disabled && _totalProgress >= 1f && _prevThinkProgress < 1f)
                {
                    mode.AddTrainTrackProgress(Team, _totalProgress);
                    control.SetWinningTeam((TFTeam)Team, WinReason.AllPointsCaptured);
                }
                _prevThinkProgress = _totalProgress;

                float lastDistanceFromStart = _trainDistanceFromStart;
                _trainDistanceFromStart = _totalPathDistance - distanceToGoal;

                // Check if we've made forward progress, and accumulate it.
                // Stock drains the accumulator ONE foot at a time — a single
                // if, not a while — and per drained foot fires payload_pushed
                // (pusher, distance=1) server-only, once per player standing
                // in the bound area (team_train_watcher.cpp:1247-1282).
                if (_trainDistanceFromStart > lastDistanceFromStart)
                {
                    _trainDistanceAccumulator += _trainDistanceFromStart - lastDistanceFromStart;

                    if (_trainSpeedLevel > 0 && _trainDistanceAccumulator >= MinProgressToReport)
                    {
                        _trainDistanceAccumulator -= MinProgressToReport;

                        if (_areaCap is { } area)
                        {
                            foreach (var entity in area.TouchingEntities)
                            {
                                if (!engine.IsPlayer(entity))
                                    continue;

                                IntPtr pushed = gameEvents.EventCreate("payload_pushed");
                                if (pushed != IntPtr.Zero)
                                {
                                    gameEvents.EventSetInt(pushed, "pusher", players.GetUserId(entity));
                                    gameEvents.EventSetInt(pushed, "distance", 1);
                                    gameEvents.EventFire(pushed, dontBroadcast: true);
                                }
                            }
                        }
                    }
                }

                // play alert sounds if necessary
                for (int iCount = 0; iCount < _numCPLinks; iCount++)
                {
                    if (_trainDistanceFromStart < _cpLinks[iCount].DistanceFromStart - AlertDistance)
                    {
                        // back up twice the alert distance before resetting our flag to play the warning again
                        if (_trainDistanceFromStart < _cpLinks[iCount].DistanceFromStart - (AlertDistance * 2) || !_trainCanRecede)
                        {
                            // reset our alert flag
                            _cpLinks[iCount].AlertPlayed = false;
                        }
                    }
                    else
                    {
                        // PlayCaptureAlert (the announcer VO) is deferred; the
                        // flag keeps stock's arm/re-arm semantics.
                        if (_trainDistanceFromStart < _cpLinks[iCount].DistanceFromStart && !_cpLinks[iCount].AlertPlayed)
                            _cpLinks[iCount].AlertPlayed = true;
                    }
                }

                // check to see if we need to start or stop the alarm
                if (distanceToGoal <= AlarmDistance)
                {
                    mode.SetTrackAlarm(Team, true);

                    if (!disableAlarm)
                    {
                        if (!HasAlarm)
                        {
                            if (_numCPLinks > 0 && !_alarmPlayed)
                            {
                                // start the alarm at the final point
                                StartCaptureAlarm(_cpLinks[_numCPLinks - 1].Point);
                                _alarmPlayed = true; // used to prevent the alarm from starting again on maps where the train doesn't recede (alarm loops for short time then only plays singles)
                            }
                        }
                        else if (!_trainCanRecede) // if the train won't recede, we only want to play the alarm for a short time
                        {
                            if (_alarmEndTime > 0 && _alarmEndTime < clock.Now)
                            {
                                StopCaptureAlarm();
                                SetContextThink(AlarmThinkContext, AlarmThinkInterval);
                            }
                        }
                    }
                }
                else
                {
                    mode.SetTrackAlarm(Team, false);

                    StopCaptureAlarm();
                    _alarmPlayed = false;
                }
            }

            mode.SetTrainProgress(Team, _totalProgress);
            mode.SetTrainCappers(Team, _numCappers);
            mode.SetTrainSpeedLevel(Team, _trainSpeedLevel);
            mode.SetTrainRecedeTime(Team, _recedeTime);
        }

        SetNextThink(ThinkInterval);
    }

    private void WatcherAlarmThink()
    {
        if (_numCPLinks > 0)
        {
            var point = _cpLinks[_numCPLinks - 1].Point;
            if (point is not null)
                sound.EntityEmitSound(point.Handle, AlarmSingleSound);
        }

        SetContextThink(AlarmThinkContext, AlarmThinkInterval);
    }

    /// <summary>Stock's TimerMayExpire: the watcher's arm of the round timer's veto.</summary>
    public bool TimerMayExpire()
    {
        if (_disabled)
            return true;

        // Still in overtime if we're waiting to recede
        if (_waitingToRecede)
            return false;

        // capture blocked so we're not receding, but game shouldn't end
        if (_capBlocked)
            return false;

        // not waiting, so we're capping, in which case the area capture
        // will not let us expire
        return true;
    }

    // Project the given position onto the track and return the point and how far along that projected position is
    public void ProjectPointOntoPath(Vector pos, out Vector posOnPath, out float distanceAlongPath)
    {
        Vector closestPointOnPath = Vector.Zero;
        float closestPerpendicularDistanceSq = float.MaxValue;
        float closestDistanceAlongPath = float.MaxValue;
        float distanceAlong = 0f;

        HashSet<PathTrack> visited = [];
        PathTrack? node = _startNode;

        while (node is not null)
        {
            if (!visited.Add(node))
                break;

            PathTrack? nextNode = node.GetNext();

            if (nextNode is null)
                break;

            Vector alongPath = nextNode.Position - node.Position;
            float segmentLength = alongPath.Length;
            Vector alongUnit = segmentLength > 0f ? alongPath * (1f / segmentLength) : Vector.Zero;

            Vector toPos = pos - node.Position;
            float segmentOverlap = toPos.Dot(alongUnit);

            if (segmentOverlap >= 0.0f && segmentOverlap < segmentLength)
            {
                // projection is within segment bounds
                Vector onPath = node.Position + alongUnit * segmentOverlap;

                float perpendicularDistanceSq = (onPath - pos).LengthSquared;
                if (perpendicularDistanceSq < closestPerpendicularDistanceSq)
                {
                    closestPointOnPath = onPath;
                    closestPerpendicularDistanceSq = perpendicularDistanceSq;
                    closestDistanceAlongPath = distanceAlong + segmentOverlap;
                }
            }

            distanceAlong += segmentLength;
            node = nextNode;
        }

        posOnPath = closestPointOnPath;
        distanceAlongPath = closestDistanceAlongPath;
    }

    // Return true if the given position is farther down the track than the train is
    public bool IsAheadOfTrain(Vector pos)
    {
        ProjectPointOntoPath(pos, out _, out float distanceAlongPath);

        return distanceAlongPath > _trainDistanceFromStart;
    }

    // return true if the train is almost at the next checkpoint
    public bool IsTrainNearCheckpoint()
    {
        for (int i = 0; i < _numCPLinks; ++i)
        {
            if (_trainDistanceFromStart > _cpLinks[i].DistanceFromStart - AlertDistance &&
                _trainDistanceFromStart < _cpLinks[i].DistanceFromStart)
            {
                return true;
            }
        }

        return false;
    }

    // return true if the train hasn't left its starting position yet
    public bool IsTrainAtStart() => _trainDistanceFromStart < AlarmDistance;

    // return world space location of next checkpoint along the path
    public Vector GetNextCheckpointPosition()
    {
        for (int i = 0; i < _numCPLinks; ++i)
        {
            if (_trainDistanceFromStart < _cpLinks[i].DistanceFromStart)
                return _cpLinks[i].Node!.Position;
        }

        return Vector.Zero;
    }
}

/// <summary>What a map may set on team_train_watcher (team_train_watcher.cpp's DATADESC).</summary>
[ScriptVars]
public partial class TeamTrainWatcherVars
{
    [VarName("train")]
    public string Train { get; set; } = string.Empty;

    [VarName("start_node")]
    public string StartNode { get; set; } = string.Empty;

    [VarName("goal_node")]
    public string GoalNode { get; set; } = string.Empty;

    // The eight links, by index (stock's m_iszLinkedPathTracks[i] /
    // m_iszLinkedCPs[i] arrays under the linked_pathtrack_N / linked_cp_N keys).
    [VarName("linked_pathtrack_1")] public string LinkedPathTrack1 { get; set; } = string.Empty;
    [VarName("linked_pathtrack_2")] public string LinkedPathTrack2 { get; set; } = string.Empty;
    [VarName("linked_pathtrack_3")] public string LinkedPathTrack3 { get; set; } = string.Empty;
    [VarName("linked_pathtrack_4")] public string LinkedPathTrack4 { get; set; } = string.Empty;
    [VarName("linked_pathtrack_5")] public string LinkedPathTrack5 { get; set; } = string.Empty;
    [VarName("linked_pathtrack_6")] public string LinkedPathTrack6 { get; set; } = string.Empty;
    [VarName("linked_pathtrack_7")] public string LinkedPathTrack7 { get; set; } = string.Empty;
    [VarName("linked_pathtrack_8")] public string LinkedPathTrack8 { get; set; } = string.Empty;

    [VarName("linked_cp_1")] public string LinkedCP1 { get; set; } = string.Empty;
    [VarName("linked_cp_2")] public string LinkedCP2 { get; set; } = string.Empty;
    [VarName("linked_cp_3")] public string LinkedCP3 { get; set; } = string.Empty;
    [VarName("linked_cp_4")] public string LinkedCP4 { get; set; } = string.Empty;
    [VarName("linked_cp_5")] public string LinkedCP5 { get; set; } = string.Empty;
    [VarName("linked_cp_6")] public string LinkedCP6 { get; set; } = string.Empty;
    [VarName("linked_cp_7")] public string LinkedCP7 { get; set; } = string.Empty;
    [VarName("linked_cp_8")] public string LinkedCP8 { get; set; } = string.Empty;

    public string LinkedPathTrack(int index) => index switch
    {
        0 => LinkedPathTrack1,
        1 => LinkedPathTrack2,
        2 => LinkedPathTrack3,
        3 => LinkedPathTrack4,
        4 => LinkedPathTrack5,
        5 => LinkedPathTrack6,
        6 => LinkedPathTrack7,
        _ => LinkedPathTrack8,
    };

    public string LinkedCP(int index) => index switch
    {
        0 => LinkedCP1,
        1 => LinkedCP2,
        2 => LinkedCP3,
        3 => LinkedCP4,
        4 => LinkedCP5,
        5 => LinkedCP6,
        6 => LinkedCP7,
        _ => LinkedCP8,
    };

    public bool TrainCanRecede { get; set; } = true;

    [VarName("handle_train_movement")]
    public bool HandleTrainMovement { get; set; }

    [VarName("hud_min_speed_level_1")]
    public float HudMinSpeedLevel1 { get; set; }

    [VarName("hud_min_speed_level_2")]
    public float HudMinSpeedLevel2 { get; set; }

    [VarName("hud_min_speed_level_3")]
    public float HudMinSpeedLevel3 { get; set; }

    [VarName("start_disabled")]
    public bool StartDisabled { get; set; }

    /// <summary>env_spark_name: the named env_sparks burn while the cart runs backwards.</summary>
    [VarName("env_spark_name")]
    public string SparkName { get; set; } = string.Empty;

    [VarName("speed_forward_modifier")]
    public float SpeedForwardModifier { get; set; } = 1f;

    [VarName("train_recede_time")]
    public int TrainRecedeTime { get; set; }

    /// <summary>The watcher's team; stock's constructor ChangeTeam(TF_TEAM_BLUE).</summary>
    public int TeamNum { get; set; } = (int)TFTeam.Blue;
}
