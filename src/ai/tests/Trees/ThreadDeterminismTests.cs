using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// §2.6's thread-count determinism over the HL2MP trees: a recording of eight
/// bots, each losing three opponents whose beliefs then spread, replays to the
/// same intent stream, byte for byte, inline and on a pool of 1, 2 and 4
/// worker threads (§8.2). A mutant that lets the brains share one predictor
/// scratch turns the pooled arms red (tools/ai-mutations shared-scratch).
/// </summary>
public sealed class ThreadDeterminismTests
{
    private const int Slots = 16, Sighted = 8, Ticks = 300;

    private static readonly string[] Presets = ["easy", "normal", "hard", "expert", "normal", "hard", "easy", "expert"];

    private static AiVector Home(int bot) => new(200f + 220f * (bot % 4), 200f + 700f * (bot / 4), 0f);

    private static AiVector Seen(int bot, int opponent) => Home(bot) + new AiVector(150f + 120f * opponent, 90f * (opponent - 1), 0f);

    private static ActorId BotId(int i) => new(1 + i, 1 + i);

    private static ActorId OpponentId(int o) => new(10 + o, 0x4000_0000 | (10 + o));

    internal static (byte[] Recording, MemoryWorld World) Make()
    {
        var (g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 64), 64)]);
        var map = new MapKnowledge("grid", [new ItemSpawn(0, "item_healthkit", ItemKind.Health, new AiVector(1000f, 1000f, 0f), 30f)], [], Duel.Weapons);
        var world = new MemoryWorld(map, g, 0.015f, id => id.ToString());
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, new AiRecordingHeader(7, "grid", g.Hash, 0, Slots, Sighted));
        recorder.World(new AiRecordedWorld(map, 0.015f, SourceSharp.Ai.Tests.Skills.Curves.Game().Json, g.Hash));
        var s = new AiSession(new InlineTransport(), recorder, Slots, Sighted)
        {
            World = world,
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = setup => new Hl2mpDecider(setup),
        };
        for (int i = 0; i < Presets.Length; i++)
        {
            SkillPresets.TryGet(Presets[i], out SkillProfile? p);
            s.AddBot(BotId(i), (ulong)(1000 + i), [.. p!.Levels.Select(l => (byte)l)]);
        }

        for (int t = 1; t <= Ticks; t++)
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                for (int o = 0; o < 3; o++)
                {
                    if (t == 3)
                        s.Push(AiEvent.SightGained(BotId(i), new Tick(t), OpponentId(o), Seen(i, o), AiVector.Zero, 1f));
                    else if (t == 30 + 7 * o)
                        s.Push(AiEvent.SightLost(BotId(i), new Tick(t), OpponentId(o), Seen(i, o), new AiVector(190f, 40f * (o - 1), 0f)));
                    else if (t % 97 == 50 + o)
                        s.Push(AiEvent.SoundHeard(BotId(i), new Tick(t), SoundKind.Footstep, Seen(i, o) + new AiVector(t, -t, 0), 0.6f, OpponentId(o), -1));
                }
            }

            AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(t));
            for (int i = 0; i < Presets.Length; i++)
            {
                PerceivedView v = snap.Add(BotId(i), new SelfState(Home(i), default, default, 100, 0, Duel.Smg1, 30, 90, SelfFlags.Alive | SelfFlags.Grounded));
                for (int o = 0; o < 3; o++)
                {
                    if (t >= 3 && t < 30 + 7 * o)
                        v.AddSighted(new ActorFacts(OpponentId(o), Seen(i, o), AiVector.Zero, Duel.Smg1, ActorActivity.None, 1f));
                }
            }

            s.Publish(snap);
        }

        s.Drain(wait: true);
        return (bytes.ToArray(), world);
    }

    private static AiReplayResult Replay(byte[] recording, MemoryWorld world, IAiTransport transport)
        => AiReplayer.Replay(new MemoryStream(recording), transport, world, SourceSharp.Ai.Tests.Skills.Curves.Game(),
                             setup => new Hl2mpDecider(setup), null);

    [Fact]
    public void TheIntentsAreTheSameInlineAndAtOneTwoAndFourThreads()
    {
        (byte[] recording, MemoryWorld world) = Make();

        AiReplayResult inline = Replay(recording, world, new InlineTransport());
        Assert.True(inline.RecordedIntentCount >= Presets.Length * (Ticks / 6) - Presets.Length, $"the control: {inline.RecordedIntentCount} intents recorded");
        Assert.True(inline.Identical, "inline");

        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var pool = new AiWorkerPool();
            pool.Start(threads);
            AiReplayResult pooled = Replay(recording, world, new PooledTransport(pool));
            Assert.Equal(inline.ReplayedIntentCount, pooled.ReplayedIntentCount);
            Assert.True(pooled.Identical, $"{threads} thread(s): the intent stream differs from the recording's");
        }
    }
}
