//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

// D4 and D5: how each shadow is projected (the blob and render-to-texture
// bases, the clip planes, the hierarchical bounds) and how a flashlight is
// (its matrices, light-world, models and version2).
public sealed partial class ShadowManager
{
    private void ClearExtraClipPlanes(int h) => _engine.ClearExtraClipPlanes(_shadows[h].ShadowHandle);

    private void AddExtraClipPlane(int h, in Vector normal, float dist) => _engine.AddExtraClipPlane(_shadows[h].ShadowHandle, normal, dist);

    private void UpdateStudioShadow(nint renderable, int handle)
    {
        if ((_shadows[handle].Flags & ShadowFlags.Flashlight) == 0)
        {
            ComputeHierarchicalBounds(renderable, out Vector mins, out Vector maxs);
            if (GetActualShadowCastType(handle) != ShadowTypes.RenderToTexture)
                BuildOrthoShadow(renderable, handle, mins, maxs);
            else
                BuildRenderToTextureShadow(renderable, handle, mins, maxs);
        }
        else
        {
            BuildFlashlight(handle);
        }
    }

    private void UpdateBrushShadow(nint renderable, int handle) => UpdateStudioShadow(renderable, handle);

    /// <summary><c>ComputeHierarchicalBounds( r, mins, maxs )</c>: the caster's box, grown by its shadow children unless it casts a blob.</summary>
    private void ComputeHierarchicalBounds(nint renderable, out Vector mins, out Vector maxs)
    {
        int shadowType = GetActualShadowCastType(renderable);
        _renderables.ShadowRenderBounds(renderable, shadowType, out mins, out maxs);

        nint child = _renderables.FirstShadowChild(renderable);
        if (child != 0 && shadowType != ShadowTypes.Simple)
        {
            _renderables.RenderableToWorldTransform(renderable, out ShadowMatrix3x4 toWorld);
            ShadowMatrix3x4 worldToBBox = ShadowMath.MatrixInvert(toWorld);
            AddChildBounds(worldToBBox, renderable, ref mins, ref maxs);
        }
    }

    private void AddChildBounds(in ShadowMatrix3x4 worldToBBox, nint parent, ref Vector mins, ref Vector maxs)
    {
        nint child = _renderables.FirstShadowChild(parent);
        while (child != 0)
        {
            if (GetActualShadowCastType(child) != ShadowTypes.None)
            {
                _renderables.ShadowRenderBounds(child, ShadowTypes.RenderToTexture, out Vector childMins, out Vector childMaxs);
                _renderables.RenderableToWorldTransform(child, out ShadowMatrix3x4 childToWorld);
                ShadowMatrix3x4 childToBBox = ShadowMath.ConcatTransforms(worldToBBox, childToWorld);
                ShadowMath.TransformAABB(childToBBox, childMins, childMaxs, out Vector newMins, out Vector newMaxs);
                mins = new Vector(Min(mins.X, newMins.X), Min(mins.Y, newMins.Y), Min(mins.Z, newMins.Z));
                maxs = new Vector(Max(maxs.X, newMaxs.X), Max(maxs.Y, newMaxs.Y), Max(maxs.Z, newMaxs.Z));
            }
            AddChildBounds(worldToBBox, child, ref mins, ref maxs);
            child = _renderables.NextShadowPeer(child);
        }
    }

    private static float Min(float a, float b) => a < b ? a : b;

    private static float Max(float a, float b) => a > b ? a : b;

    /// <summary>The object's basis: AngleVectors, with the right vector turned into a left one.</summary>
    private void Basis(nint renderable, Span<Vector> vec)
    {
        ShadowMath.AngleVectors(_renderables.RenderAngles(renderable), out vec[0], out vec[1], out vec[2]);
        vec[1] = new Vector(vec[1].X * -1.0f, vec[1].Y * -1.0f, vec[1].Z * -1.0f);
    }

    private static Vector Project(in Vector shadowDir, ReadOnlySpan<Vector> vec) =>
        new(ShadowMath.Dot(vec[0], shadowDir), ShadowMath.Dot(vec[1], shadowDir), ShadowMath.Dot(vec[2], shadowDir));

    /// <summary><c>ComputeLocalShadowOrigin</c>: the back of the box along the shadow, and the falloff's start.</summary>
    private static float ComputeLocalShadowOrigin(in Vector mins, in Vector maxs, in Vector localShadowDir, float backupFactor, out Vector origin)
    {
        Vector centroid = new(mins.X + maxs.X, mins.Y + maxs.Y, mins.Z + maxs.Z);
        centroid = new Vector(centroid.X * 0.5f, centroid.Y * 0.5f, centroid.Z * 0.5f);
        Vector size = new(maxs.X - mins.X, maxs.Y - mins.Y, maxs.Z - mins.Z);
        float radius = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z) * 0.5f;

        float centroidProjection = ShadowMath.Dot(centroid, localShadowDir);
        float minDist = -centroidProjection;
        for (int i = 0; i < 3; ++i)
        {
            float d = ShadowMath.At(localShadowDir, i);
            if (d > 0.0f)
                minDist += d * ShadowMath.At(mins, i);
            else
                minDist += d * ShadowMath.At(maxs, i);
        }
        minDist *= backupFactor;
        origin = ShadowMath.MA(centroid, minDist, localShadowDir);
        return radius - minDist;
    }

    private static void SortAbsVectorComponents(in Vector src, Span<int> vecIdx)
    {
        float a0 = MathF.Abs(src.X), a1 = MathF.Abs(src.Y), a2 = MathF.Abs(src.Z);
        int maxIdx = a0 > a1 ? 0 : 1;
        if (a2 > (maxIdx == 0 ? a0 : a1))
            maxIdx = 2;
        switch (maxIdx)
        {
            case 0:
                vecIdx[0] = 1; vecIdx[1] = 2; vecIdx[2] = 0;
                break;
            case 1:
                vecIdx[0] = 2; vecIdx[1] = 0; vecIdx[2] = 1;
                break;
            default:
                vecIdx[0] = 0; vecIdx[1] = 1; vecIdx[2] = 2;
                break;
        }
    }

    private static ShadowMatrix BuildWorldToTextureMatrix(in ShadowMatrix worldToShadow, float sizeX, float sizeY)
    {
        ShadowMatrix shadowToUnit = ShadowMath.Scale(1.0f / sizeX, 1.0f / sizeY, 1.0f);
        shadowToUnit[3] = 0.5f;
        shadowToUnit[7] = 0.5f;
        return ShadowMath.Multiply(shadowToUnit, worldToShadow);
    }

    /// <summary><c>BuildGeneralWorldToShadowMatrix</c>: the basis and origin, inverted in general (xvec and yvec need not be perpendicular).</summary>
    private static void BuildGeneralWorldToShadowMatrix(ref ShadowMatrix m, in Vector origin, in Vector dir, in Vector xvec, in Vector yvec)
    {
        ShadowMath.SetBasisVectors(ref m, xvec, yvec, dir);
        ShadowMath.SetTranslation(ref m, origin);
        m[12] = m[13] = m[14] = 0.0f;
        m[15] = 1.0f;
        ShadowMatrix copy = m;
        ShadowMath.InverseGeneral(copy, ref m);
    }

    /// <summary><c>BuildOrthoWorldToShadowMatrix</c>: the basis transposed, the origin rotated and negated.</summary>
    private static void BuildOrthoWorldToShadowMatrix(ref ShadowMatrix m, in Vector origin, in Vector dir, in Vector xvec, in Vector yvec)
    {
        ShadowMath.SetBasisVectors(ref m, xvec, yvec, dir);
        m = ShadowMath.Transpose(m);
        Vector translation = ShadowMath.Multiply3x3(m, origin);
        translation = new Vector(translation.X * -1.0f, translation.Y * -1.0f, translation.Z * -1.0f);
        ShadowMath.SetTranslation(ref m, translation);
        m[12] = m[13] = m[14] = 0.0f;
        m[15] = 1.0f;
    }

    /// <summary><c>BuildWorldToShadowMatrix( origin, quat )</c>: the flashlight's view, with stock's forward/left/up flip.</summary>
    private static ShadowMatrix BuildWorldToShadowMatrix(in Vector origin, in FlashlightState state)
    {
        ShadowMatrix3x4 orientation = ShadowMath.QuaternionMatrix(state.OrientationX, state.OrientationY, state.OrientationZ, state.OrientationW);
        orientation[3] = 0f;
        orientation[7] = 0f;
        orientation[11] = 0f;
        ShadowMatrix basis = ShadowMath.FromMatrix3x4(orientation);
        Vector forward = new(basis[0], basis[4], basis[8]);
        Vector left = new(basis[1], basis[5], basis[9]);
        Vector up = new(basis[2], basis[6], basis[10]);
        // Bizarre vector flip inherited from earlier code
        basis[0] = left.X; basis[4] = left.Y; basis[8] = left.Z;
        basis[1] = up.X; basis[5] = up.Y; basis[9] = up.Z;
        basis[2] = forward.X; basis[6] = forward.Y; basis[10] = forward.Z;
        ShadowMatrix worldToShadow = ShadowMath.Transpose(basis);

        Vector translation = ShadowMath.Multiply3x3(worldToShadow, origin);
        translation = new Vector(translation.X * -1.0f, translation.Y * -1.0f, translation.Z * -1.0f);
        ShadowMath.SetTranslation(ref worldToShadow, translation);
        worldToShadow[12] = worldToShadow[13] = worldToShadow[14] = 0.0f;
        worldToShadow[15] = 1.0f;
        return worldToShadow;
    }

    private static ShadowMatrix BuildPerspectiveWorldToFlashlightMatrix(in FlashlightState state)
    {
        ShadowMatrix view = BuildWorldToShadowMatrix(new Vector(state.OriginX, state.OriginY, state.OriginZ), state);
        ShadowMatrix perspective = ShadowMath.BuildPerspective(state.HorizontalFovDegrees, state.VerticalFovDegrees, state.NearZ, state.FarZ);
        return ShadowMath.Multiply(perspective, view);
    }

    /// <summary><c>BuildShadowLeafList</c>: the leaves the shadow's swept box touches.</summary>
    private void BuildShadowLeafList(in Vector origin, in Vector dir, float sizeX, float sizeY, float maxDist)
    {
        Vector delta = new(dir.X * maxDist, dir.Y * maxDist, dir.Z * maxDist);
        float radius = MathF.Sqrt(sizeX * sizeX + sizeY * sizeY) * 0.5f;
        _leafScratch.Clear();
        _world.EnumerateLeavesAlongRay(origin, delta, new Vector(radius, radius, radius), _leafScratch);
    }

    /// <summary><c>BuildOrthoShadow</c>: the blob, on the two object axes most across the shadow, bloated by 10.</summary>
    private void BuildOrthoShadow(nint renderable, int handle, in Vector mins, in Vector maxs)
    {
        Span<Vector> vec = stackalloc Vector[3];
        Basis(renderable, vec);
        Vector shadowDir = GetShadowDirection(renderable);
        Vector localShadowDir = Project(shadowDir, vec);

        Span<int> vecIdx = stackalloc int[3];
        SortAbsVectorComponents(localShadowDir, vecIdx);

        Vector xvec = vec[vecIdx[0]];
        Vector yvec = vec[vecIdx[1]];
        float dx0 = ShadowMath.Dot(shadowDir, xvec);
        xvec = new Vector(xvec.X - shadowDir.X * dx0, xvec.Y - shadowDir.Y * dx0, xvec.Z - shadowDir.Z * dx0);
        float dy0 = ShadowMath.Dot(shadowDir, yvec);
        yvec = new Vector(yvec.X - shadowDir.X * dy0, yvec.Y - shadowDir.Y * dy0, yvec.Z - shadowDir.Z * dy0);
        xvec = ShadowMath.Normalize(xvec);
        yvec = ShadowMath.Normalize(yvec);

        Vector boxSize = new(maxs.X - mins.X, maxs.Y - mins.Y, maxs.Z - mins.Z);
        float sizeX = ShadowMath.At(boxSize, vecIdx[0]);
        float sizeY = ShadowMath.At(boxSize, vecIdx[1]);
        sizeX *= MathF.Abs(ShadowMath.Dot(vec[vecIdx[0]], xvec));
        sizeY *= MathF.Abs(ShadowMath.Dot(vec[vecIdx[1]], yvec));
        sizeX += ShadowMath.At(boxSize, vecIdx[2]) * MathF.Abs(ShadowMath.Dot(vec[vecIdx[2]], xvec));
        sizeY += ShadowMath.At(boxSize, vecIdx[2]) * MathF.Abs(ShadowMath.Dot(vec[vecIdx[2]], yvec));
        sizeX += 10.0f;
        sizeY += 10.0f;
        sizeX = ShadowMath.Max(sizeX, 10.0f);
        sizeY = ShadowMath.Max(sizeY, 10.0f);

        float falloffStart = ComputeLocalShadowOrigin(mins, maxs, localShadowDir, 2.0f, out Vector org);

        Vector worldOrigin = _renderables.RenderOrigin(renderable);
        worldOrigin = ShadowMath.MA(worldOrigin, org.X, vec[0]);
        worldOrigin = ShadowMath.MA(worldOrigin, org.Y, vec[1]);
        worldOrigin = ShadowMath.MA(worldOrigin, org.Z, vec[2]);

        // FUNKY: A trick to reduce annoying texelization artifacts!?
        float dx = 1.0f / TexelSizePerCasterSize;
        worldOrigin = new Vector((int)(worldOrigin.X / dx) * dx, (int)(worldOrigin.Y / dx) * dx, (int)(worldOrigin.Z / dx) * dx);

        ref ClientShadow shadow = ref _shadows[handle];
        BuildGeneralWorldToShadowMatrix(ref shadow.WorldToShadow, worldOrigin, shadowDir, xvec, yvec);
        ShadowMatrix worldToTexture = BuildWorldToTextureMatrix(shadow.WorldToShadow, sizeX, sizeY);
        shadow.WorldSizeX = sizeX;
        shadow.WorldSizeY = sizeY;

        float shadowCastDistance = GetShadowDistance(renderable);
        float maxHeight = shadowCastDistance + falloffStart;

        BuildShadowLeafList(worldOrigin, shadowDir, sizeX, sizeY, maxHeight);
        int[] leaves = _leafScratch.ToArray();

        _engine.ProjectShadow(_shadows[handle].ShadowHandle, worldOrigin, shadowDir, worldToTexture, sizeX, sizeY, leaves, maxHeight,
                              falloffStart, MaxFalloffAmount, _renderables.RenderOrigin(renderable));

        // FIXME!!!!!!!!!!!!!!  Removing this for now since it seems to mess up the blobby shadows.
        // ComputeExtraClipPlanes( pEnt, handle, vec, mins, maxs, localShadowDir );

        _leafSystem.ProjectShadow(_shadows[handle].LeafShadow, leaves);
    }

    /// <summary><c>BuildRenderToTextureShadow</c>: the render-to-texture basis, its clip planes, and the debug boxes under cl_drawshadowtexture.</summary>
    private void BuildRenderToTextureShadow(nint renderable, int handle, in Vector mins, in Vector maxs)
    {
        if (_settings.DrawShadowTexture != 0)
            DrawRenderToTextureDebugInfo(renderable, mins, maxs);

        Span<Vector> vec = stackalloc Vector[3];
        Basis(renderable, vec);
        Vector shadowDir = GetShadowDirection(renderable);
        Vector localShadowDir = Project(shadowDir, vec);

        Vector boxSize = new(maxs.X - mins.X, maxs.Y - mins.Y, maxs.Z - mins.Z);

        Vector yvec = Vector.Zero;
        float projMax = 0.0f;
        for (int i = 0; i != 3; ++i)
        {
            float d = ShadowMath.Dot(shadowDir, vec[i]);
            Vector test = new(vec[i].X - shadowDir.X * d, vec[i].Y - shadowDir.Y * d, vec[i].Z - shadowDir.Z * d);
            float s = ShadowMath.At(boxSize, i);
            test = new Vector(test.X * s, test.Y * s, test.Z * s);
            float lengthSqr = test.X * test.X + test.Y * test.Y + test.Z * test.Z;
            if (lengthSqr > projMax)
            {
                projMax = lengthSqr;
                yvec = test;
            }
        }
        yvec = ShadowMath.Normalize(yvec);
        Vector xvec = ShadowMath.Cross(yvec, shadowDir);

        float sizeX = boxSize.X * MathF.Abs(ShadowMath.Dot(vec[0], xvec)) + boxSize.Y * MathF.Abs(ShadowMath.Dot(vec[1], xvec)) +
                      boxSize.Z * MathF.Abs(ShadowMath.Dot(vec[2], xvec));
        float sizeY = boxSize.X * MathF.Abs(ShadowMath.Dot(vec[0], yvec)) + boxSize.Y * MathF.Abs(ShadowMath.Dot(vec[1], yvec)) +
                      boxSize.Z * MathF.Abs(ShadowMath.Dot(vec[2], yvec));
        sizeX += 2.0f * TexelSizePerCasterSize;
        sizeY += 2.0f * TexelSizePerCasterSize;

        float falloffStart = ComputeLocalShadowOrigin(mins, maxs, localShadowDir, 1.0f, out Vector org);

        Vector worldOrigin = _renderables.RenderOrigin(renderable);
        worldOrigin = ShadowMath.MA(worldOrigin, org.X, vec[0]);
        worldOrigin = ShadowMath.MA(worldOrigin, org.Y, vec[1]);
        worldOrigin = ShadowMath.MA(worldOrigin, org.Z, vec[2]);

        ref ClientShadow shadow = ref _shadows[handle];
        BuildOrthoWorldToShadowMatrix(ref shadow.WorldToShadow, worldOrigin, shadowDir, xvec, yvec);
        ShadowMatrix worldToTexture = BuildWorldToTextureMatrix(shadow.WorldToShadow, sizeX, sizeY);
        shadow.WorldSizeX = sizeX;
        shadow.WorldSizeY = sizeY;

        float shadowCastDistance = GetShadowDistance(renderable);
        float maxHeight = shadowCastDistance + falloffStart;

        BuildShadowLeafList(worldOrigin, shadowDir, sizeX, sizeY, maxHeight);
        int[] leaves = _leafScratch.ToArray();

        _engine.ProjectShadow(_shadows[handle].ShadowHandle, worldOrigin, shadowDir, worldToTexture, sizeX, sizeY, leaves, maxHeight,
                              falloffStart, MaxFalloffAmount, _renderables.RenderOrigin(renderable));

        ComputeExtraClipPlanes(renderable, handle, vec, mins, maxs, localShadowDir);

        _leafSystem.ProjectShadow(_shadows[handle].LeafShadow, leaves);
    }

    /// <summary>
    /// <c>ComputeExtraClipPlanes</c>: three planes through the box's corner
    /// furthest along the shadow. The fork's build has no fourth (the
    /// renderable's own clip plane is a C_BaseEntity field none of its
    /// renderables has).
    /// </summary>
    private void ComputeExtraClipPlanes(nint renderable, int handle, ReadOnlySpan<Vector> vec, in Vector mins, in Vector maxs, in Vector localShadowDir)
    {
        Vector origin = _renderables.RenderOrigin(renderable);
        Span<float> dir = stackalloc float[3];
        for (int i = 0; i < 3; ++i)
        {
            if (ShadowMath.At(localShadowDir, i) < 0.0f)
            {
                origin = ShadowMath.MA(origin, ShadowMath.At(maxs, i), vec[i]);
                dir[i] = 1;
            }
            else
            {
                origin = ShadowMath.MA(origin, ShadowMath.At(mins, i), vec[i]);
                dir[i] = -1;
            }
        }

        ClearExtraClipPlanes(handle);
        for (int i = 0; i < 3; ++i)
        {
            Vector normal = new(vec[i].X * dir[i], vec[i].Y * dir[i], vec[i].Z * dir[i]);
            float dist = ShadowMath.Dot(normal, origin);
            AddExtraClipPlane(handle, normal, dist);
        }
    }

    private void BuildFlashlightLeafList(in ShadowMatrix worldToShadow)
    {
        ShadowMath.AABBFromProjectionMatrix(worldToShadow, out Vector mins, out Vector maxs);
        _leafScratch.Clear();
        _world.EnumerateLeavesInBox(mins, maxs, _leafScratch);
    }

    /// <summary>
    /// <c>BuildFlashlight( h )</c>: version2 hands the engine only the matrix;
    /// else the leaves of the frustum's box, projected onto the world when it
    /// lights the world (or the engine's surfaces cleared when not), and onto
    /// the leaf system's renderables when it lights models.
    /// </summary>
    private void BuildFlashlight(int handle)
    {
        ref ClientShadow shadow = ref _shadows[handle];
        if (_settings.FlashlightVersion2 != 0)
        {
            _engine.ProjectFlashlight(shadow.ShadowHandle, shadow.WorldToShadow, ReadOnlySpan<int>.Empty);
            return;
        }

        bool lightModels = _settings.FlashlightModels;
        nint target = shadow.TargetEntity == InvalidEntity ? 0 : _renderables.RenderableOf(shadow.TargetEntity);
        bool lightSpecificEntity = target != 0;
        bool lightWorld = (shadow.Flags & ShadowFlags.LightWorld) != 0;
        int[] leaves = [];

        if (lightWorld || (lightModels && !lightSpecificEntity))
        {
            BuildFlashlightLeafList(shadow.WorldToShadow);
            leaves = _leafScratch.ToArray();
        }

        if (lightWorld)
        {
            _engine.ProjectFlashlight(shadow.ShadowHandle, shadow.WorldToShadow, leaves);
        }
        else
        {
            // This should clear all models and surfaces from this shadow
            _engine.EnableShadow(shadow.ShadowHandle, false);
            _engine.EnableShadow(shadow.ShadowHandle, true);
        }

        if (!lightModels)
            return;

        if (!lightSpecificEntity)
        {
            _leafSystem.ProjectFlashlight(shadow.LeafShadow, leaves);
            return;
        }

        // We know what we are focused on, so just add the shadow directly to
        // that receiver: its move children first, then itself (the pristine
        // tail, which the fork deleted because nothing can set a target).
        for (nint child = _renderables.FirstMoveChild(target); child != 0; child = _renderables.NextMovePeer(child))
            AddShadowToTargetReceiver(handle, child);
        AddShadowToTargetReceiver(handle, target);
    }

    private void AddShadowToTargetReceiver(int handle, nint renderable)
    {
        int modelType = _renderables.ModelType(_renderables.Model(renderable));
        if (modelType == ModBrush)
            AddShadowToReceiver(handle, renderable, ShadowReceivers.BrushModel);
        else if (modelType == ModStudio)
            AddShadowToReceiver(handle, renderable, ShadowReceivers.StudioModel);
    }

    /// <summary><c>SetFlashlightTarget( h, entity )</c>: flashlights only.</summary>
    public void SetFlashlightTarget(int shadowHandle, uint targetEntity)
    {
        ref ClientShadow shadow = ref _shadows[shadowHandle];
        if ((shadow.Flags & ShadowFlags.Flashlight) == 0)
            return;
        shadow.TargetEntity = targetEntity;
    }

    /// <summary><c>SetFlashlightLightWorld( h, on )</c>: flashlights only.</summary>
    public void SetFlashlightLightWorld(int shadowHandle, bool lightWorld)
    {
        ref ClientShadow shadow = ref _shadows[shadowHandle];
        if ((shadow.Flags & ShadowFlags.Flashlight) == 0)
            return;
        if (lightWorld)
            shadow.Flags |= ShadowFlags.LightWorld;
        else
            shadow.Flags &= ~ShadowFlags.LightWorld;
    }

    /// <summary><c>IsFlashlightTarget( h, r )</c>: the target, or one of its move children.</summary>
    private bool IsFlashlightTarget(int shadowHandle, nint renderable)
    {
        nint target = _renderables.RenderableOf(_shadows[shadowHandle].TargetEntity);
        if (target == renderable)
            return true;
        for (nint child = _renderables.FirstMoveChild(target); child != 0; child = _renderables.NextMovePeer(child))
        {
            if (child == renderable)
                return true;
        }
        return false;
    }

    /// <summary><c>ComputeBoundingSphere( r, origin, radius )</c>: the shadow box's sphere, its centre in the world.</summary>
    internal void ComputeBoundingSphere(nint renderable, out Vector origin, out float radius)
    {
        _renderables.ShadowRenderBounds(renderable, GetActualShadowCastType(renderable), out Vector mins, out Vector maxs);
        Vector size = new(maxs.X - mins.X, maxs.Y - mins.Y, maxs.Z - mins.Z);
        radius = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z) * 0.5f;

        Vector centroid = new(mins.X + maxs.X, mins.Y + maxs.Y, mins.Z + maxs.Z);
        centroid = new Vector(centroid.X * 0.5f, centroid.Y * 0.5f, centroid.Z * 0.5f);

        Span<Vector> vec = stackalloc Vector[3];
        Basis(renderable, vec);
        origin = _renderables.RenderOrigin(renderable);
        origin = ShadowMath.MA(origin, centroid.X, vec[0]);
        origin = ShadowMath.MA(origin, centroid.Y, vec[1]);
        origin = ShadowMath.MA(origin, centroid.Z, vec[2]);
    }

    /// <summary><c>ComputeShadowBBox</c>: the sphere's box, extruded along the shadow by its cast distance.</summary>
    internal void ComputeShadowBBox(nint renderable, in Vector absCenter, float radius, out Vector absMins, out Vector absMaxs)
    {
        Vector shadowDir = GetShadowDirection(renderable);
        absMins = default;
        absMaxs = default;
        for (int i = 0; i < 3; ++i)
        {
            float shadowCastDistance = GetShadowDistance(renderable);
            float dist = shadowCastDistance * ShadowMath.At(shadowDir, i);
            float c = ShadowMath.At(absCenter, i);
            if (ShadowMath.At(shadowDir, i) < 0)
            {
                ShadowMath.Set(ref absMins, i, c - radius + dist);
                ShadowMath.Set(ref absMaxs, i, c + radius);
            }
            else
            {
                ShadowMath.Set(ref absMins, i, c - radius);
                ShadowMath.Set(ref absMaxs, i, c + radius + dist);
            }
        }
    }
}
