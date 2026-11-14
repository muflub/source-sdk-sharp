using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot on its way with nothing to look at looks where its team's players looked (plan_bots_teams_tf2_part_g
/// G.4, lane G4a), on the synthetic field (<see cref="TFField"/>): the red Soldier runs east to the blue flag at
/// 300 u/s across a stretch of bins seeded with a yaw of 90 (north, off its way), and its view is moved tick by
/// tick by the motion layer as the applier moves it (<see cref="HumanMotion"/>: a reaction to a new stimulus,
/// then a flick and a pursuit). The Hard bot's chance is its TF2 glance rate's (Alertness 6, 0.26): 0.65.
/// </summary>
public sealed class TFLearnedLookTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private const float TickSeconds = 0.015f, Speed = 300f, SeededYaw = 90f;

    /// <summary>The seeded stretch: bins 32 to 47 (x 2,048 to 3,072) of the bot's row, y bin 4.</summary>
    private const int FirstBin = 32, LastBin = 47;

    private static readonly ExperienceKey Key = new("tf_field", "0123456789abcdef", "ctf");

    /// <summary>A snapshot whose look rows say <paramref name="role"/>'s players faced <paramref name="yaw"/> across the stretch, 20 s a bin.</summary>
    private static MapExperience Looks(int role = TFField.Red, float yaw = SeededYaw, double seconds = 20d)
    {
        var rows = new ExperienceDelta();
        for (int x = FirstBin; x <= LastBin; x++)
            rows.Add(new LookKey(new ExperienceContext(0, role, 3), new WorldBin(x, 4, 0), LookBuckets.Yaw(yaw), 1), new Tally(seconds));
        return new MapExperience(Key, 1, rows);
    }

    private static TFField Runner(MapExperience experience, ulong seed = 7, float glanceRate = 0.26f)
    {
        var f = new TFField(seed: seed, graph: false, tune: p => p with { GlanceRate = glanceRate });
        f.Memory.World.ExperienceSlot.Current = experience;
        return f;
    }

    /// <summary>What a run across the field did: the look of each decide and the share of the stretch's ticks the view faced the seeded yaw.</summary>
    private sealed record Run(List<IntentCommand> Intents, double FacedShare, int StretchTicks);

    /// <summary>
    /// Runs the bot east from x 1,800 to 3,300 at <see cref="Speed"/>, a decide every
    /// <see cref="DecideSchedule.Period"/> ticks, the view stepped every tick toward the intent's look as the
    /// applier does (a path look along the way, east; a point by its angles, a new stimulus waiting the reaction).
    /// </summary>
    private static Run Cross(TFField f, Func<int, (ActorId, AiVector, AiVector, int)[]>? sighted = null)
    {
        var motion = new HumanMotion(MotionCurves.For(SkillPresets.TryGet("hard", out SkillProfile? p) ? p! : throw new InvalidOperationException(), f.Params),
                                     new AiRandom(99));
        var intents = new List<IntentCommand>();
        var at = new AiVector(1800f, 288f, 0f);
        IntentCommand intent = default;
        int faced = 0, stretch = 0, decide = 0;
        for (int t = 0; at.X < 3300f; t++)
        {
            if (t % DecideSchedule.Period == 0)
            {
                SelfState self = TFField.Self(at, velocity: new AiVector(Speed, 0f, 0f)) with { Eye = motion.View };
                intent = f.Step(self, TFField.Ctf(), sighted?.Invoke(decide) ?? []);
                intents.Add(intent);
                decide++;
            }

            AiVector eye = at + new AiVector(0f, 0f, 68f);
            (AiAngles target, int stimulus) = intent.Look.Kind switch
            {
                LookKind.Path => (new AiAngles(0f, 0f, 0f), 0),
                LookKind.Watch => (Angles(eye, intent.Look.Point), HashCode.Combine((int)LookKind.Watch, intent.Look.Ticks) | 1),
                _ => (Angles(eye, intent.Look.Point), HashCode.Combine((int)intent.Look.Kind, (int)(intent.Look.Point.X / 8f), (int)(intent.Look.Point.Y / 8f)) | 1),
            };
            AiAngles view = motion.Step(target, stimulus, TickSeconds);
            int bin = (int)MathF.Floor(at.X / WorldBin.Size);
            if (bin is >= FirstBin and <= LastBin)
            {
                stretch++;
                faced += MathF.Abs(HumanMotion.AngleDelta(view.Yaw, SeededYaw)) <= LearnedLook.FacedDegrees ? 1 : 0;
            }

            at += new AiVector(Speed * TickSeconds, 0f, 0f);
        }

        return new Run(intents, stretch == 0 ? 0d : (double)faced / stretch, stretch);
    }

    private static AiAngles Angles(AiVector eye, AiVector point)
    {
        AiVector d = point - eye;
        return new AiAngles(-MathF.Atan2(d.Z, d.Length2D) * (180f / MathF.PI), MathF.Atan2(d.Y, d.X) * (180f / MathF.PI), 0f);
    }

    [Fact]
    public void ABotCrossingSeededBinsFacesTheSeededYawForAtLeastTwoFifthsOfItsCrossing()
    {
        Run run = Cross(Runner(Looks()));
        Assert.True(run.StretchTicks > 200, $"the control: {run.StretchTicks} ticks on the stretch");
        Assert.True(run.FacedShare >= 0.4, $"faced the seeded yaw for {run.FacedShare:P0} of the crossing");
    }

    /// <summary>
    /// Over seeds the share holds: it is the chance (0.65) less the turns, not one seed's luck. One crossing is
    /// about three periods, so one seed's share runs from a few percent to nine in ten (seeds 1 to 12 read 6 % to
    /// 89 %, mean 50 %); the mean of 24 is the property.
    /// </summary>
    [Fact]
    public void TheShareHoldsOverSeeds()
    {
        double[] shares = [.. Enumerable.Range(1, 24).Select(s => Cross(Runner(Looks(), seed: (ulong)s)).FacedShare)];
        Assert.True(shares.Average() >= 0.4, $"mean {shares.Average():P0}: {string.Join(" ", shares.Select(s => s.ToString("P0")))}");
    }

    [Fact]
    public void WithAnEmptyStoreTheBotLooksAlongItsPathAsBefore()
    {
        Run run = Cross(Runner(MapExperience.Empty));
        Assert.All(run.Intents, i => Assert.True(i.Look.Kind is LookKind.Path or LookKind.Glance, $"{i.Look.Kind}"));
        Assert.True(run.FacedShare < 0.1, $"{run.FacedShare:P0}");
        Assert.Equal(0, Runner(MapExperience.Empty).Decider.LearnedLookDecides);
    }

    /// <summary>
    /// Every decide's intent is the same with the empty store as with a store whose looks are the other team's
    /// (or elsewhere): a look row this bot cannot use draws nothing from its stream and changes nothing.
    /// </summary>
    [Fact]
    public void LooksTheBotCannotUseChangeNoIntent()
    {
        List<IntentCommand> empty = Cross(Runner(MapExperience.Empty)).Intents;
        TFField blue = Runner(Looks(role: TFField.Blue));
        Assert.Equal(empty, Cross(blue).Intents);
        Assert.Equal(0, blue.Decider.LearnedLooks.Periods);
        // A yaw behind the bot's way (180, more than 100 off east) is not its look either.
        Assert.Equal(empty, Cross(Runner(Looks(yaw: 180f))).Intents);
        // The control: its own team's looks do change them.
        Assert.NotEqual(empty, Cross(Runner(Looks())).Intents);
    }

    [Fact]
    public void ATargetInSightTakesTheViewAtOnce()
    {
        // The same seed decides alike up to the enemy: it steps into sight on the decide after the first learned look.
        int first = Cross(Runner(Looks())).Intents.FindIndex(i => i.Look.Kind == LookKind.Watch);
        Assert.True(first > 0, "the control: a learned look");
        TFField f = Runner(Looks());
        Run run = Cross(f, d => d > first ? [(Enemy, new AiVector(3600f, 288f, 0f), AiVector.Zero, TFField.Blue)] : []);
        Assert.Equal(LookKind.Watch, run.Intents[first].Look.Kind);
        Assert.All(run.Intents.Skip(first + 1), i => Assert.Equal(LookKind.Actor, i.Look.Kind));
    }

    [Fact]
    public void ALearnedYawIsHeldForAPeriodOfPointSixToOnePointFiveSeconds()
    {
        TFField f = Runner(Looks());
        List<IntentCommand> intents = Cross(f).Intents;
        // Runs of Watch decides: each a period or more (consecutive learned periods of one yaw join), never under 0.6 s.
        var lengths = new List<int>();
        int n = 0;
        foreach (IntentCommand i in intents.Append(default))
        {
            if (i.Look.Kind == LookKind.Watch)
                n++;
            else if (n > 0)
            {
                lengths.Add(n);
                n = 0;
            }
        }

        float decideSeconds = DecideSchedule.Period * TickSeconds;
        Assert.NotEmpty(lengths);
        // The last run may be cut short where the stretch ends.
        Assert.All(lengths.SkipLast(1), l => Assert.True(l * decideSeconds >= 0.6f - decideSeconds, $"{l} decides"));
        Assert.True(f.Decider.LearnedLooks.Periods >= 2, $"{f.Decider.LearnedLooks.Periods} periods");
    }

    [Fact]
    public void TheSameSeedLooksAlikeAndAnotherSeedDoesNot()
    {
        Assert.Equal(Cross(Runner(Looks(), seed: 7)).Intents, Cross(Runner(Looks(), seed: 7)).Intents);
        Assert.NotEqual(Cross(Runner(Looks(), seed: 7)).Intents, Cross(Runner(Looks(), seed: 8)).Intents);
    }

    [Fact]
    public void AnEasyBotFacesTheLearnedYawLessThanAHardOne()
    {
        double easy = Enumerable.Range(1, 8).Average(s => Cross(Runner(Looks(), seed: (ulong)s, glanceRate: 0.12f)).FacedShare);
        double hard = Enumerable.Range(1, 8).Average(s => Cross(Runner(Looks(), seed: (ulong)s)).FacedShare);
        Assert.True(easy < hard, $"easy {easy:P0}, hard {hard:P0}");
    }

    [Fact]
    public void TheDeciderCountsTheDecidesOnALearnedLook()
    {
        TFField f = Runner(Looks());
        List<IntentCommand> intents = Cross(f).Intents;
        Assert.Equal(intents.Count(i => i.Look.Kind == LookKind.Watch), f.Decider.LearnedLookDecides);
        Assert.InRange(f.Decider.LearnedLookFaced, 1, f.Decider.LearnedLookDecides);
    }
}
