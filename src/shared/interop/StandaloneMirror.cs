//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// One client entity's animation clock, as managed code's own record.
///
/// <para>
/// The mirror of the six members <c>C_StandaloneEntity::SetPlayingSequence</c>
/// writes. Four of them are kept here by applying that function's rules; the
/// two studio-header ones (<see cref="Duration"/>, <see cref="Looping"/>) are
/// read back from the shell after the write, because managed has no studio
/// header. See <see cref="StandaloneMirror"/>.
/// </para>
/// </summary>
public readonly struct SequenceRecord
{
    /// <summary>What a shell entity looks like before anything plays on it — the
    /// C_StandaloneEntity constructor's values, member for member.</summary>
    public static readonly SequenceRecord Fresh = new(0, 1f, 0f, 0f, false, 0);

    public SequenceRecord(int sequence, float playbackRate, float startTime,
                          float duration, bool looping, int starts, float startCycle = 0f)
    {
        StartCycle = startCycle;
        Sequence = sequence;
        PlaybackRate = playbackRate;
        StartTime = startTime;
        Duration = duration;
        Looping = looping;
        Starts = starts;
    }

    public int Sequence { get; }
    public float PlaybackRate { get; }
    public float StartTime { get; }
    public float Duration { get; }
    public bool Looping { get; }
    public int Starts { get; }

    /// <summary>The cycle the sequence started at (0, or 0.999 for one played backwards).</summary>
    public float StartCycle { get; }

    /// <summary>
    /// The same write <c>SetPlayingSequence</c> performs, on the managed copy.
    ///
    /// <para>
    /// <b>The rules are native's, restated rather than approximated,</b> and
    /// each of the three is load-bearing:
    /// </para>
    /// <list type="bullet">
    /// <item>a rate of zero becomes 1 — a caller that means "default speed"
    /// passes 0, and a rate of 0 would freeze the cycle at its first frame
    /// forever; a negative rate plays backwards from the start cycle;</item>
    /// <item>a negative sequence becomes 0 — the shell clamps rather than
    /// refusing, so a lookup that missed plays the reference pose;</item>
    /// <item><b>the same sequence, not restarted, moves ONLY the rate</b> —
    /// the clock and the restart counter are both kept. A system that re-sends
    /// what it is already playing every frame (which several do) would
    /// otherwise never get past its own first frame.</item>
    /// </list>
    ///
    /// <para>
    /// On a real change the duration and loop flag go to zero and false, which
    /// is what the shell does before it consults the model. The caller fills
    /// them from <see cref="StandaloneMirror.NoteSequenceInfo"/>; a caller that
    /// cannot reach the model leaves a duration of zero, which every reader of
    /// this record already treats as "this model has no animation".
    /// </para>
    /// </summary>
    public SequenceRecord Play(int sequence, float playbackRate, bool restart, float now, float startCycle = 0f)
    {
        if (playbackRate == 0f)
            playbackRate = 1f;

        if (sequence < 0)
            sequence = 0;

        if (sequence == Sequence && !restart)
            return new SequenceRecord(Sequence, playbackRate, StartTime, Duration, Looping, Starts, StartCycle);

        return new SequenceRecord(sequence, playbackRate, now, 0f, false, Starts + 1, startCycle);
    }

    /// <summary>This record with the model's two numbers filled in.</summary>
    public SequenceRecord WithInfo(float duration, bool looping)
        => new(Sequence, PlaybackRate, StartTime, duration, looping, Starts, StartCycle);

    /// <summary>
    /// How far through the sequence is, at <paramref name="now"/>.
    ///
    /// <para>
    /// <c>C_StandaloneEntity::SequenceCycle</c>'s arithmetic, moved rather than
    /// reinvented: a duration of zero is "no animation" and answers 0 rather
    /// than dividing; a clock that has not started stands at its start cycle;
    /// a looping sequence keeps the fraction; and a finished
    /// one-shot HOLDS ITS LAST FRAME at 0.999 rather than wrapping — a fire
    /// animation that returned to the ready pose on its own would look like a
    /// shot that never happened.
    /// </para>
    /// </summary>
    public float Cycle(float now)
    {
        if (Duration <= 0f)
            return 0f;

        float elapsed = now - StartTime;
        float cycle = StartCycle + (elapsed > 0f ? elapsed : 0f) * PlaybackRate / Duration;

        if (Looping)
            return cycle - MathF.Floor(cycle);

        if (cycle < 0f)
            return 0f;

        return MathF.Min(cycle, 0.999f);
    }
}

/// <summary>
/// What managed code knows about the client's shell entities without asking:
/// the animation clock it writes itself, and the dormancy the engine pushes.
///
/// <para>
/// <b>plan_crossings.md Phases X2 and X3, and together they are 62 % of the
/// client's boundary traffic</b> — <c>StandaloneSequenceState</c> and
/// <c>StandaloneIsDormant</c> were 51.000 crossings a client frame each (A11,
/// <c>docs/a11-frame-crossing-census.tsv</c>). Both are polls of something
/// managed either authors or is already told about, which is the design smell
/// A12 named.
/// </para>
///
/// <para>
/// <b>Push-on-change, and NOT a frame-start snapshot.</b> This tree already
/// settled that question against itself: <c>CStandalonePlayer::SyncToBlob</c>
/// pushes at every write site — "every command, every hit, every spawn" — and
/// says why in its own comment, that a HUD reading a stale health for a tick is
/// the kind of quiet wrongness the sync exists to prevent. So every field here
/// is written synchronously with the write that changes it, and the staleness
/// of this mirror is ZERO FRAMES BY CONSTRUCTION. The question a reader should
/// ask of it is not "how old is this" but "is the set of write sites closed",
/// which is a grep and is gated.
/// </para>
///
/// <para>
/// <b>The failure mode, said plainly: a missed write site diverges and then
/// answers confidently forever.</b> Three things hold it shut. The write-site
/// census is in the phase's commit (six sequence members written in the
/// constructor and <c>SetPlayingSequence</c> only; <c>m_bDormant</c> written in
/// the constructor and the two arms of <c>NotifyShouldTransmit</c> only). The
/// ABI slots are KEPT, both for ABI stability and as the reference. And
/// <c>cs_cl_mirror_check</c> walks every live entity and compares this mirror
/// against those slots — at a phase gate, not per frame in production, which
/// would put the crossings straight back.
/// </para>
///
/// <para>
/// <b>Client state, and the side gate is not decoration.</b>
/// <c>StandaloneSequenceState</c> and <c>StandaloneIsDormant</c> are both
/// client-only slots — dormancy is a client concept and the cycle is a clock
/// the two sides legitimately disagree about — while
/// <c>StandaloneSetSequence</c> is wired on both. Recording a server write here
/// would stamp it with the SERVER's curtime and then answer it back to a reader
/// that expects the client's, which is a plausible cycle rather than an error.
/// </para>
///
/// <para>
/// <b>One instance per side's composition (plan_abi_hotreload D7).</b>
/// <see cref="EngineServices"/> builds a binding per container, so state held
/// on <c>EngineEntityRender</c> itself would be one mirror per container and
/// the push would feed a different one from the read. The side's module owns
/// one of these, registers it in its container before the engine areas, and
/// hands the same instance to the render binding (through the container) and
/// to the push handlers (through their constructors). Nothing static holds
/// one: two compositions are two mirrors. A hot reload carries the contents
/// across as bytes (<see cref="Save"/>, <see cref="Restore"/>), because the
/// entities it describes outlive the module that owned it.
/// </para>
/// </summary>
public sealed class StandaloneMirror
{
    private readonly Dictionary<uint, SequenceRecord> _sequences = new();
    private readonly Dictionary<uint, bool> _dormant = new();

    /// <summary>
    /// Whether readers are answered from this mirror or fall through to the
    /// ABI slots.
    ///
    /// <para>
    /// <b>The census control, and it is why the phase's two arms are one
    /// process rather than two builds.</b> Phase X1 measured its reroute by
    /// building two variants of one commit, and the client half of that
    /// measurement was confounded: the two runs had different renderable
    /// populations, so the client's boundary TOTAL moved 22 crossings a frame
    /// for a reason X1 had not caused. Flipping this at runtime holds the
    /// population, the map, the viewpoint and the frame rate fixed and moves
    /// only the thing under test.
    /// </para>
    ///
    /// <para>
    /// Writes happen either way, so the mirror stays correct while it is
    /// switched off and is not stale when it is switched back on.
    /// </para>
    /// </summary>
    public bool Rerouted { get; set; } = true;

    /// <summary>How many entities have a sequence record. For the gate.</summary>
    public int SequenceCount => _sequences.Count;

    /// <summary>How many entities have a dormancy record. For the gate.</summary>
    public int DormancyCount => _dormant.Count;

    /// <summary>
    /// Managed just wrote the animation clock. Records the same write.
    /// </summary>
    public SequenceRecord NoteSequence(EntityHandle entity, int sequence,
                                       float playbackRate, bool restart, float now, float startCycle = 0f)
    {
        SequenceRecord before = _sequences.TryGetValue(entity.Value, out var seen)
            ? seen
            : SequenceRecord.Fresh;

        SequenceRecord after = before.Play(sequence, playbackRate, restart, now, startCycle);

        _sequences[entity.Value] = after;
        return after;
    }

    /// <summary>
    /// Fills in the two numbers that came out of the model. Ignored for an
    /// entity with no record, because a record is what a write creates.
    /// </summary>
    public void NoteSequenceInfo(EntityHandle entity, float duration, bool looping)
    {
        if (_sequences.TryGetValue(entity.Value, out var record))
            _sequences[entity.Value] = record.WithInfo(duration, looping);
    }

    /// <summary>
    /// What this entity's clock says. An entity nothing has ever played a
    /// sequence on answers <see cref="SequenceRecord.Fresh"/> — which is the
    /// shell constructor's own values, so the two agree without a crossing.
    /// </summary>
    public SequenceRecord Sequence(EntityHandle entity)
        => _sequences.TryGetValue(entity.Value, out var record) ? record : SequenceRecord.Fresh;

    /// <summary>The engine pushed a PVS transition. See the class remarks.</summary>
    public void NoteDormancy(EntityHandle entity, bool dormant)
        => _dormant[entity.Value] = dormant;

    /// <summary>
    /// Whether the engine has stopped sending this entity.
    ///
    /// <para>
    /// An entity that has never been pushed answers false, which is the shell
    /// constructor's value and the slot's answer for a handle that resolves to
    /// nothing. The creation push means a live managed entity is never in that
    /// state; the default covers the window before it.
    /// </para>
    /// </summary>
    public bool IsDormant(EntityHandle entity)
        => _dormant.TryGetValue(entity.Value, out bool dormant) && dormant;

    /// <summary>
    /// The entity has gone. Both records go with it.
    ///
    /// <para>
    /// <b>Handles are recycled</b>, so an entry left behind is not merely
    /// stale — it is inherited by whatever entity next wears the number, and a
    /// dormancy of true would make it invisible to every managed reader while
    /// the engine drew it.
    /// </para>
    /// </summary>
    public void Forget(EntityHandle entity)
    {
        _sequences.Remove(entity.Value);
        _dormant.Remove(entity.Value);
    }

    /// <summary>Everything goes. The level is going.</summary>
    public void Clear()
    {
        _sequences.Clear();
        _dormant.Clear();
    }

    /// <summary>The <see cref="ReloadStore"/> section a hot reload carries this mirror in.</summary>
    public const string ReloadSection = "standalone-mirror";

    private const uint SaveMagic = 0x3152_4D53; // "SMR1"
    private const int HeaderBytes = 4 + 1 + 4 + 4;
    private const int SequenceBytes = 4 + 4 + 4 + 4 + 4 + 1 + 4;
    private const int DormancyBytes = 4 + 1;

    /// <summary>
    /// Everything this mirror holds, as bytes for the reload carrier.
    ///
    /// <para>
    /// <b>Why a reload carries it at all.</b> The mirror belongs to the side's
    /// composition, and a hot reload builds a new one; but the entities it
    /// describes stay alive in the engine across the reload, and neither push
    /// repeats on its own. Dormancy arrives only on a CHANGE, so a dormant
    /// entity would read awake until it next woke and slept; and a sequence
    /// record is written only by <c>PlaySequence</c>, so every looping idle
    /// would read sequence 0, cycle 0 until its owner chose a new one.
    /// <see cref="Rerouted"/> goes with it: it is an operator's setting.
    /// </para>
    /// </summary>
    public byte[] Save()
    {
        var bytes = new byte[HeaderBytes + _sequences.Count * SequenceBytes + _dormant.Count * DormancyBytes];
        var span = bytes.AsSpan();
        int at = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(span[at..], SaveMagic); at += 4;
        span[at++] = Rerouted ? (byte)1 : (byte)0;

        BinaryPrimitives.WriteInt32LittleEndian(span[at..], _sequences.Count); at += 4;
        foreach (var (handle, record) in _sequences)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[at..], handle); at += 4;
            BinaryPrimitives.WriteInt32LittleEndian(span[at..], record.Sequence); at += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[at..], record.PlaybackRate); at += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[at..], record.StartTime); at += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[at..], record.Duration); at += 4;
            span[at++] = record.Looping ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(span[at..], record.Starts); at += 4;
        }

        BinaryPrimitives.WriteInt32LittleEndian(span[at..], _dormant.Count); at += 4;
        foreach (var (handle, dormant) in _dormant)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[at..], handle); at += 4;
            span[at++] = dormant ? (byte)1 : (byte)0;
        }

        return bytes;
    }

    /// <summary>
    /// Takes back what <see cref="Save"/> wrote, and answers how many records
    /// it adopted, or -1 when the bytes are not a whole save (nothing is
    /// adopted then).
    ///
    /// <para>
    /// <b>A record already here wins.</b> A push that reached the new mirror
    /// before the restore is newer than anything the old one held, so the
    /// carried record fills only the gaps.
    /// </para>
    /// </summary>
    public int Restore(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != SaveMagic)
            return -1;

        int at = 4;
        bool rerouted = bytes[at++] != 0;

        int sequences = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]); at += 4;
        if (sequences < 0 || (long)sequences * SequenceBytes > bytes.Length - at - 4)
            return -1;

        int sequenceStart = at;
        at += sequences * SequenceBytes;

        int dormancies = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]); at += 4;
        if (dormancies < 0 || (long)dormancies * DormancyBytes != bytes.Length - at)
            return -1;

        int adopted = 0;
        Rerouted = rerouted;

        for (int i = 0, p = sequenceStart; i < sequences; i++, p += SequenceBytes)
        {
            uint handle = BinaryPrimitives.ReadUInt32LittleEndian(bytes[p..]);
            var record = new SequenceRecord(
                BinaryPrimitives.ReadInt32LittleEndian(bytes[(p + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[(p + 8)..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[(p + 12)..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[(p + 16)..]),
                bytes[p + 20] != 0,
                BinaryPrimitives.ReadInt32LittleEndian(bytes[(p + 21)..]));

            if (_sequences.TryAdd(handle, record))
                adopted++;
        }

        for (int i = 0; i < dormancies; i++, at += DormancyBytes)
        {
            if (_dormant.TryAdd(BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]), bytes[at + 4] != 0))
                adopted++;
        }

        return adopted;
    }
}
