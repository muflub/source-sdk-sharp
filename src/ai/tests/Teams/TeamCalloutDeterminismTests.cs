using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// B.4's determinism: a team whose spotters call three opponents out to teammates who
/// cannot see them replays to the same intent stream, byte for byte, inline and on 1, 2
/// and 4 worker threads, with the callouts re-published by the replay's own session.
/// </summary>
public sealed class TeamCalloutDeterminismTests
{
    private const int Slots = 16, Sighted = 8, Ticks = 400;

    private static readonly string[] Presets = ["easy", "normal", "hard", "expert", "normal", "hard", "easy", "expert"];

    /// <summary>Bots 0-3 spot from the west; bots 4-7 wait in the east, out of sight.</summary>
    private static AiVector Home(int bot) => bot < 4 ? new(200f + 220f * bot, 300f, 0f) : new(2000f + 200f * (bot - 4), 2400f, 0f);

    private static AiVector Seen(int bot, int opponent) => Home(bot) + new AiVector(250f + 120f * opponent, 90f * (opponent - 1), 0f);

    private static ActorId BotId(int i) => new(1 + i, 1 + i);

    private static ActorId OpponentId(int o) => new(10 + o, 0x4000_0000 | (10 + o));

    private static (byte[] Recording, MemoryWorld World, int Callouts) Make()
    {
        var (g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 64), 64)]);
        var map = new MapKnowledge("grid", [new ItemSpawn(0, "item_healthkit", ItemKind.Health, new AiVector(1000f, 1000f, 0f), 30f)], [], Duel.Weapons);
        var world = new MemoryWorld(map, g, 0.015f, id => id.ToString());
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, new AiRecordingHeader(9, "grid", g.Hash, 0, Slots, Sighted));
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
            s.AddBot(BotId(i), (ulong)(2000 + i), [.. p!.Levels.Select(l => (byte)l)]);
        }

        for (int t = 1; t <= Ticks; t++)
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                if (t == 1)
                {
                    for (int j = 0; j < Presets.Length; j++)
                        s.Push(AiEvent.ActorTeam(BotId(i), new Tick(t), BotId(j), 2, 0));
                    for (int o = 0; o < 3; o++)
                        s.Push(AiEvent.ActorTeam(BotId(i), new Tick(t), OpponentId(o), 3, 0));
                }

                for (int o = 0; i < 4 && o < 3; o++)
                {
                    if (t == 3 + 40 * o)
                        s.Push(AiEvent.SightGained(BotId(i), new Tick(t), OpponentId(o), Seen(i, o), AiVector.Zero, 1f));
                    else if (t == 30 + 40 * o)
                        s.Push(AiEvent.SightLost(BotId(i), new Tick(t), OpponentId(o), Seen(i, o), new AiVector(190f, 40f * (o - 1), 0f)));
                }
            }

            AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(t));
            for (int i = 0; i < Presets.Length; i++)
            {
                PerceivedView v = snap.Add(BotId(i), new SelfState(Home(i), default, default, 100, 0, Duel.Smg1, 30, 90, SelfFlags.Alive | SelfFlags.Grounded, Team: 2));
                for (int o = 0; i < 4 && o < 3; o++)
                {
                    if (t >= 3 + 40 * o && t < 30 + 40 * o)
                        v.AddSighted(new ActorFacts(OpponentId(o), Seen(i, o), AiVector.Zero, Duel.Smg1, ActorActivity.None, 1f, 3));
                }
            }

            s.Publish(snap);
        }

        s.Drain(wait: true);
        return (bytes.ToArray(), world, (int)s.Counters.CalloutsDelivered);
    }

    private static AiReplayResult Replay(byte[] recording, MemoryWorld world, IAiTransport transport)
        => AiReplayer.Replay(new MemoryStream(recording), transport, world, SourceSharp.Ai.Tests.Skills.Curves.Game(),
                             setup => new Hl2mpDecider(setup), null);

    [Fact]
    public void ATeamsCalloutsReplayTheSameInlineAndAtOneTwoAndFourThreads()
    {
        (byte[] recording, MemoryWorld world, int callouts) = Make();
        Assert.True(callouts > 0, "the stimulus reaches the subject: the spotters called something out");

        AiReplayResult inline = Replay(recording, world, new InlineTransport());
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
