using Xunit;

namespace SourceSharp.Ai.Tests.Record;

public class FormatTests
{
    private static byte[] Encode(Action<BinaryWriter> write)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        write(w);
        w.Flush();
        return m.ToArray();
    }

    private static BinaryReader Reader(byte[] b) => new(new MemoryStream(b));

    private static readonly AiEvent AnEvent =
        AiEvent.SoundHeard(new ActorId(2, 5), new Tick(77), SoundKind.Reload, new AiVector(1.5f, -2, 3), 0.25f, new ActorId(9, 1), 4);

    private static readonly IntentCommand AnIntent = new(
        new ActorId(3, 2), new Tick(30), 9, new Tick(46),
        new MoveIntent(MoveKind.Follow, MoveSpeed.Walk, 2, new AiVector(1, 2, 3), new AiVector(4, 5, 6), default, default, default, false, 0),
        new LookIntent(LookKind.Actor, default, new ActorId(5, 1), AimPoint.Head, 0.25f, 0),
        new ActionIntent(ActionKind.Fire, FireStyle.Burst, 0, 3),
        new DecisionReason(17, 0.75f, 0.1f, 0.2f));

    private static readonly AiUserCmd AUserCmd = new(new ActorId(1, 1), new Tick(5), 100, 105, new AiAngles(10, 20, 0),
                                                      450, -450, 0, 0x21, 100, 7, 0, 12345, -3, 4);

    [Fact]
    public void AnEventRoundTrips()
        => Assert.Equal(AnEvent, AiRecordFormat.ReadEvent(Reader(Encode(w => AiRecordFormat.Write(w, AnEvent)))));

    [Fact]
    public void AnIntentRoundTrips()
        => Assert.Equal(AnIntent, AiRecordFormat.ReadIntent(Reader(Encode(w => AiRecordFormat.Write(w, AnIntent)))));

    [Fact]
    public void AUserCmdRoundTrips()
        => Assert.Equal(AUserCmd, AiRecordFormat.ReadUserCmd(Reader(Encode(w => AiRecordFormat.Write(w, AUserCmd)))));

    [Fact]
    public void AHeaderRoundTrips()
        => Assert.Equal(Recordings.Header, AiRecordFormat.ReadHeader(Reader(Encode(w => AiRecordFormat.Write(w, Recordings.Header)))));

    [Fact]
    public void ASnapshotRoundTripsByteForByte()
    {
        var ring = new SnapshotRing(8, 8, 4);
        AiSnapshot s = ring.BeginWrite(new Tick(3));
        s.Add(new ActorId(2, 1), new SelfState(new AiVector(1, 2, 3), default, new AiAngles(0, 90, 0), 80, 10, 1, 5, 40, SelfFlags.Alive | SelfFlags.Ducking))
         .AddSighted(new ActorFacts(new ActorId(5, 2), new AiVector(9, 9, 9), default, 3, ActorActivity.Firing, 0.4f));
        s.Add(new ActorId(6, 1), default);
        ring.Publish(s);
        byte[] first = Encode(w => AiRecordFormat.Write(w, s));

        var ring2 = new SnapshotRing(8, 8, 4);
        AiSnapshot back = AiRecordFormat.ReadSnapshot(Reader(first), ring2);
        ring2.Publish(back);
        Assert.Equal(first, Encode(w => AiRecordFormat.Write(w, back)));
    }

    [Fact]
    public void AWrongMagicIsRefused()
    {
        byte[] rec = Recordings.Make(_ => { });
        rec[0] = (byte)'X';
        var e = Assert.Throws<InvalidDataException>(() => Recordings.Replay(rec));
        Assert.Contains("magic", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherFormatVersionIsRefusedByNumber()
    {
        byte[] rec = Recordings.Make(_ => { });
        rec[8] = 2;
        var e = Assert.Throws<InvalidDataException>(() => Recordings.Replay(rec));
        Assert.Contains("format 2", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACutRecordIsRefused()
    {
        byte[] rec = Recordings.Make(s => s.AddBot(new ActorId(1, 1), 1, []));
        Assert.Throws<InvalidDataException>(() => Recordings.Replay(rec[..^3]));
    }

    [Fact]
    public void AnUnknownRecordTypeIsRefused()
    {
        var records = Recordings.Records(Recordings.Make(_ => { }));
        records.Add(((AiRecordType)99, []));
        Assert.Throws<InvalidDataException>(() => Recordings.Replay(Recordings.Build(records)));
    }

    [Fact]
    public void ARecordLongerThanItsTypeIsRefused()
    {
        var records = Recordings.Records(Recordings.Make(s => s.AddBot(new ActorId(1, 1), 1, [])));
        int i = records.FindIndex(x => x.Type == AiRecordType.BotAdded);
        records[i] = (AiRecordType.BotAdded, [.. records[i].Payload, 0]);
        Assert.Throws<InvalidDataException>(() => Recordings.Replay(Recordings.Build(records)));
    }

    [Fact]
    public void ARecordingWithoutAHeaderIsRefused()
    {
        var records = Recordings.Records(Recordings.Make(s => s.AddBot(new ActorId(1, 1), 1, [])));
        records.RemoveAt(0);
        Assert.Throws<InvalidDataException>(() => Recordings.Replay(Recordings.Build(records)));
    }

    [Fact]
    public void EveryRecordIsLengthPrefixed()
    {
        byte[] rec = Recordings.Make(s => s.AddBot(new ActorId(1, 1), 1, [9]));
        // magic (8) + version (2), then the header record's type byte and its length.
        uint length = BitConverter.ToUInt32(rec, 11);
        Assert.Equal((byte)AiRecordType.BotAdded, rec[11 + 4 + (int)length]);
    }
}
