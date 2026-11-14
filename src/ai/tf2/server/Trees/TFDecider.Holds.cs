namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// Where its side's players held by its flag (plan_bots_teams_tf2_part_g G.4's "spots", lane G4b): a defender picking a
/// spot to hold takes, by its Discipline (<see cref="DecisionParams.SpotTrust"/>), one of the places its team's players
/// held within <see cref="HoldReach"/> of the flag, drawn by what holding there earned (<see cref="ExperienceHolds"/>),
/// and faces the way they faced there while it holds. With no such place it draws its point as before, from its own
/// stream as before: the learned pick has a stream of its own.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>How near its flag a learned hold may be: the flag room and the ways into it.</summary>
    public const float HoldReach = 1024f;

    private AiRandom _holdRng;
    private ExperienceHolds? _holds;
    private bool _defendLearned;
    private float _defendYaw;

    /// <summary>Learned holds picked, and decides spent holding one, for the facts and the counters.</summary>
    public int LearnedHoldPicks { get; private set; }

    public int LearnedHoldDecides { get; private set; }

    /// <summary>The hold spots the bot's side has in the snapshot it last read (null before it needed them).</summary>
    internal ExperienceHolds? Holds => _holds;

    /// <summary>
    /// Whether the bot takes a place its side held near <paramref name="flag"/>: only where the snapshot has one, then at its
    /// Discipline's chance, the place drawn by worth. Nothing is drawn where there is none.
    /// </summary>
    private bool PickLearnedHold(AiVector flag)
    {
        MapExperience snapshot = _memory.World.Experience;
        if (snapshot.Spots.Count == 0)
            return false;
        if (_holds is null || !ReferenceEquals(_holds.Snapshot, snapshot) || _holds.Role != _self.Team)
            _holds = ExperienceHolds.Of(snapshot, _self.Team, (int)Nav.TFSpotKind.Hold);
        IReadOnlyList<HoldSpot> near = _holds.Near(flag, HoldReach);
        if (near.Count == 0 || _holdRng.NextFloat() >= _p.SpotTrust || ExperienceHolds.Draw(near, _holdRng) is not { } spot)
            return false;
        (_defendSpot, _defendYaw, _defendLearned) = (spot.At, spot.Yaw, true);
        LearnedHoldPicks++;
        return true;
    }

    /// <summary>At a learned hold: the look along the yaw its side faced there, at eye height.</summary>
    private void FaceHeldYaw()
    {
        float r = _defendYaw * (MathF.PI / 180f);
        AiVector eye = OwnEye;
        _look = new LookIntent(LookKind.Point, eye + new AiVector(MathF.Cos(r) * LearnedLook.Reach, MathF.Sin(r) * LearnedLook.Reach, 0f),
                               ActorId.None, AimPoint.Body, 0f, 0);
        LearnedHoldDecides++;
    }
}
