using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Scout's play (plan_bots_teams_tf2 P7, scout.md §4.3 with its review's fixes): its band, reach and weapon choice,
/// its lean on the points, its dodge, and what each archetype changes.
/// </summary>
public sealed class TFScoutPlayTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    private const int Scattergun = 20, Pistol = 21, Bat = 22, Drink = 23, FaN = 24, Babyface = 25;

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Scattergun, "tf_weapon_scattergun", 6, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Pistol, "tf_weapon_pistol_scout", 12, 0.15f, 1f, true, false, 0f),
        new WeaponFacts(Bat, "tf_weapon_bat", 0, 0.5f, 0f, false, false, 0f),
        new WeaponFacts(Drink, "tf_weapon_lunchbox_drink", 0, 1f, 0f, false, false, 0f),
        new WeaponFacts(FaN, "tf_weapon_scattergun_fan", 2, 0.3125f, 3f, true, true, 0f),
        new WeaponFacts(Babyface, "tf_weapon_pep_brawler_blaster", 4, 0.625f, 3f, true, true, 0f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    private static readonly TFKitItem StockGun = Item(TFLoadoutSlot.Primary, 13, "tf_weapon_scattergun", Scattergun);
    private static readonly TFKitItem StockPistol = Item(TFLoadoutSlot.Secondary, 23, "tf_weapon_pistol", Pistol);
    private static readonly TFKitItem StockBat = Item(TFLoadoutSlot.Melee, 0, "tf_weapon_bat", Bat);

    private static TFHud Kit(TFMeter[]? meters, params TFKitItem[] items) => new([.. items], [.. meters ?? []], []);

    private static TFHud Flanker() => Kit(null, StockGun, StockPistol, StockBat);

    private static TFHud BonkKit(float drink, bool phased = false)
        => Kit([new TFMeter(TFScoutMeters.Drink, drink), new TFMeter(TFScoutMeters.Phased, phased ? 1f : 0f)],
               StockGun, Item(TFLoadoutSlot.Secondary, 46, "tf_weapon_lunchbox_drink", Drink), StockBat);

    private static TFHud FaNKit()
        => Kit(null, Item(TFLoadoutSlot.Primary, 45, "tf_weapon_scattergun", FaN, new TFOwnKitAttribute("set_scattergun_has_knockback", 1f)), StockPistol, StockBat);

    /// <summary>The first seed whose archetype draw for the Scout is <paramref name="name"/>: the decider plays the archetype its seed draws.</summary>
    private static ulong SeedFor(string name)
    {
        TFArchetypeTable table = TFScoutClass.Archetypes();
        for (ulong seed = 1; seed < 10_000; seed++)
        {
            if (table.Draw(seed)?.Name == name)
                return seed;
        }

        throw new InvalidOperationException($"no seed draws {name}");
    }

    /// <summary>A Scout defender (the last slot of a team of three) playing <paramref name="archetype"/>: a defender stops for a fight where an attacker would run on for the flag.</summary>
    private static TFField Scout(TFHud hud, string archetype = TFScoutClass.Flanker)
        => new(slot: 3, mates: [1, 2], seed: SeedFor(archetype), weapons: Weapons) { Hud = hud };

    private static SelfState Self(int weapon, int clip = 6, int reserve = 32, int health = 125, AiVector? at = null)
        => TFField.Self(at ?? Mid, health: health, clip: clip, reserve: reserve, weapon: weapon, playerClass: TFClass.Scout);

    private static ActorFacts EnemyAt(float dist, TFClass cls = TFClass.Soldier, AiAngles facing = default)
        => new(Enemy, Mid + new AiVector(dist, 0f, 0f), default, Scattergun, ActorActivity.None, 1f, TFField.Blue, (int)cls, Facing: facing);

    /// <summary>A Scattergun's target at 500 u is closed on (scout.md §4.3: close when over 350); the general band held a launcher's 320-900.</summary>
    [Fact]
    public void AScoutWithAScattergunClosesOnATargetAt500()
    {
        TFField f = Scout(Flanker());
        f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(500f));
        Assert.Equal("closing", f.Decider.Doing.Verdict);
    }

    /// <summary>A Scattergun's target at 200 u is held, never backed off from: the Soldier's band backs off inside 320 for its own blast.</summary>
    [Fact]
    public void AScoutWithAScattergunDoesNotBackOffAt200()
    {
        TFField f = Scout(Flanker());
        f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(200f));
        Assert.Equal("holding range", f.Decider.Doing.Verdict);
    }

    /// <summary>The Scattergun is not fired at 1,200 u (its reach is 900: past it a shot does under 30); the general reach was 2,000.</summary>
    [Fact]
    public void AScoutDoesNotFireItsScattergunAt1200()
    {
        TFField f = Scout(Kit(null, StockGun, StockBat));
        IntentCommand[] decides = [.. Enumerable.Range(0, 8).Select(_ => f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(1200f)))];
        Assert.DoesNotContain(decides, c => c.Action.Kind == ActionKind.Fire);
    }

    /// <summary>The control: at 600 u the same Scattergun is fired.</summary>
    [Fact]
    public void AScoutFiresItsScattergunAt600()
    {
        TFField f = Scout(Kit(null, StockGun, StockBat));
        IntentCommand[] decides = [.. Enumerable.Range(0, 8).Select(_ => f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(600f)))];
        Assert.Contains(decides, c => c.Action.Kind == ActionKind.Fire);
    }

    /// <summary>An empty Scattergun with a target at 400 u: the Pistol comes out (stock switches on an empty primary, tf_bot.cpp:3365-3375).</summary>
    [Fact]
    public void AnEmptyScattergunAt400TakesOutThePistol()
    {
        TFField f = Scout(Flanker());
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Scattergun, clip: 0), TFField.Ctf(), EnemyAt(400f)))];
        Assert.Contains(decides, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Pistol);
    }

    /// <summary>A loaded Scattergun with a target at 900 u: the Pistol comes out (over 600).</summary>
    [Fact]
    public void ATargetAt900TakesOutThePistol()
    {
        TFField f = Scout(Flanker());
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(900f)))];
        Assert.Contains(decides, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Pistol);
    }

    /// <summary>The Pistol out with a target come within 300 u, the primary never seen dry: the Scattergun comes back out.</summary>
    [Fact]
    public void ThePistolOutWithATargetAt300TakesOutTheScattergun()
    {
        TFField f = Scout(Flanker());
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Pistol, clip: 12), TFField.Ctf(), EnemyAt(300f)))];
        Assert.Contains(decides, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Scattergun);
    }

    /// <summary>The Pistol out after the Scattergun ran dry in the bot's hands: it is not taken back out at 300 u (no weapon reloads holstered).</summary>
    [Fact]
    public void AScattergunSeenDryIsNotTakenBackOut()
    {
        TFField f = Scout(Flanker());
        f.StepDrawn(Self(Scattergun, clip: 0), TFField.Ctf(), EnemyAt(400f));
        // Past the hands' second between changes (12 decides of 6 ticks).
        IntentCommand[] decides = [.. Enumerable.Range(0, 24).Select(_ => f.StepDrawn(Self(Pistol, clip: 12), TFField.Ctf(), EnemyAt(300f)))];
        Assert.DoesNotContain(decides, c => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Scattergun);
    }

    /// <summary>
    /// The points weigh a Scout <see cref="TFScoutClass.PointsLean"/> times a Soldier's of the same seed and skills: it caps at
    /// twice the rate (scout.md §4.3 "objective").
    /// </summary>
    [Fact]
    public void ThePointsWeighAScoutMoreThanASoldier()
    {
        var scout = new TFField(seed: 11, weapons: Weapons) { Hud = Flanker() };
        var soldier = new TFField(seed: 11, weapons: Weapons);
        scout.Step(Self(Scattergun), TFField.Ctf());
        soldier.Step(TFField.Self(Mid), TFField.Ctf());
        float ratio = scout.Decider.Weigh(TFDecider.PointsBranch, 0.5f) / soldier.Decider.Weigh(TFDecider.PointsBranch, 0.5f);
        Assert.Equal(TFScoutClass.PointsLean, ratio, 3);
    }

    /// <summary>
    /// Runs <paramref name="decides"/> decides of a fight with a target at <paramref name="dist"/> watching the bot (facing it), the
    /// bot in the air on the two decides after each jump it sends, and answers the jumps sent on the ground and in the air.
    /// </summary>
    private static (int Ground, int Air) FightJumps(TFField f, float dist, int decides, TFClass cls = TFClass.Pyro, float yaw = 180f)
    {
        int ground = 0, air = 0, airborne = 0;
        var facing = new AiAngles(0f, yaw, 0f);
        for (int i = 0; i < decides; i++)
        {
            SelfState self = Self(Scattergun);
            if (airborne > 0)
                self = self with { Flags = SelfFlags.Alive, Origin = self.Origin + new AiVector(0f, 0f, 40f) };
            IntentCommand c = f.StepDrawn(self, TFField.Ctf(), EnemyAt(dist, cls, facing));
            bool jumped = c.Move.Kind == MoveKind.Jump;
            if (jumped && airborne > 0)
                air++;
            else if (jumped)
                ground++;
            airborne = jumped && airborne == 0 ? 2 : Math.Max(0, airborne - 1);
        }

        return (ground, air);
    }

    /// <summary>Close in (200 u), a Scout's hops become double jumps: a second jump sent in the air, toward the other side.</summary>
    [Fact]
    public void CloseInAScoutsHopsAreDoubleJumps()
    {
        (int ground, int air) = FightJumps(Scout(Flanker()), 200f, 400);
        Assert.True(ground > 0, "no hop at all");
        Assert.True(air > 0, $"{ground} hops and no second jump in the air");
    }

    /// <summary>A target looking away (its facing 30 degrees off the line to the bot): no hop is doubled, the dodge is for a target that watches.</summary>
    [Fact]
    public void ATargetLookingAwayDrawsNoDoubleJump()
    {
        (int ground, int air) = FightJumps(Scout(Flanker()), 200f, 400, yaw: 150f);
        Assert.Equal(0, air);
    }

    /// <summary>The control: watched (its facing 10 degrees off the line), the hops are doubled.</summary>
    [Fact]
    public void ATargetWatchingWithinTwelveDegreesDrawsDoubleJumps()
    {
        (int ground, int air) = FightJumps(Scout(Flanker()), 200f, 400, yaw: 170f);
        Assert.True(air > 0, $"{ground} hops and no second jump in the air");
    }

    /// <summary>The Scout hops more close in (200 u) than at mid range (450 u): close favours the jump, mid range stays grounded (scout.md §4.3).</summary>
    [Fact]
    public void AScoutHopsMoreCloseInThanAtMidRange()
    {
        int near = FightJumps(Scout(Flanker()), 200f, 400).Ground;
        int mid = FightJumps(Scout(Flanker()), 450f, 400).Ground;
        Assert.True(near > mid, $"{near} hops at 200 u, {mid} at 450 u");
    }

    /// <summary>Against a Soldier close in, the hop is never doubled: his splash punishes the air dash.</summary>
    [Fact]
    public void AgainstASoldierCloseInTheHopIsNotDoubled()
    {
        (int ground, int air) = FightJumps(Scout(Flanker()), 200f, 400, TFClass.Soldier);
        Assert.True(ground > 0, "no hop at all");
        Assert.Equal(0, air);
    }

    /// <summary>The BFB runner never spends its boost on an air dash (scout.md §4.3 E: −75 % boost per air jump).</summary>
    [Fact]
    public void TheBabyFaceRunnerNeverDoublesAHop()
    {
        var hud = Kit(null, Item(TFLoadoutSlot.Primary, 772, "tf_weapon_pep_brawler_blaster", Babyface), StockPistol, StockBat);
        TFField f = Scout(hud, TFScoutClass.BabyFace);
        (int ground, int air) = FightJumps(f, 200f, 400);
        Assert.True(ground > 0, "no hop at all");
        Assert.Equal(0, air);
    }

    /// <summary>The FaN brawler closes on a target at 300 u (its band ends at 200) where a Scattergun Flanker holds.</summary>
    [Fact]
    public void TheForceANatureClosesOnATargetAt300()
    {
        TFField f = Scout(FaNKit(), TFScoutClass.ForceANature);
        f.StepDrawn(Self(FaN, clip: 2), TFField.Ctf(), EnemyAt(300f));
        Assert.Equal("closing", f.Decider.Doing.Verdict);
    }

    /// <summary>The control: the Flanker's Scattergun holds at 300 u.</summary>
    [Fact]
    public void TheScattergunHoldsATargetAt300()
    {
        TFField f = Scout(Flanker());
        f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(300f));
        Assert.Equal("holding range", f.Decider.Doing.Verdict);
    }

    /// <summary>The FaN is not fired at 400 u (only within 250, so its knockback lands); a Scattergun would be.</summary>
    [Fact]
    public void TheForceANatureIsNotFiredAt400()
    {
        TFField f = Scout(FaNKit(), TFScoutClass.ForceANature);
        IntentCommand[] decides = [.. Enumerable.Range(0, 8).Select(_ => f.StepDrawn(Self(FaN, clip: 2), TFField.Ctf(), EnemyAt(400f)))];
        Assert.DoesNotContain(decides, c => c.Action.Kind == ActionKind.Fire);
    }

    private static bool TakesOutTheDrink(IntentCommand c) => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + Drink;

    /// <summary>The Bonk runner, hurt (40 of 125) with an enemy in sight at 500 u and its drink full, takes the drink out to escape (scout.md §4.3 B).</summary>
    [Fact]
    public void AHurtBonkRunnerWithAFullDrinkTakesItOut()
    {
        TFField f = Scout(BonkKit(100f), TFScoutClass.Bonk);
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Scattergun, health: 40), TFField.Ctf(), EnemyAt(500f)))];
        Assert.Contains(decides, TakesOutTheDrink);
    }

    /// <summary>A drink meter at 99 is not full: no drink (stock's gate, tf_bot.cpp:4593).</summary>
    [Fact]
    public void ABonkRunnerWithADrinkAt99DoesNotTakeItOut()
    {
        TFField f = Scout(BonkKit(99f), TFScoutClass.Bonk);
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Scattergun, health: 40), TFField.Ctf(), EnemyAt(500f)))];
        Assert.DoesNotContain(decides, TakesOutTheDrink);
    }

    /// <summary>The Flanker with the same drink in its kit (a pin's kit, say) never drinks: the drink is the Bonk runner's.</summary>
    [Fact]
    public void AFlankerDoesNotDrink()
    {
        TFField f = Scout(BonkKit(100f), TFScoutClass.Flanker);
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Scattergun, health: 40), TFField.Ctf(), EnemyAt(500f)))];
        Assert.DoesNotContain(decides, TakesOutTheDrink);
    }

    /// <summary>The drink out, the Bonk runner presses the trigger to drink it, with the enemy it ran from out of sight too (no fight's trigger to do it).</summary>
    [Fact]
    public void WithTheDrinkOutTheBonkRunnerDrinksIt()
    {
        TFField f = Scout(BonkKit(100f), TFScoutClass.Bonk);
        f.StepDrawn(Self(Scattergun, health: 40), TFField.Ctf(), EnemyAt(500f));
        IntentCommand[] decides = [.. Enumerable.Range(0, 3).Select(_ => f.StepDrawn(Self(Drink, clip: 0, reserve: 0, health: 40), TFField.Ctf()))];
        Assert.Contains(decides, c => c.Action.Kind == ActionKind.Fire);
    }
    /// <summary>An enemy sentry by their flag, and a place 100 u past the edge the bot keeps from it, its way to the flag leading in (TFThingsTests' course).</summary>
    private static readonly AiVector ByTheirFlag = TFField.BlueFlag + new AiVector(-200f, 0f, 0f);

    private static readonly AiVector AtTheEdge = ByTheirFlag + new AiVector(-(TFKnowledge.SentryRange + TFDecider.SentryMargin + 100f), 0f, 0f);

    private static ThingFacts Sentry(AiVector at) => new(700, ThingKind.Building, (int)TFThingType.Sentry, at, default, TFField.Blue, 5, 1);

    /// <summary>An attacking Bonk runner at full health, its drink full, at the edge of a sentry's range on its way: it takes the drink out (scout.md §4.3 B).</summary>
    [Fact]
    public void ABonkRunnerAtASentrysEdgeTakesOutTheDrink()
    {
        var f = new TFField(seed: SeedFor(TFScoutClass.Bonk), weapons: Weapons) { Hud = BonkKit(100f) };
        IntentCommand i = f.StepSeeing(Self(Scattergun, at: AtTheEdge), TFField.Ctf(), [Sentry(ByTheirFlag)]);
        Assert.True(TakesOutTheDrink(i), $"{i.Action}");
    }

    /// <summary>The control: the Flanker at the same edge stops there and drinks nothing.</summary>
    [Fact]
    public void AFlankerAtASentrysEdgeStopsThere()
    {
        var f = new TFField(seed: SeedFor(TFScoutClass.Flanker), weapons: Weapons) { Hud = BonkKit(100f) };
        IntentCommand i = f.StepSeeing(Self(Scattergun, at: AtTheEdge), TFField.Ctf(), [Sentry(ByTheirFlag)]);
        Assert.Equal((MoveKind.Hold, TFDecider.SentryEdgeVerdict, false), (i.Move.Kind, f.Decider.Doing.Verdict, TakesOutTheDrink(i)));
    }

    /// <summary>Phased, the Bonk runner walks on into the sentry's range: the sentry cannot hurt it.</summary>
    [Fact]
    public void PhasedTheBonkRunnerWalksIntoTheSentrysRange()
    {
        var f = new TFField(seed: SeedFor(TFScoutClass.Bonk), weapons: Weapons) { Hud = BonkKit(0f, phased: true) };
        IntentCommand i = f.StepSeeing(Self(Drink, clip: 0, reserve: 0, at: AtTheEdge), TFField.Ctf(), [Sentry(ByTheirFlag)]);
        Assert.Equal((MoveKind.Follow, 0), (i.Move.Kind, f.Decider.SentryHolds));
    }

    /// <summary>A primary's shot fired is counted with the distance it was fired from, for the harness (the FaN brawler's should be nearer).</summary>
    [Fact]
    public void AShotIsCountedWithItsDistance()
    {
        TFField f = Scout(Kit(null, StockGun, StockBat));
        int fired = Enumerable.Range(0, 8).Count(_ => f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(200f)).Action.Kind == ActionKind.Fire);
        Dictionary<string, long> counters = f.Decider.ClassCounters().ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(fired > 0, "no shot");
        Assert.Equal((fired, 200L * fired), (counters["scout.primary_shots"], counters["scout.primary_units"]));
    }

    /// <summary>A Pistol's shot is not a primary shot: the counters are the primary's range.</summary>
    [Fact]
    public void APistolShotIsNotCountedAsThePrimarys()
    {
        TFField f = Scout(Kit(null, StockGun, StockPistol, StockBat));
        int fired = Enumerable.Range(0, 8).Count(_ => f.StepDrawn(Self(Pistol, clip: 12), TFField.Ctf(), EnemyAt(800f)).Action.Kind == ActionKind.Fire);
        Dictionary<string, long> counters = f.Decider.ClassCounters().ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(fired > 0, "no shot");
        Assert.Equal(0L, counters["scout.primary_shots"]);
    }
    private static bool Selects(IntentCommand c, int kind) => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + kind;

    private static string[] DryVerdicts => ["launcher dry", TFDecider.CoverVerdict];

    /// <summary>
    /// The Scattergun gone dry inside the hands' second between changes (it had just been taken out): the Pistol comes out on
    /// that same decide; the general hands wait out their spacing, and the shared fight's "launcher dry" backs the bot off
    /// all the while.
    /// </summary>
    [Fact]
    public void ADryScattergunTakesOutThePistolPastTheHandsSpacing()
    {
        TFField f = Scout(Flanker());
        Assert.True(Selects(f.StepDrawn(Self(Pistol, clip: 12), TFField.Ctf(), EnemyAt(300f)), Scattergun), "the Scattergun was not taken out first");
        IntentCommand dry = f.StepDrawn(Self(Scattergun, clip: 0), TFField.Ctf(), EnemyAt(200f));
        Assert.True(Selects(dry, Pistol), $"{dry.Action}");
    }

    /// <summary>
    /// What a dry Scattergun does to the footwork with a loaded Pistol (round 2's S10, <c>ClassPlay.BacksOffDry</c>): on the
    /// decide it is seen dry the shared fight does not back the bot off ("launcher dry"), and that decide and the next, with
    /// the Pistol out, are the Scout's own fight (closing or holding, never backing off). Before S10 the first backed off.
    /// </summary>
    [Fact]
    public void ADryScattergunWithALoadedPistolDoesNotBackOff()
    {
        TFField f = Scout(Flanker());
        f.StepDrawn(Self(Scattergun, clip: 0), TFField.Ctf(), EnemyAt(500f));
        Assert.Equal(("fight at range", true), (f.Decider.Doing.Leaf, new[] { "closing", "holding range" }.Contains(f.Decider.Doing.Verdict)));
        f.StepDrawn(Self(Pistol, clip: 12), TFField.Ctf(), EnemyAt(500f));
        Assert.Contains(f.Decider.Doing.Verdict, new[] { "closing", "holding range" });
    }

    /// <summary>A Soldier's dry launcher still backs it off on the first dry decide (the default <c>BacksOffDry</c>).</summary>
    [Fact]
    public void ADrySoldierStillBacksOff()
    {
        var f = new TFField(slot: 3, mates: [1, 2], weapons: Weapons);
        f.StepDrawn(TFField.Self(Mid, clip: 0), TFField.Ctf(), EnemyAt(500f));
        Assert.Equal("back off and load", f.Decider.Doing.Leaf);
        Assert.Contains(f.Decider.Doing.Verdict, DryVerdicts);
    }

    /// <summary>The Bonk runner (no Pistol) with its Scattergun dry and the target within a swing (80 u) does not back off: it swings.</summary>
    [Fact]
    public void ADryScoutWithinASwingDoesNotBackOff()
    {
        TFField f = Scout(BonkKit(0f), TFScoutClass.Bonk);
        f.StepDrawn(Self(Scattergun, clip: 0, reserve: 32), TFField.Ctf(), EnemyAt(80f));
        Assert.NotEqual("back off and load", f.Decider.Doing.Leaf);
        Assert.DoesNotContain(f.Decider.Doing.Verdict, DryVerdicts);
    }

    /// <summary>The Bonk runner (no Pistol) with its Scattergun dry, the target within a swing (80 u): the Bat, though the gun has shells in reserve.</summary>
    [Fact]
    public void ABonkRunnerWithADryScattergunTakesTheBatWithinASwing()
    {
        TFField f = Scout(BonkKit(0f), TFScoutClass.Bonk);
        IntentCommand c = f.StepDrawn(Self(Scattergun, clip: 0, reserve: 32), TFField.Ctf(), EnemyAt(80f));
        Assert.True(Selects(c, Bat), $"{c.Action}");
    }

    /// <summary>The same past a swing (300 u): it backs off ("launcher dry") and reloads, stock's barrage-weapon retreat.</summary>
    [Fact]
    public void ABonkRunnerWithADryScattergunPastASwingBacksOffAndReloads()
    {
        TFField f = Scout(BonkKit(0f), TFScoutClass.Bonk);
        IntentCommand c = f.StepDrawn(Self(Scattergun, clip: 0, reserve: 32), TFField.Ctf(), EnemyAt(300f));
        Assert.Equal(ActionKind.Reload, c.Action.Kind);
        Assert.Contains(f.Decider.Doing.Verdict, DryVerdicts);
    }

    /// <summary>The FaN dry with the target inside its reach (200 u): reloaded in hand, the Pistol left holstered (scout.md §4.3 C: two shells are its fight).</summary>
    [Fact]
    public void ADryFaNWithinItsReachIsReloadedInHand()
    {
        TFField f = Scout(FaNKit(), TFScoutClass.ForceANature);
        IntentCommand c = f.StepDrawn(Self(FaN, clip: 0, reserve: 32), TFField.Ctf(), EnemyAt(200f));
        Assert.Equal(ActionKind.Reload, c.Action.Kind);
    }

    /// <summary>The FaN dry with the target past its reach (400 u): the Pistol.</summary>
    [Fact]
    public void ADryFaNPastItsReachTakesOutThePistol()
    {
        TFField f = Scout(FaNKit(), TFScoutClass.ForceANature);
        IntentCommand c = f.StepDrawn(Self(FaN, clip: 0, reserve: 32), TFField.Ctf(), EnemyAt(400f));
        Assert.True(Selects(c, Pistol), $"{c.Action}");
    }

    /// <summary>
    /// The FaN brawler with the Pistol out after its FaN went dry, the target come within the FaN's reach (200 u): the FaN back
    /// out, to reload it in hand (the Flanker leaves its dry Scattergun holstered: AScattergunSeenDryIsNotTakenBackOut).
    /// </summary>
    [Fact]
    public void TheFaNBrawlerTakesItsDryFaNBackOutInsideItsReach()
    {
        TFField f = Scout(FaNKit(), TFScoutClass.ForceANature);
        f.StepDrawn(Self(FaN, clip: 0, reserve: 32), TFField.Ctf(), EnemyAt(400f));
        IntentCommand[] decides = [.. Enumerable.Range(0, 24).Select(_ => f.StepDrawn(Self(Pistol, clip: 12), TFField.Ctf(), EnemyAt(200f)))];
        Assert.Contains(decides, c => Selects(c, FaN));
    }

    /// <summary>The Pistol out of shells and reserve both, the Scattergun seen dry but with shells in reserve: the Scattergun back out (it reloads in hand).</summary>
    [Fact]
    public void AnEmptyPistolTakesThePrimaryBackOut()
    {
        TFField f = Scout(Flanker());
        f.StepDrawn(Self(Scattergun, clip: 0, reserve: 32), TFField.Ctf(), EnemyAt(300f));
        IntentCommand c = f.StepDrawn(Self(Pistol, clip: 0, reserve: 0), TFField.Ctf(), EnemyAt(300f));
        Assert.True(Selects(c, Scattergun), $"{c.Action}");
    }

    /// <summary>Both guns out of shells and reserve: the Bat (stock's melee when nothing else fires).</summary>
    [Fact]
    public void BothGunsEmptyTakeOutTheBat()
    {
        TFField f = Scout(Flanker());
        f.StepDrawn(Self(Scattergun, clip: 0, reserve: 0), TFField.Ctf(), EnemyAt(300f));
        IntentCommand c = f.StepDrawn(Self(Pistol, clip: 0, reserve: 0), TFField.Ctf(), EnemyAt(300f));
        Assert.True(Selects(c, Bat), $"{c.Action}");
    }

    /// <summary>In a lull, the Pistol out and short of a full clip is reloaded before the primary is taken out (scout.md §4.3).</summary>
    [Fact]
    public void InALullThePistolIsReloadedFirst()
    {
        TFField f = Scout(Flanker());
        IntentCommand c = f.StepDrawn(Self(Pistol, clip: 5, reserve: 36), TFField.Ctf());
        Assert.Equal(ActionKind.Reload, c.Action.Kind);
    }

    /// <summary>The Scout's play object, which the decider keeps for its class (private), for the facts on its numbers.</summary>
    private static TFDecider.ClassPlay PlayOf(TFField f)
        => (TFDecider.ClassPlay)typeof(TFDecider).GetField("_classPlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f.Decider)!;

    private static KnownActor Seen(TFField f, TFClass cls, float dist)
    {
        f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(dist, cls));
        Blackboard board = f.Memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            if (board.Actor(s).Id == Enemy)
                return board.Actor(s);
        }

        throw new InvalidOperationException("the enemy was not remembered");
    }

    /// <summary>A Sniper and a Demoman are worth +0.2 to a Scout (scout.md §4.3's comp targets), a Soldier nothing.</summary>
    [Fact]
    public void ASniperAndADemomanAreWorthMoreToAScout()
    {
        TFField f = Scout(Flanker());
        KnownActor sniper = Seen(f, TFClass.Sniper, 700f);
        Assert.Equal(TFScoutClass.PickBonus, PlayOf(f).TargetBonus(sniper, true, 700f));
        TFField g = Scout(Flanker());
        KnownActor demo = Seen(g, TFClass.Demoman, 700f);
        Assert.Equal(TFScoutClass.PickBonus, PlayOf(g).TargetBonus(demo, true, 700f));
        TFField h = Scout(Flanker());
        KnownActor soldier = Seen(h, TFClass.Soldier, 700f);
        Assert.Equal(0f, PlayOf(h).TargetBonus(soldier, true, 700f));
    }

    /// <summary>A Heavy within 600 u and a Pyro within 350 u are worth -0.3; further off, nothing.</summary>
    [Fact]
    public void ANearHeavyAndANearPyroAreWorthLessToAScout()
    {
        TFField f = Scout(Flanker());
        KnownActor heavy = Seen(f, TFClass.HeavyWeapons, 500f);
        Assert.Equal((TFScoutClass.AvoidBonus, 0f), (PlayOf(f).TargetBonus(heavy, true, 500f), PlayOf(f).TargetBonus(heavy, true, 700f)));
        TFField g = Scout(Flanker());
        KnownActor pyro = Seen(g, TFClass.Pyro, 300f);
        Assert.Equal((TFScoutClass.AvoidBonus, 0f), (PlayOf(g).TargetBonus(pyro, true, 300f), PlayOf(g).TargetBonus(pyro, true, 400f)));
    }

    /// <summary>A Scout in a lull at 45 % of its health goes for a pack (its share is 0.5: no sustain of its own); at 55 % it does not.</summary>
    [Fact]
    public void AScoutAtFortyFivePercentGoesForAPack()
    {
        TFSkillSwitches(out TFFeelSwitches feel);
        var hurt = new TFField(slot: 3, mates: [1, 2], seed: SeedFor(TFScoutClass.Flanker), weapons: Weapons, feel: feel) { Hud = Flanker() };
        hurt.StepDrawn(Self(Scattergun, health: 56), TFField.Ctf());
        var fine = new TFField(slot: 3, mates: [1, 2], seed: SeedFor(TFScoutClass.Flanker), weapons: Weapons, feel: feel) { Hud = Flanker() };
        fine.StepDrawn(Self(Scattergun, health: 69), TFField.Ctf());
        Assert.Equal(("hurt", false), (hurt.Decider.Doing.Verdict, fine.Decider.Doing.Verdict == "hurt"));
    }

    /// <summary>The share alone decides (the Nerve's retreat off).</summary>
    private static void TFSkillSwitches(out TFFeelSwitches feel) => feel = TFFeelSwitches.All with { Retreat = false };

    /// <summary>The flag weighs a Flanker 1.1 times a Soldier's of the same seed (scout.md §4.3; 1.3 would beat a near fight everywhere).</summary>
    [Fact]
    public void TheFlagWeighsAFlankerMoreThanASoldier()
    {
        ulong seed = SeedFor(TFScoutClass.Flanker);
        var scout = new TFField(seed: seed, weapons: Weapons) { Hud = Flanker() };
        var soldier = new TFField(seed: seed, weapons: Weapons);
        scout.Step(Self(Scattergun), TFField.Ctf());
        soldier.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal(TFScoutClass.FetchLean, scout.Decider.Weigh(TFDecider.Fetch, 0.5f) / soldier.Decider.Weigh(TFDecider.Fetch, 0.5f), 3);
    }

    /// <summary>The flag weighs the BFB runner 1.2 times a Soldier's (scout.md §4.3 E).</summary>
    [Fact]
    public void TheFlagWeighsTheBabyFaceRunnerMore()
    {
        ulong seed = SeedFor(TFScoutClass.BabyFace);
        var hud = Kit(null, Item(TFLoadoutSlot.Primary, 772, "tf_weapon_pep_brawler_blaster", Babyface), StockPistol, StockBat);
        var scout = new TFField(seed: seed, weapons: Weapons) { Hud = hud };
        var soldier = new TFField(seed: seed, weapons: Weapons);
        scout.Step(Self(Babyface), TFField.Ctf());
        soldier.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal(TFScoutClass.BabyFaceFetchLean, scout.Decider.Weigh(TFDecider.Fetch, 0.5f) / soldier.Decider.Weigh(TFDecider.Fetch, 0.5f), 3);
    }

    /// <summary>A hurt Bonk runner carrying the enemy's flag does not drink (stock's gate, tf_bot.cpp:4566; the game refuses it too).</summary>
    [Fact]
    public void ABonkRunnerCarryingTheFlagDoesNotDrink()
    {
        TFField f = Scout(BonkKit(100f), TFScoutClass.Bonk);
        ObjectiveKnowledge carrying = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot);
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Scattergun, health: 40), carrying, EnemyAt(500f)))];
        Assert.DoesNotContain(decides, TakesOutTheDrink);
    }

    /// <summary>A Bonk runner at full health with an enemy in sight at 500 u and no sentry does not drink: the escape is under 0.4 of its health.</summary>
    [Fact]
    public void AHealthyBonkRunnerDoesNotDrinkToEscape()
    {
        TFField f = Scout(BonkKit(100f), TFScoutClass.Bonk);
        IntentCommand[] decides = [.. Enumerable.Range(0, 4).Select(_ => f.StepDrawn(Self(Scattergun, health: 125), TFField.Ctf(), EnemyAt(500f)))];
        Assert.DoesNotContain(decides, TakesOutTheDrink);
    }
}
