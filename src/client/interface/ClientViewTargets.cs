using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>A render target's facts: its size, the vgui texture id that samples it, and the views drawn into it since load.</summary>
public readonly record struct ViewTargetInfo(int Width, int Height, int Texture, int Renders);

/// <summary>
/// The second cameras' render targets (lane camproto): IShimViewClient's
/// <c>RenderViewToTarget</c> and <c>ViewTargetInfo</c>. What to draw and when
/// is the caller's; native carries a queued view out at the frame's render
/// (shell_view_targets.cpp).
/// </summary>
public interface IViewTargets
{
    /// <summary>Queues <paramref name="setup"/> for this frame's render into <paramref name="target"/>; false when refused.</summary>
    bool Render(int target, in ViewTargetSetup setup);

    /// <summary>The target's facts, or false when there is no such target.</summary>
    bool Info(int target, out ViewTargetInfo info);
}
