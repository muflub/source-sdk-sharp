//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Reads one temp entity's blob, addressing fields through the same
/// <see cref="EntityLayout"/> <see cref="TempEntWriter"/> wrote them with.
///
/// <para>
/// IT IS THE WRITER'S MIRROR AND NOT A SECOND OPINION. Both address by wire
/// name through <see cref="TempEntType.Fields"/> — this class's own fields and
/// its base chain's — so a field the server packed at offset 12 is the field
/// the client reads at offset 12, because one <see cref="TeClass"/> produced
/// both numbers. There is nothing here for the two sides to disagree about,
/// which is the whole reason the schema is shared.
/// </para>
///
/// <para>
/// THE BLOB IS BORROWED. It is the native shell's own storage, valid only for
/// the duration of the callback, and it is deliberately NOT cleared between
/// events: the engine deltas consecutive events of one class against each other
/// and an unchanged prop is not sent at all, so the previous event's value is
/// what a delta is applied to. A reader that stored the span would be reading
/// the next spark's coordinates.
/// </para>
///
/// <para>
/// A NAME NOTHING DECLARES IS A WARNING AND A DEFAULT, never a silent zero. A
/// zero coordinate is a plausible value at the world origin, so an effect that
/// played at the wrong place would look like a bug in the effect rather than a
/// misspelled field.
/// </para>
/// </summary>
public readonly ref struct TempEntReader
{
    private readonly TempEntType _type;
    private readonly ReadOnlySpan<byte> _blob;

    /// <summary>The type's log, which a reader of it warns through (statics to zero).</summary>
    public ILog Log => _type.Log;

    public TempEntReader(TempEntType type, ReadOnlySpan<byte> blob)
    {
        _type = type;
        _blob = blob;

        if (blob.Length < type.BlobBytes)
        {
            _type.Log.Warning(
                $"[SourceSharp] temp entity '{type.Class.ClassName}': {blob.Length} byte(s) " +
                $"arrived for a {type.BlobBytes} byte blob. Every field past the end reads " +
                "its default.\n");
        }
    }

    /// <summary>The class this blob belongs to, for a player that wants to name it.</summary>
    public TempEntType Type => _type;

    /// <summary>How many bytes the declaration says the blob is.</summary>
    public int BlobBytes => _type.BlobBytes;

    /// <summary>
    /// An integer, read at the field's DECLARED storage width — which is the
    /// width the RecvProp's proxy wrote (<c>dt_recv.cpp:202</c>). Reading four
    /// bytes out of a one-byte field is three bytes of the next one.
    ///
    /// <para>
    /// SIGN FOLLOWS THE DECLARATION, not the value. A one-byte field is read as
    /// a signed byte and a two-byte one as a short, because
    /// <c>RecvProxy_Int32ToInt8</c> wrote whatever the wire carried into that
    /// width and stock's own narrow fields include a signed one —
    /// <c>DT_TEDynamicLight</c>'s exponent, the only signed 8-bit int in all
    /// 44 tables (plan Part 6, lane C).
    /// </para>
    /// </summary>
    public int Int(string name, int fallback = 0)
    {
        if (!Slot(name, out TeField field, out ReadOnlySpan<byte> at))
            return fallback;

        return field.SizeBytes switch
        {
            1 => (sbyte)at[0],
            2 => MemoryMarshal.Read<short>(at),
            4 => MemoryMarshal.Read<int>(at),
            _ => fallback,
        };
    }

    /// <summary>
    /// An unsigned integer, for the fields stock declares <c>SPROP_UNSIGNED</c>
    /// and reads back as a count or an index rather than a quantity.
    /// </summary>
    public int UInt(string name, int fallback = 0)
    {
        if (!Slot(name, out TeField field, out ReadOnlySpan<byte> at))
            return fallback;

        return field.SizeBytes switch
        {
            1 => at[0],
            2 => MemoryMarshal.Read<ushort>(at),
            4 => MemoryMarshal.Read<int>(at),
            _ => fallback,
        };
    }

    /// <summary>
    /// A bool, which is one bit on the wire and one byte in the blob — plan 2B:
    /// <c>SendPropBool</c> declares <c>SendPropInt(x, 1, UNSIGNED)</c> and C#
    /// wrote 0 or 1.
    /// </summary>
    public bool Bool(string name, bool fallback = false)
        => Slot(name, out _, out _) ? UInt(name) != 0 : fallback;

    public float Float(string name, float fallback = 0f)
    {
        if (!Slot(name, out _, out ReadOnlySpan<byte> at) || at.Length < sizeof(float))
            return fallback;

        return MemoryMarshal.Read<float>(at);
    }

    /// <summary>
    /// A vector, three floats at the field's offset — whether it arrived as one
    /// <c>RecvPropVector</c> or as three <c>m_vecOrigin[0..2]</c> floats. The
    /// blob does not know the difference and does not need to; that is the
    /// encoding's business, and it is the reason the writer and the reader can
    /// both spell it as one call.
    /// </summary>
    public Vector Vector(string name)
    {
        if (!Slot(name, out _, out ReadOnlySpan<byte> at) || at.Length < 3 * sizeof(float))
            return Abi.Vector.Zero;

        return new Vector(
            MemoryMarshal.Read<float>(at),
            MemoryMarshal.Read<float>(at[4..]),
            MemoryMarshal.Read<float>(at[8..]));
    }

    /// <summary>
    /// A facing. Twelve bytes like a <see cref="Vector"/> and a different kind
    /// on the wire — see <c>TempEntWriter.QAngle</c>; the type is what carries
    /// the distinction rather than the table having to state it.
    /// </summary>
    public QAngle QAngle(string name)
    {
        if (!Slot(name, out _, out ReadOnlySpan<byte> at) || at.Length < 3 * sizeof(float))
            return Abi.QAngle.Zero;

        return new QAngle(
            MemoryMarshal.Read<float>(at),
            MemoryMarshal.Read<float>(at[4..]),
            MemoryMarshal.Read<float>(at[8..]));
    }

    /// <summary>
    /// One element of an array field, as a vector. There is exactly one array in
    /// all 44 tables — <c>DT_TEBeamSpline</c>'s sixteen COORD points (plan 3.3)
    /// — and its elements are vectors, which is a statement about the inventory
    /// rather than a limitation: a second array kind adds an element-kind knob
    /// to the description and this reads it.
    /// </summary>
    public Vector VectorAt(string name, int index)
    {
        if (!_type.Fields.TryGetValue(name, out TeField? field))
        {
            Missing(name);
            return Abi.Vector.Zero;
        }

        if (index < 0 || index >= Math.Max(field.Elems, 1))
            return Abi.Vector.Zero;

        int stride = field.ElemStride > 0 ? field.ElemStride : 12;
        int at = field.Offset + index * stride;

        if (at < 0 || at + 12 > _blob.Length)
            return Abi.Vector.Zero;

        return new Vector(
            MemoryMarshal.Read<float>(_blob[at..]),
            MemoryMarshal.Read<float>(_blob[(at + 4)..]),
            MemoryMarshal.Read<float>(_blob[(at + 8)..]));
    }

    /// <summary>
    /// A stock <c>SendPropEHandle</c>, undone. The proxy that packed it is gone
    /// (plan 2B) and <see cref="TempEntWriter.EHandle"/> wrote what it would
    /// have produced, so this reverses exactly that:
    /// <c>index | (serial &lt;&lt; MAX_EDICT_BITS)</c>, with all twenty-one bits
    /// set meaning "resolves to nothing".
    ///
    /// <para>
    /// Answers the ENTRY INDEX and the SERIAL separately rather than an
    /// <see cref="EntityHandle"/>, because the serial that crossed the wire is
    /// ten bits of the sixteen a handle holds — the top six are not sent — so
    /// reassembling a handle here would invent them. Whoever resolves the index
    /// against the managed entity list is the side that knows the whole serial.
    /// </para>
    /// </summary>
    public bool EHandle(string name, out int entryIndex, out int serial)
    {
        entryIndex = -1;
        serial     = 0;

        int packed = UInt(name, InvalidHandle);

        if (packed == InvalidHandle)
            return false;

        entryIndex = EntryIndexOf(packed);
        serial     = (packed >> MaxEdictBits) & SerialMask;
        return true;
    }

    private const int MaxEdictBits = 11;              // MAX_EDICT_BITS
    private const int EntryMask    = (1 << 11) - 1;
    private const int SerialMask   = (1 << 10) - 1;   // NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS

    /// <summary>
    /// <c>INVALID_NETWORKED_EHANDLE_VALUE</c> — what the wire carries for "no
    /// entity", and not zero, which is the world.
    /// </summary>
    public const int InvalidHandle = (1 << 21) - 1;

    /// <summary>
    /// The entity index out of a packed networked handle, or -1 for an invalid
    /// one.
    ///
    /// <para>
    /// PUBLIC AND STATIC because a declaration that keeps its handle as the
    /// packed int — <c>TeClientProjectile.Owner</c> is the one — needs the same
    /// decomposition, and the alternative is a second copy of these constants
    /// somewhere it can drift from
    /// <see cref="TempEntWriter.EHandle(string, EntityHandle)"/>, which is the
    /// single transcription of stock's proxy going the other way.
    /// </para>
    /// </summary>
    public static int EntryIndexOf(int packed)
        => packed == InvalidHandle ? -1 : packed & EntryMask;

    private bool Slot(string name, out TeField field, out ReadOnlySpan<byte> at)
    {
        at = default;

        if (!_type.Fields.TryGetValue(name, out field!))
        {
            Missing(name);
            return false;
        }

        int width = Width(field);

        if (field.Offset < 0 || field.Offset + width > _blob.Length)
        {
            _type.Log.Warning($"[SourceSharp] temp entity '{_type.Class.ClassName}' field '{name}' " +
                        $"wants bytes {field.Offset}..{field.Offset + width} of a " +
                        $"{_blob.Length} byte blob; read as its default\n");
            return false;
        }

        at = _blob.Slice(field.Offset, width);
        return true;
    }

    private void Missing(string name)
        => _type.Log.Warning($"[SourceSharp] temp entity '{_type.Class.ClassName}' has no field " +
                       $"'{name}'; read as its default\n");

    /// <summary>How many bytes of blob one field occupies, elements included.</summary>
    private static int Width(TeField field)
    {
        int stride = field.ElemStride > 0 ? field.ElemStride : field.SizeBytes;

        return field.Elems > 1 ? field.Elems * stride : field.SizeBytes;
    }
}
