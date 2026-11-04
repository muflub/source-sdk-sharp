using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// Crouching in a fight (plan_hl2mp_ai §5.4, ruling 2026-11-02: "bots should crouch as well
/// when shooting to make it hard to hit"): at range, holding position, sometimes, for a hold
/// a person's length; what scales it is Cover.
/// </summary>
public sealed class CrouchTests
{
    private const float DecideSeconds = 6 * 0.015f;
    private static readonly AiVector AtRange = new(700f, 0f, 0f);

    private static SkillProfile Coverer(int cover)
    {
        SkillProfile p = Duel.Preset("normal");
        return p.With(p.IndexOf("cover"), cover);
    }

    private static bool Crouched(in IntentCommand i)
        => i.Move.CrouchOn && i.Move.Kind is MoveKind.Crouch or MoveKind.Strafe;

    /// <summary>The crouching of a long fight at <paramref name="at"/>: each decide, whether it crouched, and whether it fired.</summary>
    private static (bool Crouched, bool Fired)[] Fight(SkillProfile profile, AiVector at, int decides = 3000, SelfState? self = null)
    {
        var d = new Duel(profile);
        SelfState me = self ?? d.Self();
        var seen = new (bool, bool)[decides];
        d.Step(me, [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        for (int k = 0; k < decides; k++)
        {
            // An endless clip: this is about the crouch, not the reload.
            IntentCommand i = d.Step(me, [(Duel.Opponent, at)]);
            seen[k] = (Crouched(i), i.Action.Kind == ActionKind.Fire);
        }

        return seen;
    }

    private static float Share((bool Crouched, bool Fired)[] seen) => seen.Count(s => s.Crouched) / (float)seen.Length;

    /// <summary>It does crouch, and it goes on shooting while it does: a crouch is part of the fire, not instead of it.</summary>
    [Fact]
    public void ABotAtRangeCrouchesSomeOfTheFightAndKeepsFiring()
    {
        (bool Crouched, bool Fired)[] seen = Fight(Coverer(10), AtRange);
        Assert.InRange(Share(seen), 0.02f, 0.9f);
        Assert.Contains(seen, s => s.Crouched && s.Fired);
        Assert.Contains(seen, s => !s.Crouched && s.Fired);
    }

    /// <summary>Cover is what scales it.</summary>
    [Fact]
    public void MoreCoverCrouchesMore()
    {
        float low = Share(Fight(Coverer(1), AtRange)), mid = Share(Fight(Coverer(5), AtRange)), high = Share(Fight(Coverer(10), AtRange));
        Assert.True(low < mid && mid < high, $"cover 1 {low:F3}, 5 {mid:F3}, 10 {high:F3}");
    }

    /// <summary>The share reads the curve: the fight's time crouched is about what the curve says, within a loose band.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(4)]
    public void TheTimeCrouchedIsAboutWhatTheCurveSays(int cover)
    {
        SkillProfile p = Coverer(cover);
        float wanted = new Duel(p).Params.CrouchShare;
        float got = Share(Fight(p, AtRange, 16000));
        Assert.InRange(got, wanted * 0.5f, wanted * 1.5f);
    }

    /// <summary>The key is held, then let go for a while: a hold under a person's shortest, or a gap under one, flickers and looks wrong.</summary>
    [Fact]
    public void AHoldAndAGapAreEachNoShorterThanTheirMinimum()
    {
        (bool Crouched, bool Fired)[] seen = Fight(Coverer(10), AtRange, 6000);
        int holds = 0, run = 0;
        bool? was = null;
        for (int k = 0; k < seen.Length; k++)
        {
            if (seen[k].Crouched == was)
            {
                run++;
                continue;
            }

            // The first run has no start, so it says nothing.
            if (was is not null && k > run)
            {
                float seconds = run * DecideSeconds;
                float least = was.Value ? Hl2mpDecider.CrouchMinHoldSeconds : Hl2mpDecider.CrouchMinGapSeconds;
                Assert.True(seconds >= least - DecideSeconds, $"{(was.Value ? "a hold" : "a gap")} of {seconds:F2} s at {k}");
                holds += was.Value ? 1 : 0;
            }

            was = seen[k].Crouched;
            run = 1;
        }

        Assert.True(holds >= 5, $"only {holds} holds to judge");
    }

    /// <summary>Some are quick dips and most are a stretch: not every crouch the same length.</summary>
    [Fact]
    public void SomeCrouchesAreDipsAndSomeAreStretches()
    {
        (bool Crouched, bool Fired)[] seen = Fight(Coverer(10), AtRange, 12000);
        var holds = new List<float>();
        int run = 0;
        foreach ((bool crouched, bool _) in seen)
        {
            if (crouched)
                run++;
            else if (run > 0)
            {
                holds.Add(run * DecideSeconds);
                run = 0;
            }
        }

        Assert.Contains(holds, h => h <= Hl2mpDecider.CrouchDipSeconds);
        Assert.Contains(holds, h => h > Hl2mpDecider.CrouchDipSeconds + 0.3f);
    }

    /// <summary>Close, a sidestep is worth more than a smaller body: no crouch.</summary>
    [Fact]
    public void AtCloseRangeItDoesNotCrouch()
    {
        var close = new AiVector(Hl2mpDecider.CloseRange - 20f, 0f, 0f);
        Assert.Equal(0f, Share(Fight(Coverer(10), close)));
    }

    /// <summary>In the air a crouch is a different jump: none until it lands.</summary>
    [Fact]
    public void InTheAirItDoesNotCrouch()
    {
        var d = new Duel(Coverer(10));
        SelfState air = d.Self() with { Velocity = new AiVector(0f, 0f, -200f) };
        Assert.Equal(0f, Share(Fight(Coverer(10), AtRange, 3000, air)));
    }

    /// <summary>Nothing to shoot, no crouch: on a hunt it is on its feet.</summary>
    [Fact]
    public void WithNoOneInSightItDoesNotCrouch()
    {
        var d = new Duel(Coverer(10));
        SelfState self = d.Self();
        d.Step(self, [(Duel.Opponent, AtRange)], d.SeeAt(Duel.Opponent, AtRange));
        d.Step(self, [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, AtRange, default));
        for (int k = 0; k < 1500; k++)
        {
            IntentCommand i = d.Step(self, []);
            Assert.False(Crouched(i), $"at decide {k}, doing {d.Decider.Doing.Leaf}");
        }
    }

    /// <summary>Closing a long range waits for a crouch and goes on after it: neither a stop for good, nor a crouched walk.</summary>
    [Fact]
    public void ACrouchStopsTheWalkAndTheWalkGoesOn()
    {
        var far = new AiVector(900f, 0f, 0f);
        var d = new Duel(Coverer(10));
        SelfState self = d.Self();
        d.Step(self, [(Duel.Opponent, far)], d.SeeAt(Duel.Opponent, far));
        int walking = 0, crouched = 0;
        for (int k = 0; k < 3000; k++)
        {
            IntentCommand i = d.Step(self, [(Duel.Opponent, far)]);
            walking += i.Move.Kind == MoveKind.Follow ? 1 : 0;
            crouched += i.Move.Kind == MoveKind.Crouch ? 1 : 0;
            Assert.False(i.Move.Kind == MoveKind.Follow && i.Move.CrouchOn, $"a crouched walk at {k}");
        }

        Assert.True(walking > 0 && crouched > 0, $"walking {walking}, crouched {crouched}");
    }

    /// <summary>It stops as soon as the fight moves it: a crouch is never what keeps a bot where it is.</summary>
    [Fact]
    public void ACrouchEndsTheDecideTheFightStops()
    {
        var d = new Duel(Coverer(10));
        SelfState self = d.Self();
        d.Step(self, [(Duel.Opponent, AtRange)], d.SeeAt(Duel.Opponent, AtRange));
        int k = 0;
        for (; k < 4000 && !d.Decider.Crouching; k++)
            d.Step(self, [(Duel.Opponent, AtRange)]);
        Assert.True(d.Decider.Crouching, "it never crouched");
        d.Step(self, [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, AtRange, default));
        for (int settle = 0; settle < 40; settle++)
            d.Step(self, []);
        Assert.False(d.Decider.Crouching);
    }

    /// <summary>The same seed crouches at the same decides, and a crouch comes out of no other stream: determinism (§7).</summary>
    [Fact]
    public void TheSameSeedCrouchesAtTheSameDecides()
    {
        (bool Crouched, bool Fired)[] one = Fight(Coverer(10), AtRange), two = Fight(Coverer(10), AtRange);
        Assert.Equal(one, two);
    }

    /// <summary>What a client draws: someone crouched has lower shoulders, so the aim drops to them, once settled.</summary>
    [Fact]
    public void AimAtACrouchedTargetGoesLower()
    {
        float Aim(ActorActivity activity)
        {
            var d = new Duel(Coverer(5));
            SelfState self = d.Self();
            IntentCommand i = default;
            for (int k = 0; k < 40; k++)
                i = d.StepSeeing(self, Duel.Opponent, AtRange, AiVector.Zero, activity, k == 0 ? d.SeeAt(Duel.Opponent, AtRange) : []);
            return i.Look.Point.Z;
        }

        StanceHeight shoulder = Hl2mpAimHeights.Shoulder;
        Assert.Equal(shoulder.Standing - shoulder.Crouched, Aim(ActorActivity.None) - Aim(ActorActivity.Crouched), 2);
    }
}
