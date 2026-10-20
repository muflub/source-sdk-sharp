using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// IEffects' suppression answers over the FX area (shell_effects_client.cpp).
/// Each call is one slot with the arguments as they are. (The gunship
/// overlay's FxImpactOverlay slot left with the glow overlays' port, C7: the
/// overlay is the managed GlowOverlaySystem's.)
/// </summary>
internal sealed unsafe class ClientFxSlots(FxClientArea fxClient) : IFxSuppression
{
    public bool CanPredict() => fxClient.FxCanPredict() != 0;

    public int RecipientCount(Vector origin)
    {
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        return fxClient.FxRecipientCount(o);
    }
}
