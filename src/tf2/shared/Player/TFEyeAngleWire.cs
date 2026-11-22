//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Player;

/// <summary>
/// <c>m_angEyeAngles</c> on the wire as stock's send table has it
/// (tf_player.cpp:731-732): the pitch a <c>SendPropFloat</c> of 8 bits over
/// [-90, 90], the yaw a <c>SendPropAngle</c> of 10 bits. The encoding is the
/// engine's clamped-range float (dt_encode.cpp <c>EncodeFloat</c> /
/// <c>DecodeFloat</c>, the multiplier <c>AssignRangeMultiplier</c> makes,
/// rounded by <c>FastFloatToSmallInt</c>), the yaw first through
/// <c>SendProxy_AngleToFloat</c>'s <c>anglemod</c>.
/// </summary>
public static class TFEyeAngleWire
{
    public const int PitchBits = 8;
    public const int YawBits = 10;

    private const float PitchLow = -90f, PitchHigh = 90f;

    private const float PitchMul = ((1 << PitchBits) - 1) / (PitchHigh - PitchLow);
    // AssignRangeMultiplier (dt_send.cpp:318): 1023/360 as a float times 360
    // is 1023.0000114, past the highest value, so stock squeezes it by its
    // first multiplier, 0.9999. 255/180 times 180 is 254.9999928 and stays.
    private const float YawMul = (float)((float)(((1 << YawBits) - 1) / 360.0) * 0.9999);

    /// <summary>The pitch's wire value; out of range clamps, as stock's does.</summary>
    public static int EncodePitch(float pitch)
    {
        if (pitch < PitchLow)
            return 0;
        if (pitch > PitchHigh)
            return (1 << PitchBits) - 1;
        return (int)MathF.Round((pitch - PitchLow) * PitchMul, MidpointRounding.ToEven);
    }

    public static float DecodePitch(int wire)
        => PitchLow + (PitchHigh - PitchLow) * ((float)wire / ((1 << PitchBits) - 1));

    /// <summary>The yaw's wire value, after <c>anglemod</c>.</summary>
    public static int EncodeYaw(float yaw)
    {
        float a = (360f / 65536) * ((int)(yaw * (65536f / 360f)) & 65535);
        return (int)MathF.Round(a * YawMul, MidpointRounding.ToEven);
    }

    public static float DecodeYaw(int wire) => 360f * ((float)wire / ((1 << YawBits) - 1));

    /// <summary>
    /// A server eye pitch in stock's -90..90 domain: the engine keeps view
    /// pitch in [-89, 89], but an angle carried as 0..360 reads 350 for -10.
    /// </summary>
    public static float SignedPitch(float pitch)
    {
        pitch %= 360f;
        if (pitch > 180f)
            pitch -= 360f;
        else if (pitch < -180f)
            pitch += 360f;
        return pitch;
    }
}
