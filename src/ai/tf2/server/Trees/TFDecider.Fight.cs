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

    // ------------------------------------------------------------------ round 2's S4: the enemy a hit does more to

    /// <summary>
    /// What an enemy in sight that every hit mini-crits is worth over one as near: Jarated or marked for death (stock's
    /// mini-crit arm, tf_gamerules.cpp:6126-6129; milk heals the hitter and crits nothing, so it adds nothing here). Stock's
    /// tf_bot weighs no condition in its pick (<c>SelectMoreDangerousThreatInternal</c>, tf_bot_behavior.cpp:1062-1140:
    /// sentries, immediate threats, nearness), so the term is ours and small: of <see cref="TargetScore.Priority"/>'s
    /// nearness, at 500 u it is worth what about 110 u closer is, and inside <see cref="TargetCloseUnits"/> the nearest is
    /// taken whatever it carries.
    /// </summary>
    public const float MiniCritBonus = 0.05f;

    /// <summary>
    /// What an enemy thrown by a blast is worth to a bot whose weapon in hand mini-crits the airborne: stock's
    /// <c>mini_crit_airborne</c> 1 (tf_gamerules.cpp:6268-6276), the Direct Hit's (127) and the Reserve Shooter's (415). As
    /// small as <see cref="MiniCritBonus"/>, for the same reason.
    /// </summary>
    public const float AirborneBonus = 0.05f;

    /// <summary>The weapon attribute whose 1 mini-crits a victim in the air from a blast (the game's own rule reads it so).</summary>
    public const string MiniCritAirborne = "mini_crit_airborne";

    /// <summary>The conditions every hit mini-crits.</summary>
    public const ActorConditions MiniCritConditions = ActorConditions.Jarated | ActorConditions.MarkedForDeath;

    private TargetPick _plainPick;
    private int _plainTier;
    private KnownActor? _plainTarget, _condTarget;
    private bool _condSeen, _jaratedSeen, _airborneOn;
    private ActorConditions _seenThisDecide;

    /// <summary>Decides on which an enemy offered in sight carried a mini-crit condition, and a Jarated one: for the facts and the counters.</summary>
    public int MiniCritSeenDecides { get; private set; }

    public int JaratedSeenDecides { get; private set; }

    /// <summary>Decides on which an enemy offered in sight was drawn marked, wet, or thrown by a blast: the view's reads live, whatever the pick made of them.</summary>
    public int MarkedSeenDecides { get; private set; }

    public int WetSeenDecides { get; private set; }

    public int AirborneSeenDecides { get; private set; }

    /// <summary>Decides whose pick carried a condition's bonus, and those whose pick the bonus changed (the pick with none went elsewhere).</summary>
    public int ConditionTargetDecides { get; private set; }

    public int ConditionSwings { get; private set; }

    /// <summary>Decides whose pick carried the airborne bonus.</summary>
    public int AirborneTargetDecides { get; private set; }

    /// <summary>
    /// The bonus the conditions drawn on <paramref name="a"/> earn it (<see cref="MiniCritBonus"/>, <see cref="AirborneBonus"/>):
    /// only for an enemy in sight, by what this decide's view draws on him.
    /// </summary>
    private float ConditionBonus(KnownActor a, bool sighted)
    {
        if (!sighted || !_view.TryActor(a.Id, out ActorFacts f))
            return 0f;
        _seenThisDecide |= f.Conditions;
        float bonus = 0f;
        if ((f.Conditions & MiniCritConditions) != 0)
        {
            bonus += MiniCritBonus;
            _condSeen = true;
            _jaratedSeen |= (f.Conditions & ActorConditions.Jarated) != 0;
        }

        if ((f.Conditions & ActorConditions.BlastAirborne) != 0 && HandMiniCritsAirborne)
            bonus += AirborneBonus;
        return bonus;
    }

    /// <summary>Whether the weapon in the bot's hands carries <see cref="MiniCritAirborne"/> 1, as its kit lists it; with no kit, no.</summary>
    private bool HandMiniCritsAirborne
        => Kit is { } kit && kit.TryKind(_self.ActiveWeapon, out Senses.TFKitItem item) && item.Attribute(MiniCritAirborne, 0f) == 1f;

    /// <summary>The decide's choice begins for the conditions: the last decide's is counted, and the pick with no condition bonus starts over.</summary>
    private void BeginConditionPick()
    {
        if (_condSeen)
            MiniCritSeenDecides++;
        if (_jaratedSeen)
            JaratedSeenDecides++;
        MarkedSeenDecides += (_seenThisDecide & ActorConditions.MarkedForDeath) != 0 ? 1 : 0;
        WetSeenDecides += (_seenThisDecide & ActorConditions.Wet) != 0 ? 1 : 0;
        AirborneSeenDecides += (_seenThisDecide & ActorConditions.BlastAirborne) != 0 ? 1 : 0;
        _seenThisDecide = ActorConditions.None;
        if (_condTarget is not null)
        {
            ConditionTargetDecides++;
            if (!ReferenceEquals(_condTarget, _plainTarget))
                ConditionSwings++;
            if (_airborneOn)
                AirborneTargetDecides++;
        }

        (_condSeen, _jaratedSeen, _airborneOn) = (false, false, false);
        (_plainTarget, _condTarget, _plainTier) = (null, null, -1);
    }

    /// <summary>
    /// The same offer to the pick with no condition bonus (<paramref name="cond"/> taken out of <paramref name="priority"/>),
    /// and whether the real pick took it with one, so the counters can tell a pick the bonus made. <paramref name="took"/> is
    /// the real pick's answer.
    /// </summary>
    private bool NoteConditionPick(KnownActor a, int tier, bool sighted, float dist, float priority, float cond, bool took)
    {
        if (tier > _plainTier)
            (_plainTier, _plainPick) = (tier, new TargetPick(TargetCloseUnits));
        float plain = priority - cond * (ReferenceEquals(a, _aboutBefore) ? TargetScore.Stick : 1f);
        if (_plainPick.Takes(sighted, dist, plain))
            _plainTarget = a;
        if (took)
        {
            _condTarget = cond > 0f ? a : null;
            _airborneOn = cond > 0f && sighted && _view.TryActor(a.Id, out ActorFacts f) && (f.Conditions & ActorConditions.BlastAirborne) != 0
                          && HandMiniCritsAirborne;
        }

        return took;
    }

    /// <summary>The conditions' counters for <c>/ai/classes</c>, once any is set.</summary>
    private void CountConditions(List<KeyValuePair<string, long>> into)
    {
        if (MiniCritSeenDecides == 0 && ConditionTargetDecides == 0 && WetSeenDecides == 0 && AirborneSeenDecides == 0)
            return;
        into.Add(new("shared.minicrit_seen_decides", MiniCritSeenDecides));
        into.Add(new("shared.jarated_seen_decides", JaratedSeenDecides));
        into.Add(new("shared.marked_seen_decides", MarkedSeenDecides));
        into.Add(new("shared.wet_seen_decides", WetSeenDecides));
        into.Add(new("shared.airborne_seen_decides", AirborneSeenDecides));
        into.Add(new("shared.condition_target_decides", ConditionTargetDecides));
        into.Add(new("shared.condition_swings", ConditionSwings));
        into.Add(new("shared.airborne_target_decides", AirborneTargetDecides));
    }
}
