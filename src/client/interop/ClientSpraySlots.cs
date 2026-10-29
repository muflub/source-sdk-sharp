using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary><see cref="IClientSprayEngine"/> over IShimTempEntBackendsClient's ClientPlayerLogoDecal: one crossing.</summary>
internal sealed unsafe class ClientSpraySlots(ITempEntBackendsClientArea backends) : IClientSprayEngine
{
    public void PlayerLogoDecal(Vector position, int player, int entity, string material, string texture)
    {
        float* p = stackalloc float[3] { position.X, position.Y, position.Z };
        byte[] m = Vtable.Utf8(material);
        byte[] t = Vtable.Utf8(texture);
        fixed (byte* pm = m)
        fixed (byte* pt = t)
            backends.ClientPlayerLogoDecal(p, player, entity, pm, pt);
    }
}
