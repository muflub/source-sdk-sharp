namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The CTF objectives (plan_bots_teams_tf2 C.6, P6 step 6): fetch the enemy's flag, deliver it,
/// escort a teammate carrying it, chase the enemy carrying the bot's own, guard it dropped and
/// defend it at home. Everything is read off the HUD's objectives (<see cref="PerceivedView.Objectives"/>),
/// which every player sees: where each flag is, who carries it (a slot) and each team's capture zone.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>Fetch's weight for an attacker far from the flag and right on it; a defender's is a fraction.</summary>
    public const float FetchFar = 0.7f, FetchNear = 0.9f, FetchReach = 3000f, DefenderFetch = 0.15f;

    /// <summary>Defend's weight for a defender and for an attacker with nothing better to do.</summary>
    public const float DefenderDefend = 0.5f, AttackerDefend = 0.05f;

    /// <summary>Escort's weight for an attacker and for a defender.</summary>
    public const float AttackerEscort = 0.6f, DefenderEscort = 0.3f;

    /// <summary>Chasing the enemy carrying the bot's flag, and guarding it dropped.</summary>
    public const float ChaseCarrierWeight = 0.85f, GuardWeight = 0.7f, GuardReach = 2000f;

    private Objective? _ownFlag, _enemyFlag, _ownZone;

    /// <summary>
    /// The CTF team-play policy (P6 step 3), by a team's size: how many of it defend, and how many
    /// may escort a teammate carrying the enemy's flag; the rest attack. A third defend (none at two a
    /// side, one at four, two at six, as Part G's <c>RoleSplits.ThirdDefend</c>), and every other bot
    /// but the carrier escorts. A <see cref="RoleSplit"/> as it is, for Part G's G2 role allocator
    /// (<see cref="RoleDeal"/>) to take when its call sites land.
    /// Before it a team's defenders were the bots whose slot was a multiple of three, whichever team
    /// the slot joined: live at 4v4 on ctf_2fort, red's four were slots 3, 5, 6 and 9, three
    /// defenders and one attacker, and blue had none.
    /// </summary>
    public static RoleCounts CtfRoleSplit(int teamSize)
    {
        int defend = Math.Max(0, teamSize) / 3;
        return new RoleCounts(defend, Math.Max(0, teamSize - defend - 1));
    }

    /// <summary>Picks out this decide's three objectives the CTF branches read: the team's flag, the flag it may take and its own capture zone.</summary>
    private void ReadObjectives()
    {
        _ownFlag = _enemyFlag = _ownZone = null;
        if (_view.Objectives is not { } all || !TeamRule.IsGameTeam(_self.Team))
            return;
        int mine = 1 << _self.Team;
        foreach (Objective o in all.All)
        {
            switch (o.Kind)
            {
                case ObjectiveKind.Carry when o.OwnerTeam == _self.Team:
                    _ownFlag ??= o;
                    break;
                case ObjectiveKind.Carry when (o.MayAct & mine) != 0 && o.State != ObjectiveState.Locked:
                    _enemyFlag ??= o;
                    break;
                case ObjectiveKind.Deliver when o.OwnerTeam == _self.Team && o.State != ObjectiveState.Locked:
                    _ownZone ??= o;
                    break;
            }
        }
    }

    /// <summary>The bot itself carries the enemy's flag (the HUD's carried icon names it).</summary>
    public bool Carrying => _enemyFlag is { State: ObjectiveState.Carried } f && f.CarrierSlot == _bot.Slot;

    private bool TeammateCarries => _enemyFlag is { State: ObjectiveState.Carried } f && f.CarrierSlot != _bot.Slot && f.CarrierSlot != 0;

    private bool EnemyCarriesOurs => _ownFlag is { State: ObjectiveState.Carried };

    private bool OursDropped => _ownFlag is { State: ObjectiveState.Dropped };

    /// <summary>
    /// Chasing the enemy carrying the bot's flag: a defender always; an attacker only for a carrier
    /// within <see cref="AttackerChaseReach"/> of it, else it goes on for the enemy's flag, as stock's
    /// CTF bots all go on fetching (CTFBotScenarioMonitor). Live on ctf_2fort (8 bots, ten minutes)
    /// every bot of a team turned back for each of nine carriers, and none of them got home.
    /// </summary>
    private float ChaseCarrierUtility()
    {
        if (!EnemyCarriesOurs)
            return 0f;
        return Role == TFTeamRole.Defend || _self.Origin.DistanceTo(_ownFlag!.Value.Position) < AttackerChaseReach ? ChaseCarrierWeight : 0f;
    }

    /// <summary>How near the enemy carrying its flag an attacker turns back for it.</summary>
    public const float AttackerChaseReach = 1500f;

    /// <summary>
    /// Guarding the bot's flag dropped: a defender's job only (round 2's S14). An attacker goes on for the enemy's flag, as
    /// every stock CTF bot but the carrier does (<c>CTFBotFetchFlag</c>, <c>tf_bot_fetch_flag.cpp</c>: stock has no guard). Live
    /// on ctf_2fort (8 Soldiers, six runs of 300 s at 7e79f0290c) attackers within <see cref="GuardReach"/> turned back and
    /// stood on the dropped flag until it went home: 38 s and 37 s for two of them at once in red's basement.
    /// </summary>
    private float GuardUtility()
        => OursDropped && Role == TFTeamRole.Defend && _self.Origin.DistanceTo(_ownFlag!.Value.Position) < GuardReach ? GuardWeight : 0f;

    private float EscortUtility() => TeammateCarries ? (Role == TFTeamRole.Attack ? AttackerEscort : DefenderEscort) : 0f;

    private float FetchUtility()
    {
        if (_enemyFlag is not { } f || f.State is ObjectiveState.Carried or ObjectiveState.Won)
            return 0f;
        if (Role == TFTeamRole.Defend)
            return DefenderFetch;
        // Nearer the flag, the run is worth more than a fight on the way.
        float near = 1f - Math.Clamp(_self.Origin.DistanceTo(f.Position) / FetchReach, 0f, 1f);
        return FetchFar + (FetchNear - FetchFar) * near;
    }

    private float DefendUtility()
        => _ownFlag is { State: ObjectiveState.Home } ? (Role == TFTeamRole.Defend ? DefenderDefend : AttackerDefend) : 0f;

    private bool GoDeliver()
    {
        if (_ownZone is not { } zone)
            return false;
        GoTo(zone.Position, MoveSpeed.Run);
        _verdict = "carrying the flag home";
        return true;
    }

    private bool GoChaseCarrier()
    {
        if (_ownFlag is not { } flag)
            return false;
        GoTo(flag.Position, MoveSpeed.Run);
        _verdict = "after our flag";
        return true;
    }

    private bool GoGuard()
    {
        if (_ownFlag is not { } flag)
            return false;
        GoTo(flag.Position, MoveSpeed.Run);
        _verdict = "our flag is down";
        return true;
    }

    /// <summary>Behind the carrier, between it and its own way back: it walks to the carrier, stopping short.</summary>
    private bool GoEscort()
    {
        if (_enemyFlag is not { } flag)
            return false;
        AiVector at = flag.Position;
        if (_self.Origin.DistanceTo(at) < EscortGap)
        {
            _hasGoal = true;
            _goal = at;
            _verdict = "with the carrier";
            return true;
        }

        GoTo(at, MoveSpeed.Run);
        _verdict = "to the carrier";
        return true;
    }

    private bool GoFetch()
    {
        if (_enemyFlag is not { } flag)
            return false;
        GoTo(FetchGoal(flag), MoveSpeed.Run);
        _verdict = flag.State == ObjectiveState.Dropped ? "their flag is down" : "for their flag";
        return true;
    }

    /// <summary>
    /// Near the flag: every <see cref="DefendSpotSeconds"/> a spot drawn anew, one where its side's players held
    /// (<see cref="PickLearnedHold"/>) or, as before, a point within <see cref="DefendRadius"/> of the flag. Holding a learned
    /// spot it faces the way they faced there.
    /// </summary>
    private bool GoDefend()
    {
        if (_ownFlag is not { } flag)
            return false;
        float limit = _defendLearned ? HoldReach * 1.5f : DefendRadius * 1.5f;
        if (!_defendPicked || _now >= _defendUntil || _defendSpot.DistanceTo(flag.Position) > limit)
        {
            if (!PickLearnedHold(flag.Position))
            {
                float a = _rng.NextFloat() * MathF.Tau, r = DefendRadius * MathF.Sqrt(_rng.NextFloat());
                _defendSpot = OnFootNear(flag.Position, flag.Position + new AiVector(MathF.Cos(a) * r, MathF.Sin(a) * r, 0f));
                _defendLearned = false;
            }

            _defendUntil = _now + (int)(DefendSpotSeconds / _tick);
            _defendPicked = true;
        }

        if (_self.Origin.DistanceTo(_defendSpot) < ArriveUnits)
        {
            _hasGoal = true;
            _goal = _defendSpot;
            _verdict = "holding at our flag";
            if (_defendLearned)
                FaceHeldYaw();
            return true;
        }

        GoTo(_defendSpot, MoveSpeed.Run);
        _verdict = "to our flag";
        return true;
    }

    /// <summary>How far from its flag, on foot in the graph's cost, a drawn defend spot may be: within the spot's limit as the crow flies.</summary>
    public const float DefendWalk = DefendRadius * 1.25f;

    private readonly List<int> _defendCells = new();
    private readonly HashSet<int> _defendCellSet = new();
    private AiVector _defendCellsFlag;
    private bool _defendCellsKnown;

    /// <summary>
    /// A drawn defend spot where the bot gets to it from <paramref name="flag"/> on foot (round 2's S14): the spot itself, on
    /// its cell's floor, on a cell within <see cref="DefendWalk"/> of the flag's (<see cref="NavGraph.CellsWithin"/>), else the centre of the nearest such
    /// cell; the spot as drawn with no graph or no cell by the flag. Live on ctf_2fort, a spot 384 u round blue's flag
    /// at (740 -3158) was over a floor 270 u under the intel room with no way to it: the defender stood 17 s at the room's
    /// wall, in every run of the same seed. The cells are worked out once for a flag's place.
    /// </summary>
    private AiVector OnFootNear(AiVector flag, AiVector spot)
    {
        if (_graph is null)
            return spot;
        var up = new AiVector(0f, 0f, 1f);
        if (!_defendCellsKnown || _defendCellsFlag.DistanceTo(flag) > 1f)
        {
            _graph.CellsWithin(_graph.FindCell(flag + up), DefendWalk, ClearanceClass.Crouch, new NavObstacleState(_graph.ObstacleCount), _defendCells);
            _defendCellSet.Clear();
            _defendCellSet.UnionWith(_defendCells);
            (_defendCellsFlag, _defendCellsKnown) = (flag, true);
        }

        if (_defendCells.Count == 0)
            return spot;
        CellId under = _graph.FindCell(spot + up);
        // Kept, on its cell's floor: drawn at the flag's height, a spot over a floor 72 u or more under it is never arrived at.
        if (under.IsValid && _defendCellSet.Contains(under.Index))
            return spot with { Z = _graph.Cell(under).FloorZ };
        AiVector best = spot;
        float bestD = float.PositiveInfinity;
        foreach (int c in _defendCells)
        {
            AiVector centre = _graph.Cell(c).Centre;
            float d = centre.DistanceTo(spot);
            if (d < bestD)
                (bestD, best) = (d, centre);
        }

        return best;
    }
}
