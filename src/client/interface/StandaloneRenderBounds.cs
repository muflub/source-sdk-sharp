namespace SourceSharp.Client.Interop;

/// <summary>
/// The world-space surrounding boxes of standalone shells
/// (<c>ICollideable::WorldSpaceSurroundingBounds</c>: the render bounds about
/// the render origin), for a pass that measures from them.
/// </summary>
public interface IStandaloneRenderBounds
{
    /// <summary>
    /// Six floats per handle into <paramref name="minsMaxs"/> (mins, then
    /// maxs), a zero box for a handle that names no shell; how many named one.
    /// </summary>
    int SurroundingBounds(ReadOnlySpan<uint> handles, Span<float> minsMaxs);
}
