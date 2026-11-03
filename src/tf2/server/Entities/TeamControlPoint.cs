//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Rules;
using TF2.Server.Voice;
using TF2.Shared;
using TF2.Shared.Resources;
using TF2.Shared.Rules;

namespace TF2.Server.Entities;

/// <summary>team_control_point's spawnflags (team_control_point.h:16-20).</summary>
[Flags]
public enum ControlPointFlags
{
    None = 0,
    HideFlag = 1 << 0,
    HideModel = 1 << 1,
    HideShadow = 1 << 2,
    NoCapSounds = 1 << 3,
    BotsIgnore = 1 << 4,
}

/// <summary>
/// team_control_point: a point a team owns, the hologram that shows who, and
/// the outputs a map wires to its ownership (stock's CTeamControlPoint,
/// team_control_point.cpp). The capture itself is the area's
/// (<see cref="TriggerAreaCapture"/>); this keeps the owner, the lock and the
/// per-team data, tells the objective table, and asks the masters to check
/// for a win whenever the owner changes.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Prop)]
public sealed class TeamControlPoint(
    IObjectiveResource objectives,
    IObjectiveRoundRules roundRules,
    IControlPointRules rules,
    ITeamRoundState round,
    IObjectiveEntities objectiveEntities,
    IObjectiveEvents events,
    IEngineSound sound,
    IEnginePrecache precache,
    IHudMaterialIndex materials,
    IEngineEntityRender render,
    IGameClock clock,
    TFWinPanelCappers panelCappers,
    TFConceptSpeech speech) : ScriptedEntity
{
    public const string MapClass = "team_control_point";

    /// <summary>CONTROL_POINT_UNLOCK_THINK.</summary>
    public const string UnlockThinkContext = "UnlockThink";

    private const int Teams = ObjectiveLimits.Teams;

    public readonly Output OnCapTeam1 = new();
    public readonly Output OnCapTeam2 = new();
    public readonly Output OnCapReset = new();
    public readonly Output OnOwnerChangedToTeam1 = new();
    public readonly Output OnOwnerChangedToTeam2 = new();
    public readonly Output OnRoundStartOwnedByTeam1 = new();
    public readonly Output OnRoundStartOwnedByTeam2 = new();
    public readonly Output OnUnlocked = new();

    [ScriptVars] public TeamControlPointVars Vars = new();

    /// <summary>One copy per team, +1 for the un-owned state (stock's perteamdata_t).</summary>
    private sealed class TeamData
    {
        public string CapSound = string.Empty;
        public string Model = string.Empty;
        public int ModelBodygroup = -1;
        public int TeamPoseParam;
        public int Icon;
        public string IconName = string.Empty;
        public int Overlay;
        public string OverlayName = string.Empty;
        public int PlayersRequired;
        public int TimedPoints;
        public readonly string[] PreviousPoint = [string.Empty, string.Empty, string.Empty];
    }

    private readonly TeamData[] _teamData = [new(), new(), new(), new()];

    private int _team;
    private bool _active;
    private bool _locked;
    private float _unlockTime = -1f;
    private float _lastContestedAt = -1f;
    private int _pointIndex;
    private bool _pointIndexRead;
    private string _loopingSound = string.Empty;

    // ---- the map's keys -----------------------------------------------------

    /// <summary>The per-team keys (team_control_point.cpp:146-205): team_capsound_N and the rest.</summary>
    public override bool OnKeyValue(string key, string value)
    {
        if (TeamKey(key, "team_capsound_") is { } capsound) { capsound.CapSound = value; return true; }
        if (TeamKey(key, "team_model_") is { } model) { model.Model = value; return true; }
        if (TeamKey(key, "team_timedpoints_") is { } timed) { timed.TimedPoints = ParseInt(value); return true; }
        if (TeamKey(key, "team_bodygroup_") is { } body) { body.ModelBodygroup = ParseInt(value); return true; }
        if (TeamKey(key, "team_icon_") is { } icon) { icon.IconName = value; return true; }
        if (TeamKey(key, "team_overlay_") is { } overlay) { overlay.OverlayName = value; return true; }

        const string previous = "team_previouspoint_";
        if (key.StartsWith(previous, StringComparison.OrdinalIgnoreCase))
        {
            // sscanf( "%d_%d", &iTeam, &iPoint ), iPoint defaulting to 0.
            string[] parts = key[previous.Length..].Split('_');
            int team = parts.Length > 0 ? ParseInt(parts[0], -1) : -1;
            int point = parts.Length > 1 ? ParseInt(parts[1]) : 0;
            if ((uint)team < Teams && (uint)point < ObjectiveLimits.MaxPreviousPoints)
            {
                _teamData[team].PreviousPoint[point] = value;
                return true;
            }

            Log.Warning($"[TF2] {MapClass} '{Name}': {key} names no team or previous point\n");
            return true;
        }

        return false;
    }

    private TeamData? TeamKey(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        int team = ParseInt(key[prefix.Length..], -1);
        if ((uint)team < Teams)
            return _teamData[team];

        Log.Warning($"[TF2] {MapClass} '{Name}': {key} names no team\n");
        return null;
    }

    // ---- spawn --------------------------------------------------------------

    public override void OnSpawn()
    {
        // Validate our default team.
        if (Vars.DefaultOwner < 0 || Vars.DefaultOwner >= Teams)
        {
            Log.Warning($"{MapClass} '{Name}' has bad point_default_owner.\n");
            Vars.DefaultOwner = (int)TFTeam.Unassigned;
        }

        // TF_DLL's defaults.
        if (Vars.CaptureStartSound.Length == 0)
            Vars.CaptureStartSound = "Hologram.Start";
        if (Vars.CaptureEndSound.Length == 0)
            Vars.CaptureEndSound = "Hologram.Stop";
        if (Vars.CaptureProgressSound.Length == 0)
            Vars.CaptureProgressSound = "Hologram.Move";
        if (Vars.CaptureInterruptedSound.Length == 0)
            Vars.CaptureInterruptedSound = "Hologram.Interrupted";

        _locked = Vars.StartLocked;
        if (!_pointIndexRead)
            _pointIndex = Vars.PointIndex;

        Precache();

        // A hologram is looked at, never stood on: CBaseAnimating's SOLID_NONE.
        SetSolidity(BrushSolidity.None);

        InternalSetOwner(Vars.DefaultOwner, makeSound: false, []);
        rules.RecalculateControlPointState();

        SetActive(true);

        if (Flags.HasFlag(ControlPointFlags.HideModel))
            SetVisible(false);

        _lastContestedAt = -1f;
    }

    private ControlPointFlags Flags => SpawnFlagsAs<ControlPointFlags>();

    private void Precache()
    {
        for (int i = 0; i < Teams; i++)
        {
            // Skip over spectator.
            if (i == (int)TFTeam.Spectator)
                continue;

            var data = _teamData[i];
            if (data.CapSound.Length > 0)
                precache.PrecacheSound(data.CapSound);
            if (data.Model.Length > 0)
                precache.PrecacheModel(data.Model);

            if (data.IconName.Length > 0)
                data.Icon = materials.IndexOf(data.IconName);
            else
                Log.Warning($"Invalid hud icon material for team {i} in control point '{Name}' ( point index {PointIndex} )\n");

            if (data.OverlayName.Length > 0)
                data.Overlay = materials.IndexOf(data.OverlayName);
        }

        precache.PrecacheSound(Vars.CaptureStartSound);
        precache.PrecacheSound(Vars.CaptureEndSound);
        precache.PrecacheSound(Vars.CaptureProgressSound);
        precache.PrecacheSound(Vars.CaptureInterruptedSound);

        if (Vars.WarnSound.Length > 0)
            precache.PrecacheSound(Vars.WarnSound);

        precache.PrecacheSound("Announcer.ControlPointContested");
        precache.PrecacheSound("Announcer.ControlPointContested_Neutral");
    }

    // ---- what the master, the areas and the rules read ----------------------

    /// <summary>GetPointIndex: the map's point_index until the master renumbers from zero.</summary>
    public int PointIndex
    {
        get => _pointIndexRead ? _pointIndex : Vars.PointIndex;
        set
        {
            _pointIndex = value;
            _pointIndexRead = true;
        }
    }

    public int Owner => _team;

    public int DefaultOwner => Vars.DefaultOwner;

    public int Group => Vars.Group;

    public string PrintName => Vars.PrintName;

    public int WarnOnCap => Vars.WarnOnCap;

    public string WarnSound => Vars.WarnSound;

    public bool RandomOwnerOnRestart => Vars.RandomOwnerOnRestart;

    public bool IsActive => _active;

    public bool IsLocked => _locked;

    public bool PointIsVisible => !Flags.HasFlag(ControlPointFlags.HideFlag);

    public bool ShouldBotsIgnore => Flags.HasFlag(ControlPointFlags.BotsIgnore);

    public int GetHudIconIndexForTeam(int team) => _teamData[team].Icon;

    public int GetHudOverlayIndexForTeam(int team) => _teamData[team].Overlay;

    public int CurrentHudIconIndex => _teamData[_team].Icon;

    public int PlayersRequired(int team) => _teamData[team].PlayersRequired;

    public void SetCappersRequiredForTeam(int team, int cappers) => _teamData[team].PlayersRequired = cappers;

    /// <summary>GetPreviousPointForTeam: the index of the named point, -1 for none.</summary>
    public int GetPreviousPointForTeam(int team, int previous)
    {
        string name = _teamData[team].PreviousPoint[previous];
        return name.Length > 0 && objectiveEntities.FindByName<TeamControlPoint>(name) is { } point
            ? point.PointIndex
            : -1;
    }

    /// <summary>PointValue: the time-based points of an owner that is not the default one.</summary>
    public int PointValue => _team != Vars.DefaultOwner ? _teamData[_team].TimedPoints : 0;

    public bool HasBeenContested => _lastContestedAt > 0f;

    public float LastContestedAt
    {
        get => _lastContestedAt;
        set => _lastContestedAt = value;
    }

    public void SetActive(bool active)
    {
        _active = active;
        SetVisible(active);
    }

    // ---- inputs -------------------------------------------------------------

    /// <summary>
    /// InputSetOwner (used by area caps to set the owner). Stock gives the
    /// capture to a player caller; an input here has no caller, so the owner
    /// is set without a capper and without the capture sound
    /// (docs/divergences.md).
    /// </summary>
    [ScriptedEvent]
    private void OnSetOwner(float team)
    {
        int capTeam = (int)team;
        if ((uint)capTeam >= Teams)
        {
            Log.Warning($"[TF2] {MapClass} '{Name}': SetOwner {capTeam} names no team\n");
            return;
        }

        if (Owner == capTeam)
            return;

        if (rules.PointsMayBeCaptured)
        {
            // must be done before setting the owner
            HandleScoring(capTeam);

            InternalSetOwner(capTeam, makeSound: false, []);

            objectives.SetOwningTeam(PointIndex, _team);
            rules.RecalculateControlPointState();
        }
    }

    [ScriptedEvent] private void OnShowModel() => SetVisible(true);

    [ScriptedEvent] private void OnHideModel() => SetVisible(false);

    [ScriptedEvent]
    private void OnRoundActivate()
    {
        switch (_team)
        {
            case (int)TFTeam.Red: OnRoundStartOwnedByTeam1.Fire(); break;
            case (int)TFTeam.Blue: OnRoundStartOwnedByTeam2.Fire(); break;
        }

        InternalSetLocked(_locked);
    }

    [ScriptedEvent]
    private void OnSetLocked(float locked)
    {
        // never lock/unlock the point if we're in waiting for players
        if (round.InWaitingForPlayers)
            return;

        InternalSetLocked((int)locked > 0);
    }

    [ScriptedEvent]
    private void OnSetUnlockTime(float time)
    {
        // never lock/unlock the point if we're in waiting for players
        if (round.InWaitingForPlayers)
            return;

        int seconds = (int)time;
        if (seconds <= 0)
        {
            InternalSetLocked(false);
            return;
        }

        _unlockTime = clock.Now + seconds;
        objectives.SetCPUnlockTime(PointIndex, _unlockTime);
        SetContextThink(UnlockThinkContext, 0.1f);
    }

    public override void OnThink(string context)
    {
        if (context == UnlockThinkContext)
            UnlockThink();
    }

    /// <summary>UnlockThink: unlocks once the time has passed while the round runs.</summary>
    public void UnlockThink()
    {
        if (_unlockTime > 0f && _unlockTime < clock.Now && round.State == RoundState.RoundRunning)
        {
            InternalSetLocked(false);
            return;
        }

        SetContextThink(UnlockThinkContext, 0.1f);
    }

    // ---- owner handling -----------------------------------------------------

    /// <summary>ForceOwner: used when selecting a specific round to play.</summary>
    public void ForceOwner(int team)
    {
        InternalSetOwner(team, makeSound: false, []);
        objectives.SetOwningTeam(PointIndex, _team);
        rules.RecalculateControlPointState();
    }

    /// <summary>SetOwner: a capture by players, while points may be captured.</summary>
    public void SetOwner(int capTeam, bool makeSound, IReadOnlyList<int> cappers)
    {
        if (!rules.PointsMayBeCaptured)
            return;

        // must be done before setting the owner
        HandleScoring(capTeam);

        InternalSetOwner(capTeam, makeSound, cappers);
        objectives.SetOwningTeam(PointIndex, _team);
        rules.RecalculateControlPointState();
    }

    /// <summary>
    /// HandleScoring: a master that scores per capture gives the team a
    /// point, and plays Hud.EndRoundScored unless the capture wins.
    /// </summary>
    private void HandleScoring(int team)
    {
        var master = objectiveEntities.Master;
        if (master is null || !master.ShouldScorePerCapture)
            return;

        roundRules.AddTeamScore((TFTeam)team, 1);

        if (!master.WouldNewCPOwnerWinGame(this, team))
            sound.EntityEmitSound(Handle, "Hud.EndRoundScored");
    }

    /// <summary>
    /// InternalSetOwner: the new owner, its sound and model, the outputs, and
    /// the masters' win check.
    /// </summary>
    private void InternalSetOwner(int capTeam, bool makeSound, IReadOnlyList<int> cappers)
    {
        if ((uint)capTeam >= Teams)
            throw new ArgumentOutOfRangeException(nameof(capTeam), capTeam, "a TF2 team is 0..3");

        int oldTeam = _team;
        _team = capTeam;

        var data = _teamData[_team];
        if (makeSound && data.CapSound.Length > 0)
            sound.EntityEmitSound(Handle, data.CapSound);

        // Update visuals.
        if (data.Model.Length > 0)
            SetModel(data.Model);
        render.SetSkin(Handle, _team == (int)TFTeam.Unassigned ? 2 : _team - 2);
        PlaySequence("idle");

        // We add 1 to the index because we consider the default "no points capped" as 0.
        roundRules.SetLastCapPointChanged(PointIndex + 1);

        // Determine the pose parameters for each team.
        for (int i = 0; i < Teams; i++)
        {
            if (i == (int)TFTeam.Spectator)
                continue;
            _teamData[i].TeamPoseParam = render.LookupPoseParameter(Handle, $"cappoint_{i}_percentage");
        }
        UpdateCapPercentage();

        if (_team == (int)TFTeam.Unassigned)
            OnCapReset.Fire();
        else if (_team == (int)TFTeam.Red)
            OnCapTeam1.Fire();
        else if (_team == (int)TFTeam.Blue)
            OnCapTeam2.Fire();

        // If we're playing a sound, this is a true cap by players.
        if (makeSound)
        {
            // Make the members of our old team say something, then the cappers
            // their captured-point line (team_control_point.cpp:635-665).
            if (oldTeam > (int)TFTeam.Spectator && oldTeam != _team)
                speech.SpeakTeam(oldTeam, TFConceptSpeech.LostControlPoint);

            foreach (int capper in cappers)
                speech.Speak(capper, TFConceptSpeech.CapturedPoint);

            if (_team == (int)TFTeam.Red)
                OnOwnerChangedToTeam1.Fire();
            else if (_team == (int)TFTeam.Blue)
                OnOwnerChangedToTeam2.Fire();

            if (_team != (int)TFTeam.Unassigned && cappers.Count > 0)
                SendCapString(_team, cappers);
        }

        // Have control point master check the win conditions now!
        foreach (var master in objectiveEntities.Masters)
        {
            if (master.IsActive)
            {
                master.CheckWinConditions();
                master.LastOwnershipChangeTime = clock.Now;
            }
        }
    }

    private void SendCapString(int capTeam, IReadOnlyList<int> cappers)
    {
        // The win panel's cappers list: stock heard it off teamplay_point_captured
        // (tf_gamerules.cpp:18200-18202).
        panelCappers.NoteCappers(cappers);

        if (Vars.PrintName.Length == 0)
            return;

        events.PointCaptured(PointIndex, Vars.PrintName, capTeam, cappers);
    }

    /// <summary>CaptureStart: the teamplay_point_startcapture event.</summary>
    public void CaptureStart(int capTeam, IReadOnlyList<int> cappers)
    {
        float lastOwnershipChange = -1f;
        foreach (var master in objectiveEntities.Masters)
        {
            if (master.IsActive)
                lastOwnershipChange = master.LastOwnershipChangeTime;
        }

        events.PointStartCapture(PointIndex, Vars.PrintName, _team, capTeam, clock.Now - lastOwnershipChange, cappers);
    }

    public void CaptureEnd()
    {
        StopLoopingSounds();

        if (!Flags.HasFlag(ControlPointFlags.NoCapSounds))
            sound.EntityEmitSound(Handle, Vars.CaptureEndSound);
    }

    /// <summary>
    /// CaptureInterrupted: the looping progress sound (or the interrupted one,
    /// when blocked). Stock plays the loop through a sound patch; here it is
    /// the entity's own sound, stopped by name (<see cref="StopLoopingSounds"/>).
    /// </summary>
    public void CaptureInterrupted(bool blocked)
    {
        StopLoopingSounds();

        if (Flags.HasFlag(ControlPointFlags.NoCapSounds))
            return;

        string loop;
        if (blocked)
        {
            loop = Vars.CaptureInterruptedSound;
        }
        else
        {
            loop = Vars.CaptureProgressSound;
            sound.EntityEmitSound(Handle, Vars.CaptureStartSound);
        }

        if (loop.Length > 0)
        {
            sound.EntityEmitSound(Handle, loop);
            _loopingSound = loop;
        }
    }

    public void StopLoopingSounds()
    {
        if (_loopingSound.Length == 0)
            return;

        sound.EntityStopSound(Handle, _loopingSound);
        _loopingSound = string.Empty;
    }

    /// <summary>CaptureBlocked: teamplay_capture_blocked, then the blocker's
    /// voice concept (team_control_point.cpp:751-773, :970-976).</summary>
    public void CaptureBlocked(EntityHandle blocker, EntityHandle victim)
    {
        if (Vars.PrintName.Length == 0)
            return;

        events.CaptureBlocked(PointIndex, Vars.PrintName, blocker.EntryIndex,
                              victim == EntityHandle.None ? 0 : victim.EntryIndex);

        speech.Speak(blocker, TFConceptSpeech.CaptureBlocked);
    }

    /// <summary>UpdateCapPercentage: each team's pose parameter on the hologram.</summary>
    public void UpdateCapPercentage()
    {
        for (int i = (int)TFTeam.Red; i < Teams; i++)
        {
            float percentage = GetTeamCapPercentage(i);
            if (_teamData[i].TeamPoseParam != -1)
                render.SetPoseParameter(Handle, _teamData[i].TeamPoseParam, percentage);
        }
    }

    /// <summary>GetTeamCapPercentage: how much of the point a team holds.</summary>
    public float GetTeamCapPercentage(int team)
    {
        int cappingTeam = objectives.GetCappingTeam(PointIndex);
        if (cappingTeam == (int)TFTeam.Unassigned)
        {
            // No-one's capping this point.
            return team == _team ? 1f : 0f;
        }

        float percentage = objectives.GetCPCapPercentage(PointIndex);
        if (team == cappingTeam)
            return 1f - percentage;
        if (team == _team)
            return percentage;

        return 0f;
    }

    private void InternalSetLocked(bool locked)
    {
        if (!locked && _locked)
            events.PointUnlocked(PointIndex, Vars.PrintName, _team);
        else if (locked && !_locked)
            events.PointLocked(PointIndex, Vars.PrintName, _team);

        _locked = locked;

        if (PointIndex >= 0 && PointIndex < objectives.NumControlPoints)
        {
            objectives.SetCPLocked(PointIndex, _locked);
            objectives.SetCPUnlockTime(PointIndex, 0f);
        }

        if (!_locked)
        {
            _unlockTime = -1f;
            OnUnlocked.Fire();
            StopThinking(UnlockThinkContext);
        }
    }

    public override void OnRemoved() => StopLoopingSounds();
}

/// <summary>The map keys <see cref="TeamControlPoint"/> declares (team_control_point.cpp:25-37).</summary>
[ScriptVars]
public partial class TeamControlPointVars
{
    [VarName("point_printname")] public string PrintName { get; set; } = string.Empty;
    [VarName("point_group")] public int Group { get; set; }
    [VarName("point_default_owner")] public int DefaultOwner { get; set; }
    [VarName("point_index")] public int PointIndex { get; set; }
    [VarName("point_warn_on_cap")] public int WarnOnCap { get; set; }
    [VarName("point_warn_sound")] public string WarnSound { get; set; } = string.Empty;
    [VarName("point_capture_start_sound")] public string CaptureStartSound { get; set; } = string.Empty;
    [VarName("point_capture_end_sound")] public string CaptureEndSound { get; set; } = string.Empty;
    [VarName("point_capture_progress_sound")] public string CaptureProgressSound { get; set; } = string.Empty;
    [VarName("point_capture_interrupted_sound")] public string CaptureInterruptedSound { get; set; } = string.Empty;
    [VarName("random_owner_on_restart")] public bool RandomOwnerOnRestart { get; set; }
    [VarName("point_start_locked")] public bool StartLocked { get; set; }
}
