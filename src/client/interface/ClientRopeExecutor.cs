using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The view a draw is happening in (with the render context's camera,
/// which a strip faces), and the facts about the screen a rope's widths read
/// (<c>ClientViewState</c>).</summary>
public readonly record struct ClientViewState(
    Vector Forward, Vector Origin, Vector MainOrigin, int ScreenWidth, int DxLevel, bool AAEnabled,
    Vector Camera);

/// <summary>One hull trace against the world (<c>ClientTraceHullWorld</c>).</summary>
public readonly record struct ClientHullTrace(float Fraction, bool AllSolid, bool StartSolid, Vector PlaneNormal, float PlaneDist);

/// <summary>
/// <c>engine-&gt;ComputeLighting( pos, NULL, true, colour )</c>: the light at
/// a point, averaged, and the six box colours when asked (IShimEffectsRenderClient).
/// </summary>
public interface IClientComputeLighting
{
    Vector ComputeLighting(in Vector pos, Span<Vector> boxColors);
}

/// <summary>
/// The native rope executor (shell_rope_render.cpp) and the reads a managed
/// rope makes of the engine. Game thread; <see cref="Draw"/> only from inside a
/// draw callback.
/// </summary>
public interface IClientRopeExecutor
{
    /// <summary>The handle for a material (and whether it has a "_back"
    /// partner); -1 when there is no name. The same name answers the same
    /// handle until the level ends.</summary>
    int Material(string name, out int textureHeight, out bool hasBack);

    /// <summary>Queues one strip for this view's rope pass. Both spans are the
    /// camera-facing vertices BeamSegDraw made, seventeen floats a vertex
    /// (beam_strip_vertex.h); the solid one is empty or the same length.</summary>
    void Draw(int material, ReadOnlySpan<float> vertices, ReadOnlySpan<float> solid, int flags);

    /// <summary>The view being drawn (only when <paramref name="insideView"/>:
    /// a draw callback), the main view, and the screen.</summary>
    ClientViewState View(bool insideView);

    /// <summary><c>engine-&gt;ComputeLighting</c>: the averaged colour, and the six box colours.</summary>
    Vector ComputeLighting(in Vector pos, Span<Vector> boxColors);

    /// <summary>A hull trace against the world only.</summary>
    ClientHullTrace TraceHullWorld(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask);
}
