namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// Where its side keeps dying from (plan_bots_teams_tf2_part_g G.4's "danger places", lane G4b): on its way with nobody in
/// sight, a bot coming into a spot where its team's players were killed from one place more than from anywhere else checks
/// that place (<see cref="DangerLook"/>), by its Alertness (<see cref="DecisionParams.DangerLookChance"/>, how often;
/// <see cref="DecisionParams.DangerLookSeconds"/>, how long). It comes after the tree, the hands and the reflexes and before
/// the glance and the learned look, so a target in sight comes first and a check comes before a glance or an idle look; it
/// does not check while it goes to look into a sound. The table is the snapshot's, read once a snapshot
/// (<see cref="ExperienceDangers"/>); with no sight line in the snapshot nothing here runs.
/// </summary>
public sealed partial class TFDecider
{
    private DangerLook _dangerLook;
    private ExperienceDangers? _dangers;

    /// <summary>Decides on which the bot checked a danger place, for the facts and the counters.</summary>
    public int DangerLookDecides { get; private set; }

    /// <summary>The danger look's entries and checks, for the facts and the counters.</summary>
    public DangerLook DangerLooks => _dangerLook;

    /// <summary>The danger places the bot's side has in the snapshot it last read (null before it needed them).</summary>
    internal ExperienceDangers? Dangers => _dangers;

    /// <summary>
    /// Makes the experience uses' own streams from the seed (lane G4b: the danger look's, the learned hold's, the route family's), so a use with
    /// rows to act on moves no other stream: the constructor's, once.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_dangerLook), nameof(_holdRng), nameof(_familyRng))]
    private void StartExperience(ulong seed)
    {
        _dangerLook = new DangerLook(new AiRandom(seed ^ 0x5446_444EUL));
        _holdRng = new AiRandom(seed ^ 0x5446_484CUL);
        _familyRng = new AiRandom(seed ^ 0x5446_4652UL);
    }

    /// <summary>
    /// A look still along the path, with nobody in sight, a goal, no jump under way and no sound being looked into: where
    /// the bot comes into a bin with a danger place it may check the place for a while.
    /// </summary>
    private void LookAtDanger()
    {
        MapExperience snapshot = _memory.World.Experience;
        if (snapshot.Sights.Count == 0)
            return;
        if (_targetSighted || !_hasGoal || Busy || _look.Kind != LookKind.Path || _root.Chosen?.Name == Search)
        {
            _dangerLook.Stop();
            return;
        }

        if (_dangers is null || !ReferenceEquals(_dangers.Snapshot, snapshot) || _dangers.Role != _self.Team)
            _dangers = ExperienceDangers.Of(snapshot, _self.Team);
        if (!_dangerLook.Step(_now, _tick, _p.DangerLookChance, _p.DangerLookSeconds, _dangers, _self.Origin))
            return;
        _look = new LookIntent(LookKind.CornerCheck, _dangerLook.At, ActorId.None, AimPoint.Body, 0f, 0);
        DangerLookDecides++;
    }

    // ------------------------------------------------------------------ the danger shot (lane Gp)

    /// <summary>A teammate's death is placed by its scream heard this near in time to the kill feed, or by the teammate seen this lately.</summary>
    public const float DeathHeardSeconds = 1f, DeathSeenSeconds = 2f;

    /// <summary>A rocket at the place a teammate was just killed from is worth this long after the kill feed said so.</summary>
    public const float DangerShotSeconds = 8f;

    private readonly bool[] _deadBefore = new bool[64];
    private bool _dangerPending;
    private AiVector _dangerAt;
    private Tick _dangerDeathAt;

    /// <summary>Teammates the kill feed said died, those whose place the bot knew (heard or seen), and those whose place the store has a killer place for.</summary>
    public int TeammateDeaths { get; private set; }

    public int DeathsPlaced { get; private set; }

    public int DeathsAtDanger { get; private set; }

    /// <summary>
    /// Every decide: a teammate the kill feed has just marked dead is a death of this round, placed where its scream was heard
    /// (within <see cref="DeathHeardSeconds"/>, with the hearing's own error) or where the bot last saw it (within
    /// <see cref="DeathSeenSeconds"/>); never where the game says it died. A placed death in a bin the store says its side
    /// keeps dying in (<see cref="ExperienceDangers"/>, the bin or one next to it) makes the place the deaths come from a
    /// danger shot's for <see cref="DangerShotSeconds"/>.
    /// </summary>
    private void NoteTeammateDeaths()
    {
        Blackboard board = _memory.Board;
        int n = Math.Min(board.Slots, _deadBefore.Length);
        for (int s = 0; s < n; s++)
        {
            KnownActor a = board.Actor(s);
            bool dead = a.Known && a.Dead && _memory.IsFriend(a) && a.Id != _bot;
            bool fresh = dead && !_deadBefore[s];
            _deadBefore[s] = dead;
            if (!fresh)
                continue;
            TeammateDeaths++;
            if (!DeathPlace(a, out AiVector where))
                continue;
            DeathsPlaced++;
            if (DangerFor(where) is not { } place)
                continue;
            DeathsAtDanger++;
            (_dangerPending, _dangerAt, _dangerDeathAt) = (true, place.At, _now);
        }

        if (_dangerPending && (_now - _dangerDeathAt) * _tick > DangerShotSeconds)
            _dangerPending = false;
    }

    private bool DeathPlace(KnownActor a, out AiVector where)
    {
        where = default;
        if (a.EverHeard && a.LastHeardKind == SoundKind.Death && (_now - a.LastHeardTick) * _tick <= DeathHeardSeconds)
            where = a.LastHeardPos;
        else if (a.EverSeen && (_now - a.LastSeenTick) * _tick <= DeathSeenSeconds)
            where = a.LastSeenPos;
        else
            return false;
        return true;
    }

    /// <summary>The store's killer place for deaths at <paramref name="where"/>'s bin or one next to it (the most deaths), or none.</summary>
    private DangerPlace? DangerFor(AiVector where)
    {
        MapExperience snapshot = _memory.World.Experience;
        if (snapshot.Sights.Count == 0)
            return null;
        if (_dangers is null || !ReferenceEquals(_dangers.Snapshot, snapshot) || _dangers.Role != _self.Team)
            _dangers = ExperienceDangers.Of(snapshot, _self.Team);
        WorldBin bin = WorldBin.Of(where);
        DangerPlace? best = null;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (_dangers.At(new WorldBin(bin.X + dx, bin.Y + dy, bin.Z + dz)) is { } p && (best is null || p.Deaths > best.Value.Deaths))
                        best = p;
                }
            }
        }

        return best;
    }
}
