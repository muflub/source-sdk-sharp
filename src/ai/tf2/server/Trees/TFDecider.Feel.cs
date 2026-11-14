using SourceSharp.Ai.TF2.Knowledge;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The feel layer (plan_bots_teams_tf2_part_g G.2.1): what makes the HL2MP bot read as a person,
/// lifted to the core and called here with TF2's own numbers. The eye a shot is led from is the
/// bot's class's, and a body is aimed at where its class's middle is, held through a crouch or a
/// jump (<see cref="AimHeightHold"/>): the crosshair stays for a reaction, then re-aims. The aim's
/// error wanders from where first sight put it (<see cref="AimWander"/>), wider at first and
/// smaller up than across, where it was a new draw at each decide. And the target is scored
/// (<see cref="TargetScore"/>), with TF2's own terms: the enemy carrying the bot's flag, a Medic.
/// On its way with nobody in sight it glances off the route (<see cref="PathGlance"/>), and it
/// does not fire through a teammate (<see cref="FriendLine"/>). Its streaks (<see cref="Streaks"/>)
/// reach its aim, its lead and, through the doing record, its reaction and recognition.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>
    /// Makes the layer's parts for this bot, each with a stream of its own from the bot's seed (the
    /// aim's error keeps the aim's stream), so a replay of the same views draws the same and one
    /// part's luck moves no other's. The constructor's, once.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_aimHeight), nameof(_aimWander), nameof(_glance), nameof(_streaks))]
    private void StartFeel(ulong seed)
    {
        _aimHeight = new AimHeightHold(new AiRandom(seed ^ 0x5446_4148UL));
        _aimWander = new AimWander(_aimRng);
        _glance = new PathGlance(new AiRandom(seed ^ 0x5446_474CUL));
        _streaks = new Streaks(new AiRandom(seed ^ 0x5446_484FUL));
    }

    private Streaks _streaks;

    /// <summary>
    /// The bot's hot and cold streaks (<see cref="Streaks"/>), stepped every decide, alive or dead:
    /// a kill or two can start a run above itself, two quick deaths one below, by its own stream.
    /// </summary>
    private void Streak() => _streaks.Step(_now, _tick, _memory, in _p, _self.Alive);

    /// <summary>The aim's settled error and a shot's lead now: a streak's, or the bot's own.</summary>
    private float AimErrorNow => _streaks.AimErrorDeg(in _p);

    private float LeadNow => _streaks.LeadQuality(in _p);

    /// <summary>
    /// <paramref name="doing"/> with the streak's four fields filled, which the motion layer (its
    /// reaction) and the senses (its recognition) read off the record; as it was with none on.
    /// </summary>
    private DoingRecord WithStreak(DoingRecord doing)
    {
        (string name, float aimErrorDeg, float reactionMs, float recognizeSeconds) = _streaks.Record(in _p);
        return doing with { Streak = name, StreakAimErrorDeg = aimErrorDeg, StreakReactionMs = reactionMs, StreakRecognizeSeconds = recognizeSeconds };
    }

    private AimHeightHold _aimHeight;
    private ActorId _aimHeightOf;
    private Tick _aimHeightAt;

    /// <summary>What a Medic is worth over another class as near: a team that loses its Medic loses its push.</summary>
    public const float MedicBonus = 0.35f;

    /// <summary>What the enemy carrying the bot's flag is worth over anyone else remembered; in sight it comes before everyone.</summary>
    public const float CarrierBonus = 1f;

    /// <summary>Within this someone in plain sight comes before a better target further off, the nearest first (the core's close range).</summary>
    public const float TargetCloseUnits = AimStats.CloseRange;

    private KnownActor? _aboutBefore;
    private Tick _choseAt;
    private TargetPick _pick;
    private int _pickTier, _pickRows;

    /// <summary>A decide's choice begins: nobody is picked, and the one it was about at its last decide is remembered, for its hold over a rival.</summary>
    private void BeginTargets()
    {
        // After a decide that chose nobody's (it was dead), it is about no one.
        _aboutBefore = _now - _choseAt <= DecideSchedule.Period ? _target : null;
        _choseAt = _now;
        _pickTier = -1;
        _pickRows = TargetScore.Rows(_memory);
    }

    private bool CarriesOurFlag(KnownActor a) => _ownFlag is { State: ObjectiveState.Carried } flag && flag.CarrierSlot == a.Id.Slot;

    /// <summary>
    /// Whether the enemy offered is the decide's pick so far. In order: the enemy in sight carrying
    /// the bot's flag (the HUD names its carrier), anyone else in sight, then the remembered. Among
    /// those the core's score (<see cref="TargetScore"/>): someone close first, else nearness, the
    /// scoreboard's rank and a grudge by the bot's skills, a Medic (as last drawn) more than another
    /// class, and the one the bot is already about keeps its hold over one slightly better.
    /// </summary>
    private bool TakesTarget(KnownActor a, bool sighted, float dist)
    {
        bool carrier = CarriesOurFlag(a);
        int tier = !sighted ? 0 : carrier ? 2 : 1;
        if (tier < _pickTier)
            return false;
        if (tier > _pickTier)
            (_pickTier, _pick) = (tier, new TargetPick(TargetCloseUnits));
        float bonus = _p.RankBonus * TargetScore.RankBonus(TargetScore.RankOf(_memory, a), _pickRows)
                      + (a.ClassBelieved == (int)TFClass.Medic ? MedicBonus : 0f) + (carrier ? CarrierBonus : 0f);
        float priority = TargetScore.Priority(sighted, a.Confidence, dist, _p.EasyTargetDiscount, bonus, TargetScore.Grudge(a, _now, _tick, _p.GrudgeWeight),
                                              ReferenceEquals(a, _aboutBefore));
        return _pick.Takes(sighted, dist, priority);
    }

    private PathGlance _glance;

    /// <summary>Glances taken, for the facts and the counters.</summary>
    public int Glances => _glance.Taken;

    /// <summary>A glance and the view's way back from it are over in this long: no glance begins this near a rocket jump.</summary>
    public const float GlanceClearSeconds = 1f;

    /// <summary>
    /// Whether a rocket jump may begin before a glance taken now is over. The jump's sequence
    /// pitches the view down and leaves its yaw where it is, so its rocket goes where the bot
    /// looks: a Soldier whose jump is due keeps its eyes on its way. Only what holds for a while
    /// is asked (the class, the launcher in hand, the health, the long run, the wait since the
    /// last jump), so a glance is never begun on a guess that the next decide's speed or floor
    /// will refuse the jump.
    /// </summary>
    private bool RocketJumpDue
        => _class == TFClass.Soldier && _rocketJumpRow >= 0 && _graph is not null && HoldingLauncher && !Carrying
           && _self.Health >= RocketJumpHealthFloor && _move.Kind == MoveKind.Follow && _self.Origin.DistanceTo(_goal) >= RocketJumpRoute
           && (_nextJumpAt - _now) * _tick < GlanceClearSeconds;

    /// <summary>
    /// A glance off the way while nobody is in sight (<see cref="PathGlance"/>, at Alertness's
    /// <c>glanceRate</c>): the bot checks a doorway in passing, where it stared along its route.
    /// Not while a rocket jump's sequence has the view or one is due, nor when the look is already
    /// on something.
    /// </summary>
    private void Glance()
    {
        if (_targetSighted || !_hasGoal || _look.Kind != LookKind.Path || Busy || RocketJumpDue)
            return;
        if (_glance.Step(_now, _tick, _p.GlanceRate, _self.Origin, _goal, OwnEye.Z - _self.Origin.Z, out AiVector at))
            _look = new LookIntent(LookKind.Glance, at, ActorId.None, AimPoint.Body, 0f, 0);
    }

    /// <summary>Decides on which fire was held for a teammate in the way, for the facts and the counters.</summary>
    public int HeldFireFriend { get; private set; }

    /// <summary>
    /// Not shooting a teammate (<see cref="FriendLine"/>): the trigger is let go when a teammate the
    /// bot sees stands on the line to its aim or on the view's own line, nearer than the target,
    /// and, with friendly fire on, when one stands where a rocket's blast would reach
    /// (<see cref="SelfBlastUnits"/>). As the bot's client draws them: a Spy disguised as a teammate
    /// holds its fire too. The shot it did not take is not counted.
    /// </summary>
    private void FriendGate()
    {
        if (_action.Kind != ActionKind.Fire || !TeamRule.IsGameTeam(_self.Team))
            return;
        float blast = _memory.FriendlyFireOn && Splash(out _) ? SelfBlastUnits : 0f;
        if (!FriendLine.Blocked(_view, OwnEye.Z - _self.Origin.Z, _look.Kind == LookKind.Actor ? _look.Point : null, FireReach, blast,
                                TFKnowledge.AimHeight(AimPoint.Head, TFClass.Undefined).Standing))
            return;
        _action = ActionIntent.None;
        _verdict = "held fire (teammate)";
        HeldFireFriend++;
        Shots--;
        if (_look.Aim == AimPoint.Feet)
            FeetShots--;
    }

    private AimWander _aimWander;
    private Tick _aimErrorAt;

    /// <summary>
    /// The error by Accuracy on this decide's aim at the target in sight, to add to
    /// <paramref name="aim"/>: <c>aimErrorDeg</c> is its settled size, and it drifts from decide to
    /// decide (<see cref="AimWander"/>). It starts over, wide, on another target and after a decide
    /// that aimed at nobody. A blast at the <paramref name="feet"/> errs along the floor only.
    /// </summary>
    private AiVector AimError(AiVector aim, bool feet)
    {
        if (_now - _aimErrorAt > DecideSchedule.Period)
            _aimWander.Lost();
        _aimErrorAt = _now;
        FreshAims += _aimWander.Step(_target!.Id, sighted: true, _now, _tick) ? 1 : 0;
        AiVector error = _aimWander.Error(_self.Origin, aim, AimErrorNow, _targetDist, _now, _tick);
        return feet ? new AiVector(error.X, error.Y, 0f) : error;
    }

    /// <summary>Aims begun anew (a target newly in sight, or another one), for the facts and the counters.</summary>
    public int FreshAims { get; private set; }

    /// <summary>
    /// The bot's own eye, which a shot's flight is led from: its class's over its feet
    /// (<see cref="TFKnowledge.AimHeight"/>'s head, the class's eye), the mod's duck view when ducked.
    /// </summary>
    public AiVector OwnEye
        => _self.Origin + new AiVector(0f, 0f, TFKnowledge.AimHeight(AimPoint.Head, _class).At((_self.Flags & SelfFlags.Ducking) != 0));

    /// <summary>What the body aim's height is doing: the inspector's and the facts'.</summary>
    public AimHeightPhase AimHeightNow => _aimHeight.Phase;

    /// <summary>
    /// The world height to aim at on the target in sight. The feet are the floor's, whatever the
    /// body does. The body's middle is its class's as the bot's client draws it, standing or
    /// ducked, and is held: an engagement begins where the body would stand, and a body that is
    /// not there, or leaves it, is re-aimed at after the bot's recognition and reaction.
    /// </summary>
    private float AimHeight(bool feet)
    {
        if (feet || _target is null || !_view.TryActor(_target.Id, out ActorFacts drawn))
        {
            _aimHeightOf = ActorId.None;
            return _targetPos.Z + (feet ? TFKnowledge.FeetAim : TFKnowledge.AimHeight(AimPoint.Body, TFClass.Undefined).Standing);
        }

        // New when it is another body, or the last decide did not aim at this one's.
        bool fresh = _target.Id != _aimHeightOf || _now - _aimHeightAt > DecideSchedule.Period;
        (_aimHeightOf, _aimHeightAt) = (_target.Id, _now);
        return _aimHeight.Hold(fresh, sighted: true, _targetPos.Z, TFKnowledge.AimHeight(AimPoint.Body, (TFClass)drawn.Class),
                               (drawn.Activity & ActorActivity.Crouched) != 0, rise: 0f, _streaks.ReAimSeconds(in _p),
                               _p.FlickOvershootPct, _targetDist, DecideSchedule.Period * _tick);
    }
}
