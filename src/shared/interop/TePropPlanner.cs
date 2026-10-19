//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// One prop of a planned temp entity table: what the SEND side builds one
/// <c>SendProp</c> from. The managed twin of <c>SourceSharpTePropPlan_t</c>
/// (tempent_schema.h), which the client still plans its RecvProps with: the two
/// must agree prop for prop, because the client pairs props with the server's by
/// name and a list that differs by one name or offset begins a join and stops.
/// </summary>
/// <param name="Name">The send-side name, <c>"m_vecOrigin[1]"</c> for an element.</param>
/// <param name="Kind">The prop kind; <see cref="TePropKind.Array"/> is the DPT_Array itself.</param>
/// <param name="Offset">Into the blob, always positive: what the client declares with.</param>
/// <param name="DeclareOffset">What the send prop is declared with: <c>-Offset</c> for a vector elem.</param>
/// <param name="SizeBytes">The storage width, which picks SendPropInt's proxy.</param>
/// <param name="Bits">Resolved: no symbolic width survives into a plan.</param>
/// <param name="Flags">The SPROP_* to pass on, with VectorElem stripped.</param>
/// <param name="Low">The declared low value.</param>
/// <param name="High">The declared high value.</param>
/// <param name="Elems">Array only: the DPT_Array's element count.</param>
/// <param name="ElemStride">Array only: its stride.</param>
/// <param name="SubTable">DataTable only: the nested table, by name.</param>
/// <param name="ArrayElement">An array's element template (named differently on the recv side).</param>
/// <param name="Field">The description field this came from; -1 for the baseclass prop.</param>
/// <param name="Elem">Which element of that field.</param>
public readonly record struct TePropPlan(
    string     Name,
    TePropKind Kind,
    int        Offset,
    int        DeclareOffset,
    int        SizeBytes,
    int        Bits,
    TeFlags    Flags,
    float      Low,
    float      High,
    int        Elems,
    int        ElemStride,
    string?    SubTable,
    bool       ArrayElement,
    int        Field,
    int        Elem);

/// <summary>
/// The arithmetic over a temp entity description: widths, wire costs, blob
/// sizes and the prop plan. A port of tempent_schema.h's free functions, which
/// the server's table building used and the client still uses; the facts in
/// TePropPlannerTests are that header's gtests, case for case.
///
/// <para>
/// THREE THINGS ABOUT A TEMP ENTITY CAN BE QUIETLY WRONG and none reports
/// itself: an offset outside the blob networks whatever is beyond it; a width
/// one bit off makes a bitstream stop making sense partway through; a worst case
/// over the engine's 192-byte event cap is a Host_Error mid-game. All three are
/// arithmetic over the description, so all three are here, where a fact can
/// reach them with no running Source.
/// </para>
/// </summary>
public static class TePropPlanner
{
    /// <summary>The engine's per-event data cap, 192 bytes (not in the SDK).</summary>
    public const int MaxEventBytes = 192;

    /// <summary><see cref="MaxEventBytes"/> in bits.</summary>
    public const int MaxEventBits = MaxEventBytes * 8;

    /// <summary>public/coordsize.h's COORD_INTEGER_BITS.</summary>
    public const int CoordIntegerBits = 14;

    /// <summary>public/coordsize.h's COORD_FRACTIONAL_BITS.</summary>
    public const int CoordFractionalBits = 5;

    /// <summary>public/coordsize.h's COORD_FRACTIONAL_BITS_MP_LOWPRECISION.</summary>
    public const int CoordFractionalBitsMpLowPrecision = 3;

    /// <summary>public/coordsize.h's NORMAL_FRACTIONAL_BITS.</summary>
    public const int NormalFractionalBits = 11;

    /// <summary>const.h's MAX_EDICT_BITS.</summary>
    public const int MaxEdictBits = 11;

    /// <summary>const.h's SP_MODEL_INDEX_BITS.</summary>
    public const int ModelIndexBits = 13;

    /// <summary>const.h's EF_MAX_BITS.</summary>
    public const int EfMaxBits = 10;

    /// <summary>baseanimating.h's ANIMATION_SKIN_BITS.</summary>
    public const int AnimationSkinBits = 10;

    /// <summary>effect_dispatch_data.h's MAX_EFFECT_FLAG_BITS.</summary>
    public const int EffectFlagBits = 8;

    /// <summary>effect_dispatch_data.h's MAX_EFFECT_DISPATCH_STRING_BITS.</summary>
    public const int EffectStringBits = 10;

    /// <summary>shareddefs.h's MAX_PLAYERS, the branch this build compiles.</summary>
    public const int MaxPlayers = 33;

    /// <summary>const.h's NUM_NETWORKED_EHANDLE_BITS.</summary>
    public const int EHandleBits = 21;

    /// <summary>
    /// What one prop costs in index bits before its value: the delta writer
    /// charges 7 for a gap of 16 or less, and every prop of a temp entity is
    /// written (gap 1). An upper bound, deliberately.
    /// </summary>
    public const int PropIndexBits = 7;

    /// <summary>The message's own framing before any prop, bounded generously.</summary>
    public const int MessageOverheadBits = 32;

    /// <summary>How many classes the registry holds.</summary>
    public const int MaxClasses = 64;

    /// <summary>How many fields one class may declare.</summary>
    public const int MaxFields = 64;

    /// <summary>The longest prop name, terminator included (SOURCESHARP_TE_PROP_NAME_MAX).</summary>
    public const int PropNameMax = 128;

    /// <summary>The baseclass prop's name, which is stock's own (BEGIN_SEND_TABLE).</summary>
    public const string BaseClassProp = "baseclass";

    /// <summary>How many bits it takes to hold [0, n]: Q_log2 without tier1, plus one.</summary>
    public static int BitsFor(int value)
    {
        int bits = 0;

        while (value > 0)
        {
            bits++;
            value >>= 1;
        }

        return bits;
    }

    /// <summary>
    /// A symbolic width, resolved; -1 for a symbol this does not know.
    /// <see cref="TeBits.MaxPlayersLog2"/> is Q_log2(33), which is FLOOR of log2:
    /// five, one less than the six bits 33 needs.
    /// </summary>
    public static int ResolveBits(TeBits from, int literal) => from switch
    {
        TeBits.Literal              => literal,
        TeBits.MaxEdict             => MaxEdictBits,
        TeBits.ModelIndex           => ModelIndexBits,
        TeBits.EfMax                => EfMaxBits,
        TeBits.AnimationSkin        => AnimationSkinBits,
        TeBits.MaxPlayersLog2       => BitsFor(MaxPlayers) - 1,
        TeBits.EffectFlag           => EffectFlagBits,
        TeBits.EffectDispatchString => EffectStringBits,
        TeBits.NetworkedEHandle     => EHandleBits,
        _                           => -1,
    };

    /// <summary>
    /// The bits ONE float costs at its worst, in the engine encoder's own branch
    /// order: COORD before COORD_MP before NOSCALE before NORMAL before the
    /// clamped range. SendPropFloat ORs NOSCALE into a width of 0 or less, so a
    /// COORD float can carry both, and reading NOSCALE first would price it at 32.
    /// </summary>
    public static int FloatWireBits(int bits, TeFlags flags)
    {
        if ((flags & TeFlags.Coord) != 0)
            return 3 + CoordIntegerBits + CoordFractionalBits;

        if ((flags & TeFlags.CoordMp) != 0)
            return 3 + CoordIntegerBits + CoordFractionalBits;

        if ((flags & TeFlags.CoordMpLowPrecision) != 0)
            return 3 + CoordIntegerBits + CoordFractionalBitsMpLowPrecision;

        if ((flags & TeFlags.CoordMpIntegral) != 0)
            return 3 + CoordIntegerBits;

        if ((flags & TeFlags.NoScale) != 0)
            return 32;

        if ((flags & TeFlags.Normal) != 0)
            return 1 + NormalFractionalBits;

        return bits > 0 && bits <= 32 ? bits : 32;
    }

    /// <summary>A vector's worst case: a normal sends x, y and one sign bit for z.</summary>
    public static int VectorWireBits(int bits, TeFlags flags)
    {
        int per = FloatWireBits(bits, flags);

        return (flags & TeFlags.Normal) != 0 ? 2 * per + 1 : 3 * per;
    }

    /// <summary>
    /// How many props one field becomes: N for an expanded field, one for most,
    /// one for an array (its pair is two props, but only one is encoded) and for
    /// a nested table.
    /// </summary>
    public static int FieldPropCount(TeField? field)
    {
        if (field is null)
            return 0;

        if (field.Kind is TePropKind.Array or TePropKind.DataTable)
            return 1;

        return field.Elems > 1 ? field.Elems : 1;
    }

    /// <summary>
    /// What one of a field's props costs in value bits, worst case; -1 for a
    /// shape this cannot price. A nested table costs nothing itself.
    /// </summary>
    public static int PropValueBits(TeField? field)
    {
        if (field is null)
            return -1;

        int bits = ResolveBits(field.BitsFrom, field.Bits);

        switch (field.Kind)
        {
            case TePropKind.Int:
                return bits > 0 && bits <= 32 ? bits : -1;

            case TePropKind.Float:
            case TePropKind.Angle:
                return FloatWireBits(bits, field.Flags);

            case TePropKind.Vector:
            case TePropKind.QAngles:
                return VectorWireBits(bits, field.Flags);

            case TePropKind.DataTable:
                return 0;

            case TePropKind.Array:
                if (field.Elems <= 0)
                    return -1;

                // Every element goes every time (a NULL length proxy), plus the
                // length the engine writes. The element is a vector: the one
                // array in all 44 tables is DT_TEBeamSpline's sixteen points.
                return field.Elems * VectorWireBits(bits, field.Flags) + BitsFor(field.Elems);
        }

        return -1;
    }

    /// <summary>The whole field's value bits; -1 propagates.</summary>
    public static int FieldValueBits(TeField? field)
    {
        int one = PropValueBits(field);

        return one < 0 ? -1 : one * FieldPropCount(field);
    }

    /// <summary>
    /// How many blob bytes a field occupies, from its kind and declared storage;
    /// -1 for storage that disagrees with the kind, or a stride that would make
    /// two props overlap.
    /// </summary>
    public static int FieldBlobBytes(TeField? field)
    {
        if (field is null)
            return -1;

        int one;

        switch (field.Kind)
        {
            case TePropKind.Int:
                one = field.SizeBytes is 1 or 2 or 4 ? field.SizeBytes : -1;
                break;

            case TePropKind.Float:
            case TePropKind.Angle:
                one = field.SizeBytes == 4 ? 4 : -1;
                break;

            case TePropKind.Vector:
            case TePropKind.QAngles:
                one = field.SizeBytes == 12 ? 12 : -1;
                break;

            case TePropKind.DataTable:
                return 0;

            case TePropKind.Array:
                return field.Elems > 0 && field.ElemStride > 0 ? field.Elems * field.ElemStride : -1;

            default:
                one = -1;
                break;
        }

        if (one < 0)
            return -1;

        int props = FieldPropCount(field);

        if (props == 1)
            return one;

        if (field.ElemStride < one)
            return -1;

        return (props - 1) * field.ElemStride + one;
    }

    /// <summary>
    /// <c>"m_vecOrigin"</c> or <c>"m_vecOrigin[1]"</c>, truncated to what a
    /// <see cref="PropNameMax"/>-byte buffer holds. A negative element is
    /// written as 0.
    /// </summary>
    public static string PropName(string? name, int elem, bool expanded)
    {
        if (name is null)
            return string.Empty;

        const int room = PropNameMax - 1;
        string text = name.Length > room ? name[..room] : name;

        if (!expanded || text.Length >= room)
            return text;

        string full = text + "[" + (elem < 0 ? 0 : elem) + "]";

        return full.Length > room ? full[..room] : full;
    }

    /// <summary>
    /// The name the RECV side declares a planned prop under: the send name for
    /// every prop except an array's element template, which stock's RECVINFO
    /// spells <c>m_vecPoints[0]</c> where SENDINFO_ARRAY spells <c>m_vecPoints</c>.
    /// </summary>
    public static string RecvPropName(in TePropPlan plan) => PropName(plan.Name, 0, plan.ArrayElement);

    /// <summary>
    /// How many props a description becomes, counted rather than guessed; -1 for
    /// no description.
    /// </summary>
    public static int PlanCount(TeClass? declaration)
    {
        if (declaration is null)
            return -1;

        int wanted = string.IsNullOrEmpty(declaration.BaseTable) ? 0 : 1;

        foreach (TeField field in declaration.Fields)
            wanted += field.Kind == TePropKind.Array ? 2 : FieldPropCount(field);

        return wanted;
    }

    /// <summary>
    /// Every prop of one description, in the order both sides emit them; null
    /// for a description that cannot be built.
    ///
    /// <para>
    /// THE BASE GOES FIRST, where BEGIN_SEND_TABLE puts it. AN ARRAY IS TWO PROPS
    /// OF ONE NAME: the element template, then the DPT_Array; the engine links
    /// the second to the first. A VECTOR_ELEM field is N props whose send offsets
    /// are negated, so SendTable_InitTable sets SPROP_IS_A_VECTOR_ELEM; the flag
    /// itself is an instruction to negate and never passed on.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TePropPlan>? Plan(TeClass? declaration)
    {
        if (declaration is null)
            return null;

        var props = new List<TePropPlan>(Math.Max(PlanCount(declaration), 0));

        if (!string.IsNullOrEmpty(declaration.BaseTable))
        {
            props.Add(new TePropPlan(
                Name: PropName(BaseClassProp, 0, false), Kind: TePropKind.DataTable,
                Offset: 0, DeclareOffset: 0, SizeBytes: 0, Bits: 0, Flags: TeFlags.None,
                Low: 0f, High: 0f, Elems: 0, ElemStride: 0, SubTable: declaration.BaseTable,
                ArrayElement: false, Field: -1, Elem: 0));
        }

        for (int f = 0; f < declaration.Fields.Length; f++)
        {
            TeField field = declaration.Fields[f];

            if (string.IsNullOrEmpty(field.Name))
                return null;

            if (field.Kind == TePropKind.DataTable)
            {
                if (string.IsNullOrEmpty(field.SubTable))
                    return null;

                props.Add(new TePropPlan(
                    Name: PropName(field.Name, 0, false), Kind: TePropKind.DataTable,
                    Offset: field.Offset, DeclareOffset: field.Offset, SizeBytes: 0, Bits: 0,
                    Flags: TeFlags.None, Low: 0f, High: 0f, Elems: 0, ElemStride: 0,
                    SubTable: field.SubTable, ArrayElement: false, Field: f, Elem: 0));
                continue;
            }

            int bits = ResolveBits(field.BitsFrom, field.Bits);

            // A symbol this build does not know is refused rather than guessed.
            if (bits < 0 && field.BitsFrom != TeBits.Literal)
                return null;

            TeFlags flags = field.Flags & ~TeFlags.VectorElem;
            bool negate = (field.Flags & TeFlags.VectorElem) != 0;
            int stride = field.ElemStride > 0 ? field.ElemStride : 0;

            if (field.Kind == TePropKind.Array)
            {
                if (field.Elems <= 0 || field.ElemStride <= 0)
                    return null;

                props.Add(new TePropPlan(
                    Name: PropName(field.Name, 0, false), Kind: TePropKind.Vector,
                    Offset: field.Offset, DeclareOffset: negate ? -field.Offset : field.Offset,
                    SizeBytes: field.SizeBytes, Bits: bits, Flags: flags,
                    Low: field.Low, High: field.High, Elems: 0, ElemStride: 0, SubTable: null,
                    ArrayElement: true, Field: f, Elem: 0));

                props.Add(new TePropPlan(
                    Name: PropName(field.Name, 0, false), Kind: TePropKind.Array,
                    Offset: field.Offset, DeclareOffset: field.Offset, SizeBytes: 0, Bits: 0,
                    Flags: TeFlags.None, Low: 0f, High: 0f, Elems: field.Elems,
                    ElemStride: field.ElemStride, SubTable: null, ArrayElement: false,
                    Field: f, Elem: 0));
                continue;
            }

            int count = FieldPropCount(field);
            bool expand = count > 1;

            for (int e = 0; e < count; e++)
            {
                int offset = field.Offset + e * stride;

                props.Add(new TePropPlan(
                    Name: PropName(field.Name, e, expand), Kind: field.Kind,
                    Offset: offset, DeclareOffset: negate ? -offset : offset,
                    SizeBytes: field.SizeBytes, Bits: bits, Flags: flags,
                    Low: field.Low, High: field.High, Elems: 0, ElemStride: 0, SubTable: null,
                    ArrayElement: false, Field: f, Elem: e));
            }
        }

        return props;
    }
}
