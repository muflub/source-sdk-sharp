using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// A side's native console line buffer (console_line_buffer.h) in unmanaged
/// memory, with a producer that appends exactly as CConsoleLineBuffer::Append
/// does: the engine's half of the console feed, for facts that run no engine.
/// Halves are small by default so a fact can fill one.
/// </summary>
public sealed unsafe class TestConsoleBuffer : IDisposable
{
    private readonly byte* _base;
    private readonly uint _halfBytes;

    public TestConsoleBuffer(uint halfBytes = 64 * 1024)
    {
        _halfBytes = halfBytes;
        _base = (byte*)NativeMemory.AllocZeroed((nuint)(NativeConsoleBuffer.HeaderBytes + 2 * (long)halfBytes));
        *(uint*)(_base + NativeConsoleBuffer.HalfBytesOffset) = halfBytes;
    }

    /// <summary>The address ConsoleBufferAttach hands managed.</summary>
    public nint Address => (nint)_base;

    /// <summary>The managed reader over this memory, as the binding builds it.</summary>
    public NativeConsoleBuffer Reader() => new(Address);

    /// <summary>The header's counter: the last number taken.</summary>
    public uint Sequence
    {
        get => *(uint*)(_base + NativeConsoleBuffer.SeqOffset);
        set => *(uint*)(_base + NativeConsoleBuffer.SeqOffset) = value;
    }

    /// <summary>Holds the producers' lock, as a print mid-copy does.</summary>
    public void HoldLock() => *(int*)(_base + NativeConsoleBuffer.LockOffset) = 1;

    /// <summary>Releases it.</summary>
    public void ReleaseLock() => Volatile.Write(ref *(int*)(_base + NativeConsoleBuffer.LockOffset), 0);

    /// <summary>One engine print, appended the way the native forwarder does. False when it did not fit.</summary>
    public bool Append(string text, bool warning = false)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        ref int gate = ref *(int*)(_base + NativeConsoleBuffer.LockOffset);
        var spin = new SpinWait();
        while (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
            spin.SpinOnce();
        try
        {
            uint seq = ++*(uint*)(_base + NativeConsoleBuffer.SeqOffset);
            int active = *(int*)(_base + NativeConsoleBuffer.ActiveOffset);
            uint* used = (uint*)(_base + NativeConsoleBuffer.UsedOffset);
            uint need = (uint)((NativeConsoleBuffer.RecordBytes + utf8.Length + 3) & ~3);
            if (need > _halfBytes - used[active])
            {
                ++*(uint*)(_base + NativeConsoleBuffer.OverflowOffset);
                return false;
            }

            byte* at = _base + NativeConsoleBuffer.HeaderBytes + (long)active * _halfBytes + used[active];
            *(uint*)(at + NativeConsoleBuffer.RecordSeqOffset) = seq;
            *(uint*)(at + NativeConsoleBuffer.RecordLengthOffset) = (uint)utf8.Length;
            *(uint*)(at + NativeConsoleBuffer.RecordWarningOffset) = warning ? 1u : 0u;
            utf8.AsSpan().CopyTo(new Span<byte>(at + NativeConsoleBuffer.RecordBytes, utf8.Length));
            used[active] += need;
            return true;
        }
        finally
        {
            Volatile.Write(ref gate, 0);
        }
    }

    public void Dispose() => NativeMemory.Free(_base);
}
