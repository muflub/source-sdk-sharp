using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot's glances off its route (plan_bots_teams_tf2_part_g G.2.1) on the synthetic field
/// (<see cref="TFField"/>): on its way with nobody in sight it looks 50 to 100 degrees aside at
/// about its Alertness's rate a second, from its class's eye; never with an enemy in sight, and
/// never while a rocket jump's sequence has the view.
/// </summary>
public sealed class TFGlanceTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private const float DecideSeconds = DecideSchedule.Period * 0.015f;

    // No graph: no rocket jump, so the run to the enemy's flag is nothing but a run.
    private static TFField Runner(float glanceRate, bool graph = false) => new(graph: graph, tune: p => p with { GlanceRate = glanceRate });

    [Fact]
    public void OnItsWayWithNobodyInSightTheBotGlancesAtAboutItsRateASecond()
    {
        const int Decides = 20_000;
        TFField f = Runner(0.4f);
        int glancing = 0;
        for (int i = 0; i < Decides; i++)
            glancing += f.Step(TFField.Self(Mid), TFField.Ctf()).Look.Kind == LookKind.Glance ? 1 : 0;

        float perSecond = f.Decider.Glances / (Decides * DecideSeconds);
        // A little under the rate: no glance begins while one is held.
        Assert.InRange(perSecond, 0.3f, 0.4f);
        // Each is held 0.4 s, five decides.
        Assert.Equal(5 * f.Decider.Glances, glancing);
    }

    [Fact]
    public void AGlanceLooksFiftyToAHundredDegreesOffTheWayFromTheClasssEye()
    {
        TFField f = Runner(5f);
        int seen = 0;
        for (int i = 0; i < 400; i++)
        {
            IntentCommand c = f.Step(TFField.Self(Mid), TFField.Ctf());
            if (c.Look.Kind != LookKind.Glance)
                continue;
            seen++;
            // The way is east, to the enemy's flag.
            float off = MathF.Abs(MathF.Atan2(c.Look.Point.Y - Mid.Y, c.Look.Point.X - Mid.X)) * (180f / MathF.PI);
            Assert.InRange(off, 49.9f, 100.1f);
            Assert.Equal(Mid.Z + 68f, c.Look.Point.Z, 3);
        }

        Assert.True(seen > 100, $"the control: {seen} decides glancing");
        Assert.Equal(TFDecider.Fetch, f.Decider.Doing.Branch[(f.Decider.Doing.Branch.IndexOf(": ", StringComparison.Ordinal) + 2)..]);
    }

    /// <summary>Where each of the first 500 decides of a run looks, for a bot of <paramref name="seed"/>.</summary>
    private static AiVector[] Looks(ulong seed)
    {
        var f = new TFField(seed: seed, graph: false, tune: p => p with { GlanceRate = 1f });
        return [.. Enumerable.Range(0, 500).Select(_ => f.Step(TFField.Self(Mid), TFField.Ctf()).Look.Point)];
    }

    /// <summary>The glances are the bot's own, by its seed: a replay looks where the live bot looked, and another bot elsewhere.</summary>
    [Fact]
    public void TheSameSeedGlancesAlikeAndAnotherSeedDoesNot()
    {
        Assert.Equal(Looks(7), Looks(7));
        Assert.NotEqual(Looks(7), Looks(8));
    }

    [Fact]
    public void AtARateOfZeroTheBotLooksAlongItsWay()
    {
        TFField f = Runner(0f);
        for (int i = 0; i < 2000; i++)
            Assert.Equal(LookKind.Path, f.Step(TFField.Self(Mid), TFField.Ctf()).Look.Kind);
        Assert.Equal(0, f.Decider.Glances);
    }

    [Fact]
    public void WithAnEnemyInSightThereIsNoGlance()
    {
        TFField f = Runner(50f);
        for (int i = 0; i < 300; i++)
        {
            IntentCommand c = f.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, Mid + new AiVector(600f, 0f, 0f), AiVector.Zero, TFField.Blue));
            Assert.Equal(LookKind.Actor, c.Look.Kind);
        }

        Assert.Equal(0, f.Decider.Glances);
    }

    /// <summary>
    /// The jump's sequence pitches the view down and leaves its yaw where it is, so its rocket goes
    /// where the bot looks: no glance in the second before a jump may begin, nor for the sequence's 1.3 s.
    /// </summary>
    [Fact]
    public void ASoldierWhoseRocketJumpIsDueKeepsItsEyesOnItsWay()
    {
        TFField f = Runner(50f, graph: true);
        SelfState running = TFField.Self(Mid, velocity: new AiVector(240f, 0f, 0f));
        var looks = new List<(LookKind Look, ActionKind Action)>();
        for (int i = 0; i < 160; i++)
        {
            IntentCommand c = f.Step(running, TFField.Ctf());
            looks.Add((c.Look.Kind, c.Action.Kind));
        }

        int[] jumps = [.. Enumerable.Range(0, looks.Count).Where(i => looks[i].Action == ActionKind.Ability)];
        Assert.True(jumps.Length >= 2, $"the control: the bot rocket-jumps {jumps.Length} times on this run");
        int busy = (int)(TFDecider.RocketJumpBusySeconds / DecideSeconds), clear = (int)(TFDecider.GlanceClearSeconds / DecideSeconds);
        foreach (int jump in jumps)
        {
            for (int i = Math.Max(0, jump - clear); i < Math.Min(looks.Count, jump + busy); i++)
                Assert.Equal(LookKind.Path, looks[i].Look);
        }

        // Between two jumps it glances as anyone does.
        Assert.Contains(looks.Skip(jumps[0] + busy).Take(jumps[1] - clear - jumps[0] - busy), l => l.Look == LookKind.Glance);
    }

    /// <summary>The control for the fact above: a class with no jump of its own glances from the first decide of the same run.</summary>
    [Fact]
    public void AClassWithNoRocketJumpGlancesOnTheSameRun()
    {
        TFField f = Runner(50f, graph: true);
        SelfState running = TFField.Self(Mid, velocity: new AiVector(240f, 0f, 0f), playerClass: global::TF2.Shared.TFClass.Scout);
        Assert.Equal(LookKind.Glance, f.Step(running, TFField.Ctf()).Look.Kind);
    }
}
