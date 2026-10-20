//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

// D7: the render-to-texture pass (clientshadowmgr.cpp's
// ComputeShadowTextures, DrawRenderToTextureShadow, the LOD fallback,
// DrawShadowHierarchy and the texcoords).
public sealed partial class ShadowManager
{
    /// <summary>
    /// <c>ComputeShadowTextures( view, leaves )</c>: with render-to-texture on
    /// and both r_shadows gates open, the visible render-to-texture shadows in
    /// the leaves, largest on screen first, each redrawn into its atlas
    /// fragment while r_shadowmaxrendered allows, the rest on their blobs.
    /// </summary>
    public void ComputeShadowTextures(ReadOnlySpan<int> leaves)
    {
        if (!_renderToTextureActive || _settings.Shadows == 0 || _settings.ShadowsGameControl == 0)
            return;

        int count = _visible.FindShadows(leaves);
        if (count == 0)
            return;

        // Clear to white (color unused), black alpha
        _context.ClearColor4ub(255, 255, 255, 0);

        // No height clip mode please.
        int oldHeightClipMode = _context.GetHeightClipMode();
        _context.SetHeightClipMode(MaterialHeightClipModeDisable);

        // No projection matrix (orthographic mode)
        _context.MatrixMode(MaterialProjection);
        _context.PushMatrix();
        _context.LoadIdentity();
        _context.Scale(1, -1, 1);
        _context.Ortho(0, 0, 1, 1, -9999, 0);

        _context.MatrixMode(MaterialView);
        _context.PushMatrix();

        _context.PushRenderTargetAndViewport(_allocator.Page);

        if (_renderTargetNeedsClear)
        {
            // don't need to clear absent depth buffer
            _context.ClearBuffers(true, false, false);
            _renderTargetNeedsClear = false;
        }

        int maxShadows = _settings.ShadowMaxRendered;
        int modelsRendered = 0;
        for (int i = 0; i < count; ++i)
        {
            ref readonly VisibleShadowInfo info = ref _visible.GetVisibleShadow(i);
            if (modelsRendered < maxShadows)
            {
                if (DrawRenderToTextureShadow(info.Shadow, info.Area))
                    ++modelsRendered;
            }
            else
            {
                DrawRenderToTextureShadowLOD(info.Shadow);
            }
        }

        // Render to the backbuffer again
        _context.PopRenderTargetAndViewport();

        // Restore the matrices
        _context.MatrixMode(MaterialProjection);
        _context.PopMatrix();

        _context.MatrixMode(MaterialView);
        _context.PopMatrix();

        _context.SetHeightClipMode(oldHeightClipMode);
        _context.SetHeightClipMode(oldHeightClipMode);

        // Restore the clear color
        _context.ClearColor3ub(0, 0, 0);
    }

    private bool DrawRenderToTextureShadow(int handle, float area)
    {
        ref ClientShadow shadow = ref _shadows[handle];

        // If we were previously using the LOD shadow, set the material
        bool previouslyUsingLodShadow = (shadow.Flags & ShadowFlags.UsingLodShadow) != 0;
        shadow.Flags &= ~ShadowFlags.UsingLodShadow;
        if (previouslyUsingLodShadow)
            _engine.SetShadowMaterial(shadow.ShadowHandle, _renderShadow, _renderModelShadow, handle);

        // Mark texture as being used...
        bool dirtyTexture = (shadow.Flags & ShadowFlags.TextureDirty) != 0;
        bool drewTexture = false;
        bool needsRedraw = _allocator.UseTexture(shadow.ShadowTexture, dirtyTexture, area);

        if (!_allocator.HasValidTexture(shadow.ShadowTexture))
        {
            DrawRenderToTextureShadowLOD(handle);
            return false;
        }

        if (needsRedraw || dirtyTexture)
        {
            // shadow to be redrawn; for now, we'll always do it.
            nint renderable = _renderables.RenderableOf(shadow.Entity);

            _allocator.GetTextureRect(shadow.ShadowTexture, out int x, out int y, out int w, out int h);
            _context.Viewport(x, y, w, h);

            // Clear the selected viewport only (don't need to clear depth)
            _context.ClearBuffers(true, false, false);

            _context.MatrixMode(MaterialView);
            _engine.GetInfo(shadow.ShadowHandle, out ShadowEngineInfo info);
            _context.LoadMatrix(info.WorldToShadow);

            if (DrawShadowHierarchy(renderable, shadow.Flags, false))
            {
                drewTexture = true;
                ++_rttShadowTexturesDrawn;
            }

            // Only clear the dirty flag if the caster isn't animating
            if ((shadow.Flags & ShadowFlags.AnimatingSource) == 0)
                shadow.Flags &= ~ShadowFlags.TextureDirty;

            SetRenderToTextureShadowTexCoords(shadow.ShadowHandle, x, y, w, h);
        }
        else if (previouslyUsingLodShadow)
        {
            // In this case, we were previously using the LOD shadow, but we didn't
            // have to reconstitute the texture. In this case, we need to reset the texcoord
            _allocator.GetTextureRect(shadow.ShadowTexture, out int x, out int y, out int w, out int h);
            SetRenderToTextureShadowTexCoords(shadow.ShadowHandle, x, y, w, h);
        }

        return drewTexture;
    }

    /// <summary><c>DrawRenderToTextureShadowLOD( h )</c>: the blob, until the texture comes back.</summary>
    private void DrawRenderToTextureShadowLOD(int handle)
    {
        ref ClientShadow shadow = ref _shadows[handle];
        if ((shadow.Flags & ShadowFlags.UsingLodShadow) == 0)
        {
            _engine.SetShadowMaterial(shadow.ShadowHandle, _simpleShadow, _simpleShadow, Invalid);
            _engine.SetShadowTexCoord(shadow.ShadowHandle, 0, 0, 1, 1);
            ClearExtraClipPlanes(handle);
            shadow.Flags |= ShadowFlags.UsingLodShadow;
        }
    }

    /// <summary>
    /// <c>DrawShadowHierarchy( r, shadow, child )</c>: the caster drawn as its
    /// kind (the root by its brush flag, a child by its model type), stopping
    /// at a blob child, looking through a child without a shadow. A NULL root
    /// draws nothing (stock dereferences it).
    /// </summary>
    private bool DrawShadowHierarchy(nint renderable, int rootFlags, bool child)
    {
        if (renderable == 0)
            return false;

        bool drewTexture = false;

        // Stop traversing when we hit a blobby shadow
        int shadowType = GetActualShadowCastType(renderable);
        if (shadowType == ShadowTypes.Simple)
            return false;

        if (shadowType != ShadowTypes.None)
        {
            bool drawModelShadow;
            bool drawBrushShadow;
            if (!child)
            {
                drawModelShadow = (rootFlags & ShadowFlags.BrushModel) == 0;
                drawBrushShadow = !drawModelShadow;
            }
            else
            {
                int modelType = _renderables.ModelType(_renderables.Model(renderable));
                drawModelShadow = modelType == ModStudio;
                drawBrushShadow = modelType == ModBrush;
            }

            if (drawModelShadow)
            {
                _context.DrawModelShadow(renderable, _renderables.Body(renderable), _renderables.Skin(renderable));
                drewTexture = true;
            }
            else if (drawBrushShadow)
            {
                _context.DrawBrushModelShadow(renderable);
                drewTexture = true;
            }
        }

        for (nint c = _renderables.FirstShadowChild(renderable); c != 0; c = _renderables.NextShadowPeer(c))
        {
            if (DrawShadowHierarchy(c, rootFlags, true))
                drewTexture = true;
        }
        return drewTexture;
    }

    /// <summary><c>SetRenderToTextureShadowTexCoords</c>: the fragment's rectangle, half a texel in.</summary>
    private void SetRenderToTextureShadowTexCoords(int engineShadow, int x, int y, int w, int h)
    {
        int textureW = ShadowTextureAllocator.TotalTextureSize, textureH = ShadowTextureAllocator.TotalTextureSize;
        float u = ((float)x + 0.5f) / textureW;
        float v = ((float)y + 0.5f) / textureH;
        float du = ((float)w - 1) / textureW;
        float dv = ((float)h - 1) / textureH;
        _engine.SetShadowTexCoord(engineShadow, u, v, du, dv);
    }

    /// <summary><c>AdvanceFrame</c>: the atlas's frame, once a frame (not a view).</summary>
    public void AdvanceFrame() => _allocator.AdvanceFrame();

    /// <summary>
    /// The visible list's <c>EnumShadow</c>: a render-to-texture shadow not yet
    /// rendered this frame, not transparent, not a hierarchy child, inside the
    /// view; its screen area recorded and its frame stamped.
    /// </summary>
    internal void EnumShadow(int clientShadowHandle, List<VisibleShadowInfo> inView)
    {
        ref ClientShadow shadow = ref _shadows[clientShadowHandle];

        // Don't bother if we rendered it this frame, no matter which view it was rendered for
        if (shadow.RenderFrame == _world.FrameCount)
            return;

        // We don't need to bother with it if it's not render-to-texture
        if (GetActualShadowCastType(clientShadowHandle) != ShadowTypes.RenderToTexture)
            return;

        // Don't bother with it if the shadow is totally transparent
        _engine.GetInfo(shadow.ShadowHandle, out ShadowEngineInfo info);
        if (info.FalloffBias == 255)
            return;

        nint renderable = _renderables.RenderableOf(shadow.Entity);

        // Don't bother with children of hierarchy; they will be drawn with their parents
        if (ShouldUseParentShadow(renderable) || WillParentRenderBlobbyShadow(renderable))
            return;

        ComputeBoundingSphere(renderable, out Vector absCenter, out float radius);
        ComputeShadowBBox(renderable, absCenter, radius, out Vector absMins, out Vector absMaxs);

        // Make sure it's in the frustum. If it isn't it's not interesting
        if (_world.CullBox(absMins, absMaxs))
            return;

        float diameter = _context.ComputePixelDiameterOfSphere(absCenter, radius);
        inView.Add(new VisibleShadowInfo(clientShadowHandle, diameter * diameter));

        shadow.RenderFrame = _world.FrameCount;
    }

    /// <summary>The leaf system's enumeration, into the visible list.</summary>
    internal void EnumerateShadowsInLeaves(ReadOnlySpan<int> leaves, IShadowLeafEnumerator enumerator) =>
        _leafSystem.EnumerateShadowsInLeaves(leaves, enumerator);
}

/// <summary>One shadow the pass may draw, and its screen area.</summary>
public readonly record struct VisibleShadowInfo(int Shadow, float Area);

/// <summary>
/// clientshadowmgr.cpp's <c>CVisibleShadowList</c>: the render-to-texture
/// shadows in a view's leaves (through the leaf system's enumeration), sorted
/// largest on screen first by stock's selection sort (ties keep the
/// enumeration's order).
/// </summary>
internal sealed class VisibleShadowList(ShadowManager manager) : IShadowLeafEnumerator
{
    private readonly List<VisibleShadowInfo> _inView = [];
    private readonly List<int> _priority = [];

    public int Count => _inView.Count;

    public ref readonly VisibleShadowInfo GetVisibleShadow(int i) =>
        ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_inView)[_priority[i]];

    public void EnumShadow(int userId) => manager.EnumShadow(userId, _inView);

    public int FindShadows(ReadOnlySpan<int> leaves)
    {
        _inView.Clear();
        manager.EnumerateShadowsInLeaves(leaves, this);
        int count = _inView.Count;
        if (count != 0)
            PrioritySort();
        return count;
    }

    private void PrioritySort()
    {
        int count = _inView.Count;
        _priority.Clear();
        for (int i = 0; i < count; ++i)
            _priority.Add(i);

        for (int i = 0; i < count - 1; ++i)
        {
            int largest = i;
            float largestArea = _inView[_priority[i]].Area;
            for (int j = i + 1; j < count; ++j)
            {
                int index = _priority[j];
                if (largestArea < _inView[index].Area)
                {
                    largest = j;
                    largestArea = _inView[index].Area;
                }
            }
            (_priority[i], _priority[largest]) = (_priority[largest], _priority[i]);
        }
    }

    /// <summary>The list's handles and areas in priority order (the facts' view).</summary>
    public IEnumerable<VisibleShadowInfo> Sorted()
    {
        for (int i = 0; i < _inView.Count; i++)
            yield return _inView[_priority[i]];
    }
}
