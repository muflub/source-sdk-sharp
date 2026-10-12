using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;
using SourceSharp.Server.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables E14: the server's crossings report, as
/// DiagnosticsBinding answers it. The counters are process-wide and other tests
/// count concurrently, so these never compare two reports. Each asserts what holds of any one:
/// a sizing answer past the terminator, and a fill that terminates where it
/// says.
/// </summary>
public unsafe class DiagnosticsBindingTests
{
    private readonly DiagnosticsBinding _binding = new(new SourceSharp.Abi.Crossings(), new RecordingLog());

    [Fact]
    public void ASizingCallAnswersMoreThanTheTerminator()
    {
        int needed = _binding.CrossingsReport(null, 0, 0);

        Assert.True(needed > 1, $"a sizing call answers the report's length ({needed})");
    }

    [Fact]
    public void ANullBufferWithASizeIsASizingCall()
    {
        // Built into a Span it would be written through: native passes NULL to size.
        int needed = _binding.CrossingsReport(null, 4096, 0);

        Assert.True(needed > 1, $"a null buffer sizes rather than fills ({needed})");
    }

    [Fact]
    public void AFillTerminatesWhereItSays()
    {
        var buffer = new byte[1 << 16];
        Array.Fill(buffer, (byte)'#');

        int filled;
        fixed (byte* p = buffer)
            filled = _binding.CrossingsReport(p, buffer.Length, 0);

        Assert.True(filled > 1 && buffer[filled - 1] == 0 && buffer[0] != (byte)'#',
                    $"the report is written and NUL-terminated at its answer ({filled})");
    }
}
