using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>The two kinds of blast jump a search looks for (plan_bots_teams_tf2_part_g G.5 step 4).</summary>
public enum JumpFamily : byte
{
    /// <summary>Onto something higher: from standing, the rocket under the feet.</summary>
    Up = 0,

    /// <summary>Along the way: from a run, the rocket behind.</summary>
    Forward = 1,

    /// <summary>Up, with a second rocket in the air (lane G7b): fired at a floor the flight passes over once the launcher is ready again.</summary>
    Double = 2,

    /// <summary>Up, with a second rocket in the air fired at a wall beside the flight once the launcher is ready again (lane G7c): a wall jump.</summary>
    Wall = 3,
}

/// <summary>
/// A blast jump's second rocket, fired in the air (lane G7b): at a floor the flight passes over (or a wall beside it, lane
/// G7c) once the launcher is
/// ready again, so its blast lifts the body a second time. The pilot fires it as the body comes over the place it was
/// fired from in the flight the search found, no sooner than the launcher is ready and no later than its latest: a place,
/// not a time, so a first blast a little off still meets the floor where the search found it.
/// </summary>
/// <param name="Over">Where the body was over the ground when the found flight's second rocket left, and the floor's height there: the rocket is fired once the body is past it along the way, and never from under it.</param>
/// <param name="Earliest">Commands from the first trigger's first command before which it is never fired: the launcher's refire.</param>
/// <param name="Latest">Commands from the first trigger's first command after which it is never fired: a body that has not come over <paramref name="Over"/> by then fires nothing (lane G7c).</param>
/// <param name="Pitch">The view's pitch when it leaves, degrees, down positive.</param>
/// <param name="YawOffset">The view's yaw when it leaves, against the direction of travel, degrees.</param>
public readonly record struct JumpSecondShot(Vector Over, int Earliest, int Latest, float Pitch, float YawOffset)
{
    /// <summary>
    /// Whether the rocket is fired at a wall beside the flight (lane G7c), not at a floor under it: the pilot fires it by the
    /// same rule (<see cref="Over"/>'s height is then where the found flight's rocket met the wall); the store keeps such a
    /// recipe under a format of its own.
    /// </summary>
    public bool Wall { get; init; }
}

/// <summary>
/// One blast jump, named by the few numbers a pilot needs to fly it
/// (plan_bots_teams_tf2_part_g G.5 steps 2 and 4): where it leaves the ground
/// and where it means to come down, how far behind the takeoff the run at it
/// starts, where the view is when the rocket leaves, when the rocket leaves
/// against the jump, and whether the body crouches. The direction of travel is
/// the line from <see cref="Takeoff"/> to <see cref="Landing"/> over the ground.
/// <see cref="JumpPilot"/> turns it into commands, a tick at a time.
/// </summary>
/// <param name="Takeoff">Where the feet are when the jump is made.</param>
/// <param name="Landing">Where the feet are meant to come down.</param>
/// <param name="RunUp">How far behind <paramref name="Takeoff"/>, back along the direction of travel, the run at it starts; 0 for a jump from standing.</param>
/// <param name="Pitch">The view's pitch when the rocket leaves, degrees, down positive.</param>
/// <param name="YawOffset">The view's yaw when the rocket leaves, against the direction of travel, degrees: 180 looks back the way it came.</param>
/// <param name="ShotDelay">Commands from the jump's command to the trigger's: 0 the same command, negative the trigger first.</param>
/// <param name="Ducked">Whether the jump is crouched: the duck is taken with the jump and held to the landing.</param>
/// <param name="Second">A second rocket fired in the air (lane G7b), or none: a one-rocket recipe is what it was before it.</param>
public readonly record struct JumpRecipe(Vector Takeoff, Vector Landing, float RunUp, float Pitch, float YawOffset, int ShotDelay, bool Ducked,
                                         JumpSecondShot? Second = null)
{
    /// <summary>The rockets the recipe fires: one, or two with a second shot.</summary>
    public int Rockets => Second is null ? 1 : 2;

    /// <summary>The view the second rocket leaves along, or the first's when there is none.</summary>
    public Vector SecondView => Second is { } s ? new Vector(s.Pitch, JumpPilot.WrapDegrees(TravelYaw + s.YawOffset), 0f) : ShotView;

    /// <summary>The direction of travel over the ground, degrees; 0 when the landing is straight over the takeoff.</summary>
    public float TravelYaw
    {
        get
        {
            float dx = Landing.X - Takeoff.X, dy = Landing.Y - Takeoff.Y;
            return dx * dx + dy * dy < 1e-6f ? 0f : MathF.Atan2(dy, dx) * (180f / MathF.PI);
        }
    }

    /// <summary>The view the rocket leaves along: (pitch, yaw, 0).</summary>
    public Vector ShotView => new(Pitch, JumpPilot.WrapDegrees(TravelYaw + YawOffset), 0f);

    /// <summary>
    /// Where a body that flies it starts in a simulation: standing
    /// <see cref="RunUp"/> behind the takeoff, looking the way it will go.
    /// </summary>
    public JumpStart Start()
    {
        float yaw = TravelYaw * (MathF.PI / 180f);
        var origin = new Vector(Takeoff.X - MathF.Cos(yaw) * RunUp, Takeoff.Y - MathF.Sin(yaw) * RunUp, Takeoff.Z);
        return new JumpStart(origin, Vector.Zero, new Vector(0f, TravelYaw, 0f), Ducked: false, OnGround: true);
    }
}
