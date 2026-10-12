using System.Text;

namespace SourceSharp.Abi;

/// <summary>One console line, with the number the side's shared counter gave it.</summary>
/// <param name="Sequence">
/// The side's console sequence (<c>console_line_buffer.h</c>): native and
/// managed prints take it from the same uint32. Compare two with
/// <see cref="NativeConsoleBuffer.After"/>, never with <c>&lt;</c>.
/// </param>
public readonly record struct ConsoleLineEntry(uint Sequence, string Text, bool IsWarning);

/// <summary>
/// The managed reader of a side's native console line buffer
/// (<c>src/shared/native/console_line_buffer.h</c>, console-batch): the memory
/// the forwarder appends the engine's prints to, handed over once per load by
/// <c>ConsoleBufferAttach</c>. Reading it costs no crossing.
///
/// <para>
/// <b>The lock is held for the swap only.</b> <see cref="Drain"/> takes the
/// header's spinlock, swaps the halves, takes the counts (and, when asked, the
/// next sequence number), releases, and only then parses the half it took --
/// so a print on any thread never waits for managed work.
/// </para>
///
/// <para>
/// <b>One consumer.</b> The half a drain took is the drainer's until the next
/// swap, so two drains must not run at once; <see cref="ConsoleLines"/>
/// serialises them.
/// </para>
/// </summary>
public sealed unsafe class NativeConsoleBuffer
{
    // The layout console_line_buffer.h declares; console_line_buffer_test and
    // NativeConsoleBufferTests pin both sides to these literals.
    public const int SeqOffset = 0;
    public const int OverflowOffset = 4;
    public const int LockOffset = 8;
    public const int ActiveOffset = 12;
    public const int UsedOffset = 16;
    public const int HalfBytesOffset = 24;
    public const int HeaderBytes = 32;
    public const int RecordBytes = 12;
    public const int RecordSeqOffset = 0;
    public const int RecordLengthOffset = 4;
    public const int RecordWarningOffset = 8;

    private readonly byte* _base;

    public NativeConsoleBuffer(nint buffer)
    {
        if (buffer == 0)
            throw new ArgumentNullException(nameof(buffer));
        _base = (byte*)buffer;
    }

    /// <summary>The buffer's address, for the binding to tell a re-attach of the same memory.</summary>
    public nint Address => (nint)_base;

    /// <summary>The last number taken on this side.</summary>
    public uint Sequence => Volatile.Read(ref *(uint*)(_base + SeqOffset));

    /// <summary>Whether <paramref name="a"/> comes after <paramref name="b"/>, across the uint32 wrap.</summary>
    public static bool After(uint a, uint b) => (int)(a - b) > 0;

    /// <summary>Takes the next number, as a managed print does, under the producers' lock.</summary>
    public uint TakeSequence()
    {
        Lock();
        uint seq = ++*(uint*)(_base + SeqOffset);
        Unlock();
        return seq;
    }

    /// <summary>
    /// Swaps the halves and appends what the producers wrote since the last
    /// drain to <paramref name="into"/>, in number order. Answers how many
    /// lines did not fit since then. With <paramref name="takeSequence"/>,
    /// also takes the next number in the same hold, so a managed print's
    /// number falls after every drained line and before every later one.
    /// </summary>
    public uint Drain(List<ConsoleLineEntry> into, bool takeSequence, out uint sequence)
    {
        ArgumentNullException.ThrowIfNull(into);

        byte* half;
        uint used;
        uint overflow;

        Lock();
        try
        {
            int active = *(int*)(_base + ActiveOffset);
            uint halfBytes = *(uint*)(_base + HalfBytesOffset);
            uint* usedArray = (uint*)(_base + UsedOffset);
            half = _base + HeaderBytes + (long)active * halfBytes;
            used = usedArray[active];
            overflow = *(uint*)(_base + OverflowOffset);
            *(int*)(_base + ActiveOffset) = active ^ 1;
            usedArray[active ^ 1] = 0;
            *(uint*)(_base + OverflowOffset) = 0;
            sequence = takeSequence ? ++*(uint*)(_base + SeqOffset) : 0;
        }
        finally
        {
            Unlock();
        }

        // Outside the lock: producers now write the other half.
        uint at = 0;
        while (at < used)
        {
            byte* rec = half + at;
            uint seq = *(uint*)(rec + RecordSeqOffset);
            uint length = *(uint*)(rec + RecordLengthOffset);
            bool warning = *(uint*)(rec + RecordWarningOffset) != 0;
            string text = Encoding.UTF8.GetString(rec + RecordBytes, (int)length);
            into.Add(new ConsoleLineEntry(seq, text, warning));
            at += (RecordBytes + length + 3u) & ~3u;
        }

        return overflow;
    }

    private void Lock()
    {
        ref int gate = ref *(int*)(_base + LockOffset);
        var spin = new SpinWait();
        while (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
            spin.SpinOnce();
    }

    private void Unlock() => Volatile.Write(ref *(int*)(_base + LockOffset), 0);
}
