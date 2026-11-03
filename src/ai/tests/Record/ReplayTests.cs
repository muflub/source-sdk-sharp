using Xunit;

namespace SourceSharp.Ai.Tests.Record;

public class ReplayTests
{
    private static readonly ActorId Bot = new(6, 1);

    [Fact]
    public void AnEmptyRecordingReplaysToNoIntents()
    {
        AiReplayResult r = Recordings.Replay(Recordings.Make(_ => { }));
        Assert.Equal((0, 0, true), (r.RecordedIntentCount, r.ReplayedIntentCount, r.Identical));
    }

    [Fact]
    public void AOneEventRecordingReplaysToAHoldFromItsTick()
    {
        byte[] rec = Recordings.Make(s =>
        {
            s.AddBot(Bot, 11, []);
            s.Push(AiEvent.Spawned(Bot, new Tick(12), new AiVector(1, 2, 3)));
            Recordings.Publish(s, 12, Bot);
        });

        AiReplayResult r = Recordings.Replay(rec);
        IntentCommand replayed = AiRecordFormat.ReadIntent(new BinaryReader(new MemoryStream(r.ReplayedIntents)));
        Assert.Equal((1, MoveKind.Hold, new Tick(12)), (r.ReplayedIntentCount, replayed.Move.Kind, replayed.FromTick));
    }

    [Fact]
    public void AReplayMatchesTheRecordingByteForByte()
    {
        byte[] rec = Recordings.Make(s =>
        {
            s.AddBot(new ActorId(1, 1), 1, [1, 2, 3]);
            s.AddBot(new ActorId(4, 2), 2, [4, 5]);
            for (int t = 0; t < 40; t++)
            {
                s.Push(AiEvent.SoundHeard(new ActorId(1, 1), new Tick(t), SoundKind.Gunfire, new AiVector(t, 0, 0), 0.5f, new ActorId(7, 3), 2));
                Recordings.Publish(s, t, new ActorId(1, 1), new ActorId(4, 2));
            }
        });

        AiReplayResult r = Recordings.Replay(rec);
        // Slot 1 decides at 5, 11, ..., 35 (6 steps), slot 4 at 2, 8, ..., 38 (7).
        Assert.Equal((true, 13), (r.Identical, r.RecordedIntentCount));
    }

    [Fact]
    public void AChangedRecordedIntentIsNotIdentical()
    {
        byte[] rec = Recordings.Make(s =>
        {
            s.AddBot(Bot, 11, []);
            Recordings.Publish(s, 12, Bot);
        });

        var records = Recordings.Records(rec);
        int i = records.FindIndex(x => x.Type == AiRecordType.Intent);
        records[i].Payload[^1] ^= 0x40;
        Assert.False(Recordings.Replay(Recordings.Build(records)).Identical, "a flipped bit in the recorded intent is seen");
    }

    [Fact]
    public void TheHeaderComesBack()
        => Assert.Equal(Recordings.Header, Recordings.Replay(Recordings.Make(_ => { })).Header);

    [Fact]
    public void UserCmdsAreCountedNotStepped()
    {
        byte[] rec = Recordings.Make(_ => { });
        var records = Recordings.Records(rec);
        var w = new MemoryStream();
        AiRecordFormat.Write(new BinaryWriter(w), new AiUserCmd(Bot, new Tick(3), 1, 3, default, 450, 0, 0, 1, 0, 0, 0, 0, 0, 0));
        records.Add((AiRecordType.UserCmd, w.ToArray()));
        AiReplayResult r = Recordings.Replay(Recordings.Build(records));
        Assert.Equal((1, 0), (r.UserCmds, r.ReplayedIntentCount));
    }
}
