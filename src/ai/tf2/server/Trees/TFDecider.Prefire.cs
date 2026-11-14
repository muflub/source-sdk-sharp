using System.Diagnostics.CodeAnalysis;
using SourceSharp.Ai.TF2.Knowledge;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>Why a TF2 bot's prefire did not go on a decide, for the counters (<see cref="TFDecider.PrefireRefused"/>).</summary>
public enum TFPrefireRefusal : byte
{
    /// <summary>The place is nearer than <see cref="TFDecider.PrefireNearest"/>: the blast would take the bot.</summary>
    Near = 0,

    /// <summary>Further than <see cref="TFDecider.PrefireFarthest"/>.</summary>
    Far,

    /// <summary>The belief would be past its seconds when the rocket landed.</summary>
    Old,

    /// <summary>Further round than a human turn from where the bot looks.</summary>
    Turn,

    /// <summary>Not a rocket for it and the jump's two kept back.</summary>
    Clip,

    /// <summary>The game answered the line, and the last place seen too, blocked.</summary>
    Blocked,
}

/// <summary>
/// A Soldier's rocket at the place it believes an enemy it lost from sight is (plan_bots_teams_tf2_part_g G.2.1's prefire,
/// lane Gp): the core's prefire (<see cref="PrefireRun"/>, lifted from HL2MP's decider) with TF2's own shot. A person
/// playing a Soldier puts a rocket into the doorway an enemy just went behind; HL2MP's bot sprays it, a rocket is one
/// shot that splashes, so it goes at the floor of the opening (<see cref="PrefireOpening"/>: the nearest place to the
/// belief along the graph that the map's sight table says the bot sees), and only down a line the game answered clear.
/// <para>Allowed: a rocket launcher in hand (a weapon whose projectile flies flat); no enemy in sight (a teammate in sight is no reason to stop); a target remembered
/// and out of sight that the core believes (lost from sight within 2.5 s, heard within 1.5 s); the root's branch about
/// the enemy (Fight, Defend, Guard, Search: not delivering the flag nor fetching it, nor going for health, roaming or on
/// the course); no jump under way or begun this decide; not held still; a rocket for it and, for a bot that rocket
/// jumps, <see cref="RocketJumpRockets"/> left for the jump.</para>
/// <para>In reach: at least <see cref="PrefireNearest"/> from the eye (the blast), at most <see cref="PrefireFarthest"/>,
/// the belief still within its seconds when the rocket lands (its age now and the rocket's flight), and within
/// <see cref="PrefireTurnDeg"/> of the view unless already firing.</para>
/// <para>How often: a chance every <see cref="PrefireRun.CheckSeconds"/>, at <c>prefireChance x prefireThrift</c> (Cunning
/// up, Discipline down), from a stream of its own. The trigger is held until the clip shows the rocket went, then it
/// waits; a press that sent nothing in <see cref="PrefireGiveUpSeconds"/> is given up. Seeing an enemy ends it that
/// decide; a teammate in the way holds it (<see cref="FriendGate"/>, with the place as the aim).</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>A prefire's place is no nearer the eye than the launcher's near range, where its blast no longer reaches the bot, and no further than a rocket at the floor reaches.</summary>
    public const float PrefireNearest = FightNear, PrefireFarthest = FeetReach;

    /// <summary>The place must be within this of the way the view points: a human turn (HL2MP's).</summary>
    public const float PrefireTurnDeg = 60f;

    /// <summary>A press that has not sent the rocket in this long is given up (the hands wait for the view's flick).</summary>
    public const float PrefireGiveUpSeconds = 1f;

    /// <summary>After a prefire rocket lands, a hurt or a kill in this long is counted to it.</summary>
    public const float PrefireAfterLandSeconds = 0.5f;

    private LineAsks _lines;
    private PrefireRun _prefire;
    private PrefireOpening _opening;
    private string _prefireState = "";
    private bool _prefireFiring, _prefireDanger, _outcomeDanger;
    private int _prefireClip;
    private float _rocketSpeed;
    private Tick _beliefAt;
    private float _beliefAge, _beliefWindow;
    private readonly int[] _prefireRefused = new int[(int)TFPrefireRefusal.Blocked + 1];

    // The last prefire rocket's window: what the memory showed when it went.
    private bool _outcomeOpen, _hitSeen, _selfSeen, _killSeen;
    private Tick _outcomeUntil, _outcomeDamage, _outcomeAttack, _outcomeKill;
    private int _outcomeEngaged;

    /// <summary>Chances drawn (a place in reach, the wait over), and those taken: the view went to the place.</summary>
    public int PrefireChances { get; private set; }

    public int PrefireAims { get; private set; }

    /// <summary>Prefire rockets that went (the clip fell under a prefire's trigger), and presses given up with none gone.</summary>
    public int PrefireRockets { get; private set; }

    public int PrefireGiveUps { get; private set; }

    /// <summary>Decides on which a prefire's trigger was let go for a teammate in the way.</summary>
    public int PrefireHeldFriend { get; private set; }

    /// <summary>Prefire rockets after which, within their flight and <see cref="PrefireAfterLandSeconds"/>, the bot hurt an enemy, was hurt by itself (or the world), or killed.</summary>
    public int PrefireHits { get; private set; }

    public int PrefireSelfHurt { get; private set; }

    public int PrefireKills { get; private set; }

    /// <summary>The danger shot's part of those (TFDecider.Danger.cs): chances drawn at a killer place, aims, rockets, and rockets after which an enemy was hurt.</summary>
    public int DangerChances { get; private set; }

    public int DangerAims { get; private set; }

    public int DangerRockets { get; private set; }

    public int DangerHits { get; private set; }

    /// <summary>Decides on which a prefire did not go on, by <see cref="TFPrefireRefusal"/>.</summary>
    public ReadOnlySpan<int> PrefireRefused => _prefireRefused;

    /// <summary>The core's parts, the prefire's stream from the seed. The constructor's, once.</summary>
    [MemberNotNull(nameof(_lines), nameof(_prefire), nameof(_opening))]
    private void StartPrefire(ulong seed)
    {
        _lines = new LineAsks();
        _prefire = new PrefireRun(new AiRandom(seed ^ 0x5446_5046UL), _lines);
        _opening = new PrefireOpening(_graph?.CellCount ?? 0);
    }

    /// <summary>The decide's prefire: after the rocket jump and before the hands (TFDecider.cs's Decide).</summary>
    private void Prefire()
    {
        _prefireState = "";
        _prefireFiring = false;
        if (_prefire.Firing && _self.Clip < _prefireClip)
        {
            // The rocket went: the clip shows one fewer.
            PrefireRockets++;
            DangerRockets += _prefireDanger ? 1 : 0;
            OpenOutcome();
            _prefire.EndAndWait(_now, _tick);
            return;
        }

        bool belief = PrefireAllowed(danger: false), danger = (_dangerPending || (_prefire.Aiming && _prefireDanger)) && PrefireAllowed(danger: true);
        bool allowed = _prefire.Aiming ? (_prefireDanger ? danger : belief) : belief || danger;
        bool spent = _prefire.Firing && _now >= _prefire.Until;
        if (!_prefire.Gate(allowed, _now, _tick))
        {
            PrefireGiveUps += allowed && spent ? 1 : 0;
            return;
        }

        if (!_prefire.Aiming)
        {
            if (_now < _prefire.Next)
                return;
            AiVector spot, fallback;
            if (belief && PrefireBelief.Of(_target!, _now, _tick, _graph, null, out AiVector believed, out AiVector last, out float age, out float window))
            {
                (_prefireDanger, _beliefAt, _beliefAge, _beliefWindow) = (false, _now, age, window);
                spot = OnFloor(_opening.Find(_graph, _memory.World.Sight, _self.Origin, believed));
                fallback = OnFloor(last);
            }
            else if (danger)
            {
                // The killer place is where they stand: the rocket goes at its floor, and there is nowhere behind it to try.
                (_prefireDanger, _beliefAt, _beliefAge, _beliefWindow) = (true, _dangerDeathAt, 0f, DangerShotSeconds);
                spot = fallback = OnFloor(FloorUnder(_dangerAt));
            }
            else
            {
                return;
            }

            if (!PrefireInReach(spot, count: true))
                return;
            PrefireChances++;
            if (_prefireDanger)
            {
                // One chance a death.
                _dangerPending = false;
                DangerChances++;
            }

            if (!_prefire.Draw(_now, _tick, _p.PrefireChance * _p.PrefireThrift, spot, fallback))
                return;
            PrefireAims++;
            DangerAims += _prefireDanger ? 1 : 0;
        }

        if (!PrefireInReach(_prefire.Spot, count: true))
        {
            _prefire.End();
            return;
        }

        // The view goes there at once; the hands follow when the line is known, and the applier fires when the view is there.
        _look = new LookIntent(LookKind.CornerCheck, _prefire.Spot, _prefireDanger ? ActorId.None : _target!.Id, AimPoint.Body, 0f, 0);
        _prefireState = "aiming";
        bool wasFiring = _prefire.Firing;
        PrefireStep step = _prefire.Line(_memory.LastLine, _now, _self.Origin, PrefireInReach(_prefire.Fallback, count: false), (int)(PrefireGiveUpSeconds / _tick));
        if (step == PrefireStep.Ended)
            _prefireRefused[(int)TFPrefireRefusal.Blocked]++;
        if (step != PrefireStep.Firing)
            return;
        // A rocket loaded since the press began is no rocket gone.
        _prefireClip = wasFiring ? Math.Max(_prefireClip, _self.Clip) : _self.Clip;
        _prefireState = "firing";
        _prefireFiring = true;
        _action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
    }

    /// <summary>
    /// Whether a prefire may go on this decide (the type's summary says which rules): at a belief, or with
    /// <paramref name="danger"/> at the place a teammate was just killed from, which wants no target and goes on the bot's
    /// way to the flag or with its carrier too.
    /// </summary>
    private bool PrefireAllowed(bool danger)
    {
        if (!Feel.Prefire || (!danger && _target is null) || _targetSighted || HeldStill || Busy || _action.Kind != ActionKind.None)
            return false;
        if (!Splash(out WeaponFacts own) || EnemyInSight())
            return false;
        if (danger ? _root.Chosen?.Name is not (Fight or Defend or Guard or Search or Fetch or Escort or ChaseCarrier)
                   : _root.Chosen?.Name is not (Fight or Defend or Guard or Search))
            return false;
        _rocketSpeed = own.ProjectileSpeed;
        if (_self.Clip < (MayRocketJump ? RocketJumpRockets + 1 : 1))
        {
            _prefireRefused[(int)TFPrefireRefusal.Clip]++;
            return false;
        }

        return true;
    }

    private bool EnemyInSight()
    {
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if (!TeamRule.Friends(_self.Team, f.Team))
                return true;
        }

        return false;
    }

    /// <summary>A place on a floor, where a rocket's blast is aimed: the floor's height and the feet's aim over it.</summary>
    private static AiVector OnFloor(AiVector at) => new(at.X, at.Y, at.Z + TFKnowledge.FeetAim);

    /// <summary>The floor under <paramref name="at"/>: the graph's floor nearest below it, else the point itself.</summary>
    private AiVector FloorUnder(AiVector at)
    {
        CellId cell = _graph?.FindCell(at) ?? CellId.None;
        return cell.IsValid ? new AiVector(at.X, at.Y, _graph!.Cell(cell).FloorZ) : at;
    }

    /// <summary>Whether a rocket at <paramref name="spot"/> is in reach now (the type's summary says which rules); a refusal counted when <paramref name="count"/>.</summary>
    private bool PrefireInReach(AiVector spot, bool count)
    {
        AiVector eye = OwnEye;
        float distance = eye.DistanceTo(spot);
        float lands = _beliefAge + (_now - _beliefAt) * _tick + (_rocketSpeed > 0f ? distance / _rocketSpeed : 0f);
        int why = distance < PrefireNearest ? (int)TFPrefireRefusal.Near
                  : distance > PrefireFarthest ? (int)TFPrefireRefusal.Far
                  : lands > _beliefWindow ? (int)TFPrefireRefusal.Old
                  : !_prefire.Firing && VisionMath.Forward(_self.Eye).Dot((spot - eye).Normalised()) < MathF.Cos(PrefireTurnDeg * (MathF.PI / 180f)) ? (int)TFPrefireRefusal.Turn
                  : -1;
        if (why >= 0 && count)
            _prefireRefused[why]++;
        return why < 0;
    }

    /// <summary>A prefire rocket went: its window opens on what the memory shows now.</summary>
    private void OpenOutcome()
    {
        float flight = _rocketSpeed > 0f ? OwnEye.DistanceTo(_prefire.Spot) / _rocketSpeed : 0f;
        Blackboard b = _memory.Board;
        (_outcomeOpen, _hitSeen, _selfSeen, _killSeen, _outcomeDanger) = (true, false, false, false, _prefireDanger);
        _outcomeUntil = _now + (int)((flight + PrefireAfterLandSeconds) / _tick);
        (_outcomeEngaged, _outcomeDamage, _outcomeAttack, _outcomeKill) = (Engaged(), b.LastDamageTick, b.LastAttackTick, _memory.OwnKills.Last);
    }

    /// <summary>
    /// Every decide, alive or dead: what the memory has shown since the last prefire rocket went, while its window is open. An
    /// enemy the bot hurt (<see cref="KnownActor.TimesEngaged"/> rose), a hurt with no one else's attack (its own blast, or a
    /// fall), a kill of its own. Counted by time, not by projectile: a rocket at someone seen in the window counts too.
    /// </summary>
    private void NotePrefireOutcome()
    {
        if (!_outcomeOpen)
            return;
        Blackboard b = _memory.Board;
        if (!_hitSeen && Engaged() > _outcomeEngaged)
        {
            (_hitSeen, PrefireHits) = (true, PrefireHits + 1);
            DangerHits += _outcomeDanger ? 1 : 0;
        }
        if (!_selfSeen && b.LastDamageTick > _outcomeDamage && b.LastAttackTick == _outcomeAttack)
            (_selfSeen, PrefireSelfHurt) = (true, PrefireSelfHurt + 1);
        if (!_killSeen && _memory.OwnKills.Last > _outcomeKill)
            (_killSeen, PrefireKills) = (true, PrefireKills + 1);
        if (_now >= _outcomeUntil)
            _outcomeOpen = false;
    }

    /// <summary>The hits the bot has landed on everyone it knows (a foe's count rises with each).</summary>
    private int Engaged()
    {
        Blackboard board = _memory.Board;
        int sum = 0;
        for (int s = 0; s < board.Slots; s++)
            sum += board.Actor(s).TimesEngaged;
        return sum;
    }

    /// <summary><paramref name="doing"/> with the line asked of (the server answers it) and the prefire's state and place, for the inspector.</summary>
    private DoingRecord WithPrefire(DoingRecord doing)
        => doing with { LineAsk = _lines.Ask, LineAt = _lines.At, Prefire = _prefireState, PrefireAt = _prefire.Aiming ? _prefire.Spot : default };
}
