//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

/// <summary>
/// The mathlib the detail props were computed with, spelled as stock compiles
/// it, so every float comes out with the same bits: the angle conversions
/// (mathlib_base.cpp), the lightmap exponent table (color_conversion.cpp's power2_n), float16
/// (compressed_vector.h), tier1's HeapSort and ssemath.h's lane operations.
/// </summary>
public static class DetailMath
{
    /// <summary>mathlib.h's DEG2RAD factor: <c>(float)(M_PI_F / 180.f)</c>, one float division.</summary>
    public const float DegToRad = (float)Math.PI / 180f;

    /// <summary>
    /// mathlib_base.cpp's <c>AngleVectors( angles, &amp;forward, &amp;right, &amp;up )</c>:
    /// math_pfns.h's SinCos is sinf/cosf, and each product keeps stock's order.
    /// </summary>
    public static void AngleVectors(Vector angles, out Vector forward, out Vector right, out Vector up)
    {
        float sy = MathF.Sin(angles.Y * DegToRad), cy = MathF.Cos(angles.Y * DegToRad);
        float sp = MathF.Sin(angles.X * DegToRad), cp = MathF.Cos(angles.X * DegToRad);
        float sr = MathF.Sin(angles.Z * DegToRad), cr = MathF.Cos(angles.Z * DegToRad);

        forward = new Vector(cp * cy, cp * sy, -sp);
        right = new Vector(
            -1 * sr * sp * cy + -1 * cr * -sy,
            -1 * sr * sp * sy + -1 * cr * cy,
            -1 * sr * cp);
        up = new Vector(
            cr * sp * cy + -sr * -sy,
            cr * sp * sy + -sr * cy,
            cr * cp);
    }

    /// <summary>
    /// mathlib_base.cpp's <c>VectorAngles( forward, angles )</c> as it compiles:
    /// the float atan2 overload, the product with 180 in float and the quotient
    /// by M_PI in double. Straight up or down reads yaw 0.
    /// </summary>
    public static Vector VectorAngles(Vector forward)
    {
        float yaw, pitch;
        if (forward.Y == 0 && forward.X == 0)
        {
            yaw = 0;
            pitch = forward.Z > 0 ? 270 : 90;
        }
        else
        {
            yaw = (float)(MathF.Atan2(forward.Y, forward.X) * 180 / Math.PI);
            if (yaw < 0)
                yaw += 360;
            float tmp = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
            pitch = (float)(MathF.Atan2(-forward.Z, tmp) * 180 / Math.PI);
            if (pitch < 0)
                pitch += 360;
        }
        return new Vector(pitch, yaw, 0);
    }

    /// <summary><c>VectorMA( start, scale, direction, dest )</c>, per component.</summary>
    public static Vector MA(Vector start, float scale, Vector direction)
        => new(start.X + scale * direction.X, start.Y + scale * direction.Y, start.Z + scale * direction.Z);

    /// <summary>
    /// color_conversion.cpp's <c>power2_n[ exponent + 128 ]</c>: 2^exponent /
    /// 255, which the table holds as double literals stored as floats. The
    /// quotient rounded to a double and then to a float gives all 256 entries'
    /// bits (DetailPropsTests holds the 256 to the literals' values).
    /// </summary>
    public static float Power2N(int exponent) => (float)(Math.ScaleB(1.0, exponent) / 255.0);

    /// <summary>mathlib.h's <c>TexLightToLinear( c, exponent )</c>.</summary>
    public static float TexLightToLinear(int c, int exponent) => c * Power2N(exponent);

    /// <summary>compressed_vector.h's <c>float16::ConvertFloatTo16bits</c>: clamped, truncating, denorms to zero.</summary>
    public static ushort FloatTo16Bits(float input)
    {
        const float maxfloat16bits = 65504.0f;
        if (input > maxfloat16bits)
            input = maxfloat16bits;
        else if (input < -maxfloat16bits)
            input = -maxfloat16bits;

        uint bits = (uint)BitConverter.SingleToInt32Bits(input);
        uint sign = bits >> 31;
        uint biased = (bits >> 23) & 0xff;
        uint mantissa = bits & 0x7fffff;
        uint outMantissa, outExponent;

        if (biased == 0)
        {
            // zero, and a denormal float, both map to zero
            outMantissa = 0;
            outExponent = 0;
        }
        else if (biased == 0xff)
        {
            // infinity maps to the largest half; NaN to zero
            outMantissa = mantissa == 0 ? 0x3ffu : 0;
            outExponent = mantissa == 0 ? 0x1eu : 0;
        }
        else
        {
            int newExp = (int)biased - 127;
            outMantissa = 0;
            outExponent = 0;
            if (newExp < -14)
            {
                // a denormal half (or zero below 2^-24)
                uint expVal = (uint)(-14 - ((int)biased - 127));
                if (expVal > 0 && expVal < 11)
                    outMantissa = ((1u << (int)(10 - expVal)) + (mantissa >> (int)(13 + expVal))) & 0x3ff;
            }
            else if (newExp > 15)
            {
                outMantissa = 0x3ff;
                outExponent = 0x1e;
            }
            else
            {
                outExponent = (uint)(newExp + 15) & 0x1f;
                outMantissa = (mantissa >> 13) & 0x3ff;
            }
        }
        return (ushort)((sign << 15) | (outExponent << 10) | outMantissa);
    }

    /// <summary>compressed_vector.h's <c>float16::Convert16bitFloatTo32bits</c>.</summary>
    public static float Float16To32(ushort input)
    {
        uint sign = (uint)input >> 15;
        uint biased = ((uint)input >> 10) & 0x1f;
        uint mantissa = (uint)input & 0x3ff;

        if (biased == 31 && mantissa == 0)
            return 65504.0f * (sign == 1 ? -1.0f : 1.0f);
        if (biased == 31)
            return 0.0f;
        if (biased == 0 && mantissa != 0)
        {
            const float halfDenorm = 1.0f / 16384.0f;
            float m = mantissa / 1024.0f;
            float sgn = sign != 0 ? -1.0f : 1.0f;
            return sgn * m * halfDenorm;
        }

        uint exponent = biased != 0 ? (biased - 15 + 127) << 23 : 0;
        return BitConverter.Int32BitsToSingle((int)((mantissa << 13) | exponent | (sign << 31)));
    }

    /// <summary>A float through a float16, as CDetailModel stores a sprite's scale.</summary>
    public static float Half(float f) => Float16To32(FloatTo16Bits(f));

    /// <summary>ssemath.h's MaxSIMD on one lane: <c>maxps</c> answers its second operand unless the first is greater.</summary>
    public static float MaxLane(float a, float b) => a > b ? a : b;

    /// <summary>ssemath.h's MinSIMD on one lane: <c>minps</c> answers its second operand unless the first is less.</summary>
    public static float MinLane(float a, float b) => a < b ? a : b;

    /// <summary>
    /// ssemath.h's <c>ReciprocalSqrtEstSIMD</c> on one lane: the CPU's
    /// <c>rsqrtps</c> estimate, whose last bits differ between CPU makers.
    /// Without SSE, the exact reciprocal (as ssemath.h's generic arm).
    /// </summary>
    public static float ReciprocalSqrtEstimate(float f)
    {
        if (Sse.IsSupported)
            return Sse.ReciprocalSqrt(Vector128.CreateScalar(f)).ToScalar();
        return (float)(1.0 / Math.Sqrt(f));
    }

    /// <summary>The bits of <c>MAGIC_NUMBER + 255 * alpha</c>'s low byte: the alpha the fast sprites write.</summary>
    public static byte MagicByte(float alpha)
    {
        float magic = 8388608.0f + 255.0f * alpha;
        return (byte)(BitConverter.SingleToInt32Bits(magic) & 0xff);
    }

    /// <summary>A float to <c>unsigned char</c> as the compiler converts one: truncated through an int.</summary>
    public static byte ToByte(float f) => unchecked((byte)(int)f);

    /// <summary>A double to <c>unsigned char</c> the same way.</summary>
    public static byte ToByte(double d) => unchecked((byte)(int)d);
}

/// <summary><c>CDetailObjectSystem::SortInfo_t</c>: an object and its squared distance.</summary>
public struct DetailSortInfo
{
    public int Index;
    public float Distance;
}

/// <summary>tier1/heapsort.h's HeapSort over the detail sort entries, with stock's comparison.</summary>
public static class DetailHeapSort
{
    // CDetailObjectSystem::SortLessFunc: the distances' bits compared as int32,
    // so the larger distance sorts first.
    private static bool Less(in DetailSortInfo left, in DetailSortInfo right)
        => BitConverter.SingleToInt32Bits(left.Distance) > BitConverter.SingleToInt32Bits(right.Distance);

    public static void Sort(Span<DetailSortInfo> arr)
    {
        int count = arr.Length;
        // heapify
        for (int endOfHeap = 1; endOfHeap < count; ++endOfHeap)
        {
            int idx = endOfHeap;
            do
            {
                int parent = (idx + 1) / 2 - 1;
                if (Less(arr[parent], arr[idx]))
                    (arr[parent], arr[idx]) = (arr[idx], arr[parent]);
                else
                    break;
                idx = parent;
            }
            while (idx > 0);
        }

        // heap sort
        for (int endOfHeap = count; endOfHeap-- > 1;)
        {
            (arr[0], arr[endOfHeap]) = (arr[endOfHeap], arr[0]);
            int idx = 0;
            for (;;)
            {
                int child = idx * 2 + 1;
                if (child >= endOfHeap)
                    break;
                if (child + 1 < endOfHeap && Less(arr[child], arr[child + 1]))
                    child++;
                if (Less(arr[idx], arr[child]))
                {
                    (arr[idx], arr[child]) = (arr[child], arr[idx]);
                    idx = child;
                }
                else
                {
                    break;
                }
            }
        }
    }
}
