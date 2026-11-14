using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// plan_descent_logic 3.10's determinism gate for the horde's session (HO4): a
/// session recorded through the core's <see cref="AiRecorder"/> while robots
/// see, lose, hear and are hit by a hero walking the level replays through a
/// fresh session to the same intent stream, byte for byte, inline and on a
/// pool of 1, 2 and 4 worker threads (as the core's ThreadDeterminismTests).
/// </summary>
public sealed class HordeReplayTests
{
    private const int Ticks = 900;

    private static (byte[] Recording, HordeLevel Level, int Intents) Record()
    {
        var level = Row(4, markers: 0);
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, HordeBrains.Header(level));
        recorder.World(HordeBrains.RecordedWorld(0.015f));
        var log = new EventLog();
        var rig = new BrainRig(level, sink: new Both(recorder, log));
        var robots = new List<TF2.Descent.Ai.Roster.RobotId>();
        for (int r = 0; r < 6; r++)
        {
            var id = rig.Robot(Centre(r % 4) + At(-60 + 25 * r, 40 * (r % 3) - 40), seed: (ulong)(31 + r));
            robots.Add(id);
            rig.Lend(id, yaw: 60f * r);
        }

        for (int t = 0; t < Ticks; t++)
        {
            // The hero walks room 0 to room 3 and back; walls block every line while it is in room 2.
            float x = 256f + 1536f * MathF.Abs(MathF.Sin(t * 0.004f));
            rig.Hero(1, At(x, 300));
            rig.Lines.Clear = (from, to) => rig.Level.RoomAt(to with { Z = 0 }) != 2;
            if (t % 97 == 40)
                rig.Sounds.Add(new HordeSound(SoundKind.Gunfire, At(x, 300), 1500f, 1, rig.Level.RoomAt(At(x, 300))));
            if (t % 151 == 75)
            {
                var hit = robots[t % robots.Count];
                if (rig.Roster.Body(hit.Index) is var b && b != 0)
                    rig.Brains.Hit(rig.Now + 1, hit, 1, 12f, false, rig.Poses.Position(b));
            }

            rig.Tick();
        }

        rig.Brains.Session.Drain(wait: true);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.SightGained);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.SightLost);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.SoundHeard);
        Assert.Contains(log.Events, e => e.Kind == AiEventKind.Damaged);
        Assert.Contains(log.Intents, i => i.Move.Kind == MoveKind.Follow);
        return (bytes.ToArray(), level, log.Intents.Count);
    }

    private static AiReplayResult Replay(byte[] recording, HordeLevel level, IAiTransport transport) =>
        AiReplayer.ReplayWithRecordedWorld(new MemoryStream(recording), transport, _ => (null, null), HordeDeciders.For(level).Make);

    [Fact]
    public void A_recorded_horde_session_replays_to_the_same_intents_inline_and_at_one_two_and_four_threads()
    {
        var (recording, level, intents) = Record();

        var inline = Replay(recording, level, new InlineTransport());
        Assert.True(inline.RecordedIntentCount == intents && intents >= 6 * (Ticks / DecideSchedule.Period) - 12,
                    $"the control: {inline.RecordedIntentCount} intents recorded, {intents} made");
        Assert.True(inline.Identical, "inline: the intent stream differs from the recording's");

        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var pool = new AiWorkerPool();
            pool.Start(threads);
            var pooled = Replay(recording, level, new PooledTransport(pool));
            Assert.Equal(inline.ReplayedIntentCount, pooled.ReplayedIntentCount);
            Assert.True(pooled.Identical, $"{threads} thread(s): the intent stream differs from the recording's");
        }
    }

    /// <summary>Hands every record to two sinks.</summary>
    private sealed class Both(IAiRecordSink a, IAiRecordSink b) : IAiRecordSink
    {
        public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels)
        {
            a.BotAdded(bot, seed, skillLevels);
            b.BotAdded(bot, seed, skillLevels);
        }

        public void BotRemoved(ActorId bot)
        {
            a.BotRemoved(bot);
            b.BotRemoved(bot);
        }

        public void DecisionTree(IReadOnlyList<string> names, IReadOnlyList<int> branches)
        {
            a.DecisionTree(names, branches);
            b.DecisionTree(names, branches);
        }

        public void Event(in AiEvent e)
        {
            a.Event(e);
            b.Event(e);
        }

        public void Snapshot(AiSnapshot snapshot)
        {
            a.Snapshot(snapshot);
            b.Snapshot(snapshot);
        }

        public void Intent(in IntentCommand intent)
        {
            a.Intent(intent);
            b.Intent(intent);
        }

        public void UserCmd(in AiUserCmd cmd) => a.UserCmd(cmd);

        public void Frame(in AiActorFrame frame) => a.Frame(frame);

        public void Observed(in AiEvent e) => a.Observed(e);
    }
}
