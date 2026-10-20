using System.Diagnostics;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_trace_cost</c>: what the server's traces cost in the running game
/// (plan_server_native_v2 T4f), in release as in debug. One <c>SSCOST</c> row
/// per kind of trace (<see cref="CostMeter.Line"/>), timed around the side's
/// whole trace member, and the timer's own floor. "reset" zeroes every meter
/// after printing, so a window is read, reset, played, read.
/// </summary>
[ConCommand("cs_sv_trace_cost",
    Help = "cs_sv_trace_cost [reset]: the server's traces (line, hull, entity hull, water), timed in game: "
         + "calls, means, min/median/p95/max ns. 'reset' zeroes them after printing.")]
public sealed class TraceCostReport(IEngineConsole console, ServerTraceCost cost) : IConCommand
{
    public void Execute(string args)
    {
        long hz = Stopwatch.Frequency;
        foreach ((string name, CostMeter meter) in cost.Meters())
            console.Msg(CostMeter.Line(name, meter.Summary(hz)));
        console.Msg(CostMeter.Line("timer.empty", CostMeter.Calibrate(1000).Summary(hz)));

        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            foreach ((_, CostMeter meter) in cost.Meters())
                meter.Reset();
        }
    }
}
