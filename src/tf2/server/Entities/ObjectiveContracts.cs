//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Entities;

/// <summary>
/// What the control-point stack asks of the round rules beyond the
/// ITeamRoundState/ITeamRoundControl contract: the members of
/// CTeamplayRoundBasedRules and CTFGameRules that the points, the areas and
/// the master call (team_control_point.cpp, trigger_area_capture.cpp,
/// team_control_point_master.cpp). The round flow (<c>TeamRoundFlow</c>,
/// Phase 2.1) is the one owner.
/// </summary>
public interface IObjectiveRoundRules
{
    /// <summary>InOvertime: a deteriorating capture falls back six times faster.</summary>
    bool InOvertime { get; }

    /// <summary>PointsMayAlwaysBeBlocked: TF's escort (payload) answer is true.</summary>
    bool PointsMayAlwaysBeBlocked { get; }

    /// <summary>AddTeamRespawnWaveTime: a capture area's team_spawn_N adjustment.</summary>
    void AddTeamRespawnWaveTime(TFTeam team, float seconds);

    /// <summary>CTeam::AddScore + HandleTeamScoreModify, for a point that scores per capture.</summary>
    void AddTeamScore(TFTeam team, int points);

    /// <summary>SetLastCapPointChanged( index + 1 ): 0 is "no point capped".</summary>
    void SetLastCapPointChanged(int index);

    /// <summary>GetRoundToPlayNext / SetRoundToPlayNext (tf_playround): a mini-round's name, or "".</summary>
    string RoundToPlayNext { get; set; }

    /// <summary>GetLastPlayedRound.</summary>
    string LastPlayedRound { get; }

    /// <summary>GetFirstRoundPlayed / SetFirstRoundPlayed: the first mini-round after a full restart.</summary>
    string FirstRoundPlayed { get; set; }

    /// <summary>AddPlayedRound.</summary>
    void AddPlayedRound(string round);

    /// <summary>IsPreviouslyPlayedRound.</summary>
    bool IsPreviouslyPlayedRound(string round);

    /// <summary>SetRoundOverlayDetails: the HUD's round overlay for the mini-round selected.</summary>
    void SetRoundOverlayDetails();

    /// <summary>PlaySpecialCapSounds: the announcer for a capture (TF: the final point's), before the owner changes.</summary>
    void PlaySpecialCapSounds(TFTeam cappingTeam, TeamControlPoint point);
}

/// <summary>
/// What a player is to a capture: CTeamplayRules' three per-player questions
/// (teamplay_gamerules.h:49-52). TF's answers need the player's class and
/// conditions (scouts count double, cloaked spies and ubered players cannot
/// cap, ubered players block): the per-player TF carrier's lane registers
/// those; until then <see cref="BaseCapturePlayerRules"/> answers the base
/// class's.
/// </summary>
public interface ICapturePlayerRules
{
    /// <summary>PlayerMayCapturePoint.</summary>
    bool PlayerMayCapturePoint(EntityHandle player, int pointIndex);

    /// <summary>PlayerMayBlockPoint: a player who may not cap but still blocks.</summary>
    bool PlayerMayBlockPoint(EntityHandle player, int pointIndex);

    /// <summary>GetCaptureValueForPlayer: how many cappers this player counts as.</summary>
    int CaptureValueForPlayer(EntityHandle player);
}

/// <summary>
/// The game events the stack fires (resource/modevents.res names), as one
/// seam the facts record. The game's is <see cref="ObjectiveGameEvents"/>.
/// </summary>
public interface IObjectiveEvents
{
    void StartTouch(EntityHandle player, int area);
    void EndTouch(EntityHandle player, int area);
    void PointStartCapture(int cp, string cpName, int team, int capTeam, float capTime, IReadOnlyList<int> cappers);
    void PointCaptured(int cp, string cpName, int team, IReadOnlyList<int> cappers);
    void CaptureBroken(int cp, string cpName, float timeRemaining);
    void CaptureBlocked(int cp, string cpName, int blocker, int victim);
    void PointLocked(int cp, string cpName, int team);
    void PointUnlocked(int cp, string cpName, int team);
    void CapperKilled(int blocker, int victim);
    void RoundSelected(string round);
}

/// <summary>
/// A HUD material's string-table index (CBaseEntity::GetMaterialIndex after
/// PrecacheMaterial): what the objective table's icons carry.
/// </summary>
public interface IHudMaterialIndex
{
    int IndexOf(string material);
}

/// <summary>
/// CTeamplayRules' answers (teamplay_gamerules.h:49-52): everyone counts once,
/// may cap, never only blocks. Not registered: the game's owner is TF's
/// (TF2.Server.Player.TFCapturePlayerRules); the facts' rig hands this one in.
/// </summary>
public sealed class BaseCapturePlayerRules : ICapturePlayerRules
{
    public bool PlayerMayCapturePoint(EntityHandle player, int pointIndex) => true;

    public bool PlayerMayBlockPoint(EntityHandle player, int pointIndex) => false;

    public int CaptureValueForPlayer(EntityHandle player) => 1;
}

/// <summary>
/// GetMaterialIndex: the material is precached into the Materials string
/// table (shell_stringtables.cpp:76) and its index read back from it. A name
/// the table does not hold answers 0 — the "invalid hud icon" stock's
/// warning names (team_control_point.cpp:221-262).
/// </summary>
[SideService(Side.Server, As = [typeof(IHudMaterialIndex)])]
public sealed class PrecachedMaterials(IEnginePrecache precache, INetworkStringTableDirectory tables) : IHudMaterialIndex
{
    /// <summary>The table shell_stringtables.cpp creates for precached materials.</summary>
    public const string TableName = "Materials";

    public int IndexOf(string material)
    {
        if (material.Length > 0)
            precache.PrecacheMaterial(material);

        return tables.Find(TableName) is { } table
            && table.FindStringIndex(material) is int index
            && index != INetworkStringTableRef.InvalidStringIndex
            ? index
            : 0;
    }
}

/// <summary>
/// The stack's game events through the engine's event bus: create, set,
/// fire, as DeathNotice does. An event the mod's event files do not declare
/// is created as zero and skipped. A float field has no setter on the bus, so
/// it is set as its text: KeyValues answers GetFloat from a string by atof,
/// which is how the event serialiser reads a "float" key.
/// </summary>
[SideService(Side.Server, As = [typeof(IObjectiveEvents)])]
public sealed class ObjectiveGameEvents(IEngineGameEvents events) : IObjectiveEvents
{
    public void StartTouch(EntityHandle player, int area) => Touch("controlpoint_starttouch", player, area);

    public void EndTouch(EntityHandle player, int area) => Touch("controlpoint_endtouch", player, area);

    private void Touch(string name, EntityHandle player, int area)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "player", player.EntryIndex);
        events.EventSetInt(e, "area", area);
        events.EventFire(e);
    }

    public void PointStartCapture(int cp, string cpName, int team, int capTeam, float capTime, IReadOnlyList<int> cappers)
    {
        IntPtr e = events.EventCreate("teamplay_point_startcapture");
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "cp", cp);
        events.EventSetString(e, "cpname", cpName);
        events.EventSetInt(e, "team", team);
        events.EventSetInt(e, "capteam", capTeam);
        events.EventSetString(e, "captime", capTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
        events.EventSetString(e, "cappers", Cappers(cappers));
        events.EventSetInt(e, "priority", 7);
        events.EventFire(e);
    }

    public void PointCaptured(int cp, string cpName, int team, IReadOnlyList<int> cappers)
    {
        IntPtr e = events.EventCreate("teamplay_point_captured");
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "cp", cp);
        events.EventSetString(e, "cpname", cpName);
        events.EventSetInt(e, "team", team);
        events.EventSetString(e, "cappers", Cappers(cappers));
        events.EventSetInt(e, "priority", 9);
        events.EventFire(e);
    }

    public void CaptureBroken(int cp, string cpName, float timeRemaining)
    {
        IntPtr e = events.EventCreate("teamplay_capture_broken");
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "cp", cp);
        events.EventSetString(e, "cpname", cpName);
        events.EventSetString(e, "time_remaining", timeRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
        events.EventFire(e);
    }

    public void CaptureBlocked(int cp, string cpName, int blocker, int victim)
    {
        IntPtr e = events.EventCreate("teamplay_capture_blocked");
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "cp", cp);
        events.EventSetString(e, "cpname", cpName);
        events.EventSetInt(e, "blocker", blocker);
        events.EventSetInt(e, "priority", 9);
        if (victim > 0)
            events.EventSetInt(e, "victim", victim);
        events.EventFire(e);
    }

    public void PointLocked(int cp, string cpName, int team) => Lock("teamplay_point_locked", cp, cpName, team);

    public void PointUnlocked(int cp, string cpName, int team) => Lock("teamplay_point_unlocked", cp, cpName, team);

    private void Lock(string name, int cp, string cpName, int team)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "cp", cp);
        events.EventSetString(e, "cpname", cpName);
        events.EventSetInt(e, "team", team);
        events.EventFire(e);
    }

    public void CapperKilled(int blocker, int victim)
    {
        IntPtr e = events.EventCreate("capper_killed");
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "blocker", blocker);
        events.EventSetInt(e, "victim", victim);
        events.EventFire(e);
    }

    public void RoundSelected(string round)
    {
        IntPtr e = events.EventCreate("teamplay_round_selected");
        if (e == IntPtr.Zero)
            return;
        events.EventSetString(e, "round", round);
        events.EventFire(e);
    }

    /// <summary>
    /// The cappers as stock writes them: each player index as one char, at
    /// most eight, the string ending at the first zero.
    /// </summary>
    internal static string Cappers(IReadOnlyList<int> cappers)
    {
        int count = Math.Min(cappers.Count, 8);
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = (char)(byte)cappers[i];
        return new string(chars);
    }
}
