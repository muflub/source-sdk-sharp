namespace SourceSharp.Ai;

/// <summary>
/// A bot on its way with nothing to look at looks where players look from that place (plan_bots_teams_tf2_part_g
/// G.4, lane G4a). The look is held in periods of <see cref="MinPeriodSeconds"/> to <see cref="MaxPeriodSeconds"/>,
/// drawn from the bot's own stream; at each period's start, where its bin has a learned yaw it can use, the bot
/// draws whether this period looks along that yaw (at its Alertness's chance) or along its path, so the two are
/// blended in time. A yaw is used only where it was seen for <see cref="MinSeconds"/> with at least
/// <see cref="MinShare"/> of the bin's seconds, and within <see cref="MaxOffWayDegrees"/> of the way the bot goes
/// (the look of players walking the other way is not its look). Where no bin has a look nothing is drawn, so a
/// bot with an empty store looks along its path exactly as before.
/// </summary>
public sealed class LearnedLook(AiRandom random)
{
    /// <summary>How long a period's choice is held.</summary>
    public const float MinPeriodSeconds = 0.6f, MaxPeriodSeconds = 1.5f;

    /// <summary>The counted seconds a bin's yaw must have been seen for: a human's 2 s, a bot's 8.</summary>
    public const double MinSeconds = 2d;

    /// <summary>The share of a bin's looking its yaw must have: where players look all round, there is no yaw to take.</summary>
    public const double MinShare = 0.3d;

    /// <summary>The furthest a learned yaw may be from the bot's way.</summary>
    public const float MaxOffWayDegrees = 100f;

    /// <summary>How far along the yaw the point looked at is: far, so the bearing barely moves within a decide.</summary>
    public const float Reach = 1024f;

    /// <summary>A view within this of the learned yaw faces it (the counters', and G.4's fact's).</summary>
    public const float FacedDegrees = 25f;

    private readonly AiRandom _random = random;
    private Tick _until;
    private bool _on;
    private float _yaw;
    private int _run;

    /// <summary>Periods begun, and those that looked along a learned yaw.</summary>
    public int Periods { get; private set; }

    public int LearnedPeriods { get; private set; }

    /// <summary>The yaw the look is on now, in degrees, while <see cref="Step"/> answers true.</summary>
    public float Yaw => _yaw;

    /// <summary>
    /// Names a run of learned looks along one yaw: the same while consecutive periods keep the yaw, so the
    /// view is not made to react anew (a look's stimulus), and new when the yaw changes.
    /// </summary>
    public int Run => _run;

    /// <summary>Something else has the look (a target, a jump, a glance), or the bot has no way: the period ends.</summary>
    public void Stop()
    {
        _until = default;
        _on = false;
    }

    /// <summary>
    /// One decide of a bot at <paramref name="self"/> going along <paramref name="wayYaw"/> (degrees) with nothing
    /// to look at. True while the look is a learned yaw, at <paramref name="at"/>: a point <see cref="Reach"/> along
    /// it at <paramref name="eyeHeight"/> over the bot's feet, moved with the bot.
    /// </summary>
    public bool Step(Tick now, float tickSeconds, float chance, ExperienceLooks looks, AiVector self, float wayYaw, float eyeHeight, out AiVector at)
    {
        ArgumentNullException.ThrowIfNull(looks);
        at = default;
        if (now < _until)
        {
            if (!_on)
                return false;
            if (MathF.Abs(HumanMotion.AngleDelta(wayYaw, _yaw)) > MaxOffWayDegrees)
            {
                // The way has turned off the yaw (a corner): the period ends.
                Stop();
                return false;
            }

            at = Point(self, eyeHeight);
            return true;
        }

        bool wasOn = _on;
        float wasYaw = _yaw;
        _on = false;
        if (looks.IsEmpty || Usable(looks.At(WorldBin.Of(self)), wayYaw) is not { } yaw)
        {
            _until = default;
            return false;
        }

        float seconds = MinPeriodSeconds + (MaxPeriodSeconds - MinPeriodSeconds) * _random.NextFloat();
        _until = now + Math.Max(1, (int)(seconds / tickSeconds));
        Periods++;
        if (_random.NextFloat() >= chance)
            return false;
        _on = true;
        _yaw = yaw;
        if (!wasOn || wasYaw != yaw)
            _run++;
        LearnedPeriods++;
        at = Point(self, eyeHeight);
        return true;
    }

    /// <summary>The yaw of <paramref name="look"/> where a bot going along <paramref name="wayYaw"/> takes it, else null.</summary>
    public static float? Usable(ExperienceLook? look, float wayYaw)
        => look is { } l && l.Seconds >= MinSeconds && l.Share >= MinShare && MathF.Abs(HumanMotion.AngleDelta(wayYaw, l.Yaw)) <= MaxOffWayDegrees
            ? l.Yaw
            : null;

    /// <summary>Whether a view of <paramref name="viewYaw"/> faces the learned yaw now.</summary>
    public bool Faces(float viewYaw) => _on && MathF.Abs(HumanMotion.AngleDelta(viewYaw, _yaw)) <= FacedDegrees;

    private AiVector Point(AiVector self, float eyeHeight)
    {
        float r = _yaw * (MathF.PI / 180f);
        return self + new AiVector(MathF.Cos(r) * Reach, MathF.Sin(r) * Reach, eyeHeight);
    }
}
