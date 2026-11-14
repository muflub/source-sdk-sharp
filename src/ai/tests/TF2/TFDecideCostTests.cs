using SourceSharp.Tests;
using SourceSharp.Ai.Replay;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// What a TF2 bot's decide costs (plan_bots_teams_tf2_part_g G1's cost): six bots a side on a
/// 96 by 96 floor, each seeing its five teammates and one enemy, losing all six from sight and
/// hearing each of them walk now and again, for 480 ticks, recorded and replayed through TF2's
/// tree with TF2's bodies by the cost instrument (<see cref="CostTool"/>), in the thread's CPU time.
///
/// <para>The bound is some three times what it cost when the fact was written (176 to 179 µs a
/// decide, 0.71 hops of belief in it, debug, 2026-11-10), the least of five replays, so a loaded
/// or a slower machine does not turn it red. A teammate's belief spreading again, as it did
/// before, is five beliefs more a bot: 826 to 1,109 µs a decide and 4.42 hops, on the same machine.
/// Live it is the counter <c>ai.brain.cpu.us</c>.</para>
/// </summary>
public sealed class TFDecideCostTests
{
    private const int Bots = 12, Slots = 16, Sighted = 12, Ticks = 480;

    /// <summary>The most a decide may cost, in microseconds of thread CPU time.</summary>
    public const double BoundMicros = 600;

    private static ActorId Id(int bot) => new(1 + bot, 1 + bot);

    private static int Team(int bot) => bot < Bots / 2 ? TFField.Red : TFField.Blue;

    private static AiVector Home(int bot) => new(400f + 450f * (bot % 4), 500f + 900f * (bot / 4), 0f);

    /// <summary>Who a bot sees and then loses: its five teammates, then the enemy across from it.</summary>
    private static int[] Seen(int bot)
        => [.. Enumerable.Range(0, Bots).Where(o => o != bot && Team(o) == Team(bot)), (bot + Bots / 2) % Bots];

    private static int LostAt(int nth) => 30 + 7 * nth;

    internal static (byte[] Recording, NavGraph Graph) Make()
    {
        (NavGraph g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 96), 96)]);
        var map = new MapKnowledge("tf_cost", [], [], TFField.Weapons, [new TeamSpawn(TFField.Red, Home(0)), new TeamSpawn(TFField.Blue, Home(Bots - 1))]);
        SkillCurves curves = SourceSharp.Ai.Tests.Skills.Curves.Game();
        var bytes = new MemoryStream();
        var recorder = new AiRecorder(bytes, new AiRecordingHeader(7, "tf_cost", g.Hash, 0, Slots, Sighted));
        recorder.World(new AiRecordedWorld(map, 0.015f, curves.Json, g.Hash));
        var s = new AiSession(new InlineTransport(), recorder, Slots, Sighted)
        {
            World = new MemoryWorld(map, g, 0.015f, id => id.ToString()),
            Curves = curves,
            Deciders = setup => new TFDecider(setup, TFKnowledge.Classes(_ => (0f, 0))),
        };
        SkillPresets.TryGet("hard", out SkillProfile? hard);
        for (int i = 0; i < Bots; i++)
            s.AddBot(Id(i), (ulong)(2000 + i), [.. hard!.Levels.Select(l => (byte)l)]);

        for (int t = 1; t <= Ticks; t++)
        {
            for (int i = 0; i < Bots; i++)
            {
                int[] seen = Seen(i);
                if (t == 1)
                {
                    // The scoreboard: everybody's team and class (the class is what a body is believed to run at).
                    for (int o = 0; o < Bots; o++)
                    {
                        if (o != i)
                            s.Push(AiEvent.ScoreboardRow(Id(i), new Tick(t), Id(o), 0, 0, 1 + o, Team(o), 1 + o % 9));
                    }
                }

                for (int n = 0; n < seen.Length; n++)
                {
                    int o = seen[n];
                    if (t == 3)
                        s.Push(AiEvent.SightGained(Id(i), new Tick(t), Id(o), Home(o), AiVector.Zero, 1f));
                    else if (t == LostAt(n))
                        s.Push(AiEvent.SightLost(Id(i), new Tick(t), Id(o), Home(o), new AiVector(240f, 60f * (n - 2), 0f)));
                    else if (t % 97 == 50 + n)
                        s.Push(AiEvent.SoundHeard(Id(i), new Tick(t), SoundKind.Footstep, Home(o) + new AiVector(t % 64, -(t % 48), 0f), 0.6f, Id(o), -1));
                }
            }

            AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(t));
            for (int i = 0; i < Bots; i++)
            {
                int[] seen = Seen(i);
                PerceivedView v = snap.Add(Id(i), TFField.Self(Home(i), team: Team(i)));
                for (int n = 0; n < seen.Length; n++)
                {
                    if (t >= 3 && t < LostAt(n))
                        v.AddSighted(new ActorFacts(Id(seen[n]), Home(seen[n]), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, Team(seen[n])));
                }
            }

            s.Publish(snap);
        }

        s.Drain(wait: true);
        return (bytes.ToArray(), g);
    }

    private static readonly Lazy<(byte[] Recording, NavGraph Graph)> Session = new(Make);

    private static CostReport Replay(Func<double> clock)
        => CostTool.Measure(new MemoryStream(Session.Value.Recording), CostTool.TF2, Session.Value.Graph, null, CostTool.Bodies(CostTool.TF2), clock);

    /// <summary>The session is what the bound is about: every bot decides all through it, and an enemy's belief spreads in each.</summary>
    [Fact]
    public void TheSessionIsTwelveBotsDecidingWithAnEnemysBeliefSpreading()
    {
        double now = 0;
        CostReport report = Replay(() => now++);
        Assert.Equal((Bots, (long)Bots * (Ticks / DecideSchedule.Period)), (report.Bots, report.Steps));
        long hops = report.Parts.Single(p => p.Part == StepPart.Predict).Units;
        Assert.True(hops >= Bots * 40, $"the control: {hops} hops over the twelve brains");
    }

    [NotOnCiFact("a CPU-time bound measured on the owner's machine: a shared runner's slower CPU misses it, and make check holds it here")]
    public void ADecideCostsAtMostTheBound()
    {
        // The thread's CPU time; where a system has no such clock, the wall's.
        Func<double> clock = ThreadCpuClock.Micros() ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() * 1e6 / System.Diagnostics.Stopwatch.Frequency);
        double best = double.MaxValue;
        string parts = "";
        for (int trial = 0; trial < 5 && best > BoundMicros; trial++)
        {
            CostReport report = Replay(clock);
            if (report.PerStep < best)
                (best, parts) = (report.PerStep, CostTool.Table(report));
        }

        Assert.True(best <= BoundMicros, $"ai.brain.cpu.us {best:F1} µs a decide over {BoundMicros:F0}, the least of five replays:\n{parts}");
    }
}
