using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// <see cref="NavNumbers"/>' stock movement, read from the server's movement
/// code: here and not beside the numbers, so SourceSharp.Ai.Nav references no
/// game logic. A static extension, so callers still write <c>NavNumbers.Stock</c>.
/// </summary>
public static class StockNavNumbers
{
    extension(NavNumbers)
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
}
