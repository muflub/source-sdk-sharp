using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Heavy's play (plan_bots_teams_tf2 P7; research heavy.md §4.3, §4.5): the spin held after a sighting and let go to
/// travel, the pre-spin at a target believed near, the fire by the minigun's falloff, the band that never backs off a
/// player, the dry gun's swap, the ammo trip, and the Sandvich in a quiet moment.
/// </summary>
public sealed class TFHeavyTests
{
    internal const int Minigun = 50, Shotgun = 51, Lunchbox = 52, Fists = 53;

    private static readonly ActorId Enemy = new(5, 0x4000_0001), Other = new(6, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    internal static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Minigun, TFHeavyClass.MinigunClass, -1, 0.105f, 0f, false, false, 0f),
        new WeaponFacts(Shotgun, "tf_weapon_shotgun_hwg", 6, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Lunchbox, TFHeavyClass.LunchboxClass, -1, 0.5f, 0f, false, false, 0f),
        new WeaponFacts(Fists, "tf_weapon_fists", 0, 0.8f, 0f, false, false, 0f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    /// <summary>A Heavy's kit: the minigun (or <paramref name="primary"/>), the shotgun or the Sandvich, the fists.</summary>
    internal static TFHud Kit(bool sandvich = false, TFKitItem? primary = null, params TFOwnKitAttribute[] lunchbox)
        => new([
            primary ?? Item(TFLoadoutSlot.Primary, 15, TFHeavyClass.MinigunClass, Minigun),
            sandvich ? Item(TFLoadoutSlot.Secondary, 42, TFHeavyClass.LunchboxClass, Lunchbox, lunchbox) : Item(TFLoadoutSlot.Secondary, 11, "tf_weapon_shotgun", Shotgun),
            Item(TFLoadoutSlot.Melee, 5, "tf_weapon_fists", Fists),
        ], [], []);

    internal static SelfState Heavy(AiVector at, int health = 125, int reserve = 200, int weapon = Minigun, int clip = -1)
        => TFField.Self(at, health: health, clip: clip, reserve: reserve, weapon: weapon, playerClass: TFClass.HeavyWeapons);

    /// <summary>A Heavy bot playing Standard: its seed draws Standard from the class's table.</summary>
    internal static TFField Field(TFHud? hud = null, ItemSpawn[]? packs = null) => Pinned(TFHeavyClass.Standard, hud, packs);

    /// <summary>The first seed whose archetype draw for the Heavy is <paramref name="archetype"/>.</summary>
    internal static ulong SeedFor(string archetype)
    {
        TFArchetypeTable table = TFHeavyClass.Archetypes();
        for (ulong seed = 1; seed < 10_000; seed++)
        {
            if (table.Draw(seed)?.Name == archetype)
                return seed;
        }

        throw new InvalidOperationException("no seed draws " + archetype);
    }

    private static readonly int StepsPerSecond = (int)MathF.Round(1f / (DecideSchedule.Period * 0.015f));

    private static (ActorId, AiVector, AiVector, int) At(float distance, AiVector velocity = default)
        => (Enemy, Mid + new AiVector(distance, 0f, 0f), velocity, TFField.Blue);

    /// <summary>The intents of <paramref name="seconds"/> of decides with nobody in sight.</summary>
    private static List<IntentCommand> Quiet(TFField f, float seconds, ObjectiveKnowledge? objectives = null, int reserve = 200, bool none = false)
        => [.. Enumerable.Range(0, (int)(seconds * StepsPerSecond)).Select(_ => f.Step(Heavy(Mid, reserve: reserve), none ? null : objectives ?? TFField.Ctf()))];

    /// <summary>
    /// No objectives in the HUD: the fight is the bot's branch, not the run for the flag, which since the travel rule fights only
    /// inside the band. The fight's own rules (the spin's hold, the reach, the bursts) are facts of a fight.
    /// </summary>
    private static readonly ObjectiveKnowledge? NoObjective = null;

    /// <summary>
    /// The spin is held (the second trigger) for <see cref="TFHeavyClass.SpinHoldSeconds"/> after the last enemy seen, and let
    /// go after it: stock's 3 s (tf_bot_behavior.cpp:1284-1291). The enemy was 1,000 u off, past the pre-spin's reach.
    /// </summary>
    [Fact]
    public void TheSpinIsHeldThreeSecondsAfterASightingThenLetGo()
    {
        TFField f = Field();
        for (int i = 0; i < 3; i++)
            f.Step(Heavy(Mid), NoObjective, At(1000f));
        List<IntentCommand> after = Quiet(f, 5f, none: true);
        int held = after.TakeWhile(c => c.Action.Kind == ActionKind.AltFire).Count();
        float heldSeconds = held / (float)StepsPerSecond;
        Assert.InRange(heldSeconds, 2.6f, 3.3f);
        Assert.DoesNotContain(after.Skip(held + 2), c => c.Action.Kind == ActionKind.AltFire);
    }

    /// <summary>An enemy seen only past the gun's reach (1,300 u) neither holds the spin nor gets a burst from a cold gun: the Heavy travels at full speed.</summary>
    [Fact]
    public void AFarEnemyHoldsNoSpin()
    {
        TFField f = Field();
        List<IntentCommand> seen = [.. Enumerable.Range(0, 10).Select(_ => f.Step(Heavy(Mid), NoObjective, At(1300f)))];
        Assert.DoesNotContain([.. seen, .. Quiet(f, 2f, none: true)], c => c.Action.Kind is ActionKind.AltFire or ActionKind.Fire);
    }

    /// <summary>
    /// On the way to the enemy's flag (an attacker's Fetch, which an enemy past 480 u does not stop), an enemy 800 u off is
    /// neither fired at nor spun for: the Heavy walks on at full speed. The control: with no objective (the Fight branch) the
    /// same enemy is fired at.
    /// </summary>
    [Fact]
    public void OnTheWayToTheFlagAnEnemyOffTheBandIsNotFought()
    {
        List<IntentCommand> Seen(ObjectiveKnowledge? objectives, out string branch)
        {
            TFField f = Field();
            List<IntentCommand> all = [.. Enumerable.Range(0, 20).Select(_ => f.Step(Heavy(Mid), objectives, At(800f)))];
            branch = f.Branch;
            return all;
        }

        Assert.Contains(Seen(null, out _), c => c.Action.Kind == ActionKind.Fire);
        List<IntentCommand> fetching = Seen(TFField.Ctf(), out string fetch);
        Assert.Equal(TFDecider.Fetch, fetch);
        Assert.DoesNotContain(fetching, c => c.Action.Kind is ActionKind.Fire or ActionKind.AltFire);
    }

    /// <summary>
    /// On the way to the flag a gun already spun (an enemy at 500 u) fires on when he steps back to 700 u, inside the reach of
    /// fire at will: the Heavy pays the spin's speed anyway. The control is the off-band fact: from cold, 800 u, no fire.
    /// </summary>
    [Fact]
    public void OnTheWayToTheFlagASpunGunFiresOnPastTheBand()
    {
        TFField f = Field();
        for (int i = 0; i < 3; i++)
            f.Step(Heavy(Mid), TFField.Ctf(), At(500f));
        List<IntentCommand> past = [.. Enumerable.Range(0, 10).Select(_ => f.Step(Heavy(Mid), TFField.Ctf(), At(700f)))];
        Assert.Equal(TFDecider.Fetch, f.Branch);
        Assert.Contains(past, c => c.Action.Kind == ActionKind.Fire);
    }

    /// <summary>
    /// On the way to the flag a target lost 700 u off (past the band) is not pre-spun for; with no objective (a fight) the same
    /// lost target is, within the pre-spin's 800 u.
    /// </summary>
    [Fact]
    public void OnTheWayToTheFlagThePreSpinReachesOnlyTheBand()
    {
        bool PreSpun(ObjectiveKnowledge? objectives)
        {
            TFField f = Field();
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid), objectives, At(700f));
            return Quiet(f, 1f, objectives, none: objectives is null).Any(c => c.Action.Kind == ActionKind.AltFire);
        }

        Assert.Equal((true, false), (PreSpun(NoObjective), PreSpun(TFField.Ctf())));
    }

    /// <summary>
    /// On the way to the flag, hurt by an enemy 800 u off (past the band, inside the reach), the Heavy answers him: he fires.
    /// The control is the off-band fact, the same enemy unhurt; and a hurt 4 s old is no reason any more.
    /// </summary>
    [Fact]
    public void OnTheWayToTheFlagAHeavyShotAtAnswers()
    {
        (List<IntentCommand> Intents, long Answers) Hurt(float secondsAgo)
        {
            TFField f = Field();
            // Five seconds on the way first, so a hurt 4 s ago is a tick of this run.
            for (int i = 0; i < 5 * StepsPerSecond; i++)
                f.Step(Heavy(Mid), TFField.Ctf(), At(800f));
            int at = f.Tick - (int)(secondsAgo / 0.015f);
            f.Memory.Apply(AiEvent.Damaged(f.Bot, new Tick(at), Enemy, 30f, new AiVector(1f, 0f, 0f), 0));
            List<IntentCommand> all = [.. Enumerable.Range(0, 8).Select(_ => f.Step(Heavy(Mid), TFField.Ctf(), At(800f)))];
            Assert.Equal(TFDecider.Fetch, f.Branch);
            return (all, f.Decider.ClassCounters().Single(c => c.Key == "heavy.travel_hurt_answers").Value);
        }

        (List<IntentCommand> now, long answers) = Hurt(0f);
        Assert.Contains(now, c => c.Action.Kind == ActionKind.Fire);
        Assert.True(answers > 0, "the answers were not counted");
        (List<IntentCommand> old, long none) = Hurt(4f);
        Assert.DoesNotContain(old, c => c.Action.Kind == ActionKind.Fire);
        Assert.Equal(0, none);
    }

    /// <summary>On the way to the flag an enemy inside the band (520 u: the attacker's tree still walks on) is fought.</summary>
    [Fact]
    public void OnTheWayToTheFlagAnEnemyInTheBandIsFought()
    {
        TFField f = Field();
        List<IntentCommand> all = [.. Enumerable.Range(0, 20).Select(_ => f.Step(Heavy(Mid), TFField.Ctf(), At(520f)))];
        Assert.Equal(TFDecider.Fetch, f.Branch);
        // From cold, at the first sight: not only once a spin (which a sighting inside the band also holds) has the gun turning.
        Assert.Equal(ActionKind.Fire, all[0].Action.Kind);
    }

    /// <summary>The spin goes to the applier as a Hold (no shot: pressed whatever the view, kept through the Fire's wait), not a Spray shot.</summary>
    [Fact]
    public void TheSpinIsSentAsAHoldNotAShot()
    {
        TFField f = Field();
        for (int i = 0; i < 3; i++)
            f.Step(Heavy(Mid), NoObjective, At(1000f));
        IntentCommand spin = Quiet(f, 1f, none: true)[^1];
        Assert.Equal((ActionKind.AltFire, FireStyle.Hold), (spin.Action.Kind, spin.Action.Style));
    }

    /// <summary>A Heavy on the points or the escort with a low Aggression (fightDrive 0.3), so an enemy in sight does not win the Fight branch over his walk.</summary>
    private static TFField Walker(int[]? mates = null)
        => new(seed: SeedFor(TFHeavyClass.Standard), weapons: Weapons, mates: mates ?? [4], tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f, FightDrive = 0.3f }) { Hud = Kit() };

    private static readonly AiVector PointA = new(1600f, 256f, 0f);

    private static ObjectiveKnowledge PointLevel()
        => new([new Objective(20, ObjectiveKind.Capture, TFField.Blue, 1 << TFField.Red, ObjectiveState.Open, PointA)]);

    /// <summary>The fire decides of a Heavy at <paramref name="at"/> on the Points stance, an enemy 700 u off (past the band, inside the fire reach), and its branch.</summary>
    private static (int Fires, string Branch) OnThePoints(AiVector at)
    {
        TFField f = Walker();
        f.Decider.HoldStance(TFStance.Points);
        int fires = Enumerable.Range(0, 15).Count(_ => f.Step(Heavy(at), PointLevel(), (Enemy, at + new AiVector(0f, 700f, 0f), AiVector.Zero, TFField.Blue)).Action.Kind == ActionKind.Fire);
        return (fires, f.Branch);
    }

    /// <summary>
    /// Walking to a point (600 u short of it) an enemy past the band is not fought; standing on the point the same enemy is: the
    /// travel rule is for the walk, not for the point, where his spun gun is worth most.
    /// </summary>
    [Fact]
    public void OnTheWayToAPointAnEnemyOffTheBandIsNotFoughtButOnItHeIs()
    {
        (int walking, string walkBranch) = OnThePoints(new AiVector(1000f, 256f, 0f));
        (int standing, string standBranch) = OnThePoints(PointA + new AiVector(20f, 0f, 0f));
        Assert.Equal((TFDecider.PointsBranch, TFDecider.PointsBranch), (walkBranch, standBranch));
        Assert.Equal(0, walking);
        Assert.True(standing > 0, "on the point he did not fire");
    }

    /// <summary>The fire decides of a Heavy at <paramref name="at"/> escorting a teammate carrying the enemy's flag at <paramref name="carrier"/>, an enemy 700 u off, and its branch.</summary>
    private static (int Fires, string Branch) Escorting(AiVector at, AiVector carrier)
    {
        TFField f = Walker(mates: [4, 6]);
        ObjectiveKnowledge carried = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: carrier, blueCarrier: 4);
        int fires = Enumerable.Range(0, 15).Count(_ => f.Step(Heavy(at), carried, (Enemy, at + new AiVector(0f, 700f, 0f), AiVector.Zero, TFField.Blue)).Action.Kind == ActionKind.Fire);
        return (fires, f.Branch);
    }

    /// <summary>Walking to the carrier (800 u off) an enemy past the band is not fought; beside the carrier the same enemy is.</summary>
    [Fact]
    public void OnTheWayToTheCarrierAnEnemyOffTheBandIsNotFoughtButBesideItHeIs()
    {
        var carrier = new AiVector(1200f, 256f, 0f);
        (int walking, string walkBranch) = Escorting(new AiVector(2000f, 256f, 0f), carrier);
        (int beside, string besideBranch) = Escorting(carrier + new AiVector(100f, 0f, 0f), carrier);
        Assert.Equal((TFDecider.Escort, TFDecider.Escort), (walkBranch, besideBranch));
        Assert.Equal(0, walking);
        Assert.True(beside > 0, "beside the carrier he did not fire");
    }

    /// <summary>A Heavy carrying the flag home holds no spin after a sighting: it hurries at full speed (research §4.3).</summary>
    [Fact]
    public void ACarrierHoldsNoSpin()
    {
        TFField f = Field();
        ObjectiveKnowledge carrying = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot);
        for (int i = 0; i < 3; i++)
            f.Step(Heavy(Mid), carrying, At(1000f));
        Assert.DoesNotContain(Quiet(f, 2f, carrying), c => c.Action.Kind == ActionKind.AltFire);
    }

    /// <summary>With the minigun low on ammo the spin is not held (stock's <c>!IsAmmoLow()</c>).</summary>
    [Fact]
    public void ALowGunHoldsNoSpin()
    {
        TFField f = Field();
        for (int i = 0; i < 3; i++)
            f.Step(Heavy(Mid, reserve: 30), NoObjective, At(1000f));
        Assert.DoesNotContain(Quiet(f, 2f, reserve: 30, none: true), c => c.Action.Kind == ActionKind.AltFire);
    }

    /// <summary>
    /// A target lost from sight and believed within <see cref="TFHeavyClass.PreSpinReach"/> keeps the gun spun past the hold:
    /// at 4.5 s after it went out of sight 500 u off the gun is still spun, where one lost 1,000 u off was let go.
    /// </summary>
    [Fact]
    public void ATargetBelievedNearIsSpunFor()
    {
        bool SpunAt(float distance, float seconds)
        {
            TFField f = Field();
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid), TFField.Ctf(), At(distance));
            return Quiet(f, seconds)[^1].Action.Kind == ActionKind.AltFire;
        }

        Assert.Equal((true, false), (SpunAt(500f, 4.5f), SpunAt(1000f, 4.5f)));
    }

    /// <summary>
    /// No fire past <see cref="TFHeavyClass.BurstReach"/>, where the general play's gun fires to 2,000 u: a still enemy 1,800 u
    /// off, with the gun spun by a sighting at 900 u first (a cold gun gets no burst at any range, so it would prove nothing).
    /// </summary>
    [Fact]
    public void NoFirePastTheBurstReach()
    {
        List<IntentCommand> Far(TFField f)
        {
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid), NoObjective, At(900f));
            return [.. Enumerable.Range(0, 20).Select(_ => f.Step(Heavy(Mid), NoObjective, At(1800f)))];
        }

        // The control: the general play (no Heavy play) fires the same gun at it.
        var general = new TFField(weapons: Weapons, plays: (d, c) => c == TFClass.HeavyWeapons ? new GeneralPlayForFact(d) : null) { Hud = Kit() };
        Assert.Contains(Far(general), c => c.Action.Kind == ActionKind.Fire);
        List<IntentCommand> heavy = Far(Field());
        // The setup took: the Heavy holds its gun spun at 1,800 u (the nearer sighting's hold), and fires nothing.
        Assert.Contains(heavy, c => c.Action.Kind == ActionKind.AltFire);
        Assert.DoesNotContain(heavy, c => c.Action.Kind == ActionKind.Fire);
    }

    /// <summary>
    /// Between the falloff's floor and the burst reach, a still target gets bursts from a gun spun by a nearer sighting (some
    /// decides fire, some not); a running one none.
    /// </summary>
    [Fact]
    public void PastTheFalloffOnlyAStillTargetGetsBursts()
    {
        int Fires(AiVector velocity)
        {
            TFField f = Field();
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid), NoObjective, At(900f));
            return Enumerable.Range(0, 30).Count(_ => f.Step(Heavy(Mid), NoObjective, At(1300f, velocity)).Action.Kind == ActionKind.Fire);
        }

        int still = Fires(AiVector.Zero), running = Fires(new AiVector(0f, 300f, 0f));
        Assert.InRange(still, 5, 30);
        Assert.Equal(0, running);
    }

    /// <summary>The Heavy never backs off a player for being near (the Soldier's launcher does inside 320 u): an enemy 200 u off.</summary>
    [Fact]
    public void TheHeavyNeverBacksOffANearPlayer()
    {
        var soldier = new TFField();
        soldier.Step(TFField.Self(Mid), TFField.Ctf(), At(200f));
        Assert.Equal("backing off", soldier.Decider.Doing.Verdict);

        TFField f = Field();
        string[] verdicts = [.. Enumerable.Range(0, 10).Select(_ => { f.Step(Heavy(Mid), TFField.Ctf(), At(200f)); return f.Decider.Doing.Verdict; })];
        Assert.DoesNotContain("backing off", verdicts);
        Assert.Contains("holding range", verdicts);
    }

    /// <summary>The Heavy closes on a player past its band (<see cref="TFHeavyClass.BandFar"/>), where the launcher's band holds at 700 u.</summary>
    [Fact]
    public void TheHeavyClosesPastItsBand()
    {
        // No objectives: the fight is the branch (an attacker on its way to the flag stops only for someone within 480 u).
        TFField f = Field();
        f.Step(Heavy(Mid), null, At(700f));
        Assert.Equal("closing", f.Decider.Doing.Verdict);
    }

    /// <summary>The minigun dry in a fight: the shotgun comes out, and the dry gun is not fired.</summary>
    [Fact]
    public void ADryMinigunGivesWayToTheShotgun()
    {
        TFField f = Field();
        IntentCommand i = f.Step(Heavy(Mid, reserve: 0), TFField.Ctf(), At(400f));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Shotgun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>With the Sandvich for a secondary, a dry minigun gives way to the fists, not the lunchbox.</summary>
    [Fact]
    public void ADryMinigunWithASandvichGivesWayToTheFists()
    {
        TFField f = Field(Kit(sandvich: true));
        IntentCommand i = f.Step(Heavy(Mid, reserve: 0), TFField.Ctf(), At(400f));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Fists), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>The shotgun in hand is fired at the shotgun's reach only: an enemy 1,200 u off is not fired at, one at 400 u is.</summary>
    [Fact]
    public void TheShotgunIsFiredAtItsReach()
    {
        ActionKind At_(float distance)
        {
            TFField f = Field();
            return f.Step(Heavy(Mid, weapon: Shotgun, clip: 6, reserve: 32), TFField.Ctf(), At(distance)).Action.Kind;
        }

        Assert.Equal((ActionKind.None, ActionKind.Fire), (At_(1200f), At_(400f)));
    }

    private static readonly AiVector PackAt = new(1600f, 256f, 0f);

    private static ItemSpawn[] Packs() =>
    [
        new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, new AiVector(512f, 64f, 0f), 10f),
        new ItemSpawn(1, "item_ammopack_medium", ItemKind.AmmoMedium, PackAt, 10f),
    ];

    /// <summary>
    /// The minigun low (under <see cref="TFHeavyClass.AmmoLow"/>) in a lull sends the Heavy to the ammo pack 450 u off, its
    /// own branch; with the gun full it does not go.
    /// </summary>
    [Fact]
    public void ALowGunInALullGoesForAmmo()
    {
        (string Branch, AiVector Goal) Going(int reserve)
        {
            TFField f = Field(packs: Packs());
            // Past the first branch's commitment (2 s): the gun's pool is read from the first decide's hands on.
            Quiet(f, 3f, reserve: reserve);
            return (f.Branch, f.Decider.Doing.Goal);
        }

        (string branch, AiVector goal) = Going(30);
        Assert.Equal((TFDecider.ClassBranch, PackAt), (branch, goal));
        Assert.NotEqual(TFDecider.ClassBranch, Going(150).Branch);
    }

    /// <summary>A friendly dispenser seen 400 u off is an ammo place: a low gun in a lull with no pack in reach goes to it.</summary>
    [Fact]
    public void ALowGunGoesToAFriendlyDispenserItSaw()
    {
        AiVector dispenser = Mid + new AiVector(-400f, 0f, 0f);
        ItemSpawn[] noAmmo = [new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, new AiVector(512f, 64f, 0f), 10f)];
        (string Branch, AiVector Goal) Going(int team)
        {
            TFField f = Field(packs: noAmmo);
            var seen = new ThingFacts(950, ThingKind.Building, (int)TFThingType.Dispenser, dispenser, default, team);
            f.StepSeeing(Heavy(Mid, reserve: 30), TFField.Ctf(), [seen]);
            Quiet(f, 3f, reserve: 30);
            return (f.Branch, f.Decider.Doing.Goal);
        }

        Assert.Equal((TFDecider.ClassBranch, dispenser), Going(TFField.Red));
        // The control: the enemy's dispenser is no ammo place.
        Assert.NotEqual(TFDecider.ClassBranch, Going(TFField.Blue).Branch);
    }

    /// <summary>Hurt under <see cref="TFHeavyClass.EatShare"/> in a lull, the Heavy takes out the Sandvich, then eats it (a press).</summary>
    [Fact]
    public void AHurtHeavyInALullEatsTheSandvich()
    {
        TFField f = Field(Kit(sandvich: true));
        IntentCommand select = f.Step(Heavy(Mid, health: 60), TFField.Ctf());
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Lunchbox), (select.Action.Kind, select.Action.WeaponSlot));
        // Pressed till the taunt shows (a voice line, the Medic call at this health, may take one decide's press).
        List<IntentCommand> eat = [.. Enumerable.Range(0, 3).Select(_ => f.Step(Heavy(Mid, health: 60, weapon: Lunchbox, reserve: 1), TFField.Ctf()))];
        Assert.Contains(eat, c => (c.Action.Kind, c.Action.Style) == (ActionKind.Fire, FireStyle.Tap));
    }

    /// <summary>At full health the Heavy never takes the Sandvich out (stock eats whenever it can, tf_bot.cpp:4587-4598).</summary>
    [Fact]
    public void AHeavyAtFullHealthDoesNotEat()
    {
        TFField f = Field(Kit(sandvich: true));
        List<IntentCommand> all = [.. Enumerable.Range(0, 30).Select(_ => f.Step(Heavy(Mid, health: 125), TFField.Ctf()))];
        Assert.DoesNotContain(all, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Lunchbox);
    }

    /// <summary>With an enemy in sight the hurt Heavy does not eat: it is defenceless while eating.</summary>
    [Fact]
    public void AHeavyDoesNotEatWithAnEnemyInSight()
    {
        TFField f = Field(Kit(sandvich: true));
        List<IntentCommand> all = [.. Enumerable.Range(0, 20).Select(_ => f.Step(Heavy(Mid, health: 60), TFField.Ctf(), At(1300f)))];
        Assert.DoesNotContain(all, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Lunchbox);
    }

    /// <summary>
    /// The Sandvich in hand when an enemy shows 400 u off (between the select and the press): the Heavy does not eat it, by the
    /// fire or by the eat's press; the gun comes back out.
    /// </summary>
    [Fact]
    public void ASandvichInHandIsNotEatenWithAnEnemyNear()
    {
        TFField f = Field(Kit(sandvich: true));
        f.Step(Heavy(Mid, health: 60), TFField.Ctf());
        List<IntentCommand> near = [.. Enumerable.Range(0, 2 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, health: 60, weapon: Lunchbox, reserve: 1), TFField.Ctf(), At(400f)))];
        Assert.DoesNotContain(near, c => c.Action.Kind == ActionKind.Fire);
        Assert.Contains(near, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Minigun);
    }

    /// <summary>An empty plate in hand with an enemy 400 u off: the minigun comes back out (the plate is not "fired" at him).</summary>
    [Fact]
    public void AnEmptyPlateInAFightGivesWayToTheGun()
    {
        TFField f = Field(Kit(sandvich: true));
        List<IntentCommand> near = [.. Enumerable.Range(0, 2 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, weapon: Lunchbox, reserve: 0), TFField.Ctf(), At(400f)))];
        Assert.DoesNotContain(near, c => c.Action.Kind == ActionKind.Fire);
        Assert.Contains(near, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Minigun);
    }

    private static bool SelectsLunchbox(IntentCommand c) => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Lunchbox;

    /// <summary>
    /// A Sandvich eaten (the taunt shown) is not taken out again until its meter has refilled (<see cref="TFHeavyClass.SandvichRefillSeconds"/>):
    /// hurt again in a lull, the Heavy keeps its gun for 25 s, and takes the Sandvich out once 30 s have passed.
    /// </summary>
    [Fact]
    public void AnEatenSandvichWaitsForItsRefill()
    {
        TFField f = Field(Kit(sandvich: true));
        Assert.True(SelectsLunchbox(f.Step(Heavy(Mid, health: 60), TFField.Ctf())));
        // Pressed till the taunt shows (the Medic call at this health may take a decide's press).
        Assert.Contains(Enumerable.Range(0, 3).Select(_ => f.Step(Heavy(Mid, health: 60, weapon: Lunchbox, reserve: 1), TFField.Ctf())), c => c.Action.Kind == ActionKind.Fire);
        SelfState eating = Heavy(Mid, health: 60, weapon: Lunchbox, reserve: 0) with { Conditions = ActorConditions.Taunting };
        for (int i = 0; i < 4 * StepsPerSecond; i++)
            f.Step(eating, TFField.Ctf());
        // The taunt over, hurt again: the gun in hand for the rest of the refill.
        List<IntentCommand> waiting = [.. Enumerable.Range(0, 21 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, health: 60), TFField.Ctf()))];
        Assert.DoesNotContain(waiting, SelectsLunchbox);
        List<IntentCommand> refilled = [.. Enumerable.Range(0, 8 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, health: 60), TFField.Ctf()))];
        Assert.Contains(refilled, SelectsLunchbox);
    }

    /// <summary>
    /// An eat called off because an enemy showed while the Sandvich came out is not booked as an empty lunchbox: once it is
    /// quiet again the Heavy takes it out within 4 s, not after the empty lockout's 15.
    /// </summary>
    [Fact]
    public void AnEatCalledOffForAnEnemyIsTriedAgainSoon()
    {
        TFField f = Field(Kit(sandvich: true));
        Assert.True(SelectsLunchbox(f.Step(Heavy(Mid, health: 60), TFField.Ctf())));
        for (int i = 0; i < 3; i++)
            f.Step(Heavy(Mid, health: 60, weapon: Lunchbox, reserve: 1), TFField.Ctf(), At(1300f));
        List<IntentCommand> quiet = [.. Enumerable.Range(0, 4 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, health: 60), TFField.Ctf()))];
        Assert.Contains(quiet, SelectsLunchbox);
    }

    /// <summary>
    /// A lunchbox the game will not take out (stock deploys no empty one) is asked for once, given up after
    /// <see cref="TFHeavyClass.EatSelectSeconds"/>, and asked for again after the empty lockout: twice in 20 s, not every second
    /// and not never again.
    /// </summary>
    [Fact]
    public void ALunchboxTheGameWillNotDeployIsAskedForAgainOnlyAfterALockout()
    {
        TFField f = Field(Kit(sandvich: true));
        List<IntentCommand> all = [.. Enumerable.Range(0, 20 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, health: 60), TFField.Ctf()))];
        Assert.Equal(2, all.Count(SelectsLunchbox));
        Assert.DoesNotContain(all.Take(15 * StepsPerSecond).Skip(1), SelectsLunchbox);
    }

    /// <summary>The Sandvich taken out with nothing in it (its meter still filling): the minigun goes back in hand and the lunchbox is not tried again at once.</summary>
    [Fact]
    public void AnEmptyLunchboxIsPutAwayAndNotTriedAgainAtOnce()
    {
        TFField f = Field(Kit(sandvich: true));
        f.Step(Heavy(Mid, health: 60), TFField.Ctf());
        IntentCommand back = f.Step(Heavy(Mid, health: 60, weapon: Lunchbox, reserve: 0), TFField.Ctf());
        Assert.NotEqual(ActionKind.Fire, back.Action.Kind);
        // The empty lunchbox in hand: the minigun is taken out once the hands may change again.
        List<IntentCommand> putAway = [back, .. Enumerable.Range(0, 2 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, health: 60, weapon: Lunchbox, reserve: 0), TFField.Ctf()))];
        Assert.Contains(putAway, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Minigun);
        // The minigun in hand, still hurt and quiet: the lunchbox is not taken out again for a while.
        List<IntentCommand> after = [.. Enumerable.Range(0, 3 * StepsPerSecond).Select(_ => f.Step(Heavy(Mid, health: 60), TFField.Ctf()))];
        Assert.DoesNotContain(after, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Lunchbox);
    }

    /// <summary>
    /// Of two enemies in sight past close range, the Heavy takes the Pyro (stock's danger 1.0) over a Sniper (0.4) a little
    /// nearer; the general play takes the nearer Sniper.
    /// </summary>
    [Fact]
    public void TheHeavyTakesTheMoreDangerousClass()
    {
        ActorId Target(TFField f, SelfState self)
        {
            var sniper = new ActorFacts(Other, Mid + new AiVector(800f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Sniper);
            var pyro = new ActorFacts(Enemy, Mid + new AiVector(-860f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Pyro);
            f.StepDrawn(self, TFField.Ctf(), sniper, pyro);
            return f.Decider.Doing.Target;
        }

        var general = new TFField(weapons: Weapons, plays: (d, c) => c == TFClass.HeavyWeapons ? new GeneralPlayForFact(d) : null) { Hud = Kit() };
        Assert.Equal(Other, Target(general, Heavy(Mid)));
        Assert.Equal(Enemy, Target(Field(), Heavy(Mid)));
    }


    // ------------------------------------------------------------------ the archetypes (step 3)

    /// <summary>A Heavy bot whose seed draws <paramref name="archetype"/> (the draw its items follow too).</summary>
    internal static TFField Pinned(string archetype, TFHud? hud = null, ItemSpawn[]? packs = null, TFFeelSwitches? feel = null)
        => new(seed: SeedFor(archetype), weapons: Weapons, packs: packs, feel: feel) { Hud = hud ?? Kit() };

    /// <summary>Each archetype names its own primary (research §2): Ambusher the Tomislav, Anchor Natascha, Siege the Brass Beast.</summary>
    [Fact]
    public void EachArchetypeNamesItsMinigun()
    {
        TFArchetypeTable t = TFHeavyClass.Archetypes();
        int[] primaries = [.. new[] { TFHeavyClass.Standard, TFHeavyClass.Ambusher, TFHeavyClass.Anchor, TFHeavyClass.Siege }.Select(n => t.Named(n)!.Prefer(TFLoadoutSlot.Primary)[0])];
        Assert.Equal([15, 424, 41, 312], primaries);
    }

    /// <summary>The Ambusher spins for a target believed 1,100 u off (silent, it can afford it); Standard, whose pre-spin reaches 800 u, does not.</summary>
    [Fact]
    public void TheAmbusherSpinsForAFartherBelief()
    {
        bool SpunAt(string archetype)
        {
            TFField f = Pinned(archetype);
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid), NoObjective, At(1100f));
            return Quiet(f, 4.5f, none: true)[^1].Action.Kind == ActionKind.AltFire;
        }

        Assert.Equal((true, false), (SpunAt(TFHeavyClass.Ambusher), SpunAt(TFHeavyClass.Standard)));
    }

    /// <summary>The Siege Heavy holds its spin 6 s after a sighting (Standard 3 s): at 5 s it is still spun.</summary>
    [Fact]
    public void TheSiegeHeavyHoldsItsSpinLonger()
    {
        bool SpunAt(string archetype)
        {
            TFField f = Pinned(archetype);
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid), NoObjective, At(1000f));
            return Quiet(f, 5f, none: true)[^1].Action.Kind == ActionKind.AltFire;
        }

        Assert.Equal((true, false), (SpunAt(TFHeavyClass.Siege), SpunAt(TFHeavyClass.Standard)));
    }

    /// <summary>The Siege Heavy holds inside its fire reach (44 u/s spun): at 900 u it holds, where Standard closes past 550 u.</summary>
    [Fact]
    public void TheSiegeHeavyDoesNotClose()
    {
        string Verdict(string archetype)
        {
            TFField f = Pinned(archetype);
            f.Step(Heavy(Mid), null, At(900f));
            return f.Decider.Doing.Verdict;
        }

        Assert.Equal(("holding range", "closing"), (Verdict(TFHeavyClass.Siege), Verdict(TFHeavyClass.Standard)));
    }

    /// <summary>A Siege Heavy seeing an enemy past its fire reach (1,300 u) closes to it rather than stand where it can hurt nobody.</summary>
    [Fact]
    public void TheSiegeHeavyClosesOnATargetPastItsReach()
    {
        TFField f = Pinned(TFHeavyClass.Siege);
        string[] verdicts = [.. Enumerable.Range(0, 5).Select(_ => { f.Step(Heavy(Mid), null, At(1300f)); return f.Decider.Doing.Verdict; })];
        Assert.All(verdicts, v => Assert.Equal("closing", v));
    }

    /// <summary>The Anchor fights further out (the slow lands at range): at 700 u it holds where Standard closes.</summary>
    [Fact]
    public void TheAnchorHoldsFurtherOut()
    {
        string Verdict(string archetype)
        {
            TFField f = Pinned(archetype);
            f.Step(Heavy(Mid), null, At(700f));
            return f.Decider.Doing.Verdict;
        }

        Assert.Equal(("holding range", "closing"), (Verdict(TFHeavyClass.Anchor), Verdict(TFHeavyClass.Standard)));
    }

    /// <summary>The first target of a bot of <paramref name="archetype"/> with <paramref name="near"/> 800 u off and <paramref name="far"/> past it, each drawn as its class.</summary>
    private static ActorId FirstTarget(string archetype, (ActorId Id, TFClass Class) near, (ActorId Id, TFClass Class) far, float farDistance)
    {
        TFField f = Pinned(archetype);
        var a = new ActorFacts(near.Id, Mid + new AiVector(800f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)near.Class);
        var b = new ActorFacts(far.Id, Mid + new AiVector(-farDistance, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)far.Class);
        f.StepDrawn(Heavy(Mid), TFField.Ctf(), a, b);
        return f.Decider.Doing.Target;
    }

    /// <summary>The Anchor ranks a Scout (mobile, ×1.4 twice: the slow's best mark) over a Soldier a little nearer; Standard takes the Soldier.</summary>
    [Fact]
    public void TheAnchorTakesTheScout()
    {
        (ActorId, TFClass) soldier = (Other, TFClass.Soldier), scout = (Enemy, TFClass.Scout);
        Assert.Equal((Enemy, Other), (FirstTarget(TFHeavyClass.Anchor, soldier, scout, 870f), FirstTarget(TFHeavyClass.Standard, soldier, scout, 870f)));
    }

    /// <summary>The Ambusher leaves a Scout (×0.7: "slow targets rather than fast Scouts") for an Engineer a little nearer; Standard takes the Scout.</summary>
    [Fact]
    public void TheAmbusherLeavesTheScout()
    {
        (ActorId, TFClass) engineer = (Other, TFClass.Engineer), scout = (Enemy, TFClass.Scout);
        Assert.Equal((Other, Enemy), (FirstTarget(TFHeavyClass.Ambusher, engineer, scout, 900f), FirstTarget(TFHeavyClass.Standard, engineer, scout, 900f)));
    }

    /// <summary>
    /// A Siege Heavy does not chase a target gone out of sight (its fight weighed to nothing): with no objectives it roams
    /// where Standard chases.
    /// </summary>
    [Fact]
    public void TheSiegeHeavyDoesNotChase()
    {
        string Branch(string archetype)
        {
            TFField f = Pinned(archetype);
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid), null, At(700f));
            // Past the fight's commitment (1.5 s) with the target out of sight, believed.
            for (int i = 0; i < 3 * StepsPerSecond; i++)
                f.Step(Heavy(Mid), null);
            return f.Branch;
        }

        Assert.Equal((TFDecider.Fight, TFDecider.Roam), (Branch(TFHeavyClass.Standard), Branch(TFHeavyClass.Siege)));
    }

    /// <summary>
    /// The Anchor goes to heal later (under 0.3 of its health, stock's critical ratio, with its spun resistance): at 35 % in a
    /// lull Standard (the general 0.4) goes to the health pack and the Anchor does not. The skills' retreat rule is off, so
    /// the share alone decides.
    /// </summary>
    [Fact]
    public void TheAnchorRetreatsLater()
    {
        string Branch(string archetype)
        {
            TFField f = Pinned(archetype, feel: TFFeelSwitches.All with { Retreat = false });
            for (int i = 0; i < 3; i++)
                f.Step(Heavy(Mid, health: 44), null);
            return f.Branch;
        }

        Assert.Equal(TFDecider.Survive, Branch(TFHeavyClass.Standard));
        Assert.NotEqual(TFDecider.Survive, Branch(TFHeavyClass.Anchor));
    }

    /// <summary>The general play under the Heavy's class, the control of the facts above.</summary>
    private sealed class GeneralPlayForFact(TFDecider d) : TFDecider.ClassPlay(d, TFClass.HeavyWeapons);
}
