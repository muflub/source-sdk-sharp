using System.Collections.Immutable;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// A jump course (plan_bots_teams_tf2_part_g G.7, lane G7a): a level whose objectives are control points and no flag,
/// read off the HUD as a player reads it. The next stage is the nearest point the bot's team may capture now (the feed
/// says which: not its own, not locked, the points it needs held, <see cref="Knowledge.TFObjectiveFeed"/>); the goal is
/// the point, and the route's blast links and the jump's own rule (TFDecider.Jump.cs) do the rest. Only a bot that
/// rocket jumps runs it: on a course nobody else gets anywhere.
///
/// <list type="bullet">
/// <item><b>Health.</b> The follower says when its route to the stage has no way and the least health a link it left
/// out for the bot's health costs (<see cref="NavLinkStep.Need"/>, read through the book: <see cref="Jump.IJumpAdvisor.RouteSaid"/>):
/// what a search spends on it, which for a link that lands by a health pack is what the pack does not give back (lane G9,
/// <see cref="Jump.JumpHealthRule.Spend"/>; under 0 for a cheap jump by a big pack), so the bot does not walk back to heal
/// for a jump its landing's pack pays for. A link the tree refuses at its start for the health though the route took it
/// (the pack believed gone) is healed for at its blast's cost.
/// While the bot's health less that is under <see cref="RocketJumpHealthFloor"/> it heals: in a resupply cabinet's zone
/// (its team's or every team's) it waits there, which refills a player every 3 s; else it goes to the nearest such zone
/// or health pack on its own floor (a pack on a stage below is no way back); then back to the stage.</item>
/// <item><b>A fall.</b> The stage's point is not taken, so the goal stays; the follower leaves the failed link out for a
/// while and the driver takes the bot as near as it can get, then tries the goal again.</item>
/// <item><b>No way.</b> A route that has had no way, for nothing the health could change, for
/// <see cref="CourseGiveUpSeconds"/> (past the follower's 8 s of leaving a failed link out): the bot says it cannot
/// go on (<see cref="CourseQuits"/>) and the branch scores nothing for <see cref="CourseQuitSeconds"/>. So with
/// nowhere to heal on its floor. Never a bot standing at a takeoff: the follower's own hold is a second.</item>
/// <item><b>The end.</b> Every point the team's: the bot stays where it is, the course finished.</item>
/// </list>
///
/// <para><b>A course of ends</b> (lane Ge): a level that places capture areas naming points it does not place
/// (<see cref="Knowledge.TFCourseEnds"/>, as <c>jump_academy2_rc9a</c> does, one area a course) is a course whose
/// stages are those ends, and its points are none (the real map's one point is locked, at the hub, and no area names
/// it). An end is reached, not captured: the bot remembers which it has stood in for a life (a death, or the game
/// putting the body back on its team's spawn as a round's restart does, ends it). The next end is the easiest its
/// title says (<see cref="Knowledge.TFCourseEnds.RankOf"/>: a person starts easy), then the nearest, of those not
/// reached and not set aside; no way to one for <see cref="CourseGiveUpSeconds"/> sets that end aside for
/// <see cref="CourseQuitSeconds"/> and the next is taken. Every end reached: the course is finished. A level with no
/// end is read as it was.</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The course's branch name.</summary>
    public const string Course = "Course";

    /// <summary>
    /// The course's weight: under a fight in sight (0.6 to 0.8) and Survive (0.82 to 0.9), over defending, searching and
    /// roaming; once finished, just over roaming (0.1), so the bot stays at the end.
    /// </summary>
    public const float CourseWeight = 0.5f, CourseDoneWeight = 0.11f;

    /// <summary>How long a route with no way for anything but the health is borne, and how long the course is left after.</summary>
    public const float CourseGiveUpSeconds = 20f, CourseQuitSeconds = 60f;

    /// <summary>A cabinet or a pack within this of the bot's height is on its floor.</summary>
    public const float CourseHealRise = 64f;

    private bool _onCourse, _courseDone;
    private Objective? _stage;
    private float _courseNeed;
    private long _courseNoWaySince = -1;
    private AiVector _courseGoal;
    private Tick _courseQuitUntil;

    /// <summary>Times the bot said it could not go on, and decides it spent healing for a link it could not pay for.</summary>
    public int CourseQuits { get; private set; }

    public int CourseHealDecides { get; private set; }

    /// <summary>The stage this decide made for: the point the bot's team may take next, or null.</summary>
    public Objective? Stage => _stage;

    /// <summary>A move between two decides faster than this (sv_maxvelocity) is the game's, not the body's.</summary>
    public const float CourseMovedSpeed = 3500f;

    /// <summary>A body this near one of its team's spawns (each way across, and up or down) after such a move was respawned there.</summary>
    public const float CourseSpawnUnits = 96f;

    private readonly ImmutableArray<Knowledge.TFCourseEnd> _ends = [];
    private readonly bool[] _endReached = [];
    private readonly Tick[] _endAsideUntil = [];
    private int _end = -1;
    private AiVector _endsLastAt;
    private Tick _endsLastTick;

    /// <summary>
    /// The level's course ends (lane Ge), as <see cref="Knowledge.TFCourseEnds.Of"/> reads them from its placements; none
    /// on a level without (and for a tree nobody gave any: a replay's).
    /// </summary>
    public ImmutableArray<Knowledge.TFCourseEnd> CourseEnds
    {
        get => _ends;
        init
        {
            _ends = value.IsDefault ? [] : value;
            _endReached = new bool[_ends.Length];
            _endAsideUntil = new Tick[_ends.Length];
        }
    }

    /// <summary>The end this decide made for (its index in <see cref="CourseEnds"/>), or -1.</summary>
    public int EndTarget => _end;

    /// <summary>Whether the bot has stood in end <paramref name="index"/> this life.</summary>
    public bool EndReached(int index) => _endReached[index];

    /// <summary>Whether end <paramref name="index"/> is set aside now, its route having had no way.</summary>
    public bool EndAside(int index) => _now < _endAsideUntil[index];

    /// <summary>Times the bot came to stand in an end it had not stood in this life, over all its lives; and the lives that forgot the ends.</summary>
    public int CourseEndsReached { get; private set; }

    public int CourseEndLives { get; private set; }

    /// <summary>
    /// Whether the level is a course and which stage is next (<see cref="ReadObjectives"/>'s kin, every decide): control
    /// points and no flag; the nearest point the bot's team may capture now; finished when every point is the team's.
    /// </summary>
    private void ReadCourse()
    {
        (_onCourse, _courseDone, _stage, _end) = (false, false, null, -1);
        if (_view.Objectives is not { } all || !TeamRule.IsGameTeam(_self.Team))
            return;
        if (!_ends.IsEmpty)
        {
            ReadEnds(all);
            return;
        }

        int mine = 1 << _self.Team, points = 0, ours = 0;
        float best = float.PositiveInfinity;
        foreach (Objective o in all.All)
        {
            if (o.Kind == ObjectiveKind.Carry)
            {
                _stage = null;
                return;
            }
            if (o.Kind != ObjectiveKind.Capture)
                continue;
            points++;
            if (o.OwnerTeam == _self.Team)
            {
                ours++;
                continue;
            }

            float d = _self.Origin.DistanceTo(o.Position);
            if ((o.MayAct & mine) != 0 && o.State != ObjectiveState.Locked && d < best)
                (best, _stage) = (d, o);
        }

        _onCourse = points > 0;
        _courseDone = _onCourse && ours == points;
    }

    private float CourseUtility()
    {
        // A points level (lane Gc, TFDecider.Points.cs) is the stances' branches'.
        if (_onPoints)
            return 0f;
        if (!MayRocketJump || !_onCourse)
            return 0f;
        if (_courseDone)
            return CourseDoneWeight;
        return (_stage is null && _end < 0) || _now < _courseQuitUntil ? 0f : CourseWeight;
    }

    /// <summary>
    /// A course of ends (lane Ge): note a new life and the ends the body stands in, then the next end; no course with a
    /// flag on the map; finished when every end is reached this life.
    /// </summary>
    private void ReadEnds(ObjectiveKnowledge all)
    {
        NoteEndLife();
        foreach (Objective o in all.All)
        {
            if (o.Kind == ObjectiveKind.Carry)
                return;
        }

        _onCourse = true;
        int left = 0, bestRank = int.MaxValue;
        float best = float.PositiveInfinity;
        for (int i = 0; i < _ends.Length; i++)
        {
            Knowledge.TFCourseEnd end = _ends[i];
            if (!_endReached[i] && end.Holds(_self.Origin))
            {
                _endReached[i] = true;
                CourseEndsReached++;
            }

            if (_endReached[i])
                continue;
            left++;
            if (_now < _endAsideUntil[i])
                continue;
            float d = _self.Origin.DistanceTo(end.Middle);
            if (end.Rank < bestRank || (end.Rank == bestRank && d < best))
                (bestRank, best, _end) = (end.Rank, d, i);
        }

        _courseDone = left == 0;
    }

    /// <summary>
    /// A new life forgets the ends: alive after a decide dead (<see cref="ForgetEnds"/> from the dead branch), or put on one of
    /// the team's spawns by a move faster than a body goes, as a round's restart respawns everyone without a death.
    /// </summary>
    private void NoteEndLife()
    {
        if (_endsLastTick.Value > 0 && _now > _endsLastTick)
        {
            float seconds = (_now.Value - _endsLastTick.Value) * _tick;
            if (_self.Origin.DistanceTo(_endsLastAt) > CourseMovedSpeed * seconds && OnOwnSpawn())
                ForgetEnds();
        }

        (_endsLastAt, _endsLastTick) = (_self.Origin, _now);
    }

    private bool OnOwnSpawn()
    {
        foreach (TeamSpawn s in _map.TeamSpawns)
        {
            AiVector d = _self.Origin - s.Position;
            if (s.Team == _self.Team && MathF.Abs(d.X) <= CourseSpawnUnits && MathF.Abs(d.Y) <= CourseSpawnUnits && MathF.Abs(d.Z) <= CourseSpawnUnits)
                return true;
        }

        return false;
    }

    /// <summary>A new life: no end reached, none set aside.</summary>
    private void ForgetEnds()
    {
        if (_ends.IsEmpty)
            return;
        Array.Clear(_endReached);
        Array.Clear(_endAsideUntil);
        _endsLastTick = default;
        CourseEndLives++;
    }

    private bool GoCourse()
    {
        if (_courseDone)
        {
            _verdict = "course finished";
            return true;
        }

        if (_stage is null && _end < 0)
            return false;
        AiVector goal = _stage is { } stage ? stage.Position : _ends[_end].Middle;
        if (goal.DistanceTo(_courseGoal) > 1f)
            (_courseGoal, _courseNoWaySince, _courseNeed) = (goal, -1, 0f);

        // What the route last said of the way to the stage: a link next, or no way and what it would need.
        if (Jumps!.RouteSaid(_bot.Slot, _now, goal) is { } said)
        {
            if (said.IsLink)
                _courseNoWaySince = -1;
            else if (said.NoWay && said.Need != 0f)
                (_courseNeed, _courseNoWaySince) = (said.Need, -1);
            else if (said.NoWay && _courseNoWaySince < 0)
                _courseNoWaySince = _now.Value;
        }

        // A link the route stood at last decide that the tree refused for the health though the route took it (lane G9: a jump by
        // health whose pack the bot now believes gone, which a search cannot know): healed for, at its plain cost.
        if (Feel.JumpHealth && _linkHealthRefusedAt.Value != 0 && _now - _linkHealthRefusedAt <= DecideSchedule.Period + 1)
            _courseNeed = MathF.Max(_courseNeed, _linkHealthRefused);

        if (_courseNeed != 0f && _self.Health - _courseNeed < RocketJumpHealthFloor)
            return CourseHeal();
        _courseNeed = 0f;

        if (_courseNoWaySince >= 0 && (_now.Value - _courseNoWaySince) * _tick > CourseGiveUpSeconds)
            return CannotGoOn();
        GoTo(goal, MoveSpeed.Run);
        _verdict = _end >= 0 ? "to the course's end" : _stage!.Value.State == ObjectiveState.Capturing ? "taking the stage" : "to the next stage";
        return true;
    }

    /// <summary>Health for the next link: wait in a cabinet's zone, or go to the nearest cabinet or pack on this floor; none: the bot cannot go on.</summary>
    private bool CourseHeal()
    {
        CourseHealDecides++;
        if (InResupply())
        {
            _verdict = "waiting at the cabinet for the jump";
            return true;
        }

        if (!CourseHealPlace(out AiVector at))
            return CannotGoOn();
        GoTo(at, MoveSpeed.Run);
        _verdict = "to the cabinet for the jump";
        return true;
    }

    private bool CannotGoOn()
    {
        CourseQuits++;
        // An end is set aside, and the next taken; a point's course is left.
        if (_end >= 0)
            (_endAsideUntil[_end], _courseNoWaySince, _courseNeed) = (_now + (int)(CourseQuitSeconds / _tick), -1, 0f);
        else
            (_courseQuitUntil, _courseNoWaySince, _courseNeed) = (_now + (int)(CourseQuitSeconds / _tick), -1, 0f);
        _verdict = "cannot go on";
        return false;
    }

    /// <summary>Whether the bot stands in a resupply cabinet's zone that refills it: its team's, or every team's (TeamNum 0).</summary>
    private bool InResupply()
    {
        foreach (TeamZone z in _map.TeamZones)
        {
            if (z.Kind == ZoneKind.Resupply && (z.Team == 0 || z.Team == _self.Team) && z.Contains(_self.Origin))
                return true;
        }

        return false;
    }

    /// <summary>The nearest cabinet zone (its team's or every team's) or health pack the bot has not seen taken, on its own floor and within <see cref="HealReach"/>.</summary>
    private bool CourseHealPlace(out AiVector at)
    {
        at = default;
        float best = HealReach;
        foreach (TeamZone z in _map.TeamZones)
        {
            if (z.Kind != ZoneKind.Resupply || (z.Team != 0 && z.Team != _self.Team) || MathF.Abs(z.Mins.Z - _self.Origin.Z) > CourseHealRise)
                continue;
            var mid = new AiVector((z.Mins.X + z.Maxs.X) * 0.5f, (z.Mins.Y + z.Maxs.Y) * 0.5f, z.Mins.Z);
            float d = _self.Origin.DistanceTo(mid);
            if (d < best)
                (best, at) = (d, mid);
        }

        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            ItemSpawn item = _map.Items[i];
            if (item.Kind is not (ItemKind.HealthSmall or ItemKind.HealthMedium or ItemKind.HealthLarge or ItemKind.Health)
                || (i < beliefs.Length && beliefs[i].Present == false) || MathF.Abs(item.Position.Z - _self.Origin.Z) > CourseHealRise)
                continue;
            float d = _self.Origin.DistanceTo(item.Position);
            if (d < best)
                (best, at) = (d, item.Position);
        }

        return best < HealReach;
    }
}
