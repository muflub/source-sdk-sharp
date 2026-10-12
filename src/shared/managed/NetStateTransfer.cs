namespace SourceSharp.Abi;

/// <summary>
/// Networked state across a hot reload, carried BY NAME rather than by offset.
///
/// <para>
/// WHY THIS EXISTS AT ALL, since the blob survives. A blob is native memory and
/// a reload does not touch it, so an entity whose class did not change comes
/// back with every networked value exactly where it was and this does nothing.
/// The case it is for is the other one: a blob only means anything through the
/// LAYOUT it was written with, so the moment a class is reshaped its bytes stop
/// meaning what they say and the reload ZEROES them rather than let them be
/// misread through the new offsets. By name, a field that still exists gets its
/// value back even though it moved — which is strictly better than zero, and is
/// the whole difference between a reshape being survivable and being lossless.
/// </para>
///
/// <para>
/// RAW BITS, NOT TEXT, and that is the decision worth defending. Every other
/// half of the reload carrier is text because a map key IS text — it came from
/// the map that way and the parse that reads it back is the parse that read it
/// first. A networked field never was: it is a float, a packed seven-bit
/// integer, a fixed-width string buffer, a vector. Formatting one and reading it
/// back would round a float through decimal, would have to invent a spelling for
/// a byte array, and would put a second opinion about every type beside the one
/// the generator already computed. The bits are the value; carrying the bits is
/// carrying the value.
/// </para>
///
/// <para>
/// SO THE COMPATIBILITY TEST IS ON SHAPE, NOT ON NAME ALONE. A field is restored
/// when the new build declares one with the same name, the same
/// <see cref="NetFieldKind"/> and the same width — bit width for a packed field,
/// byte count for everything else. Anything else is DROPPED and said out loud:
/// a field that changed from an int to a float has the same name and the same
/// four bytes and means something completely different, and writing the old bits
/// into it would be the pane-of-glass failure with a fresh coat of paint.
/// </para>
/// </summary>
public static class NetStateTransfer
{
    /// <summary>
    /// The prefix a networked field's key takes in the carrier, so it cannot
    /// collide with a map key or with something an entity saved by hand.
    /// </summary>
    private const string Separator = ".";

    /// <summary>
    /// Reads every field of one state out of the blob it is bound to.
    ///
    /// <para>
    /// Silently does nothing for a state with no live storage, which is not the
    /// usual rule in this file. It is right here: this runs over every entity on
    /// the map during a reload, an entity that died earlier in the same frame is
    /// an ordinary occurrence, and a save that threw would take down the reload
    /// of everything after it.
    /// </para>
    /// </summary>
    public static void Save(NetState state, SavedEntity into)
    {
        if (!state.IsLive)
            return;

        var described = StandaloneNetStates.Of(state.GetType());
        byte[] blob = state.Snapshot();

        foreach (var field in described.Fields)
        {
            string key = Key(described.ClassName, field.Name);

            if (field.IsPacked)
            {
                // A packed field's bytes are its containing word's and are
                // shared with its neighbours, so the value is the bits and
                // nothing else.
                into.SaveNet(key, Descriptor(field) + ":" + ReadBits(state, field).ToString());
                continue;
            }

            if (field.Offset < 0 || field.Offset + field.Bytes > blob.Length)
                continue;

            into.SaveNet(key, Descriptor(field) + ":"
                              + Convert.ToHexString(blob, field.Offset, field.Bytes));
        }
    }

    /// <summary>
    /// Writes back every field the new build still declares in a compatible
    /// shape, and reports the ones it does not.
    /// </summary>
    public static void Load(NetState state, SavedEntity from, string className, string name, ILog log)
    {
        if (!state.IsLive || from.Net.Count == 0)
            return;

        var described = StandaloneNetStates.Of(state.GetType());

        foreach (var field in described.Fields)
        {
            string key = Key(described.ClassName, field.Name);

            if (!from.Net.TryGetValue(key, out string? saved))
            {
                // NEW IN THIS BUILD. Nothing was saved for it and nothing should
                // be: it comes up holding its own initialiser, which is the
                // right answer and the only one available.
                continue;
            }

            int colon = saved.IndexOf(':');

            if (colon <= 0)
                continue;

            string wasShape = saved[..colon];
            string body     = saved[(colon + 1)..];

            if (wasShape != Descriptor(field))
            {
                log.Warning($"[SourceSharp] reload: {className} '{name}' field " +
                               $"'{field.Name}' changed shape ({wasShape} -> {Descriptor(field)}); " +
                               $"it is dropped rather than reinterpreted\n");
                continue;
            }

            if (field.IsPacked)
            {
                if (uint.TryParse(body, out uint bits))
                    WriteBits(state, field, bits);

                continue;
            }

            byte[] bytes;

            try
            {
                bytes = Convert.FromHexString(body);
            }
            catch (FormatException)
            {
                continue;
            }

            WriteRaw(state, field, bytes);
        }
    }

    /// <summary>
    /// The state's own class name in front of the field's, because one entity
    /// may hold several states and two of them may declare the same field name.
    /// </summary>
    private static string Key(string className, string field) => className + Separator + field;

    /// <summary>
    /// The shape a value was saved in: the kind and the width. It is compared as
    /// a string rather than field by field so that a kind added later cannot
    /// silently pass an equality test that predates it.
    /// </summary>
    private static string Descriptor(in NetFieldLayout field)
        => field.IsPacked
            ? $"{field.Kind}/{field.Bits}b"
            : $"{field.Kind}/{field.Bytes}B";

    private static uint ReadBits(NetState state, in NetFieldLayout field)
        => state.Blob(field.Name).ReadBits(field.Offset, field.BitShift, field.Bits);

    private static void WriteBits(NetState state, in NetFieldLayout field, uint value)
        => state.Blob(field.Name).WriteBits(field.Offset, field.BitShift, field.Bits, value);

    /// <summary>
    /// Byte by byte through the ordinary write, rather than a bulk copy.
    ///
    /// Every guard the accessors have applies that way: the serial compare that
    /// says the binding is still current, the bounds check, and the rule that a
    /// write storing the bytes already there marks nothing. A memcpy past the
    /// gate would have been faster and would have been the one write path in
    /// this file nobody was checking.
    /// </summary>
    private static void WriteRaw(NetState state, in NetFieldLayout field, byte[] bytes)
    {
        int count = Math.Min(bytes.Length, field.Bytes);

        for (int i = 0; i < count; i++)
            state.Blob(field.Name).Write(field.Offset + i, bytes[i]);
    }
}
