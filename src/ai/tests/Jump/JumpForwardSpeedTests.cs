using System.Globalization;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// Whether a blast jump along ss_ai_tf_jump's flat run is faster than walking it
/// (plan_bots_teams_tf2_part_g G.5 step 1's question; the main plan's C.3.2 asks for under 70 % of
/// the walk's time). The stretch is the jump's own: from the west end of G.13's stretch along the
/// run lane's middle (x = 500 to 1,700), <see cref="ReachShare"/> of the range the simulator gives
/// a blast jump in open air today (<see cref="JumpEnvelope"/>), and no further than G.13's east
/// end. The search is asked for a jump from one end of it to the other, and its answer is timed
/// against a walk the simulator walks over the same stretch.
///
/// <para>
/// Whether the jump is the faster depends on the blast's physics, so the fact records that and
/// does not assert it: it writes <c>bin/&lt;config&gt;/ai-jump/forward.json</c>. What it holds is
/// what any physics owes: a stretch inside the jump's reach has a jump, the recorded recipe flown
/// again lands where it says, and the walk is the walk (the stretch at the class's run speed).
/// <c>make ai-test FILTER='FullyQualifiedName~JumpForwardSpeedTests'</c> measures it again.
/// </para>
/// </summary>
public class JumpForwardSpeedTests
{
    /// <summary>G.13's stretch along the run lane's middle, on the floor: the flat a jump may be timed over.</summary>
    internal const float LaneFrom = 500f, LaneTo = 1700f;

    /// <summary>
    /// The share of the jump's range the stretch is. The range is where an unbraked flight touches; a body asked to
    /// stop on a landing brakes over it and slides to rest, and a tenth of the range is left it for that.
    /// </summary>
    internal const float ReachShare = 0.9f;

    /// <summary>The share of the walk's time the main plan's C.3.2 asks a jumped route to be under.</summary>
    internal const float TargetShare = 0.7f;

    /// <summary>The run lane's middle and the floor's top, as the map's generator has them (TFAiTestMaps.RunWest.Y, FloorZ).</summary>
    internal const float RunY = 232f, FloorZ = 16f;

    internal static Vector At(float x) => new(x, RunY, FloorZ + VoxelMoveWorld.DistEpsilon);

    /// <summary>The stretch a jump is asked over, by the reach <paramref name="settings"/>' grids have in the simulator now.</summary>
    internal static (float From, float To, JumpEnvelope Reach) Stretch(JumpSearchSettings settings)
    {
        JumpEnvelope reach = Envelope(settings);
        return (LaneFrom, LaneFrom + MathF.Min(LaneTo - LaneFrom, ReachShare * reach.Range), reach);
    }

    /// <summary>The seconds a walk east from standing at <paramref name="from"/> takes to cross <paramref name="line"/> and then <paramref name="end"/>.</summary>
    internal static (float ToLine, float ToEnd) WalkSeconds(VoxelColumns columns, Vector from, float line, float end)
    {
        JumpPoint[] track = Walk(columns, new JumpStart(from, Vector.Zero, Vector.Zero, Ducked: false, OnGround: true), Hold(900, InForward, forward: ForwardSpeed)).Track;
        int atLine = Array.FindIndex(track, p => p.Origin.X >= line), atEnd = Array.FindIndex(track, p => p.Origin.X >= end);
        Assert.True(atLine >= 0 && atEnd > atLine, $"the walk crossed {line} at command {atLine} and {end} at {atEnd}");
        return ((atLine + 1) * TickSeconds, (atEnd + 1) * TickSeconds);
    }

    [JumpMapFact]
    public void TheWalkOverTheStretchTakesItsLengthAtTheRunSpeed()
    {
        (float from, float to, JumpEnvelope reach) = Stretch(new JumpSearchSettings());
        Assert.True(to > from + RunSpeed * TickSeconds * 10f, $"the control: a stretch to walk, {to - from} u of a reach of {reach.Range}");

        // From 160 u before the stretch, so the walk is at its speed when it crosses the first line.
        (float toLine, float toEnd) = WalkSeconds(JumpRecording.Columns(), At(from - 160f), from, to);
        Assert.Equal((to - from) / RunSpeed, toEnd - toLine, 1);
        // The graph's walk between the same places is the same walk.
        Assert.Equal((to - from) / RunSpeed, GraphJumpWalk.Of(JumpRecording.Graph(), RunSpeed).Seconds(At(from), At(to))!.Value, 1);
    }

    [JumpMapFact]
    public void AJumpOverTheStretchIsFoundTimedAgainstTheWalkAndWrittenDown()
    {
        VoxelColumns columns = JumpRecording.Columns();
        var settings = new JumpSearchSettings();
        (float from, float to, JumpEnvelope reach) = Stretch(settings);
        JumpSearch search = Search(columns, settings, GraphJumpWalk.Of(JumpRecording.Graph(), RunSpeed));
        var ask = new JumpAsk(At(from), At(to), 24f);
        JumpSearchResult result = search.Find(ask);

        var json = new StringBuilder("{\n");
        json.Append(CultureInfo.InvariantCulture, $" \"map\": \"{JumpRecording.Map}\",\n \"config\": \"{ServerNav.PerTreeNav.Config}\",\n \"reach_range\": {reach.Range:F1},\n \"reach_share\": {ReachShare},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"stretch_from\": {from:F1},\n \"stretch_units\": {to - from:F1},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"target_share_of_walk\": {TargetShare},\n \"search_flights\": {result.Flights},\n \"found\": {(result.Found is null ? "false" : "true")}");
        if (result.Found is { } found)
        {
            // Flown again it is the jump recorded: it lands on the stretch's far end.
            JumpOutcome again = search.Fly(found.Recipe);
            Assert.True(search.Made(ask, again), $"the recorded recipe rests at {again.Rest}");
            Assert.Equal((found.Seconds, found.AirSeconds, found.Health), (again.Seconds, again.AirSeconds, again.BlastDamage));

            // The walk from where the jump's own run starts: over the stretch alone, and from standing at that start.
            Vector start = found.Recipe.Start().Origin;
            (float toLine, float toEnd) = start.X < from - 1f ? WalkSeconds(columns, start, from, to) : (0f, WalkSeconds(columns, start, start.X + 1f, to).ToEnd);
            float walkStretch = start.X < from - 1f ? toEnd - toLine : (to - from) / RunSpeed;
            float overStretch = found.AirSeconds / walkStretch, fromStart = found.Seconds / toEnd;
            json.Append(CultureInfo.InvariantCulture, $",\n \"family\": \"{found.Family}\",\n \"run_up\": {found.Recipe.RunUp},\n \"pitch\": {found.Recipe.Pitch},\n \"yaw_offset\": {found.Recipe.YawOffset},\n \"shot_delay\": {found.Recipe.ShotDelay},\n \"ducked\": {(found.Recipe.Ducked ? "true" : "false")},\n");
            json.Append(CultureInfo.InvariantCulture, $" \"robustness\": {found.Robustness:F2},\n \"worth_keeping\": {(found.Robustness >= settings.Keep ? "true" : "false")},\n \"health\": {found.Health:F1},\n \"rest_from_landing\": {found.Miss:F1},\n");
            json.Append(CultureInfo.InvariantCulture, $" \"jump_air_seconds\": {found.AirSeconds:F3},\n \"walk_seconds_over_the_stretch\": {walkStretch:F3},\n \"jump_over_walk_on_the_stretch\": {overStretch:F3},\n");
            json.Append(CultureInfo.InvariantCulture, $" \"jump_seconds_from_its_start\": {found.Seconds:F3},\n \"walk_seconds_from_the_same_start\": {toEnd:F3},\n \"jump_over_walk_from_the_start\": {fromStart:F3},\n");
            json.Append(CultureInfo.InvariantCulture, $" \"walk_seconds_by_the_graph\": {found.WalkSeconds?.ToString("F3", CultureInfo.InvariantCulture) ?? "null"},\n");
            json.Append(CultureInfo.InvariantCulture, $" \"under_target_on_the_stretch\": {(overStretch < TargetShare ? "true" : "false")},\n \"under_target_from_the_start\": {(fromStart < TargetShare ? "true" : "false")}");
        }

        json.Append("\n}\n");
        string path = BlastJumpDifferentialTests.ReportPath("forward.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToString());
        Assert.InRange(result.Flights, 1, settings.MaxFlights);

        // The stretch is inside what the jump reaches, so there is a jump over it: the question has its answer, written down.
        Assert.True(result.Found is not null, $"no jump found over {to - from} u, {ReachShare} of a reach of {reach.Range}: {json}");
        using var read = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(read.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(result.Found.Value.AirSeconds, read.RootElement.GetProperty("jump_air_seconds").GetSingle(), 3);
    }
}
