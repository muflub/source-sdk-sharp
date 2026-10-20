using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IManagedPixelVisClient"/>, managed-built: the plan's
/// PixelVisEndCurrentView and PixelVisEndScene ops (shell_pixelvis_client.cpp)
/// ask the managed pixel visibility. Each body counts its crossing first and
/// keeps its own catch; a failure draws nothing.
/// </summary>
public sealed unsafe class PixelVisClientBinding(Crossings crossings, ILog log, IClientPixelVisHost host)
    : IManagedPixelVisClient
{
    public int EndView(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientPixelVisEndView);
        if (a0 == null || a1 <= 0 || a2 == null || a3 < 0)
            return 0;
        try
        {
            return host.EndView(new ReadOnlySpan<float>(a0, a1), new Span<int>(a2, a3));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedPixelVisClient.EndView", e);
            return 0;
        }
    }

    public int Commands(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientPixelVisCommands);
        if (a0 == null || a1 < 0)
            return 0;
        try
        {
            return host.Commands(new Span<int>(a0, a1));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedPixelVisClient.Commands", e);
            return 0;
        }
    }

    public void EndScene()
    {
        crossings.Count(CrossingOp.ClientPixelVisEndScene);
        try { host.EndScene(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedPixelVisClient.EndScene", e); }
    }
}
