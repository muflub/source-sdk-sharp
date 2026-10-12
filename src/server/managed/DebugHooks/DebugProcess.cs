using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// The exit recorders, through IEngineDiagnostics: native's facts in
/// ExitWatchFact_t order, named.
/// </summary>
public sealed class DebugProcess(IEngineDiagnostics diagnostics) : IDebugProcess
{
    private readonly IEngineDiagnostics _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    // exit_watch.h's ExitWatchFact_t, and its kExitWatch* bits.
    private const int FactCount = 19;
    private const int SignalsBit = 1, SdlBit = 2;

    /// <summary>
    /// No recorders: what a server with no engine answers. Here and not on the
    /// record in SourceSharp.Server.Interface, which holds data only (SS0030).
    /// </summary>
    public static DebugExitFacts NoFacts => new(false, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public void Arm(string recordPath) => _diagnostics.ExitWatchArm(recordPath);

    public DebugExitFacts Read()
    {
        Span<int> f = stackalloc int[FactCount];
        if (_diagnostics.ExitWatchRead(f) < FactCount || f[0] != 1)
            return NoFacts;

        return FromFacts(f);
    }

    public void Disarm() => _diagnostics.ExitWatchDisarm();

    /// <summary>Native's fact array, named. Internal for the tests.</summary>
    internal static DebugExitFacts FromFacts(ReadOnlySpan<int> f)
        => new(Armed: true, SignalsArmed: (f[1] & SignalsBit) != 0, SdlArmed: (f[1] & SdlBit) != 0, NowMs: f[2],
               Signals: f[3], FirstSigno: f[4], FirstPid: f[5], FirstUid: f[6], FirstAtMs: f[7],
               LastSigno: f[8], LastPid: f[9], LastUid: f[10], LastAtMs: f[11],
               SdlQuits: f[12], SdlQuitAtMs: f[13], WindowCloses: f[14], WindowCloseAtMs: f[15],
               PrevTerm: f[16], PrevInt: f[17], PrevHup: f[18]);
}
