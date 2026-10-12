//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Packs one temp entity's blob, addressing fields through the
/// <see cref="EntityLayout"/> both sides built from the same declaration.
///
/// <para>
/// NO PACKING AND NO REORDERING (plan 3.2). Nothing here is delta-encoded
/// against a previous tick — a temp entity is a one-shot event and has no
/// previous tick (2B-bis) — so a clever layout buys nothing and makes the
/// offset/field agreement harder to check by eye. That is deliberately the
/// opposite of what the generic payload does, and for exactly the reason
/// 2B-bis gives.
/// </para>
///
/// <para>
/// EVERY WRITE IS BY WIRE NAME, and a name nothing declares is a warning rather
/// than a silent no-op. A field left unwritten is a zero, which for a
/// coordinate is a plausible value at the world origin — so the blob is zeroed
/// by the caller and a misspelled name has to say so out loud or the effect
/// simply happens somewhere else.
/// </para>
/// </summary>
public readonly ref struct TempEntWriter
{
    private readonly TempEntType _type;
    private readonly Span<byte> _blob;

    /// <summary>The type's log, which a writer of it warns through (statics to zero).</summary>
    public ILog Log => _type.Log;

    public TempEntWriter(TempEntType type, Span<byte> blob)
    {
        _type = type;
        _blob = blob;

        if (blob.Length < type.BlobBytes)
        {
            _type.Log.Warning(
                $"[SourceSharp] temp entity '{type.Class.ClassName}': a {blob.Length} byte " +
                $"buffer for a {type.BlobBytes} byte blob. Every field past the end will be " +
                "refused.\n");
        }

        blob.Clear();
    }

    /// <summary>How many bytes native expects, which is what the caller should have allocated.</summary>
    public int BlobBytes => _type.BlobBytes;

    /// <summary>
    /// An integer, written at the field's DECLARED storage width — which is the
    /// width the SendProp's proxy will read back (<c>dt_send.cpp:492</c>). A
    /// four-byte store into a one-byte field is three bytes of the next field.
    /// </summary>
    public bool Int(string name, int value)
    {
        if (!Slot(name, out TeField field, out Span<byte> at))
            return false;

        switch (field.SizeBytes)
        {
            case 1: at[0] = (byte)value; return true;
            case 2: MemoryMarshal.Write(at, (short)value); return true;
            case 4: MemoryMarshal.Write(at, value); return true;
        }

        _type.Log.Warning($"[SourceSharp] temp entity field '{name}' has a {field.SizeBytes} byte " +
                    "storage width, which no SendPropInt proxy reads\n");
        return false;
    }

    /// <summary>
    /// A bool, which is one bit on the wire and one byte in the blob (plan 2B:
    /// <c>SendPropBool</c> declares <c>SendPropInt(x, 1, UNSIGNED)</c> and C#
    /// writes 0 or 1).
    /// </summary>
    public bool Bool(string name, bool value) => Int(name, value ? 1 : 0);

    public bool Float(string name, float value)
    {
        if (!Slot(name, out _, out Span<byte> at) || at.Length < sizeof(float))
            return false;

        MemoryMarshal.Write(at, value);
        return true;
    }

    /// <summary>
    /// A vector, three floats at the field's offset — whether it goes on the
    /// wire as one <c>SendPropVector</c> or as three
    /// <c>SENDINFO_VECTORELEM</c> floats. The blob does not know the difference
    /// and does not need to; that is the encoding's business.
    /// </summary>
    public bool Vector(string name, Vector value)
    {
        if (!Slot(name, out _, out Span<byte> at) || at.Length < 3 * sizeof(float))
            return false;

        MemoryMarshal.Write(at, value.X);
        MemoryMarshal.Write(at[4..], value.Y);
        MemoryMarshal.Write(at[8..], value.Z);
        return true;
    }

    /// <summary>
    /// A facing, three floats wide like a <see cref="Vector"/> and a different
    /// KIND on the wire: the engine encodes a <c>SendPropQAngles</c> through
    /// bf_write's angle path, not through three coords. The storage is the same
    /// twelve bytes, which is why this is the vector writer with another name
    /// and another argument type — the type is what carries the distinction.
    /// </summary>
    public bool QAngle(string name, QAngle value)
    {
        if (!Slot(name, out _, out Span<byte> at) || at.Length < 3 * sizeof(float))
            return false;

        MemoryMarshal.Write(at, value.Pitch);
        MemoryMarshal.Write(at[4..], value.Yaw);
        MemoryMarshal.Write(at[8..], value.Roll);
        return true;
    }

    /// <summary>
    /// One element of an array field, as a vector — <see cref="TempEntReader.VectorAt"/>'s
    /// mirror, and the writer's twin of the native
    /// <c>SourceSharpTe_SetVectorElem</c> the server's forwards already use
    /// (<c>shell_tempents.cpp</c>).
    ///
    /// <para>
    /// IT EXISTS BECAUSE <see cref="Vector"/> ADDRESSES A FIELD AND AN ARRAY IS
    /// ONE FIELD. <c>DT_TEBeamSpline</c>'s <c>m_vecPoints</c> is a single
    /// description field standing for sixteen Vectors at
    /// <c>offset + i * ElemStride</c> (plan 3.3), so <c>Vector("m_vecPoints",
    /// v)</c> writes element ZERO and silently leaves the other fifteen — which
    /// is the shape of bug that shows up as a spline whose far end is at the
    /// world origin.
    /// </para>
    ///
    /// <para>
    /// There is exactly one array in all 44 tables and its elements are
    /// vectors, which is a statement about the inventory rather than a
    /// limitation: a second array kind adds an element-kind knob to the
    /// description, and this writes through it.
    /// </para>
    /// </summary>
    public bool VectorAt(string name, int index, Vector value)
    {
        if (!_type.Fields.TryGetValue(name, out TeField? field))
        {
            _type.Log.Warning($"[SourceSharp] temp entity '{_type.Class.ClassName}' has no field " +
                        $"'{name}'; nothing written\n");
            return false;
        }

        if (index < 0 || index >= Math.Max(field.Elems, 1))
            return false;

        int stride = field.ElemStride > 0 ? field.ElemStride : 12;
        int at = field.Offset + index * stride;

        if (at < 0 || at + 12 > _blob.Length)
            return false;

        MemoryMarshal.Write(_blob[at..], value.X);
        MemoryMarshal.Write(_blob[(at + 4)..], value.Y);
        MemoryMarshal.Write(_blob[(at + 8)..], value.Z);
        return true;
    }

    /// <summary>
    /// An entity handle as stock's <c>SendPropEHandle</c> sends it. The proxy
    /// that would have computed this is gone (plan 2B), so C# writes what it
    /// would have produced.
    ///
    /// <para>
    /// <c>SendProxy_EHandleToInt</c> (<c>game/server/sendproxy.cpp:24</c>) is
    /// the whole of it, transcribed here rather than approximated:
    /// </para>
    /// <code>
    /// int iSerialNum = pHandle-&gt;GetSerialNumber() &amp; ( (1 &lt;&lt; 10) - 1 );
    /// pOut-&gt;m_Int   = pHandle-&gt;GetEntryIndex() | (iSerialNum &lt;&lt; MAX_EDICT_BITS);
    /// </code>
    /// <para>
    /// with <c>INVALID_NETWORKED_EHANDLE_VALUE</c> — all twenty-one bits set —
    /// for a handle that resolves to nothing. The entry index and the serial
    /// come out of the packed handle exactly as <c>CBaseHandle</c> stores them
    /// (<c>public/basehandle.h:137</c>): the low sixteen bits are the entry, the
    /// high sixteen the serial.
    /// </para>
    /// <para>
    /// It reads the packed value and NOT <c>EntityHandle.Index</c>, which would
    /// be an engine call per field written.
    /// </para>
    /// </summary>
    public bool EHandle(string name, EntityHandle entity)
    {
        const int EntryMask     = (1 << 16) - 1;   // ENT_ENTRY_MASK
        const int SerialShift   = 16;              // NUM_SERIAL_NUM_SHIFT_BITS
        const int SerialMask    = (1 << 10) - 1;   // NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS
        const int MaxEdictBits  = 11;              // MAX_EDICT_BITS
        const int InvalidHandle = (1 << 21) - 1;   // INVALID_NETWORKED_EHANDLE_VALUE

        if (entity == EntityHandle.None || entity.Value == uint.MaxValue)
            return Int(name, InvalidHandle);

        int entry  = (int)(entity.Value & EntryMask);
        int serial = (int)(entity.Value >> SerialShift) & SerialMask;

        return Int(name, entry | (serial << MaxEdictBits));
    }

    private bool Slot(string name, out TeField field, out Span<byte> at)
    {
        at = default;

        if (!_type.Fields.TryGetValue(name, out field!))
        {
            _type.Log.Warning($"[SourceSharp] temp entity '{_type.Class.ClassName}' has no field " +
                        $"'{name}'; nothing written\n");
            return false;
        }

        int width = Width(field);

        if (field.Offset < 0 || field.Offset + width > _blob.Length)
        {
            _type.Log.Warning($"[SourceSharp] temp entity '{_type.Class.ClassName}' field '{name}' " +
                        $"wants bytes {field.Offset}..{field.Offset + width} of a " +
                        $"{_blob.Length} byte buffer; nothing written\n");
            return false;
        }

        at = _blob.Slice(field.Offset, width);
        return true;
    }

    /// <summary>How many bytes of blob one field occupies, elements included.</summary>
    private static int Width(TeField field)
    {
        int stride = field.ElemStride > 0 ? field.ElemStride : field.SizeBytes;

        return field.Elems > 1 ? field.Elems * stride : field.SizeBytes;
    }
}
