namespace SourceSharp.Ai.TF2.Jump;

/// <summary>One family's coarse grid: the run-up, and the pitches and shot delays tried.</summary>
public readonly record struct JumpGrid(float RunUp, float PitchFrom, float PitchTo, float PitchStep, int DelayFrom, int DelayTo, int DelayStep)
{
    /// <summary>The pitches and the delays it tries: its flights are one by the other.</summary>
    public int Pitches => (int)MathF.Floor((PitchTo - PitchFrom) / PitchStep + 1e-3f) + 1;

    public int Delays => (DelayTo - DelayFrom) / DelayStep + 1;
}

/// <summary>
/// A search's numbers. The perturbations are plan_bots_teams_tf2_part_g G.5
/// step 4's: the takeoff place ± 16 u, the view ± 3 degrees, the shot ± 2
/// commands; a jump is worth keeping at 60 % of its trials or more.
/// </summary>
public sealed record JumpSearchSettings
{
    /// <summary>Up: from standing, the rocket behind and steeply under.</summary>
    public JumpGrid Up { get; init; } = new(0f, 45f, 85f, 10f, 0, 4, 2);

    /// <summary>Forward: from a run, the rocket behind and shallower, the trigger at the jump or before it.</summary>
    public JumpGrid Forward { get; init; } = new(160f, 30f, 60f, 10f, -3, 3, 3);

    /// <summary>The view's yaw against the direction of travel a grid shoots at: behind.</summary>
    public float YawOffset { get; init; } = 180f;

    /// <summary>How far each way the refinement moves the yaw offset.</summary>
    public float RefineYaw { get; init; } = 15f;

    /// <summary>Perturbed trials a recipe's robustness is the share of.</summary>
    public int Trials { get; init; } = 20;

    /// <summary>Hits whose robustness is measured, the likeliest first.</summary>
    public int Measured { get; init; } = 4;

    /// <summary>The robustness a jump is worth keeping at.</summary>
    public float Keep { get; init; } = 0.6f;

    public float StartJitter { get; init; } = 16f;

    public float ViewJitter { get; init; } = 3f;

    public int ShotJitter { get; init; } = 2;

    /// <summary>How far from the landing's height a body may rest and still be on it: a step.</summary>
    public float HeightTolerance { get; init; } = 18f;

    /// <summary>Commands one flight is given, approach to rest.</summary>
    public int MaxCommands { get; init; } = 600;

    /// <summary>The most flights one search may spend; it stops looking further when they are spent.</summary>
    public int MaxFlights { get; init; } = 160;
}
