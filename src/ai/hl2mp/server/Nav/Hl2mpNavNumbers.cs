using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>
/// HL2MP's body and movement numbers, as the derivation splits a
/// <c>.nav3d</c>'s jump links and classes its cells by (plan_hl2mp_ai §6.2):
/// the player hull (32 wide, 72 standing, 36 crouched), the step, the highest
/// plain jump (jump height + step, 21 + 18 = 39), the highest crouch jump
/// (the file's own 56), and the highest safe drop (the fall speed that does
/// no damage under the game's gravity: 526.5² / (2 × 600) ≈ 231). Read from
/// the movement code, not restated (Hl2mpNavNumbersTests pins them).
/// </summary>
public readonly record struct Hl2mpNavNumbers(
    float HullWidth,
    float StandHeight,
    float CrouchHeight,
    float StepHeight,
    float MaxJumpRise,
    float MaxCrouchJumpRise,
    float SafeDrop)
{
    /// <summary>The numbers of the core's default movement, with the file's crouch-jump height.</summary>
    public static Hl2mpNavNumbers Stock(float fileJumpHeight)
    {
        MovementParams m = Movement.Current;
        float v = MoveMath.PlayerMaxSafeFallSpeed;
        return new Hl2mpNavNumbers(32f, 72f, 36f, m.StepSize, m.JumpHeight + m.StepSize, fileJumpHeight, v * v / (2f * m.Gravity));
    }
}

/// <summary>What crossing each edge type adds to its distance (a jump's effort, a door's wait), in world units.</summary>
public readonly record struct NavEdgeCosts(float Jump, float CrouchJump, float Drop, float Ladder, float DoorWait)
{
    public static NavEdgeCosts Default => new(16f, 32f, 8f, 64f, 64f);
}
