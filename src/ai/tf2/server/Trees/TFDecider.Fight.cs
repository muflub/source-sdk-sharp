namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// How a fighting Soldier moves (lane Gfight; the user, 2026-11-15, of bots on cp_granary and
/// cp_badlands: "and not move while shooting"). The fight's leaves keep their range as before
/// (closer past <see cref="FightFar"/>, back inside <see cref="FightNear"/>); around that:
/// <list type="bullet">
/// <item><b>a sidestep nearly all the time</b>: the core's <see cref="StrafeRhythm"/> by Agility's
/// curves, where a stretch with no sidestep stands. HL2MP's share of stepping stretches is the curve's
/// <c>strafeWeight</c> (0.3 at Medium), so a Medium Soldier stood still for 70 % of its stretches in
/// range; TF2's lifts it by <see cref="FightStepLift"/> of what the curve leaves (Medium 0.83), since
/// a Soldier who stands takes a rocket's whole blast, and a person moves for most of a fight (the
/// user's own sessions: 66 % of firing ticks over half a run).</item>
/// <item><b>a change of side at a person's intervals</b>: a stretch is held at least
/// <see cref="FightStepLeastSeconds"/> and mostly reverses (<see cref="FightStepReverse"/>), its mean the
/// curve's; HL2MP's coin kept the side half the time.</item>
/// <item><b>never into a wall</b> (lane Gmove, 2026-11-16): a side with no floor within <see cref="FightWallUnits"/>
/// on the graph, a wall or a drop (the core's <see cref="SideRoom"/>), turns to the other side when that has room
/// (no hop: that is for the rhythm's own reversals); with no room either side, holding range, the stretch steps
/// along the line to the target. On cp_granary over three runs of 300 s, of the firing ticks a sidestep was sent
/// and the body was under 120 u/s, 28 % had a wall or a drop a step to that side.</item>
/// <item><b>a hop now and then</b>: a reversal begins with a jump the way it goes at a share by
/// Agility (<see cref="FightHopMost"/> × the step share), on the ground only.</item>
/// <item><b>a crouch now and then</b>: holding range on the ground, the core's <see cref="CrouchRhythm"/>
/// at Cover's <c>crouchShare</c>.</item>
/// <item><b>cover to load in</b>: a launcher dry with the target in sight goes to the nearest place the
/// target's cell does not see in the map's sight table (the core's <see cref="CoverSearch"/>, within
/// Cover's <c>coverWeight</c> × <see cref="FightCoverReach"/>), else backs off sidestepping.</item>
/// </list>
/// A rocket it sees coming it steps out of as before (TFDecider.Things.cs, the core's
/// <see cref="BlastDodge"/>): the reflex has the body over all of this.
/// </summary>
public sealed partial class TFDecider
{
    // With the fight switch off (ai_tf_feel_fight 0) the leaves take the old Strafe (TFDecider.cs), as before the lane.

    /// <summary>Of the stretches the Agility curve leaves without a sidestep, this share step anyway: a Soldier's own.</summary>
    public const float FightStepLift = 0.75f;

    /// <summary>The least a sidestep holds its side, and the chance a new stretch goes the other way.</summary>
    public const float FightStepLeastSeconds = 0.25f, FightStepReverse = 0.8f;

    /// <summary>The share of a reversal that begins with a hop, at the most step share; scaled down by it.</summary>
    public const float FightHopMost = 0.4f;

    /// <summary>How far a hop is aimed along the sidestep.</summary>
    public const float FightHopUnits = 64f;

    /// <summary>How far along the graph a bot of <c>coverWeight</c> 1 goes to load in cover: HL2MP's reach.</summary>
    public const float FightCoverReach = 512f;

    /// <summary>The verdicts of the fight's own moves.</summary>
    public const string CoverVerdict = "loading in cover", HopVerdict = "hop";

    private StrafeRhythm _fightStep = null!;
    private CrouchRhythm _fightCrouch = null!;
    private AiRandom _hopRng = null!;
    private CoverSearch? _cover;
    private NavObstacleState? _sideDoors;
    private int _stepChanges;

    /// <summary>Hops taken in a fight, crouched decides, and cover places gone to: for the facts.</summary>
    public int FightHops { get; private set; }

    public int FightCrouches { get; private set; }

    public int CoverMoves { get; private set; }

    /// <summary>Sidesteps turned round at a wall or a drop, and ones turned along the line to the target with no room either side: for the facts.</summary>
    public int WallTurns { get; private set; }

    public int WallAlongs { get; private set; }

    /// <summary>
    /// How far a sidestep looks for a wall or a drop: half a step. The lane's first rule looked a whole step
    /// (<see cref="SideRoom.StepUnits"/>) and live it turned and stood too often: in a doorway's 96 u both sides of a
    /// centred body lie within a step.
    /// </summary>
    public const float FightWallUnits = 16f;

    /// <summary>The fight's sidesteps, for the facts: stretches drawn and changes of side.</summary>
    public StrafeRhythm FightSteps => _fightStep;

    /// <summary>The fight's parts, each with a stream of its own from the bot's seed. The constructor's, once.</summary>
    private void StartFight(ulong seed)
    {
        _fightStep = new StrafeRhythm(new AiRandom(seed ^ 0x5446_5354UL));
        _fightCrouch = new CrouchRhythm(new AiRandom(seed ^ 0x5446_4352UL));
        _hopRng = new AiRandom(seed ^ 0x5446_4850UL);
        _cover = _graph is null ? null : new CoverSearch(_graph.CellCount);
        _sideDoors = _graph is null ? null : new NavObstacleState(_graph.ObstacleCount);
    }

    /// <summary>The share of a fight's stretches that sidestep: the curve's, lifted by <see cref="FightStepLift"/> of what it leaves.</summary>
    public static float FightStepWeight(in DecisionParams p) => p.StrafeWeight + (1f - p.StrafeWeight) * FightStepLift;

    /// <summary>
    /// A fight's move: the sidestep of this stretch mixed with <paramref name="approach"/> (toward the target +,
    /// away −), a hop at a reversal now and then, a crouch while holding range.
    /// </summary>
    private void FightStep(float approach)
    {
        float weight = FightStepWeight(in _p);
        int side = _fightStep.Step(_now, _tick, weight, Math.Max(0.2f, _p.StrafeMeanSeconds), new StrafeStyle(FightStepLeastSeconds, FightStepReverse));
        // A change of side the rhythm drew; a wall's turn below begins no hop (a hop from a stand at a wall is still air).
        bool reversed = _fightStep.Changes != _stepChanges;
        AiVector to = new AiVector(_targetPos.X - _self.Origin.X, _targetPos.Y - _self.Origin.Y, 0f).Normalised();
        var across = new AiVector(-to.Y, to.X, 0f);
        float along = 0f;
        if (side != 0 && NoRoom(across * side))
        {
            if (!NoRoom(across * -side))
            {
                _fightStep.Turn();
                side = -side;
                reversed = false;
                WallTurns++;
            }
            else if (approach != 0f || !NoRoom(to * side))
            {
                // No room either side: closing or backing off goes on alone; holding range the stretch steps along the
                // line to the target, its side choosing nearer or further, as one in a doorway does. With no room that
                // way either, it pushes as drawn.
                along = approach != 0f ? 0f : side;
                side = 0;
                WallAlongs++;
            }
        }

        _stepChanges = _fightStep.Changes;
        AiVector dir = to * (approach + along) + across * side;
        _move = dir.Length < 0.05f ? MoveIntent.Hold : new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, dir.Normalised(), false, 0);

        bool grounded = (_self.Flags & SelfFlags.Grounded) != 0 && (_self.Flags & SelfFlags.Ducking) == 0;
        // Every reversal draws, so the stream does not depend on the ground.
        if (reversed && side != 0 && _hopRng.NextFloat() < FightHopMost * weight && grounded && !Busy)
        {
            _move = new MoveIntent(MoveKind.Jump, MoveSpeed.Run, 1, _self.Origin + dir.Normalised() * FightHopUnits, default, default, default, default, false, 0);
            FightHops++;
            return;
        }

        bool fit = _targetSighted && approach == 0f && grounded && !Busy && _targetDist > FightNear && MathF.Abs(_self.Velocity.Z) <= CrouchAirSpeed;
        if (!_fightCrouch.Step(_now, _tick, fit, _p.CrouchShare))
            return;
        FightCrouches++;
        _move = _move.Kind == MoveKind.Strafe ? _move with { CrouchOn = true } : MoveIntent.Hold with { Kind = MoveKind.Crouch, CrouchOn = true };
    }

    /// <summary>Whether a step along <paramref name="way"/> from the bot's feet meets a wall or a drop on the graph (every door open); with no graph, never.</summary>
    private bool NoRoom(AiVector way)
        => _graph is not null && _sideDoors is not null
           && SideRoom.Ahead(_graph, _sideDoors, _self.Origin, way, ClearanceClass.Crouch, FightWallUnits) is SideFloor.Wall or SideFloor.Drop;

    /// <summary>Up or down at more than this, the body is in the air: no crouch.</summary>
    public const float CrouchAirSpeed = 8f;

    /// <summary>
    /// The launcher dry with the target in sight: to the nearest place the target does not see, within the
    /// bot's Cover reach, to load there; with none, back off sidestepping.
    /// </summary>
    private void LoadInCover()
    {
        float reach = FightCoverReach * _p.CoverWeight;
        if (_cover is not null && _graph is not null && _memory.World.Sight is { } sight && reach > 0f && _targetDist <= sight.Range
            && _cover.Find(_graph, sight, _self.Origin, _targetPos, reach, out AiVector at))
        {
            GoTo(at, MoveSpeed.Run);
            CoverMoves++;
            _verdict = CoverVerdict;
            return;
        }

        FightStep(-0.6f);
    }
}
