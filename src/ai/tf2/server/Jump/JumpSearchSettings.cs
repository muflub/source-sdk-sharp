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

    /// <summary>
    /// Double (lane G7b): the first shot of a jump with a second rocket in the air, from standing, the rocket behind and
    /// steeply under, as Up's; its second shot is <see cref="SecondSteps"/> fire commands, from the launcher's refire on.
    /// </summary>
    public JumpGrid Double { get; init; } = new(0f, 55f, 85f, 10f, 0, 4, 2);

    /// <summary>The second rocket's fire commands a Double grid tries: the launcher's refire, then every <see cref="SecondStep"/> more, <see cref="SecondSteps"/> in all.</summary>
    public int SecondStep { get; init; } = 6;

    public int SecondSteps { get; init; } = 5;

    /// <summary>The second rocket's view: straight down and behind, so its blast is under the body and a little back.</summary>
    public float SecondPitch { get; init; } = 89f;

    public float SecondYawOffset { get; init; } = 180f;

    /// <summary>How many commands past the found flight's a second rocket may still be fired as the body comes over its place: after them, never (lane G7c).</summary>
    public int SecondLateCommands { get; init; } = 12;

    /// <summary>The most flights a search of the Double family may spend, beside the single families' <see cref="MaxFlights"/>.</summary>
    public int DoubleMaxFlights { get; init; } = 320;

    /// <summary>
    /// Wall (lane G7c): the first shot of a jump whose second rocket is fired at a wall beside the flight, from standing, the
    /// rocket behind and steeply under (the highest of Up's pitches: the second rocket is fired near the top of the rise).
    /// </summary>
    public JumpGrid WallFirst { get; init; } = new(0f, 75f, 85f, 10f, 0, 2, 2);

    /// <summary>The second rocket's fire commands a Wall grid tries: the launcher's refire, then every <see cref="SecondStep"/> more.</summary>
    public int WallSecondSteps { get; init; } = 3;

    /// <summary>The second rocket's pitches a Wall grid tries: from, step and count (down positive: at the wall under the body).</summary>
    public float WallSecondPitchFrom { get; init; } = 40f;

    public float WallSecondPitchStep { get; init; } = 18f;

    public int WallSecondPitches { get; init; } = 3;

    /// <summary>The second rocket's yaw offsets a Wall grid tries, from the way, on either side: straight across it, then a little behind.</summary>
    public float WallSecondYawFrom { get; init; } = 90f;

    public float WallSecondYawStep { get; init; } = 30f;

    public int WallSecondYaws { get; init; } = 2;

    /// <summary>The most flights a search of the Wall family may spend, beside the other families'.</summary>
    public int WallMaxFlights { get; init; } = 480;

    /// <summary>
    /// A Double's first shot is perturbed as the program flies it (lane G7b), not as a single's is: it is begun only standing
    /// placed within <see cref="RocketJumpProgram.PlaceSlack"/> of its start, its view within
    /// <see cref="RocketJumpProgram.TightDegrees"/> of the shot's whatever the hand, and its trigger on the recipe's own
    /// command (the pilot counts it). Its second shot's view is put off by <see cref="ViewJitter"/> (the pilot fires it within
    /// <see cref="JumpPilot.SecondAimDegrees"/>) and its place by <see cref="StartJitter"/>.
    /// </summary>
    public float DoubleStartJitter { get; init; } = RocketJumpProgram.PlaceSlack;

    public float DoubleViewJitter { get; init; } = RocketJumpProgram.TightDegrees;

    public int DoubleShotJitter { get; init; }

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
