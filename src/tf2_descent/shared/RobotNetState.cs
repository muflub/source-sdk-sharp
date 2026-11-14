using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client
// (plan_side_isolation §2.4): the robot body's and its weapon's networked
// state (plan_descent_logic HO2, 3.7, 3.11 rows 2 and 4).

namespace TF2.Descent.Shared;

/// <summary>The flags a robot body networks in <see cref="RobotNetState.Flags"/>.</summary>
[Flags]
public enum RobotFlags : byte
{
    None = 0,

    /// <summary>The body is lent to a robot; clear while it is parked.</summary>
    Lent = 1,

    /// <summary>The robot carries champion affixes.</summary>
    Champion = 2,

    /// <summary>The robot is its level's boss.</summary>
    Boss = 4,

    /// <summary>The robot's life is spent; its body goes back to the pool at the next decision.</summary>
    Dead = 8,
}

/// <summary>
/// A <c>tf2_descent_robot</c> body's own networked state: what the engine's
/// entity block does not send (the block carries origin, angles, model, skin
/// and bounds; HO0 found no sequence, cycle, scale or pose parameter reaches a
/// client any other way), and what the health bar and the client's look need.
///
/// <para>
/// The client plays <see cref="Sequence"/> from the start each time
/// <see cref="SequenceSerial"/> changes, scales the model by
/// <see cref="ModelScale"/> and sets the four pose parameters the player
/// animations blend on (<c>move_x</c>, <c>move_y</c>, <c>body_pitch</c>,
/// <c>body_yaw</c>). <see cref="Life"/> is the life left as a fraction of the
/// robot's maximum, 0 to 255. <see cref="Template"/> is the robot's row in
/// <c>robots.json</c> plus one (0 for a parked body), <see cref="Level"/> its
/// monster level, <see cref="Statuses"/> a bit per row of <c>status.json</c>
/// in table order (bit 0 the first row).
/// </para>
/// </summary>
[StandaloneNetState(ClassName)]
public partial class RobotNetState
{
    public const string ClassName = "tf2_descent_robot";

    public partial float ModelScale { get; set; }

    public partial short Sequence { get; set; }

    public partial byte SequenceSerial { get; set; }

    public partial float MoveX { get; set; }

    public partial float MoveY { get; set; }

    public partial float BodyPitch { get; set; }

    public partial float BodyYaw { get; set; }

    public partial byte Life { get; set; }

    public partial byte Template { get; set; }

    public partial byte Level { get; set; }

    public partial byte Flags { get; set; }

    public partial byte Statuses { get; set; }
}

/// <summary>
/// A <c>tf2_descent_robot_weapon</c>: the model a robot holds, its own entity
/// (one edict, plan section 2), parented to its body on the server and
/// bone-merged to it on the client. <see cref="OwnerIndex"/> is the body's
/// entity index, 0 while parked.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class RobotWeaponNetState
{
    public const string ClassName = "tf2_descent_robot_weapon";

    public partial short OwnerIndex { get; set; }
}
