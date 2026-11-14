using System.Diagnostics;
using System.Globalization;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// How many flights a second one core simulates on ss_ai_tf_jump (plan_bots_teams_tf2_part_g G.5
/// step 3; the candidate search of step 4 is sized by it). The flight is the recording's first:
/// its commands from the takeoff state to 20 ticks after the landing, a blast in it. The number
/// is written for <c>tools/ai-tf-jump sim</c>, with the build it was measured in.
///
/// <para>
/// The flights are counted against the thread's own CPU time where there is one (Linux), as the
/// AI's other cost facts count, and each stretch (the warming, the count) ends when the thread has
/// had its CPU time or the wall clock's bound is up, whichever is first: `make test` runs every
/// leg at once, and by the wall clock alone this fact measured 48 flights a second there where by
/// itself it measures 830. The fact holds only a floor far under the debug build's speed: a
/// simulator that slow has gone wrong, whatever else the box runs.
/// </para>
/// </summary>
public class BlastJumpSpeedTests
{
    /// <summary>Flights a CPU second under which the fact fails: a sixteenth of the debug build's.</summary>
    internal const double FloorFlightsASecond = 50d;

    /// <summary>The thread's CPU seconds the simulator flies before the count starts (the runtime compiles hot code again), and is counted for.</summary>
    private const double WarmSeconds = 0.3d, MeasureSeconds = 0.5d;

    /// <summary>The wall clock's bound on each of the two stretches, for a thread that gets little of a core.</summary>
    private const double WallBoundSeconds = 0.9d;

#if DEBUG
    private const string Build = "debug";
#else
    private const string Build = "release";
#endif

    [JumpMapFact]
    public void FlightsASecondAreMeasuredAndWrittenDown()
    {
        RecordedFlight flight = JumpRecording.Flights()[0];
        BlastJumpSim sim = JumpRig.Sim(JumpRecording.Columns());

        // The control: the flight that is timed is the recorded one, a blast in it.
        JumpFlight flown = sim.Fly(flight.Start, flight.Commands);
        Assert.True(flown.Landed && flown.Blasts == 1 && MathF.Abs(flown.Apex - flight.Apex) <= BlastJumpDifferentialTests.ApexTolerance,
                    $"the timed flight: apex {flown.Apex}, recorded {flight.Apex}, {flown.Blasts} blasts");
        Func<double>? cpu = ThreadCpuClock.Micros();
        Func<double> micros = cpu ?? (static () => Stopwatch.GetTimestamp() * 1e6 / Stopwatch.Frequency);
        var wall = Stopwatch.StartNew();
        double start = micros();
        while (micros() - start < WarmSeconds * 1e6 && wall.Elapsed.TotalSeconds < WallBoundSeconds)
            sim.Fly(flight.Start, flight.Commands);

        int flights = 0;
        wall.Restart();
        start = micros();
        do
        {
            sim.Fly(flight.Start, flight.Commands);
            flights++;
        }
        while (micros() - start < MeasureSeconds * 1e6 && wall.Elapsed.TotalSeconds < WallBoundSeconds);

        double seconds = (micros() - start) / 1e6, perSecond = flights / seconds;
        string json = string.Create(CultureInfo.InvariantCulture,
            $"{{\n \"map\": \"{JumpRecording.Map}\",\n \"build\": \"{Build}\",\n \"clock\": \"{(cpu is null ? "wall" : "thread cpu")}\",\n \"threads\": 1,\n \"commands_a_flight\": {flight.Commands.Length},\n \"flights\": {flights},\n \"seconds\": {seconds:F3},\n \"flights_a_second\": {perSecond:F0},\n \"microseconds_a_command\": {seconds * 1e6 / (flights * (double)flight.Commands.Length):F2}\n}}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(BlastJumpDifferentialTests.ReportPath("speed.json"))!);
        File.WriteAllText(BlastJumpDifferentialTests.ReportPath("speed.json"), json);
        Assert.True(perSecond >= FloorFlightsASecond, json);
    }
}
