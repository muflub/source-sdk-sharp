using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Medic's play (plan_bots_teams_tf2 P7; medic.md §4.4, §4.7 with the Fable review's fixes): the patient it picks, the
/// beam, the weapon it holds, where it stands, and when it pops its Über, over what its HUD shows (its kit, its charge, the
/// heal-target panel, the callers and the target ID).
/// </summary>
public sealed class TFMedicTests
{
    public const int Medigun = 50, Syringe = 51, Bonesaw = 52;

    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId Soldier = new(4, 0x4000_0001), Heavy = new(5, 0x4000_0001), Demo = new(6, 0x4000_0001),
                                    Sniper = new(7, 0x4000_0001), Enemy = new(9, 0x4000_0001), Enemy2 = new(10, 0x4000_0001);

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Medigun, "tf_weapon_medigun", 0, 0.1f, 0f, false, false, 0f),
        new WeaponFacts(Syringe, "tf_weapon_syringegun_medic", 40, 0.1f, 1.6f, true, false, 1000f, ProjectileGravity: 300f),
        new WeaponFacts(Bonesaw, "tf_weapon_bonesaw", 0, 0.8f, 0f, false, false, 0f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    /// <summary>The stock Medic's kit and HUD: the charge, whether it runs, the beam's patient's slot and the teammates the HUD shows.</summary>
    internal static TFHud Hud(float charge = 0f, int beam = 0, TFMateHealth[]? mates = null, TFMeter[]? extra = null, TFKitItem? secondary = null)
        => new([
                Item(TFLoadoutSlot.Primary, 17, "tf_weapon_syringegun_medic", Syringe),
                secondary ?? Item(TFLoadoutSlot.Secondary, 29, "tf_weapon_medigun", Medigun),
                Item(TFLoadoutSlot.Melee, 8, "tf_weapon_bonesaw", Bonesaw),
            ],
            [new TFMeter(TFMedigunMeters.Charge, charge), new TFMeter(TFMedigunMeters.Releasing, 0f), new TFMeter(TFMedigunMeters.Patient, beam), .. extra ?? []],
            [.. mates ?? []]);

    /// <summary>The heal-target panel: the beam's patient's exact health.</summary>
    internal static TFMateHealth Panel(ActorId who, int health, int most, AiVector at) => new(who.Slot, health, most, TFMateSeen.HealTarget, at);

    internal static TFField Field(TFHud hud, int[]? mates = null, ulong seed = 7, ICellSight? sight = null, Func<DecisionParams, DecisionParams>? tune = null)
        => new(weapons: Weapons, mates: mates ?? [4, 5, 6, 7], sight: sight, tune: tune) { Hud = hud };

    /// <summary>A level's cell sight by a rule on the field's cells (index x * 8 + y, 64 u cells).</summary>
    private sealed class CellSight(Func<int, int, bool> visible) : ICellSight
    {
        public float Range => 8192f;

        public bool Visible(int a, int b) => visible(a, b);

        public float Exposure(int cell) => 0f;
    }

    private static int CellOf(AiVector p) => (int)(p.X / TFField.CellSize) * TFField.Width + (int)(p.Y / TFField.CellSize);

    internal static SelfState Me(AiVector at, int health = 150, int weapon = Medigun)
        => TFField.Self(at, health: health, weapon: weapon, clip: 0, playerClass: TFClass.Medic);

    internal static ActorFacts Mate(ActorId id, AiVector at, TFClass c, ActorConditions conditions = ActorConditions.None, ActorActivity activity = ActorActivity.None, float yaw = 0f)
        => new(id, at, AiVector.Zero, TFField.Launcher, activity, 1f, TFField.Red, (int)c, conditions, 0, new AiAngles(0f, yaw, 0f));

    internal static ActorFacts Foe(ActorId id, AiVector at, ActorConditions conditions = ActorConditions.None)
        => new(id, at, AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier, conditions);

    private static AiVector East(float d) => Mid + new AiVector(d, 0f, 0f);

    internal static bool Pops(TFField f, in IntentCommand i) => RowName(f, i) == TFMedicClass.Pop;

    internal static string RowName(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) ? row.Name : "";

    // ------------------------------------------------------------------ the beam

    /// <summary>A Medic with the medigun out and a teammate Soldier in sight 300 u off holds the trigger aimed at him (the beam, A4).</summary>
    [Fact]
    public void TheBeamIsHeldOnASoldierInReach()
    {
        TFField f = Field(Hud());
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(300f), TFClass.Soldier));
        Assert.Equal((ActionKind.Fire, LookKind.Actor, Soldier), (i.Action.Kind, i.Look.Kind, i.Look.Actor));
    }

    /// <summary>A teammate beyond the beam's hold (540 u) is not fired at: the Medic goes to him.</summary>
    [Fact]
    public void APatientBeyondTheBeamIsGoneToNotFiredAt()
    {
        TFField f = Field(Hud());
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(700f), TFClass.Soldier));
        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
        Assert.Equal(MoveKind.Follow, i.Move.Kind);
        Assert.True(f.Decider.Doing.Goal.X > Mid.X + 600f, $"goal {f.Decider.Doing.Goal}");
    }

    // ------------------------------------------------------------------ the patient

    /// <summary>A Heavy within 750 u is preferred over a Demoman (stock's rank: Heavy 0, Demoman 3), the Demoman being nearer.</summary>
    [Fact]
    public void AHeavyBeatsADemomanWithinReach()
    {
        TFField f = Field(Hud());
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Demo, East(150f), TFClass.Demoman), Mate(Heavy, East(-400f), TFClass.HeavyWeapons));
        Assert.Equal(Heavy, i.Look.Actor);
    }

    /// <summary>The Demoman is kept when the Heavy is beyond 750 u: a better rank only wins near.</summary>
    [Fact]
    public void AFarHeavyDoesNotTakeTheDemomansPlace()
    {
        TFField f = Field(Hud());
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Demo, East(150f), TFClass.Demoman), Mate(Heavy, East(-900f), TFClass.HeavyWeapons));
        Assert.Equal(Demo, i.Look.Actor);
    }

    /// <summary>Of two of the same rank the one it has is kept unless the other is 300 u nearer.</summary>
    [Fact]
    public void TheSameRankKeepsTheCurrentPatientUnlessMuchNearer()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(400f), TFClass.Soldier));
        // The Heavy 200 u nearer than the Soldier: the Soldier is kept.
        IntentCommand kept = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(400f), TFClass.Soldier), Mate(Heavy, East(-200f), TFClass.HeavyWeapons));
        Assert.Equal(Soldier, kept.Look.Actor);
        // The Heavy 350 u nearer: he is taken.
        IntentCommand taken = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(450f), TFClass.Soldier), Mate(Heavy, East(-100f), TFClass.HeavyWeapons));
        Assert.Equal(Heavy, taken.Look.Actor);
    }

    /// <summary>A Sniper is never the primary patient while a Soldier is known; the flag carrier always is, whatever his class.</summary>
    [Fact]
    public void ASniperIsNoPrimaryPatientButAFlagCarrierAlwaysIs()
    {
        TFField f = Field(Hud());
        // The Soldier 900 u off, beyond a better rank's 750 u: were the Sniper a primary patient he would be kept; he is not one.
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Sniper, East(100f), TFClass.Sniper), Mate(Soldier, East(-900f), TFClass.Soldier));
        Assert.Equal(Soldier, i.Look.Actor);

        TFField g = Field(Hud());
        IntentCommand c = g.StepDrawn(Me(Mid), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: East(100f), blueCarrier: Sniper.Slot),
                                      Mate(Sniper, East(100f), TFClass.Sniper), Mate(Soldier, East(-400f), TFClass.Soldier));
        Assert.Equal(Sniper, c.Look.Actor);
    }

    internal static AiEvent MedicCall(ActorId from, AiVector at, int tick)
        => AiEvent.Callout(new ActorId(2, 1), new Tick(tick), from, new SayIntent(CalloutKind.Help, at, 1, 0, 0, 0, false));

    /// <summary>
    /// A teammate's "Medic!" within 1,000 u wins over class rank (any teammate's, a bot's too: the review's fix 4): the Medic
    /// beams the calling Sniper rather than the Soldier. At 1,200 u the call is not answered.
    /// </summary>
    [Fact]
    public void AMedicCallWithinReachWinsOverClassRank()
    {
        IntentCommand Called(float distance)
        {
            TFField f = Field(Hud());
            AiVector there = East(distance);
            f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-300f), TFClass.Soldier));
            return f.StepHearing(Me(Mid), TFField.Ctf(), [MedicCall(Sniper, there, f.Tick + 1)], Mate(Soldier, East(-300f), TFClass.Soldier), Mate(Sniper, there, TFClass.Sniper));
        }

        IntentCommand near = Called(400f), far = Called(1200f);
        Assert.Equal((Sniper, Soldier), (near.Look.Actor, far.Look.Actor));
    }

    /// <summary>
    /// With the patient stable (the panel shows him whole) a burning teammate within 405 u is healed for the moment; with the
    /// patient hurt, he is not.
    /// </summary>
    [Fact]
    public void AStablePatientLetsTheBeamGoToABurningTeammate()
    {
        ActorId Beamed(int patientHealth)
        {
            TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, patientHealth, 200, East(300f))]));
            IntentCommand i = default;
            // Unhurt for over 3 s.
            for (int k = 0; k < 40; k++)
                i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(300f), TFClass.Soldier), Mate(Demo, East(-200f), TFClass.Demoman, ActorConditions.Burning));
            return i.Look.Actor;
        }

        Assert.Equal((Demo, Soldier), (Beamed(200), Beamed(120)));
    }

    /// <summary>
    /// The HUD follows the beam (the Fable review, C4): the patient whole, a burning Demoman 200 u off gets the beam; with the beam on
    /// him (his panel 100/175, the Soldier's gone), the beam stays on him while he burns, and after while he is hurt; once his panel shows him whole
    /// and he burns no more, the beam goes back to the patient.
    /// </summary>
    [Fact]
    public void TheMomentaryTargetKeepsTheBeamUntilWhole()
    {
        TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(300f))]));
        ActorFacts soldier = Mate(Soldier, East(300f), TFClass.Soldier), burning = Mate(Demo, East(-200f), TFClass.Demoman, ActorConditions.Burning);
        IntentCommand i = default;
        for (int k = 0; k < 40; k++)
            i = f.StepDrawn(Me(Mid), TFField.Ctf(), soldier, burning);
        Assert.Equal(Demo, i.Look.Actor);

        f.Hud = Hud(beam: Demo.Slot, mates: [Panel(Demo, 100, 175, East(-200f))]);
        var looks = new List<ActorId>();
        // Burning, then put out but still hurt: the beam stays on him either way.
        for (int k = 0; k < 20; k++)
            looks.Add(f.StepDrawn(Me(Mid), TFField.Ctf(), soldier, k < 10 ? burning : Mate(Demo, East(-200f), TFClass.Demoman)).Look.Actor);
        Assert.All(looks, l => Assert.Equal(Demo, l));

        f.Hud = Hud(beam: Demo.Slot, mates: [Panel(Demo, 175, 175, East(-200f))]);
        IntentCommand back = f.StepDrawn(Me(Mid), TFField.Ctf(), soldier, Mate(Demo, East(-200f), TFClass.Demoman));
        Assert.Equal(Soldier, back.Look.Actor);
    }

    // ------------------------------------------------------------------ the weapon

    /// <summary>The patient 700 u off and an enemy in sight 300 u away: the primary comes out; the patient back at 200 u with nobody else: the medigun.</summary>
    [Fact]
    public void TheMedicFightsOnlyWhenItMust()
    {
        TFField f = Field(Hud());
        IntentCommand fight = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-700f), TFClass.Soldier), Foe(Enemy, East(300f)));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Syringe), (fight.Action.Kind, fight.Action.WeaponSlot));

        TFField g = Field(Hud());
        IntentCommand heal = g.StepDrawn(Me(Mid, weapon: Syringe), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Medigun), (heal.Action.Kind, heal.Action.WeaponSlot));
    }

    /// <summary>
    /// The syringe gun out with a full clip and an enemy in sight 500 u off; the patient comes back to 100 u, in sight: the medigun
    /// comes out this decide, not after the clip is sprayed dry (the Fable review, C3).
    /// </summary>
    [Fact]
    public void APatientBackInReachTakesTheMedigunOutDespiteAnEnemy()
    {
        TFField f = Field(Hud());
        SelfState me = TFField.Self(Mid, health: 150, weapon: Syringe, clip: 40, playerClass: TFClass.Medic);
        IntentCommand i = f.StepDrawn(me, TFField.Ctf(), Mate(Soldier, East(100f), TFClass.Soldier), Foe(Enemy, East(-500f)));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Medigun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    // ------------------------------------------------------------------ the pop

    private static bool PopsIn(int patientHealth, int self = 150, bool setup = false, bool carrier = false)
    {
        TFField f = Field(Hud(charge: 1f, beam: Soldier.Slot, mates: [Panel(Soldier, patientHealth, 200, East(200f))]));
        ObjectiveKnowledge ctf = carrier ? TFField.Ctf(blue: ObjectiveState.Carried, blueAt: East(200f), blueCarrier: Soldier.Slot) : TFField.Ctf();
        if (setup)
            ctf = new ObjectiveKnowledge([.. ctf.All.Select(o => o with { Setup = true })]);
        for (int k = 0; k < 8; k++)
        {
            if (Pops(f, f.StepDrawn(Me(Mid, health: self), ctf, Mate(Soldier, East(200f), TFClass.Soldier))))
                return true;
        }

        return false;
    }

    /// <summary>A full charge and the patient at 40 % (80 of 200): the Über goes. At 60 % it is held.</summary>
    [Fact]
    public void ThePatientUnderHalfHealthIsUbered()
        => Assert.Equal((true, false), (PopsIn(80), PopsIn(120)));

    /// <summary>In setup the charge is held, the patient at 40 % or not.</summary>
    [Fact]
    public void NoUberInSetup() => Assert.False(PopsIn(80, setup: true));

    /// <summary>The Medic at 20 health pops to save itself, the patient whole.</summary>
    [Fact]
    public void TheMedicAboutToDiePops() => Assert.Equal((true, false), (PopsIn(200, self: 20), PopsIn(200, self: 140)));

    /// <summary>A flag carrier patient gets nothing from a charge: at 40 % with the Medic healthy, no pop.</summary>
    [Fact]
    public void AFlagCarrierPatientIsNotUbered() => Assert.Equal((true, false), (PopsIn(80), PopsIn(80, carrier: true)));

    /// <summary>P7's push: the patient whole and firing with two enemies in sight within 900 u of him pops the charge; one enemy does not.</summary>
    [Fact]
    public void ThePatientFightingTwoEnemiesIsUbered()
    {
        bool Pushes(int enemies)
        {
            TFField f = Field(Hud(charge: 1f, beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(200f))]));
            ActorFacts[] seen = [Mate(Soldier, East(200f), TFClass.Soldier, activity: ActorActivity.Firing), Foe(Enemy, East(900f)), Foe(Enemy2, East(950f))];
            for (int k = 0; k < 8; k++)
            {
                if (Pops(f, f.StepDrawn(Me(Mid), TFField.Ctf(), seen[..(1 + enemies)])))
                    return true;
            }

            return false;
        }

        Assert.Equal((true, false), (Pushes(2), Pushes(1)));
    }

    /// <summary>The trade: an enemy seen invulnerable 1,200 u off pops the charge; the same enemy not invulnerable does not.</summary>
    [Fact]
    public void AnEnemyUberIsTraded()
    {
        bool Trades(ActorConditions c)
        {
            TFField f = Field(Hud(charge: 1f, beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(-200f))]));
            for (int k = 0; k < 8; k++)
            {
                if (Pops(f, f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-200f), TFClass.Soldier), Foe(Enemy, East(1200f), c))))
                    return true;
            }

            return false;
        }

        Assert.Equal((true, false), (Trades(ActorConditions.Invulnerable), Trades(ActorConditions.None)));
    }

    /// <summary>"ÜberCharge ready!" is said once when the bar fills, not every decide.</summary>
    [Fact]
    public void UberReadyIsSaidOnce()
    {
        TFField f = Field(Hud(charge: 0.5f));
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier));
        f.Hud = Hud(charge: 1f);
        int said = Enumerable.Range(0, 20).Count(_ => RowName(f, f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier))) == TFMedicClass.VoiceUberReady);
        Assert.Equal(1, said);
    }

    // ------------------------------------------------------------------ the place

    /// <summary>Seen by an enemy east of the patient, the Medic goes to the patient's far side, west of him, not toward the enemy.</summary>
    [Fact]
    public void SeenByAnEnemyTheMedicGoesBehindThePatient()
    {
        TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(200f))]));
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier), Foe(Enemy, East(1400f)));
        Assert.True(f.Decider.Doing.HasGoal && f.Decider.Doing.Goal.X < East(200f).X, $"goal {f.Decider.Doing.Goal}, branch {f.Decider.Doing.Branch}");
    }

    /// <summary>
    /// The patient 200 u off walking away east, in sight, nobody hostile seen: the Medic walks after him, behind him; the same patient
    /// standing still: it holds where it is (stock's "we're good where we are").
    /// </summary>
    [Fact]
    public void AWalkingPatientIsWalkedAfter()
    {
        MoveKind Moves(float speed)
        {
            TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(200f))]));
            var walking = new ActorFacts(Soldier, East(200f), new AiVector(speed, 0f, 0f), TFField.Launcher, ActorActivity.None, 1f, TFField.Red, (int)TFClass.Soldier);
            return f.StepDrawn(Me(Mid), TFField.Ctf(), walking).Move.Kind;
        }

        Assert.Equal((MoveKind.Follow, MoveKind.Hold), (Moves(220f), Moves(0f)));
    }

    /// <summary>
    /// The patient walks out of sight 300 u off; a second Soldier stands in sight 100 u away: the beam goes to the one in sight
    /// (the Fable review, C1: the stale place held the beam off him for 8 s, same rank and not 300 u nearer).
    /// </summary>
    [Fact]
    public void ATeammateInSightBeatsAPatientOutOfSight()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(300f), TFClass.Soldier));
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Heavy, East(-100f), TFClass.Soldier));
        Assert.Equal(Heavy, i.Look.Actor);
    }

    /// <summary>
    /// A patient out of sight is gone after to his last place; reached without seeing him, he is given up there (not stood on for
    /// 8 s): the Medic is no longer on "patient".
    /// </summary>
    [Fact]
    public void AnUnseenPatientIsGivenUpAtHisLastPlace()
    {
        TFField f = Field(Hud());
        AiVector there = East(400f);
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, there, TFClass.Soldier));
        f.StepDrawn(Me(Mid), TFField.Ctf());
        Assert.Equal("patient", f.Decider.Doing.Verdict);
        f.StepDrawn(Me(there + new AiVector(-40f, 0f, 0f)), TFField.Ctf());
        f.StepDrawn(Me(there + new AiVector(-40f, 0f, 0f)), TFField.Ctf());
        Assert.NotEqual("patient", f.Decider.Doing.Verdict);
    }

    /// <summary>
    /// Seen by an enemy east, with the level's cell sight: the cover point is one no enemy sees and the patient does (the Fable review,
    /// C6). The enemy sees every cell east of x = 1,920; the patient cannot see the west cells of rows 3 to 5 (a pillar), so the nearest
    /// unseen point straight west is no cover; one off that line is.
    /// </summary>
    [Fact]
    public void ACoverPointKeepsALineToThePatient()
    {
        int enemyCell = CellOf(East(1400f)), patientCell = CellOf(East(200f));
        var sight = new CellSight((a, b) => a == enemyCell ? b / TFField.Width >= 30
                                           : a != patientCell || !(b / TFField.Width < 30 && b % TFField.Width is >= 3 and <= 5));
        TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(200f))]), sight: sight);
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier), Foe(Enemy, East(1400f)));
        Assert.True(f.Decider.Doing.HasGoal && i.Move.Kind == MoveKind.Follow, "the Medic goes to a cover point");
        AiVector goal = f.Decider.Doing.Goal;
        int cell = CellOf(goal);
        Assert.True(cell / TFField.Width < 30, $"goal {goal} is seen by the enemy");
        Assert.True(sight.Visible(patientCell, cell), $"goal {goal} has no line to the patient");
    }

    /// <summary>With every place round the patient seen by the enemy, the Medic stays where it is (stock's closeSafety), not behind him.</summary>
    [Fact]
    public void WithNoCoverTheMedicStaysPut()
    {
        TFField f = Field(Hud(beam: Soldier.Slot, mates: [Panel(Soldier, 200, 200, East(200f))]), sight: new CellSight((_, _) => true));
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(200f), TFClass.Soldier), Foe(Enemy, East(1400f)));
        Assert.Equal(MoveKind.Hold, i.Move.Kind);
    }

    /// <summary>The class's own place wins the root with a patient (the Medic follows him), not the flag.</summary>
    [Fact]
    public void WithAPatientTheMedicFollowsHimNotTheFlag()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, East(-600f), TFClass.Soldier));
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
    }

    /// <summary>
    /// With no teammate in sight, the Medic goes to the one its HUD shows calling (a place through walls), not for the enemy's flag
    /// (live, ctf_2fort 2026-11-21: a patientless Medic took the Fetch branch most of its life and carried the flag).
    /// </summary>
    [Fact]
    public void WithNoPatientTheMedicGoesToACallerNotTheFlag()
    {
        TFField f = Field(Hud(mates: [new TFMateHealth(Soldier.Slot, -1, -1, TFMateSeen.Calling, Mid + new AiVector(-900f, 128f, 0f))]));
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf());
        Assert.Equal((TFDecider.ClassBranch, MoveKind.Follow, true), (f.Branch, i.Move.Kind, f.Decider.Doing.HasGoal));
        Assert.True(f.Decider.Doing.Goal.X < Mid.X - 800f, $"goal {f.Decider.Doing.Goal}");
    }

    /// <summary>
    /// A patient lost from sight is gone back to, at his last place (the patient's memory, 8 s), before the flag, and before
    /// another teammate seen since who is no patient (a fellow Medic, east).
    /// </summary>
    [Fact]
    public void ALostPatientIsGoneBackTo()
    {
        TFField f = Field(Hud());
        AiVector there = Mid + new AiVector(-600f, 0f, 0f);
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Soldier, there, TFClass.Soldier));
        for (int k = 0; k < 50; k++)
            f.StepDrawn(Me(Mid), TFField.Ctf());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Sniper, Mid + new AiVector(800f, 0f, 0f), TFClass.Medic));
        for (int k = 0; k < 5; k++)
            f.StepDrawn(Me(Mid), TFField.Ctf());
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.True(f.Decider.Doing.Goal.DistanceTo(there) < 64f, $"goal {f.Decider.Doing.Goal}");
    }

    /// <summary>
    /// With no teammate known at all the Medic stands and looks round for one, for 6 s, then plays on (live, ctf_2fort 2026-11-21:
    /// a Medic that ran ahead of its Soldiers never saw one and healed nobody in 300 s).
    /// </summary>
    [Fact]
    public void WithNoTeammateKnownTheMedicLooksRoundForOne()
    {
        TFField f = Field(Hud());
        IntentCommand first = f.StepDrawn(Me(Mid), TFField.Ctf());
        Assert.Equal((TFDecider.ClassBranch, MoveKind.Hold, LookKind.Glance), (f.Branch, first.Move.Kind, first.Look.Kind));
        AiVector firstLook = first.Look.Point;
        IntentCommand later = default;
        for (int k = 0; k < 12; k++)
            later = f.StepDrawn(Me(Mid), TFField.Ctf());
        Assert.True(later.Look.Point.DistanceTo(firstLook) > 100f, "the look turns");
        for (int k = 0; k < 80; k++)
            f.StepDrawn(Me(Mid), TFField.Ctf());
        // Then the team's way (the Fable review, C2): on CTF, the enemy's flag, still the class branch, not the general play.
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.True(f.Decider.Doing.Goal.DistanceTo(TFField.BlueFlag) < 1f, $"goal {f.Decider.Doing.Goal}");
    }

    /// <summary>
    /// A teammate who is no patient (a fellow Medic) seen 200 u off, then gone, while the Medic stands: the class branch holds for
    /// 120 decides (the Fable review, C2: inside 250 u the old seek dropped him, and the root flipped to the objective and back).
    /// </summary>
    [Fact]
    public void AStaleSightingDoesNotFlipTheBranch()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Sniper, East(200f), TFClass.Medic));
        var branches = new List<string>();
        for (int k = 0; k < 120; k++)
        {
            f.StepDrawn(Me(Mid), TFField.Ctf());
            branches.Add(f.Branch);
        }

        Assert.All(branches, b => Assert.Equal(TFDecider.ClassBranch, b));
    }

    /// <summary>
    /// A seek goal is held once chosen (the Fable review, C2: hysteresis): a fellow Medic seen 600 u east is gone after; one seen
    /// 600 u west a decide later does not turn the Medic round for the next 3 s.
    /// </summary>
    [Fact]
    public void ASeekGoalIsHeld()
    {
        TFField f = Field(Hud());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Sniper, East(600f), TFClass.Medic));
        f.StepDrawn(Me(Mid), TFField.Ctf());
        f.StepDrawn(Me(Mid), TFField.Ctf(), Mate(Heavy, East(-600f), TFClass.Medic));
        var goals = new List<AiVector>();
        for (int k = 0; k < 30; k++)
        {
            f.StepDrawn(Me(Mid), TFField.Ctf());
            goals.Add(f.Decider.Doing.Goal);
        }

        Assert.All(goals, g => Assert.True(g.DistanceTo(East(600f)) < 1f, $"goal {g}"));
    }

    /// <summary>With no teammate known and its look round done, a teammate carrying the enemy's flag is where the Medic goes.</summary>
    [Fact]
    public void WithNobodyKnownTheMedicGoesToTheFlagCarrier()
    {
        TFField f = Field(Hud());
        AiVector carried = East(-900f);
        ObjectiveKnowledge ctf = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: carried, blueCarrier: 6);
        for (int k = 0; k < 80; k++)
            f.StepDrawn(Me(Mid), ctf);
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.True(f.Decider.Doing.Goal.DistanceTo(carried) < 1f, $"goal {f.Decider.Doing.Goal}");
    }

    /// <summary>With no medigun in its kit the Medic plays the general play: no beam held at a teammate.</summary>
    [Fact]
    public void WithNoMedigunItPlaysTheGeneralPlay()
    {
        TFField f = Field(Hud(secondary: Item(TFLoadoutSlot.Secondary, 999, "tf_weapon_shotgun", TFField.Shotgun)));
        IntentCommand i = f.StepDrawn(Me(Mid, weapon: TFField.Shotgun), TFField.Ctf(), Mate(Soldier, East(300f), TFClass.Soldier));
        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
    }
}
