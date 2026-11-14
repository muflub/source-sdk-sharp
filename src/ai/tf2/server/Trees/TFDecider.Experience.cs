namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// What the experience store gives a TF2 bot's look (plan_bots_teams_tf2_part_g G.4, lane G4a): on its way
/// with nothing to look at, it looks where its team's players looked from that place (<see cref="LearnedLook"/>),
/// blended in time with its path, the view moved by the motion layer as any look is. Its route's side of
/// the store is the navigation profile's (<see cref="Nav.TFNavProfile.HazardFor"/>).
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>
    /// The share of periods a bot looks along a learned yaw is its glance rate (Alertness's curve, 0.05 to 0.4
    /// in tf2_skills.json) times this, at most <see cref="LearnedLookMost"/>: Alertness 2 (Easy) about 0.3,
    /// 6 (Hard) 0.65, 10 0.9.
    /// </summary>
    public const float LearnedLookPerGlance = 2.5f, LearnedLookMost = 0.9f;

    private LearnedLook _learnedLook;
    private ExperienceLooks? _looks;

    /// <summary>The chance a period of this bot's looks along a learned yaw.</summary>
    public float LearnedLookChance => MathF.Min(LearnedLookMost, LearnedLookPerGlance * _p.GlanceRate);

    /// <summary>Decides that looked along a learned yaw, and those of them whose view already faced it (within <see cref="LearnedLook.FacedDegrees"/>).</summary>
    public int LearnedLookDecides { get; private set; }

    public int LearnedLookFaced { get; private set; }

    /// <summary>The learned look's periods, for the facts and the counters.</summary>
    public LearnedLook LearnedLooks => _learnedLook;

    /// <summary>
    /// A look still along the path, with nobody in sight, a goal, no jump under way and no glance held
    /// (a glance has made the look a Glance already): where its bin has a learned yaw the bot may look along
    /// it for the period. One stimulus a run of one yaw (<see cref="LookKind.Watch"/> by <see cref="LearnedLook.Run"/>),
    /// so the view follows the point as the bot moves and reacts only to a new yaw. With no look row in the
    /// snapshot it does nothing at all.
    /// </summary>
    private void LookWhereTheyLook()
    {
        MapExperience snapshot = _memory.World.Experience;
        if (snapshot.Looks.Count == 0)
            return;
        if (_targetSighted || !_hasGoal || Busy || _look.Kind != LookKind.Path)
        {
            _learnedLook.Stop();
            return;
        }

        if (_looks is null || !ReferenceEquals(_looks.Snapshot, snapshot) || _looks.Context.Role != _self.Team)
            _looks = ExperienceLooks.Of(snapshot, new ExperienceContext(ExperienceContext.Any, _self.Team, ExperienceContext.Any));
        if (!_learnedLook.Step(_now, _tick, LearnedLookChance, _looks, _self.Origin, WayYaw(), OwnEye.Z - _self.Origin.Z, out AiVector at))
            return;
        _look = new LookIntent(LookKind.Watch, at, ActorId.None, AimPoint.Body, 0f, _learnedLook.Run);
        LearnedLookDecides++;
        if (_learnedLook.Faces(_self.Eye.Yaw))
            LearnedLookFaced++;
    }

    /// <summary>The way the bot goes, in degrees: along its velocity when it runs, else toward its goal.</summary>
    private float WayYaw()
    {
        AiVector v = _self.Velocity;
        AiVector d = v.X * v.X + v.Y * v.Y > 50f * 50f ? v : _goal - _self.Origin;
        return MathF.Atan2(d.Y, d.X) * (180f / MathF.PI);
    }
}
