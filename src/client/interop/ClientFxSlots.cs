using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// IEffects' suppression answers and the gunship overlay over IShimFxClient
/// (shell_effects_client.cpp). Each call is one slot with the arguments as
/// they are; a missing shim draws nothing and answers "suppress".
/// </summary>
internal sealed unsafe class ClientFxSlots(FxClientArea? fxClient) : IFxSuppression, IFxImpactOverlay
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private FxClientArea FxClient => fxClient ?? throw ShimAreaGuard.NotAttached(nameof(FxClient));

    public void ImpactOverlay(Vector position, Vector color)
    {
        if (fxClient is null)
            return;
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        float* c = stackalloc float[] { color.X, color.Y, color.Z };
        FxClient.FxImpactOverlay(p, c);
    }

    public bool CanPredict() => (fxClient is not null) && FxClient.FxCanPredict() != 0;

    public int RecipientCount(Vector origin)
    {
        if (fxClient is null)
            return 0;
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        return FxClient.FxRecipientCount(o);
    }
}
