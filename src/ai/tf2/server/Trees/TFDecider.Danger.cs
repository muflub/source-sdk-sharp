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
}
