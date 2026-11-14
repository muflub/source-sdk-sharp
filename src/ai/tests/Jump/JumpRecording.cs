using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A fact over this tree's built nav of the jump map: per-tree build output, so a tree without
/// it skips, naming the command that builds it.
/// </summary>
public sealed class JumpMapFactAttribute : FactAttribute
{
    public JumpMapFactAttribute()
    {
        if (!File.Exists(JumpRecording.NavPath))
            Skip = $"this tree has no {JumpRecording.NavPath}: `make map MAP={JumpRecording.Map}` builds it";
    }
}

/// <summary>
/// One recorded flight as the simulator replays it: the state the bot was in at
/// <paramref name="StartTick"/> (on the ground, running at the jump), the commands it sent on the
/// ticks after, and where the game had it after each.
/// </summary>
internal sealed record RecordedFlight(int StartTick, JumpStart Start, JumpCommand[] Commands, JumpPoint[] Frames)
{
    /// <summary>The first frame off the ground, and the first after it back on.</summary>
    public int Takeoff => Array.FindIndex(Frames, p => !p.OnGround);

    public int Landing => Array.FindIndex(Frames, Takeoff, p => p.OnGround);

    /// <summary>The highest the origin rose in the air, over where it stood at the start.</summary>
    public float Apex => Frames[Takeoff..(Landing + 1)].Max(p => p.Origin.Z) - Start.Origin.Z;

    /// <summary>
    /// The frame the game's blast is first felt in: the first in the air whose speed upward is more than the frame
    /// before's. Off the ground gravity only takes it, so a gain is a push, whatever its size; -1 when there is none.
    /// </summary>
    public int Blast => Array.FindIndex(Frames, Takeoff + 1, p => !p.OnGround) is var first && first < 0 ? -1
        : Enumerable.Range(first, Landing - first).Where(i => Frames[i].Velocity.Z > Frames[i - 1].Velocity.Z).DefaultIfEmpty(-1).First();
}

/// <summary>
/// The Soldier bot's flights on ss_ai_tf_jump, recorded in the game by <c>tools/ai-tf-jump record</c>
/// and committed as <c>recordings/ss_ai_tf_jump.airec</c> beside its provenance: the bot's
/// usercmds and its frame each tick (origin, eye, velocity) around each flight. A recording has no
/// ground flag: a frame is on the ground when its fall speed is exactly zero, which the movement
/// loop makes it there and gravity makes it nowhere else on these tracks.
/// </summary>
internal static class JumpRecording
{
    internal const string Map = "ss_ai_tf_jump";

    /// <summary>Ticks after the landing a replay goes on for.</summary>
    internal const int SettleTicks = 20;

    internal static string Path => RepoLayout.Repo("src", "ai", "tests", "recordings", Map + ".airec");

    internal static string NavPath => RepoLayout.Repo("bin", PerTreeNav.Config, "mods", "tf2", "maps", Map + ".nav3d");

    /// <summary>The map's free space, from this tree's built nav.</summary>
    internal static VoxelColumns Columns()
        => VoxelColumns.FromNav(new Nav3dNavSource(Nav3dReader.Open(File.ReadAllBytes(NavPath))));

    /// <summary>The map's graph as the TF2 binding derives it from this tree's built nav (no hurt volume: the map has none).</summary>
    internal static NavGraph Graph()
        => NavGraphFromNav3d.Derive(new Nav3dNavSource(Nav3dReader.Open(File.ReadAllBytes(NavPath))), TFNavProfile.ModNumbers, NavEdgeCosts.Default, []).Graph;

    private static Vector V(AiVector v) => new(v.X, v.Y, v.Z);

    private static bool Crouched(in AiActorFrame f) => f.Eye.Z - f.Origin.Z < 60f;

    private static JumpPoint Point(in AiActorFrame f) => new(V(f.Origin), V(f.Velocity), f.Velocity.Z == 0f, Crouched(f));

    /// <summary>
    /// Every flight in the recording, in the order flown, each from <paramref name="runUpTicks"/> ticks
    /// before its takeoff state: the last frame on the ground before the jump.
    /// </summary>
    internal static List<RecordedFlight> Flights(int runUpTicks = 0)
    {
        var commands = new SortedDictionary<int, AiUserCmd>();
        var frames = new SortedDictionary<int, AiActorFrame>();
        using (FileStream file = File.OpenRead(Path))
        {
            AiRecordFormat.ReadFileHeader(file);
            while (AiRecordFormat.TryReadRecord(file, out AiRecordType type, out byte[] payload))
            {
                using var reader = new BinaryReader(new MemoryStream(payload));
                if (type == AiRecordType.UserCmd)
                {
                    AiUserCmd c = AiRecordFormat.ReadUserCmd(reader);
                    commands.Add(c.Tick.Value, c);
                }
                else if (type == AiRecordType.Frame)
                {
                    AiActorFrame f = AiRecordFormat.ReadFrame(reader);
                    frames.Add(f.Tick.Value, f);
                }
            }
        }

        // A window is a run of consecutive ticks; its flight is the time off the ground that holds its highest frame.
        var flights = new List<RecordedFlight>();
        int[] ticks = [.. frames.Keys];
        for (int i = 0; i < ticks.Length;)
        {
            int end = i;
            while (end + 1 < ticks.Length && ticks[end + 1] == ticks[end] + 1)
                end++;
            int top = Enumerable.Range(i, end - i + 1).MaxBy(n => frames[ticks[n]].Origin.Z);
            int takeoff = top, landing = top;
            while (takeoff > i && frames[ticks[takeoff - 1]].Velocity.Z != 0f)
                takeoff--;
            while (landing < end && frames[ticks[landing]].Velocity.Z != 0f)
                landing++;
            int start = ticks[takeoff] - 1 - runUpTicks, last = Math.Min(ticks[end], ticks[landing] + SettleTicks);
            AiActorFrame at = frames[start];
            AiUserCmd held = commands[start];
            flights.Add(new RecordedFlight(start,
                new JumpStart(V(at.Origin), V(at.Velocity), new Vector(held.View.Pitch, held.View.Yaw, held.View.Roll), Crouched(at), at.Velocity.Z == 0f, held.Buttons),
                [.. Enumerable.Range(start + 1, last - start).Select(t => commands[t]).Select(c => new JumpCommand(new Vector(c.View.Pitch, c.View.Yaw, c.View.Roll), c.ForwardMove, c.SideMove, c.UpMove, c.Buttons))],
                [.. Enumerable.Range(start + 1, last - start).Select(t => Point(frames[t]))]));
            i = end + 1;
        }

        return flights;
    }
}
