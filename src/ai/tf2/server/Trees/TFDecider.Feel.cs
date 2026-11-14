using SourceSharp.Ai.TF2.Knowledge;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// Which parts of the feel layer a decider has, for telling in a live game which part changed the
/// bots' play (the <c>ai_tf_feel_*</c> convars, <see cref="TFFeelSwitchConVars"/>). A part off is
/// what the decider did before it had the part: the enemy carrying the bot's flag is no one
/// special (<see cref="Carrier"/>), a sound is not gone to (<see cref="Search"/>), the aim's error
/// is a new draw at each decide (<see cref="Aim"/>), health is gone for under 40 % by everyone
/// (<see cref="Retreat"/>), a teammate in the way holds no fire (<see cref="FriendGate"/>), and
/// the bot does not glance off its way (<see cref="Glance"/>).
/// </summary>
public readonly record struct TFFeelSwitches(bool Carrier, bool Search, bool Aim, bool Retreat, bool FriendGate, bool Glance)
{
    /// <summary>Every part on: the layer as it is.</summary>
    public static TFFeelSwitches All => new(true, true, true, true, true, true);
}

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
/// reach its aim, its lead and, through the doing record, its reaction and recognition. And it
/// leaves a fight for health when its skills say (<see cref="RetreatRule"/>), not at one share for all.
/// A sound it cannot explain it goes to look into (<see cref="SoundSearch"/>).
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The layer's parts this decider has, for its life; all of them unless its maker says otherwise.</summary>
    public TFFeelSwitches Feel { get; init; } = TFFeelSwitches.All;

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
        _scoreSeed = seed ^ 0x5446_5343UL;
    }

    /// <summary>
    /// The bot's own eye, which a shot's flight is led from: its class's over its feet
    /// (<see cref="TFKnowledge.AimHeight"/>'s head, the class's eye), the mod's duck view when ducked.
    /// </summary>
    public AiVector OwnEye
        => _self.Origin + new AiVector(0f, 0f, TFKnowledge.AimHeight(AimPoint.Head, _class).At((_self.Flags & SelfFlags.Ducking) != 0));

    private AimHeightHold _aimHeight;
    private ActorId _aimHeightOf;
    private Tick _aimHeightAt;

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
        if (!Feel.Aim)
        {
            // As it was before the error wandered: a point off the body, drawn each decide.
            float size = MathF.Tan(AimErrorNow * (MathF.PI / 180f)) * _targetDist;
            AiVector to = new AiVector(aim.X - _self.Origin.X, aim.Y - _self.Origin.Y, 0f).Normalised();
            float across = (_aimRng.NextFloat() * 2f - 1f) * size, up = (_aimRng.NextFloat() * 2f - 1f) * size * 0.5f;
            return new AiVector(-to.Y * across, to.X * across, feet ? 0f : up);
        }

        if (_now - _aimErrorAt > DecideSchedule.Period)
            _aimWander.Lost();
        _aimErrorAt = _now;
        FreshAims += _aimWander.Step(_target!.Id, sighted: true, _now, _tick) ? 1 : 0;
        AiVector error = _aimWander.Error(_self.Origin, aim, AimErrorNow, _targetDist, _now, _tick);
        return feet ? new AiVector(error.X, error.Y, 0f) : error;
    }

    /// <summary>Aims begun anew (a target newly in sight, or another one), for the facts and the counters.</summary>
    public int FreshAims { get; private set; }

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
        bool carrier = Feel.Carrier && CarriesOurFlag(a);
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
        if (!Feel.Glance || _targetSighted || !_hasGoal || _look.Kind != LookKind.Path || Busy || RocketJumpDue)
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
        if (!Feel.FriendGate || _action.Kind != ActionKind.Fire || !TeamRule.IsGameTeam(_self.Team))
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

    /// <summary>
    /// Whether the bot goes for health now, by its skills where it was 40 % for everyone. Under
    /// threat (<see cref="RetreatRule.Threatened"/>: an enemy in sight, or hurt in the last three
    /// seconds) it gives the fight up under the share its Nerve says, or its Tenacity's give-up
    /// when that is higher (<see cref="RetreatRule.HealthShare"/>): a timid bot at 60 %, a bold one
    /// not until 10 %. In a lull anyone tops up under <see cref="HurtShare"/>, and a timid bot
    /// still under its own share, so the end of a threat does not turn it round.
    /// </summary>
    private bool WantsHealth
    {
        get
        {
            if (!Feel.Retreat)
                return HealthShare < HurtShare;
            float share = RetreatRule.HealthShare(in _p);
            // A bot never hurt has no last hit to be under threat from.
            Tick hurt = _memory.Board.LastDamageTick;
            bool threatened = _targetSighted || (hurt.Value != 0 && RetreatRule.Threatened(false, _now, hurt, _tick));
            return HealthShare < (threatened ? share : Math.Max(HurtShare, share));
        }
    }

    private int _lastAttackSeen;

    /// <summary>
    /// Whether someone has hurt the bot since the last decide asked, which ends a commitment. The
    /// bot's own blast does not (plan_bots_teams_tf2_part_g G.13: a rocket jump costs health, and
    /// is the bot's own doing in the middle of what it is committed to), nor a fall: it is the
    /// memory's <see cref="Blackboard.LastAttackTick"/> that is read, not its last damage.
    /// </summary>
    internal bool HurtByAnother()
    {
        int attack = _memory.Board.LastAttackTick.Value;
        bool hurt = attack != _lastAttackSeen;
        _lastAttackSeen = attack;
        return hurt;
    }

    /// <summary>The root's branch for a sound heard and not explained, beside the names in TFDecider.cs.</summary>
    public const string Search = "Search";

    /// <summary>
    /// Search's weight: over roaming and over chasing a memory, under the flag's business; for a
    /// defender a sound within <see cref="DefenderSearchReach"/> of its flag is worth more than
    /// standing at it.
    /// </summary>
    public const float SearchWeight = 0.4f, DefenderSearchWeight = 0.55f, DefenderSearchReach = 1200f;

    /// <summary>
    /// A sound the bot heard and has not explained (a rocket, a shot, an enemy's footsteps; a
    /// teammate's own noise is none, the memory's rule) is worth going to look into while it is
    /// fresh by Alertness's <c>searchSeconds</c> (<see cref="SoundSearch"/>). Not with the flag in
    /// hand, nor a sound from inside another team's respawn room; an enemy in sight outweighs it.
    /// </summary>
    private float SearchUtility()
    {
        if (!Feel.Search || Carrying || !SoundSearch.Worth(_memory.Board, _now, _tick, _p.SearchSeconds, out Investigation sound)
            || InAnothersRoom(sound.Position))
            return 0f;
        bool byItsPost = Role == TFTeamRole.Defend && _ownFlag is { } flag && sound.Position.DistanceTo(flag.Position) < DefenderSearchReach;
        return byItsPost ? DefenderSearchWeight : SearchWeight;
    }

    private bool InAnothersRoom(AiVector at)
    {
        foreach (TeamZone z in _map.TeamZones)
        {
            if (z.Kind == ZoneKind.RespawnRoom && z.Team != _self.Team && TeamRule.IsGameTeam(z.Team) && z.Contains(at))
                return true;
        }

        return false;
    }

    /// <summary>The search's leaf: to the sound, which is settled once the bot is there.</summary>
    private bool GoSearch()
    {
        if (!SoundSearch.Next(_memory.Board, _now, _tick, _self.Origin, gettingNowhere: false, out AiVector at))
            return false;
        GoTo(at, MoveSpeed.Run);
        _verdict = "heard something";
        return true;
    }
}
