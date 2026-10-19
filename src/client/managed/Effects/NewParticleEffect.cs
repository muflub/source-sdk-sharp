//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>CNewParticleEffect's own flags (particles_new.h's bitfield), as the manager decides them.</summary>
[Flags]
public enum NewParticleFlags
{
    None = 0,
    /// <summary>m_bIsFirstFrame: the first step simulates zero seconds.</summary>
    FirstFrame = 1 << 0,
    /// <summary>m_bNeedsBBoxUpdate: the step starts with a first box.</summary>
    NeedsBBoxUpdate = 1 << 1,
    /// <summary>m_bAutoUpdateBBox: the first box is recalculated, not taken as set.</summary>
    AutoUpdateBBox = 1 << 2,
    /// <summary>m_bRemove: the manager removes the effect at the end of its update.</summary>
    Remove = 1 << 3,
    /// <summary>m_bSimulate: steps after the first simulate.</summary>
    Simulate = 1 << 4,
    /// <summary>m_bDrawn: cleared by each step.</summary>
    Drawn = 1 << 5,
    /// <summary>m_bShouldPerformCullCheck: set only while a retirement pass runs.</summary>
    CullCheck = 1 << 6,
}

/// <summary>How a new effect's DrawModel ended (the return it took).</summary>
public enum NewParticleDrawReason
{
    /// <summary>r_drawparticles 0.</summary>
    DrawParticles,
    /// <summary>The client mode or the manager draws no particles.</summary>
    ClientMode,
    /// <summary>A shadow or SSAO depth pass.</summary>
    ShadowDepth,
    /// <summary>Past the definition's max draw distance (the sleep time is pushed out).</summary>
    Distance,
    /// <summary>The skip-render control point's entity is the camera.</summary>
    SkipSelf,
    /// <summary>Its root move parent is the camera.</summary>
    SkipRoot,
    /// <summary>The camera spectates it in eye.</summary>
    SkipInEye,
    /// <summary>Drawn: rendered now, or added to the render cache.</summary>
    Drawn,
}

/// <summary>What a drawn effect asks native to do.</summary>
public enum NewParticleDrawAction
{
    /// <summary>Nothing (DrawModel returns 0).</summary>
    None = 0,
    /// <summary>A translucent pass: render now, two-pass when the system is.</summary>
    Render = 1,
    /// <summary>An opaque pass: hand the collection to the library's render cache.</summary>
    RenderCache = 2,
}

/// <summary>DrawModel's inputs: what native read for one draw.</summary>
public struct NewParticleDrawIn
{
    public int Flags;
    public bool DrawParticles;          // r_drawparticles
    public bool ClientModeDraws;        // g_pClientMode->ShouldDrawParticles()
    public bool RenderSystems;          // CParticleMgr::ShouldRenderParticleSystems()
    public Vector Camera;               // the render context's world-space camera
    public Vector Min, Max;             // the collection's bounds
    public float MaxDrawDistance;       // the definition's m_flMaxDrawDistance
    public float NoDrawTimeToGoToSleep; // the definition's m_flNoDrawTimeToGoToSleep
    public float LastSimulationTime;    // the library manager's
    public float NextSleepTime;         // the collection's m_flNextSleepTime
    public int SkipRenderControlPoint;  // the definition's m_nSkipRenderControlPoint (-1: none)
    public int HighestControlPoint;     // the collection's m_nHighestCP
    // Entities are identity tokens (native's C_StandaloneEntity pointers), 0 for none:
    // the skip-render tests compare them and nothing else.
    public ulong CameraObject;          // the entity at the view entity's index
    public ulong SkipObject;            // the skip-render control point's entity
    public bool ThirdPerson;            // input->CAM_IsThirdPerson()
    public ulong LocalPlayer;
    public int ObserverMode;            // the local player's
    public ulong ObserverTarget;        // the local player's observer target
}

/// <summary>DrawModel's answer.</summary>
public readonly record struct NewParticleDraw(NewParticleDrawAction Action, NewParticleDrawReason Reason, float NextSleepTime);

/// <summary>One retirement candidate: a listed effect, as ComputeParticleDefScreenArea reads it.</summary>
public struct NewParticleRetireIn
{
    public ulong Definition;      // which definition (its identity)
    public float CullRadius;      // m_flCullRadius
    public float CullFillCost;    // m_flCullFillCost
    public Vector CullPoint;      // GetControlPointAtCurrentTime( the definition's cull control point )
    public bool HasReplacement;   // the definition names a cull replacement
    public bool FirstFrame;       // the effect's first-frame flag
}

/// <summary>The player view a retirement pass measures against (view->GetPlayerViewSetup and its matrices).</summary>
public struct NewParticleRetireView
{
    public int X, Y, Width, Height;
    public float Fov;
    public Vector Origin;
    /// <summary>render->GetMatricesForView's world-to-screen VMatrix, row-major.</summary>
    public Matrix4x4Raw WorldToScreen;
}

/// <summary>
/// CNewParticleEffect's decisions (particles_new.cpp) and the new-effect
/// half of CParticleMgr's (particlemgr.cpp: BuildParticleSimList,
/// ProcessPSystem, EarlyRetireParticleSystems), held to stock's recording
/// (NewParticleEffectOracleTests). The particle library simulates, bounds and
/// renders; what it answers is an input here.
/// </summary>
public sealed class NewParticleEffect
{
    /// <summary>OBS_MODE_IN_EYE (shareddefs.h).</summary>
    public const int ObserverInEye = 4;

    /// <summary>model_types.h's STUDIO_TRANSPARENCY.</summary>
    public const int StudioTransparency = unchecked((int)0x80000000);

    /// <summary>STUDIO_SHADOWDEPTHTEXTURE | STUDIO_SSAODEPTHTEXTURE.</summary>
    public const int DepthPassFlags = 0x40000000 | 0x08000000;

    /// <summary>Construct()'s flags: the first frame and simulating.</summary>
    public NewParticleFlags Flags = NewParticleFlags.FirstFrame | NewParticleFlags.Simulate;

    /// <summary>m_LastMin / m_LastMax: Construct() sets the box inside out, so the first change always reports.</summary>
    public Vector LastMin = new(1.0e6f, 1.0e6f, 1.0e6f);

    public Vector LastMax = new(-1.0e6f, -1.0e6f, -1.0e6f);

    public bool Has(NewParticleFlags f) => (Flags & f) != 0;

    public void Set(NewParticleFlags f, bool on) => Flags = on ? Flags | f : Flags & ~f;

    /// <summary>
    /// Release, when the reference count reaches zero: an allocated effect
    /// that has already finished is flagged for removal; one with particles
    /// left is removed by its step when it finishes.
    /// </summary>
    public void Release(int refCountAfter, bool allocated, bool finished)
    {
        if (refCountAfter == 0 && allocated && finished)
            Set(NewParticleFlags.Remove, true);
    }

    /// <summary>
    /// BuildParticleSimList's test for one effect: an effect past its sleep
    /// time with particles, or flagged for removal, is skipped unless it moved;
    /// a moved one is listed for its bounds only.
    /// </summary>
    public (bool Listed, bool BoundsOnly) SimList(float now, float nextSleepTime, int activeParticles, bool hasMoved)
    {
        bool skip = now >= nextSleepTime && activeParticles > 0;
        if (Has(NewParticleFlags.Remove))
            skip = true;
        if (!skip)
            return (true, false);
        return hasMoved ? (true, true) : (false, false);
    }

    /// <summary>
    /// ProcessPSystem's first box: true when the step must recalculate the
    /// box first (it needs one and updates it itself).
    /// </summary>
    public bool FirstBoxRecalculates => Has(NewParticleFlags.NeedsBBoxUpdate) && Has(NewParticleFlags.AutoUpdateBBox);

    /// <summary>
    /// ProcessPSystem's first box, after the recalculation if there was one:
    /// the effect stops needing a box when it does not update its own or the
    /// recalculation found one.
    /// </summary>
    public void FirstBox(bool recalculated)
    {
        if (Has(NewParticleFlags.NeedsBBoxUpdate) && (!Has(NewParticleFlags.AutoUpdateBBox) || recalculated))
            Set(NewParticleFlags.NeedsBBoxUpdate, false);
    }

    /// <summary>
    /// RecalculateBoundingBox after the library bloated the bounds by the
    /// control points: a box still inside out collapses to the sort origin and
    /// answers false.
    /// </summary>
    public static bool RecalculateBoundingBox(ref Vector min, ref Vector max, Vector sortOrigin)
    {
        if (max.X < min.X)
        {
            min = max = sortOrigin;
            return false;
        }
        return true;
    }

    /// <summary>
    /// ProcessPSystem's step: the drawn flag cleared, then the seconds to
    /// simulate (zero on the first frame, which it clears; the frame's step
    /// when simulating; -1 for none).
    /// </summary>
    public float Step(float dt)
    {
        Set(NewParticleFlags.Drawn, false);
        if (Has(NewParticleFlags.FirstFrame))
        {
            Set(NewParticleFlags.FirstFrame, false);
            return 0.0f;
        }
        return Has(NewParticleFlags.Simulate) ? dt : -1.0f;
    }

    /// <summary>ProcessPSystem's end: a finished collection is flagged for removal.</summary>
    public void Finished(bool finished)
    {
        if (finished)
            Set(NewParticleFlags.Remove, true);
    }

    /// <summary>
    /// DetectChanges: true when the box left the last one by more than the
    /// buffer (cl_particleeffect_aabb_buffer), which is when stock tells the
    /// leaf system; the last box is then the box padded by the buffer. An
    /// effect with no render handle reports nothing.
    /// </summary>
    public bool DetectChanges(Vector min, Vector max, float buffer, bool hasRenderHandle)
    {
        if (!hasRenderHandle)
            return false;
        float extra = buffer * 1.3f;
        if (min.X < LastMin.X ||
            min.Y < LastMin.Y ||
            min.Z < LastMin.Z ||
            min.X > (LastMin.X + extra) ||
            min.Y > (LastMin.Y + extra) ||
            min.Z > (LastMin.Z + extra) ||
            max.X > LastMax.X ||
            max.Y > LastMax.Y ||
            max.Z > LastMax.Z ||
            max.X < (LastMax.X - extra) ||
            max.Y < (LastMax.Y - extra) ||
            max.Z < (LastMax.Z - extra))
        {
            LastMin = new Vector(min.X - buffer, min.Y - buffer, min.Z - buffer);
            LastMax = new Vector(max.X + buffer, max.Y + buffer, max.Z + buffer);
            return true;
        }
        return false;
    }

    /// <summary>mathlib's CalcSqrDistanceToAABB.</summary>
    public static float SqrDistanceToBox(Vector mins, Vector maxs, Vector point)
    {
        float delta;
        float distSqr = 0.0f;
        if (point.X < mins.X)
        {
            delta = mins.X - point.X;
            distSqr += delta * delta;
        }
        else if (point.X > maxs.X)
        {
            delta = point.X - maxs.X;
            distSqr += delta * delta;
        }
        if (point.Y < mins.Y)
        {
            delta = mins.Y - point.Y;
            distSqr += delta * delta;
        }
        else if (point.Y > maxs.Y)
        {
            delta = point.Y - maxs.Y;
            distSqr += delta * delta;
        }
        if (point.Z < mins.Z)
        {
            delta = mins.Z - point.Z;
            distSqr += delta * delta;
        }
        else if (point.Z > maxs.Z)
        {
            delta = point.Z - maxs.Z;
            distSqr += delta * delta;
        }
        return distSqr;
    }

    /// <summary>
    /// DrawModel's decisions, in stock's order: the switches, the depth
    /// passes, the distance cull (which keeps the system awake for its no-draw
    /// time so it still follows its points), then, in a translucent pass, the
    /// skip-render control point's tests against the camera's entity. A drawn
    /// translucent pass renders now; an opaque one goes to the render cache.
    /// </summary>
    public static NewParticleDraw Draw(in NewParticleDrawIn d)
    {
        if (!d.DrawParticles)
            return new(NewParticleDrawAction.None, NewParticleDrawReason.DrawParticles, d.NextSleepTime);
        if (!d.ClientModeDraws || !d.RenderSystems)
            return new(NewParticleDrawAction.None, NewParticleDrawReason.ClientMode, d.NextSleepTime);
        if ((d.Flags & DepthPassFlags) != 0)
            return new(NewParticleDrawAction.None, NewParticleDrawReason.ShadowDepth, d.NextSleepTime);

        if (SqrDistanceToBox(d.Min, d.Max, d.Camera) > d.MaxDrawDistance * d.MaxDrawDistance)
        {
            float sleep = MathF.Max(d.NextSleepTime, d.LastSimulationTime + d.NoDrawTimeToGoToSleep);
            return new(NewParticleDrawAction.None, NewParticleDrawReason.Distance, sleep);
        }

        if ((d.Flags & StudioTransparency) == 0)
            return new(NewParticleDrawAction.RenderCache, NewParticleDrawReason.Drawn, d.NextSleepTime);

        if (d.CameraObject != 0 && d.SkipRenderControlPoint != -1 && d.SkipRenderControlPoint <= d.HighestControlPoint
            && d.SkipObject != 0 && !d.ThirdPerson)
        {
            if (d.SkipObject == d.CameraObject)
                return new(NewParticleDrawAction.None, NewParticleDrawReason.SkipSelf, d.NextSleepTime);
            // An entity is its own root move parent on this hierarchy
            // (particles_new.cpp's ARMED note): the root test is the same test.
            ulong root = d.SkipObject;
            if (root == d.CameraObject)
                return new(NewParticleDrawAction.None, NewParticleDrawReason.SkipRoot, d.NextSleepTime);
            if (d.LocalPlayer != 0 && d.LocalPlayer == d.CameraObject && d.ObserverMode == ObserverInEye
                && (d.ObserverTarget == d.SkipObject || root == d.ObserverTarget))
                return new(NewParticleDrawAction.None, NewParticleDrawReason.SkipInEye, d.NextSleepTime);
        }
        return new(NewParticleDrawAction.Render, NewParticleDrawReason.Drawn, d.NextSleepTime);
    }

    /// <summary>
    /// DebugDrawBbox's colour: grey when culled; by particle count against
    /// cl_particle_show_bbox_cost when it is set (a negative cost also shows
    /// the cheap ones, which are otherwise not drawn); else red for an effect
    /// that updates its own box, green for one that does not.
    /// </summary>
    public static (bool Draw, int R, int G, int B) DebugBox(bool culled, int showBBoxCost, int activeParticles, bool autoUpdateBBox)
    {
        bool showCheap = false;
        int cost = showBBoxCost;
        if (cost < 0)
        {
            cost = -cost;
            showCheap = true;
        }
        if (culled)
            return (true, 64, 64, 64);
        if (cost > 0)
        {
            float amount = (float)activeParticles / (float)cost;
            if (amount < 0.5f)
                return showCheap ? (true, 0, 255, 0) : (false, 0, 0, 0);
            if (amount < 1.0f)
            {
                int blend = Math.Min(255, Math.Max(0, (int)(512.0f * (amount - 0.5f))));
                return (true, 0, 255 - blend, blend);
            }
            if (amount < 2.0f)
            {
                int blend = Math.Min(255, Math.Max(0, (int)(256.0f * (amount - 1.0f))));
                return (true, blend, 0, 255 - blend);
            }
            return (true, 255, 0, 0);
        }
        return autoUpdateBBox ? (true, 255, 0, 0) : (true, 0, 255, 0);
    }

    /// <summary>vmatrix.cpp's Vector3DMultiplyPositionProjective.</summary>
    public static Vector MultiplyPositionProjective(in Matrix4x4Raw m, Vector v)
    {
        float w = m.M30 * v.X + m.M31 * v.Y + m.M32 * v.Z + m.M33;
        if (w != 0.0f)
            w = 1.0f / w;
        float x = m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z + m.M03;
        float y = m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z + m.M13;
        float z = m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z + m.M23;
        return new Vector(x * w, y * w, z * w);
    }

    /// <summary>
    /// EarlyRetireParticleSystems for the listed effects (in list order): which
    /// to retire (ReplaceWith their definition's replacement). Answers the
    /// pass's result, which is stock's: true once past the early returns,
    /// whatever was retired (bRetiredCollections starts true), so the list is
    /// built again. <paramref name="checkedFrames"/> is each definition's
    /// m_nRetireCheckFrame.
    /// </summary>
    public static bool EarlyRetire(ReadOnlySpan<NewParticleRetireIn> listed, in NewParticleRetireView view, float retireCost,
                                   int frameCount, Dictionary<ulong, int> checkedFrames, List<int> retire)
    {
        if (view.Width == 0 || view.Height == 0)
            return false;
        float maxScreenArea = retireCost * 1000.0f;
        if (maxScreenArea == 0.0f)
            return false;

        var defs = new List<ulong>();
        foreach (ref readonly NewParticleRetireIn e in listed)
        {
            if (e.CullRadius == 0.0f || !e.FirstFrame)
                continue;
            if (checkedFrames.TryGetValue(e.Definition, out int frame) && frame == frameCount)
                continue;
            checkedFrames[e.Definition] = frameCount;
            defs.Add(e.Definition);
        }
        if (defs.Count == 0)
            return false;

        float focalDist = MathF.Tan(view.Fov * 0.5f * (MathF.PI / 180.0f));
        var info = new List<(int Index, float Area, bool FirstFrame)>();
        foreach (ulong def in defs)
        {
            float total = ScreenArea(listed, def, view, focalDist, info);
            if (total > maxScreenArea)
                Retire(listed, info, total, maxScreenArea, retire);
        }
        return true;
    }

    // ComputeParticleDefScreenArea: every listed effect of the definition (all
    // of them carry the cull-check mark during the pass), in list order.
    private static float ScreenArea(ReadOnlySpan<NewParticleRetireIn> listed, ulong def, in NewParticleRetireView view, float focalDist,
                                    List<(int Index, float Area, bool FirstFrame)> info)
    {
        info.Clear();
        float total = 0.0f;
        for (int i = 0; i < listed.Length; i++)
        {
            ref readonly NewParticleRetireIn e = ref listed[i];
            if (e.Definition != def)
                continue;
            float cullRadiusSqr = e.CullRadius * e.CullRadius;
            Vector screen = MultiplyPositionProjective(view.WorldToScreen, e.CullPoint);
            float dx = e.CullPoint.X - view.Origin.X, dy = e.CullPoint.Y - view.Origin.Y, dz = e.CullPoint.Z - view.Origin.Z;
            float lSqr = dx * dx + dy * dy + dz * dz;
            float projRadius = lSqr > cullRadiusSqr ? 0.5f * focalDist * e.CullRadius / MathF.Sqrt(lSqr - cullRadiusSqr) : 1.0f;
            projRadius *= view.Width;
            float minX = MathF.Max(view.X, screen.X - projRadius);
            float maxX = MathF.Min(view.X + view.Width, screen.X + projRadius);
            float minY = MathF.Max(view.Y, screen.Y - projRadius);
            float maxY = MathF.Min(view.Y + view.Height, screen.Y + projRadius);
            float area = (maxX - minX) * (maxY - minY) * e.CullFillCost;
            total += area;
            info.Add((i, area, e.FirstFrame));
        }
        return total;
    }

    // RetireParticleCollections: largest first; only effects in their first
    // frame can be replaced, until the area fits.
    private static void Retire(ReadOnlySpan<NewParticleRetireIn> listed, List<(int Index, float Area, bool FirstFrame)> info,
                               float area, float maxArea, List<int> retire)
    {
        // The replacement is the definition's, so any of its effects answers.
        bool hasReplacement = info.Count > 0 && listed[info[0].Index].HasReplacement;
        if (!hasReplacement && info.Count <= 1)
            return;
        // qsort by area, descending; glibc's qsort merges, so equal areas keep their order.
        var sorted = info.Select((x, n) => (x, n)).OrderByDescending(p => p.x.Area).ThenBy(p => p.n).Select(p => p.x).ToList();
        foreach ((int index, float a, bool firstFrame) in sorted)
        {
            if (area <= maxArea)
                break;
            if (!firstFrame)
                continue;
            retire.Add(index);
            area -= a;
        }
    }
}
