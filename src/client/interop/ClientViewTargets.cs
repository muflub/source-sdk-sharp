using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real targets, over the slots.</summary>
internal sealed unsafe class ClientViewTargets(ViewClientArea viewClient) : IViewTargets
{
    public bool Render(int target, in ViewTargetSetup setup)
    {
        var copy = setup;
        return viewClient.RenderViewToTarget(target, &copy) != 0;
    }

    public bool Info(int target, out ViewTargetInfo info)
    {
        int* out4 = stackalloc int[4];
        if (viewClient.ViewTargetInfo(target, out4) == 0)
        {
            info = default;
            return false;
        }
        info = new ViewTargetInfo(out4[0], out4[1], out4[2], out4[3]);
        return true;
    }
}
