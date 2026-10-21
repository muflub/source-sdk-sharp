using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The engine console's prints, delivered in process (docs/todo.md's console
/// capture item): the native forwarder — installed on ICvar at area acquire —
/// appends each print to its line buffer, and this binding hands that buffer to
/// the side's lines once per load (<c>ConsoleBufferAttach</c>). The one-line
/// slot, <c>ConsoleLine</c>, uncalled since console-batch (2026-10-11), left in
/// the serialised ABI pass (docs/abi-retirements.md, 2026-10-20).
/// </summary>
public sealed unsafe class ConsoleDisplayBinding : IManagedConsoleOutput
{
    // Named `crossings`, not `_crossings`: CrossingsAuditTests reads the
    // crossing-count call as the op's one entry, and its regex needs a word
    // boundary before `crossings.Count(` — an underscore prefix hides one.
    private readonly Crossings crossings;
    private readonly ConsoleLines _lines;

    public ConsoleDisplayBinding(Crossings crossings, ConsoleLines lines)
    {
        this.crossings = crossings;
        _lines = lines;
    }

    public void ConsoleBufferAttach(void* a0)
    {
        // A NULL buffer is not a hand-over: nothing is counted or attached.
        if (a0 is null)
            return;

        // Once per load (console-batch): the forwarder's line buffer, which
        // the side's lines drain from here on -- the engine's prints no
        // longer cross one by one. The buffer, and its sequence, outlive a
        // managed reload; the next load is handed the same memory.
        crossings.Count(CrossingOp.ServerConsoleBufferAttach);
        _lines.AttachBuffer(new NativeConsoleBuffer((nint)a0));
    }
}
