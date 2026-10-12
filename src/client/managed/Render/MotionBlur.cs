//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>
/// One frame's view and the motion blur cvars, as DoImageSpaceMotionBlur
/// (viewpostprocess.cpp) reads them: <c>gpGlobals->realtime</c>, the blur
/// view's origin, angles, fov and aspect, and mat_motion_blur_forward_enabled,
/// _falling_min, _falling_max, _falling_intensity, _rotation_intensity and
/// _strength.
/// </summary>
public readonly record struct MotionBlurFrame(
    float RealTime,
    Vector Origin,
    Vector Angles,
    float Fov,
    float AspectRatio,
    bool ForwardEnabled,
    float FallingMin,
    float FallingMax,
    float FallingIntensity,
    float RotationIntensity,
    float Strength);

/// <summary>
/// The four values the MotionBlur material proxy hands the motion blur
/// shader: horizontal and vertical rotational blur, the forward/falling blur
/// and roll (stock's <c>g_vMotionBlurValues[0..3]</c>).
/// </summary>
public readonly record struct MotionBlurValues(float X, float Y, float Falling, float Roll);

/// <summary>
/// DoImageSpaceMotionBlur's decisions (viewpostprocess.cpp): from this frame's
/// view and the last one's, the four values the MotionBlur proxy reads. Stock
/// keeps the previous frame in function statics and the values in a global;
/// here they are this object's, one per client. Float arithmetic in stock's
/// order, so the values are stock's to the bit (motionblur-stock.tsv).
/// The caller decides whether blur runs at all (mat_motion_blur_enabled and
/// the DX level): stock returns before touching any of this state when off.
/// </summary>
public sealed class MotionBlur
{
    // DEG2RAD's factor: (float)( M_PI_F / 180.f ), divided in float.
    private const float DegToRad = MathF.PI / 180.0f;

    private float _lastTimeUpdate;
    private float _previousPitch;
    private float _previousYaw;
    private Vector _previousPosition;
    private float _noRotationalBlurUntil;
    private float _x, _y, _falling, _roll;

    /// <summary>The values the last <see cref="Update"/> left: what the proxy hands the shader.</summary>
    public MotionBlurValues Values => new(_x, _y, _falling, _roll);

    /// <summary>Advances one frame. The values persist across frames, as stock's global does.</summary>
    public MotionBlurValues Update(in MotionBlurFrame f)
    {
        float rotationIntensity = f.RotationIntensity * 0.15f;
        const float rollIntensity = 0.3f;

        float timeElapsed = f.RealTime - _lastTimeUpdate;

        float pitch = f.Angles.X;
        while (pitch > 180.0f)
            pitch -= 360.0f;
        while (pitch < -180.0f)
            pitch += 360.0f;

        float yaw = f.Angles.Y;
        while (yaw > 180.0f)
            yaw -= 360.0f;
        while (yaw < -180.0f)
            yaw += 360.0f;

        // AngleMatrix's columns 0 (forward) and 1 (side), over the unwrapped angles.
        SinCos(f.Angles.Y * DegToRad, out float sy, out float cy);
        SinCos(f.Angles.X * DegToRad, out float sp, out float cp);
        SinCos(f.Angles.Z * DegToRad, out float sr, out float cr);
        float fwdX = cp * cy, fwdY = cp * sy, fwdZ = -sp;
        float crcy = cr * cy, crsy = cr * sy, srcy = sr * cy, srsy = sr * sy;
        float sideX = sp * srcy - crsy, sideY = sp * srsy + crcy, sideZ = sr * cp;

        Vector position = f.Origin;
        float chX = _previousPosition.X - position.X;
        float chY = _previousPosition.Y - position.Y;
        float chZ = _previousPosition.Z - position.Z;
        float change = MathF.Sqrt(chX * chX + chY * chY + chZ * chZ);

        if (change > 30.0f && timeElapsed >= 0.5f)
        {
            // Moved far after more than half a second: no blur this frame.
            _x = _y = _falling = _roll = 0.0f;
        }
        else if (timeElapsed > 1.0f / 15.0f)
        {
            // Slower than 15 fps: no blur.
            _x = _y = _falling = _roll = 0.0f;
        }
        else if (change > 50.0f)
        {
            // A far move in one frame (a portal): keep last frame's values and
            // hold off rotational blur for a second.
            _noRotationalBlurUntil = f.RealTime + 1.0f;
        }
        else
        {
            float horizontalFov = f.Fov;
            float verticalFov = f.AspectRatio <= 0.0f ? f.Fov : f.Fov / f.AspectRatio;

            float viewDotMotion = fwdX * chX + fwdY * chY + fwdZ * chZ;
            _falling = f.ForwardEnabled ? viewDotMotion : viewDotMotion * MathF.Abs(fwdZ);

            // Yaw, compensated for circle strafing.
            float sideDotMotion = sideX * chX + sideY * chY + sideZ * chZ;
            float yawDiffOriginal = _previousYaw - yaw;
            if ((_previousYaw - yaw > 180.0f || _previousYaw - yaw < -180.0f) &&
                (_previousYaw + yaw > -180.0f && _previousYaw + yaw < 180.0f))
                yawDiffOriginal = _previousYaw + yaw;

            float yawDiffAdjusted = yawDiffOriginal + sideDotMotion / 3.0f;
            yawDiffAdjusted = yawDiffOriginal < 0.0f
                ? Clamp(yawDiffAdjusted, yawDiffOriginal, 0.0f)
                : Clamp(yawDiffAdjusted, 0.0f, yawDiffOriginal);

            float undampenedYaw = yawDiffAdjusted / horizontalFov;
            _x = undampenedYaw * (1.0f - MathF.Abs(pitch) / 90.0f);

            // Pitch, compensated for forward motion.
            float pitchCompensateMask = 1.0f - (1.0f - MathF.Abs(fwdZ)) * (1.0f - MathF.Abs(fwdZ));
            float pitchDiffOriginal = _previousPitch - pitch;
            float pitchDiffAdjusted = pitch > 0.0f
                ? pitchDiffOriginal - viewDotMotion / 2.0f * pitchCompensateMask
                : pitchDiffOriginal + viewDotMotion / 2.0f * pitchCompensateMask;
            pitchDiffAdjusted = pitchDiffOriginal < 0.0f
                ? Clamp(pitchDiffAdjusted, pitchDiffOriginal, 0.0f)
                : Clamp(pitchDiffAdjusted, 0.0f, pitchDiffOriginal);
            _y = pitchDiffAdjusted / verticalFov;

            // Roll: undampened yaw, scaled by pitch cubed.
            _roll = undampenedYaw;
            _roll *= MathF.Abs(pitch) / 90.0f * (MathF.Abs(pitch) / 90.0f) * (MathF.Abs(pitch) / 90.0f);

            // Time-adjust the falling term, then scale and bias it.
            if (timeElapsed > 0.0f)
                _falling /= timeElapsed * 30.0f;
            else
                _falling = 0.0f;
            _falling = Clamp((MathF.Abs(_falling) - f.FallingMin) / (f.FallingMax - f.FallingMin), 0.0f, 1.0f)
                * (_falling >= 0.0f ? 1.0f : -1.0f);
            _falling /= 30.0f;

            _x *= rotationIntensity * f.Strength;
            _y *= rotationIntensity * f.Strength;
            _falling *= f.FallingIntensity * f.Strength;
            _roll *= rollIntensity * f.Strength;

            // Fade out from 50 fps down to 30.
            const float slowFps = 30.0f, fastFps = 50.0f;
            float currentFps = timeElapsed > 0.0f ? 1.0f / timeElapsed : 0.0f;
            float dampen = Clamp((currentFps - slowFps) / (fastFps - slowFps), 0.0f, 1.0f);
            _x *= dampen;
            _y *= dampen;
            _falling *= dampen;
            _roll *= dampen;
        }

        if (f.RealTime < _noRotationalBlurUntil)
        {
            // Rotational blur held off; forward/falling blur stays.
            _x = 0.0f;
            _y = 0.0f;
            _roll = 0.0f;
        }
        else
        {
            _noRotationalBlurUntil = 0.0f;
        }

        _previousPosition = position;
        _previousPitch = pitch;
        _previousYaw = yaw;
        _lastTimeUpdate = f.RealTime;
        return Values;
    }

    // math_pfns.h's SinCos: sinf and cosf.
    private static void SinCos(float radians, out float sine, out float cosine)
    {
        sine = MathF.Sin(radians);
        cosine = MathF.Cos(radians);
    }

    // mathlib.h's clamp (the DEBUG spelling; the SSE one agrees off NaN).
    private static float Clamp(float val, float minVal, float maxVal)
    {
        if (maxVal < minVal)
            return maxVal;
        if (val < minVal)
            return minVal;
        if (val > maxVal)
            return maxVal;
        return val;
    }
}
