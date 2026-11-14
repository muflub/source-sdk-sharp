namespace SourceSharp.Ai.TF2.Trees;

/// <summary>A try of a way into the enemy's flag room, as the bot hands it to the store: its side and class, the door's key, the score, and whether it was drawn.</summary>
public readonly record struct FamilyTry(int Team, int Class, long Choice, double Score, bool Drawn);

/// <summary>
/// The ways into the enemy's flag room and what came of them (plan_bots_teams_tf2_part_g G.4's "outcomes", lane G4b).
///
/// <para>A family is a door of the room (<see cref="RouteFamilies"/>, found once for the graph and the flag's home). A try is
/// an attacker's run at the flag at home, from its first fetch to its pickup or its death: its door is the first it passed
/// through (else the one it drew), its score <see cref="PickedUpScore"/> for the flag picked up, <see cref="RoomScore"/> for a
/// death within <see cref="RoomReach"/> of the flag, <see cref="DoorScore"/> for one after the door, 0 for one before. A run
/// that ends any other way (the flag leaves home, the bot is dealt defence) is no try. Tries are kept only while the store is
/// on (its snapshot has a key), for the binding to hand to it (<see cref="FamilyTries"/>).</para>
///
/// <para>Where the snapshot has tries of its side's doors, an attacker draws a door once a run, at its Cunning's chance
/// (<see cref="DecisionParams.FamilyTrust"/>), by seeded Thompson sampling (<see cref="OutcomeSampler"/>), and is held to it
/// by its goal: it goes to the door first, then to the flag, one more field build a run. With no such tries nothing is
/// drawn: the bot goes as before, and its tries still teach the store.</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The outcome kind a route family is written as.</summary>
    public const int RouteFamilyKind = 1;

    /// <summary>The scores of a try by how far it got.</summary>
    public const double PickedUpScore = 1d, RoomScore = 0.5d, DoorScore = 0.25d;

    /// <summary>A death this near the flag was in its room.</summary>
    public const float RoomReach = 512f;

    private AiRandom _familyRng;
    private RouteFamilies? _families;
    private AiVector _familiesFlag;
    private readonly List<FamilyTry> _tries = new();
    private bool _tryOn, _viaDone, _tryRoom;
    private int _tryDrawn = -1, _tryPassed = -1, _tryTeam, _tryClass;
    private int[] _familyDraws = [];

    /// <summary>The tries this bot finished, oldest first; the binding reads them by a count it keeps.</summary>
    public IReadOnlyList<FamilyTry> FamilyTries => _tries;

    /// <summary>The doors of the enemy's flag room the bot last found (null before it needed them).</summary>
    public RouteFamilies? Families => _families;

    /// <summary>How often the bot drew each door (by door index), runs begun, and runs that went as before.</summary>
    public IReadOnlyList<int> FamilyDraws => _familyDraws;

    public int FamilyRuns { get; private set; }

    /// <summary>The side of the bot's last run: whose enemy's flag room <see cref="Families"/> is of.</summary>
    public int FamilyTeam => _tryTeam;

    /// <summary>
    /// A fetch decide: a run at the flag at home begins (and its door is drawn) when none is on. Answers the point to go to:
    /// the drawn door until the bot has come through it, else the flag.
    /// </summary>
    private AiVector FetchGoal(Objective flag)
    {
        if (flag.State != ObjectiveState.Home || Role != TFTeamRole.Attack || _graph is null || !_memory.World.Experience.Key.IsValid)
            return flag.Position;
        if (!_tryOn)
            BeginTry(flag.Position);
        if (_tryDrawn < 0 || _viaDone || _families is null)
            return flag.Position;
        return _families.Doors[_tryDrawn].At;
    }

    private void BeginTry(AiVector flag)
    {
        if (_families is null || !ReferenceEquals(_families.Graph, _graph) || _familiesFlag.DistanceTo(flag) > 1f)
        {
            CellId cell = _graph!.FindCell(flag + new AiVector(0f, 0f, 1f));
            _families = RouteFamilies.Around(_graph, cell.IsValid ? cell.Index : -1);
            _familiesFlag = flag;
            _familyDraws = new int[_families.Doors.Count];
        }

        (_tryOn, _viaDone, _tryRoom, _tryDrawn, _tryPassed, _tryTeam, _tryClass) = (true, false, false, -1, -1, _self.Team, (int)_class);
        FamilyRuns++;
        if (_families.Doors.Count < 2)
            return;
        MapExperience snapshot = _memory.World.Experience;
        if (snapshot.Outcomes.Count == 0)
            return;
        var context = new ExperienceContext(ExperienceContext.Any, _self.Team, ExperienceContext.Any);
        Span<OutcomeStat> stats = stackalloc OutcomeStat[_families.Doors.Count];
        bool any = false;
        foreach ((OutcomeKey k, OutcomeStat v) in snapshot.Outcomes)
        {
            if (k.Kind != RouteFamilyKind || !context.Takes(k.Context))
                continue;
            int door = _families.IndexOf(WorldBin.Unpack(k.Choice));
            if (door < 0)
                continue;
            stats[door] = stats[door].Plus(v);
            any = true;
        }

        if (!any || _familyRng.NextFloat() >= _p.FamilyTrust)
            return;
        _tryDrawn = OutcomeSampler.Choose(stats, _familyRng);
        if (_tryDrawn >= 0)
            _familyDraws[_tryDrawn]++;
    }

    /// <summary>
    /// Once a decide alive, after the tree: notes the door the run came through and whether it reached the room; ends it
    /// with a pickup, or with nothing when the flag left home or the bot was dealt defence.
    /// </summary>
    private void FollowTry()
    {
        if (!_tryOn || _families is null)
            return;
        if (Carrying)
        {
            EndTry(PickedUpScore);
            return;
        }

        if (_enemyFlag is not { State: ObjectiveState.Home } flag || Role != TFTeamRole.Attack)
        {
            _tryOn = false;
            return;
        }

        int passing = _families.Passing(_self.Origin);
        if (passing >= 0)
        {
            _tryPassed = _tryPassed < 0 ? passing : _tryPassed;
            _viaDone |= passing == _tryDrawn;
        }

        _tryRoom |= _self.Origin.DistanceTo(flag.Position) <= RoomReach;
    }

    /// <summary>The bot died: a run on ends with what it got to.</summary>
    private void DiedOnTry()
    {
        if (_tryOn)
            EndTry(_tryRoom ? RoomScore : _tryPassed >= 0 ? DoorScore : 0d);
    }

    private void EndTry(double score)
    {
        _tryOn = false;
        int door = _tryPassed >= 0 ? _tryPassed : _tryDrawn;
        if (door < 0 || _families is null)
            return;
        _tries.Add(new FamilyTry(_tryTeam, _tryClass, _families.Doors[door].Bin.Packed, score, _tryDrawn >= 0));
    }
}
