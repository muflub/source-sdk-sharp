using System.Globalization;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Tests;
using TF2.Shared;
using TF2.Shared.Movement;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The simulator against the game (plan_bots_teams_tf2_part_g G.5 step 3): every flight the
/// Soldier bot flew on ss_ai_tf_jump while <c>tools/ai-tf-jump record</c> recorded it is flown
/// again, from its recorded takeoff state (the last frame on the ground) with its own commands,
/// through this tree's nav of the map, and the gap to the recorded flight is held: the apex, the
/// air time, the landing place.
///
/// <para>
/// The tolerances are from the first run (2026-11-10, 13 flights): the worst apex gap was
/// under 0.01 u, the air time the same tick count in every flight, the worst landing gap under
/// 0.01 u; the recording made again under TF2's own hull (2026-11-11, 13 flights) is held to
/// the same. Every recorded blast finds the bot ducked and off the ground, so a standing or a
/// grounded shooter's blast is held by BlastJumpSimTests alone. The map is built on the nav's 16 u grid and the bot flew in its open lane, so this
/// holds the movement, the rocket and the blast, and says nothing yet of a map whose walls
/// stand inside a voxel (VoxelColumns states that limit; TheMapsLedges... shows it on this map).
/// <c>tools/ai-tf-jump sim</c> prints the numbers these facts write.
/// </para>
/// </summary>
public class BlastJumpDifferentialTests
{
    /// <summary>How far a replayed flight's apex and landing place may be from the recorded one's, in units.</summary>
    internal const float ApexTolerance = 0.1f, LandingTolerance = 0.1f;

    /// <summary>How many ticks a replayed flight's air time may differ by.</summary>
    internal const int AirTicksTolerance = 0;

    /// <summary>
    /// One flight's gaps: the recorded and replayed apex and air ticks, the distance between the landing places, and the
    /// worst distance of a point of the track up to <c>through</c> (the landing, or the replay's end).
    /// </summary>
    internal sealed record Gap(int StartTick, float RecordedApex, float Apex, int RecordedAirTicks, int AirTicks, float Landing, float Track, bool Landed);

    private static float Distance(Vector a, Vector b) => (a - b).Length;

    internal static List<Gap> Gaps(BlastJumpSim sim, List<RecordedFlight> flights, bool throughTheLanding = true)
    {
        var gaps = new List<Gap>();
        foreach (RecordedFlight f in flights)
        {
            JumpFlight s = sim.Fly(f.Start, f.Commands);
            float track = Enumerable.Range(0, throughTheLanding ? f.Landing + 1 : f.Frames.Length).Max(i => Distance(s.Track[i].Origin, f.Frames[i].Origin));
            gaps.Add(new Gap(f.StartTick, f.Apex, s.Apex, f.Landing - f.Takeoff, s.Landing - s.Takeoff, Distance(s.To, f.Frames[f.Landing].Origin), track, s.Landed));
        }

        return gaps;
    }

    private static List<Gap> Gaps() => Gaps(JumpRig.Sim(JumpRecording.Columns()), JumpRecording.Flights());

    private static float Median(IEnumerable<float> values)
    {
        float[] sorted = [.. values.Order()];
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5f;
    }

    /// <summary>The speed upward a recorded blast added: the leap over its command, less the tick of gravity the same command took.</summary>
    private static Vector Leap(RecordedFlight f)
        => f.Frames[f.Blast].Velocity - f.Frames[f.Blast - 1].Velocity + new Vector(0f, 0f, TFViewVectors.Params.Gravity * TickSeconds);

    [Fact]
    public void TheRecordingHoldsTheBotsFlights()
    {
        List<RecordedFlight> flights = JumpRecording.Flights();
        Assert.True(flights.Count >= 10, $"{flights.Count} flights recorded");
        BlastJumpSim open = JumpRig.Sim(Field());
        Assert.All(flights, f =>
        {
            // On the ground at a run, then the row: jump and duck, the trigger, a blast on the way up.
            Assert.True(f.Start.OnGround && !f.Start.Ducked);
            Assert.Equal(GameMovement.InJump | GameMovement.InDuck, f.Commands[0].Buttons & (GameMovement.InJump | GameMovement.InDuck | GameMovement.InAttack));
            int trigger = Array.FindIndex(f.Commands, c => (c.Buttons & GameMovement.InAttack) != 0);
            int top = Enumerable.Range(0, f.Landing + 1).MaxBy(i => f.Frames[i].Origin.Z);
            Assert.Equal(0, f.Takeoff);
            Assert.True(trigger > 0 && f.Blast > trigger && f.Blast < top, $"the flight from tick {f.StartTick}: the trigger at {trigger}, the blast at {f.Blast}, the top at {top}");

            // And a flight no jump on foot is. Its own commands without the trigger, from its own start on an open floor, are a
            // hop in the simulator; a push upward on the way up lifts a body over that by more than the push alone would
            // from rest (its speed squared over twice the gravity), and keeps it in the air longer. No number of the
            // blast's is here but the recorded leap.
            JumpFlight hop = open.Fly(f.Start with { Origin = OnTheField() }, [.. f.Commands.Select(c => c with { Buttons = c.Buttons & ~GameMovement.InAttack })]);
            Assert.True(hop.Landed && hop.Rockets == 0 && hop.Apex > 0f, $"the control: without the trigger the flight from tick {f.StartTick} is a hop: {hop.Apex} up, {hop.Rockets} rockets");
            float push = Leap(f).Z, lift = push * push / (2f * TFViewVectors.Params.Gravity);
            Assert.True(push > 0f && f.Apex > hop.Apex + lift, $"the flight from tick {f.StartTick}: rose {f.Apex}, the hop {hop.Apex}, pushed upward at {push}");
            Assert.True(f.Landing - f.Takeoff > hop.Landing - hop.Takeoff, $"the flight from tick {f.StartTick}: {f.Landing - f.Takeoff} ticks in the air, the hop {hop.Landing - hop.Takeoff}");
        });
    }

    [Fact]
    public void TheRecordingIsOfTheMapSourceThisTreeHolds()
    {
        // The recording's provenance names the map source it was flown on: a changed map is a recording to make again
        // (tools/ai-tf-jump record), not a gap to explain.
        using var provenance = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(JumpRecording.Path, ".json")));
        string source = RepoLayout.Repo("game", "mod_tf2", "maps", "src", "ai", JumpRecording.Map + ".vmf");
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))),
                     provenance.RootElement.GetProperty("source_sha256").GetString());
        Assert.Equal(JumpRecording.Flights().Count, provenance.RootElement.GetProperty("windows").GetArrayLength());
    }

    [JumpMapFact]
    public void EveryRecordedFlightIsFlownAgainToItsApex()
        => Assert.All(Gaps(), g => Assert.True(g.Landed && MathF.Abs(g.Apex - g.RecordedApex) <= ApexTolerance,
                                               $"the flight from tick {g.StartTick}: apex {g.Apex}, recorded {g.RecordedApex}"));

    [JumpMapFact]
    public void EveryRecordedFlightIsFlownAgainForItsAirTime()
        => Assert.All(Gaps(), g => Assert.True(Math.Abs(g.AirTicks - g.RecordedAirTicks) <= AirTicksTolerance,
                                               $"the flight from tick {g.StartTick}: {g.AirTicks} ticks in the air, recorded {g.RecordedAirTicks}"));

    [JumpMapFact]
    public void EveryRecordedFlightIsFlownAgainToItsLandingPlace()
        => Assert.All(Gaps(), g => Assert.True(g.Landing <= LandingTolerance, $"the flight from tick {g.StartTick}: landed {g.Landing} from the recorded place"));

    [JumpMapFact]
    public void TheBlastIsFeltOnTheCommandTheGameFeltItWithTheGamesPush()
    {
        BlastJumpSim sim = JumpRig.Sim(JumpRecording.Columns());
        Assert.All(JumpRecording.Flights(), f =>
        {
            JumpImpact blast = Assert.Single(sim.Fly(f.Start, f.Commands).Impacts);
            Assert.Equal(f.Blast, blast.Command);
            // The recorded leap in speed is the push, less the tick of gravity the same command took.
            Vector leap = Leap(f);
            Assert.True((leap - blast.Impulse).Length < 0.05f, $"the flight from tick {f.StartTick}: pushed {blast.Impulse}, the game {leap}");
        });
    }

    [JumpMapFact]
    public void FromTwentyTicksBackTheRunUpIsWalkedAsTheGameWalkedIt()
    {
        // The flights begun at the Soldier's class speed. The others were run up after a capture, in the round's win
        // bonus (1.1 of the class speed, TFMaxSpeed.TeamWinScale): the simulator's round is always a running one,
        // so those are replayed from their takeoff state only.
        float classSpeed = Classes()[TFClass.Soldier].MaxSpeed;
        List<RecordedFlight> all = JumpRecording.Flights(runUpTicks: 20);
        List<RecordedFlight> atClassSpeed = [.. all.Where(f => new Vector(f.Start.Velocity.X, f.Start.Velocity.Y, 0f).Length <= classSpeed + 1f)];
        Assert.True(atClassSpeed.Count >= 8 && atClassSpeed.Count < all.Count, $"the control: {atClassSpeed.Count} of {all.Count} flights were run up at {classSpeed}");
        Assert.All(atClassSpeed, f => Assert.Equal(20, f.Takeoff));
        // The whole replay, the 20 ticks walked on after the landing too.
        Assert.All(Gaps(JumpRig.Sim(JumpRecording.Columns()), atClassSpeed, throughTheLanding: false),
                   g => Assert.True(g.Track <= LandingTolerance, $"the flight from tick {g.StartTick}: {g.Track} off the recorded track at its worst"));
    }

    [JumpMapFact]
    public void TheMapsLedgesHaveTheirOwnTopsAndSidesAVoxelWide()
    {
        // The first yard ledge (TFAiTestMaps): 150 over the floor's 16, from x = 300 to 556 about y = 1150. Its top is in
        // no voxel boundary (166 = 10.4 voxels), and the columns have it to the unit, from the nav's floor heights. Its
        // west side is 12 units into the column from 288: the whole column is the ledge's, so the ledge is 12 units fat.
        VoxelColumns columns = JumpRecording.Columns();
        int row = columns.ColumnY(1150f);
        Assert.Equal(166f, columns.Spans(columns.ColumnX(428f), row)[0].Bottom);
        Assert.Equal(166f, columns.Spans(columns.ColumnX(301f), row)[0].Bottom);
        Assert.Equal(columns.ColumnX(301f), columns.ColumnX(289f));
        Assert.Equal(16f, columns.Spans(columns.ColumnX(287f), row)[0].Bottom);
    }

    /// <summary>Where the numbers are written for <c>tools/ai-tf-jump sim</c>: per-tree build output.</summary>
    internal static string ReportPath(string name) => RepoLayout.Repo("bin", ServerNav.PerTreeNav.Config, "ai-jump", name);

    [JumpMapFact]
    public void TheGapsAreWrittenDownForTheTool()
    {
        List<Gap> gaps = Gaps();
        float[] apex = [.. gaps.Select(g => MathF.Abs(g.Apex - g.RecordedApex))], landing = [.. gaps.Select(g => g.Landing)];
        float[] air = [.. gaps.Select(g => Math.Abs(g.AirTicks - g.RecordedAirTicks) * TickSeconds)], track = [.. gaps.Select(g => g.Track)];
        var json = new StringBuilder("{\n");
        json.Append(CultureInfo.InvariantCulture, $" \"recording\": \"src/ai/tests/recordings/{JumpRecording.Map}.airec\",\n \"config\": \"{ServerNav.PerTreeNav.Config}\",\n \"flights\": {gaps.Count},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"recorded_apex_median\": {Median(gaps.Select(g => g.RecordedApex)):F2},\n \"recorded_air_seconds_median\": {Median(gaps.Select(g => g.RecordedAirTicks * TickSeconds)):F3},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"apex_gap_median\": {Median(apex):F4},\n \"apex_gap_worst\": {apex.Max():F4},\n \"apex_tolerance\": {ApexTolerance},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"air_seconds_gap_median\": {Median(air):F4},\n \"air_seconds_gap_worst\": {air.Max():F4},\n \"air_ticks_tolerance\": {AirTicksTolerance},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"landing_gap_median\": {Median(landing):F4},\n \"landing_gap_worst\": {landing.Max():F4},\n \"landing_tolerance\": {LandingTolerance},\n");
        json.Append(CultureInfo.InvariantCulture, $" \"track_gap_median\": {Median(track):F4},\n \"track_gap_worst\": {track.Max():F4}\n}}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath("differential.json"))!);
        File.WriteAllText(ReportPath("differential.json"), json.ToString());
        Assert.True(gaps.Count >= 10 && apex.Max() <= ApexTolerance && landing.Max() <= LandingTolerance, json.ToString());
    }
}
