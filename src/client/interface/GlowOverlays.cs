using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>IShimViewClient's GlowMaterialFind: materials-&gt;FindMaterial by name and group, no reference taken; the effect list's material id, 0 for none.</summary>
public interface IGlowMaterials
{
    int Find(string name, string group);
}

/// <summary>
/// What native's glow overlay ops reach (IManagedGlowClient): a draw hands the
/// view (<c>SS_GLOW_VIEW_FLOATS</c>) and takes the commands as words; the sky
/// overlays' update hands the view's origin.
/// </summary>
public interface IClientGlowHost
{
    /// <summary>The draw's commands into <paramref name="commands"/>; how many words they are (more than fit: ask <see cref="Commands"/>).</summary>
    int DrawOverlays(ReadOnlySpan<float> view, bool cacheFullSceneState, Span<int> commands);

    /// <summary>The last draw's commands again, into a larger buffer; how many were written.</summary>
    int Commands(Span<int> commands);

    void UpdateSkyOverlays(in Vector origin, float zFar, bool cacheFullSceneState);
}

/// <summary>
/// The command words a glow overlay draw answers with (shell_glow_client.h's
/// SS_GLOW_CMD_*): Clip mode (0 on, its answer kept; 1 off; 2 the kept answer);
/// HdrScale material value; Quad material and four (position, colour, s, t);
/// WireQuad material and four positions. Floats travel as their bits.
/// </summary>
public enum GlowCommand
{
    Clip = 1,
    HdrScale = 2,
    Quad = 3,
    WireQuad = 4,
}

/// <summary>c_te_explosion.cpp's fireball flash: the explosion's warp overlay, at its origin.</summary>
public interface IExplosionFireball
{
    void Fireball(Vector origin);
}
