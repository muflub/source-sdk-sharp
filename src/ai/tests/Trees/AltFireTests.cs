using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// The hands on the second trigger (plan_hl2mp_ai §5.4, ruling 2026-11-03), through the decider: the
/// reason from <see cref="Hl2mpAltFire"/>, a chance by Cunning, the view going first, the press.
/// </summary>
public sealed class AltFireTests
{
    private static readonly AiVector Mid = new(600f, 0f, 0f);

    private static Duel Cunning(int level, ulong seed = 3)
    {
        SkillProfile p = Duel.Preset("hard");
        return new Duel(p.With(p.IndexOf("cunning"), level), Duel.Bot, seed, null, null, null);
    }

    /// <summary>
    /// A fight against someone standing in the open, with every line asked of answered as given: how
    /// many decides of holding the second trigger, and the last intent that did.
    /// </summary>
    private static (int Presses, IntentCommand Last) Fight(Duel d, SelfState self, AiVector at, int decides, bool clear = true, ActorActivity activity = ActorActivity.None)
    {
        int presses = 0, answered = 0;
        IntentCommand last = default;
        for (int i = 0; i < decides; i++)
        {
            AiEvent[] events = i == 0 ? d.SeeAt(Duel.Opponent, at) : [];
            DoingRecord doing = d.Decider.Doing;
            if (doing.LineAsk > answered)
            {
                answered = doing.LineAsk;
                events = [.. events, AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 6), doing.LineAt, doing.LineAsk, clear)];
            }

            IntentCommand c = d.StepSeeing(self, Duel.Opponent, at, AiVector.Zero, activity, events);
            if (c.Action.Kind == ActionKind.AltFire)
            {
                presses++;
                last = c;
            }
        }

        return (presses, last);
    }

    private static SelfState Grenades(Duel d, int ammo = 3) => d.Self() with { AltAmmo = ammo };

    [Fact]
    public void ACamperGetsAGrenadeThroughTheSecondTrigger()
    {
        Duel d = Cunning(10);
        (int presses, IntentCommand last) = Fight(d, Grenades(d), Mid, 300);
        Assert.True(presses > 0, "no press in 300 decides of someone standing still");
        Assert.Equal(ActionKind.AltFire, last.Action.Kind);
        Assert.Equal(LookKind.Actor, last.Look.Kind);
    }

    /// <summary>A contact grenade drops 45 u over this flight: the view goes above the body by the arc, not at it.</summary>
    [Fact]
    public void AGrenadeIsAimedOnAnArc()
    {
        Duel d = Cunning(10);
        (_, IntentCommand last) = Fight(d, Grenades(d), Mid, 300);
        var eye = new AiVector(0f, 0f, 64f);
        AiVector body = Mid + new AiVector(0f, 0f, Hl2mpAimHeights.Body.Standing);
        AiVector arc = AimLead.Lob(eye, body, 1000f, 400f)!.Value;
        Assert.True(arc.Z > body.Z + 30f, $"the control: the arc is above the body ({arc.Z:F1} over {body.Z:F1})");
        Assert.InRange(last.Look.Point.Z, arc.Z - 12f, arc.Z + 12f);
    }

    [Fact]
    public void NothingInThePoolNoPress()
    {
        Duel d = Cunning(10);
        Assert.Equal(0, Fight(d, Grenades(d, 0), Mid, 300).Presses);
    }

    [Fact]
    public void NobodyLobsOneMidReload()
    {
        Duel d = Cunning(10);
        SelfState reloading = Grenades(d) with { Flags = SelfFlags.Alive | SelfFlags.Grounded | SelfFlags.Reloading };
        Assert.Equal(0, Fight(d, reloading, Mid, 300).Presses);
    }

    /// <summary>A grenade goes off on the first thing it touches: a line the game refuses is one it would have died on.</summary>
    [Fact]
    public void ALineRefusedMeansNoGrenade()
    {
        Duel d = Cunning(10);
        Assert.Equal(0, Fight(d, Grenades(d), Mid, 300, clear: false).Presses);
    }

    [Fact]
    public void AtItsFeetNoGrenadeHoweverSly()
    {
        Duel d = Cunning(10);
        Assert.Equal(0, Fight(d, Grenades(d), new AiVector(200f, 0f, 0f), 300).Presses);
    }

    /// <summary>The primary is off while the view goes up: a person does not hold both.</summary>
    [Fact]
    public void TheGunStopsWhileTheViewGoesToTheArc()
    {
        Duel d = Cunning(10);
        SelfState self = Grenades(d);
        int fireInAim = 0, goingDecides = 0, answered = 0;
        for (int i = 0; i < 300; i++)
        {
            AiEvent[] events = i == 0 ? d.SeeAt(Duel.Opponent, Mid) : [];
            DoingRecord doing = d.Decider.Doing;
            if (doing.LineAsk > answered)
            {
                answered = doing.LineAsk;
                events = [.. events, AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 6), doing.LineAt, doing.LineAsk, true)];
            }

            IntentCommand c = d.StepSeeing(self, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None, events);
            if (d.Decider.Doing.AltFire.Length > 0)
            {
                goingDecides++;
                fireInAim += c.Action.Kind == ActionKind.Fire ? 1 : 0;
            }
        }

        Assert.True(goingDecides > 0, "the control: it went for one");
        Assert.Equal(0, fireInAim);
    }

    /// <summary>The pool goes down: that was the shot, counted, and the hands leave it alone for a while.</summary>
    [Fact]
    public void AGrenadeThatWentIsCountedAndNotFollowedAtOnce()
    {
        Duel d = Cunning(10);
        SelfState full = Grenades(d, 3), less = Grenades(d, 2);
        int answered = 0;
        bool pressed = false;
        for (int i = 0; i < 300 && !pressed; i++)
        {
            AiEvent[] events = i == 0 ? d.SeeAt(Duel.Opponent, Mid) : [];
            DoingRecord doing = d.Decider.Doing;
            if (doing.LineAsk > answered)
            {
                answered = doing.LineAsk;
                events = [.. events, AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 6), doing.LineAt, doing.LineAsk, true)];
            }

            pressed = d.StepSeeing(full, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None, events).Action.Kind == ActionKind.AltFire;
        }

        Assert.True(pressed, "the control: it pressed");
        // The HUD says it went.
        d.StepSeeing(less, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None);
        Assert.Equal(1, d.Decider.AltFires);
        int afterwards = 0;
        for (int i = 0; i < (int)(7f / (6 * 0.015f)); i++)
        {
            AiEvent[] events = [];
            DoingRecord doing = d.Decider.Doing;
            if (doing.LineAsk > answered)
            {
                answered = doing.LineAsk;
                events = [AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 6), doing.LineAt, doing.LineAsk, true)];
            }

            afterwards += d.StepSeeing(less, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None, events).Action.Kind == ActionKind.AltFire ? 1 : 0;
        }

        Assert.Equal(0, afterwards);
    }

    /// <summary>Cunning is the skill: knowing when a grenade pays. Over the same fights, more of it presses more often.</summary>
    [Fact]
    public void CunningPressesMore()
    {
        double Goes(int level)
        {
            int goes = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                Duel d = Cunning(level, seed);
                // Never spent: how many times it would have gone for one, by its own count of chances taken.
                int answered = 0;
                bool going = false;
                SelfState self = Grenades(d);
                for (int i = 0; i < 600; i++)
                {
                    AiEvent[] events = i == 0 ? d.SeeAt(Duel.Opponent, Mid) : [];
                    DoingRecord doing = d.Decider.Doing;
                    if (doing.LineAsk > answered)
                    {
                        answered = doing.LineAsk;
                        events = [.. events, AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 6), doing.LineAt, doing.LineAsk, true)];
                    }

                    d.StepSeeing(self, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None, events);
                    bool now = d.Decider.Doing.AltFire.Length > 0;
                    goes += now && !going ? 1 : 0;
                    going = now;
                }
            }

            return goes;
        }

        double[] seen = [Goes(1), Goes(5), Goes(10)];
        Assert.True(seen[0] < seen[1] && seen[1] < seen[2], $"goes at Cunning 1, 5, 10: {string.Join(", ", seen)}");
    }

    /// <summary>The ruling's limit lives here: the curve reads a level, and Hard's Cunning stays low on it.</summary>
    [Fact]
    public void TheChanceRisesWithTheLevelAndHardsIsSmall()
    {
        float Chance(string preset) => Duel.Preset(preset) is var p ? SourceSharp.Ai.Tests.Skills.Curves.Game().For(p).AltFireChance : 0f;
        Assert.True(Chance("easy") < Chance("normal") && Chance("normal") < Chance("hard") && Chance("hard") < Chance("expert"),
                    $"easy {Chance("easy")}, normal {Chance("normal")}, hard {Chance("hard")}, expert {Chance("expert")}");
        Assert.InRange(Chance("hard"), 0.01f, 0.25f);
    }

    [Fact]
    public void BothBarrelsGoUpCloseWithTwoInTheClip()
    {
        Duel d = Cunning(10);
        var near = new AiVector(150f, 0f, 0f);
        SelfState two = d.Self(clip: 2, reserve: 6, weapon: Duel.Shotgun);
        Assert.True(Fight(d, two, near, 200).Presses > 0, "no double in 200 decides at 150 u");
    }

    [Fact]
    public void OneInTheClipIsNoDouble()
    {
        Duel d = Cunning(10);
        SelfState one = d.Self(clip: 1, reserve: 6, weapon: Duel.Shotgun);
        Assert.Equal(0, Fight(d, one, new AiVector(150f, 0f, 0f), 200).Presses);
    }

    [Fact]
    public void ABallNeedsItsPool()
    {
        Duel d = Cunning(10);
        SelfState ball = d.Self(weapon: Duel.Ar2) with { AltAmmo = 1 };
        Assert.True(Fight(d, ball, Mid, 200).Presses > 0, "no ball in 200 decides at someone standing in line");
        Duel dry = Cunning(10);
        Assert.Equal(0, Fight(dry, dry.Self(weapon: Duel.Ar2), Mid, 200).Presses);
    }

    /// <summary>
    /// The ball goes about 0.6 s after the press, where the eyes are then, and the pool shows it only then: the view
    /// stays on the lead, and nothing else is asked of the hands, however long the charge takes (a weapon's to say).
    /// </summary>
    [Fact]
    public void AChargingBallKeepsTheAimUntilThePoolSaysItWent()
    {
        Duel d = Cunning(10);
        SelfState ball = d.Self(weapon: Duel.Ar2) with { AltAmmo = 1 };
        var aim = default(IntentCommand);
        for (int i = 0; i < 400 && aim.Action.Kind != ActionKind.AltFire; i++)
            aim = d.StepSeeing(ball, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None, i == 0 ? d.SeeAt(Duel.Opponent, Mid) : []);
        Assert.Equal(ActionKind.AltFire, aim.Action.Kind);
        for (int i = 0; i < 8; i++)
        {
            IntentCommand during = d.StepSeeing(ball, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None);
            Assert.Equal(ActionKind.AltFire, during.Action.Kind);
            Assert.InRange(MathF.Abs(during.Look.Point.Y), 0f, 40f);
        }

        // Gone behind something mid-charge: it stays pressed, and on what it had.
        IntentCommand lost = d.Step(ball, []);
        Assert.Equal(ActionKind.AltFire, lost.Action.Kind);
        d.StepSeeing(ball with { AltAmmo = 0 }, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None);
        Assert.Equal(1, d.Decider.AltFires);
    }

    /// <summary>A frag this near goes underhand whoever holds it, and not overhand; and a roll wants the duck held.</summary>
    [Fact]
    public void AFragInHandRollsNotThrowsThisNear()
    {
        Duel d = Cunning(1);
        SelfState frag = d.Self(clip: 0, reserve: 1, weapon: Duel.Frag);
        var near = new AiVector(350f, 0f, 0f);
        bool ducked = false;
        int primary = 0, presses = 0;
        for (int i = 0; i < 60; i++)
        {
            IntentCommand c = d.StepSeeing(frag, Duel.Opponent, near, AiVector.Zero, ActorActivity.None, i == 0 ? d.SeeAt(Duel.Opponent, near) : []);
            primary += c.Action.Kind == ActionKind.Fire ? 1 : 0;
            presses += c.Action.Kind == ActionKind.AltFire ? 1 : 0;
            ducked |= c.Action.Kind == ActionKind.AltFire && c.Move.CrouchOn;
        }

        Assert.True(presses > 0, "no press in 60 decides at 350 u");
        Assert.True(ducked, "a roll is a duck held");
        Assert.Equal(0, primary);
    }

    [Fact]
    public void AFragIsNotThrownAtItsFeet()
    {
        Duel d = Cunning(10);
        SelfState frag = d.Self(clip: 0, reserve: 1, weapon: Duel.Frag);
        var feet = new AiVector(150f, 0f, 0f);
        for (int i = 0; i < 60; i++)
        {
            IntentCommand c = d.StepSeeing(frag, Duel.Opponent, feet, AiVector.Zero, ActorActivity.None, i == 0 ? d.SeeAt(Duel.Opponent, feet) : []);
            Assert.NotEqual(ActionKind.AltFire, c.Action.Kind);
            Assert.NotEqual(ActionKind.Fire, c.Action.Kind);
        }
    }

    /// <summary>Its own stream: a bot that never has the chance decides as it did, and the same seed goes at the same decides.</summary>
    [Fact]
    public void TheSameSeedGoesAtTheSameDecides()
    {
        int[] Run()
        {
            Duel d = Cunning(7);
            var at = new List<int>();
            int answered = 0;
            SelfState self = Grenades(d);
            for (int i = 0; i < 300; i++)
            {
                AiEvent[] events = i == 0 ? d.SeeAt(Duel.Opponent, Mid) : [];
                DoingRecord doing = d.Decider.Doing;
                if (doing.LineAsk > answered)
                {
                    answered = doing.LineAsk;
                    events = [.. events, AiEvent.LineAnswer(Duel.Bot, new Tick(d.Tick + 6), doing.LineAt, doing.LineAsk, true)];
                }

                if (d.StepSeeing(self, Duel.Opponent, Mid, AiVector.Zero, ActorActivity.None, events).Action.Kind == ActionKind.AltFire)
                    at.Add(i);
            }

            return [.. at];
        }

        int[] one = Run();
        Assert.NotEmpty(one);
        Assert.Equal(one, Run());
    }

    /// <summary>A grenade's lob over its flight: the point it aims at lands it on the target, under its gravity.</summary>
    [Theory]
    [InlineData(300f, 0f)]
    [InlineData(800f, 0f)]
    [InlineData(600f, 128f)]
    [InlineData(600f, -128f)]
    public void TheArcLandsWhereItIsAimed(float distance, float rise)
    {
        var from = new AiVector(0f, 0f, 64f);
        var target = new AiVector(distance, 0f, 64f + rise);
        AiVector point = AimLead.Lob(from, target, 1000f, 400f)!.Value;
        float tan = (point.Z - from.Z) / distance, cos = 1f / MathF.Sqrt(1f + tan * tan);
        float t = distance / (1000f * cos);
        float landed = from.Z + 1000f * (tan * cos) * t - 0.5f * 400f * t * t;
        Assert.Equal(target.Z, landed, 1);
    }

    [Fact]
    public void AnArcBeyondItsSpeedSaysSo() => Assert.Null(AimLead.Lob(default, new AiVector(5000f, 0f, 0f), 1000f, 400f));
}
