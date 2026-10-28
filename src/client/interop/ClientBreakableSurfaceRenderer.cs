using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The standalone area's surface slots (shell_surface_render.cpp): the brush
/// model's material facts, the materials managed chose and the plan, one
/// crossing each.
/// </summary>
internal sealed unsafe class ClientBreakableSurfaceRenderer(IStandaloneClientArea standalone) : IBreakableSurfaceRenderer
{
    // MAX_PATH, for the two names the facts answer.
    private const int NameBytes = 260;

    // shell_surface_render.h's SURFACE_MATERIALS_* flags.
    private const int SwapsDetailFlag = 1 << 0;
    private const int DebugFlag = 1 << 1;

    public SurfaceMaterialFacts MaterialFacts(EntityHandle entity)
    {
        byte* material = stackalloc byte[NameBytes];
        byte* crack = stackalloc byte[NameBytes];
        int hasCrack = 0;
        material[0] = 0;
        crack[0] = 0;
        int count = standalone.SurfaceMaterialFacts(entity.Value, material, NameBytes, crack, NameBytes, &hasCrack);
        return new SurfaceMaterialFacts(count, Text(material), hasCrack != 0, Text(crack));
    }

    public void SetMaterials(EntityHandle entity, in SurfaceMaterialChoice choice)
    {
        var names = new StringBuilder();
        names.Append(choice.Pieces).Append('\0').Append(choice.Box).Append('\0');
        foreach (string edge in choice.Edges)
            names.Append(edge).Append('\0');
        byte[] bytes = Encoding.UTF8.GetBytes(names.ToString());
        int flags = (choice.SwapsDetail ? SwapsDetailFlag : 0) | (choice.IsDebug ? DebugFlag : 0);
        fixed (byte* p = bytes)
            standalone.SurfaceSetMaterials(entity.Value, p, bytes.Length, flags);
    }

    public void SetPlan(EntityHandle entity, ReadOnlySpan<int> words)
    {
        fixed (int* p = words)
            standalone.SurfaceSetPlan(entity.Value, p, words.Length);
    }

    private static string Text(byte* p)
    {
        int n = 0;
        while (n < NameBytes && p[n] != 0)
            n++;
        return Encoding.UTF8.GetString(p, n);
    }
}
