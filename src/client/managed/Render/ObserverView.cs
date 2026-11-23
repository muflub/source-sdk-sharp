//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>What the observed entity looks like to the camera this frame.</summary>
/// <param name="Index">The entity index, for the trace filter's pass entity.</param>
/// <param name="CamOrigin">GetObserverCamOrigin: the drawn (render) origin.</param>
/// <param name="EyeAngles">EyeAngles.</param>
/// <param name="EyePosition">EyePosition.</param>
/// <param name="Visible">Has a model (stock's `GetBaseAnimating() || GetModel()`).</param>
/// <param name="IsPlayer">A player, whose chase offset is its view height.</param>
/// <param name="Alive">IsAlive.</param>
/// <param name="Ducking">FL_DUCKING in its flags.</param>
/// <param name="ViewOffset">GetViewOffset.</param>
public readonly record struct ObserverTarget(int Index, Vector CamOrigin, QAngle EyeAngles, Vector EyePosition,
                                            bool Visible, bool IsPlayer, bool Alive, bool Ducking, Vector ViewOffset);

/// <summary>One frame's inputs to the observer's view (CBasePlayer::CalcObserverView's).</summary>
/// <param name="Mode">OBS_MODE_* (<see cref="ObserverMode"/>).</param>
/// <param name="Target">GetObserverTarget, or null.</param>
/// <param name="EyePosition">The local player's EyePosition.</param>
/// <param name="EyeAngles">The local player's EyeAngles (its view angles).</param>
/// <param name="ViewAngles">engine->GetViewAngles.</param>
/// <param name="ViewOffset">The local player's GetViewOffset.</param>
/// <param name="PunchAngle">The local player's GetPunchAngle.</param>
/// <param name="FrameTime">gpGlobals->frametime.</param>
/// <param name="CurTime">gpGlobals->curtime.</param>
/// <param name="DeathTime">m_flDeathTime: when the local player died.</param>
/// <param name="ForceCameraAllowAll">mp_forcecamera == OBS_ALLOW_ALL.</param>
/// <param name="RagdollOrigin">The local player's ragdoll origin, when it has one.</param>
public readonly record struct ObserverInputs(ObserverMode Mode, ObserverTarget? Target, Vector EyePosition, QAngle EyeAngles,
                                            QAngle ViewAngles, Vector ViewOffset, QAngle PunchAngle, float FrameTime,
                                            float CurTime, float DeathTime, bool ForceCameraAllowAll, Vector? RagdollOrigin);

/// <summary>The view an observer mode produced, and whether the engine's view angles follow it (in-eye).</summary>
public readonly record struct ObserverViewResult(Vector Origin, QAngle Angles, bool SetViewAngles);

/// <summary>A hull trace's answer: how far it got and where it stopped.</summary>
public readonly record struct ObserverTrace(float Fraction, Vector End);

/// <summary>Which entities a camera trace may stop on.</summary>
public enum ObserverTraceFilter
{
    /// <summary>CTraceFilterNoNPCsOrPlayer passing the target (the chase camera's).</summary>
    NoPlayersPassing = 0,

    /// <summary>CTraceFilterSimple passing the local player (the death camera's).</summary>
    SimplePassingSelf = 1,

    /// <summary>CTraceFilterSimple passing the target (the freeze camera's).</summary>
    SimplePassingTarget = 2,
}

/// <summary>
/// C_BasePlayer's freeze-frame state, set the frame the freeze cam begins
/// (c_baseplayer.cpp:885-891): where the camera starts, when, how far from
/// the target it stops and how far above its eyes.
/// </summary>
/// <param name="Start">m_vecFreezeFrameStart: the view the camera leaves from (MainViewOrigin).</param>
/// <param name="StartTime">m_flFreezeFrameStartTime.</param>
/// <param name="Distance">m_flFreezeFrameDistance: RandomFloat( spec_freeze_distance_min, _max ).</param>
/// <param name="ZOffset">m_flFreezeZOffset: RandomFloat( -30, 20 ).</param>
/// <param name="TravelTime">spec_freeze_traveltime.</param>
public readonly record struct FreezeFrame(Vector Start, float StartTime, float Distance, float ZOffset, float TravelTime);

/// <summary>The world the camera traces against: UTIL_TraceHull, MASK_SOLID, WALL_MIN..WALL_MAX.</summary>
public interface IObserverWorld
{
    ObserverTrace TraceHull(Vector start, Vector end, ObserverTraceFilter filter, int passIndex);
}

/// <summary>
/// The observer's view (plan_client_novalve C7 item 5): stock's
/// CBasePlayer::CalcObserverView and the C_BasePlayer views it chooses between
/// -- CalcInEyeCamView, CalcChaseCamView, CalcRoamingView, CalcDeathCamView
/// (c_baseplayer.cpp, baseplayer_shared.cpp) -- from the local player's
/// networked ObserverMode and ObserverTarget. The one piece of state stock keeps
/// across frames, m_flObserverChaseDistance, is this object's.
///
/// <para>
/// LEFT AS STOCK LEAVES THEM, OR OUT: spec_track and the prediction-error
/// smoothing vector of CalcRoamingView are not carried (no spec_track convar,
/// and the player's prediction error is not smoothed in this build); the
/// HLTV arm of the in-eye view is out (no HLTV client); a model scale is 1.
/// </para>
/// </summary>
public sealed class ObserverView
{
    public const float ChaseCamDistanceMin = 16.0f;
    public const float ChaseCamDistanceMax = 96.0f;
    public const float DeathAnimationTime = 3.0f;

    // gamerules.cpp's g_DefaultViewVectors.
    public static readonly Vector ViewHeight = new(0, 0, 64);
    public static readonly Vector DuckViewHeight = new(0, 0, 28);
    public static readonly Vector DeadViewHeight = new(0, 0, 14);

    /// <summary>m_flObserverChaseDistance.</summary>
    public float ChaseDistance { get; set; }

    /// <summary>
    /// The players' standing hull height, VEC_HULL_MAX's z (the mod's view
    /// vectors: 72 stock, 82 in TF2): the freeze cam aims at its middle.
    /// </summary>
    public float HullHeight { get; set; } = 72f;

    /// <summary>The freeze cam's flight, or null outside it (<see cref="FreezeFrame"/>).</summary>
    public FreezeFrame? Freeze { get; set; }

    /// <summary>m_bSentFreezeFrame: the flight arrived and the picture was frozen.</summary>
    public bool SentFreezeFrame { get; set; }

    /// <summary>
    /// The view the last frame drew (CViewRender's m_View, which stock hands
    /// CalcView as its starting eyeOrigin and eyeAngles): the chase camera
    /// measures its distance from it and the death camera starts from its
    /// angles. Set by the caller from what it published.
    /// </summary>
    public Vector LastOrigin { get; set; }

    public QAngle LastAngles { get; set; }

    /// <summary>
    /// The view for <paramref name="input"/>, or null when the mode draws the
    /// player's own view (OBS_MODE_NONE).
    /// </summary>
    public ObserverViewResult? Calc(in ObserverInputs input, IObserverWorld world)
    {
        switch (input.Mode)
        {
            case ObserverMode.DeathCam: return DeathCam(input, world);
            // C_BasePlayer::GetObserverTarget: "no target in roaming mode",
            // so the roaming view is the player's own eye, which its own
            // movement flies. With the target it was the target's eye, and
            // the free flight moved a player nobody saw from.
            case ObserverMode.Roaming: return Roaming(input with { Target = null });
            case ObserverMode.Fixed: return Roaming(input);
            case ObserverMode.FreezeCam: return FreezeCam(input, world);
            case ObserverMode.InEye: return InEye(input, world);
            case ObserverMode.PointOfInterest:
            case ObserverMode.Chase: return Chase(input, LastAngles, world);
            default: return null;
        }
    }

    /// <summary>VEC_VIEW and VEC_DUCK_VIEW of the mod's view vectors (64 and 28 stock, 72 and 45 in TF2).</summary>
    public Vector View { get; set; } = ViewHeight;

    public Vector DuckView { get; set; } = DuckViewHeight;

    // C_BasePlayer::GetChaseCamViewOffset: VEC_VIEW_SCALED and VEC_DUCK_VIEW_SCALED.
    private Vector ChaseCamViewOffset(in ObserverTarget target)
    {
        if (target.IsPlayer)
        {
            if (target.Alive)
                return target.Ducking ? DuckView : View;
            return DeadViewHeight;
        }
        return target.ViewOffset;
    }

    // C_BasePlayer::CalcChaseCamView. `eyeAngles` is the angles the caller had
    // in hand: the in-eye view hands over its own.
    private ObserverViewResult Chase(in ObserverInputs input, QAngle eyeAngles, IObserverWorld world)
    {
        if (input.Target is not { } target)
            return new ObserverViewResult(input.EyePosition, input.EyeAngles, false);

        if (!target.Visible)
            return Roaming(input);

        Vector origin = target.CamOrigin + ChaseCamViewOffset(target);

        QAngle viewAngles = input.Mode == ObserverMode.InEye ? eyeAngles : input.ViewAngles;

        ChaseDistance += input.FrameTime * 48.0f;
        ChaseDistance = Math.Clamp(ChaseDistance, ChaseCamDistanceMin, ChaseCamDistanceMax);

        Vector forward = MoveVec.AngleForward(viewAngles.AsVector()).Normalized;
        Vector viewpoint = origin - forward * ChaseDistance;

        ObserverTrace trace = world.TraceHull(origin, viewpoint, ObserverTraceFilter.NoPlayersPassing, target.Index);
        if (trace.Fraction < 1.0f)
        {
            viewpoint = trace.End;
            // Stock measures from eyeOrigin, which still holds what CalcView was
            // handed: the last frame's view.
            ChaseDistance = (origin - LastOrigin).Length;
        }

        return new ObserverViewResult(viewpoint, viewAngles, false);
    }

    // C_BasePlayer::CalcInEyeCamView
    private ObserverViewResult InEye(in ObserverInputs input, IObserverWorld world)
    {
        if (input.Target is not { } target)
            return new ObserverViewResult(input.EyePosition, input.EyeAngles, false);

        if (!target.Alive)
            return Chase(input, LastAngles, world);

        ChaseDistance = 0.0f;

        QAngle angles = target.EyeAngles;
        angles = new QAngle(angles.Pitch + input.PunchAngle.Pitch, angles.Yaw + input.PunchAngle.Yaw,
                            angles.Roll + input.PunchAngle.Roll);

        // Not HLTV: the OBSERVER's view offset, as stock has it ("hack hack").
        Vector origin = target.CamOrigin + input.ViewOffset;
        return new ObserverViewResult(origin, angles, true);
    }

    // C_BasePlayer::CalcRoamingView, without spec_track and the smoothing vector.
    private ObserverViewResult Roaming(in ObserverInputs input)
    {
        ChaseDistance = 0.0f;
        if (input.Target is { } target)
            return new ObserverViewResult(target.EyePosition, target.EyeAngles, false);
        return new ObserverViewResult(input.EyePosition, input.EyeAngles, false);
    }

    // C_BasePlayer::CalcFreezeCamView (c_baseplayer.cpp:1652-1730): fly from
    // where the view was to a spot in front of the target, at its eyes'
    // height plus the random offset, and look at its chest (its eyes when a
    // wall pushes the camera close). The caller freezes the picture once
    // SentFreezeFrame turns true.
    private ObserverViewResult FreezeCam(in ObserverInputs input, IObserverWorld world)
    {
        if (input.Target is not { } target || Freeze is not { } freeze)
            return DeathCam(input, world);

        float elapsed = input.CurTime - freeze.StartTime;
        float blend = SimpleSpline(Math.Clamp(elapsed / freeze.TravelTime, 0f, 1f));

        Vector camDesired = target.CamOrigin + ChaseCamViewOffset(target);
        Vector camTarget = camDesired;
        camTarget = target.Alive
            // Look at their chest, not their head.
            ? new Vector(camTarget.X, camTarget.Y, camTarget.Z - HullHeight * 0.5f)
            // Look over the ragdoll, not through it.
            : new Vector(camTarget.X, camTarget.Y, camTarget.Z + DeadViewHeight.Z);

        // A view position in front of the target: from the camera's own spot
        // (the last frame's view), on the target's plane.
        Vector eyeOnPlane = new(LastOrigin.X, LastOrigin.Y, camTarget.Z);
        Vector toTarget = (camTarget - eyeOnPlane).Normalized;

        // Stop a few units away, at the target's eye height plus the offset.
        Vector targetPos = camTarget - toTarget * freeze.Distance;
        targetPos = new Vector(targetPos.X, targetPos.Y, target.EyePosition.Z + freeze.ZOffset);

        // Out from the target, so the camera stays in front of any wall.
        ObserverTrace trace = world.TraceHull(camTarget, targetPos, ObserverTraceFilter.SimplePassingTarget, target.Index);
        if (trace.Fraction < 1.0f)
        {
            // Close: aim at the eyes, and lift the view so it does not look up a chin.
            targetPos = trace.End;
            camTarget = camDesired;
            targetPos = new Vector(targetPos.X, targetPos.Y,
                                   targetPos.Z + MathF.Abs(camTarget.Z - targetPos.Z) * 0.85f);
            trace = world.TraceHull(camTarget, targetPos, ObserverTraceFilter.SimplePassingTarget, target.Index);
            targetPos = trace.End;
        }

        // Look directly at the target.
        Vector look = (camTarget - targetPos).Normalized;
        QAngle angles = QAngle.FromVector(VectorAngles(look));
        Vector origin = freeze.Start + (targetPos - freeze.Start) * blend;

        if (elapsed >= freeze.TravelTime)
            SentFreezeFrame = true;

        return new ObserverViewResult(origin, angles, false);
    }

    // mathlib's SimpleSpline: 3x^2 - 2x^3.
    internal static float SimpleSpline(float value)
    {
        float squared = value * value;
        return 3 * squared - 2 * squared * value;
    }

    // C_BasePlayer::CalcDeathCamView
    private ObserverViewResult DeathCam(in ObserverInputs input, IObserverWorld world)
    {
        // The last frame's angles, unless the killer may be watched: then the
        // player's own.
        QAngle eyeAngles = LastAngles;
        ObserverTarget? killer = null;
        if (input.ForceCameraAllowAll)
        {
            killer = input.Target;
            eyeAngles = input.EyeAngles;
        }

        float interpolation = Math.Clamp((input.CurTime - input.DeathTime) / DeathAnimationTime, 0.0f, 1.0f);

        ChaseDistance += input.FrameTime * 48.0f;
        ChaseDistance = Math.Clamp(ChaseDistance, ChaseCamDistanceMin * 2, ChaseCamDistanceMax);

        QAngle forward = eyeAngles;
        Vector origin = input.EyePosition;
        if (input.RagdollOrigin is { } ragdoll)
            origin = ragdoll + new Vector(0, 0, DeadViewHeight.Z);

        if (killer is { IsPlayer: true } k)
        {
            Vector toKiller = k.EyePosition - origin;
            Vector killerAngles = VectorAngles(toKiller);
            eyeAngles = InterpolateAngles(forward, QAngle.FromVector(killerAngles), interpolation);
        }

        Vector direction = MoveVec.AngleForward(eyeAngles.AsVector()).Normalized;
        Vector eyeOrigin = origin - direction * ChaseDistance;

        ObserverTrace trace = world.TraceHull(origin, eyeOrigin, ObserverTraceFilter.SimplePassingSelf, -1);
        if (trace.Fraction < 1.0f)
        {
            eyeOrigin = trace.End;
            ChaseDistance = (origin - eyeOrigin).Length;
        }

        return new ObserverViewResult(eyeOrigin, eyeAngles, false);
    }

    // mathlib's VectorAngles (the one-output form).
    internal static Vector VectorAngles(Vector forward)
    {
        float yaw, pitch;
        if (forward.Y == 0 && forward.X == 0)
        {
            yaw = 0;
            pitch = forward.Z > 0 ? 270 : 90;
        }
        else
        {
            yaw = (float)(Math.Atan2(forward.Y, forward.X) * 180 / Math.PI);
            if (yaw < 0)
                yaw += 360;
            float tmp = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
            pitch = (float)(Math.Atan2(-forward.Z, tmp) * 180 / Math.PI);
            if (pitch < 0)
                pitch += 360;
        }
        return new Vector(pitch, yaw, 0);
    }

    // mathlib's InterpolateAngles: through quaternions, slerped.
    internal static QAngle InterpolateAngles(QAngle start, QAngle end, float t)
    {
        if (start == end)
            return start;
        Span<float> p = stackalloc float[4], q = stackalloc float[4], r = stackalloc float[4];
        AngleQuaternion(start, p);
        AngleQuaternion(end, q);
        QuaternionSlerp(p, q, t, r);
        return QuaternionAngles(r);
    }

    private static void AngleQuaternion(QAngle a, Span<float> q)
    {
        float d = MathF.PI / 360.0f;
        float sy = MathF.Sin(a.Yaw * d), cy = MathF.Cos(a.Yaw * d);
        float sp = MathF.Sin(a.Pitch * d), cp = MathF.Cos(a.Pitch * d);
        float sr = MathF.Sin(a.Roll * d), cr = MathF.Cos(a.Roll * d);
        float srXcp = sr * cp, crXsp = cr * sp;
        q[0] = srXcp * cy - crXsp * sy;
        q[1] = crXsp * cy + srXcp * sy;
        float crXcp = cr * cp, srXsp = sr * sp;
        q[2] = crXcp * sy - srXsp * cy;
        q[3] = crXcp * cy + srXsp * sy;
    }

    // QuaternionSlerp: QuaternionAlign, then the no-align slerp.
    private static void QuaternionSlerp(ReadOnlySpan<float> p, ReadOnlySpan<float> q, float t, Span<float> r)
    {
        Span<float> qa = stackalloc float[4];
        float a = 0, b = 0;
        for (int i = 0; i < 4; i++)
        {
            a += (p[i] - q[i]) * (p[i] - q[i]);
            b += (p[i] + q[i]) * (p[i] + q[i]);
        }
        for (int i = 0; i < 4; i++)
            qa[i] = a > b ? -q[i] : q[i];

        float cosom = p[0] * qa[0] + p[1] * qa[1] + p[2] * qa[2] + p[3] * qa[3];
        float sclp, sclq;
        if (1.0f + cosom > 0.000001f)
        {
            if (1.0f - cosom > 0.000001f)
            {
                float omega = MathF.Acos(cosom);
                float sinom = MathF.Sin(omega);
                sclp = MathF.Sin((1.0f - t) * omega) / sinom;
                sclq = MathF.Sin(t * omega) / sinom;
            }
            else
            {
                sclp = 1.0f - t;
                sclq = t;
            }
            for (int i = 0; i < 4; i++)
                r[i] = sclp * p[i] + sclq * qa[i];
        }
        else
        {
            r[0] = -qa[1];
            r[1] = qa[0];
            r[2] = -qa[3];
            r[3] = qa[2];
            sclp = MathF.Sin((1.0f - t) * (0.5f * MathF.PI));
            sclq = MathF.Sin(t * (0.5f * MathF.PI));
            for (int i = 0; i < 3; i++)
                r[i] = sclp * p[i] + sclq * r[i];
        }
    }

    // QuaternionAngles, through the matrix as mathlib does.
    private static QAngle QuaternionAngles(ReadOnlySpan<float> q)
    {
        float m00 = 1.0f - 2.0f * q[1] * q[1] - 2.0f * q[2] * q[2];
        float m10 = 2.0f * q[0] * q[1] + 2.0f * q[3] * q[2];
        float m20 = 2.0f * q[0] * q[2] - 2.0f * q[3] * q[1];
        float m21 = 2.0f * q[1] * q[2] + 2.0f * q[3] * q[0];
        float m22 = 1.0f - 2.0f * q[0] * q[0] - 2.0f * q[1] * q[1];
        float m01 = 2.0f * q[0] * q[1] - 2.0f * q[3] * q[2];
        float m11 = 1.0f - 2.0f * q[0] * q[0] - 2.0f * q[2] * q[2];

        float xyDist = MathF.Sqrt(m00 * m00 + m10 * m10);
        float pitch, yaw, roll;
        if (xyDist > 0.001f)
        {
            yaw = MathF.Atan2(m10, m00) * (180.0f / MathF.PI);
            pitch = MathF.Atan2(-m20, xyDist) * (180.0f / MathF.PI);
            roll = MathF.Atan2(m21, m22) * (180.0f / MathF.PI);
        }
        else
        {
            yaw = MathF.Atan2(-m01, m11) * (180.0f / MathF.PI);
            pitch = MathF.Atan2(-m20, xyDist) * (180.0f / MathF.PI);
            roll = 0;
        }
        return new QAngle(pitch, yaw, roll);
    }
}
