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
using TF2.Shared.Resources;
using TF2.Shared.Rules;

namespace TF2.Server.Entities;

/// <summary>
/// team_control_point_master: the map's referee for its points (stock's
/// CTeamControlPointMaster, team_control_point_master.cpp). It numbers the
/// points from zero, writes them into the objective table, picks the
/// mini-round to play, and declares the round won when one team owns every
/// point of a group (or of the round being played) — through
/// <see cref="ITeamRoundControl"/>, the round flow's contract.
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class TeamControlPointMaster(
    IObjectiveResource objectives,
    IObjectiveEntities entities,
    IObjectiveRoundRules roundRules,
    IControlPointRules rules,
    ITeamRoundState round,
    ITeamRoundControl control,
    IObjectiveEvents events,
    IHudMaterialIndex materials,
    IEngineEntities engineEntities,
    IUniformRandom random,
    IKothRoundState koth) : ScriptedEntity
{
    public const string MapClass = "team_control_point_master";

    /// <summary>CPM_THINK.</summary>
    public const string ThinkContext = "CTeamControlPointMasterCPMThink";

    public readonly Output OnWonByTeam1 = new();
    public readonly Output OnWonByTeam2 = new();

    [ScriptVars] public TeamControlPointMasterVars Vars = new();

    private readonly string[] _teamBaseIconNames = [string.Empty, string.Empty, string.Empty, string.Empty];
    private readonly int[] _teamBaseIcons = new int[ObjectiveLimits.Teams];

    /// <summary>m_ControlPoints, in the order found (CUtlMap's element order), renumbered from zero.</summary>
    private readonly List<TeamControlPoint> _points = new();

    /// <summary>m_ControlPointRounds, highest priority first.</summary>
    private readonly List<TeamControlPointRound> _rounds = new();

    private bool _disabled;
    private int _currentRoundIndex = -1;
    private bool _firstRoundAfterRestart = true;
    private string _capLayout = string.Empty;
    private float _customPositionX = -1f;
    private float _customPositionY = -1f;

    public override bool OnKeyValue(string key, string value)
    {
        const string prefix = "team_base_icon_";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        int team = ParseInt(key[prefix.Length..], -1);
        if ((uint)team < ObjectiveLimits.Teams)
            _teamBaseIconNames[team] = value;
        else
            Log.Warning($"[TF2] {MapClass} '{Name}': {key} names no team\n");
        return true;
    }

    public override void OnSpawn()
    {
        _disabled = Vars.StartDisabled;
        _capLayout = Vars.CapLayout;
        _customPositionX = Vars.CustomPositionX;
        _customPositionY = Vars.CustomPositionY;
        _currentRoundIndex = -1;
        _firstRoundAfterRestart = true;
        LastOwnershipChangeTime = -1f;

        for (int team = 0; team < ObjectiveLimits.Teams; team++)
        {
            if (_teamBaseIconNames[team].Length > 0)
                _teamBaseIcons[team] = materials.IndexOf(_teamBaseIconNames[team]);
        }
    }

    /// <summary>
    /// Activate: find the points and the rounds right away, so the client's
    /// HUD knows the number and the starting state of the points before the
    /// game starts.
    /// </summary>
    public override void OnServerActivate()
    {
        FindControlPoints();
        FindControlPointRounds();
        SetBaseControlPoints();
    }

    // ---- what the points, the areas and the rules read ----------------------

    public bool IsActive => !_disabled;

    public int NumPoints => _points.Count;

    public IReadOnlyList<TeamControlPoint> ControlPoints => _points;

    public IReadOnlyList<TeamControlPointRound> Rounds => _rounds;

    public bool PlayingMiniRounds => _rounds.Count > 0;

    public int CurrentRoundIndex => _currentRoundIndex;

    public TeamControlPointRound? CurrentRound
        => !PlayingMiniRounds || _currentRoundIndex == -1 ? null : _rounds[_currentRoundIndex];

    public bool ShouldScorePerCapture => Vars.ScoreStyle;

    public bool ShouldPlayAllControlPointRounds => Vars.PlayAllRounds;

    public bool ShouldSwitchTeamsOnRoundWin => Vars.SwitchTeams;

    public float PartialCapturePointRate => Vars.PartialCapPointsRate;

    public float LastOwnershipChangeTime { get; set; } = -1f;

    public TeamControlPoint? GetControlPoint(int point) => _points.FirstOrDefault(p => p.PointIndex == point);

    public float PointLastContestedAt(int point) => GetControlPoint(point)?.LastContestedAt ?? -1f;

    public int GetNumPointsOwnedByTeam(int team) => _points.Count(p => p.Owner == team);

    /// <summary>GetRoundToUseAfterRestart: the current round's name if it is of the highest priority, else "".</summary>
    public string GetRoundToUseAfterRestart()
    {
        if (PlayingMiniRounds && CurrentRound is { } current && current.Priority == HighestRoundPriority)
            return current.Name;
        return string.Empty;
    }

    private int HighestRoundPriority => _rounds.Count == 0 ? -1 : Math.Max(-1, _rounds.Max(r => r.Priority));

    // ---- finding ------------------------------------------------------------

    /// <summary>FindControlPoints: the active points, numbered from zero by their point_index, into the table.</summary>
    private bool FindControlPoints()
    {
        int found = 0;
        foreach (var point in entities.Points.ToList())
        {
            if (!point.IsActive)
                continue;

            if (_points.Any(p => p.PointIndex == point.PointIndex))
            {
                Log.Warning("!!!!\nMultiple control points with the same index, duplicates ignored\n!!!!\n");
                engineEntities.Remove(point.Handle);
                continue;
            }

            if (_points.Count == ObjectiveLimits.MaxControlPoints)
            {
                // Stock adds it anyway and writes past its arrays; the extra point is left out here.
                Log.Warning($"Too many control points! Max is {ObjectiveLimits.MaxControlPoints}\n");
                continue;
            }

            _points.Add(point);
            found++;
        }

        // Remap the indices of the control points so they are 0-based.
        var handled = new bool[_points.Count];
        for (int newIndex = 0; newIndex < _points.Count; newIndex++)
        {
            // Find the lowest numbered, unhandled point.
            int lowest = -1;
            int lowestValue = 999;
            for (int j = 0; j < _points.Count; j++)
            {
                if (!handled[j] && _points[j].PointIndex < lowestValue)
                {
                    lowest = j;
                    lowestValue = _points[j].PointIndex;
                }
            }

            // Don't examine this point again, and give it its new index.
            handled[lowest] = true;
            _points[lowest].PointIndex = newIndex;
        }

        if (_points.Count == 0)
        {
            Log.Warning("Error! No control points found in map!\n");
            return false;
        }

        // Now setup the objective resource.
        objectives.SetNumControlPoints(_points.Count);
        foreach (var point in _points)
        {
            int index = point.PointIndex;
            objectives.SetOwningTeam(index, point.Owner);
            objectives.SetCPVisible(index, point.PointIsVisible);
            objectives.SetCPPosition(index, engineEntities.GetOrigin(point.Handle));
            objectives.SetWarnOnCap(index, point.WarnOnCap);
            objectives.SetWarnSound(index, point.WarnSound);
            objectives.SetCPGroup(index, point.Group);
            for (int team = 0; team < ObjectiveLimits.Teams; team++)
            {
                objectives.SetCPIcons(index, team, point.GetHudIconIndexForTeam(team));
                objectives.SetCPOverlays(index, team, point.GetHudOverlayIndexForTeam(team));
                for (int previous = 0; previous < ObjectiveLimits.MaxPreviousPoints; previous++)
                    objectives.SetPreviousPoint(index, team, previous, point.GetPreviousPointForTeam(team, previous));
            }
        }

        _ = found;
        return true;
    }

    private void SetBaseControlPoints()
    {
        for (int team = 0; team < ObjectiveLimits.Teams; team++)
        {
            objectives.SetTeamBaseIcons(team, _teamBaseIcons[team]);
            objectives.SetBaseCP(GetBaseControlPoint(team), team);
        }
    }

    /// <summary>FindControlPointRounds: every round, highest priority first.</summary>
    private bool FindControlPointRounds()
    {
        _rounds.Clear();
        foreach (var r in entities.Rounds)
        {
            if (!_rounds.Contains(r))
                _rounds.Insert(0, r);
        }

        bool found = _rounds.Count > 0;
        if (found)
        {
            // sort them in our list by priority (highest priority first); stock's qsort leaves ties unordered
            var sorted = _rounds.OrderByDescending(r => r.Priority).ToList();
            _rounds.Clear();
            _rounds.AddRange(sorted);
        }

        objectives.SetPlayingMiniRounds(found);
        objectives.SetCapLayoutInHud(_capLayout);
        objectives.SetCapLayoutCustomPosition(_customPositionX, _customPositionY);

        return found;
    }

    /// <summary>IsInRound: whether a point belongs to the mini-round being played (always, without mini-rounds).</summary>
    public bool IsInRound(TeamControlPoint point)
    {
        if (_rounds.Count > 0 && _currentRoundIndex != -1)
            return _rounds[_currentRoundIndex].IsControlPointInRound(point);
        return true;
    }

    public int NumPlayableControlPointRounds() => _rounds.Count(r => r.IsPlayable);

    // ---- the round to play --------------------------------------------------

    private bool SelectSpecificRound()
    {
        TeamControlPointRound? chosen = null;

        if (roundRules.RoundToPlayNext.Length > 0)
        {
            // do we have the name of a round?
            chosen = entities.FindByName<TeamControlPointRound>(roundRules.RoundToPlayNext);
            if (chosen is not null && (!_rounds.Contains(chosen) || (!chosen.IsPlayable && !chosen.MakePlayable())))
                chosen = null;

            roundRules.RoundToPlayNext = string.Empty;
        }

        // do we have a round to play?
        if (chosen is null)
            return false;

        _currentRoundIndex = _rounds.IndexOf(chosen);
        chosen.SelectedToPlay();
        roundRules.SetRoundOverlayDetails();
        FireRoundStartOutput();

        if (!round.InWaitingForPlayers)
        {
            Log.Msg($"World triggered \"Mini_Round_Selected\" (round \"{chosen.Name}\")\n");
            Log.Msg("World triggered \"Mini_Round_Start\"\n");
        }

        return true;
    }

    private void RegisterRoundBeingPlayed()
    {
        // let the game rules know what round we're playing
        string name = _rounds[_currentRoundIndex].Name;
        roundRules.AddPlayedRound(name);

        if (_firstRoundAfterRestart)
        {
            roundRules.FirstRoundPlayed = name;
            _firstRoundAfterRestart = false;
        }

        events.RoundSelected(name);
    }

    /// <summary>GetControlPointRoundToPlay (L468): the playable rounds of the top priority, avoiding a repeat, at random.</summary>
    public bool GetControlPointRoundToPlay()
    {
        // are we trying to pick a specific round?
        if (SelectSpecificRound())
        {
            SetBaseControlPoints();
            RegisterRoundBeingPlayed();
            return true;
        }

        // rounds are sorted with the higher priority rounds first
        int first = _rounds.FindIndex(r => r.IsPlayable);
        if (first < 0)
        {
            // we didn't find one to play
            _currentRoundIndex = -1;
            return false;
        }

        // we have a priority value, now we need to randomly pick a round with this priority that's playable
        int priority = _rounds[first].Priority;
        var candidates = new List<int>();

        string lastPlayed = roundRules.LastPlayedRound;
        int lastPlayedIndex = -1;
        string firstPlayed = roundRules.FirstRoundPlayed;
        int firstPlayedIndex = -1; // after a full restart

        // loop through and find the rounds with this priority value
        for (int i = 0; i < _rounds.Count; i++)
        {
            var r = _rounds[i];
            if (!r.IsPlayable || r.Priority != priority)
                continue;

            // is this the last round we played?
            if (lastPlayed.Length > 0 && lastPlayed == r.Name)
                lastPlayedIndex = i;

            // is this the first round we played after the last full restart?
            if (_firstRoundAfterRestart && firstPlayed.Length > 0 && firstPlayed == r.Name)
                firstPlayedIndex = i;

            candidates.Insert(0, i);
        }

        if (candidates.Count <= 0)
        {
            _currentRoundIndex = -1;
            return false;
        }

        // if we have more than one and the last played round is in our list, remove it
        if (candidates.Count > 1 && lastPlayedIndex != -1)
            candidates.Remove(lastPlayedIndex);

        // if this is the first round after a full restart, we still have more than one round in our list,
        // and the first played round (after the last full restart) is in our list, remove it
        if (_firstRoundAfterRestart && candidates.Count > 1 && firstPlayedIndex != -1)
            candidates.Remove(firstPlayedIndex);

        // pick one to play but try to avoid picking one that we have recently played if there are other rounds to play
        int index = random.RandomInt(0, candidates.Count - 1);

        // keep picking a round until we find one that's not a previously played round
        // or until we don't have any more rounds to choose from
        while (candidates.Count > 1 && roundRules.IsPreviouslyPlayedRound(_rounds[candidates[index]].Name))
        {
            candidates.RemoveAt(index); // we have played this round recently so get it out of the list
            index = random.RandomInt(0, candidates.Count - 1);
        }

        // pick one to play and fire its OnSelected output
        _currentRoundIndex = candidates[index];
        _rounds[_currentRoundIndex].SelectedToPlay();

        roundRules.SetRoundOverlayDetails();
        FireRoundStartOutput();

        if (!round.InWaitingForPlayers)
        {
            Log.Msg($"World triggered \"Mini_Round_Selected\" (round \"{_rounds[_currentRoundIndex].Name}\")\n");
            Log.Msg("World triggered \"Mini_Round_Start\"\n");
        }

        SetBaseControlPoints();
        RegisterRoundBeingPlayed();
        return true;
    }

    // ---- the win ------------------------------------------------------------

    public override void OnThink(string context)
    {
        if (context == ThinkContext)
            CpmThink();
    }

    /// <summary>CPMThink, every 0.2 s: the win check (a point's owner change also asks it at once).</summary>
    public void CpmThink()
    {
        if (!_disabled && rules.PointsMayBeCaptured)
            CheckWinConditions();

        // the next time we 'think'
        SetContextThink(ThinkContext, 0.2f);
    }

    /// <summary>CheckWinConditions (L638): the round being played, or every group of points.</summary>
    public void CheckWinConditions()
    {
        if (_disabled)
            return;

        if (_rounds.Count > 0)
        {
            if (_currentRoundIndex == -1)
                return;

            // Check the current round to see if one team is a winner yet.
            int winners = _rounds[_currentRoundIndex].CheckWinConditions();
            if (winners != -1 && winners >= (int)TFTeam.Red)
            {
                // are there any more rounds to play?
                bool forceMapReset = NumPlayableControlPointRounds() == 0;

                if (!forceMapReset)
                {
                    // we have more rounds to play
                    control.SetWinningTeam((TFTeam)winners, WinReason.AllPointsCaptured, forceMapReset);
                }
                else
                {
                    // we have played all of the available rounds
                    control.SetWinningTeam((TFTeam)winners, WinReason.AllPointsCaptured, forceMapReset, Vars.SwitchTeams);
                }

                FireTeamWinOutput(winners);
            }

            return;
        }

        // Check that the points aren't all held by one team...if they are
        // this will reset the round and will reset all the points.
        int winner = TeamOwnsAllPoints();
        // Stock's koth arm (L679-700) sits inside this decision: the winner
        // holds only once its koth timer has run out, so the win — and its
        // output — wait for WinMayStand.
        if (Vars.RestrictTeamCapWin != 1 && winner >= (int)TFTeam.Red && winner != Vars.RestrictTeamCapWin
            && koth.WinMayStand((TFTeam)winner))
        {
            control.SetWinningTeam((TFTeam)winner, WinReason.AllPointsCaptured, true, Vars.SwitchTeams);
            FireTeamWinOutput(winner);
        }
    }

    [ScriptedEvent] private void OnSetWinner(float team) => InternalSetWinner((int)team);

    [ScriptedEvent]
    private void OnSetWinnerAndForceCaps(float team)
    {
        int winner = (int)team;

        // Set all cap points in the current round to be owned by the winning team.
        foreach (var point in _points)
        {
            if (!PlayingMiniRounds || objectives.IsInMiniRound(point.PointIndex))
                point.ForceOwner(winner);
        }

        InternalSetWinner(winner);
    }

    private void InternalSetWinner(int team)
    {
        bool forceMapReset = true;

        // if we're playing rounds and there are more to play, don't do a full reset
        if (_rounds.Count > 0)
            forceMapReset = NumPlayableControlPointRounds() == 0;

        if (team == (int)TFTeam.Unassigned)
        {
            control.SetStalemate(StalemateReason.Timer, forceMapReset);
            return;
        }

        if (!forceMapReset)
            control.SetWinningTeam((TFTeam)team, WinReason.AllPointsCaptured, forceMapReset);
        else
            control.SetWinningTeam((TFTeam)team, WinReason.AllPointsCaptured, forceMapReset, Vars.SwitchTeams);

        FireTeamWinOutput(team);
    }

    private void FireTeamWinOutput(int team)
    {
        if (team == (int)TFTeam.Red)
            OnWonByTeam1.Fire();
        else if (team == (int)TFTeam.Blue)
            OnWonByTeam2.Fire();
    }

    public void FireRoundStartOutput() => CurrentRound?.FireOnStartOutput();

    public void FireRoundEndOutput()
    {
        if (CurrentRound is { } current)
        {
            current.FireOnEndOutput();
            _currentRoundIndex = -1;
        }
    }

    // ---- the round's inputs -------------------------------------------------

    [ScriptedEvent] private void OnEnable() => _disabled = false;

    [ScriptedEvent] private void OnDisable() => _disabled = true;

    [ScriptedEvent]
    private void OnRoundSpawn()
    {
        // clear out old control points
        _points.Clear();

        // find the control points, and if successful, do CPMThink
        if (FindControlPoints())
            SetContextThink(ThinkContext, 0.1f);

        // find the rounds (if the map has any)
        FindControlPointRounds();
        SetBaseControlPoints();

        objectives.ResetControlPoints();
    }

    [ScriptedEvent]
    private void OnRoundActivate()
    {
        // if we're using mini-rounds and haven't picked one yet, find one to play
        if (PlayingMiniRounds && CurrentRound is null)
            GetControlPointRoundToPlay();

        if (PlayingMiniRounds && CurrentRound is { } current)
        {
            // Tell the objective resource what control points are in use in the selected mini-round.
            foreach (var point in _points)
                objectives.SetInMiniRound(point.PointIndex, current.IsControlPointInRound(point));
        }
    }

    [ScriptedEvent]
    private void OnSetCapLayout(string layout)
    {
        _capLayout = layout;
        objectives.SetCapLayoutInHud(_capLayout);
    }

    [ScriptedEvent]
    private void OnSetCapLayoutCustomPositionX(float x)
    {
        _customPositionX = x;
        objectives.SetCapLayoutCustomPosition(_customPositionX, _customPositionY);
    }

    [ScriptedEvent]
    private void OnSetCapLayoutCustomPositionY(float y)
    {
        _customPositionY = y;
        objectives.SetCapLayoutCustomPosition(_customPositionX, _customPositionY);
    }

    // ---- ownership ----------------------------------------------------------

    /// <summary>TeamOwnsAllPoints (L986): the first group one team holds wholly, else TEAM_UNASSIGNED.</summary>
    public int TeamOwnsAllPoints(TeamControlPoint? overridePoint = null, int overrideNewTeam = (int)TFTeam.Unassigned)
        => TeamOwnership.OwnsAllPoints(_points, overridePoint, overrideNewTeam, Log);

    public bool WouldNewCPOwnerWinGame(TeamControlPoint point, int newOwner)
        => TeamOwnsAllPoints(point, newOwner) == newOwner;

    public bool IsBaseControlPoint(int pointIndex)
    {
        for (int team = (int)TFTeam.Red; team < ObjectiveLimits.Teams; team++)
        {
            if (GetBaseControlPoint(team) == pointIndex)
                return true;
        }
        return false;
    }

    /// <summary>GetBaseControlPoint (L1070): the team's end of the chain of points.</summary>
    public int GetBaseControlPoint(int team)
    {
        int result = -1;
        int lowestValue = 999;
        int highestValue = -1;
        TeamControlPoint? lowest = null;
        TeamControlPoint? highest = null;

        foreach (var point in _points)
        {
            if (!PlayingMiniRounds || (IsInRound(point) && team > (int)TFTeam.Spectator))
            {
                int value = point.PointIndex;
                if (value > highestValue)
                {
                    highestValue = value;
                    highest = point;
                }

                if (value < lowestValue)
                {
                    lowestValue = value;
                    lowest = point;
                }
            }
        }

        if (lowest is not null && highest is not null)
        {
            // which point is owned by this team?
            if ((lowest.DefaultOwner == team && highest.DefaultOwner == team) || highest.DefaultOwner == team)
                result = highestValue;
            else if (lowest.DefaultOwner == team)
                result = lowestValue;
        }

        return result;
    }

    /// <summary>
    /// CalcNumRoundsRemaining (L1157): how many more mini-rounds it takes the
    /// team to own every point, if it wins every one.
    /// </summary>
    public int CalcNumRoundsRemaining(int team)
    {
        if (_rounds.Count == 0)
            return 0;

        // start with all the control points the team currently owns
        var owned = _points.Where(p => p.Owner == team).ToList();
        int remaining = 0;

        // keep simulating what will happen next if this team keeps winning,
        // until it owns all the control points in the map
        while (owned.Count < _points.Count)
        {
            remaining++;
            int before = owned.Count;

            // choose the next highest-priority round that is playable
            foreach (var r in _rounds)
            {
                // see if one team owns all control points in this round
                int roundOwner = TeamControlPointRound.TeamInvalid;
                for (int i = 0; i < r.ControlPoints.Count; i++)
                {
                    var point = r.ControlPoints[i];
                    int pointOwner = owned.Contains(point) ? team : point.Owner;
                    if (i == 0)
                        roundOwner = pointOwner;
                    else if (roundOwner != pointOwner)
                        roundOwner = TeamControlPointRound.TeamInvalid;
                }

                // this round is playable if all control points are not owned by one team
                // (or owned by a team that can't win by capping them)
                bool playable = roundOwner < (int)TFTeam.Red || r.InvalidCapWinner == 1 || roundOwner == r.InvalidCapWinner;
                if (!playable)
                    continue;

                // Pretend this team played and won this round.
                foreach (var point in r.ControlPoints)
                {
                    if (!owned.Contains(point))
                        owned.Add(point);
                }

                break;
            }

            // Stock asserts progress here and would spin forever without it.
            if (owned.Count == before)
                break;
        }

        return remaining;
    }

    /// <summary>ListRounds (tf_listrounds).</summary>
    public string ListRounds()
        => PlayingMiniRounds
            ? "Rounds in this map:\n\n" + string.Concat(_rounds.Select(r => r.Name + "\n"))
            : "* No rounds in this map *\n";
}

/// <summary>The map keys <see cref="TeamControlPointMaster"/> declares (team_control_point_master.cpp:17-27).</summary>
[ScriptVars]
public partial class TeamControlPointMasterVars
{
    public bool StartDisabled { get; set; }
    [VarName("caplayout")] public string CapLayout { get; set; } = string.Empty;
    [VarName("cpm_restrict_team_cap_win")] public int RestrictTeamCapWin { get; set; }
    [VarName("switch_teams")] public bool SwitchTeams { get; set; }
    [VarName("score_style")] public bool ScoreStyle { get; set; }
    [VarName("play_all_rounds")] public bool PlayAllRounds { get; set; }
    [VarName("partial_cap_points_rate")] public float PartialCapPointsRate { get; set; }
    [VarName("custom_position_x")] public float CustomPositionX { get; set; } = -1f;
    [VarName("custom_position_y")] public float CustomPositionY { get; set; } = -1f;
}
