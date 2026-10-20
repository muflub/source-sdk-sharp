//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

// D3: when each shadow is re-projected (clientshadowmgr.cpp's PreRender,
// AddToDirtyShadowList, MarkRenderToTextureShadowDirty, UpdateShadow,
// UpdateProjectedTexture and the shadow-parent hierarchy).
public sealed partial class ShadowManager
{
    /// <summary>
    /// <c>WillParentRenderBlobbyShadow( r )</c>: the nearest shadow-casting
    /// parent casts a blob (a parent without a shadow is looked through).
    /// </summary>
    public bool WillParentRenderBlobbyShadow(nint renderable)
    {
        if (renderable == 0)
            return false;
        nint parent = _renderables.ShadowParent(renderable);
        if (parent == 0)
            return false;
        int shadowType = GetActualShadowCastType(parent);
        if (shadowType == ShadowTypes.None)
            return WillParentRenderBlobbyShadow(parent);
        return shadowType == ShadowTypes.Simple;
    }

    /// <summary><c>ShouldUseParentShadow( r )</c>: the nearest shadow-casting parent renders to texture.</summary>
    public bool ShouldUseParentShadow(nint renderable)
    {
        if (renderable == 0)
            return false;
        nint parent = _renderables.ShadowParent(renderable);
        if (parent == 0)
            return false;
        int shadowType = GetActualShadowCastType(parent);
        if (shadowType == ShadowTypes.Simple)
            return false;
        if (shadowType == ShadowTypes.None)
            return ShouldUseParentShadow(parent);
        return true;
    }

    /// <summary>
    /// <c>PreRender</c>: the depth textures' and render-to-texture's switches
    /// first (a render-to-texture switch dirties everything and ends the call);
    /// then the dirty set, walked in handle order, each re-projected; the
    /// transparent ones stay dirty.
    /// </summary>
    public void PreRender()
    {
        if (_settings.FlashlightDepthTexture && !_materials.SupportsShadowDepthTextures())
        {
            _settings.SetFlashlightDepthTexture(0);
            ShutdownDepthTextureShadows();
        }

        bool depthTextureActive = _settings.FlashlightDepthTexture;
        int depthTextureResolution = _settings.FlashlightDepthResolution;
        if (depthTextureActive != _depthTextureActive || depthTextureResolution != _depthTextureResolution)
        {
            if (depthTextureActive && _depthTextureActive && depthTextureResolution != _depthTextureResolution)
            {
                ShutdownDepthTextureShadows();
                InitDepthTextureShadows();
            }
            else if (_depthTextureActive && !depthTextureActive)
            {
                ShutdownDepthTextureShadows();
            }
            else if (depthTextureActive && !_depthTextureActive)
            {
                InitDepthTextureShadows();
            }
        }

        bool renderToTextureActive = _settings.RenderToTexture;
        if (renderToTextureActive != _renderToTextureActive)
        {
            if (_renderToTextureActive)
                ShutdownRenderToTextureShadows();
            else
                InitRenderToTextureShadows();
            UpdateAllShadows();
            return;
        }

        _updatingDirtyShadows = true;

        // The walk: stock's in-order traversal of its RB tree. A shadow the
        // update destroys is freed out of the tree under the walk, and the
        // next in-order step of a freed node is no node: the walk ends there.
        _dirtyShadows.CopyTo(_walkScratch);
        for (int i = 0; i < _walkScratch.Count; i++)
        {
            int handle = _walkScratch[i];
            int before = _dirtyShadows.Occurrences(handle);
            UpdateProjectedTextureInternal(handle, false);
            if (_dirtyShadows.Occurrences(handle) < before)
                break;
        }
        _dirtyShadows.Clear();

        // Transparent shadows must remain dirty, since they were not re-projected
        foreach (int h in _transparentShadows)
            _dirtyShadows.Insert(h);
        _transparentShadows.Clear();

        _updatingDirtyShadows = false;
    }

    private nint GetParentShadowEntity(int handle)
    {
        nint renderable = _renderables.RenderableOf(_shadows[handle].Entity);
        if (renderable != 0 && ShouldUseParentShadow(renderable))
        {
            nint parent = _renderables.ShadowParent(renderable);
            while (GetActualShadowCastType(parent) == ShadowTypes.None)
                parent = _renderables.ShadowParent(parent);
            return parent;
        }
        return 0;
    }

    /// <summary><c>AddToDirtyShadowList( h, force )</c>: into the dirty set, and the parent it renders into with it.</summary>
    public void AddToDirtyShadowList(int handle, bool force)
    {
        if (_updatingDirtyShadows)
            return;
        if (handle == Invalid)
            return;

        _dirtyShadows.Insert(handle);

        // This pretty much guarantees we'll recompute the shadow
        if (force)
            _shadows[handle].LastAngles = new QAngle(float.MaxValue, float.MaxValue, float.MaxValue);

        nint parent = GetParentShadowEntity(handle);
        if (parent != 0)
            AddToDirtyShadowList(parent, force);
    }

    /// <summary><c>AddToDirtyShadowList( renderable, force )</c>: refused while dirty already, or with no shadow.</summary>
    public void AddToDirtyShadowList(nint renderable, bool force)
    {
        if (_updatingDirtyShadows)
            return;
        if (_renderables.IsShadowDirty(renderable))
            return;
        int handle = _renderables.ShadowHandle(renderable);
        if (handle == Invalid)
            return;
        _renderables.MarkShadowDirty(renderable, true);
        AddToDirtyShadowList(handle, force);
    }

    /// <summary><c>MarkRenderToTextureShadowDirty( h )</c>: its texture, and its parent's, redrawn on next use.</summary>
    public void MarkRenderToTextureShadowDirty(int handle)
    {
        if (handle == Invalid)
            return;
        _shadows[handle].Flags |= ShadowFlags.TextureDirty;

        nint parent = GetParentShadowEntity(handle);
        if (parent != 0)
        {
            int parentHandle = _renderables.ShadowHandle(parent);
            if (parentHandle != Invalid)
                _shadows[parentHandle].Flags |= ShadowFlags.TextureDirty;
        }
    }

    private static bool Same(in Vector a, in Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private static bool Same(in QAngle a, in QAngle b) => a.Pitch == b.Pitch && a.Yaw == b.Yaw && a.Roll == b.Roll;

    /// <summary>
    /// <c>UpdateShadow( h, force )</c>: retired when its entity is gone;
    /// nothing without a model; disabled (and kept dirty) while transparent,
    /// or while a parent renders it; else enabled and re-projected when it
    /// moved, under no fog.
    /// </summary>
    private void UpdateShadow(int handle, bool force)
    {
        nint renderable = _renderables.RenderableOf(_shadows[handle].Entity);
        if (renderable == 0)
        {
            // Retire the shadow if the entity is gone
            DestroyShadow(handle);
            return;
        }

        if (_renderables.Model(renderable) == 0)
        {
            _renderables.MarkShadowDirty(renderable, false);
            return;
        }

        int engineShadow = _shadows[handle].ShadowHandle;
        _engine.GetInfo(engineShadow, out ShadowEngineInfo info);
        if (info.FalloffBias == 255)
        {
            _engine.EnableShadow(engineShadow, false);
            _transparentShadows.Add(handle);
            return;
        }

        if (_break)
            _break = false;

        if (ShouldUseParentShadow(renderable) || WillParentRenderBlobbyShadow(renderable))
        {
            _engine.EnableShadow(engineShadow, false);
            _renderables.MarkShadowDirty(renderable, false);
            return;
        }

        _engine.EnableShadow(engineShadow, true);

        // Figure out if the shadow moved...
        Vector origin = _renderables.RenderOrigin(renderable);
        QAngle angles = _renderables.RenderAngles(renderable);

        ref ClientShadow shadow = ref _shadows[handle];
        if (force || !Same(origin, shadow.LastOrigin) || !Same(angles, shadow.LastAngles))
        {
            shadow.LastOrigin = origin;
            shadow.LastAngles = angles;

            nint model = _renderables.Model(renderable);
            int fogMode = _context.GetFogMode();
            _context.FogMode(MaterialFogNone);
            switch (_renderables.ModelType(model))
            {
                case ModBrush:
                    UpdateBrushShadow(renderable, handle);
                    break;
                case ModStudio:
                    UpdateStudioShadow(renderable, handle);
                    break;
                default:
                    // Shouldn't get here if not a brush or studio
                    break;
            }
            _context.FogMode(fogMode);
        }

        // NOTE: We can't do this earlier because pEnt->GetRenderOrigin() can
        // provoke a recomputation of render origin, which, for aiments, can cause everything
        // to be marked as dirty. So don't clear the flag until this point.
        _renderables.MarkShadowDirty(renderable, false);
    }

    private void UpdateProjectedTextureInternal(int handle, bool force)
    {
        if ((_shadows[handle].Flags & ShadowFlags.Flashlight) != 0)
        {
            _engine.EnableShadow(_shadows[handle].ShadowHandle, true);
            // FIXME: What's the difference between brush and model shadows for light projectors? Answer: nothing.
            UpdateBrushShadow(0, handle);
        }
        else
        {
            UpdateShadow(handle, force);
        }
    }

    /// <summary><c>UpdateProjectedTexture( h, force )</c>: for flashlights only (a shadow's is warned about).</summary>
    public void UpdateProjectedTexture(int handle, bool force)
    {
        if (handle == Invalid)
            return;
        if ((_shadows[handle].Flags & ShadowFlags.Flashlight) == 0)
        {
            _console.Warning("CClientShadowMgr::UpdateProjectedTexture can only be used with flashlights!");
            return;
        }
        UpdateProjectedTextureInternal(handle, force);
        RemoveShadowFromDirtyList(handle);
    }
}
