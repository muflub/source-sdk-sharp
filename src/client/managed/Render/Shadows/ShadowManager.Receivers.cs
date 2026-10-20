//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

// D6: the receivers (clientshadowmgr.cpp's AddShadowToReceiver,
// CullReceiver, ComputeSeparatingPlane and RemoveAllShadowsFromReceiver).
public sealed partial class ShadowManager
{
    private bool ComputeSeparatingPlane(nint rend1, nint rend2, out Vector normal)
    {
        _renderables.ShadowRenderBounds(rend1, GetActualShadowCastType(rend1), out Vector min1, out Vector max1);
        _renderables.ShadowRenderBounds(rend2, GetActualShadowCastType(rend2), out Vector min2, out Vector max2);
        return ShadowMath.ComputeSeparatingPlane(_renderables.RenderOrigin(rend1), _renderables.RenderAngles(rend1), min1, max1,
                                                 _renderables.RenderOrigin(rend2), _renderables.RenderAngles(rend2), min2, max2, 3.0f,
                                                 out normal);
    }

    /// <summary>
    /// <c>CullReceiver( h, r, source )</c>: a flashlight culls by its frustum;
    /// a shadow by its volume against the receiver's sphere, then by the
    /// plane that separates receiver from caster (their centres' line, or the
    /// boxes' separating plane when the spheres meet), the receiver kept only
    /// on the side the shadow is cast toward.
    /// </summary>
    private bool CullReceiver(int handle, nint renderable, nint sourceRenderable)
    {
        ref ClientShadow shadow = ref _shadows[handle];
        if ((shadow.Flags & ShadowFlags.Flashlight) != 0)
        {
            _engine.GetFlashlightFrustum(shadow.ShadowHandle, out ShadowFrustum frustum);
            _renderables.RenderBoundsWorldspace(renderable, out Vector mins, out Vector maxs);
            return ShadowMath.CullBox(mins, maxs, frustum);
        }

        ComputeBoundingSphere(renderable, out Vector origin, out float radius);

        _engine.GetInfo(shadow.ShadowHandle, out ShadowEngineInfo info);
        Vector localOrigin = ShadowMath.MultiplyPosition(shadow.WorldToShadow, origin);

        Vector shadowMin = new(-shadow.WorldSizeX * 0.5f, -shadow.WorldSizeY * 0.5f, 0);
        Vector shadowMax = new(shadow.WorldSizeX * 0.5f, shadow.WorldSizeY * 0.5f, info.MaxDist);

        if (!ShadowMath.IsBoxIntersectingSphere(shadowMin, shadowMax, localOrigin, radius))
            return true;

        ComputeBoundingSphere(sourceRenderable, out Vector originSource, out float radiusSource);

        bool foundSeparatingPlane;
        Vector planeNormal;
        if (!ShadowMath.IsSphereIntersectingSphere(originSource, radiusSource, origin, radius))
        {
            foundSeparatingPlane = true;
            // the plane normal doesn't need to be normalized...
            planeNormal = new Vector(origin.X - originSource.X, origin.Y - originSource.Y, origin.Z - originSource.Z);
        }
        else
        {
            foundSeparatingPlane = ComputeSeparatingPlane(renderable, sourceRenderable, out planeNormal);
        }

        if (foundSeparatingPlane)
        {
            Vector shadowDir = GetShadowDirection(sourceRenderable);
            float shadowDot = ShadowMath.Dot(shadowDir, planeNormal);
            float receiverDot = ShadowMath.Dot(planeNormal, origin);
            float sourceDot = ShadowMath.Dot(planeNormal, originSource);

            if (shadowDot > 0.0f)
            {
                if (receiverDot <= sourceDot)
                    return true;
            }
            else
            {
                if (receiverDot >= sourceDot)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>AddShadowToReceiver( h, r, kind )</c>: never onto its own caster, only
    /// onto what takes projected textures and survives the cull; then the
    /// engine call for the receiver's kind. A static prop takes a flashlight;
    /// it took a render-to-texture shadow from an NPC or player in stock, an
    /// arm that asks for a C_BaseEntity and is gone from the fork (no renderable
    /// here is one).
    /// </summary>
    public void AddShadowToReceiver(int handle, nint renderable, int type)
    {
        ref ClientShadow shadow = ref _shadows[handle];

        nint sourceRenderable = _renderables.RenderableOf(shadow.Entity);
        // NOTE: if pSourceRenderable == NULL, the source is probably a flashlight since there is no entity.
        if (sourceRenderable == renderable)
            return;

        if (!_renderables.ShouldReceiveProjectedTextures(renderable, ShadowFlags.ProjectedTextureTypeMask))
            return;

        if (CullReceiver(handle, renderable, sourceRenderable))
            return;

        bool flashlight = (shadow.Flags & ShadowFlags.Flashlight) != 0;
        bool targeted = flashlight && shadow.TargetEntity != InvalidEntity && _renderables.RenderableOf(shadow.TargetEntity) != 0;
        switch (type)
        {
            case ShadowReceivers.BrushModel:
                if (flashlight)
                {
                    if (!targeted || IsFlashlightTarget(handle, renderable))
                    {
                        _engine.AddShadowToBrushModel(shadow.ShadowHandle, _renderables.Model(renderable), _renderables.RenderOrigin(renderable),
                                                      _renderables.RenderAngles(renderable));
                        _engine.AddFlashlightRenderable(shadow.ShadowHandle, renderable);
                    }
                }
                else
                {
                    _engine.AddShadowToBrushModel(shadow.ShadowHandle, _renderables.Model(renderable), _renderables.RenderOrigin(renderable),
                                                  _renderables.RenderAngles(renderable));
                }
                break;

            case ShadowReceivers.StaticProp:
                if (GetActualShadowCastType(handle) == ShadowTypes.RenderToTexture)
                {
                    // Stock added them only when an NPC or player cast them; no renderable is a C_BaseEntity here.
                }
                else if (flashlight)
                {
                    if (!targeted || IsFlashlightTarget(handle, renderable))
                    {
                        _engine.AddShadowToStaticProp(shadow.ShadowHandle, renderable);
                        _engine.AddFlashlightRenderable(shadow.ShadowHandle, renderable);
                    }
                }
                break;

            case ShadowReceivers.StudioModel:
                if (flashlight)
                {
                    if (!targeted || IsFlashlightTarget(handle, renderable))
                    {
                        _renderables.CreateModelInstance(renderable);
                        _engine.AddShadowToModel(shadow.ShadowHandle, _renderables.ModelInstance(renderable));
                        _engine.AddFlashlightRenderable(shadow.ShadowHandle, renderable);
                    }
                }
                break;
        }
    }

    /// <summary><c>RemoveAllShadowsFromReceiver( r, kind )</c>: the engine's call for the kind, for what takes projected textures.</summary>
    public void RemoveAllShadowsFromReceiver(nint renderable, int type)
    {
        if (!_renderables.ShouldReceiveProjectedTextures(renderable, ShadowFlags.ProjectedTextureTypeMask))
            return;

        switch (type)
        {
            case ShadowReceivers.BrushModel:
                _engine.RemoveAllShadowsFromBrushModel(_renderables.Model(renderable));
                break;
            case ShadowReceivers.StaticProp:
                _engine.RemoveAllShadowsFromStaticProp(renderable);
                break;
            case ShadowReceivers.StudioModel:
                int instance = _renderables.ModelInstance(renderable);
                if (renderable != 0 && instance != ShadowFlags.InvalidModelInstance)
                    _engine.RemoveAllShadowsFromModel(instance);
                break;
        }
    }
}
