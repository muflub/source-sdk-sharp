using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// What a breakable surface's brush model says about its material
/// (<c>IVModelInfo::GetModelMaterialCount</c>, the first material's name and
/// its <c>$crackmaterial</c>), for the managed choice of what the broken
/// surface draws with. <see cref="MaterialCount"/> is -1 when the handle
/// names no shell with a model.
/// </summary>
public readonly record struct SurfaceMaterialFacts(int MaterialCount, string Material, bool HasCrackMaterial, string CrackMaterial);

/// <summary>
/// The materials a broken surface draws with, by name: the one its pieces are
/// drawn in, whether that one takes the detail texture swap (only a real
/// cracked material does), whether it is the debug stand-in, the solid
/// block's material and the twelve edge materials, edge shape by edge shape
/// and style by style (<c>edge * 3 + style</c>).
/// </summary>
public readonly record struct SurfaceMaterialChoice(string Pieces, bool SwapsDetail, bool IsDebug, string Box,
                                                    IReadOnlyList<string> Edges);

/// <summary>
/// The native draw of a broken surface (shell_surface_render.cpp): it is handed
/// the materials and the plan (<c>BreakableSurfaceDraw</c>'s words) and builds
/// the meshes the engine's brush surface asks it for.
/// </summary>
public interface IBreakableSurfaceRenderer
{
    /// <summary>The facts the material choice is made from.</summary>
    SurfaceMaterialFacts MaterialFacts(EntityHandle entity);

    /// <summary>The materials the surface's pieces, blocks and edges draw with.</summary>
    void SetMaterials(EntityHandle entity, in SurfaceMaterialChoice choice);

    /// <summary>What to draw: the plan's words, replacing the last; empty draws nothing.</summary>
    void SetPlan(EntityHandle entity, ReadOnlySpan<int> words);
}
