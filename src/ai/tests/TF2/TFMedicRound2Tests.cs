using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Medic's round 2 (P7; round2.md §5): the respawn glow as a patient's place (S11), the beam held through a one-shot
/// measured (S9), the Vaccinator's resist kept on the threat and its bubble, and the Battle Medic, who heals less and fights
/// more. Each fact holds the control beside the rule.
/// </summary>
public sealed class TFMedicRound2Tests
{
    private const int Medigun = TFMedicTests.Medigun, Syringe = TFMedicTests.Syringe, Bonesaw = TFMedicTests.Bonesaw, Crossbow = 53;

    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId Soldier = new(4, 0x4000_0001), Enemy = new(9, 0x4000_0001), Enemy2 = new(10, 0x4000_0001);

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Medigun, "tf_weapon_medigun", 0, 0.1f, 0f, false, false, 0f),
        new WeaponFacts(Syringe, "tf_weapon_syringegun_medic", 40, 0.1f, 1.6f, true, false, 1000f, ProjectileGravity: 300f),
        new WeaponFacts(Bonesaw, "tf_weapon_bonesaw", 0, 0.8f, 0f, false, false, 0f),
        new WeaponFacts(Crossbow, "tf_weapon_crossbow", 1, 0.25f, 1.6f, true, false, 2400f, ProjectileGravity: 160f),
    ]);

    private static AiVector East(float d) => Mid + new AiVector(d, 0f, 0f);

    private static TFField Field(TFHud hud, ulong seed = 7) => new(weapons: Weapons, mates: [4, 5, 6, 7], seed: seed) { Hud = hud };

    private static SelfState Me(int health = 150, int weapon = Medigun) => TFField.Self(Mid, health: health, weapon: weapon, clip: 0, playerClass: TFClass.Medic);

    private static ActorFacts Patient() => TFMedicTests.Mate(Soldier, East(200f), TFClass.Soldier);

    /// <summary>An enemy of <paramref name="c"/> in sight, firing or not.</summary>
    private static ActorFacts Shooter(ActorId id, float east, TFClass c, bool firing = true)
        => new(id, East(east), AiVector.Zero, TFField.Launcher, firing ? ActorActivity.Firing : ActorActivity.None, 1f, TFField.Blue, (int)c);

    private static bool IsRow(TFField f, in IntentCommand i, string name) => TFMedicTests.RowName(f, i) == name;

    private static long Counter(TFField f, string key) => f.Decider.ClassCounters().Single(c => c.Key == key).Value;

    // ------------------------------------------------------------------ S11: the respawn glow

    /// <summary>
    /// Round 2's S11: the Medic's own respawn glow outlines a Soldier 1,500 u off, never seen: he is the patient, gone to, and the
    /// decide is counted; the control, with no glow shown, does not go "patient" anywhere.
    /// </summary>
    [Fact]
    public void ARespawnGlowShowsTheMedicAPatientOutOfSight()
    {
        AiVector there = East(1500f);
        TFField f = Field(TFMedicTests.Hud(mates: [new TFMateHealth(Soldier.Slot, -1, -1, TFMateSeen.Glowing, there)]));
        f.StepDrawn(Me(), TFField.Ctf());
        Assert.Equal("patient", f.Decider.Doing.Verdict);
        Assert.True(f.Decider.Doing.Goal.DistanceTo(there) < 64f, $"goal {f.Decider.Doing.Goal}");
        Assert.Equal(1L, Counter(f, "medic.glow_decides"));

        TFField g = Field(TFMedicTests.Hud());
        g.StepDrawn(Me(), TFField.Ctf());
        Assert.NotEqual("patient", g.Decider.Doing.Verdict);
    }

    /// <summary>
    /// The glow is proof of life (the Fable review, C4): a teammate the kill feed showed dead, never seen since, outlined by the Medic's
    /// own respawn glow 1,500 u off, is the patient gone to; the control, the same death with no glow shown, is no patient.
    /// </summary>
    [Fact]
    public void AGlowOutranksTheKillFeedsDeath()
    {
        AiVector there = East(1500f);
        string Verdict(bool glows)
        {
            TFField f = Field(TFMedicTests.Hud(mates: glows ? [new TFMateHealth(Soldier.Slot, -1, -1, TFMateSeen.Glowing, there)] : null));
            ActorId row = f.Memory.Board.Actor(Soldier.Slot).Id;   // the scoreboard's teammate in that slot
            f.StepHearing(Me(), TFField.Ctf(), [AiEvent.KillFeed(f.Bot, new Tick(f.Tick + 1), row, Enemy)]);
            Assert.True(f.Memory.Board.Of(row, make: false) is { Dead: true }, "the feed's death took");
            f.StepDrawn(Me(), TFField.Ctf());
            return f.Decider.Doing.Verdict;
        }

        Assert.Equal("patient", Verdict(glows: true));
        Assert.NotEqual("patient", Verdict(glows: false));
    }

    // ------------------------------------------------------------------ S9: the beam through a one-shot, measured

    /// <summary>
    /// "ÜberCharge ready!" said while beaming is a one-shot sent with the beam held (medic.beam_oneshots); it is a drop
    /// (medic.beam_drops) only when the next decide's HUD shows the beam gone while the Soldier is still in sight within its hold:
    /// with the panel still on him, none; with him gone from sight (dead, or run off), none either.
    /// </summary>
    [Fact]
    public void ADropIsABeamTheHudShowsGoneAfterAOneShot()
    {
        (long OneShots, long Drops) Run(bool beamGoes, bool stays = true)
        {
            TFField f = Field(TFMedicTests.Hud(charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
            IntentCommand i = default;
            for (int k = 0; k < 8 && !IsRow(f, i, TFMedicClass.VoiceUberReady); k++)
                i = f.StepDrawn(Me(), TFField.Ctf(), Patient());
            Assert.True(IsRow(f, i, TFMedicClass.VoiceUberReady), "the ready line was said");
            if (beamGoes)
                f.Hud = TFMedicTests.Hud(charge: 1f, beam: 0, mates: []);
            if (stays)
                f.StepDrawn(Me(), TFField.Ctf(), Patient());
            else
                f.StepDrawn(Me(), TFField.Ctf());
            return (Counter(f, "medic.beam_oneshots"), Counter(f, "medic.beam_drops"));
        }

        Assert.Equal(((1L, 0L), (1L, 1L), (1L, 0L)), (Run(beamGoes: false), Run(beamGoes: true), Run(beamGoes: true, stays: false)));
    }

    /// <summary>
    /// The drop measure tells the one-shots apart: "ÜberCharge ready!" is an ability's command (a voice line), the Vaccinator's resist
    /// a reload press; each counts in its own kind, and none in the other.
    /// </summary>
    [Fact]
    public void OneShotsAreCountedByKind()
    {
        TFField voice = Field(TFMedicTests.Hud(charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
        for (int k = 0; k < 3; k++)
            voice.StepDrawn(Me(), TFField.Ctf(), Patient());
        TFField reload = Field(Vacc(TFMedicClass.ResistBullet, charge: 0.1f));
        for (int k = 0; k < 3; k++)
            reload.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier));
        Assert.Equal((1L, 0L, 0L), (Counter(voice, "medic.oneshots_command"), Counter(voice, "medic.oneshots_reload"), Counter(voice, "medic.oneshots_buttons")));
        Assert.Equal((0L, 1L, 0L), (Counter(reload, "medic.oneshots_command"), Counter(reload, "medic.oneshots_reload"), Counter(reload, "medic.oneshots_buttons")));
    }

    /// <summary>
    /// The control beside the drop: a beam lost after a beamed decide with no one-shot counts in medic.beam_lost_plain (out of
    /// medic.beam_plain), not as a drop; held, it counts in beam_plain only.
    /// </summary>
    [Fact]
    public void ABeamLostWithoutAOneShotIsTheControlsLoss()
    {
        (long Plain, long Lost, long Drops) Run(bool beamGoes)
        {
            TFField f = Field(TFMedicTests.Hud(charge: 0.5f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 150, 200, East(200f))]));
            for (int k = 0; k < 3; k++)
                f.StepDrawn(Me(), TFField.Ctf(), Patient());
            if (beamGoes)
                f.Hud = TFMedicTests.Hud(charge: 0.5f, beam: 0, mates: []);
            f.StepDrawn(Me(), TFField.Ctf(), Patient());
            return (Counter(f, "medic.beam_plain"), Counter(f, "medic.beam_lost_plain"), Counter(f, "medic.beam_drops"));
        }

        Assert.Equal(((3L, 0L, 0L), (3L, 1L, 0L)), (Run(beamGoes: false), Run(beamGoes: true)));
    }

    // ------------------------------------------------------------------ the Vaccinator

    private static TFHud Vacc(int resist, float charge = 0.5f)
        => TFMedicTests.Hud(charge: charge, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))],
                            extra: [new TFMeter(TFTargetIdMeters.Resist, resist)],
                            secondary: new TFKitItem(TFLoadoutSlot.Secondary, 998, "tf_weapon_medigun", Medigun, [new TFOwnKitAttribute("set_charge_type", 3f)]));

    /// <summary>A decide on which the Medic was hit by the enemy (his health down to <paramref name="health"/>, the hit in his memory).</summary>
    private static IntentCommand Shot(TFField f, int health, params ActorFacts[] sighted)
        => f.StepHearing(Me(health: health), TFField.Ctf(), [AiEvent.Damaged(f.Bot, new Tick(f.Tick + 1), Enemy, 10f, new AiVector(1f, 0f, 0f), 0)], sighted);

    private static bool Reloads(in IntentCommand i) => i.Action.Kind == ActionKind.Reload && i.Action.OneShotSeq > 0;

    /// <summary>Soldiers firing in sight and the resist on bullets: one reload press (to blast); on blast already, none.</summary>
    [Fact]
    public void TheResistIsPressedOnToWhatTheEnemyDeals()
    {
        bool Pressed(int resist)
        {
            TFField f = Field(Vacc(resist));
            return Reloads(f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier), Shooter(Enemy2, 1000f, TFClass.Soldier)));
        }

        Assert.Equal((true, false), (Pressed(TFMedicClass.ResistBullet), Pressed(TFMedicClass.ResistBlast)));
    }

    /// <summary>The live measure: with Soldiers in sight a decide on bullets counts off the threat, one on blast on it.</summary>
    [Fact]
    public void TheResistIsCountedOnOrOffTheThreat()
    {
        (long On, long Off) Count(int resist)
        {
            TFField f = Field(Vacc(resist));
            f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier));
            return (Counter(f, "medic.resist_on_threat"), Counter(f, "medic.resist_off_threat"));
        }

        Assert.Equal(((0L, 1L), (1L, 0L)), (Count(TFMedicClass.ResistBullet), Count(TFMedicClass.ResistBlast)));
    }

    /// <summary>The patient burning wants the fire resist whatever is in sight: a Soldier firing and the resist on fire, no press; on blast, a press.</summary>
    [Fact]
    public void ABurningPatientWantsTheFireResist()
    {
        bool Pressed(int resist)
        {
            TFField f = Field(Vacc(resist));
            return Reloads(f.StepDrawn(Me(), TFField.Ctf(), TFMedicTests.Mate(Soldier, East(200f), TFClass.Soldier, ActorConditions.Burning),
                                       Shooter(Enemy, 900f, TFClass.Soldier)));
        }

        Assert.Equal((false, true), (Pressed(TFMedicClass.ResistFire), Pressed(TFMedicClass.ResistBlast)));
    }

    /// <summary>A Pyro and a Heavy in sight, the Pyro firing: fire (2) outweighs bullets (1); with neither firing, the bullet resist is kept.</summary>
    [Fact]
    public void FiringEnemiesCountDouble()
    {
        bool Pressed(bool pyroFires)
        {
            TFField f = Field(Vacc(TFMedicClass.ResistBullet));
            return Reloads(f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 600f, TFClass.Pyro, pyroFires),
                                       Shooter(Enemy2, 700f, TFClass.HeavyWeapons, firing: false), Shooter(new ActorId(11, 0x4000_0001), 750f, TFClass.Scout, firing: false)));
        }

        // Fire 2 against bullet 2: a tie keeps the resist shown; the Pyro not firing, bullets 3 against fire 1.
        Assert.Equal((false, false), (Pressed(true), Pressed(false)));
        TFField g = Field(Vacc(TFMedicClass.ResistBullet));
        IntentCommand i = g.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 600f, TFClass.Pyro), Shooter(Enemy2, 700f, TFClass.HeavyWeapons, firing: false));
        Assert.True(Reloads(i), "a firing Pyro (2) over a Heavy who does not fire (1)");
    }

    /// <summary>A Vaccinator Medic who dies with half a bar counts no bubble at his next life's first decide, whose bar is empty (the Fable review, C7).</summary>
    [Fact]
    public void ADeathIsNoBubble()
    {
        TFField f = Field(Vacc(TFMedicClass.ResistBlast, charge: 0.5f));
        f.StepDrawn(Me(), TFField.Ctf(), Patient());
        f.StepDrawn(Me() with { Flags = SelfFlags.None, Health = 0 }, TFField.Ctf());
        f.Hud = Vacc(TFMedicClass.ResistBlast, charge: 0f);
        f.StepDrawn(Me(), TFField.Ctf(), Patient());
        Assert.Equal(0L, Counter(f, "medic.bubbles"));
    }

    /// <summary>One press a second at most: with the HUD still showing the wrong resist, the next press waits.</summary>
    [Fact]
    public void TheResistIsPressedAtMostOnceASecond()
    {
        TFField f = Field(Vacc(TFMedicClass.ResistBullet));
        int presses = 0;
        for (int k = 0; k < 10; k++)
            presses += Reloads(f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier))) ? 1 : 0;
        // Ten decides of 0.09 s: under a second, one press.
        Assert.Equal(1, presses);
        Assert.Equal(1L, Counter(f, "medic.resist_cycles"));
    }

    /// <summary>An enemy sentry in sight is a bullet threat, weighed double: with a Soldier not firing beside it and the resist on blast, the press goes (to bullet); without the sentry, none.</summary>
    [Fact]
    public void ASentryInSightIsABulletThreat()
    {
        bool Pressed(bool sentry)
        {
            TFField f = Field(Vacc(TFMedicClass.ResistBlast));
            ThingFacts[] things = sentry ? [new ThingFacts(951, ThingKind.Building, (int)TFThingType.Sentry, East(800f), default, TFField.Blue)] : [];
            return Reloads(f.StepSeeing(Me(), TFField.Ctf(), things, null, Patient(), Shooter(Enemy, 900f, TFClass.Soldier, firing: false)));
        }

        Assert.Equal((true, false), (Pressed(true), Pressed(false)));
    }

    /// <summary>The threat is read within 1,500 u: Soldiers firing 1,600 u off with the resist on bullets press nothing; at 1,400 u they do.</summary>
    [Fact]
    public void TheThreatIsReadWithinReach()
    {
        bool Pressed(float at)
        {
            TFField f = Field(Vacc(TFMedicClass.ResistBullet));
            return Reloads(f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, at, TFClass.Soldier)));
        }

        Assert.Equal((false, true), (Pressed(1600f), Pressed(1400f)));
    }

    /// <summary>The resist is pressed only with the medigun out: the syringe gun in hand, Soldiers firing and the resist on bullets, no press.</summary>
    [Fact]
    public void TheResistIsPressedWithTheMedigunOutOnly()
    {
        TFField f = Field(Vacc(TFMedicClass.ResistBullet));
        IntentCommand i = f.StepDrawn(Me(weapon: Syringe), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier));
        Assert.False(Reloads(i));
    }

    /// <summary>The bubble (its own row) when the Medic was just hurt, the resist on the threat; unhurt, none.</summary>
    [Fact]
    public void TheBubbleGoesWhenHurtWithTheResistOnTheThreat()
    {
        bool Bubbles(bool hurt)
        {
            TFField f = Field(Vacc(TFMedicClass.ResistBlast));
            f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier));
            for (int k = 0; k < 8; k++)
            {
                IntentCommand i = hurt ? Shot(f, 140 - 5 * k, Patient(), Shooter(Enemy, 900f, TFClass.Soldier))
                                      : f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier));
                if (IsRow(f, i, TFMedicClass.Bubble))
                    return true;
                Assert.False(IsRow(f, i, TFMedicClass.Pop), "the Vaccinator never sends the Über's row");
            }

            return false;
        }

        Assert.Equal((true, false), (Bubbles(true), Bubbles(false)));
    }

    /// <summary>Hurt with the resist on bullets against Soldiers: the press first, no bubble of the wrong type.</summary>
    [Fact]
    public void AWrongResistIsPressedBeforeAnyBubble()
    {
        TFField f = Field(Vacc(TFMedicClass.ResistBullet));
        var rows = new List<string>();
        for (int k = 0; k < 8; k++)
        {
            IntentCommand i = Shot(f, 140 - 5 * k, Patient(), Shooter(Enemy, 900f, TFClass.Soldier));
            rows.Add(Reloads(i) ? "reload" : TFMedicTests.RowName(f, i));
        }

        Assert.Contains("reload", rows);
        Assert.DoesNotContain(TFMedicClass.Bubble, rows);
    }

    /// <summary>
    /// Only the enemy's injury calls a bubble (stock's tf_bot_medic_heal.cpp:638, :643; the Fable review, C3): the patient's panel falling
    /// with no enemy in sight (his own rocket jump) is none, nor the Medic's own fall with nobody's hit; the control, the same panel fall
    /// with a Soldier firing in sight and the resist on blast, bubbles.
    /// </summary>
    [Fact]
    public void OnlyTheEnemysInjuryCallsABubble()
    {
        bool PatientFall(bool threat)
        {
            TFField f = Field(Vacc(TFMedicClass.ResistBlast));
            ActorFacts[] seen = threat ? [Patient(), Shooter(Enemy, 900f, TFClass.Soldier)] : [Patient()];
            f.StepDrawn(Me(), TFField.Ctf(), seen);
            for (int k = 0; k < 8; k++)
            {
                f.Hud = TFMedicTests.Hud(charge: 0.5f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 190 - 10 * k, 200, East(200f))],
                                         extra: [new TFMeter(TFTargetIdMeters.Resist, TFMedicClass.ResistBlast)],
                                         secondary: new TFKitItem(TFLoadoutSlot.Secondary, 998, "tf_weapon_medigun", Medigun, [new TFOwnKitAttribute("set_charge_type", 3f)]));
                if (IsRow(f, f.StepDrawn(Me(), TFField.Ctf(), seen), TFMedicClass.Bubble))
                    return true;
            }

            return false;
        }

        bool OwnFall()
        {
            TFField f = Field(Vacc(TFMedicClass.ResistBlast));
            f.StepDrawn(Me(), TFField.Ctf(), Patient());
            for (int k = 0; k < 8; k++)
            {
                if (IsRow(f, f.StepDrawn(Me(health: 140 - 5 * k), TFField.Ctf(), Patient()), TFMedicClass.Bubble))
                    return true;
            }

            return false;
        }

        Assert.Equal((false, false, true), (PatientFall(threat: false), OwnFall(), PatientFall(threat: true)));
    }

    /// <summary>The Vaccinator says "ÜberCharge ready!" at the full bar, not at the quarter it is ready to bubble at (the Fable review, C6).</summary>
    [Fact]
    public void TheVaccinatorSaysReadyAtTheFullBarOnly()
    {
        bool Says(float charge)
        {
            TFField f = Field(Vacc(TFMedicClass.ResistBlast, charge: 0.1f));
            f.StepDrawn(Me(), TFField.Ctf(), Patient());
            f.Hud = Vacc(TFMedicClass.ResistBlast, charge: charge);
            for (int k = 0; k < 4; k++)
            {
                if (IsRow(f, f.StepDrawn(Me(), TFField.Ctf(), Patient()), TFMedicClass.VoiceUberReady))
                    return true;
            }

            return false;
        }

        Assert.Equal((false, true), (Says(0.5f), Says(1f)));
    }

    /// <summary>One bubble while it lasts (3 s): hurt on every decide for about 2.3 s, one bubble sent.</summary>
    [Fact]
    public void NoSecondBubbleWhileOneLasts()
    {
        TFField f = Field(Vacc(TFMedicClass.ResistBlast, charge: 1f));
        f.StepDrawn(Me(), TFField.Ctf(), Patient(), Shooter(Enemy, 900f, TFClass.Soldier));
        int bubbles = 0;
        for (int k = 0; k < 25; k++)
            bubbles += IsRow(f, Shot(f, 145 - 4 * k, Patient(), Shooter(Enemy, 900f, TFClass.Soldier)), TFMedicClass.Bubble) ? 1 : 0;
        Assert.Equal(1, bubbles);
    }

    // ------------------------------------------------------------------ the flag and the seek (the coordinator, 2026-11-24)

    /// <summary>
    /// An enemy carries our flag 600 u off and no teammate is in sight: a Medic with a teammate alive on the scoreboard does not chase
    /// the carrier (he goes to find the teammate); the control, a Medic with no teammate at all, does (ChaseCarrier's 0.85 over the seek's 0.7).
    /// </summary>
    [Fact]
    public void AMedicWithATeammateAliveDoesNotChaseOurFlag()
    {
        ObjectiveKnowledge taken = TFField.Ctf(red: ObjectiveState.Carried, redAt: East(600f), redCarrier: Enemy.Slot);
        TFField alone = new(weapons: Weapons, mates: []) { Hud = TFMedicTests.Hud() };
        TFField mated = Field(TFMedicTests.Hud());
        for (int k = 0; k < 3; k++)
        {
            alone.StepDrawn(Me(), taken);
            mated.StepDrawn(Me(), taken);
        }

        Assert.Equal(TFDecider.ChaseCarrier, alone.Branch);
        Assert.NotEqual(TFDecider.ChaseCarrier, mated.Branch);
    }

    /// <summary>
    /// Carrying the enemy's flag, the Medic drops it (its dropitem row) for a Soldier in sight 200 u off; with the Soldier 800 u off, he
    /// keeps it and carries it home.
    /// </summary>
    [Fact]
    public void ACarryingMedicHandsTheFlagToATeammateBeside()
    {
        bool Drops(float soldierAt)
        {
            TFField f = Field(TFMedicTests.Hud());
            ObjectiveKnowledge carried = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot);
            return IsRow(f, f.StepDrawn(Me(), carried, TFMedicTests.Mate(Soldier, East(soldierAt), TFClass.Soldier)), TFMedicClass.DropFlag);
        }

        Assert.Equal((true, false), (Drops(200f), Drops(800f)));
    }

    private static bool DropsAt(TFField f, AiVector me, AiVector soldier, int health = 150)
    {
        ObjectiveKnowledge carried = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: me, blueCarrier: f.Bot.Slot);
        SelfState self = TFField.Self(me, health: health, weapon: Medigun, clip: 0, playerClass: TFClass.Medic);
        return IsRow(f, f.StepDrawn(self, carried, TFMedicTests.Mate(Soldier, soldier, TFClass.Soldier)), TFMedicClass.DropFlag);
    }

    /// <summary>
    /// No hand-off by his own capture zone (the Fable review, C2): 150 u from it with a Soldier 200 u off, he keeps the flag to cap; the
    /// control, the same Soldier 200 u off far from the zone, takes it.
    /// </summary>
    [Fact]
    public void NoHandOffBesideHisOwnCaptureZone()
    {
        AiVector byZone = TFField.RedZone + new AiVector(150f, 0f, 0f);
        Assert.False(DropsAt(Field(TFMedicTests.Hud()), byZone, byZone + new AiVector(200f, 0f, 0f)));
        Assert.True(DropsAt(Field(TFMedicTests.Hud()), Mid, East(200f)));
    }

    /// <summary>
    /// One hand-off a carry: having dropped the flag for a Soldier, the Medic carrying it again 5 s later keeps it; the control, carrying
    /// it again 11 s later, hands it off again.
    /// </summary>
    [Fact]
    public void OneHandOffACarry()
    {
        bool Again(int steps)
        {
            TFField f = Field(TFMedicTests.Hud());
            Assert.True(DropsAt(f, Mid, East(200f)), "the first hand-off");
            for (int k = 0; k < steps; k++)
                f.StepDrawn(Me(), TFField.Ctf(blue: ObjectiveState.Dropped, blueAt: Mid), TFMedicTests.Mate(Soldier, East(200f), TFClass.Soldier));
            return DropsAt(f, Mid, East(200f));
        }

        // A step is a decide period (DecideSchedule.Period ticks of 0.015 s).
        int perSecond = (int)MathF.Ceiling(1f / (DecideSchedule.Period * 0.015f));
        Assert.Equal((false, true), (Again(5 * perSecond), Again(12 * perSecond)));
    }

    /// <summary>
    /// The flag he handed off lies dropped 5 s on, nobody having taken it: the Medic goes for it again; the control, a Medic with the same
    /// dropped flag who never handed it off, does not (a teammate lives).
    /// </summary>
    [Fact]
    public void AnUntakenHandOffIsFetchedAgain()
    {
        string Verdict(bool handed)
        {
            TFField f = Field(TFMedicTests.Hud());
            if (handed)
                Assert.True(DropsAt(f, Mid, East(200f)), "the hand-off");
            ObjectiveKnowledge lying = TFField.Ctf(blue: ObjectiveState.Dropped, blueAt: East(300f));
            for (int k = 0; k < 60; k++)
                f.StepDrawn(Me(), lying);
            return f.Decider.Doing.Verdict;
        }

        Assert.Equal(TFMedicClass.UntakenVerdict, Verdict(handed: true));
        Assert.NotEqual(TFMedicClass.UntakenVerdict, Verdict(handed: false));
    }

    /// <summary>
    /// Fetching and guarding weigh nothing while a teammate lives (the coordinator's rule): weighed by a Medic with a teammate on the
    /// scoreboard, both are 0; by one with none, both stand.
    /// </summary>
    [Fact]
    public void FetchAndGuardWeighNothingWithATeammateAlive()
    {
        TFField alone = new(weapons: Weapons, mates: []) { Hud = TFMedicTests.Hud() };
        TFField mated = Field(TFMedicTests.Hud());
        alone.StepDrawn(Me(), TFField.Ctf());
        mated.StepDrawn(Me(), TFField.Ctf());
        Assert.Equal((0f, 0f), (mated.Decider.Weigh(TFDecider.Fetch, 0.5f), mated.Decider.Weigh(TFDecider.Guard, 0.5f)));
        Assert.True(alone.Decider.Weigh(TFDecider.Fetch, 0.5f) > 0f && alone.Decider.Weigh(TFDecider.Guard, 0.5f) > 0f, "alone, both stand");
    }

    /// <summary>Only the carrier hands off: the flag carried by the Soldier beside him, the Medic sends no drop.</summary>
    [Fact]
    public void OnlyTheCarrierHandsOff()
    {
        TFField f = Field(TFMedicTests.Hud());
        ObjectiveKnowledge carried = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: East(200f), blueCarrier: Soldier.Slot);
        Assert.False(IsRow(f, f.StepDrawn(Me(), carried, TFMedicTests.Mate(Soldier, East(200f), TFClass.Soldier)), TFMedicClass.DropFlag));
    }

    /// <summary>A hand-off goes to no Medic: a teammate Medic 200 u off gets no flag.</summary>
    [Fact]
    public void NoHandOffToAMedic()
    {
        TFField f = Field(TFMedicTests.Hud());
        ObjectiveKnowledge carried = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot);
        IntentCommand i = f.StepDrawn(Me(), carried, TFMedicTests.Mate(Soldier, East(200f), TFClass.Medic));
        Assert.False(IsRow(f, i, TFMedicClass.DropFlag));
    }

    /// <summary>The guess along the way is capped at 2 s: out of sight 5 s, the patient last seen walking east at 240 u/s is looked for about 480 u on, not 1,200.</summary>
    [Fact]
    public void TheWayHeWentIsGuessedTwoSecondsAtMost()
    {
        TFField f = Field(TFMedicTests.Hud());
        f.StepDrawn(Me(), TFField.Ctf(), new ActorFacts(Soldier, East(400f), new AiVector(240f, 0f, 0f), TFField.Launcher, ActorActivity.None, 1f, TFField.Red, (int)TFClass.Soldier));
        for (int k = 0; k < 56; k++)
            f.StepDrawn(Me(), TFField.Ctf());
        float x = f.Decider.Doing.Goal.X;
        Assert.True(x > East(400f).X + 400f && x < East(400f).X + 560f, $"goal x {x}");
    }

    /// <summary>The guess needs a floor: a patient last seen walking north off the level's floor is looked for where he was last seen.</summary>
    [Fact]
    public void TheWayHeWentNeedsAFloor()
    {
        TFField f = Field(TFMedicTests.Hud());
        AiVector last = Mid + new AiVector(300f, 200f, 0f);
        f.StepDrawn(Me(), TFField.Ctf(), new ActorFacts(Soldier, last, new AiVector(0f, 240f, 0f), TFField.Launcher, ActorActivity.None, 1f, TFField.Red, (int)TFClass.Soldier));
        for (int k = 0; k < 12; k++)
            f.StepDrawn(Me(), TFField.Ctf());
        Assert.True(f.Decider.Doing.Goal.DistanceTo(last) < 48f, $"goal {f.Decider.Doing.Goal}");
    }

    /// <summary>
    /// A patient last seen walking east at 240 u/s, then out of sight for about a second: the Medic goes on past where he was last seen,
    /// along his way; the control, a patient last seen standing, is gone to where he stood.
    /// </summary>
    [Fact]
    public void ALostPatientIsLookedForAlongTheWayHeWent()
    {
        float GoalX(float speed)
        {
            TFField f = Field(TFMedicTests.Hud());
            var seen = new ActorFacts(Soldier, East(400f), new AiVector(speed, 0f, 0f), TFField.Launcher, ActorActivity.None, 1f, TFField.Red, (int)TFClass.Soldier);
            f.StepDrawn(Me(), TFField.Ctf(), seen);
            for (int k = 0; k < 10; k++)
                f.StepDrawn(Me(), TFField.Ctf());
            return f.Decider.Doing.Goal.X;
        }

        (float walked, float stood) = (GoalX(240f), GoalX(0f));
        Assert.True(walked > East(400f).X + 100f, $"walked: goal x {walked}");
        Assert.True(MathF.Abs(stood - East(400f).X) < 48f, $"stood: goal x {stood}");
    }

    // ------------------------------------------------------------------ the Battle Medic

    private static TFHud BattleHud(bool bow = false, int patientHealth = 200)
    {
        TFKitItem primary = bow ? new(TFLoadoutSlot.Primary, 305, "tf_weapon_crossbow", Crossbow, [])
                                : new(TFLoadoutSlot.Primary, 36, "tf_weapon_syringegun_medic", Syringe, [new TFOwnKitAttribute("add_onhit_addhealth", 3f)]);
        return new TFHud([primary, new(TFLoadoutSlot.Secondary, 29, "tf_weapon_medigun", Medigun, []), new(TFLoadoutSlot.Melee, 8, "tf_weapon_bonesaw", Bonesaw, [])],
                         [new TFMeter(TFMedigunMeters.Charge, 0.3f), new TFMeter(TFMedigunMeters.Releasing, 0f), new TFMeter(TFMedigunMeters.Patient, Soldier.Slot)],
                         [TFMedicTests.Panel(Soldier, patientHealth, 200, East(200f))]);
    }

    /// <summary>The first seed whose draw is <paramref name="archetype"/>: the decider plays the archetype its seed draws.</summary>
    private static ulong SeedOf(string archetype)
    {
        for (ulong seed = 1; ; seed++)
        {
            if (TFMedicClass.Archetypes().Draw(seed)?.Name == archetype)
                return seed;
        }
    }

    private static bool TakesPrimary(in IntentCommand i, int kind) => i.Action.Kind == ActionKind.SelectWeapon && i.Action.WeaponSlot == EngineBotSelf.ByKind + kind;

    /// <summary>
    /// The Battle Medic (a Blutsauger in its kit) beaming a whole Soldier takes the primary to an enemy in sight 500 u off; the stock
    /// kit in the same place keeps the medigun on its patient.
    /// </summary>
    [Fact]
    public void TheBattleMedicFightsWhileItsPatientIsWhole()
    {
        TFField battle = Field(BattleHud());
        IntentCommand b = battle.StepDrawn(Me(), TFField.Ctf(), Patient(), TFMedicTests.Foe(Enemy, East(-500f)));
        TFField stock = Field(TFMedicTests.Hud(charge: 0.3f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
        IntentCommand s = stock.StepDrawn(Me(), TFField.Ctf(), Patient(), TFMedicTests.Foe(Enemy, East(-500f)));
        Assert.Equal((true, false), (TakesPrimary(b, Syringe), TakesPrimary(s, Syringe)));
    }

    /// <summary>The Battle Medic weighs the fight as a fighter, twice the Medic's half: the Blutsauger kit's Fight weight is double the stock kit's.</summary>
    [Fact]
    public void TheBattleMedicWeighsTheFightAsAFighter()
    {
        TFField battle = Field(BattleHud());
        TFField stock = Field(TFMedicTests.Hud(charge: 0.3f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
        battle.StepDrawn(Me(), TFField.Ctf(), Patient());
        stock.StepDrawn(Me(), TFField.Ctf(), Patient());
        Assert.Equal(2f * stock.Decider.Weigh(TFDecider.Fight, 0.3f), battle.Decider.Weigh(TFDecider.Fight, 0.3f), 3);
    }

    /// <summary>The Battle Medic keeps the medigun out with the charge ready: an enemy 500 u off and a full bar, no primary.</summary>
    [Fact]
    public void TheBattleMedicDoesNotFightWithAChargeReady()
    {
        TFHud full = BattleHud();
        full = new TFHud(full.Kit, [.. full.Meters.Select(m => m.Name == TFMedigunMeters.Charge ? new TFMeter(m.Name, 1f) : m)], full.Mates);
        TFField f = Field(full);
        // Past the first decide's "ÜberCharge ready!" (a one-shot that holds the hands that decide).
        var taken = new List<bool>();
        for (int k = 0; k < 5; k++)
            taken.Add(TakesPrimary(f.StepDrawn(Me(), TFField.Ctf(), Patient(), TFMedicTests.Foe(Enemy, East(-500f))), Syringe));
        Assert.DoesNotContain(true, taken);
    }

    /// <summary>The Battle Medic's patient hurt (his panel at half): the medigun stays on him.</summary>
    [Fact]
    public void TheBattleMedicHealsAHurtPatientFirst()
    {
        TFField f = Field(BattleHud(patientHealth: 100));
        IntentCommand i = f.StepDrawn(Me(), TFField.Ctf(), Patient(), TFMedicTests.Foe(Enemy, East(-500f)));
        Assert.False(TakesPrimary(i, Syringe));
        Assert.Equal((ActionKind.Fire, Soldier), (i.Action.Kind, i.Look.Actor));
    }

    /// <summary>An enemy 1,000 u off: past the syringes' 800 u, so the Blutsauger stays away; the crossbow Battle Medic (its archetype) takes its bow to him.</summary>
    [Fact]
    public void TheCrossbowFightsFurtherThanTheSyringes()
    {
        TFField syringes = Field(BattleHud());
        IntentCommand s = syringes.StepDrawn(Me(), TFField.Ctf(), Patient(), TFMedicTests.Foe(Enemy, East(-1000f)));
        TFField bow = Field(BattleHud(bow: true), seed: SeedOf(TFMedicClass.BattleBow));
        IntentCommand c = bow.StepDrawn(Me(), TFField.Ctf(), Patient(), TFMedicTests.Foe(Enemy, East(-1000f)));
        Assert.Equal(TFMedicClass.BattleBow, bow.Decider.Archetype);
        Assert.Equal((false, true), (TakesPrimary(s, Syringe), TakesPrimary(c, Crossbow)));
    }

    /// <summary>The crossbow kit without the Battle archetype (the Combo) keeps healing in the same place: the bow's fight is the archetype's.</summary>
    [Fact]
    public void TheComboKitDoesNotFightLikeTheBattleBow()
    {
        TFField combo = Field(BattleHud(bow: true), seed: SeedOf(TFMedicClass.Combo));
        IntentCommand c = combo.StepDrawn(Me(), TFField.Ctf(), Patient(), TFMedicTests.Foe(Enemy, East(-1000f)));
        Assert.False(TakesPrimary(c, Crossbow));
    }
}
