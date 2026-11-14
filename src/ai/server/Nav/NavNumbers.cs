using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// A game's body and movement numbers, as the derivation splits a
/// <c>.nav3d</c>'s jump links and classes its cells by (plan_hl2mp_ai §6.2):
/// the player hull's width, standing and crouched heights, the step, the
/// highest plain jump, the highest crouch jump, the highest safe drop, and the
/// highest rise a double jump reaches (0 for a game without one: no edge is a
/// <see cref="NavEdgeType.DoubleJump"/>), and whether the hull is wider than two voxels
/// (<see cref="WideHull"/>, TF2's 48 over 16 u voxels): such a box at a column's centre
/// reaches into the columns beside it, so the derivation stands it on a stair's step a step
/// up, and drops it past a ledge as the two-voxel body that clears the edge it leaves.
/// <see cref="CrouchOnlyCost"/> multiplies what a crouch-only place costs to cross (1, HL2MP's: as any
/// place), for a game whose players all but never duck to get round: TF2's.
/// A game's <see cref="INavProfile"/>
/// publishes them; <see cref="Stock"/> is the core's default movement, HL2MP's
/// (NavNumbersTests pins them).
/// </summary>
public readonly record struct NavNumbers(
    float HullWidth,
    float StandHeight,
    float CrouchHeight,
    float StepHeight,
    float MaxJumpRise,
    float MaxCrouchJumpRise,
    float SafeDrop,
    float MaxDoubleJumpRise = 0f,
    bool WideHull = false,
    float CrouchOnlyCost = 1f)
{
    /// <summary>
    /// The core's default movement (HL2MP's): the player hull (32 wide, 72
    /// standing, 36 crouched), the step, the highest plain jump (jump height +
    /// step, 21 + 18 = 39), the highest crouch jump (the file's own 56), and
    /// the highest safe drop (the fall speed that does no damage under the
    /// game's gravity: 526.5² / (2 × 600) ≈ 231). Read from the movement code,
    /// not restated. No double jump.
    /// </summary>
    public static NavNumbers Stock(float fileJumpHeight)
    {
        MovementParams m = Movement.Current;
        float v = MoveMath.PlayerMaxSafeFallSpeed;
        return new NavNumbers(32f, 72f, 36f, m.StepSize, m.JumpHeight + m.StepSize, fileJumpHeight, v * v / (2f * m.Gravity));
    }
}

/// <summary>What crossing each edge type adds to its distance (a jump's effort, a door's wait), in world units.</summary>
public readonly record struct NavEdgeCosts(float Jump, float CrouchJump, float Drop, float Ladder, float DoorWait, float DoubleJump = 48f)
{
    public static NavEdgeCosts Default => new(16f, 32f, 8f, 64f, 64f, 48f);

    /// <summary>What crossing an edge of <paramref name="type"/> adds to its distance.</summary>
    public float ExtraFor(NavEdgeType type) => type switch
    {
        NavEdgeType.Jump => Jump,
        NavEdgeType.CrouchJump => CrouchJump,
        NavEdgeType.Drop => Drop,
        NavEdgeType.Ladder => Ladder,
        NavEdgeType.Door => DoorWait,
        NavEdgeType.DoubleJump => DoubleJump,
        _ => 0f,
    };
}
