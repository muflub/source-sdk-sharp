using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// The post-processing ops a pass can ask the executor for: the calls
/// viewpostprocess.cpp makes on the render context, one op each, in the order
/// asked. The core's passes write through it and so does a mod's
/// <see cref="IPostProcessContributor"/>.
/// </summary>
public interface IPostOps
{
    /// <summary>A material's id for the ops (<c>materials-&gt;FindMaterial</c>, held by native); 0 when none.</summary>
    int Material(string name);

    /// <summary>UpdateScreenEffectTexture( 0, x, y, w, h, destFullScreen ).</summary>
    void CopyScreenEffect(int x, int y, int w, int h, bool destFullScreen);

    /// <summary>SetRenderTarget( texture ), NULL for <see cref="PostTexture.None"/>.</summary>
    void SetRenderTarget(PostTexture texture);

    /// <summary>SetRenderTarget( texture ), then Viewport( 0, 0, its actual size ).</summary>
    void SetRenderTargetAndViewport(PostTexture texture);

    /// <summary>PushRenderTargetAndViewport().</summary>
    void PushRenderTargetAndViewport();

    /// <summary>PopRenderTargetAndViewport().</summary>
    void PopRenderTargetAndViewport();

    /// <summary>
    /// DrawScreenSpaceRectangle( material, dest, src corners, src size,
    /// the world renderable or NULL, dice ).
    /// </summary>
    void DrawRect(int material, int x, int y, int w, int h, float srcX0, float srcY0, float srcX1, float srcY1,
                  int srcWidth, int srcHeight, bool worldRenderable = false, int diceX = 1, int diceY = 1);

    /// <summary>A material var's SetFloatValue.</summary>
    void SetVarFloat(int material, PostVar var, float value);

    /// <summary>SetScissorRect( x, y, w, h, enable ).</summary>
    void Scissor(int x, int y, int w, int h, bool enable);

    /// <summary>ClearColor3ub.</summary>
    void ClearColor3ub(int r, int g, int b);

    /// <summary>Viewport.</summary>
    void Viewport(int x, int y, int w, int h);

    /// <summary>ClearBuffers( color, depth ).</summary>
    void ClearBuffers(bool color, bool depth);

    /// <summary>ClearColor4ub.</summary>
    void ClearColor4ub(int r, int g, int b, int a);

    /// <summary>PushRenderTargetAndViewport( texture ).</summary>
    void PushRenderTarget(PostTexture texture);

    /// <summary>
    /// Begins a dynamic mesh of <paramref name="count"/> primitives with
    /// <paramref name="material"/> bound (to the world renderable when asked),
    /// inside identity view and projection matrices when asked; nothing draws
    /// when <paramref name="unlessDisabled"/> and the material's own $disabled is set.
    /// </summary>
    void MeshBegin(int material, PostPrimitive primitive, int count, bool identityMatrices, bool worldRenderable, bool unlessDisabled);

    /// <summary>A vertex: position, texture coordinate, and a colour when given.</summary>
    void MeshVertex(float x, float y, float z, float u, float v, (float R, float G, float B, float A)? color = null);

    /// <summary>A vertex with its tangent frame: position, coordinate, colour when given, TangentS, TangentT, Normal.</summary>
    void MeshVertex(float x, float y, float z, float u, float v, (float R, float G, float B, float A)? color,
                    SourceSharp.Abi.Vector tangentS, SourceSharp.Abi.Vector tangentT, SourceSharp.Abi.Vector normal);

    /// <summary>Ends the mesh and draws it.</summary>
    void MeshEnd();
}

/// <summary>
/// Where a mod's pass runs: stock's pyro-vision point in DoEnginePostProcessing
/// (after the engine-post passes, before the render-target dump), with what
/// stock's pyro pass read there.
/// </summary>
public sealed class PostVisionContext
{
    /// <summary>The frame's post-processing inputs.</summary>
    public PostProcessInputs Inputs { get; internal set; }

    /// <summary>Whether a pass already wrote the frame buffer this frame (stock's bFBUpdated): a pass that reads it copies it again.</summary>
    public bool FrameBufferUpdated { get; internal set; }

    /// <summary>The post passes' destination rectangle (partialViewportPostDestRect).</summary>
    public int DestX { get; internal set; }

    public int DestY { get; internal set; }

    public int DestWidth { get; internal set; }

    public int DestHeight { get; internal set; }

    /// <summary>The post passes' source corners in the quarter-size bloom texture (partialViewportPostSrcCorners).</summary>
    public float SrcX0 { get; internal set; }

    public float SrcY0 { get; internal set; }

    public float SrcX1 { get; internal set; }

    public float SrcY1 { get; internal set; }

    /// <summary>A render target's actual size.</summary>
    public int TextureWidth(PostTexture texture) => Inputs.TextureWidth(texture);

    public int TextureHeight(PostTexture texture) => Inputs.TextureHeight(texture);

    /// <summary>_rt_SmallFB1's actual size.</summary>
    public int SmallWidth { get; internal set; }

    public int SmallHeight { get; internal set; }
}

/// <summary>
/// A mod's own screen-space pass in the engine's post-processing (a mod's
/// vision filter, say). The core calls each at stock's vision point every
/// frame the combined post passes run; one that has nothing to draw writes
/// nothing. Registered through the mod's side-service registrar
/// (<c>AddHooks&lt;IPostProcessContributor&gt;</c>).
/// </summary>
public interface IPostProcessContributor
{
    void AtVisionPoint(PostVisionContext context, IPostOps ops);
}

/// <summary><see cref="IPostOps"/> over a plan builder.</summary>
public sealed class PostOpsWriter(RenderPlanBuilder b, Func<string, int> materialOf) : IPostOps
{
    public int Material(string name) => materialOf(name);

    public void CopyScreenEffect(int x, int y, int w, int h, bool destFullScreen) =>
        b.Add(RenderOpKind.PostCopyScreenEffect, [x, y, w, h, destFullScreen ? 1 : 0]);

    public void SetRenderTarget(PostTexture texture) => b.Add(RenderOpKind.PostSetRenderTarget, [(int)texture]);

    public void SetRenderTargetAndViewport(PostTexture texture) =>
        b.Add(RenderOpKind.PostSetRenderTargetAndViewport, [(int)texture]);

    public void PushRenderTargetAndViewport() => b.Op(RenderOpKind.PostPushRenderTargetAndViewport);

    public void PopRenderTargetAndViewport() => b.Op(RenderOpKind.PopRenderTargetAndViewport);

    public void DrawRect(int material, int x, int y, int w, int h, float srcX0, float srcY0, float srcX1, float srcY1,
                         int srcWidth, int srcHeight, bool worldRenderable = false, int diceX = 1, int diceY = 1) =>
        b.Add(RenderOpKind.PostDrawRect, [material, x, y, w, h, srcWidth, srcHeight],
              [srcX0, srcY0, srcX1, srcY1, diceX, diceY, worldRenderable ? 1f : 0f]);

    public void SetVarFloat(int material, PostVar var, float value) =>
        b.Add(RenderOpKind.PostMaterialVarFloat, [material, (int)var], [value]);

    public void Scissor(int x, int y, int w, int h, bool enable) => b.Add(RenderOpKind.PostScissor, [x, y, w, h, enable ? 1 : 0]);

    public void ClearColor3ub(int r, int g, int b2) => b.Add(RenderOpKind.PostClearColor3ub, [r, g, b2]);

    public void Viewport(int x, int y, int w, int h) => b.Add(RenderOpKind.PostViewport, [x, y, w, h]);

    public void ClearBuffers(bool color, bool depth) => b.Add(RenderOpKind.PostClearBuffers, [color ? 1 : 0, depth ? 1 : 0]);

    public void ClearColor4ub(int r, int g, int b2, int a) => b.Add(RenderOpKind.PostClearColor4ub, [r, g, b2, a]);

    public void PushRenderTarget(PostTexture texture) => b.Add(RenderOpKind.PostPushRenderTarget, [(int)texture]);

    public void MeshBegin(int material, PostPrimitive primitive, int count, bool identityMatrices, bool worldRenderable, bool unlessDisabled) =>
        b.Add(RenderOpKind.PostMeshBegin, [material, (int)primitive, count,
              (identityMatrices ? 1 : 0) | (worldRenderable ? 2 : 0) | (unlessDisabled ? 4 : 0)]);

    public void MeshVertex(float x, float y, float z, float u, float v, (float R, float G, float B, float A)? color = null)
    {
        var c = color ?? default;
        b.Add(RenderOpKind.PostMeshVertex, [color.HasValue ? 1 : 0, BitConverter.SingleToInt32Bits(c.A)], [x, y, z, u, v, c.R, c.G, c.B]);
    }

    public void MeshVertex(float x, float y, float z, float u, float v, (float R, float G, float B, float A)? color,
                           SourceSharp.Abi.Vector tangentS, SourceSharp.Abi.Vector tangentT, SourceSharp.Abi.Vector normal)
    {
        var c = color ?? default;
        b.Add(RenderOpKind.PostMeshVertex, [(color.HasValue ? 1 : 0) | 2, BitConverter.SingleToInt32Bits(c.A)], [x, y, z, u, v, c.R, c.G, c.B]);
        b.Add(RenderOpKind.PostMeshFrame, [BitConverter.SingleToInt32Bits(normal.Z)],
              [tangentS.X, tangentS.Y, tangentS.Z, tangentT.X, tangentT.Y, tangentT.Z, normal.X, normal.Y]);
    }

    public void MeshEnd() => b.Op(RenderOpKind.PostMeshEnd);

    // The core's own ops, which a contributor has no business asking for.
    internal void QueryBegin(int bucket) => b.Add(RenderOpKind.PostQueryBegin, [bucket]);

    internal void QueryEnd(int bucket) => b.Add(RenderOpKind.PostQueryEnd, [bucket]);

    internal void Stencil(int mode) => b.Add(RenderOpKind.PostStencil, [mode]);

    internal void GoalToneScale(float value) => b.Add(RenderOpKind.PostGoalToneScale, default, [value]);

    internal void ResetToneScale(float value) => b.Add(RenderOpKind.PostResetToneScale, default, [value]);

    internal void EnableColorCorrection(bool enable) => b.Add(RenderOpKind.PostEnableColorCorrection, [enable ? 1 : 0]);

    internal void DumpTga(int w, int h, PostDump name, int index) => b.Add(RenderOpKind.PostDumpTga, [w, h, (int)name, index]);

    internal void ReadbackNext(int mask) => b.Add(RenderOpKind.PostReadbackNext, [mask]);
}
