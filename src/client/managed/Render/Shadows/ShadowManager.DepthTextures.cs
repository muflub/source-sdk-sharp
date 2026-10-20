//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

// D8: the flashlight depth textures (clientshadowmgr.cpp's
// BuildActiveShadowDepthList, ComputeShadowDepthTextures, the locks and
// SetViewFlashlightState).
public sealed partial class ShadowManager
{
    private const int MaxActiveDepthShadows = 1024;

    /// <summary>
    /// <c>BuildActiveShadowDepthList</c>: each flashlight with a depth texture
    /// that wants shadows and whose frustum's box the view sees, up to the
    /// list's size; the others are told they have none.
    /// </summary>
    private int BuildActiveShadowDepthList(in ShadowViewSetup view, int maxDepthShadows, Span<int> activeDepthShadows)
    {
        int activeCount = 0;
        for (int i = _shadowHandles.Head; i != Invalid; i = _shadowHandles.Next(i))
        {
            ref ClientShadow shadow = ref _shadows[i];

            // If this is not a flashlight which should use a shadow depth texture, skip!
            if ((shadow.Flags & ShadowFlags.UseDepthTexture) == 0)
                continue;

            // Bail if this flashlight doesn't want shadows
            if (!shadow.HasState || shadow.State.EnableShadows == 0)
                continue;

            ShadowMath.AABBFromProjectionMatrix(shadow.WorldToShadow, out Vector absMins, out Vector absMaxs);
            ShadowFrustum viewFrustum = ShadowMath.PerspectiveFrustum(view.Origin, view.Angles, view.ZNear, view.ZFar, view.Fov, view.AspectRatio);

            // If it's not in the view frustum, move on
            if (ShadowMath.CullBox(absMins, absMaxs, viewFrustum))
            {
                _engine.SetFlashlightDepthTexture(shadow.ShadowHandle, 0, 0);
                continue;
            }

            if (activeCount >= maxDepthShadows)
            {
                if (!_depthOverflowWarned)
                {
                    _console.Warning("Too many depth textures rendered in a single view!");
                    _depthOverflowWarned = true;
                }
                _engine.SetFlashlightDepthTexture(shadow.ShadowHandle, 0, 0);
                continue;
            }

            activeDepthShadows[activeCount++] = i;
        }
        return activeCount;
    }

    /// <summary><c>SetViewFlashlightState</c>: with version2, the view's flashlight (stock hands the engine the client handle).</summary>
    private void SetViewFlashlightState(int activeFlashlightCount, ReadOnlySpan<int> activeFlashlights)
    {
        if (_settings.FlashlightVersion2 == 0)
            return;
        _engine.SetFlashlightRenderState(activeFlashlightCount > 0 ? activeFlashlights[0] : Invalid);
    }

    /// <summary>
    /// <c>ComputeShadowDepthTextures( view )</c>: each active flashlight's depth
    /// view rendered into a free depth texture (warned about, ten times at
    /// most, when none is free), and the texture handed to the flashlight.
    /// </summary>
    public void ComputeShadowDepthTextures(in ShadowViewSetup viewSetup)
    {
        int[] activeDepthShadows = new int[MaxActiveDepthShadows];
        int activeCount = BuildActiveShadowDepthList(viewSetup, MaxActiveDepthShadows, activeDepthShadows);

        bool debugFrustum = _settings.FlashlightDrawFrustum;
        for (int j = 0; j < activeCount; ++j)
        {
            ref ClientShadow shadow = ref _shadows[activeDepthShadows[j]];

            if (!LockShadowDepthTexture(out int shadowDepthTexture))
            {
                // If we don't get one, that means we have too many this frame so bind no depth texture
                if (_shadowMapWarnings < 10)
                {
                    _console.Warning("Too many shadow maps this frame!");
                    _shadowMapWarnings++;
                }
                _engine.SetFlashlightDepthTexture(shadow.ShadowHandle, 0, 0);
                continue;
            }

            ref readonly FlashlightState state = ref shadow.State;
            var shadowView = new ShadowDepthView
            {
                AspectRatio = 1.0f,
                X = 0,
                Y = 0,
                Width = _materials.ActualWidth(shadowDepthTexture),
                Height = _materials.ActualHeight(shadowDepthTexture),
                Ortho = false,
                DoBloomAndToneMapping = false,
                Fov = state.HorizontalFovDegrees,
                FovViewmodel = state.HorizontalFovDegrees,
                Origin = new Vector(state.OriginX, state.OriginY, state.OriginZ),
                Angles = ShadowMath.QuaternionAngles(state.OrientationX, state.OrientationY, state.OrientationZ, state.OrientationW),
                ZNear = state.NearZ,
                ZNearViewmodel = state.NearZ,
                ZFar = state.FarZ,
                ZFarViewmodel = state.FarZ,
            };

            // Can turn on all light frustum overlays (the per-light flag is
            // not carried: FlashlightState has no DrawShadowFrustum).
            if (debugFrustum)
                DebugDrawFrustum(shadow.WorldToShadow);

            // Set depth bias factors specific to this flashlight
            _context.SetShadowDepthBiasFactors(state.ShadowSlopeScaleDepthBias, state.ShadowDepthBias);

            // Render to the shadow depth texture with appropriate view
            _context.UpdateShadowDepthTexture(_dummyColorTexture, shadowDepthTexture, shadowView);

            // Associate the shadow depth texture and stencil bit with the flashlight for use during scene rendering
            _engine.SetFlashlightDepthTexture(shadow.ShadowHandle, shadowDepthTexture, 0);
        }

        SetViewFlashlightState(activeCount, activeDepthShadows);
    }

    private bool LockShadowDepthTexture(out int texture)
    {
        for (int i = 0; i < _depthTextureCache.Count; i++)
        {
            if (!_depthTextureCacheLocks[i])
            {
                texture = _depthTextureCache[i];
                _depthTextureCacheLocks[i] = true;
                return true;
            }
        }
        texture = 0;
        return false;
    }

    /// <summary><c>UnlockAllShadowDepthTextures</c>: every depth texture free for the next view, and no view flashlight.</summary>
    public void UnlockAllShadowDepthTextures()
    {
        for (int i = 0; i < _depthTextureCache.Count; i++)
            _depthTextureCacheLocks[i] = false;
        SetViewFlashlightState(0, ReadOnlySpan<int>.Empty);
    }
}
