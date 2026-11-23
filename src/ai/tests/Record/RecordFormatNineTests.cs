using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// Format 9 (plan_bots_teams_tf2_part_g R.2 item 8): a map in rooms changes the graph the brains'
/// world is over when its bots go from room to room, and the recording says so with a NavChange
/// record, the new graph's hash and the room's name. A replay asks for that graph and follows the
/// change; the bots the game made again over it follow as BotRemoved and BotAdded records. A
/// format 8 file has no such record and reads as before.
/// </summary>
public sealed class RecordFormatNineTests
{
    private static readonly ActorId A = new(1, 1);
    private static readonly byte[] Medium = [4, 4, 4, 3, 3, 3, 4, 3, 4, 3];

    /// <summary>A strip of <paramref name="cells"/> cells, a walk each way between neighbours: two strips of different lengths are two graphs.</summary>
    private static NavGraph Strip(int cells)
    {
        var b = new NavGraphBuilder();
        for (int i = 0; i < cells; i++)
            b.AddCell(new NavCell(i * 16f, 0f, i * 16f + 16f, 16f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        for (int i = 0; i + 1 < cells; i++)
            b.AddBoth(new CellId(i), new CellId(i + 1), NavEdgeType.Walk);
        return b.Build();
    }

    private static readonly NavGraph Hub = Strip(3), Course = Strip(5);

    private static MemoryWorld Over(NavGraph g) => new(MapKnowledge.Empty(new WeaponKnowledge([])), g, 0.015f, id => id.ToString());

    private static void Publish(AiSession s, int tick)
    {
        AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(tick));
        snap.Add(A, new SelfState(default, default, default, 100, 0, 0, 0, 0, SelfFlags.Alive));
        s.Publish(snap);
    }

    /// <summary>A live session over the hub that moves to the course after a tick, as the game does: the record, the world, the bot made again.</summary>
    private static List<NavGraph?> Live(Stream into)
    {
        var seen = new List<NavGraph?>();
        var recorder = new AiRecorder(into, new AiRecordingHeader(7, "m", Hub.Hash, 0, 8, 4));
        recorder.World(new AiRecordedWorld(MapKnowledge.Empty(new WeaponKnowledge([])), 0.015f, SourceSharp.Ai.Tests.Skills.Curves.Game().Json, Hub.Hash));
        var s = new AiSession(new InlineTransport(), recorder, 8, 4)
        {
            World = Over(Hub),
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = setup =>
            {
                seen.Add(setup.Memory.World.Graph);
                return null;
            },
        };
        s.AddBot(A, 1, Medium);
        Publish(s, 1);
        recorder.NavChange(new AiRecordedNavChange(Course.Hash, "m.r5.navhull"));
        s.World = Over(Course);
        s.RemoveBot(A);
        s.AddBot(A, 1, Medium);
        Publish(s, 2);
        return seen;
    }

    private static List<NavGraph?> Replay(Stream recording, Func<AiRecordedWorld, (NavGraph?, ICellSight?)> nav)
    {
        recording.Position = 0;
        var seen = new List<NavGraph?>();
        AiReplayer.ReplayWithRecordedWorld(recording, new InlineTransport(), nav, setup =>
        {
            seen.Add(setup.Memory.World.Graph);
            return null;
        });
        return seen;
    }

    [Fact]
    public void AReplayFollowsTheRoomChangeTheRecordingSays()
    {
        using var file = new MemoryStream();
        Assert.Equal([Hub, Course], Live(file));
        var asked = new List<string?>();
        List<NavGraph?> replayed = Replay(file, w =>
        {
            asked.Add(w.Nav);
            return (w.NavGraphHash == Course.Hash ? Course : Hub, null);
        });
        Assert.Equal([Hub, Course], replayed);
        Assert.Equal([null, "m.r5.navhull"], asked);
    }

    [Fact]
    public void AReplayWithoutTheRoomsGraphIsRefusedNamingTheRoom()
    {
        using var file = new MemoryStream();
        Live(file);
        var e = Assert.Throws<InvalidDataException>(() => Replay(file, _ => (Hub, null)));
        Assert.Contains("m.r5.navhull", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANavChangeReadsBackAsItWasWritten()
    {
        var change = new AiRecordedNavChange(0x0123_4567_89ab_cdefUL, "jump_academy2_rc9a.r62.navhull");
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            AiRecordFormat.Write(w, change);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        Assert.Equal(change, AiRecordFormat.ReadNavChange(r));
        Assert.Equal(ms.Length, ms.Position);
    }

    [Theory]
    [InlineData((ushort)8, true)]
    [InlineData((ushort)9, true)]
    [InlineData((ushort)10, true)]
    [InlineData((ushort)11, false)]
    public void TheReaderTakesFormatsEightNineAndTen(ushort version, bool read)
    {
        using var ms = new MemoryStream();
        ms.Write(AiRecordFormat.Magic);
        ms.Write(BitConverter.GetBytes(version));
        ms.Position = 0;
        if (read)
            AiRecordFormat.ReadFileHeader(ms);
        else
            Assert.Throws<InvalidDataException>(() => AiRecordFormat.ReadFileHeader(ms));
        Assert.Equal(10, AiRecordFormat.Version);
    }
}
