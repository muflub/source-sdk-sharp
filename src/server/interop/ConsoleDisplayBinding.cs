using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The engine console's prints, delivered in process (docs/todo.md's console
/// capture item): the body of <c>IManagedConsoleOutput::ConsoleLine</c>, which
/// the native forwarder — installed on ICvar at area acquire — calls
/// synchronously on the printing thread. The text is the engine's already
/// formatted line, UTF-8, consumed here and never kept; <c>nWarning</c> is the
/// decorated channel (ColorPrint), not Print/DPrint's plain one.
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

    // UNCALLED SINCE CONSOLE-BATCH (2026-10-11): native appends to its line
    // buffer instead (ConsoleBufferAttach). The slot stays until the
    // serialised ABI cleanup (docs/todo.md) renumbers once.
    public void ConsoleLine(byte* a0, int a1)
    {
        // A NULL text is not a print: nothing is counted or dispatched.
        if (a0 is null)
            return;

        // The crossing is counted, then the engine's already-formatted line —
        // NUL-terminated UTF-8 on the printing thread's stack, decoded here
        // and never kept — goes to the side's lines verbatim, warning flag
        // and trailing newline included: the same shape a managed print takes.
        crossings.Count(CrossingOp.ServerConsoleLine);
        var text = Marshal.PtrToStringUTF8((nint)a0) ?? string.Empty;
        _lines.Line(text, a1 != 0);
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
