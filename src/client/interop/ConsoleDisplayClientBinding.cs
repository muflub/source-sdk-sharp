using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client engine's console prints, delivered in process (docs/todo.md's
/// console capture item): the mirror of the server's
/// <c>ConsoleDisplayBinding</c> for the client side's area, whose one slot left
/// is the buffer hand-over (<c>ClientConsoleBufferAttach</c>).
/// <c>ClientConsoleLine</c>, uncalled since console-batch, left in the
/// serialised ABI pass (2026-10-20).
/// </summary>
public sealed unsafe class ConsoleDisplayClientBinding : IManagedConsoleOutputClient
{
    // Named `crossings`, not `_crossings`: CrossingsAuditTests reads the
    // crossing-count call as the op's one entry, and its regex needs a word
    // boundary before `crossings.Count(` — an underscore prefix hides one.
    private readonly Crossings crossings;
    private readonly ConsoleLines _lines;

    public ConsoleDisplayClientBinding(Crossings crossings, ConsoleLines lines)
    {
        this.crossings = crossings;
        _lines = lines;
    }

    public void ClientConsoleBufferAttach(void* a0)
    {
        // A NULL buffer is not a hand-over: nothing is counted or attached.
        if (a0 is null)
            return;

        // Once per load (console-batch): the forwarder's line buffer, which
        // the side's lines drain from here on -- the engine's prints no
        // longer cross one by one. The buffer, and its sequence, outlive a
        // managed reload; the next load is handed the same memory.
        crossings.Count(CrossingOp.ClientConsoleBufferAttach);
        _lines.AttachBuffer(new NativeConsoleBuffer((nint)a0));
    }
}
