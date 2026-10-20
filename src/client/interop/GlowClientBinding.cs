using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IManagedGlowClient"/>, managed-built: the plan's GlowDrawOverlays
/// and GlowUpdateSkyOverlays ops (shell_glow_client.cpp) ask the managed glow
/// overlays. Each body counts its crossing first and keeps its own catch; a
/// failure draws nothing.
/// </summary>
public sealed unsafe class GlowClientBinding(Crossings crossings, ILog log, IClientGlowHost host) : IManagedGlowClient
{
    public int DrawOverlays(void* a0, int a1, int a2, void* a3, int a4)
    {
        crossings.Count(CrossingOp.ClientGlowDrawOverlays);
        if (a0 == null || a1 <= 0 || a3 == null || a4 < 0)
            return 0;
        try
        {
            return host.DrawOverlays(new ReadOnlySpan<float>(a0, a1), a2 != 0, new Span<int>(a3, a4));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedGlowClient.DrawOverlays", e);
            return 0;
        }
    }

    public int Commands(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientGlowCommands);
        if (a0 == null || a1 < 0)
            return 0;
        try
        {
            return host.Commands(new Span<int>(a0, a1));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedGlowClient.Commands", e);
            return 0;
        }
    }

    public void UpdateSkyOverlays(void* a0, float a1, int a2)
    {
        crossings.Count(CrossingOp.ClientGlowUpdateSkyOverlays);
        if (a0 == null)
            return;
        try
        {
            float* o = (float*)a0;
            host.UpdateSkyOverlays(new Vector(o[0], o[1], o[2]), a1, a2 != 0);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedGlowClient.UpdateSkyOverlays", e);
        }
    }
}

/// <summary>The view area's GlowMaterialFind, one crossing.</summary>
internal sealed unsafe class ClientGlowMaterials(ViewClientArea viewClient) : IGlowMaterials
{
    public int Find(string name, string group)
    {
        byte[] n = Encoding.UTF8.GetBytes(name + "\0");
        byte[] g = Encoding.UTF8.GetBytes(group + "\0");
        fixed (byte* pn = n)
        fixed (byte* pg = g)
            return viewClient.GlowMaterialFind(pn, pg);
    }
}
