using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The engine's debug-overlay text list, as the client's shell reads it
/// (shell_debugoverlay_client.cpp): one entry at a time, already placed on
/// the screen by the engine's ScreenPosition.
/// </summary>
public interface IClientDebugOverlay
{
    /// <summary>
    /// Entry <paramref name="index"/>: -1 past the list's end, 0 when it has no
    /// text or is off the screen, 1 with its pixel position, line offset, colour
    /// (0xRRGGBBAA) and text (UTF-8, NUL-terminated in <paramref name="text"/>).
    /// </summary>
    int Text(int index, out float x, out float y, out int line, out uint rgba, Span<byte> text);

    /// <summary>The engine drops the overlays whose time is up (stock's Paint did, last).</summary>
    void ClearDead();
}
