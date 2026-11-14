using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// Format 8 (cf's Descent proposals 4 and 6): the Header record says how its session ran (a
/// session without its team layer replays without it), and the BotAdded record carries the bot's
/// home point, which a replay hands its decider as the game did. An older file has neither tail
/// and replays as before: the layer on, no home.
/// </summary>
public sealed class RecordFormatEightTests
{
    private static readonly ActorId A = new(1, 1);
    private static readonly byte[] Medium = [4, 4, 4, 3, 3, 3, 4, 3, 4, 3];
    private static readonly AiVector Post = new(128f, -64f, 16f);

    private static AiSession Live(Stream into, bool callouts, AiVector? home, List<AiVector?> homes)
    {
        var header = new AiRecordingHeader(7, "m", 0, 0, 8, 4, callouts ? (byte)0 : AiRecordingHeader.TeamLayerOff);
        var s = new AiSession(new InlineTransport(), new AiRecorder(into, header), 8, 4)
        {
            World = MemoryWorld.Empty(),
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = setup =>
            {
                homes.Add(setup.Home);
                return null;
            },
            TeamCallouts = callouts,
        };
        s.AddBot(A, 1, Medium, home: home);
        AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(1));
        snap.Add(A, new SelfState(default, default, default, 100, 0, 0, 0, 0, SelfFlags.Alive));
        s.Publish(snap);
        return s;
    }

    private static (bool Callouts, List<AiVector?> Homes) Replay(Stream recording)
    {
        recording.Position = 0;
        var homes = new List<AiVector?>();
        bool? callouts = null;
        AiReplayer.Replay(recording, new InlineTransport(), MemoryWorld.Empty(), SourceSharp.Ai.Tests.Skills.Curves.Game(),
                          setup =>
                          {
                              homes.Add(setup.Home);
                              return null;
                          },
                          (s, _) => callouts = s.TeamCallouts);
        Assert.True(callouts.HasValue, "the control: the replay published");
        return (callouts!.Value, homes);
    }

    [Fact]
    public void ASessionWithoutItsTeamLayerReplaysWithoutIt()
    {
        using var off = new MemoryStream();
        Live(off, callouts: false, home: null, []);
        Assert.False(Replay(off).Callouts);

        // The control: a session with the layer replays with it.
        using var on = new MemoryStream();
        Live(on, callouts: true, home: null, []);
        Assert.True(Replay(on).Callouts);
    }

    [Fact]
    public void ABotsHomeReachesItsDeciderLiveAndInTheReplay()
    {
        using var file = new MemoryStream();
        var live = new List<AiVector?>();
        Live(file, callouts: true, home: Post, live);
        Assert.Equal([Post], live);
        Assert.Equal([Post], Replay(file).Homes);

        // A bot added with no home has none, live and replayed.
        using var none = new MemoryStream();
        var liveNone = new List<AiVector?>();
        Live(none, callouts: true, home: null, liveNone);
        Assert.Equal([null], liveNone);
        Assert.Equal([null], Replay(none).Homes);
    }

    [Fact]
    public void AnOlderRecordHasNoFlagsAndNoHome()
    {
        var header = new AiRecordingHeader(7, "m", 1, 2, 8, 4, AiRecordingHeader.TeamLayerOff);
        byte[] full = Payload(w => AiRecordFormat.Write(w, header));
        Assert.Equal(header, AiRecordFormat.ReadHeader(Reader(full)));
        AiRecordingHeader old = AiRecordFormat.ReadHeader(Reader(full[..^1]));
        Assert.Equal((header with { Flags = 0 }, true), (old, old.TeamCallouts));

        byte[] added = Payload(w => AiRecordFormat.WriteBotAdded(w, A, 9, Medium, Post));
        Assert.Equal(Post, AiRecordFormat.ReadBotAdded(Reader(added)).Home);
        byte[] format7 = added[..^(1 + 12)];
        (ActorId bot, ulong seed, byte[] skills, AiVector? home) = AiRecordFormat.ReadBotAdded(Reader(format7));
        Assert.Equal((A, 9UL, (AiVector?)null), (bot, seed, home));
        Assert.Equal(Medium, skills);
    }

    private static byte[] Payload(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            write(w);
        return ms.ToArray();
    }

    private static BinaryReader Reader(byte[] payload) => new(new MemoryStream(payload, writable: false));
}
