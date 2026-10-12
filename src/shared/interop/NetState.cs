using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// Binds an entity's networked state object to the entity.
///
/// <para>
/// Put on a field of a server entity class and of its client half. The
/// registries find it, create the object if the field is null, and — on the
/// server — bind it to the entity's handle, so a property set writes straight
/// into the blob the SendProps read from. Neither side has to remember to do
/// either, which is the point: a state object nobody bound is one that
/// replicates zeroes and says nothing about why.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = true)]
public sealed class NetStateAttribute : Attribute
{
}

/// <summary>
/// A window onto one state object's storage, valid for exactly as long as the
/// expression that produced it.
///
/// <para>
/// A REF STRUCT, and that is the whole design rather than an implementation
/// detail. The bytes behind this are unmanaged memory whose lifetime is the
/// entity's, and the one fatal mistake with such a pointer is keeping it — so
/// the compiler is made to refuse. A field of this type is CS8345; so is a
/// capture, a boxing, an async local, a lambda. The revalidate-on-every-access
/// rule is therefore enforced by the type system rather than by remembering to
/// write the check, because you cannot hold a stale view: you cannot hold a view
/// at all.
/// </para>
///
/// <para>
/// It is the same answer this tree already gives to the same hazard.
/// <c>GameEventReader</c> is a ref struct because the native object it wraps is
/// valid only for the duration of a callback (see GameEvent.cs). A blob pointer
/// native may free is that hazard with a longer fuse.
/// </para>
///
/// <para>
/// Obtained only from <see cref="NetState.Blob"/>, which has already checked
/// that the storage is there and that the entity behind it is still the one this
/// state was bound to. So a view that exists is a view that is good, and
/// <see cref="IsLive"/> is here for tooling that was handed one anyway rather
/// than as a check the accessors perform.
/// </para>
/// </summary>
public readonly ref struct NetBlobView
{
    private readonly Span<byte> _bytes;
    private readonly unsafe EntitySlot* _slot;

    internal unsafe NetBlobView(byte* blob, int bytes, EntitySlot* slot)
    {
        _bytes = new Span<byte>(blob, bytes);
        _slot  = slot;
    }

    /// <summary>
    /// Whether there is storage behind this view.
    ///
    /// The predicate half of the contract: game code gets the throw, and code
    /// whose job is to survive an entity that may die mid-walk — the dev
    /// inspector, a profiler — asks instead of catching. Exceptions are for
    /// bugs, not for control flow.
    /// </summary>
    public bool IsLive => !_bytes.IsEmpty;

    /// <summary>The bytes themselves, for a caller that wants the whole image.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    public T Read<T>(int offset) where T : unmanaged
        => MemoryMarshal.Read<T>(_bytes.Slice(offset, Unsafe.SizeOf<T>()));

    /// <summary>
    /// Stores a value and then says so — unless the bytes are already what is
    /// being stored, in which case it does neither.
    ///
    /// <para>
    /// THE ORDER MATTERS AND IS NOT INCIDENTAL. The dirty word is set after the
    /// store, never before, so a write that fails leaves nothing behind — no
    /// half-updated blob and no entity queued for an encode of a change that did
    /// not happen. The stale check is earlier still, in <see cref="NetState.Blob"/>,
    /// which is why a throw cannot half-write: it happens before this view
    /// exists.
    /// </para>
    ///
    /// <para>
    /// AN UNCHANGED WRITE IS A NO-OP, and on this design that is worth real
    /// money rather than being a tidy-up. A dirty edict is the only thing that
    /// makes the engine encode an entity at all, and the hierarchy now carries a
    /// 512-word payload — so one redundant store costs a full encode and a full
    /// delta compare over two kilobytes, to send nothing. Recomputing a value
    /// that has not changed is the ordinary shape of a think function, so this
    /// is the common case and not the exotic one.
    /// </para>
    ///
    /// <para>
    /// It cannot starve a client, and that is a fact about the engine rather
    /// than a hope. An edict that reports no change does not go unsent: it
    /// reuses the previously packed entity, and falls
    /// through to a full encode when there is no previous packet to reuse. The
    /// packed image is a function of these bytes, so bytes that did not change
    /// pack to the image already cached — and a client joining now still gets a
    /// full update built from that snapshot.
    /// </para>
    ///
    /// <para>
    /// BITWISE, NEVER NUMERICALLY, because the wire carries bits. No epsilon on
    /// floats: an epsilon suppresses genuine small changes and lets the error
    /// accumulate where nothing will ever report it, which is a far worse trade
    /// than the encode it saves. <c>-0.0</c> and <c>+0.0</c> are equal
    /// numerically and different on the wire, so this treats them as a change,
    /// which is correct. And <c>NaN != NaN</c> numerically, so a numeric compare
    /// would mark every NaN write dirty forever; bitwise gives the stable
    /// answer.
    /// </para>
    /// </summary>
    public unsafe void Write<T>(int offset, T value) where T : unmanaged
    {
        Span<byte> target = _bytes.Slice(offset, Unsafe.SizeOf<T>());

        ReadOnlySpan<byte> incoming =
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1));

        if (target.SequenceEqual(incoming))
            return;

        incoming.CopyTo(target);
        _slot->Dirty = 1;
    }

    //-------------------------------------------------------------------------
    // PACKED FIELDS (plan_nettable_generic.md Part 10, Step 8).
    //
    // A field narrower than a word shares one with its neighbours, because the
    // delta unit is the dword: two fields in one word cost 39 bits between them
    // where two words cost 78. WHERE each field sits is decided once, at compile
    // time, by SourceSharp.Generators -- the only copy of the layout rules. What
    // is here is the mechanics of getting a value in and out of a placement it is
    // handed, which is worth having in one tested place rather than emitted three
    // hundred times into generated accessors.
    //
    // NO FIELD STRADDLES A WORD. The packer pads to the next word rather than
    // splitting one, so every read and write below touches exactly one word --
    // which is what makes a set one read-modify-write and one dirty mark, and
    // what stops a single narrow field costing two props on the wire.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The low <paramref name="width"/> bits set. Separate from an inline
    /// <c>(1u &lt;&lt; width) - 1</c> because C# masks a shift count to five bits, so
    /// the natural expression silently answers 0 for a full-width field instead
    /// of all ones.
    /// </summary>
    internal static uint MaskOf(int width)
        => width >= 32 ? uint.MaxValue : (1u << width) - 1u;

    /// <summary>An unsigned packed field, as the raw bits it stores.</summary>
    public uint ReadBits(int wordOffset, int shift, int width)
        => (Read<uint>(wordOffset) >> shift) & MaskOf(width);

    /// <summary>
    /// A signed packed field, SIGN EXTENDED from its declared width.
    ///
    /// <para>
    /// Its own method rather than a flag on the unsigned one, for the reason
    /// <see cref="NetFieldKind.UInt"/> is its own kind: getting the extension
    /// wrong produces a plausible negative number and no error, which is the
    /// same silent shape the wire's signed/unsigned split exists to prevent. The
    /// arithmetic is the pair of shifts the plan gives — left until the field's
    /// top bit is the word's, then arithmetic right by the whole padding.
    /// </para>
    /// </summary>
    public int ReadBitsSigned(int wordOffset, int shift, int width)
        => (int)(Read<uint>(wordOffset) << (32 - shift - width)) >> (32 - width);

    /// <summary>
    /// Stores a packed field, then marks — with the same no-op-when-unchanged
    /// rule every other write here has, because it goes through
    /// <see cref="Write{T}"/> and gets it for free.
    ///
    /// <para>
    /// MASKED, so an out-of-range value cannot bleed into the neighbour sharing
    /// the word. That masking alone would turn an overflow into a silently wrong
    /// value, which is the failure this file keeps deciding against — so the
    /// generated setter range-checks and throws BEFORE calling this, and this
    /// masks as the belt to that brace. The mask is not the diagnostic; the
    /// throw is.
    /// </para>
    /// </summary>
    public void WriteBits(int wordOffset, int shift, int width, uint value)
    {
        uint mask = MaskOf(width) << shift;
        uint word = Read<uint>(wordOffset);

        Write(wordOffset, (word & ~mask) | ((value << shift) & mask));
    }

    //-------------------------------------------------------------------------
    // COLOUR, which is the one kind whose storage is not its C# type's.
    //
    // Color32 holds four ints because a call site has ints; on the wire it is
    // one dword, R in the low byte, which is what four hand-declared byte
    // channels were already producing before the kind existed. So it cannot go
    // through Read<T>/Write<T> -- those copy sizeof(T), and sizeof(Color32) is
    // sixteen -- and it does not go through ReadBits either, because it is a
    // WHOLE word rather than a field sharing one.
    //
    // Here rather than emitted into three hundred accessors for the same reason
    // the bit mechanics are: one implementation, tested, and the byte order
    // written down exactly once.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The colour in the word at <paramref name="offset"/>.
    ///
    /// R is the low byte and A the high one, which is Source's <c>color32</c>
    /// order on a little-endian machine and is the order the hand-packed
    /// channels this replaces were written in — so the same bytes decode to the
    /// same colour before and after the conversion.
    /// </summary>
    public Color32 ReadColor32(int offset)
    {
        uint word = Read<uint>(offset);

        return new Color32((int)(word & 0xFF), (int)((word >> 8) & 0xFF),
                           (int)((word >> 16) & 0xFF), (int)((word >> 24) & 0xFF));
    }

    /// <summary>
    /// Stores a colour, and REFUSES a channel that will not fit in its eight
    /// bits rather than storing its low byte.
    ///
    /// <para>
    /// The refusal is the point. <see cref="Color32"/>'s channels are
    /// <c>int</c>, so 300 and -1 are values a call site can hold and the
    /// compiler will not stop; masking them would store 44 and 255 and read back
    /// a plausible wrong colour, which is the failure every other narrow field
    /// in this file throws for. Same message shape as the packed setters, so a
    /// colour that is out of range reads like a byte field that is.
    /// </para>
    /// </summary>
    public void WriteColor32(int offset, Color32 value,
                             string stateType = "", string field = "")
    {
        Check(value.R, "R", stateType, field);
        Check(value.G, "G", stateType, field);
        Check(value.B, "B", stateType, field);
        Check(value.A, "A", stateType, field);

        Write(offset, (uint)value.R
                    | ((uint)value.G << 8)
                    | ((uint)value.B << 16)
                    | ((uint)value.A << 24));

        // Local, so the four calls above are four constants and one cold path
        // rather than four copies of a comparison.
        static void Check(int channel, string name, string stateType, string field)
        {
            if (channel < 0 || channel > 255)
                NetFieldRange.ThrowChannel(stateType, field, name, channel);
        }
    }

    /// <summary>
    /// A UTF-8 string out of its fixed buffer. The far side finds the end by the
    /// terminator, and a buffer with none is read to its length.
    /// </summary>
    public string ReadString(int offset, int size)
    {
        ReadOnlySpan<byte> buffer = _bytes.Slice(offset, size);
        int end = buffer.IndexOf((byte)0);

        return Encoding.UTF8.GetString(end < 0 ? buffer : buffer[..end]);
    }

    /// <summary>
    /// A UTF-8 string into its fixed buffer, always terminated, and REFUSED
    /// rather than truncated when it will not fit. An unchanged string is a
    /// no-op, for the reasons on <see cref="Write{T}"/>.
    ///
    /// <para>
    /// The would-be image is built first and compared whole, rather than
    /// comparing the encoded prefix and hoping the rest matches. It has to be:
    /// this writer CLEARS the buffer before encoding, so a shorter string
    /// leaves different trailing bytes and those bytes are on the wire like any
    /// other. Comparing only the prefix would call "abcdef" -> "abc" unchanged.
    /// </para>
    ///
    /// <para>
    /// THIS HAS ALWAYS REFUSED, and it used to be documented as truncating,
    /// which is the sort of comment that outlives a reading of the code beneath
    /// it. <c>Encoding.UTF8.GetBytes(chars, bytes)</c> throws when the
    /// destination is short — it does not fill what it can — so an over-long
    /// value never reached the wire shortened. What it DID reach was an
    /// <c>ArgumentException</c> about "the output byte buffer" and "codepage
    /// 65001", raised from inside a generated property setter, naming neither
    /// the field nor the class nor the width it overran. The refusal is kept
    /// because a truncated path is a lookup that is silently the wrong one; the
    /// message is replaced, in the shape packing established for a value too
    /// wide for its declaration. <see cref="NetFieldRange.ThrowString"/>.
    /// </para>
    ///
    /// <para>
    /// <paramref name="stateType"/> and <paramref name="field"/> are what the
    /// message is FOR and are the only reason they are parameters. They default
    /// to empty so a hand-written call still compiles; every generated one
    /// passes them, and a message that cannot name the field is barely better
    /// than the one this replaces.
    /// </para>
    /// </summary>
    public unsafe void WriteString(int offset, int size, string value,
                                   string stateType = "", string field = "")
    {
        Span<byte> buffer = _bytes.Slice(offset, size);

        // On the stack for every string this tree declares; the heap path is
        // for a buffer somebody declares later that will not fit.
        byte[]? rented = size > 256 ? new byte[size] : null;
        Span<byte> next = rented ?? stackalloc byte[256];
        next = next[..size];

        next.Clear();

        // TryGetBytes rather than GetBytes, so the refusal is this file's and
        // not the encoder's. It is also the cheaper of the two ways to ask: a
        // GetByteCount pass ahead of the encode would walk every string twice
        // on the happy path to catch a case that is a declaration bug.
        if (!Encoding.UTF8.TryGetBytes(value.AsSpan(), next[..(size - 1)], out int written))
            NetFieldRange.ThrowString(stateType, field, value, size);

        next[written] = 0;

        if (buffer.SequenceEqual(next))
            return;

        next.CopyTo(buffer);
        _slot->Dirty = 1;
    }
}

/// <summary>
/// The base of a C#-declared networked state object.
///
/// <para>
/// A class marked <see cref="StandaloneNetStateAttribute"/> gets the rest of
/// itself written by the generator in SourceSharp.Generators: the layout as
/// constants and an accessor per declared property. What the author writes is
/// the shape — a partial property per value, whose C# TYPE says what it is on
/// the wire.
/// </para>
///
/// <para>
/// THERE IS NO MANAGED COPY OF THE STATE ANY MORE (plan_nettable_generic.md
/// Part 9). A state object is a pointer, a length, a control-block slot and a
/// serial; every get reads the storage and every set writes it. What that
/// deletes is a whole class of bug rather than a few fields: a backing field and
/// a blob that were supposed to hold the same value could differ, and nothing
/// anywhere compared them.
/// </para>
///
/// <para>
/// THE TWO SIDES STILL DIFFER, but in where the storage is rather than in what
/// an accessor does. On the SERVER <see cref="BindTo"/> points the state at the
/// entity's own blob — the memory the generated SendProps read — so a set IS the
/// update, and the only interop is the one call that handed the pointer over. On
/// the CLIENT <see cref="BindLocal"/> gives the state a pinned buffer of its
/// own, and <see cref="Decode"/> is a memcpy of the received bytes into it. The
/// per-field decode the generator used to write is gone with the backing fields
/// it filled.
/// </para>
///
/// <para>
/// A state that has neither THROWS, on the get and on the set alike. That is a
/// deliberate change from the quiet no-op this used to be, and the reasoning is
/// in <see cref="UnboundStateException"/>: there is nowhere for an unbound write
/// to go and nothing for an unbound read to return, and a read that answers a
/// fabricated zero is a lie the game will act on.
/// </para>
/// </summary>
public abstract unsafe class NetState
{
    /// <summary>The entity classname this state belongs to. Generated.</summary>
    public abstract string NetClassName { get; }

    /// <summary>
    /// How many bytes this state's fields occupy. Generated, from the layout the
    /// generator computed at compile time.
    ///
    /// <para>
    /// Read at bind time and nowhere else. It is what turns "the blob is shorter
    /// than the struct" from an overrun on first use into a named exception at
    /// the moment the mistake is made.
    /// </para>
    /// </summary>
    protected abstract int StateBytes { get; }

    //-------------------------------------------------------------------------
    // The binding. Four fields, and none of them is safe without the others: the
    // pointer cannot be revalidated alone, and a slot cannot say which binding
    // it belongs to without the serial that came with it.
    //-------------------------------------------------------------------------
    private byte* _blob;
    private EntitySlot* _slot;
    private uint _serial;
    private int _bytes;

    /// <summary>
    /// Keeps a local buffer alive, and nothing else. The array lives on the
    /// pinned object heap, so <see cref="_blob"/> into it is stable — but only
    /// while something roots the array, and this field is that something.
    /// </summary>
    private byte[]? _localStorage;

    /// <summary>The entity this writes through to, or None when it is local.</summary>
    public EntityHandle Handle { get; private set; } = EntityHandle.None;

    /// <summary>
    /// The side whose entity blob this writes through to, or null when it is
    /// local (plan_abi_hotreload D8: it asked the static Engine, which followed
    /// whichever side was active).
    /// </summary>
    private EngineSide? _side;

    /// <summary>True once bound to an entity's own blob. False for a local state.</summary>
    public bool IsBound => _side is { } side && side.EntityIsValid(Handle);

    /// <summary>
    /// Whether this state has storage and, if it is bound, whether the entity
    /// behind it is still the one it was bound to.
    ///
    /// <para>
    /// THE PREDICATE, for code whose job is to survive a dead entity — the dev
    /// inspector, a profiler, anything walking entities that may die mid-walk.
    /// Game code gets the exception instead, because a game rule reading a dead
    /// entity's state is a bug and not a case to handle. Never throws.
    /// </para>
    /// </summary>
    public bool IsLive => _blob != null && _slot->Serial == _serial;

    //-------------------------------------------------------------------------
    // Each local state's own slot.
    //
    // A local state has no entity, so nothing will ever invalidate it and there
    // is nothing to drain — but the accessors must not have to know that. Giving
    // every local state a slot whose serial never changes means the compare in
    // Blob() below is the SAME code on both sides: one load, one compare, no
    // branch on which kind of state this is. The dirty word it sets is written
    // and never read.
    //
    // THE STATE'S OWN, at the head of its pinned storage (statics to zero): it
    // was one static slot every local state in the process wrote its dirty word
    // into. Eight bytes a local state buys a slot nothing else can write.
    //-------------------------------------------------------------------------
    private const uint LocalSerial = 1u;

    /// <summary>Bytes ahead of a local state's blob in its storage: its own slot, 8-aligned.</summary>
    private static int LocalSlotBytes => sizeof(EntitySlot);

    //-------------------------------------------------------------------------
    // Binding.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Points this state at the entity blob <paramref name="binding"/> names,
    /// which the SERVER acquired (NetStateServerBinding: the blob bind is a
    /// server-only slot, so the shared state never asks for it; P1 run 3).
    /// Throws <see cref="BlobTooShortException"/> when the blob cannot hold
    /// this state's fields, including no blob at all (a length of zero).
    /// </summary>
    internal void BindTo(EntityBlobBinding binding, EngineSide side, EntityHandle handle)
    {
        if (binding.Pointer == null || binding.Bytes < StateBytes)
            ThrowHelper.BlobTooShort(this, handle, binding.Bytes);

        _blob         = binding.Pointer;
        _bytes        = binding.Bytes;
        _slot         = binding.Slot;
        _serial       = binding.Serial;
        _localStorage = null;
        Handle        = handle;
        _side         = side;
    }

    /// <summary>
    /// Points this state at a buffer of its own, which is what a client-side
    /// state is.
    ///
    /// <para>
    /// The client receives its entity's bytes borrowed for the length of one
    /// call, so there is no native buffer here to hold a view over. It gets one
    /// on the PINNED object heap instead: a pointer to it is stable for the life
    /// of the array, no <c>fixed</c> block is needed and the GC still owns the
    /// memory, so nothing has to be freed and a dropped state cannot leak.
    /// </para>
    ///
    /// <para>
    /// Idempotent, because the client registry attaches on create and a second
    /// call must not throw away the bytes already decoded into the first buffer.
    /// </para>
    /// </summary>
    public void BindLocal()
    {
        if (_localStorage is not null)
            return;

        // At least one byte: a state with no declared fields still needs a
        // pointer that is not null, or every access reports it unbound.
        _localStorage = GC.AllocateArray<byte>(LocalSlotBytes + Math.Max(StateBytes, 1), pinned: true);

        var slot = (EntitySlot*)Unsafe.AsPointer(ref _localStorage[0]);
        slot->Serial = LocalSerial;

        _blob   = (byte*)Unsafe.AsPointer(ref _localStorage[LocalSlotBytes]);
        _bytes  = StateBytes;
        _slot   = slot;
        _serial = LocalSerial;
        Handle  = EntityHandle.None;
        _side   = null;
    }

    /// <summary>
    /// Drops the storage, so every later access throws rather than writing
    /// through a pointer nobody is maintaining any more.
    ///
    /// <para>
    /// NOT THE RELOAD MECHANISM, though it was written to be one and Step 10 of
    /// plan_nettable_generic.md still asks for that. Calling this on every state
    /// of an unloading side needs a list of live states, and a list of live
    /// states in the ABI — which lives in the DEFAULT context — is the one thing
    /// that would pin the collectible context alive forever. That is precisely
    /// the leak <see cref="ManagedCaches"/> exists to prevent, so the reload
    /// invalidates from the other end instead: native gives every open slot a
    /// fresh serial, and every pointer the old assembly held reports itself
    /// stale on its next access without anybody having to hold the state that
    /// holds it. See <c>CSourceSharpEntitySlots::Retire</c>.
    /// </para>
    ///
    /// <para>
    /// What is left for this is the deliberate drop: a caller that has a state
    /// and wants it to stop working, which is the tests and nothing else today.
    /// An entity that DIES needs neither — native closes its slot.
    /// </para>
    /// </summary>
    public void Unbind()
    {
        _blob         = null;
        _slot         = null;
        _serial       = 0;
        _bytes        = 0;
        _localStorage = null;
        Handle        = EntityHandle.None;
        _side         = null;
    }

    /// <summary>
    /// Binds to storage the caller owns. Tests only — the whole point of the
    /// serial is that native decides when a pointer stops being good, and a test
    /// needs to be able to play native.
    /// </summary>
    internal void BindTo(EntityHandle handle, byte* blob, int bytes, EntitySlot* slot, uint serial)
    {
        if (blob == null || bytes < StateBytes)
            ThrowHelper.BlobTooShort(this, handle, bytes);

        _blob         = blob;
        _bytes        = bytes;
        _slot         = slot;
        _serial       = serial;
        _localStorage = null;
        Handle        = handle;
    }

    //-------------------------------------------------------------------------
    // The one gate every accessor goes through.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The storage, after checking there is some and that it is still this
    /// state's.
    ///
    /// <para>
    /// ONE LOAD AND ONE COMPARE, replacing a call that resolved a handle, walked
    /// the standalone registry, did up to two <c>dynamic_cast</c>s and marked
    /// the edict changed. That call happened on every property access, read or
    /// write, so writing five fields on one entity paid for it five times.
    /// </para>
    ///
    /// <para>
    /// The throws are out of line, in <see cref="ThrowHelper"/>, and that is not
    /// tidiness either: this is two accessors per field per state class, and a
    /// <c>throw</c> inlined into each pushes the method past the JIT's inlining
    /// budget on exactly the code that has to stay small. What is left here is a
    /// load, a compare and a branch not taken.
    /// </para>
    /// </summary>
    protected internal NetBlobView Blob(string field)
    {
        if (_blob == null)
            ThrowHelper.Unbound(this, field);

        if (_slot->Serial != _serial)
            ThrowHelper.Stale(this, field, _serial, _slot->Serial);

        return new NetBlobView(_blob, _bytes, _slot);
    }

    /// <summary>
    /// Copies a received image into this state's storage.
    ///
    /// <para>
    /// A MEMCPY, and it used to be a field-by-field copy the generator wrote.
    /// That is the second half of what Part 9 buys and the one it was not
    /// undertaken to find: the client's state stopped being a copy of the
    /// received bytes and became a view over them, so there is nothing left to
    /// decode field by field.
    /// </para>
    ///
    /// <para>
    /// A short image leaves the tail at its last value rather than reading past
    /// the end of what is borrowed engine memory — the same rule the per-field
    /// decode followed, for the same reason.
    /// </para>
    /// </summary>
    public void Decode(ReadOnlySpan<byte> blob)
    {
        if (_blob == null)
            ThrowHelper.Unbound(this, "Decode");

        Span<byte> destination = new(_blob, _bytes);
        int copy = Math.Min(blob.Length, destination.Length);

        blob[..copy].CopyTo(destination[..copy]);
    }

    /// <summary>
    /// Copies this state's bytes into a caller-owned buffer, answering how many
    /// were written. The allocation-free sibling of <see cref="Snapshot"/>, for
    /// the prediction ring that reads a state once per replayed command.
    /// </summary>
    public int CopyTo(Span<byte> destination)
    {
        if (!IsLive)
            return 0;

        int copy = Math.Min(_bytes, destination.Length);
        new ReadOnlySpan<byte>(_blob, copy).CopyTo(destination);
        return copy;
    }

    /// <summary>
    /// This state's bytes as they stand, for tooling. Empty rather than a throw
    /// when the state has no live storage, because the callers of this are the
    /// ones that must survive an entity dying mid-walk.
    /// </summary>
    public byte[] Snapshot()
        => IsLive ? new ReadOnlySpan<byte>(_blob, _bytes).ToArray() : Array.Empty<byte>();

    /// <summary>The entity-list entry this state was bound to, for a message.</summary>
    internal int EntryIndex => (int)(Handle.Value & 0xFFFF);

    /// <summary>The serials, for a message. Zero on the observed side means closed.</summary>
    internal uint ExpectedSerial => _serial;

    internal uint ObservedSerial => _slot == null ? 0u : _slot->Serial;

    internal int BoundBytes => _bytes;

    /// <summary>What the generator declared, reachable from the throw helper.</summary>
    internal int DeclaredBytes => StateBytes;
}

//-----------------------------------------------------------------------------
// What goes wrong, said out loud.
//
// EVERY ONE OF THESE WAS A SILENT NO-OP BEFORE, and the change is deliberate.
// Silently refusing a write is the failure shape this whole design is written
// against: it is exactly what the pane-of-glass bug was — a value correct on one
// side, wrong on the other, and nothing anywhere reporting it. Keeping that
// shape would carry it forward one layer down, as an entity whose state stops
// updating because its state object went stale, with a plausible-looking zero
// where the value should be and no line in any log.
//
// This is affordable only because the boundary already exists. Every
// [UnmanagedCallersOnly] entry point in SourceSharp.Bootstrap/Entry.cs is
// wrapped in a try/catch that prints the exception and carries on, so one of
// these cannot reach C++: it is caught at the nearest ABI hop, printed loudly,
// and the frame continues. Without that, throwing would trade a silent bug for a
// process kill; with it, it trades a silent bug for a named one.
//-----------------------------------------------------------------------------

/// <summary>
/// The state was bound, and the entity behind it is gone — or has been rebound,
/// which for a pointer holder is the same fact.
/// </summary>
public sealed class StaleEntityException : InvalidOperationException
{
    public StaleEntityException(string message) : base(message) { }
}

/// <summary>
/// The state has no storage at all: never bound to an entity, never given a
/// local buffer.
///
/// <para>
/// This used to be documented as legal — writes "are kept in managed memory and
/// simply go nowhere else", justified as "it is the client's, or a server one
/// before its entity exists". Both halves stopped being true. The client's state
/// has storage of its own now (<see cref="NetState.BindLocal"/>), and a server
/// state is bound by the registry at entity creation, which is before anything
/// can set a property on it — so a write to an unbound state was already a bug,
/// it was simply not reported as one.
/// </para>
/// </summary>
public sealed class UnboundStateException : InvalidOperationException
{
    public UnboundStateException(string message) : base(message) { }
}

/// <summary>
/// The entity's blob cannot hold this state's fields.
///
/// <para>
/// Thrown at <see cref="NetState.BindTo"/> and never at an access, which is where
/// this check used to live. It had to move: a per-write check could refuse the
/// write, but a cached pointer into a short blob is a buffer overrun on first
/// use. So a short blob has to be impossible to bind rather than survivable to
/// write through, and the message carries both sizes because the fix is always
/// one of the two numbers.
/// </para>
/// </summary>
public sealed class BlobTooShortException : InvalidOperationException
{
    public BlobTooShortException(string message) : base(message) { }
}

/// <summary>
/// A value does not fit the bit width its field declared.
///
/// <para>
/// NEW WITH PACKING, and it is the consequence of a meaning change rather than a
/// new check for its own sake. <c>[NetField(Bits = n)]</c> used to narrow the
/// WIRE only — "the storage stays the C# type's size, so a value that does not
/// fit is truncated on the wire rather than misaligning everything after it".
/// Packing makes <c>Bits</c> narrow the STORAGE too, and once a field's
/// neighbour lives in the same word, an overflowing value would bleed into it.
/// </para>
///
/// <para>
/// The setter masks, so the bleed is impossible whatever happens here. But
/// masking alone converts an overflow into a silently wrong value, which is the
/// thing this design keeps deciding against — the same argument that made a
/// stale write throw rather than fail closed. So the value is checked first and
/// the field, the value and the width are all named, because the fix is always
/// one of the three.
/// </para>
/// </summary>
public sealed class FieldRangeException : ArgumentOutOfRangeException
{
    public FieldRangeException(string message) : base(message) { }
}

/// <summary>
/// The out-of-range throw, reachable from generated code.
///
/// <para>
/// PUBLIC where <see cref="ThrowHelper"/> is internal, because this is the one
/// throw a generated accessor raises and generated accessors compile into the
/// mod's own assemblies. The other three are raised by <see cref="NetState"/>
/// itself and stay where they are.
/// </para>
///
/// <para>
/// Shaped like the BCL's own thrower for the BCL's own reason: there are two
/// accessors per declared field per state class, and an inlined <c>throw</c>
/// carries the exception construction into every one of them, pushing a method
/// that must stay a load, a compare and a branch-not-taken past the JIT's
/// inlining budget.
/// </para>
/// </summary>
public static class NetFieldRange
{
    /// <summary>
    /// The widest value a signed field of this width holds, as a pair. Exposed
    /// so a test can pin the generator's bounds against the same arithmetic
    /// rather than against numbers typed twice.
    /// </summary>
    public static (long Min, long Max) SignedRange(int bits)
        => bits >= 64 ? (long.MinValue, long.MaxValue)
                      : (-(1L << (bits - 1)), (1L << (bits - 1)) - 1);

    /// <summary>The widest value an unsigned field of this width holds.</summary>
    public static ulong UnsignedMax(int bits)
        => bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1UL;

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Throw(string stateType, string field, long value, int bits, bool signed)
    {
        var (min, max) = signed ? SignedRange(bits) : (0L, (long)UnsignedMax(bits));

        throw new FieldRangeException(
            $"{stateType}.{field}: {value} does not fit the {bits} bit(s) this field " +
            $"declares (the range is {min} to {max}). Since packing, [NetField(Bits = n)] " +
            $"narrows the STORAGE and not only the wire, and this field shares its word " +
            $"with its neighbours — so the value is refused rather than truncated into " +
            $"something plausible and wrong. Widen the declaration or clamp the value.");
    }

    /// <summary>
    /// The same refusal for a string too long for its buffer.
    ///
    /// <para>
    /// A separate thrower rather than a wider <see cref="Throw"/>, because the
    /// two say different things: a narrow int overflows into the neighbour
    /// sharing its word, and a string overruns a buffer that is its own. What
    /// they share is the reason for refusing at all — a value quietly reshaped
    /// to fit is a value the far side cannot tell from a correct one.
    /// </para>
    ///
    /// <para>
    /// The value is quoted whole. A path is the thing this fires on, the length
    /// is the whole diagnosis, and a message that said "a string" would send
    /// somebody to find out which one.
    /// </para>
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowString(string stateType, string field, string value, int bytes)
    {
        string where = string.IsNullOrEmpty(field)
            ? "a [NetString] field"
            : string.IsNullOrEmpty(stateType) ? field : $"{stateType}.{field}";

        throw new FieldRangeException(
            $"{where}: \"{value}\" encodes to {Encoding.UTF8.GetByteCount(value)} UTF-8 " +
            $"byte(s) and the field declares {bytes}, of which {bytes - 1} are usable — the " +
            $"last is the terminator. It is refused rather than truncated, because a " +
            $"shortened path is a lookup that succeeds against the wrong file or fails " +
            $"against none, and neither says so. Widen the [NetString(n)] or shorten the " +
            $"value.");
    }

    /// <summary>
    /// The same refusal for a colour channel outside 0..255.
    ///
    /// <para>
    /// Its own thrower because <see cref="Throw"/>'s advice does not apply: it
    /// says to widen the <c>[NetField(Bits = n)]</c>, and a colour channel has
    /// no bit count to widen — eight is what a channel IS. What a caller can do
    /// is clamp, so that is what this says.
    /// </para>
    ///
    /// <para>
    /// It can fire, which is the reason it exists. <see cref="Color32"/> holds
    /// <c>int</c>s so a call site can write <c>new Color32(300, 0, 0)</c> and
    /// the compiler is content; masking it would store 44 and send a plausible
    /// wrong colour.
    /// </para>
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowChannel(string stateType, string field, string channel, int value)
    {
        string where = string.IsNullOrEmpty(field)
            ? "a Color32 field"
            : string.IsNullOrEmpty(stateType) ? field : $"{stateType}.{field}";

        throw new FieldRangeException(
            $"{where}.{channel}: {value} is not a colour channel. A Color32 is four " +
            $"channels of eight bits in one word, so each is 0 to 255 — the type holds " +
            $"int because a call site does, not because a channel is wider than a byte. " +
            $"It is refused rather than masked, because a masked channel is a colour the " +
            $"far side cannot tell from the one that was meant. Clamp the value.");
    }
}

/// <summary>
/// Where the throws live, so the accessors stay small.
///
/// <para>
/// The BCL's own shape, and for the BCL's own reason. There are two accessors
/// per declared field per state class; a <c>throw</c> expression inlined into
/// each one carries the exception construction with it and pushes the method
/// past the JIT's inlining budget — on the very code that has to stay a load, a
/// compare and a branch not taken. <see cref="MethodImplOptions.NoInlining"/>
/// and <see cref="DoesNotReturnAttribute"/> together keep the cold path out of
/// the hot one while still letting the compiler see that control does not
/// return.
/// </para>
/// </summary>
internal static class ThrowHelper
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Unbound(NetState state, string field)
        => throw new UnboundStateException(
            $"{state.GetType().Name}.{field}: this state has no storage. A server state is " +
            $"bound to its entity by the registry when the entity is created; a client one " +
            $"is given a buffer of its own by BindLocal(). Neither happened, so there is " +
            $"nowhere for a write to go and nothing for a read to return.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Stale(NetState state, string field, uint expected, uint observed)
        => throw new StaleEntityException(
            $"{state.GetType().Name}.{field}: this state's binding is no longer current " +
            $"(class '{state.NetClassName}', entity {state.EntryIndex}, expected serial " +
            $"{expected}, control block says {observed}). Either the entity died and its " +
            $"slot was reused, or a managed reload retired every pointer the old assembly " +
            $"held — a serial of 0 means the slot is closed and says which. Nothing " +
            $"legitimate writes through a stale state; the blob was not touched and the " +
            $"entity was not marked changed.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void BlobTooShort(NetState state, EntityHandle handle, int bytes)
        => throw new BlobTooShortException(
            $"{state.GetType().Name} needs {state.DeclaredBytes} byte(s) and entity " +
            $"{(int)(handle.Value & 0xFFFF)} ('{state.NetClassName}') offers {bytes}. " +
            $"Binding is refused rather than allowed to overrun on the first write: the " +
            $"length check is made here, once, because a bound state holds a pointer and " +
            $"cannot make it again.");
}
