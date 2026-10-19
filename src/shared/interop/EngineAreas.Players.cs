// Players, and what a player carries and sees.

namespace SourceSharp.Abi;

/// <summary>
/// Drawing on the screen, and reading the local player it is drawn for.
///
/// <para>
/// CLIENT ONLY, and the only area of which that is true in this strict a sense:
/// the draw calls are valid only inside the HUD paint pass, and the local-player
/// reads have no meaning on a server that has many.
/// </para>
/// </summary>
/// <summary>
/// How much room a string takes in the HUD's font.
///
/// A zero width is "there is no font yet" rather than "an empty string", which
/// is a real state during start-up — and a caller that laid text out against it
/// would pile every right-aligned line on the margin.
/// </summary>
public readonly record struct TextSize(int Width, int Height)
{
    /// <summary>Whether the engine could answer at all.</summary>
    public bool IsKnown => Width > 0;
}
