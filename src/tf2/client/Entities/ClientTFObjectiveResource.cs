//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Sounds;
using SourceSharp.Client.Standalone;

using TF2.Client.Ui;
using TF2.Shared;
using TF2.Shared.Resources;

namespace TF2.Client.Entities;

/// <summary>
/// The convars stock's client objective resource reads
/// (<c>mp_capstyle</c>, <c>mp_capdeteriorate_time</c> and, through
/// <c>TeamMayCapturePoint</c>, <c>tf_caplinear</c>) and the one the cap
/// ladder reads (<c>mp_blockstyle</c>, hud_controlpointicons.cpp:1571), as
/// one seam so a fact can hand them a record and the game hands them the
/// convars.
/// </summary>
public interface ITFObjectiveSettings
{
    /// <summary><c>mp_capstyle</c>: 1 scales the capture rate with the cappers.</summary>
    int CapStyle { get; }

    /// <summary><c>mp_capdeteriorate_time</c>: seconds a scaled capture takes to fall back to nothing.</summary>
    float CapDeteriorateTime { get; }

    /// <summary><c>tf_caplinear</c>: teams must capture the points in order.</summary>
    bool CapLinear { get; }

    /// <summary><c>mp_blockstyle</c>: 1 pauses a blocked capture, 0 breaks it.</summary>
    int BlockStyle { get; }
}

/// <summary>The game's <see cref="ITFObjectiveSettings"/>: the replicated convars, read as they stand.</summary>
[SideService(Side.Client, As = [typeof(ITFObjectiveSettings)])]
public sealed class TFClientObjectiveSettings(MpCapStyleConVar capStyle, MpCapDeteriorateTimeConVar capDeteriorateTime,
                                              TFCapLinearConVar capLinear, MpBlockStyleConVar blockStyle) : ITFObjectiveSettings
{
    public int CapStyle => capStyle.Value;

    public float CapDeteriorateTime => capDeteriorateTime.Value;

    public bool CapLinear => capLinear.Value;

    public int BlockStyle => blockStyle.Value;
}

/// <summary>
/// What the objective table tells this client: stock's
/// <c>C_BaseTeamObjectiveResource</c> getters (c_team_objectiveresource.h:47-232)
/// over the decoded <see cref="ObjectiveResourceNetState"/>, plus the client's
/// cap-time arithmetic (<c>GetCPCapPercentage</c>, <c>CapIsBlocked</c>,
/// <c>ClientThink</c>), which the server does not network.
/// </summary>
public interface ITFObjectiveSource
{
    int NumControlPoints { get; }

    /// <summary><c>m_iTimerToShowInHUD</c>: the round timer the HUD shows, 0 for none.</summary>
    int TimerToShowInHud { get; }

    int StopWatchTimer { get; }

    bool PlayingMiniRounds { get; }

    /// <summary><c>m_bControlPointsReset</c>: flips on each reset.</summary>
    bool ControlPointsReset { get; }

    /// <summary><c>m_iUpdateCapHudParity</c>.</summary>
    int UpdateCapHudParity { get; }

    string CapLayoutInHud { get; }

    float CustomPositionX { get; }

    float CustomPositionY { get; }

    /// <summary>Stock <c>GetOwningTeam</c>: unassigned past the point count.</summary>
    int OwningTeam(int point);

    /// <summary>Stock <c>GetCappingTeam</c>: unassigned past the point count.</summary>
    int CappingTeam(int point);

    /// <summary>Stock <c>GetTeamInZone</c>: unassigned past the point count.</summary>
    int TeamInZone(int point);

    Vector Position(int point);

    /// <summary><c>m_bCPIsVisible</c>.</summary>
    bool IsVisible(int point);

    /// <summary><c>m_bBlocked</c> (stock <c>IsCPBlocked</c>).</summary>
    bool IsBlocked(int point);

    /// <summary><c>m_bCPLocked</c> (stock <c>GetCPLocked</c>).</summary>
    bool IsLocked(int point);

    /// <summary><c>m_bInMiniRound</c>.</summary>
    bool InMiniRound(int point);

    /// <summary><c>m_iWarnOnCap</c> (CP_WARN_*).</summary>
    int WarningLevel(int point);

    /// <summary><c>m_iCPGroup</c>: -1 for none.</summary>
    int Group(int point);

    int IconForTeam(int point, int team);

    int OverlayForTeam(int point, int team);

    int RequiredCappers(int point, int team);

    float TeamCapTime(int point, int team);

    bool TeamCanCap(int point, int team);

    /// <summary>Stock <c>CTFGameRules::TeamMayCapturePoint</c>
    /// (tf_gamerules.cpp:16769) over this client's table: whether
    /// <paramref name="team"/> may capture <paramref name="point"/> — the
    /// cap-linear ladder's gate (the same answer the server's
    /// <c>ControlPointRules</c> gives, from the fields the table networks).</summary>
    bool TeamMayCap(int point, int team);

    /// <summary>Stock <c>GetNumPlayersInArea</c>: the cappers a team has on the point.</summary>
    int NumPlayersInArea(int point, int team);

    /// <summary><c>m_flUnlockTimes[point]</c>: when the point unlocks, on the server clock; 0 for none.</summary>
    float UnlockTime(int point);

    /// <summary><c>m_flCPTimerTimes[point]</c>: when the point's timer runs out, on the server clock; 0 for none.</summary>
    float TimerTime(int point);

    /// <summary>Stock <c>GetPreviousPointForPoint</c>: -1 for none.</summary>
    int PreviousPoint(int point, int team, int previous);

    int BaseIconForTeam(int team);

    /// <summary>Stock <c>GetBaseControlPointForTeam</c>: -1 for none.</summary>
    int BaseControlPointForTeam(int team);

    /// <summary>The train's place at each point, 0..1 (payload).</summary>
    float PathDistance(int point);

    /// <summary>Stock's m_bMultipleTrains (tf_gamerules'), carried here for
    /// the escort HUD's group test and alarm gate.</summary>
    bool MultipleTrains { get; }

    /// <summary>Stock <c>GetNumNodeHillData</c>.</summary>
    int NumNodeHillData(int team);

    /// <summary>Stock <c>GetHillData</c>: false when the team or hill is out of range.</summary>
    bool HillData(int team, int hill, out float start, out float end);

    bool IsHillDownhill(int team, int hill);

    bool TrackAlarm(int team);

    /// <summary>The train's progress per team, 0..1 (payload; not a stock networked field).</summary>
    float TrainProgress(int team);

    /// <summary>Stock m_nNumCappers (the watcher's): the cappers pushing a team's cart, -1 while it is blocked.</summary>
    int TrainCappers(int team);

    /// <summary>Stock m_iTrainSpeedLevel (the watcher's): -1 backwards, 0 stalled, 1..3 forward.</summary>
    int TrainSpeedLevel(int team);

    /// <summary>Stock m_flRecedeTime (the watcher's): when a waiting cart starts back, on the server clock; 0 for none.</summary>
    float TrainRecedeTime(int team);

    /// <summary>ETFFlagStatus per team (CTF): 0 home, 1 stolen, 2 dropped.</summary>
    int FlagStatus(int team);

    /// <summary>Each team's flag carrier, as an entity index; 0 for none.</summary>
    int FlagCarrier(int team);

    /// <summary>When each team's dropped flag returns, on the server clock; 0 for none.</summary>
    float FlagResetTime(int team);

    /// <summary>Each team's flag's world origin at its last status change (home's spawn point, a drop point); the carrier's live origin resolves a stolen flag, which the HUD's adapter does.</summary>
    Vector FlagOrigin(int team);

    /// <summary>Each team's capture zone's centre (not a stock networked field; stock's HUD pointed at the C_CaptureZone entity).</summary>
    Vector ZoneOrigin(int team);

    /// <summary>Whether a team has a flag in play at all (not a stock networked field; stock's HUD iterated the flag entities).</summary>
    bool HasFlag(int team);

    /// <summary>The points a team owns.</summary>
    int OwnedControlPoints(int team);

    /// <summary>
    /// Stock <c>GetCPCapPercentage</c>, advanced to <paramref name="now"/> first
    /// (see <see cref="ClientTFObjectiveResource"/>'s note on the think).
    /// </summary>
    float CapProgress(int point, float now);

    /// <summary>Stock <c>CapIsBlocked</c>: a cap has time left and no cappers on the point.</summary>
    bool CapIsBlocked(int point);
}

/// <summary>
/// The client half of <c>tf_objective_resource</c>: the decoded
/// <see cref="ObjectiveResourceNetState"/> and the cap-time model stock kept
/// client-side (<c>C_BaseTeamObjectiveResource</c>, c_team_objectiveresource.cpp).
///
/// <para>
/// THE THINK IS LAZY. Stock's <c>ClientThink</c> runs every 0.1 s and moves
/// each point's cap time by the elapsed time; this client has no per-entity
/// think driver, so <see cref="CapProgress"/> advances the model to the time it
/// is asked about. The integral is the same, and a read after any interval
/// answers what stock's last think would have (docs/divergences.md). Overtime's
/// sixfold deterioration is deferred: the overtime flag lives on the server's
/// round machine, and nothing replicated carries it.
/// </para>
/// </summary>
[ClientStandaloneEntity(ObjectiveClassNames.ObjectiveResource)]
public sealed class ClientTFObjectiveResource : ClientStandaloneEntity
{
    private readonly TFClientObjectiveResources _resources;
    private readonly ITFObjectiveSettings _settings;
    private readonly IGameClock _clock;
    private readonly TFCapTimes _capTimes = new();
    private readonly TFCapTimes.IContestedCapSounds _contested;

    public ClientTFObjectiveResource(IEngineEntityRender render, TFClientObjectiveResources resources,
                                     ITFObjectiveSettings settings, IGameClock clock,
                                     IClientEntitySounds sounds, ITFLocalPlayer localPlayer,
                                     IFwdSoundEmitter warnScripts)
        : base(render)
    {
        _resources = resources;
        _settings = settings;
        _clock = clock;
        _contested = new ContestedAnnouncer(sounds, localPlayer, warnScripts);
    }

    public override string ClassName => ObjectiveClassNames.ObjectiveResource;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public ObjectiveResourceNetState State = new();

    /// <summary>How many updates have decoded into <see cref="State"/>.</summary>
    public int Decodes { get; private set; }

    public override void OnSpawn() => _resources.Live = this;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;

        // The new values are already decoded into State; the model keeps the
        // copies stock's receive proxies and OnPreDataChanged compared against.
        _capTimes.DataChanged(State, _clock.Now, _contested);

        _resources.Live = this;
    }

    public override void OnRemoved()
    {
        if (ReferenceEquals(_resources.Live, this))
            _resources.Live = null;
    }

    // ---- the table (stock's client getters) ---------------------------------

    public int OwningTeam(int point)
        => point < 0 || point >= State.NumControlPoints ? (int)TFTeam.Unassigned : State.Owner[point];

    public int CappingTeam(int point)
        => point < 0 || point >= State.NumControlPoints ? (int)TFTeam.Unassigned : State.CappingTeam[point];

    public int TeamInZone(int point)
        => point < 0 || point >= State.NumControlPoints ? (int)TFTeam.Unassigned : State.TeamInZone[point];

    public Vector Position(int point)
    {
        Point(point);
        return new Vector(State.Positions[point * 3], State.Positions[point * 3 + 1], State.Positions[point * 3 + 2]);
    }

    public bool IsVisible(int point) => State.Visible[Point(point)];

    public bool IsBlocked(int point) => State.Blocked[Point(point)];

    public bool IsLocked(int point) => State.Locked[Point(point)];

    public bool InMiniRound(int point) => State.InMiniRound[Point(point)];

    public int WarningLevel(int point) => State.WarnOnCap[Point(point)];

    public int Group(int point) => State.Group[Point(point)];

    public int IconForTeam(int point, int team) => State.TeamIcons[Team(point, team)];

    public int OverlayForTeam(int point, int team) => State.TeamOverlays[Team(point, team)];

    public int RequiredCappers(int point, int team) => State.TeamReqCappers[Team(point, team)];

    public float TeamCapTime(int point, int team) => State.TeamCapTime[Team(point, team)];

    public bool TeamCanCap(int point, int team) => State.TeamCanCap[Team(point, team)];

    /// <summary>Stock <c>TeamMayCapturePoint</c>, via the cap-time model's
    /// copy (TFCapTimes' note on the ported ladder).</summary>
    public bool TeamMayCap(int point, int team)
    {
        Point(point);
        return TFCapTimes.TeamMayCapturePoint(State, _settings, team, point);
    }

    public int NumPlayersInArea(int point, int team) => State.NumTeamMembers[Team(point, team)];

    public float UnlockTime(int point) => State.UnlockTimes[Point(point)];

    public float TimerTime(int point) => State.TimerTimes[Point(point)];

    public int PreviousPoint(int point, int team, int previous)
    {
        Team(point, team);
        if (previous < 0 || previous >= ObjectiveLimits.MaxPreviousPoints)
            throw new ArgumentOutOfRangeException(nameof(previous));

        return State.PreviousPoints[ObjectiveLimits.PreviousArray(point, team, previous)];
    }

    public int BaseIconForTeam(int team) => State.TeamBaseIcons[TeamIndex(team)];

    public int BaseControlPointForTeam(int team) => State.BaseControlPoints[TeamIndex(team)];

    public float PathDistance(int point) => State.PathDistance[Point(point)];

    public bool MultipleTrains => State.MultipleTrains;

    public int NumNodeHillData(int team)
        => team < 0 || team >= ObjectiveLimits.TrainMaxTeams ? 0 : State.NumNodeHillData[team];

    public bool HillData(int team, int hill, out float start, out float end)
    {
        start = 0f;
        end = 0f;

        if (team < 0 || team >= ObjectiveLimits.TrainMaxTeams || hill < 0 || hill >= ObjectiveLimits.TrainMaxHills)
            return false;

        int index = hill * ObjectiveLimits.TrainFloatsPerHill
                    + team * ObjectiveLimits.TrainMaxHills * ObjectiveLimits.TrainFloatsPerHill;
        start = State.NodeHillData[index];
        end = State.NodeHillData[index + 1];
        return true;
    }

    public bool IsHillDownhill(int team, int hill)
    {
        if (team < 0 || team >= ObjectiveLimits.TrainMaxTeams || hill < 0 || hill >= ObjectiveLimits.TrainMaxHills)
            return true;   // stock's IsHillDownhill default

        return State.HillIsDownhill[hill + team * ObjectiveLimits.TrainMaxHills];
    }

    public bool TrackAlarm(int team)
        => team >= 0 && team < ObjectiveLimits.TrainMaxTeams && State.TrackAlarm[team];

    public float TrainProgress(int team)
        => team < 0 || team >= ObjectiveLimits.TrainMaxTeams ? 0f : State.TrainProgress[team];

    public int TrainCappers(int team)
        => team < 0 || team >= ObjectiveLimits.TrainMaxTeams ? 0 : State.TrainCappers[team];

    public int TrainSpeedLevel(int team)
        => team < 0 || team >= ObjectiveLimits.TrainMaxTeams ? 0 : State.TrainSpeedLevel[team];

    public float TrainRecedeTime(int team)
        => team < 0 || team >= ObjectiveLimits.TrainMaxTeams ? 0f : State.TrainRecedeTime[team];

    // The flags are per team, not per point per team: the server writes
    // s.FlagStatus[team] (ObjectiveResource.cs:816-818) into runs of Teams.
    public int FlagStatus(int team) => State.FlagStatus[TeamIndex(team)];

    public int FlagCarrier(int team) => State.FlagCarrier[TeamIndex(team)];

    public float FlagResetTime(int team) => State.FlagResetTime[TeamIndex(team)];

    public Vector FlagOrigin(int team) => Origin(State.FlagOrigin, team);

    public Vector ZoneOrigin(int team) => Origin(State.ZoneOrigin, team);

    /// <summary>A per-team origin run (three floats per team) as a vector.</summary>
    private static Vector Origin(NetArray<float> run, int team)
        => new(run[team * 3], run[team * 3 + 1], run[team * 3 + 2]);

    public bool HasFlag(int team) => team >= 0 && team < ObjectiveLimits.Teams && State.HasFlag[team];

    public int OwnedControlPoints(int team)
    {
        int owned = 0;
        for (int point = 0; point < State.NumControlPoints; point++)
        {
            if (State.Owner[point] == team)
                owned++;
        }

        return owned;
    }

    /// <summary>
    /// Stock <c>GetCPCapPercentage</c> (c_team_objectiveresource.cpp:264-279),
    /// after the model has been advanced to <paramref name="now"/>.
    /// </summary>
    public float CapProgress(int point, float now)
    {
        Point(point);
        _capTimes.Advance(State, _settings, _contested, now);
        return _capTimes.Percentage(State, point);
    }

    /// <summary>Stock <c>CapIsBlocked</c> (c_team_objectiveresource.cpp:356-368).</summary>
    public bool CapIsBlocked(int point)
    {
        Point(point);
        return _capTimes.CapIsBlocked(State, point);
    }

    /// <summary>A point index the table holds, or a throw naming it (stock asserts, which release drops).</summary>
    private static int Point(int point)
    {
        if (point < 0 || point >= ObjectiveLimits.MaxControlPoints)
            throw new ArgumentOutOfRangeException(nameof(point));

        return point;
    }

    /// <summary>A point's slot in a per-team run, or a throw naming the point or team.</summary>
    private static int Team(int point, int team)
    {
        Point(point);
        return ObjectiveLimits.TeamArray(point, TeamIndex(team));
    }

    /// <summary>A team index the table holds, or a throw naming it.</summary>
    private static int TeamIndex(int team)
    {
        if (team < 0 || team >= ObjectiveLimits.Teams)
            throw new ArgumentOutOfRangeException(nameof(team));

        return team;
    }
}

/// <summary>The client's live <c>tf_objective_resource</c>, when one has arrived.</summary>
[SideService(Side.Client, As = [typeof(ITFObjectiveSource)])]
public sealed class TFClientObjectiveResources : ITFObjectiveSource
{
    public ClientTFObjectiveResource? Live { get; set; }

    public int NumControlPoints => Live?.State.NumControlPoints ?? 0;

    public int TimerToShowInHud => Live?.State.TimerToShowInHud ?? 0;

    public int StopWatchTimer => Live?.State.StopWatchTimer ?? 0;

    public bool PlayingMiniRounds => Live?.State.PlayingMiniRounds ?? false;

    public bool ControlPointsReset => Live?.State.ControlPointsReset ?? false;

    public int UpdateCapHudParity => Live?.State.UpdateCapHudParity ?? 0;

    public string CapLayoutInHud => Live?.State.CapLayoutInHud ?? string.Empty;

    public float CustomPositionX => Live?.State.CustomPositionX ?? 0f;

    public float CustomPositionY => Live?.State.CustomPositionY ?? 0f;

    public int OwningTeam(int point) => Live?.OwningTeam(point) ?? (int)TFTeam.Unassigned;

    public int CappingTeam(int point) => Live?.CappingTeam(point) ?? (int)TFTeam.Unassigned;

    public int TeamInZone(int point) => Live?.TeamInZone(point) ?? (int)TFTeam.Unassigned;

    public Vector Position(int point) => Live?.Position(point) ?? default;

    public bool IsVisible(int point) => Live?.IsVisible(point) ?? false;

    public bool IsBlocked(int point) => Live?.IsBlocked(point) ?? false;

    public bool IsLocked(int point) => Live?.IsLocked(point) ?? false;

    public bool InMiniRound(int point) => Live?.InMiniRound(point) ?? false;

    public int WarningLevel(int point) => Live?.WarningLevel(point) ?? 0;

    public int Group(int point) => Live?.Group(point) ?? -1;

    public int IconForTeam(int point, int team) => Live?.IconForTeam(point, team) ?? 0;

    public int OverlayForTeam(int point, int team) => Live?.OverlayForTeam(point, team) ?? 0;

    public int RequiredCappers(int point, int team) => Live?.RequiredCappers(point, team) ?? 0;

    public float TeamCapTime(int point, int team) => Live?.TeamCapTime(point, team) ?? 0f;

    public bool TeamCanCap(int point, int team) => Live?.TeamCanCap(point, team) ?? false;

    public bool TeamMayCap(int point, int team) => Live?.TeamMayCap(point, team) ?? false;

    public int NumPlayersInArea(int point, int team) => Live?.NumPlayersInArea(point, team) ?? 0;

    public float UnlockTime(int point) => Live?.UnlockTime(point) ?? 0f;

    public float TimerTime(int point) => Live?.TimerTime(point) ?? 0f;

    public int PreviousPoint(int point, int team, int previous) => Live?.PreviousPoint(point, team, previous) ?? -1;

    public int BaseIconForTeam(int team) => Live?.BaseIconForTeam(team) ?? 0;

    public int BaseControlPointForTeam(int team) => Live?.BaseControlPointForTeam(team) ?? -1;

    public float PathDistance(int point) => Live?.PathDistance(point) ?? 0f;

    public bool MultipleTrains => Live?.MultipleTrains ?? false;

    public int NumNodeHillData(int team) => Live?.NumNodeHillData(team) ?? 0;

    public bool HillData(int team, int hill, out float start, out float end)
    {
        if (Live is { } resource)
            return resource.HillData(team, hill, out start, out end);

        start = 0f;
        end = 0f;
        return false;
    }

    public bool IsHillDownhill(int team, int hill) => Live?.IsHillDownhill(team, hill) ?? true;

    public bool TrackAlarm(int team) => Live?.TrackAlarm(team) ?? false;

    public float TrainProgress(int team) => Live?.TrainProgress(team) ?? 0f;

    public int TrainCappers(int team) => Live?.TrainCappers(team) ?? 0;

    public int TrainSpeedLevel(int team) => Live?.TrainSpeedLevel(team) ?? 0;

    public float TrainRecedeTime(int team) => Live?.TrainRecedeTime(team) ?? 0f;

    public int FlagStatus(int team) => Live?.FlagStatus(team) ?? 0;

    public int FlagCarrier(int team) => Live?.FlagCarrier(team) ?? 0;

    public float FlagResetTime(int team) => Live?.FlagResetTime(team) ?? 0f;

    public Vector FlagOrigin(int team) => Live?.FlagOrigin(team) ?? default;

    public Vector ZoneOrigin(int team) => Live?.ZoneOrigin(team) ?? default;

    public bool HasFlag(int team) => Live?.HasFlag(team) ?? false;

    public int OwnedControlPoints(int team) => Live?.OwnedControlPoints(team) ?? 0;

    public float CapProgress(int point, float now) => Live?.CapProgress(point, now) ?? 0f;

    public bool CapIsBlocked(int point) => Live?.CapIsBlocked(point) ?? false;
}

/// <summary>
/// The cap-time model stock kept client-side: <c>m_flCapTimeLeft</c>,
/// <c>m_flCapLastThinkTime</c> and the copies its receive proxies compared
/// against (c_team_objectiveresource.cpp:24-41, :174-246, :264-464).
///
/// <para>
/// <c>m_iCappingTeam</c> and <c>m_iOwner</c> arrive as plain fields here, where
/// stock's recv proxies ran <c>SetCappingTeam</c>/<c>SetOwningTeam</c> as they
/// decoded; <see cref="DataChanged"/> runs the same two setters from a diff
/// against the values the last packet left, in the same order stock's table
/// decodes them (CappingTeam before Owner), so each setter sees what stock's
/// did.
/// </para>
/// </summary>
internal sealed class TFCapTimes
{
    /// <summary><c>LAST_SHARED_TEAM</c>: the highest team that is not a game team.</summary>
    private const int LastSharedTeam = (int)TFTeam.Spectator;

    /// <summary><c>CP_WARN_NORMAL</c> (shareddefs.h:900): the only level the contested sound plays at.</summary>
    private const int CPWarnNormal = 0;

    /// <summary><c>CP_WARN_FINALCAP</c> (shareddefs.h:900): the level the warn-sound arm plays at.</summary>
    private const int CPWarnFinalCap = 1;

    /// <summary>
    /// The contested-point announcer's seat (c_tf_objective_resource.cpp:135-161):
    /// stock's client <c>SetCappingTeam</c> plays the sound from the local
    /// player's ears before its model moves.
    /// </summary>
    public interface IContestedCapSounds
    {
        /// <summary>The local player's team (<c>GetLocalPlayer()-&gt;GetTeamNumber()</c>).</summary>
        int LocalTeam { get; }

        /// <summary>
        /// A cap has begun on <paramref name="point"/>, owned by
        /// <paramref name="ownerTeam"/>: the generic announcer name, contested
        /// when the point has an owner and neutral when it has none.
        /// </summary>
        void Contested(int point, int ownerTeam);

        /// <summary>
        /// The FINALCAP countdown's warn (c_team_objectiveresource.cpp:404-424):
        /// the point's own name, the engine sound-script index
        /// <paramref name="warnSoundIndex"/> read back (<c>m_iszWarnSound</c>;
        /// -1 for none), played from the local player. A name the index answers
        /// nothing for plays nothing.
        /// </summary>
        void FinalCapWarn(short warnSoundIndex);
    }

    private readonly float[] _capTimeLeft = new float[ObjectiveLimits.MaxControlPoints];
    private readonly float[] _lastThink = new float[ObjectiveLimits.MaxControlPoints];
    private readonly byte[] _oldCappingTeam = new byte[ObjectiveLimits.MaxControlPoints];
    private readonly byte[] _oldOwner = new byte[ObjectiveLimits.MaxControlPoints];
    private readonly float[] _oldLazyCapPerc = new float[ObjectiveLimits.MaxControlPoints];
    private readonly bool[] _warnedOnFinalCap = new bool[ObjectiveLimits.MaxControlPoints];
    private readonly float[] _lastCapWarningTime = new float[ObjectiveLimits.MaxControlPoints];

    /// <summary>The receive proxies and OnDataChanged's lazy-percentage seeding, over one packet.</summary>
    public void DataChanged(ObjectiveResourceNetState state, float now, IContestedCapSounds? contested = null)
    {
        for (int point = 0; point < ObjectiveLimits.MaxControlPoints; point++)
        {
            int capping = state.CappingTeam[point];
            if (capping != _oldCappingTeam[point])
            {
                // Stock's C_TFObjectiveResource::SetCappingTeam
                // (c_tf_objective_resource.cpp:135-161), before the model
                // moves: the start of a cap on a point that warns, that the
                // capping team does not own, and that the local player's team
                // is not capping.
                if (contested is not null && state.NumControlPoints > 0
                    && state.WarnOnCap[point] == CPWarnNormal
                    && Percentage(state, point, _oldCappingTeam[point]) == 0f
                    && capping != (int)TFTeam.Unassigned
                    && capping != contested.LocalTeam)
                {
                    contested.Contested(point, state.Owner[point]);
                }

                SetCappingTeam(point, capping, state, now);
            }

            int owner = state.Owner[point];
            if (owner != _oldOwner[point])
                SetOwningTeam(point, owner, state);

            // Stock's OnDataChanged walks only the points the table holds, and
            // its OnPreDataChanged copies the old percentages over that same
            // run, so a point outside it never seeds and never records one.
            if (point < state.NumControlPoints && _oldLazyCapPerc[point] != state.LazyCapPerc[point])
            {
                _capTimeLeft[point] = state.LazyCapPerc[point]
                                      * state.TeamCapTime[ObjectiveLimits.TeamArray(point, state.CappingTeam[point])];
            }

            _oldCappingTeam[point] = state.CappingTeam[point];
            _oldOwner[point] = state.Owner[point];
            _oldLazyCapPerc[point] = state.LazyCapPerc[point];
        }
    }

    /// <summary>Stock <c>SetCappingTeam</c> (:324-341), with the owner as the proxy saw it.
    /// A new capping team re-arms the warn (:336).</summary>
    private void SetCappingTeam(int point, int team, ObjectiveResourceNetState state, float now)
    {
        if (team != _oldOwner[point] && team > LastSharedTeam)
            _capTimeLeft[point] = state.TeamCapTime[ObjectiveLimits.TeamArray(point, team)];
        else
            _capTimeLeft[point] = 0f;

        _warnedOnFinalCap[point] = false;

        _lastThink[point] = now;
    }

    /// <summary>Stock <c>SetOwningTeam</c> (:306-319): a successful cap clears the timer.</summary>
    private void SetOwningTeam(int point, int team, ObjectiveResourceNetState state)
    {
        if (team == state.CappingTeam[point])
        {
            _capTimeLeft[point] = 0f;
            _lastThink[point] = 0f;
        }
    }

    /// <summary>Stock <c>ClientThink</c> (:373-464), over the interval to <paramref name="now"/>.</summary>
    public void Advance(ObjectiveResourceNetState state, ITFObjectiveSettings settings,
                        IContestedCapSounds? contested, float now)
    {
        for (int point = 0; point < ObjectiveLimits.MaxControlPoints; point++)
        {
            if (_capTimeLeft[point] == 0f)
                continue;

            if (state.Blocked[point])
            {
                _lastThink[point] = now;
                continue;
            }

            bool deteriorateNormally = true;
            int teamInZone = TeamInZone(state, point);
            int playersCapping = state.NumTeamMembers[ObjectiveLimits.TeamArray(point, teamInZone)];

            if (playersCapping > 0)
            {
                float elapsed = now - _lastThink[point];
                float reduction = elapsed;

                if (settings.CapStyle == 1 && state.CapRateScalesWithPlayers[point])
                {
                    // Diminishing returns for successive players.
                    for (int player = 1; player < playersCapping; player++)
                        reduction += elapsed / (player + 1);
                }

                if (teamInZone == state.CappingTeam[point])
                {
                    deteriorateNormally = false;
                    _capTimeLeft[point] -= reduction;

                    // Stock's warn arm (:404-424): while the countdown runs,
                    // an enemy capping a final-cap point makes its own warn
                    // sound play once through the local player, no more often
                    // than every five seconds. Stock's <c>pPlayer</c> guard is
                    // the seated interface; without one nothing is asked for.
                    if (!_warnedOnFinalCap[point] && contested is { } announcer)
                    {
                        int capping = state.CappingTeam[point];
                        if (capping != (int)TFTeam.Unassigned
                            && announcer.LocalTeam != capping
                            && state.WarnOnCap[point] == CPWarnFinalCap
                            && now > _lastCapWarningTime[point] + 5f)
                        {
                            announcer.FinalCapWarn(state.WarnSoundIndexes[point]);
                            _warnedOnFinalCap[point] = true;
                            _lastCapWarningTime[point] = now;
                        }
                    }
                }
                else if (OwningTeam(state, point) == (int)TFTeam.Unassigned
                         && teamInZone != (int)TFTeam.Unassigned)
                {
                    deteriorateNormally = false;
                    _capTimeLeft[point] += reduction;
                }
            }

            if (deteriorateNormally)
            {
                // Caps deteriorate over time; if the point is not cappable at
                // all, wipe all progress.
                if (TeamMayCapturePoint(state, settings, state.CappingTeam[point], point))
                {
                    float capLength = state.TeamCapTime[ObjectiveLimits.TeamArray(point, state.CappingTeam[point])];
                    float scale = state.CapRateScalesWithPlayers[point] ? settings.CapDeteriorateTime : capLength;
                    _capTimeLeft[point] += capLength / scale * (now - _lastThink[point]);
                }
                else
                {
                    _capTimeLeft[point] = 0f;
                }

                _warnedOnFinalCap[point] = false;
            }

            _lastThink[point] = now;
        }
    }

    /// <summary>Stock <c>GetCPCapPercentage</c> (:264-279).</summary>
    public float Percentage(ObjectiveResourceNetState state, int point)
        => Percentage(state, point, state.CappingTeam[point]);

    /// <summary>
    /// <see cref="Percentage"/> over an explicit capping team: the contested
    /// check reads the model through the capping team the last packet left,
    /// because the proxy runs before <c>SetCappingTeam</c> replaces it.
    /// </summary>
    private float Percentage(ObjectiveResourceNetState state, int point, int cappingTeam)
    {
        float capLength = state.TeamCapTime[ObjectiveLimits.TeamArray(point, cappingTeam)];
        if (capLength <= 0f)
            return 0f;

        float elapsed = capLength - _capTimeLeft[point];
        if (elapsed > capLength)
            return 1f;

        return elapsed / capLength;
    }

    /// <summary>Stock <c>CapIsBlocked</c> (:356-368).</summary>
    public bool CapIsBlocked(ObjectiveResourceNetState state, int point)
        => _capTimeLeft[point] != 0f
           && state.NumTeamMembers[ObjectiveLimits.TeamArray(point, state.CappingTeam[point])] == 0;

    /// <summary>
    /// Stock <c>CTFGameRules::TeamMayCapturePoint</c> (tf_gamerules.cpp:16769),
    /// over this client's own table: the same answer the server's
    /// <c>ControlPointRules</c> gives, from the fields it networks.
    /// </summary>
    internal static bool TeamMayCapturePoint(ObjectiveResourceNetState state, ITFObjectiveSettings settings,
                                            int team, int point)
    {
        if (!settings.CapLinear)
            return true;

        int needed = state.PreviousPoints[ObjectiveLimits.PreviousArray(point, team, 0)];

        // Points set to require themselves are always cappable.
        if (needed == point)
            return true;

        if (state.Locked[point])
            return false;

        if (needed == -1)
        {
            if (!state.PlayingMiniRounds)
            {
                int farthest = FarthestOwnedControlPoint(state, team);
                return Math.Abs(farthest - point) <= 1;
            }

            return true;
        }

        for (int previous = 0; previous < ObjectiveLimits.MaxPreviousPoints; previous++)
        {
            needed = state.PreviousPoints[ObjectiveLimits.PreviousArray(point, team, previous)];
            if (needed != -1 && OwningTeam(state, needed) != team)
                return false;
        }

        return true;
    }

    /// <summary>Stock <c>GetFarthestOwnedControlPoint(team, false)</c> (tf_gamerules.cpp:16734).</summary>
    private static int FarthestOwnedControlPoint(ObjectiveResourceNetState state, int team)
    {
        int ownedEnd = state.BaseControlPoints[team];
        if (ownedEnd == -1)
            return -1;

        int walk = 1;
        int enemyEnd = state.NumControlPoints - 1;
        if (ownedEnd != 0)
        {
            walk = -1;
            enemyEnd = 0;
        }

        int farthest = ownedEnd;
        for (int point = ownedEnd; point != enemyEnd; point += walk)
        {
            if (OwningTeam(state, point) != team)
                break;

            farthest = point;
        }

        return farthest;
    }

    /// <summary>Stock <c>GetOwningTeam</c>'s guard (c_team_objectiveresource.h:67-73).</summary>
    private static int OwningTeam(ObjectiveResourceNetState state, int point)
        => point < 0 || point >= state.NumControlPoints ? (int)TFTeam.Unassigned : state.Owner[point];

    /// <summary>Stock <c>GetTeamInZone</c>'s guard (c_team_objectiveresource.h:83-89).</summary>
    private static int TeamInZone(ObjectiveResourceNetState state, int point)
        => point < 0 || point >= state.NumControlPoints ? (int)TFTeam.Unassigned : state.TeamInZone[point];
}

/// <summary>
/// The sounds themselves. Stock's contested announcer
/// (c_tf_objective_resource.cpp:147-155) is
/// <c>C_BaseEntity::EmitSound( CLocalPlayerFilter, -1, name )</c> — the local
/// player's ears, no entity — and its FINALCAP warn (c_team_objectiveresource.cpp:417)
/// is <c>pPlayer-&gt;EmitSound( GetWarnSound( i ) )</c>, the local player's own
/// ears either way: both are <see cref="IClientEntitySounds.EmitFrom"/> from
/// no handle. A point with no owner is contested neutral (:148-155).
/// </summary>
public sealed class ContestedAnnouncer(IClientEntitySounds sounds, ITFLocalPlayer localPlayer,
                                       IFwdSoundEmitter warnScripts) : TFCapTimes.IContestedCapSounds
{
    /// <summary>Stock's <c>pLocalPlayer-&gt;GetTeamNumber()</c>; 0 while no row has arrived.</summary>
    public int LocalTeam => localPlayer.Team;

    /// <summary>
    /// The generic announcer names, as stock plays them: contested on an
    /// owned point, neutral on one with no owner. The point's warn sound
    /// never enters this arm (:145-155).
    /// </summary>
    public void Contested(int point, int ownerTeam)
        => sounds.EmitFrom(EntityHandle.None, ownerTeam == (int)TFTeam.Unassigned
            ? "Announcer.ControlPointContested_Neutral"
            : "Announcer.ControlPointContested");

    /// <summary>
    /// The point's warn sound at the FINALCAP countdown: the name the engine's
    /// sound table answers at the wire's index. An empty name, or an index it
    /// answers nothing for, plays nothing.
    /// </summary>
    public void FinalCapWarn(short warnSoundIndex)
    {
        string? warn = warnSoundIndex >= 0 ? warnScripts.SoundName(warnSoundIndex) : null;
        sounds.EmitFrom(EntityHandle.None, warn ?? string.Empty);
    }
}
