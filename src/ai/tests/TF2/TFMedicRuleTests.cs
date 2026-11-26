using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFMedicTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Medic's rules the Fable review found without a fact that fails without them (its section 3): the self-save, the retarget
/// wait, the momentary target that does not burn, the patient kept through a charge, the call's life, the quiet patient, the pop's
/// wait, the hands' reasons one by one, the unseen patient's seconds, the pop row, the counters at death and the flag carrier's
/// Über. Each fact holds the rule's arm and its other side.
/// </summary>
public sealed class TFMedicRuleTests
{
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId Soldier = new(4, 0x4000_0001), Heavy = new(5, 0x4000_0001), Demo = new(6, 0x4000_0001),
                                    Sniper = new(7, 0x4000_0001), Enemy = new(9, 0x4000_0001), Enemy2 = new(10, 0x4000_0001);

    private static AiVector East(float d) => Mid + new AiVector(d, 0f, 0f);

    private static bool PopsWithin(TFField f, int decides, Func<int, IntentCommand> step)
    {
        for (int k = 0; k < decides; k++)
        {
            if (Pops(f, step(k)))
                return true;
        }

        return false;
    }

    private static TFHud Whole(float charge = 1f, ActorId? beam = null, int health = 200, int most = 200, float releasing = 0f, TFMeter[]? extra = null)
    {
        ActorId on = beam ?? Soldier;
        TFHud h = Hud(charge: charge, beam: on.Slot, mates: [Panel(on, health, most, East(200f))], extra: extra);
        if (releasing <= 0f)
            return h;
        return new TFHud(h.Kit, [.. h.Meters.Select(m => m.Name == TFMedigunMeters.Releasing ? new TFMeter(m.Name, releasing) : m)], h.Mates);
    }

    // ------------------------------------------------------------------ the pop

    /// <summary>Under 50 health and hurt in the last second the Medic pops (stock's GetUberHealthThreshold); at 45 health hurt long ago it does not.</summary>
    [Fact]
    public void TheMedicJustHurtUnderFiftyPops()
    {
        TFField hurt = Field(Whole());
        hurt.StepDrawn(Me(Mid, health: 150), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
        bool popsHurt = PopsWithin(hurt, 6, _ => hurt.StepDrawn(Me(Mid, health: 45), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier)));

        TFField steady = Field(Whole());
        for (int k = 0; k < 15; k++)
            steady.StepDrawn(Me(Mid, health: 45), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
        bool popsSteady = PopsWithin(steady, 6, _ => steady.StepDrawn(Me(Mid, health: 45), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier)));
        Assert.Equal((true, false), (popsHurt, popsSteady));
    }

    /// <summary>The pop waits the bot's reaction (1 s here): not in the first 0.5 s, by 1.5 s.</summary>
    [Fact]
    public void ThePopWaitsTheReaction()
    {
        TFField f = Field(Whole(health: 80), tune: p => p with { ReactionMs = 1000f });
        bool early = PopsWithin(f, 5, _ => f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier)));
        bool late = PopsWithin(f, 12, _ => f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier)));
        Assert.Equal((false, true), (early, late));
    }

    /// <summary>The reaction's wait is not begun again by a patient hovering at half health (stock's timer runs on): wanted every other decide, the pop still goes within 1.5 s.</summary>
    [Fact]
    public void AHoveringPatientDoesNotRestartThePopsWait()
    {
        TFField f = Field(Whole(health: 80), tune: p => p with { ReactionMs = 1000f });
        bool pops = PopsWithin(f, 17, k =>
        {
            f.Hud = Whole(health: k % 2 == 0 ? 80 : 120);
            return f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
        });
        Assert.True(pops);
    }

    /// <summary>A patient already invulnerable at 40 % is not ubered again; the same patient not invulnerable is.</summary>
    [Fact]
    public void AnInvulnerablePatientIsNotUberedAgain()
    {
        bool Pops(ActorConditions c)
        {
            TFField f = Field(Whole(health: 80));
            return PopsWithin(f, 8, _ => f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier, c)));
        }

        Assert.Equal((true, false), (Pops(ActorConditions.None), Pops(ActorConditions.Invulnerable)));
    }

    /// <summary>The trade reaches 1,500 u: an enemy invulnerable at 1,600 u pops nothing.</summary>
    [Fact]
    public void TheTradeHasItsReach()
    {
        bool Trades(float at)
        {
            TFField f = Field(Whole(beam: Soldier, health: 200));
            return PopsWithin(f, 8, _ => f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-200f), TFClass.Soldier), Foe(Enemy, East(at), ActorConditions.Invulnerable)));
        }

        Assert.Equal((true, false), (Trades(1400f), Trades(1600f)));
    }

    /// <summary>The push counts enemies within 900 u of the patient: two at 950 and 1,000 u from him do not pop it; at 700 and 750 they do.</summary>
    [Fact]
    public void ThePushHasItsReach()
    {
        bool Pushes(float a, float b)
        {
            TFField f = Field(Whole(health: 200));
            return PopsWithin(f, 8, _ => f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier, activity: ActorActivity.Firing),
                                                    Foe(Enemy, East(200f + a)), Foe(Enemy2, East(200f + b))));
        }

        Assert.Equal((true, false), (Pushes(700f, 750f), Pushes(950f, 1000f)));
    }

    /// <summary>The pop row presses both triggers for longer than a decide period, so the attack is held into the next decide's beam (the Fable review, C5).</summary>
    [Fact]
    public void ThePopRowHoldsBothTriggersOverADecide()
    {
        AbilityRow row = TFMedicClass.Abilities().Single(r => r.Name == TFMedicClass.Pop);
        Assert.Equal((AbilityRun.Buttons, (int)(SourceSharp.Abi.InputButtons.Attack | SourceSharp.Abi.InputButtons.Attack2)), (row.Run, row.Buttons));
        Assert.True(row.HoldTicks > DecideSchedule.Period, $"held {row.HoldTicks} ticks");
    }

    /// <summary>
    /// With the charge ready, a teammate carrying the enemy's flag is not the one chosen (he gets nothing from a charge): a Soldier beside
    /// him is. Without a charge the carrier is.
    /// </summary>
    [Fact]
    public void AReadyMedicLeavesTheCarrierForHisEscort()
    {
        ActorId Patient(float charge)
        {
            TFField f = Field(Hud(charge: charge));
            return f.StepDrawn(Me(Mid), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: East(150f), blueCarrier: Sniper.Slot),
                               Mate(Sniper, East(150f), TFClass.Sniper), Mate(Soldier, East(250f), TFClass.Soldier)).Look.Actor;
        }

        Assert.Equal((Sniper, Soldier), (Patient(0.3f), Patient(1f)));
    }

    // ------------------------------------------------------------------ the beam

    /// <summary>
    /// With the medigun on someone else, the trigger is let go one decide, then held 1 to 2 s on the one wanted (stock's change-patient
    /// timer): in 12 decides one let-go, the first.
    /// </summary>
    [Fact]
    public void TheRetargetLetsGoOnceThenHolds()
    {
        TFField f = Field(Hud(beam: Demo.Slot));
        ActionKind[] kinds = [.. Enumerable.Range(0, 12).Select(_ => f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier)).Action.Kind)];
        Assert.Equal(ActionKind.None, kinds[0]);
        Assert.All(kinds[1..], k => Assert.Equal(ActionKind.Fire, k));
    }

    private static IntentCommand Momentary(TFMateHealth[] extraMates, ActorActivity patient = ActorActivity.None, TFMeter[]? meters = null, float mateAt = -200f, int steps = 40)
    {
        TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(300f)), .. extraMates], extra: meters));
        IntentCommand i = default;
        for (int k = 0; k < steps; k++)
            i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(300f), TFClass.Soldier, activity: patient), Mate(Demo, East(mateAt), TFClass.Demoman));
        return i;
    }

    /// <summary>A stable patient, a Demoman the HUD flags calling 200 u off: the beam goes to him for the moment.</summary>
    [Fact]
    public void ACallingTeammateNearGetsTheMomentaryBeam()
        => Assert.Equal(Demo, Momentary([new TFMateHealth(Demo.Slot, -1, -1, TFMateSeen.Calling, East(-200f))]).Look.Actor);

    /// <summary>The same calling Demoman, with the patient firing (in a fight): the beam stays on the patient.</summary>
    [Fact]
    public void InAFightOnlyABurningTeammateGetsTheMomentaryBeam()
        => Assert.Equal(Soldier, Momentary([new TFMateHealth(Demo.Slot, -1, -1, TFMateSeen.Calling, East(-200f))], ActorActivity.Firing).Look.Actor);

    /// <summary>A calling Demoman 500 u off (past 0.9 of the beam's 450 u): the beam stays on the patient.</summary>
    [Fact]
    public void AMomentaryTargetIsWithinTheBeamsReach()
        => Assert.Equal(Soldier, Momentary([new TFMateHealth(Demo.Slot, -1, -1, TFMateSeen.Calling, East(-500f))], mateAt: -500f).Look.Actor);

    /// <summary>A Demoman the target ID shows hurt (100 of 175) gets the momentary beam; shown whole (175 of 175), not.</summary>
    [Fact]
    public void TheTargetIdShowsAHurtTeammate()
    {
        TFMeter[] Shown(int health) => [new TFMeter(TFTargetIdMeters.Slot, Demo.Slot), new TFMeter(TFTargetIdMeters.Health, health), new TFMeter(TFTargetIdMeters.Most, 175)];
        Assert.Equal((Demo, Soldier), (Momentary([], meters: Shown(100)).Look.Actor, Momentary([], meters: Shown(175)).Look.Actor));
    }

    // ------------------------------------------------------------------ the patient

    /// <summary>While the charge runs the patient is kept: a Heavy 200 u off does not take the Demoman's place; with no charge running he does.</summary>
    [Fact]
    public void ThePatientIsKeptWhileTheChargeRuns()
    {
        ActorId Patient(float releasing)
        {
            TFField f = Field(Hud(charge: 0.5f, beam: Demo.Slot, mates: [Panel(Demo, 175, 175, East(300f))]));
            f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Demo, East(300f), TFClass.Demoman));
            f.Hud = Whole(charge: 0.5f, beam: Demo, health: 175, most: 175, releasing: releasing);
            return f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Demo, East(300f), TFClass.Demoman), Mate(Heavy, East(-200f), TFClass.HeavyWeapons)).Look.Actor;
        }

        Assert.Equal((Demo, Heavy), (Patient(1f), Patient(0f)));
    }

    /// <summary>A Sniper's call is answered until the heal-target panel shows him whole; then the Soldier is the patient again.</summary>
    [Fact]
    public void ACallIsAnsweredUntilTheCallerIsWhole()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier));
        ActorId called = f.StepHearing(Me(Mid), TFField.Ctf(), [MedicCall(Sniper, East(300f), f.Tick + 1)], Mate(Soldier, East(-300f), TFClass.Soldier), Mate(Sniper, East(300f), TFClass.Sniper)).Look.Actor;
        f.Hud = Hud(beam: Sniper.Slot, mates: [Panel(Sniper, 125, 125, East(300f))]);
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier), Mate(Sniper, East(300f), TFClass.Sniper));
        ActorId after = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier), Mate(Sniper, East(300f), TFClass.Sniper)).Look.Actor;
        Assert.Equal((Sniper, Soldier), (called, after));
    }

    /// <summary>An unanswered call lasts 8 s: the Sniper is the patient 5 s after it, the Soldier 9 s after.</summary>
    [Fact]
    public void ACallLastsEightSeconds()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier));
        f.StepHearing(Me(Mid), TFField.Ctf(), [MedicCall(Sniper, East(300f), f.Tick + 1)], Mate(Soldier, East(-300f), TFClass.Soldier), Mate(Sniper, East(300f), TFClass.Sniper));
        int decides = (int)MathF.Ceiling(5f / (DecideSchedule.Period * 0.015f));
        ActorId at5 = default, at9 = default;
        for (int k = 1; k <= 2 * decides; k++)
        {
            ActorId look = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier), Mate(Sniper, East(300f), TFClass.Sniper)).Look.Actor;
            if (k == decides)
                at5 = look;
            at9 = look;
        }

        Assert.Equal((Sniper, Soldier), (at5, at9));
    }

    /// <summary>A patient out of sight, not reached, is gone after for 8 s: still the patient after 5 s, given up after 9.</summary>
    [Fact]
    public void AnUnseenPatientIsKeptEightSeconds()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(600f), TFClass.Soldier));
        int decides = (int)MathF.Ceiling(5f / (DecideSchedule.Period * 0.015f));
        string at5 = "", at9 = "";
        for (int k = 1; k <= 2 * decides; k++)
        {
            f.StepDrawn(Me(Mid), TFField.Ctf());
            if (k == decides)
                at5 = f.Decider.Doing.Verdict;
            at9 = f.Decider.Doing.Verdict;
        }

        Assert.Equal("patient", at5);
        Assert.NotEqual("patient", at9);
    }

    // ------------------------------------------------------------------ the place

    /// <summary>The patient quiet for over 5 s, facing east, the Medic in front of him (east, 150 u): it goes behind him; with him firing, it holds.</summary>
    [Fact]
    public void BehindAQuietPatient()
    {
        (MoveKind Kind, float GoalX) Moves(ActorActivity a)
        {
            TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(-150f))]));
            IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-150f), TFClass.Soldier, activity: a, yaw: 0f));
            return (i.Move.Kind, f.Decider.Doing.Goal.X);
        }

        (MoveKind quietKind, float quietX) = Moves(ActorActivity.None);
        Assert.Equal(MoveKind.Follow, quietKind);
        Assert.True(quietX < East(-150f).X, $"goal x {quietX}");
        Assert.Equal(MoveKind.Hold, Moves(ActorActivity.Firing).Kind);
    }

    // ------------------------------------------------------------------ the hands

    private static IntentCommand HandsCase(ActorFacts patient, float enemyAt, TFHud? hud = null)
    {
        TFField f = Field(hud ?? Hud());
        return f.StepDrawn(Me(Mid), TFField.Ctf(), patient, Foe(Enemy, East(enemyAt)));
    }

    private static bool SelectsPrimary(in IntentCommand i) => i.Action.Kind == ActionKind.SelectWeapon && i.Action.WeaponSlot == EngineBotSelf.ByKind + Syringe;

    /// <summary>Threatened only: the patient 300 u off in sight, an enemy 200 u off, nearer: the primary.</summary>
    [Fact]
    public void ANearerEnemyTakesThePrimaryOut()
        => Assert.True(SelectsPrimary(HandsCase(Mate(Soldier, East(-300f), TFClass.Soldier), 200f)));

    /// <summary>Far only: the patient 700 u off in sight, an enemy 900 u off: the primary; the patient at 500 u: the medigun stays.</summary>
    [Fact]
    public void AFarPatientTakesThePrimaryOut()
        => Assert.Equal((true, false), (SelectsPrimary(HandsCase(Mate(Soldier, East(-700f), TFClass.Soldier), 900f)),
                                         SelectsPrimary(HandsCase(Mate(Soldier, East(-500f), TFClass.Soldier), 900f))));

    /// <summary>Obscured only: the patient 300 u off out of sight (remembered), an enemy 900 u off: the primary.</summary>
    [Fact]
    public void AnUnseenPatientTakesThePrimaryOut()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier));
        Assert.True(SelectsPrimary(f.StepDrawn(Me(Mid), TFField.Ctf(), Foe(Enemy, East(900f)))));
    }

    /// <summary>With the beam on someone, or the charge ready, the medigun stays out though an enemy is nearer than the patient.</summary>
    [Fact]
    public void HealingOrReadyKeepsTheMedigun()
    {
        IntentCommand healing = HandsCase(Mate(Soldier, East(-300f), TFClass.Soldier), 200f, Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(-300f))]));
        // Ready: the first decide says "ÜberCharge ready!"; the next is the hands'.
        TFField r = Field(Hud(charge: 1f));
        r.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier), Foe(Enemy, East(200f)));
        IntentCommand ready = r.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier), Foe(Enemy, East(200f)));
        Assert.Equal((false, false), (SelectsPrimary(healing), SelectsPrimary(ready)));
    }

    // ------------------------------------------------------------------ the counters

    /// <summary>A Medic dying with 95 % of a charge has dropped an Über; with 50 % it has not.</summary>
    [Fact]
    public void DyingWithAFullBarIsADroppedUber()
    {
        long Dropped(float charge)
        {
            TFField f = Field(Hud(charge: charge));
            f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
            f.StepDrawn(Me(Mid) with { Flags = SelfFlags.None, Health = 0 }, TFField.Ctf());
            return f.Decider.ClassCounters().Single(c => c.Key == "medic.dropped").Value;
        }

        Assert.Equal((1L, 0L), (Dropped(0.95f), Dropped(0.5f)));
    }

    /// <summary>An Über is counted when the HUD shows the charge going (the releasing meter rising), once, not on each send.</summary>
    [Fact]
    public void AnUberIsCountedWhenTheChargeGoes()
    {
        TFField f = Field(Whole(health: 200));
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
        f.Hud = Whole(health: 200, releasing: 1f);
        for (int k = 0; k < 5; k++)
            f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
        Assert.Equal(1L, f.Decider.ClassCounters().Single(c => c.Key == "medic.ubers").Value);
    }
}
