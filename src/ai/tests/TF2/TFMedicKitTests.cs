using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Medic's archetypes play differently by the kit they carry (plan_bots_teams_tf2 C.3.1, P7; medic.md §4.5): the
/// Kritzkrieg pops only on the push and stands back, the Quick-Fix keeps to its jumpers and saves, the Combo kit's
/// crossbow heals a calling teammate out of reach and its Ubersaw swings only at an enemy already close. Each fact holds
/// the stock kit's answer beside the archetype's, so the difference is the kit's.
/// </summary>
public sealed class TFMedicKitTests
{
    private const int Medigun = TFMedicTests.Medigun, Syringe = TFMedicTests.Syringe, Bonesaw = TFMedicTests.Bonesaw, Crossbow = 53;

    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId Soldier = new(4, 0x4000_0001), Heavy = new(5, 0x4000_0001), Demo = new(6, 0x4000_0001), Scout = new(8, 0x4000_0001),
                                    Pyro = new(7, 0x4000_0001), Enemy = new(9, 0x4000_0001), Enemy2 = new(10, 0x4000_0001);

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Medigun, "tf_weapon_medigun", 0, 0.1f, 0f, false, false, 0f),
        new WeaponFacts(Syringe, "tf_weapon_syringegun_medic", 40, 0.1f, 1.6f, true, false, 1000f, ProjectileGravity: 300f),
        new WeaponFacts(Bonesaw, "tf_weapon_bonesaw", 0, 0.8f, 0f, false, false, 0f),
        new WeaponFacts(Crossbow, "tf_weapon_crossbow", 1, 0.25f, 1.6f, true, false, 2400f, ProjectileGravity: 160f),
    ]);

    private enum Kit
    {
        Stock,
        Kritz,
        QuickFix,
        Combo,
    }

    private static TFHud Hud(Kit kit, float charge = 0f, int beam = 0, TFMateHealth[]? mates = null)
    {
        TFKitItem primary = kit is Kit.Stock ? new(TFLoadoutSlot.Primary, 17, "tf_weapon_syringegun_medic", Syringe, [])
                                             : new(TFLoadoutSlot.Primary, 305, "tf_weapon_crossbow", Crossbow, []);
        TFKitItem secondary = kit switch
        {
            Kit.Kritz => new(TFLoadoutSlot.Secondary, 35, "tf_weapon_medigun", Medigun, [new TFOwnKitAttribute("set_charge_type", 1f)]),
            Kit.QuickFix => new(TFLoadoutSlot.Secondary, 411, "tf_weapon_medigun", Medigun, [new TFOwnKitAttribute("set_charge_type", 2f)]),
            _ => new(TFLoadoutSlot.Secondary, 29, "tf_weapon_medigun", Medigun, []),
        };
        TFKitItem melee = kit is Kit.Combo or Kit.Kritz ? new(TFLoadoutSlot.Melee, 37, "tf_weapon_bonesaw", Bonesaw, [new TFOwnKitAttribute("add_onhit_ubercharge", 0.25f)])
                                                        : new(TFLoadoutSlot.Melee, 8, "tf_weapon_bonesaw", Bonesaw, []);
        return new TFHud([primary, secondary, melee],
                         [new TFMeter(TFMedigunMeters.Charge, charge), new TFMeter(TFMedigunMeters.Releasing, 0f), new TFMeter(TFMedigunMeters.Patient, beam)],
                         [.. mates ?? []]);
    }

    private static TFField Field(TFHud hud) => new(weapons: Weapons, mates: [4, 5, 6, 7, 8]) { Hud = hud };

    private static SelfState Me(int health = 150, int weapon = Medigun, int clip = 0) => TFField.Self(Mid, health: health, weapon: weapon, clip: clip, playerClass: TFClass.Medic);

    private static AiVector East(float d) => Mid + new AiVector(d, 0f, 0f);

    private static ActorFacts Mate(ActorId id, float east, TFClass c, ActorActivity activity = ActorActivity.None) => TFMedicTests.Mate(id, East(east), c, activity: activity);

    private static ActorFacts Foe(ActorId id, float east) => TFMedicTests.Foe(id, East(east));

    private static bool Pops(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) && row.Name == TFMedicClass.Pop;

    private static bool PopsWithin(TFField f, Func<IntentCommand> step)
    {
        for (int k = 0; k < 8; k++)
        {
            if (Pops(f, step()))
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ the Kritzkrieg

    /// <summary>The patient at 40 % with no enemy near: the stock gun pops to save him; the Kritzkrieg holds (crits save nobody).</summary>
    [Fact]
    public void TheKritzkriegDoesNotPopToSave()
    {
        bool PopsFor(Kit kit)
        {
            TFField f = Field(Hud(kit, charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 80, 200, East(200f))]));
            return PopsWithin(f, () => f.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 200f, TFClass.Soldier)));
        }

        Assert.Equal((true, false), (PopsFor(Kit.Stock), PopsFor(Kit.Kritz)));
    }

    /// <summary>The Kritzkrieg pops on the push (the patient firing at two enemies near him), but not into an enemy sentry in sight.</summary>
    [Fact]
    public void TheKritzkriegPopsOnThePushButNotIntoASentry()
    {
        bool PopsWith(bool sentry)
        {
            TFField f = Field(Hud(Kit.Kritz, charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
            ThingFacts[] things = sentry ? [new ThingFacts(70, ThingKind.Building, (int)TFThingType.Sentry, East(1100f), AiVector.Zero, TFField.Blue)] : [];
            return PopsWithin(f, () => f.StepSeeing(Me(), TFField.Ctf(), things, null,
                                                    Mate(Soldier, 200f, TFClass.Soldier, ActorActivity.Firing), Foe(Enemy, 900f), Foe(Enemy2, 950f)));
        }

        Assert.Equal((true, false), (PopsWith(sentry: false), PopsWith(sentry: true)));
    }

    /// <summary>A Kritz Medic takes a Demoman over a Pyro (crits want a Soldier, Demoman or Heavy); the stock gun keeps the Pyro (stock's rank 0).</summary>
    [Fact]
    public void TheKritzkriegPrefersADemomanToAPyro()
    {
        ActorId Patient(Kit kit)
        {
            TFField f = Field(Hud(kit));
            return f.StepDrawn(Me(), TFField.Ctf(), Mate(Pyro, 150f, TFClass.Pyro), Mate(Demo, -400f, TFClass.Demoman)).Look.Actor;
        }

        Assert.Equal((Pyro, Demo), (Patient(Kit.Stock), Patient(Kit.Kritz)));
    }

    /// <summary>With the charge ready and the patient 150 u off, the stock Medic closes on him; the Kritz Medic stays where it is, further back.</summary>
    [Fact]
    public void TheKritzMedicStandsFurtherBack()
    {
        MoveKind Moves(Kit kit)
        {
            TFField f = Field(Hud(kit, charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(150f))]));
            return f.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 150f, TFClass.Soldier)).Move.Kind;
        }

        Assert.Equal((MoveKind.Follow, MoveKind.Hold), (Moves(Kit.Stock), Moves(Kit.Kritz)));
    }

    /// <summary>An enemy Über seen holds the Kritzkrieg's charge for 8 s: the push 5 s after it does not pop; 9 s after it does.</summary>
    [Fact]
    public void TheKritzkriegWaitsOutAnEnemyUber()
    {
        bool PopsAfter(float seconds)
        {
            TFField f = Field(Hud(Kit.Kritz, charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
            f.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 200f, TFClass.Soldier), TFMedicTests.Foe(Enemy, East(900f), ActorConditions.Invulnerable));
            int quiet = (int)(seconds / (DecideSchedule.Period * 0.015f));
            for (int k = 0; k < quiet; k++)
                f.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 200f, TFClass.Soldier));
            return PopsWithin(f, () => f.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 200f, TFClass.Soldier, ActorActivity.Firing), Foe(Enemy, 900f), Foe(Enemy2, 950f)));
        }

        Assert.Equal((false, true), (PopsAfter(5f), PopsAfter(9f)));
    }

    // ------------------------------------------------------------------ the Quick-Fix

    /// <summary>A Quick-Fix Medic takes the Scout over a Heavy; the stock gun takes the Heavy (a Scout is no primary patient of stock's).</summary>
    [Fact]
    public void TheQuickFixPrefersTheScout()
    {
        ActorId Patient(Kit kit)
        {
            TFField f = Field(Hud(kit));
            return f.StepDrawn(Me(), TFField.Ctf(), Mate(Scout, 300f, TFClass.Scout), Mate(Heavy, -300f, TFClass.HeavyWeapons)).Look.Actor;
        }

        Assert.Equal((Heavy, Scout), (Patient(Kit.Stock), Patient(Kit.QuickFix)));
    }

    /// <summary>The push pops the stock gun's charge, not the Quick-Fix's megaheal; the patient under half pops both.</summary>
    [Fact]
    public void TheQuickFixSavesButDoesNotPush()
    {
        bool PopsFor(Kit kit, int patientHealth, bool push)
        {
            TFField f = Field(Hud(kit, charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, patientHealth, 200, East(200f))]));
            ActorFacts[] seen = push ? [Mate(Soldier, 200f, TFClass.Soldier, ActorActivity.Firing), Foe(Enemy, 900f), Foe(Enemy2, 950f)] : [Mate(Soldier, 200f, TFClass.Soldier)];
            return PopsWithin(f, () => f.StepDrawn(Me(), TFField.Ctf(), seen));
        }

        Assert.Equal((true, false), (PopsFor(Kit.Stock, 200, push: true), PopsFor(Kit.QuickFix, 200, push: true)));
        Assert.True(PopsFor(Kit.QuickFix, 80, push: false));
    }

    /// <summary>The Quick-Fix's megaheal saves the Medic about to die (20 health), the patient whole; at 140 health it does not go.</summary>
    [Fact]
    public void TheQuickFixSavesTheMedic()
    {
        bool PopsAt(int health)
        {
            TFField f = Field(Hud(Kit.QuickFix, charge: 1f, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
            return PopsWithin(f, () => f.StepDrawn(Me(health: health), TFField.Ctf(), Mate(Soldier, 200f, TFClass.Soldier)));
        }

        Assert.Equal((true, false), (PopsAt(20), PopsAt(140)));
    }

    /// <summary>The patient 200 u off, in sight, unseen by any enemy: the stock Medic holds where it is (within its 250 u); the Quick-Fix closes on him.</summary>
    [Fact]
    public void TheQuickFixFollowsCloser()
    {
        MoveKind Moves(Kit kit)
        {
            TFField f = Field(Hud(kit, beam: Soldier.Slot, mates: [TFMedicTests.Panel(Soldier, 200, 200, East(200f))]));
            return f.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 200f, TFClass.Soldier)).Move.Kind;
        }

        Assert.Equal((MoveKind.Hold, MoveKind.Follow), (Moves(Kit.Stock), Moves(Kit.QuickFix)));
    }

    // ------------------------------------------------------------------ the Combo kit

    /// <summary>
    /// A teammate the HUD shows calling, in sight 800 u off, the beam on nobody: the Combo Medic takes out its crossbow and fires a
    /// bolt at him; the stock Medic keeps its medigun out.
    /// </summary>
    [Fact]
    public void TheCrossbowHealsACallingTeammateOutOfReach()
    {
        var calling = new TFMateHealth(Soldier.Slot, -1, -1, TFMateSeen.Calling, East(800f));
        TFField stock = Field(Hud(Kit.Stock, mates: [calling]));
        IntentCommand s = stock.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 800f, TFClass.Soldier));
        Assert.NotEqual(ActionKind.SelectWeapon, s.Action.Kind);

        TFField combo = Field(Hud(Kit.Combo, mates: [calling]));
        IntentCommand select = combo.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, 800f, TFClass.Soldier));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Crossbow), (select.Action.Kind, select.Action.WeaponSlot));
        IntentCommand bolt = combo.StepDrawn(Me(weapon: Crossbow, clip: 1), TFField.Ctf(), Mate(Soldier, 800f, TFClass.Soldier));
        Assert.Equal((ActionKind.Fire, Soldier), (bolt.Action.Kind, bolt.Look.Actor));
    }

    /// <summary>A calling teammate 800 u off gets the bolt with an enemy in sight too (a hurt teammate is one in a fight): the bolt goes to him, not at the enemy.</summary>
    [Fact]
    public void TheCrossbowHealsBeforeItFights()
    {
        var calling = new TFMateHealth(Soldier.Slot, -1, -1, TFMateSeen.Calling, East(800f));
        TFField combo = Field(Hud(Kit.Combo, mates: [calling]));
        IntentCommand bolt = combo.StepDrawn(Me(weapon: Crossbow, clip: 1), TFField.Ctf(), Mate(Soldier, 800f, TFClass.Soldier), Foe(Enemy, -600f));
        Assert.Equal((ActionKind.Fire, Soldier), (bolt.Action.Kind, bolt.Look.Actor));
    }

    /// <summary>
    /// With its patient 200 u off in sight and a calling teammate 800 u off, the Combo Medic keeps (or takes back) the medigun for
    /// the patient: a bolt only goes where the beam cannot reach.
    /// </summary>
    [Fact]
    public void TheCrossbowWaitsWhileThePatientIsInReach()
    {
        var calling = new TFMateHealth(Demo.Slot, -1, -1, TFMateSeen.Calling, East(800f));
        TFField combo = Field(Hud(Kit.Combo, mates: [calling]));
        IntentCommand i = combo.StepDrawn(Me(weapon: Crossbow, clip: 1), TFField.Ctf(), Mate(Soldier, -200f, TFClass.Soldier), Mate(Demo, 800f, TFClass.Demoman));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Medigun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>The Ubersaw in hand with an enemy 60 u off, and the hit filled the bar: the medigun comes back out for the Über, the saw is not swung on (the Fable review, C3).</summary>
    [Fact]
    public void TheUbersawIsPutAwayWhenItsRuleNoLongerHolds()
    {
        TFField f = Field(Hud(Kit.Combo, charge: 1f));
        // The first decide says "ÜberCharge ready!" (its one-shot); the next takes the medigun out.
        f.StepDrawn(Me(weapon: Bonesaw), TFField.Ctf(), Mate(Soldier, -300f, TFClass.Soldier), Foe(Enemy, 60f));
        IntentCommand i = f.StepDrawn(Me(weapon: Bonesaw), TFField.Ctf(), Mate(Soldier, -300f, TFClass.Soldier), Foe(Enemy, 60f));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Medigun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>The Ubersaw's reach and health line: an enemy at 120 u, or the Medic at 60 health, keeps the saw away.</summary>
    [Fact]
    public void TheUbersawHasItsReachAndHealthLine()
    {
        bool Saw(float enemyAt, int health)
        {
            TFField f = Field(Hud(Kit.Combo, charge: 0.3f));
            IntentCommand i = f.StepDrawn(Me(health: health), TFField.Ctf(), Mate(Soldier, -300f, TFClass.Soldier), Foe(Enemy, enemyAt));
            return i.Action.Kind == ActionKind.SelectWeapon && i.Action.WeaponSlot == EngineBotSelf.ByKind + Bonesaw;
        }

        Assert.Equal((true, false, false), (Saw(80f, 150), Saw(120f, 150), Saw(80f, 60)));
    }

    /// <summary>The Ubersaw comes out at an enemy 80 u off with the charge at 30 %; at 80 % charge it does not (the bar is nearly built: keep healing).</summary>
    [Fact]
    public void TheUbersawSwingsOnlyAtACloseEnemyWithTheBarLow()
    {
        IntentCommand At(float charge)
        {
            TFField f = Field(Hud(Kit.Combo, charge: charge));
            return f.StepDrawn(Me(), TFField.Ctf(), Mate(Soldier, -300f, TFClass.Soldier), Foe(Enemy, 80f));
        }

        IntentCommand low = At(0.3f), high = At(0.8f);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Bonesaw), (low.Action.Kind, low.Action.WeaponSlot));
        Assert.NotEqual(EngineBotSelf.ByKind + Bonesaw, high.Action.WeaponSlot);
    }
}
