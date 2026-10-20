namespace SourceSharp.Client.Interop;

/// <summary>
/// The native render-target store (shell_render_targets_client.cpp,
/// IShimViewClient's <c>RenderTargetDeclare</c>): what each slot finds and
/// when its reference is dropped is the caller's; native finds the texture
/// on the slot's first use, holds the reference, and drops it at the
/// material system's release in the declared order.
/// </summary>
public interface IRenderTargetTable
{
    /// <summary>
    /// Declares <paramref name="slot"/>: the texture name and group FindTexture
    /// is asked for, its complaint and creation flags, and its place in the
    /// release's order (0: kept across a release). False when refused.
    /// </summary>
    bool Declare(int slot, string name, string group, bool complain, int creationFlags, int releaseOrder);
}
