using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// IEffects' suppression answers and the gunship overlay over IShimFxClient
/// (shell_effects_client.cpp). Each call is one slot with the arguments as
/// they are; a missing shim draws nothing and answers "suppress".
/// </summary>
internal sealed unsafe class ClientFxSlots(EngineSide api) : IFxSuppression, IFxImpactOverlay
{
    public void ImpactOverlay(Vector position, Vector color)
    {
        if (!api.ClientAttached)
            return;
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        float* c = stackalloc float[] { color.X, color.Y, color.Z };
        api.ClientShims.FxImpactOverlay(p, c);
    }

    public bool CanPredict() => api.ClientAttached && api.ClientShims.FxCanPredict() != 0;

    public int RecipientCount(Vector origin)
    {
        if (!api.ClientAttached)
            return 0;
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        return api.ClientShims.FxRecipientCount(o);
    }
}
