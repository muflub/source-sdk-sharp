//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>dt_common.h's SendPropType (DPT_*), as this build compiles it (no SUPPORTS_INT64).</summary>
public enum SendPropType
{
    Int       = 0,
    Float     = 1,
    Vector    = 2,
    VectorXY  = 3,
    String    = 4,
    Array     = 5,
    DataTable = 6,
}

/// <summary>
/// Which of dt_send.cpp's standard proxies a prop carries. Native turns the id
/// into the function pointer (the engine compares those with
/// g_StandardSendProxies, so the proxy must be the real one, never a copy).
/// The values are SOURCESHARP_SENDPROXY_* in sourcesharp.h.
/// </summary>
public enum SendProxyId
{
    None                 = 0,
    Int8ToInt32          = 1,
    Int16ToInt32         = 2,
    Int32ToInt32         = 3,
    UInt8ToInt32         = 4,
    UInt16ToInt32        = 5,
    UInt32ToInt32        = 6,
    FloatToFloat         = 7,
    VectorToVector       = 8,
    AngleToFloat         = 9,
    QAngles              = 10,
    StringToString       = 11,
    DataTableToDataTable = 12,
    Empty                = 13,
}

/// <summary>
/// One SendProp, decided: every field the engine reads from it, as
/// dt_send.cpp's SendPropFloat / SendPropVector / SendPropAngle /
/// SendPropQAngles / SendPropInt / SendPropDataTable / InternalSendPropArray
/// would have left it. Native stores these and decides nothing.
/// </summary>
/// <param name="Name">The prop's name, which the engine keeps by pointer.</param>
/// <param name="Type">DPT_*.</param>
/// <param name="Offset">Into the sender; negative for a vector elem until the engine flips it.</param>
/// <param name="Flags">SPROP_*.</param>
/// <param name="Bits">m_nBits.</param>
/// <param name="Low">m_fLowValue.</param>
/// <param name="High">m_fHighValue.</param>
/// <param name="HighLowMul">m_fHighLowMul; read by the encoder's clamped-range float path only.</param>
/// <param name="Elements">m_nElements (1 unless an array).</param>
/// <param name="ElementStride">m_ElementStride (-1 unless an array).</param>
/// <param name="Proxy">The var proxy, or the datatable proxy for a DPT_DataTable.</param>
/// <param name="DataTable">The nested table's build handle, or -1.</param>
public readonly record struct SendPropRow(
    string       Name,
    SendPropType Type,
    int          Offset,
    int          Flags,
    int          Bits,
    float        Low,
    float        High,
    float        HighLowMul,
    int          Elements,
    int          ElementStride,
    SendProxyId  Proxy,
    int          DataTable);

/// <summary>
/// dt_send.cpp's SendProp constructors' decisions, in C#: the NOSCALE rule for a
/// width of 32 (or of 0 or less, for a float), the high value's default and the
/// round-up/round-down trim, the range multiplier, the int proxy by storage
/// size and signedness, and the collapsible rule for a nested table at offset
/// zero. Facts in SendPropPlanTests, from stock's bodies.
/// </summary>
public static class SendPropPlan
{
    /// <summary>dt_common.h's SPROP_UNSIGNED.</summary>
    public const int Unsigned = 1 << 0;

    /// <summary>dt_common.h's SPROP_COORD.</summary>
    public const int Coord = 1 << 1;

    /// <summary>dt_common.h's SPROP_NOSCALE.</summary>
    public const int NoScale = 1 << 2;

    /// <summary>dt_common.h's SPROP_ROUNDDOWN.</summary>
    public const int RoundDown = 1 << 3;

    /// <summary>dt_common.h's SPROP_ROUNDUP.</summary>
    public const int RoundUp = 1 << 4;

    /// <summary>dt_common.h's SPROP_NORMAL.</summary>
    public const int Normal = 1 << 5;

    /// <summary>dt_common.h's SPROP_PROXY_ALWAYS_YES.</summary>
    public const int ProxyAlwaysYes = 1 << 9;

    /// <summary>dt_common.h's SPROP_COLLAPSIBLE.</summary>
    public const int Collapsible = 1 << 12;

    /// <summary>dt_common.h's SPROP_COORD_MP.</summary>
    public const int CoordMp = 1 << 13;

    /// <summary>dt_common.h's SPROP_COORD_MP_LOWPRECISION.</summary>
    public const int CoordMpLowPrecision = 1 << 14;

    /// <summary>dt_common.h's SPROP_COORD_MP_INTEGRAL.</summary>
    public const int CoordMpIntegral = 1 << 15;

    /// <summary>dt_common.h's HIGH_DEFAULT: "the high value is 1 &lt;&lt; nBits".</summary>
    public const float HighDefault = -121121.121121f;

    /// <summary>mathlib.h's EQUAL_EPSILON, CloseEnough's default.</summary>
    private const float EqualEpsilon = 0.001f;

    // The flags whose encoder ignores m_nBits: the constructors zero it.
    private const int WidthlessFlags = Coord | NoScale | Normal | CoordMp | CoordMpLowPrecision | CoordMpIntegral;

    private static ReadOnlySpan<float> Multipliers => [0.9999f, 0.99f, 0.9f, 0.8f, 0.7f];

    /// <summary>
    /// dt_send.cpp's AssignRangeMultiplier: the largest multiplier that maps the
    /// range onto <paramref name="bits"/> bits without overflowing them, backing
    /// off by 0.9999, 0.99, 0.9, 0.8 and 0.7; 0 when none fits.
    ///
    /// <para>
    /// THE SHIFT IS C's, ON PURPOSE. Stock computes <c>(1 &lt;&lt; nBits) - 1</c>
    /// as an int, and a width outside [0, 31] (stock's own SendPropFloat passes
    /// -1 for a COORD prop) shifts by the count masked to five bits on x86-64,
    /// which is what C#'s int shift does too. The value only reaches the wire on
    /// the clamped-range float path, which those flags never take.
    /// </para>
    /// </summary>
    public static float AssignRangeMultiplier(int bits, double range)
    {
        ulong highValue = bits == 32
            ? 0xFFFFFFFE
            : unchecked((ulong)(long)((1 << bits) - 1));

        float mul = (float)(highValue / range);

        if (MathF.Abs((float)range - 0f) <= EqualEpsilon)
            mul = highValue;

        if (Overflows(mul, range, highValue))
        {
            int i;

            for (i = 0; i < Multipliers.Length; i++)
            {
                mul = (float)(highValue / range) * Multipliers[i];

                if (!Overflows(mul, range, highValue))
                    break;
            }

            if (i == Multipliers.Length)
                return 0f;
        }

        return mul;
    }

    private static bool Overflows(float mul, double range, ulong highValue)
        => (ulong)(mul * range) > highValue || mul * range > highValue;

    /// <summary>dt_send.cpp's SendPropFloat.</summary>
    public static SendPropRow Float(string name, int offset, int bits, int flags,
                                    float low, float high, SendProxyId proxy = SendProxyId.FloatToFloat)
    {
        if (bits <= 0 || bits == 32)
        {
            flags |= NoScale;
            low = 0f;
            high = 0f;
        }
        else
        {
            if (high == HighDefault)
                high = 1 << bits;

            if ((flags & RoundDown) != 0)
                high -= (high - low) / (1 << bits);
            else if ((flags & RoundUp) != 0)
                low += (high - low) / (1 << bits);
        }

        float mul = AssignRangeMultiplier(bits, high - low);

        return new SendPropRow(name, SendPropType.Float, offset, flags,
                               (flags & WidthlessFlags) != 0 ? 0 : bits,
                               low, high, mul, 1, -1, proxy, -1);
    }

    /// <summary>dt_send.cpp's SendPropVector.</summary>
    public static SendPropRow Vector(string name, int offset, int bits, int flags,
                                     float low, float high, SendProxyId proxy = SendProxyId.VectorToVector)
    {
        if (bits == 32)
            flags |= NoScale;

        float mul = AssignRangeMultiplier(bits, high - low);

        return new SendPropRow(name, SendPropType.Vector, offset, flags,
                               (flags & WidthlessFlags) != 0 ? 0 : bits,
                               low, high, mul, 1, -1, proxy, -1);
    }

    /// <summary>dt_send.cpp's SendPropAngle: [0, 360], AngleToFloat, and the width kept.</summary>
    public static SendPropRow Angle(string name, int offset, int bits, int flags,
                                    SendProxyId proxy = SendProxyId.AngleToFloat)
    {
        if (bits == 32)
            flags |= NoScale;

        return new SendPropRow(name, SendPropType.Float, offset, flags, bits,
                               0f, 360f, AssignRangeMultiplier(bits, 360f - 0f), 1, -1, proxy, -1);
    }

    /// <summary>dt_send.cpp's SendPropQAngles: SendPropAngle's rules on a DPT_Vector.</summary>
    public static SendPropRow QAngles(string name, int offset, int bits, int flags,
                                      SendProxyId proxy = SendProxyId.QAngles)
    {
        if (bits == 32)
            flags |= NoScale;

        return new SendPropRow(name, SendPropType.Vector, offset, flags, bits,
                               0f, 360f, AssignRangeMultiplier(bits, 360f - 0f), 1, -1, proxy, -1);
    }

    /// <summary>
    /// dt_send.cpp's SendPropInt with no proxy given: the proxy by storage size
    /// (an unknown size takes Int8ToInt32, stock's "safest one"), its unsigned
    /// twin under SPROP_UNSIGNED, and a width of 0 or less meaning the storage's.
    /// </summary>
    public static SendPropRow Int(string name, int offset, int sizeofVar, int bits, int flags)
    {
        SendProxyId proxy = sizeofVar switch
        {
            1 => SendProxyId.Int8ToInt32,
            2 => SendProxyId.Int16ToInt32,
            4 => SendProxyId.Int32ToInt32,
            _ => SendProxyId.Int8ToInt32,
        };

        if (bits <= 0)
            bits = sizeofVar * 8;

        if ((flags & Unsigned) != 0)
        {
            proxy = proxy switch
            {
                SendProxyId.Int8ToInt32  => SendProxyId.UInt8ToInt32,
                SendProxyId.Int16ToInt32 => SendProxyId.UInt16ToInt32,
                SendProxyId.Int32ToInt32 => SendProxyId.UInt32ToInt32,
                _                        => proxy,
            };
        }

        return new SendPropRow(name, SendPropType.Int, offset, flags, bits, 0f, 0f, 0f, 1, -1, proxy, -1);
    }

    /// <summary>
    /// dt_send.cpp's SendPropDataTable with the identity proxy: PROXY_ALWAYS_YES,
    /// REPLACED (not joined) by COLLAPSIBLE at offset zero, as stock's two
    /// SetFlags calls leave it.
    /// </summary>
    public static SendPropRow DataTable(string name, int offset, int dataTable)
        => new(name, SendPropType.DataTable, offset, offset == 0 ? Collapsible : ProxyAlwaysYes,
               0, 0f, 0f, 0f, 1, -1, SendProxyId.DataTableToDataTable, dataTable);

    /// <summary>
    /// dt_send.cpp's InternalSendPropArray with no length proxy: the engine
    /// links the element template (the prop before it) in SendTable_InitTable.
    /// </summary>
    public static SendPropRow Array(string name, int elements, int stride)
        => new(name, SendPropType.Array, 0, 0, 0, 0f, 0f, 0f, elements, stride, SendProxyId.Empty, -1);

    /// <summary>
    /// One planned temp entity prop as the SendProp shell_tempents.cpp's
    /// BuildOneProp made of it; <paramref name="dataTable"/> is the nested
    /// table's build handle for a DataTable prop. Null for a kind with no
    /// SendProp.
    /// </summary>
    public static SendPropRow? FromPlan(in TePropPlan plan, int dataTable = -1) => plan.Kind switch
    {
        TePropKind.Int       => Int(plan.Name, plan.DeclareOffset, plan.SizeBytes, plan.Bits, (int)plan.Flags),
        TePropKind.Float     => Float(plan.Name, plan.DeclareOffset, plan.Bits, (int)plan.Flags, plan.Low, plan.High),
        TePropKind.Vector    => Vector(plan.Name, plan.DeclareOffset, plan.Bits, (int)plan.Flags, plan.Low, plan.High),
        TePropKind.Angle     => Angle(plan.Name, plan.DeclareOffset, plan.Bits, (int)plan.Flags),
        TePropKind.QAngles   => QAngles(plan.Name, plan.DeclareOffset, plan.Bits, (int)plan.Flags),
        TePropKind.DataTable => DataTable(plan.Name, plan.DeclareOffset, dataTable),
        TePropKind.Array     => Array(plan.Name, plan.Elems, plan.ElemStride),
        _                    => null,
    };
}
