using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real targets, over the slots.</summary>
internal sealed unsafe class ClientViewTargets(ViewClientArea? viewClient) : IViewTargets
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private ViewClientArea ViewClient => viewClient ?? throw ShimAreaGuard.NotAttached(nameof(ViewClient));

    public bool Render(int target, in ViewTargetSetup setup)
    {
        var copy = setup;
        return ViewClient.RenderViewToTarget(target, &copy) != 0;
    }

    public bool Info(int target, out ViewTargetInfo info)
    {
        int* out4 = stackalloc int[4];
        if (ViewClient.ViewTargetInfo(target, out4) == 0)
        {
            info = default;
            return false;
        }
        info = new ViewTargetInfo(out4[0], out4[1], out4[2], out4[3]);
        return true;
    }
}
