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
/// out for the bot's health costs (<see cref="NavLinkStep.Need"/>, read through the book: <see cref="Jump.IJumpAdvisor.RouteSaid"/>).
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

    /// <summary>
    /// Whether the level is a course and which stage is next (<see cref="ReadObjectives"/>'s kin, every decide): control
    /// points and no flag; the nearest point the bot's team may capture now; finished when every point is the team's.
    /// </summary>
    private void ReadCourse()
    {
        (_onCourse, _courseDone, _stage) = (false, false, null);
        if (_view.Objectives is not { } all || !TeamRule.IsGameTeam(_self.Team))
            return;
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
        if (!MayRocketJump || !_onCourse)
            return 0f;
        if (_courseDone)
            return CourseDoneWeight;
        return _stage is null || _now < _courseQuitUntil ? 0f : CourseWeight;
    }

    private bool GoCourse()
    {
        if (_courseDone)
        {
            _verdict = "course finished";
            return true;
        }

        if (_stage is not { } stage)
            return false;
        AiVector goal = stage.Position;
        if (goal.DistanceTo(_courseGoal) > 1f)
            (_courseGoal, _courseNoWaySince, _courseNeed) = (goal, -1, 0f);

        // What the route last said of the way to the stage: a link next, or no way and what it would need.
        if (Jumps!.RouteSaid(_bot.Slot, _now, goal) is { } said)
        {
            if (said.IsLink)
                _courseNoWaySince = -1;
            else if (said.NoWay && said.Need > 0f)
                (_courseNeed, _courseNoWaySince) = (said.Need, -1);
            else if (said.NoWay && _courseNoWaySince < 0)
                _courseNoWaySince = _now.Value;
        }

        if (_courseNeed > 0f && _self.Health - _courseNeed < RocketJumpHealthFloor)
            return CourseHeal();
        _courseNeed = 0f;

        if (_courseNoWaySince >= 0 && (_now.Value - _courseNoWaySince) * _tick > CourseGiveUpSeconds)
            return CannotGoOn();
        GoTo(goal, MoveSpeed.Run);
        _verdict = stage.State == ObjectiveState.Capturing ? "taking the stage" : "to the next stage";
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
