using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using TF2.Shared.Movement;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot's aim as a person's (plan_bots_teams_tf2_part_g G.2.1) on the synthetic field
/// (<see cref="TFField"/>): the eye its shots are led from is its own class's, a body is aimed at
/// its class's middle as the mod's hull has it, and that height is held through a crouch: the
/// crosshair stays where the body stood for the bot's reaction, then goes down to it.
/// </summary>
public sealed class TFAimTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f), At = new(2348f, 256f, 0f);

    private static readonly float Standing = TFViewVectors.Params.HullMax.Z / 2f, Ducked = TFViewVectors.Params.DuckHullMax.Z / 2f;

    // No aim error, and the shotgun (no flight to lead, no feet to aim at): a look's height is the hold's alone.
    private static TFField Field() => new(tune: p => p with { AimErrorDeg = 0f });

    private static SelfState Shotgunner => TFField.Self(Mid, weapon: TFField.Shotgun, clip: 6);

    private static ActorFacts Body(bool crouched, TFClass drawnAs = TFClass.Soldier)
        => new(Enemy, At, AiVector.Zero, TFField.Shotgun, crouched ? ActorActivity.Crouched : ActorActivity.None, 1f, TFField.Blue, (int)drawnAs);

    private static float AimZ(TFField f, bool crouched)
    {
        IntentCommand i = f.StepDrawn(Shotgunner, TFField.Ctf(), Body(crouched));
        Assert.Equal((LookKind.Actor, AimPoint.Body), (i.Look.Kind, i.Look.Aim));
        return i.Look.Point.Z;
    }

    [Theory]
    [InlineData(TFClass.Soldier, 68f)]
    [InlineData(TFClass.Scout, 65f)]
    [InlineData(TFClass.HeavyWeapons, 75f)]
    public void TheEyeAShotIsLedFromIsTheBotsOwnClasss(TFClass playerClass, float eye)
    {
        TFField f = Field();
        f.StepDrawn(TFField.Self(Mid, playerClass: playerClass), TFField.Ctf(), Body(false));
        Assert.Equal(Mid + new AiVector(0f, 0f, eye), f.Decider.OwnEye);
    }

    [Fact]
    public void ADuckedBotsEyeIsTheDuckViews()
    {
        TFField f = Field();
        f.StepDrawn(TFField.Self(Mid, extra: SelfFlags.Ducking), TFField.Ctf(), Body(false));
        Assert.Equal(Mid.Z + TFViewVectors.Params.DuckView.Z, f.Decider.OwnEye.Z);
        Assert.True(f.Decider.OwnEye.Z < Mid.Z + 60f, "the control: a duck view is lower than any class's eye");
    }

    /// <summary>How far a rocket at a strafing target's feet is led by a bot of <paramref name="playerClass"/>.</summary>
    private static float Lead(TFClass playerClass)
    {
        var f = new TFField(tune: p => p with { AimErrorDeg = 0f, LeadQuality = 1f });
        var strafing = new ActorFacts(Enemy, Mid + new AiVector(600f, 0f, 0f), new AiVector(0f, 300f, 0f), TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);
        IntentCommand i = f.StepDrawn(TFField.Self(Mid, playerClass: playerClass), TFField.Ctf(), strafing);
        Assert.Equal(AimPoint.Feet, i.Look.Aim);
        return i.Look.Point.Y - Mid.Y;
    }

    /// <summary>The wiring: a rocket's flight is timed from the class's eye, so a taller class's, further from the feet it aims at, leads more.</summary>
    [Fact]
    public void ARocketsLeadIsTimedFromTheClasssEye()
    {
        float scout = Lead(TFClass.Scout), heavy = Lead(TFClass.HeavyWeapons);
        Assert.True(scout > 100f, $"the control: the shot is led at all, by {scout:F2}");
        Assert.True(heavy > scout + 0.05f, $"heavy {heavy:F3}, scout {scout:F3}");
    }

    [Fact]
    public void AStandingBodyIsAimedAtItsHullsMiddle()
        => Assert.Equal(At.Z + Standing, AimZ(Field(), crouched: false), 3);

    /// <summary>
    /// TF2's hull middle drops 10 u in a duck, under what a hand has to re-aim for
    /// (<see cref="AimHeightHold.StimulusUnits"/>): the crosshair follows it down over a few decides, with no snap.
    /// </summary>
    [Fact]
    public void ACrouchIsFollowedDownAndNotSnappedTo()
    {
        Assert.True(Standing - Ducked < AimHeightHold.StimulusUnits, "the premise: a duck moves the middle less than a re-aim's stimulus");
        TFField f = Field();
        for (int i = 0; i < 5; i++)
            AimZ(f, crouched: false);

        Assert.InRange(AimZ(f, crouched: true), At.Z + Ducked + 2f, At.Z + Standing - 2f);
        float z = 0f;
        for (int i = 0; i < 10; i++)
            z = AimZ(f, crouched: true);
        Assert.Equal(At.Z + Ducked, z, 0.5f);
    }

    [Fact]
    public void AnEngagementOnACrouchedBodyStartsFromWhereItWouldStand()
        => Assert.InRange(AimZ(Field(), crouched: true), At.Z + Ducked + 2f, At.Z + Standing);

    [Fact]
    public void AfterALullTheNextEngagementStartsOverFromTheStandingHeight()
    {
        TFField f = Field();
        for (int i = 0; i < 20; i++)
            AimZ(f, crouched: true);
        Assert.Equal(At.Z + Ducked, AimZ(f, crouched: true), 0.5f);

        f.StepDrawn(Shotgunner, TFField.Ctf());
        f.StepDrawn(Shotgunner, TFField.Ctf());
        Assert.InRange(AimZ(f, crouched: true), At.Z + Ducked + 2f, At.Z + Standing);
    }

    private static float AimZAt(TFField f, float up)
        => f.StepDrawn(Shotgunner, TFField.Ctf(), Body(false) with { Origin = At + new AiVector(0f, 0f, up) }).Look.Point.Z;

    /// <summary>A body that leaves the height (a jump's top, a step up onto a crate) is not followed for the bot's reaction, then re-aimed at.</summary>
    [Fact]
    public void ABodyThatLeavesTheHeightIsNotFollowedForTheBotsReactionThenReAimedAt()
    {
        const float Up = 48f;
        TFField f = Field();
        for (int i = 0; i < 5; i++)
            AimZAt(f, 0f);

        // The decide it rises, and the next (0.18 s, under anyone's 0.25): the crosshair has not moved.
        Assert.Equal(At.Z + Standing, AimZAt(f, Up), 0.01f);
        Assert.Equal(At.Z + Standing, AimZAt(f, Up), 0.01f);
        Assert.Equal(AimHeightPhase.Reacting, f.Decider.AimHeightNow);

        int decides = 2;
        for (; decides < 40 && f.Decider.AimHeightNow != AimHeightPhase.Settled; decides++)
            AimZAt(f, Up);
        float z = 0f;
        for (int i = 0; i < 10; i++)
            z = AimZAt(f, Up);
        Assert.InRange(decides * DecideSchedule.Period * 0.015f, AimHeightHold.ReAimLeastSeconds, 1.5f);
        Assert.Equal(At.Z + Up + Standing, z, 1.5f);
    }

    // ---------------------------------------------------------------- the aim's error (AimWander)

    private const float ErrorDeg = 3f;

    private static TFField Erring(ulong seed = 7) => new(seed: seed, graph: false, tune: p => p with { AimErrorDeg = ErrorDeg });

    /// <summary>The aim's error across the line (the line runs along x, so across is y) on a body standing still, one decide.</summary>
    private static float Across(TFField f, ActorId? who = null)
        => f.StepDrawn(Shotgunner, TFField.Ctf(), Body(false) with { Id = who ?? Enemy }).Look.Point.Y - At.Y;

    private static float Correlation(float[] a, float[] b)
    {
        float ma = a.Average(), mb = b.Average(), ab = 0f, aa = 0f, bb = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            ab += (a[i] - ma) * (b[i] - mb);
            aa += (a[i] - ma) * (a[i] - ma);
            bb += (b[i] - mb) * (b[i] - mb);
        }

        return ab / MathF.Sqrt(aa * bb);
    }

    /// <summary>A crosshair drifts: one decide's error is close to the last one's, where a new draw each decide has no memory.</summary>
    [Fact]
    public void ConsecutiveDecidesErrorsAreCorrelated()
    {
        TFField f = Erring();
        var x = new float[600];
        for (int i = 0; i < x.Length; i++)
            x[i] = Across(f);
        Assert.True(x.Max() - x.Min() > 5f, $"the control: the aim errs, over {x.Max() - x.Min():F1} u");
        float r = Correlation(x[..^1], x[1..]);
        Assert.True(r > 0.9f, $"lag-1 correlation {r:F3}");
    }

    /// <summary>Over many bots, the first decide on a newly seen target misses by about 2.5 times what the decide three seconds in does.</summary>
    [Fact]
    public void TheFirstDecideOnANewlySeenTargetHasTheLargerError()
    {
        double first = 0, later = 0;
        const int Bots = 300, Decides = 34;
        for (ulong seed = 1; seed <= Bots; seed++)
        {
            TFField f = Erring(seed);
            for (int i = 0; i < Decides; i++)
            {
                float e = Math.Abs(Across(f));
                if (i == 0)
                    first += e;
                else if (i == Decides - 1)
                    later += e;
            }
        }

        Assert.True(later / Bots > 1f, $"the control: a settled aim still errs, by {later / Bots:F2} u a decide");
        Assert.InRange(first / later, 2.0, 3.6);
    }

    [Fact]
    public void TheErrorStartsOverOnAnotherTargetAndAfterADecideThatAimedAtNobody()
    {
        var other = new ActorId(6, 0x4000_0002);
        TFField f = Erring();
        for (int i = 0; i < 5; i++)
            Across(f);
        Assert.Equal(1, f.Decider.FreshAims);

        Across(f, other);
        Across(f, other);
        Assert.Equal(2, f.Decider.FreshAims);

        f.StepDrawn(Shotgunner, TFField.Ctf());
        f.StepDrawn(Shotgunner, TFField.Ctf());
        Across(f, other);
        Across(f, other);
        Assert.Equal(3, f.Decider.FreshAims);
    }

    /// <summary>The settled error is the skill's angle at the target's distance: 3 degrees at 300 u is 15.7 u a standard deviation, a sixth less smoothed.</summary>
    [Fact]
    public void TheSettledErrorIsTheSkillsAngleAtTheDistance()
    {
        TFField f = Erring();
        for (int i = 0; i < 40; i++)
            Across(f);
        double sum = 0;
        const int N = 3000;
        for (int i = 0; i < N; i++)
        {
            float e = Across(f);
            sum += e * e;
        }

        double sigma = Math.Tan(ErrorDeg * Math.PI / 180.0) * 300.0;
        Assert.InRange(Math.Sqrt(sum / N), 0.6 * sigma, 1.05 * sigma);
    }

    /// <summary>A blast at the feet errs along the floor only: no error lifts it off the ground or sinks it under.</summary>
    [Fact]
    public void ARocketAtTheFeetErrsAlongTheFloorOnly()
    {
        TFField f = Erring();
        AiVector at = Mid + new AiVector(600f, 0f, 0f);
        float spread = 0f;
        for (int i = 0; i < 30; i++)
        {
            IntentCommand c = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Body(false) with { Origin = at });
            Assert.Equal((AimPoint.Feet, at.Z + TFKnowledge.FeetAim), (c.Look.Aim, c.Look.Point.Z));
            spread = MathF.Max(spread, MathF.Abs(c.Look.Point.Y - at.Y));
        }

        Assert.True(spread > 1f, $"the control: the aim errs across, by up to {spread:F1} u");
    }

    /// <summary>The feet are the floor's: a crouch moves them nowhere, and nothing is held.</summary>
    [Fact]
    public void ARocketAtACrouchedGroundedTargetStillGoesToItsFeet()
    {
        TFField f = Field();
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Body(true) with { Origin = Mid + new AiVector(600f, 0f, 0f) });
        Assert.Equal((AimPoint.Feet, Mid.Z + TFKnowledge.FeetAim), (i.Look.Aim, i.Look.Point.Z));
    }
}
