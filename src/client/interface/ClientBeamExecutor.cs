using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The native beam executor (shell_beam_render.cpp) and the engine reads the
/// managed beam system makes. Game thread; the draw calls only from inside a
/// beam renderable's draw (<see cref="IClientBeamCallbacks.Draw"/>).
/// </summary>
public interface IClientBeamExecutor
{
    /// <summary>The view basis (only when <paramref name="insideView"/>), the
    /// main view's up and right, the world-to-screen matrix, the clock and
    /// (only when <paramref name="insideView"/>) the render context's camera: 42
    /// floats in BeamView's order (origin, forward, right, up, angles, main up,
    /// main right, matrix, curtime, frametime, camera).</summary>
    void View(bool insideView, Span<float> out42);

    /// <summary>Whether a model index resolves, and its frame count.</summary>
    bool ModelInfo(int modelIndex, out int frameCount);

    /// <summary><c>modelinfo-&gt;GetModelIndex( name )</c>.</summary>
    int ModelIndex(string name);

    /// <summary>The handle of the entity at an entry, or <see cref="EntityHandle.None"/>.</summary>
    EntityHandle EntityAt(int entry);

    /// <summary>Whether an entity resolves and has a model.</summary>
    bool EntityHasModel(EntityHandle entity);

    /// <summary>
    /// What an entity endpoint's position is decided from: the BeamEntityFact
    /// bits (resolved, SDK, renderable, player, attachment answered, has a
    /// collideable), and in <paramref name="out9"/> the attachment (asked only
    /// when <paramref name="tryAttachment"/>), the collision centre and the
    /// render origin.
    /// </summary>
    int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9);

    /// <summary><c>IsBoxVisible</c> and <c>CullBox</c> together.</summary>
    void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled);

    /// <summary>The halo's sight test (stock's handle-less pixel visibility).</summary>
    float HaloVisibility(in Vector origin, float proxySize);

    /// <summary><c>debugoverlay-&gt;ScreenPosition</c>.</summary>
    Vector ScreenPosition(in Vector point);

    /// <summary>A translucent leaf renderable at an origin with a box; its index.</summary>
    int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs);

    /// <summary>The renderable moved or grew.</summary>
    void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs);

    /// <summary>The renderable goes.</summary>
    void RenderableRemove(int renderable);

    /// <summary>Binds a sprite model's material (or the wireframe one).</summary>
    bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe);

    /// <summary>Sets <c>$hdrcolorscale</c> on a sprite's material for a render mode.</summary>
    void SetHdrColorScale(int modelIndex, int renderMode, float scale);

    /// <summary>Whether a sprite has a glow material, or (<paramref name="bind"/>) binds it.</summary>
    bool Glow(int modelIndex, bool bind);

    /// <summary>One camera-facing strip: the vertices BeamSegDraw made,
    /// seventeen floats a vertex (beam_strip_vertex.h).</summary>
    void Strip(ReadOnlySpan<float> vertices);

    /// <summary>One dynamic mesh: nine floats a vertex.</summary>
    void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices);
}
