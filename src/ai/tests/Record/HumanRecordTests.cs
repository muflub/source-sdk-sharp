using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// A human's reference session as the recording holds it (plan_hl2mp_ai
/// §3.4.4): every actor's frames, a person's sight as Observed records that
/// no brain is fed, and every actor a record names anonymised at write time
/// (a person by a random serial of the session, a bot as it is).
/// </summary>
public sealed class HumanRecordTests
{
    private const int Mark = 0x4000_0000;
    private static readonly ActorId Bot = new(1, 3);
    private static readonly ActorId Human = new(2, Mark | 777);

    private static ActorAnonymiser Anon(ulong seed = 42) => new(id => (id.Serial & Mark) != 0, Mark, seed);

    private static byte[] Encode(Action<BinaryWriter> write)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        write(w);
        w.Flush();
        return m.ToArray();
    }

    private static byte[] Make(ActorAnonymiser? actors, Action<AiSession, AiRecorder> drive)
    {
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, Recordings.Header, actors);
        var session = new AiSession(new InlineTransport(), recorder, Recordings.Header.Slots, Recordings.Header.SightedCapacity);
        drive(session, recorder);
        return bytes.ToArray();
    }

    [Fact]
    public void AFrameRoundTrips()
    {
        var f = new AiActorFrame(Human, new Tick(9), new AiVector(1, 2, 3), new AiVector(1, 2, 67), new AiVector(-200, 5, 0));
        Assert.Equal(f, AiRecordFormat.ReadFrame(new BinaryReader(new MemoryStream(Encode(w => AiRecordFormat.Write(w, f))))));
    }

    [Fact]
    public void AnObservedEventIsWrittenButNeverFedToABrain()
    {
        // The Observed record names the bot as its subject: were it fed, the bot's session would count it in.
        AiEvent sight = AiEvent.SightGained(Bot, new Tick(4), Human, new AiVector(5, 5, 5), default, 1f);
        byte[] rec = Make(null, (s, r) =>
        {
            s.AddBot(Bot, 1, []);
            r.Observed(sight);
            r.Frame(new AiActorFrame(Bot, new Tick(5), default, default, default));
            Recordings.Publish(s, 5, Bot);
        });

        Assert.Contains(Recordings.Records(rec), x => x.Type == AiRecordType.Observed);
        long eventsIn = -1;
        AiReplayResult replay = AiReplayer.Replay(new MemoryStream(rec), new InlineTransport(), null, (session, _) => eventsIn = session.Counters.EventsIn);
        Assert.Equal(0, eventsIn);
        Assert.True(replay.Identical && replay.ReplayedIntentCount == 1, "the replay steps the bot as recorded");
    }

    [Fact]
    public void APersonIsWrittenUnderOneRandomSerialOfItsSlotForTheSession()
    {
        ActorAnonymiser a = Anon();
        ActorId first = a.Map(Human);
        Assert.Equal(Human.Slot, first.Slot);
        Assert.NotEqual(Human.Serial, first.Serial);
        Assert.Equal(Mark, first.Serial & Mark);
        Assert.Equal(first, a.Map(Human));
        Assert.Equal(first, a.Map(AiEvent.SightLost(Bot, new Tick(2), Human, default, default)).Other);
    }

    [Fact]
    public void AnotherSessionDrawsAnotherSerial()
        => Assert.NotEqual(Anon(1).Map(Human), Anon(2).Map(Human));

    [Fact]
    public void ABotIsWrittenAsItIs()
    {
        ActorAnonymiser a = Anon();
        Assert.Equal(Bot, a.Map(Bot));
        Assert.Equal(ActorId.None, a.Map(ActorId.None));
        Assert.Equal(0, a.Count);
    }

    [Fact]
    public void EveryPlaceARecordNamesAnActorIsAnonymised()
    {
        ActorAnonymiser a = Anon();
        ActorId anon = a.Map(Human);
        AiEvent feed = a.Map(AiEvent.KillFeed(Bot, new Tick(3), Bot, Human));
        Assert.Equal((anon.Slot, anon.Serial), (feed.Code, feed.Code2));
        var intent = new IntentCommand(Bot, new Tick(1), 1, new Tick(9), default,
                                       new LookIntent(LookKind.Actor, default, Human, AimPoint.Head, 0f, 0), default, default);
        Assert.Equal(anon, a.Map(intent).Look.Actor);
        Assert.Equal(anon, a.Map(new AiUserCmd(Human, new Tick(1), 1, 1, default, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)).Actor);
        Assert.Equal(anon, a.Map(new AiActorFrame(Human, new Tick(1), default, default, default)).Actor);
        Assert.Equal(anon, a.Map(AiEvent.Damaged(Bot, new Tick(1), Human, 9f, default, 0)).Other);
    }

    [Fact]
    public void ARecordingWithAnAnonymiserHoldsThePersonOnlyUnderItsRandomSerial()
    {
        ActorAnonymiser actors = Anon(7);
        byte[] rec = Make(actors, (s, r) =>
        {
            s.AddBot(Bot, 1, []);
            r.UserCmd(new AiUserCmd(Human, new Tick(4), 1, 4, new AiAngles(0, 90, 0), 450, 0, 0, 1, 0, 0, 0, 0, 0, 0));
            r.Frame(new AiActorFrame(Human, new Tick(4), default, default, default));
            r.Observed(AiEvent.SightGained(Human, new Tick(4), Bot, default, default, 1f));
            s.Push(AiEvent.SightGained(Bot, new Tick(4), Human, default, default, 1f));
            AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(5));
            snap.Add(Bot, default).AddSighted(new ActorFacts(Human, default, default, 0, ActorActivity.None, 1f));
            s.Publish(snap);
        });

        byte[] raw = BitConverter.GetBytes(Human.Serial), drawn = BitConverter.GetBytes(actors.Map(Human).Serial);
        Assert.True(Count(rec, drawn) >= 5, $"the control: the drawn serial is written ({Count(rec, drawn)} times)");
        Assert.Equal(0, Count(rec, raw));
    }

    private static int Count(byte[] hay, byte[] needle)
    {
        int n = 0;
        for (int i = hay.AsSpan().IndexOf(needle); i >= 0; )
        {
            n++;
            int next = hay.AsSpan(i + 1).IndexOf(needle);
            i = next < 0 ? -1 : i + 1 + next;
        }

        return n;
    }
    /// <summary>
    /// A recording started mid-game (a human session) names the bots already
    /// playing, with their seeds and skills, so the metrics know them for bots.
    /// </summary>
    [Fact]
    public void ARecordingStartedMidGameNamesTheBotsAlreadyPlaying()
    {
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, Recordings.Header);
        var announcing = new AiSession(new InlineTransport(), recorder, Recordings.Header.Slots, Recordings.Header.SightedCapacity);
        announcing.AddBot(new ActorId(5, 9), 88, [4, 5]);
        int before = Recordings.Records(bytes.ToArray()).Count(r => r.Type == AiRecordType.BotAdded);
        announcing.AnnounceBots();
        var added = Recordings.Records(bytes.ToArray()).Where(r => r.Type == AiRecordType.BotAdded).Skip(before).ToList();
        var r = new BinaryReader(new MemoryStream(Assert.Single(added).Payload));
        Assert.Equal((new ActorId(5, 9), 88UL), (AiRecordFormat.ReadActor(r), r.ReadUInt64()));
        Assert.Equal(new byte[] { 4, 5 }, r.ReadBytes(r.ReadUInt16()));
    }
}
