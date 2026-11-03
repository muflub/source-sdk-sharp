//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Resources;

/// <summary>
/// The objective sizes (shareddefs.h), narrowed to TF2: stock sizes its
/// per-team runs for MAX_CONTROL_POINT_TEAMS (8) and MAX_TEAMS (32), and TF2
/// has four teams (<see cref="TFTeam.Count"/>), so a run here is per TF team.
/// </summary>
public static class ObjectiveLimits
{
    /// <summary>MAX_CONTROL_POINTS.</summary>
    public const int MaxControlPoints = 8;

    /// <summary>MAX_CONTROL_POINT_GROUPS.</summary>
    public const int MaxControlPointGroups = 8;

    /// <summary>MAX_PREVIOUS_POINTS.</summary>
    public const int MaxPreviousPoints = 3;

    /// <summary>The teams a per-team run holds: TF2's four, not stock's MAX_CONTROL_POINT_TEAMS.</summary>
    public const int Teams = (int)TFTeam.Count;

    /// <summary>MAX_CAPLAYOUT_LENGTH.</summary>
    public const int MaxCapLayoutLength = 32;

    /// <summary>MAX_PLAYERS (our kMaxPlayers).</summary>
    public const int MaxPlayers = 33;

    /// <summary>
    /// MAX_OBJECTS_PER_PLAYER, narrowed to our build cap: one dispenser, one
    /// sentry and a teleporter's two modes (TFObjectDefs' MaxObjects per
    /// (type, mode)).
    /// </summary>
    public const int MaxObjectsPerPlayer = 4;

    /// <summary>One team's object-list run (team_object_array): MAX_PLAYERS * MAX_OBJECTS_PER_PLAYER.</summary>
    public const int TeamObjects = MaxPlayers * MaxObjectsPerPlayer;

    /// <summary>TEAM_TRAIN_MAX_TEAMS.</summary>
    public const int TrainMaxTeams = 4;

    /// <summary>TEAM_TRAIN_MAX_HILLS.</summary>
    public const int TrainMaxHills = 5;

    /// <summary>TEAM_TRAIN_FLOATS_PER_HILL.</summary>
    public const int TrainFloatsPerHill = 2;

    /// <summary>CAPHUD_PARITY_BITS (team_objectiveresource.cpp).</summary>
    public const int CapHudParityBits = 6;

    /// <summary>CAPHUD_PARITY_MASK.</summary>
    public const int CapHudParityMask = (1 << CapHudParityBits) - 1;

    /// <summary>TEAM_ARRAY( index, team ): a point's slot in a per-point, per-team run.</summary>
    public static int TeamArray(int point, int team) => point + team * MaxControlPoints;

    /// <summary>The slot of a previous point: ipoint + (index * MAX_PREVIOUS_POINTS) + (team * MAX_CONTROL_POINTS * MAX_PREVIOUS_POINTS).</summary>
    public static int PreviousArray(int point, int team, int previous)
        => previous + point * MaxPreviousPoints + team * MaxControlPoints * MaxPreviousPoints;
}

/// <summary>The class names of the networked objective entities.</summary>
public static class ObjectiveClassNames
{
    /// <summary>Stock's CTFObjectiveResource.</summary>
    public const string ObjectiveResource = "tf_objective_resource";

    /// <summary>
    /// The per-team table (stock's three tf_team entities, one row each here).
    /// Not a stock class name: stock networks one CTFTeam per team.
    /// </summary>
    public const string TeamResource = "tf_team_resource";
}

/// <summary>
/// What every client is told about the objectives: stock's DT_BaseTeamObjectiveResource
/// (team_objectiveresource.h:173-232) as one table, read by the HUD (stage 3).
///
/// <para>
/// One declaration, so the two sides cannot drift (the house pattern of
/// <c>[StandaloneNetState]</c>). The runs are narrowed to TF2's four teams and
/// the narrow types the values fit; the MvM half of CTFObjectiveResource is not
/// carried (docs/plans/tf2/deferred.tsv). Two parts are filled by the modes
/// through <c>IModeObjectives</c> rather than by the control points: the train
/// (payload: path distance, hills, alarm) and the flags (CTF), which stock
/// does not carry here at all — the HUD read each item_teamflag — and which
/// sit here so a mode HUD has one table to read.
/// </para>
/// </summary>
[StandaloneNetState(ObjectiveClassNames.ObjectiveResource)]
public partial class ObjectiveResourceNetState
{
    // ---- the table ----------------------------------------------------------

    /// <summary>m_iTimerToShowInHUD: the entity index of the round timer the HUD shows, 0 for none.</summary>
    public partial int TimerToShowInHud { get; set; }

    /// <summary>m_iStopWatchTimer.</summary>
    public partial int StopWatchTimer { get; set; }

    /// <summary>m_iNumControlPoints.</summary>
    public partial byte NumControlPoints { get; set; }

    /// <summary>m_bPlayingMiniRounds.</summary>
    public partial bool PlayingMiniRounds { get; set; }

    /// <summary>m_bControlPointsReset: flips on each reset.</summary>
    public partial bool ControlPointsReset { get; set; }

    /// <summary>m_iUpdateCapHudParity: CAPHUD_PARITY_BITS wide, bumped on every HUD-visible change.</summary>
    public partial byte UpdateCapHudParity { get; set; }

    /// <summary>m_pszCapLayoutInHUD.</summary>
    [NetString(ObjectiveLimits.MaxCapLayoutLength)] public partial string CapLayoutInHud { get; set; }

    /// <summary>m_flCustomPositionX.</summary>
    public partial float CustomPositionX { get; set; }

    /// <summary>m_flCustomPositionY.</summary>
    public partial float CustomPositionY { get; set; }

    // ---- per point ----------------------------------------------------------

    /// <summary>m_vCPPositions, three floats per point (x, y, z).</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * 3)] public partial NetArray<float> Positions { get; }

    /// <summary>m_bCPIsVisible.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<bool> Visible { get; }

    /// <summary>m_flLazyCapPerc.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<float> LazyCapPerc { get; }

    /// <summary>m_bInMiniRound.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<bool> InMiniRound { get; }

    /// <summary>m_iWarnOnCap (CP_WARN_*).</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<byte> WarnOnCap { get; }

    /// <summary>m_bCPLocked.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<bool> Locked { get; }

    /// <summary>m_flUnlockTimes.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<float> UnlockTimes { get; }

    /// <summary>m_flCPTimerTimes.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<float> TimerTimes { get; }

    /// <summary>m_iCappingTeam.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<byte> CappingTeam { get; }

    /// <summary>m_iTeamInZone.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<byte> TeamInZone { get; }

    /// <summary>m_bBlocked.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<bool> Blocked { get; }

    /// <summary>m_iOwner: changes when a point is captured.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<byte> Owner { get; }

    /// <summary>m_bCPCapRateScalesWithPlayers.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<bool> CapRateScalesWithPlayers { get; }

    /// <summary>m_iCPGroup: TEAM_INVALID (-1) for none.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<sbyte> Group { get; }

    // ---- per point, per team (TEAM_ARRAY) ------------------------------------

    /// <summary>m_iTeamIcons: material string-table indices.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * ObjectiveLimits.Teams)] public partial NetArray<short> TeamIcons { get; }

    /// <summary>m_iTeamOverlays.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * ObjectiveLimits.Teams)] public partial NetArray<short> TeamOverlays { get; }

    /// <summary>m_iTeamReqCappers.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * ObjectiveLimits.Teams)] public partial NetArray<byte> TeamReqCappers { get; }

    /// <summary>m_flTeamCapTime.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * ObjectiveLimits.Teams)] public partial NetArray<float> TeamCapTime { get; }

    /// <summary>m_bTeamCanCap.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * ObjectiveLimits.Teams)] public partial NetArray<bool> TeamCanCap { get; }

    /// <summary>m_iNumTeamMembers: the cappers each team has on the point.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * ObjectiveLimits.Teams)] public partial NetArray<byte> NumTeamMembers { get; }

    /// <summary>m_iPreviousPoints (<see cref="ObjectiveLimits.PreviousArray"/>): -1 for none.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints * ObjectiveLimits.Teams * ObjectiveLimits.MaxPreviousPoints)]
    public partial NetArray<sbyte> PreviousPoints { get; }

    // ---- per team -----------------------------------------------------------

    /// <summary>m_iTeamBaseIcons.</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<short> TeamBaseIcons { get; }

    /// <summary>m_iBaseControlPoints: -1 for none.</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<sbyte> BaseControlPoints { get; }

    // ---- the train (payload), filled through IModeObjectives -----------------

    /// <summary>
    /// Stock's m_bMultipleTrains, networked on DT_TeamplayRoundBasedRules
    /// (teamplayroundbased_gamerules.h:268) and set from a
    /// tf_logic_multiple_escort's presence (tf_gamerules.cpp:4350-4357). Not a
    /// stock field here: we carry no gamerules entity, so the payload mode
    /// writes it into this table for the escort HUD's gates.
    /// </summary>
    public partial bool MultipleTrains { get; set; }

    /// <summary>m_flPathDistance: each point's place along the track, 0..1.</summary>
    [NetArray(ObjectiveLimits.MaxControlPoints)] public partial NetArray<float> PathDistance { get; }

    /// <summary>m_nNumNodeHillData.</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams)] public partial NetArray<byte> NumNodeHillData { get; }

    /// <summary>m_flNodeHillData: start, end per hill.</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams * ObjectiveLimits.TrainMaxHills * ObjectiveLimits.TrainFloatsPerHill)]
    public partial NetArray<float> NodeHillData { get; }

    /// <summary>m_bTrackAlarm.</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams)] public partial NetArray<bool> TrackAlarm { get; }

    /// <summary>m_bHillIsDownhill.</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams * ObjectiveLimits.TrainMaxHills)] public partial NetArray<bool> HillIsDownhill { get; }

    /// <summary>The train's progress per team, 0..1 (not a stock field: the watcher networked it itself).</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams)] public partial NetArray<float> TrainProgress { get; }

    /// <summary>Stock m_nNumCappers on the watcher: the cappers pushing a team's cart, -1 while it is blocked (not a stock field here).</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams)] public partial NetArray<sbyte> TrainCappers { get; }

    /// <summary>Stock m_iTrainSpeedLevel on the watcher: -1 backwards, 0 stalled, 1..3 forward (not a stock field here).</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams)] public partial NetArray<sbyte> TrainSpeedLevel { get; }

    /// <summary>Stock m_flRecedeTime on the watcher: when a waiting cart starts back, on the server clock; 0 for none (not a stock field here).</summary>
    [NetArray(ObjectiveLimits.TrainMaxTeams)] public partial NetArray<float> TrainRecedeTime { get; }

    // ---- the flags (CTF), filled through IModeObjectives ---------------------

    /// <summary>Each team's flag status (ETFFlagStatus: 0 home, 1 stolen, 2 dropped).</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<byte> FlagStatus { get; }

    /// <summary>Each team's flag carrier, as an entity index; 0 for none.</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<short> FlagCarrier { get; }

    /// <summary>When each team's dropped flag returns, on the server clock; 0 for none.</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<float> FlagResetTime { get; }

    /// <summary>
    /// Whether a team has a flag in play at all. Not a stock field: stock's HUD
    /// iterated the item_teamflag entities, which we do not mirror, and a
    /// status of 0 (home) is indistinguishable from no flag without it.
    /// </summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<bool> HasFlag { get; }

    /// <summary>
    /// Each team's flag's world origin (x, y, z) at its last status change —
    /// home's spawn point, a dropped flag's drop point. Not a stock field:
    /// stock's arrow panel read the flag entity's WorldSpaceCenter, and the
    /// client does not mirror item_teamflag. While the flag is stolen the
    /// client resolves the carrier's live origin from the carrier index.
    /// </summary>
    [NetArray(ObjectiveLimits.Teams * 3)] public partial NetArray<float> FlagOrigin { get; }

    /// <summary>
    /// Each team's capture zone's centre (x, y, z). Not a stock field: stock's
    /// CarriedImage panel pointed a second arrow at the local team's
    /// C_CaptureZone entity, which the client does not mirror.
    /// </summary>
    [NetArray(ObjectiveLimits.Teams * 3)] public partial NetArray<float> ZoneOrigin { get; }
}

/// <summary>
/// The per-team table the HUD and the scoreboard read: stock's tf_team
/// entities (DT_TFTeam over DT_Team: score, rounds won, flag captures, role;
/// tf_team.cpp:44-56, team.cpp) as one row per TF team.
/// </summary>
[StandaloneNetState(ObjectiveClassNames.TeamResource)]
public partial class TeamResourceNetState
{
    /// <summary>The goal strings' wire room: the 2048-byte tier minus the table's other 1580, over two.</summary>
    public const int GoalStringSize = 232;

    /// <summary>m_iScore.</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<short> Score { get; }

    /// <summary>m_iRoundsWon.</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<short> RoundsWon { get; }

    /// <summary>m_nFlagCaptures (8 bits in stock).</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<byte> FlagCaptures { get; }

    /// <summary>m_iRole (TEAM_ROLE_*: 0 none, 1 defenders, 2 attackers).</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<byte> Role { get; }

    /// <summary>The team's player count (stock sent the player list; the HUD read its length).</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<byte> PlayerCount { get; }

    /// <summary>
    /// The team's standing objects (tf_team.cpp:18-56's team_object_array,
    /// SendProxy_TeamObjectList), as entity indices, 0 for none. The run per
    /// team is <see cref="ObjectiveLimits.TeamObjects"/>, laid out
    /// team-major: <c>team * TeamObjects + slot</c>.
    /// </summary>
    [NetArray(ObjectiveLimits.Teams * ObjectiveLimits.TeamObjects)] public partial NetArray<short> Objects { get; }

    /// <summary>The team's leader (m_hLeader, tf_team.cpp:56), as an entity index; 0 for none.</summary>
    [NetArray(ObjectiveLimits.Teams)] public partial NetArray<short> Leader { get; }

    /// <summary>
    /// Stock's <c>m_pszTeamGoalStringRed</c> (tf_gamerules.h:1151,
    /// MAX_TEAMGOAL_STRING 256): the map's goal line for the red team's
    /// goal HUD (<c>InputSetRedTeamGoalString</c>, tf_gamerules.cpp:1769) —
    /// a gamerules word on the per-team mirror, as the round words ride the
    /// player resource (TFPlayerResourceNetState's tier leaves no room). The
    /// wire holds 232 of stock's 256: the table's 2048-byte tier minus its
    /// other 1580, over two strings (the set truncates at the same bound).
    /// </summary>
    [NetString(232)] public partial string GoalStringRed { get; set; }

    /// <summary>Stock's <c>m_pszTeamGoalStringBlue</c> (tf_gamerules.h:1152): the blue arm, the same 232.</summary>
    [NetString(232)] public partial string GoalStringBlue { get; set; }
}
