using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>The two kinds of blast jump a search looks for (plan_bots_teams_tf2_part_g G.5 step 4).</summary>
public enum JumpFamily : byte
{
    /// <summary>Onto something higher: from standing, the rocket under the feet.</summary>
    Up = 0,

    /// <summary>Along the way: from a run, the rocket behind.</summary>
    Forward = 1,
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
public readonly record struct JumpRecipe(Vector Takeoff, Vector Landing, float RunUp, float Pitch, float YawOffset, int ShotDelay, bool Ducked)
{
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
