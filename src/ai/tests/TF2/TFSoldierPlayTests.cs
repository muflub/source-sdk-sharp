using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Soldier's play by its own kit (plan_bots_teams_tf2 P7; research p7-research/soldier.md §4.3 and §4.5, the review's
/// fixes): point blank and dry swaps, the melee, the banners, the Direct Hit's aim, the jump's cost by
/// kit, sentry shelling and the target bonuses. On the synthetic field (<see cref="TFField"/>), the HUD's kit set per fact.
/// </summary>
public sealed class TFSoldierPlayTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private const int Banner = 50;

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Banner, "tf_weapon_buff_item", 0, 0.5f, 0f, false, false, 0f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    private static readonly TFKitItem Launcher = Item(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", TFField.Launcher),
                                      Shotgun = Item(TFLoadoutSlot.Secondary, 10, "tf_weapon_shotgun", TFField.Shotgun),
                                      Shovel = Item(TFLoadoutSlot.Melee, 6, "tf_weapon_shovel", TFField.Shovel),
                                      BuffBanner = Item(TFLoadoutSlot.Secondary, 129, "tf_weapon_buff_item", Banner),
                                      Gunboats = Item(TFLoadoutSlot.Secondary, 133, "tf_wearable", -1, new TFOwnKitAttribute("rocket_jump_dmg_reduction", 0.4f)),
                                      DirectHit = Item(TFLoadoutSlot.Primary, 127, "tf_weapon_rocketlauncher_directhit", TFField.Launcher,
                                                       new TFOwnKitAttribute(TFItemFacts.SpeedScale, 1.8f), new TFOwnKitAttribute("mult_dmg", 1.25f));

    private static TFHud Kit(float rage, params TFKitItem[] items) => new([.. items], [new TFMeter(TFSoldierMeters.Rage, rage)], []);

    private static TFHud Kit(params TFKitItem[] items) => Kit(0f, items);

    private static TFHud StockKit() => Kit(Launcher, Shotgun, Shovel);

    private static TFField Field(TFHud kit, IJumpAdvisor? jumps = null)
        => new(weapons: Weapons, jumps: jumps, tune: p => p with { GlanceRate = 0f }) { Hud = kit };

    private static (ActorId, AiVector, AiVector, int) EnemyAt(float distance) => (Enemy, Mid + new AiVector(distance, 0f, 0f), AiVector.Zero, TFField.Blue);

    private static (ActorId, AiVector, AiVector, int) MateAt(float distance) => (Mate, Mid + new AiVector(0f, distance, 0f), AiVector.Zero, TFField.Red);

    private static bool Selects(in IntentCommand i, int kind) => i.Action.Kind == ActionKind.SelectWeapon && i.Action.WeaponSlot == EngineBotSelf.ByKind + kind;

    // ------------------------------------------------------------------ point blank, dry, melee

    /// <summary>A target inside the launcher's own blast is not held fire at: the loaded shotgun comes out (research mistake 1).</summary>
    [Fact]
    public void AtPointBlankTheLoadedShotgunComesOut()
    {
        var f = Field(StockKit());
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), EnemyAt(120f));
        Assert.True(Selects(i, TFField.Shotgun), $"{i.Action}");
    }

    /// <summary>At point blank with no secondary gun (a banner), the melee weapon comes out.</summary>
    [Fact]
    public void AtPointBlankWithNoGunTheShovelComesOut()
    {
        var f = Field(Kit(Launcher, BuffBanner, Shovel));
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), EnemyAt(100f));
        Assert.True(Selects(i, TFField.Shovel), $"{i.Action}");
    }

    /// <summary>A dry launcher is swapped for the shotgun inside stock's 500 u (tf_bot.cpp:3384), not at 550 u.</summary>
    [Fact]
    public void ADryLauncherIsSwappedInsideStocksRangeOnly()
    {
        IntentCommand At(float distance) => Field(StockKit()).Step(TFField.Self(Mid, clip: 0), TFField.Ctf(), EnemyAt(distance));
        Assert.True(Selects(At(450f), TFField.Shotgun));
        Assert.Equal(ActionKind.None, At(550f).Action.Kind);
    }

    /// <summary>A dry launcher with no gun and the target within the melee's swap range: the shovel.</summary>
    [Fact]
    public void ADryLauncherWithNoGunTakesTheShovelUpClose()
    {
        var f = Field(Kit(Launcher, Gunboats, Shovel));
        Assert.True(Selects(f.Step(TFField.Self(Mid, clip: 0), TFField.Ctf(), EnemyAt(130f)), TFField.Shovel));
    }

    /// <summary>The shotgun dry with the target still near: the shovel; the launcher's turn comes once the target is off.</summary>
    [Fact]
    public void ADryShotgunUpCloseGivesWayToTheShovel()
    {
        var f = Field(StockKit());
        Assert.True(Selects(f.Step(TFField.Self(Mid, clip: 0, weapon: TFField.Shotgun), TFField.Ctf(), EnemyAt(100f)), TFField.Shovel));
    }

    /// <summary>The shovel out with the target gone past its reach: the launcher, loaded when last in hand, comes back.</summary>
    [Fact]
    public void TheShovelGivesWayToTheLauncherOnceTheTargetIsOff()
    {
        var f = Field(StockKit());
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 0, weapon: TFField.Shovel), TFField.Ctf(), EnemyAt(600f));
        Assert.True(Selects(i, TFField.Launcher), $"{i.Action}");
    }

    // ------------------------------------------------------------------ banners

    /// <summary>A full banner with a teammate within 450 u and an enemy in sight is popped: the banner comes out.</summary>
    [Fact]
    public void AFullBannerWithAMateAndAnEnemyComesOut()
    {
        var f = Field(Kit(100f, Launcher, BuffBanner, Shovel));
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), EnemyAt(800f), MateAt(200f));
        Assert.True(Selects(i, Banner), $"{i.Action}");
    }

    /// <summary>A banner not full is not taken out (the control: the same scene full pops it, above).</summary>
    [Fact]
    public void ABannerNotFullStaysAway()
    {
        var f = Field(Kit(99f, Launcher, BuffBanner, Shovel));
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), EnemyAt(800f), MateAt(200f));
        Assert.False(Selects(i, Banner));
    }

    /// <summary>A full banner with no teammate within 450 u is held: popped alone it buffs nobody but the bot (research mistake 4).</summary>
    [Fact]
    public void AFullBannerWithNoMateNearIsHeld()
    {
        var f = Field(Kit(100f, Launcher, BuffBanner, Shovel));
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), EnemyAt(800f), MateAt(600f));
        Assert.False(Selects(i, Banner));
    }

    /// <summary>A full banner is never popped carrying the enemy's flag (stock's gate, tf_bot.cpp:4567).</summary>
    [Fact]
    public void AFullBannerIsNotPoppedCarrying()
    {
        var f = Field(Kit(100f, Launcher, BuffBanner, Shovel));
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(blue: ObjectiveState.Carried, blueCarrier: 2), EnemyAt(800f), MateAt(200f));
        Assert.False(Selects(i, Banner));
    }

    /// <summary>The banner in hand and full: the trigger deploys it.</summary>
    [Fact]
    public void TheBannerInHandIsDeployed()
    {
        var f = Field(Kit(100f, Launcher, BuffBanner, Shovel));
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 0, weapon: Banner), TFField.Ctf(), MateAt(200f));
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>
    /// The pop is pressed through the friend gate with the teammate it was due for straight ahead in the view's line (the
    /// review's C1: the gate cancelled it, and the bot stood in the fight with the banner out and no weapon).
    /// </summary>
    [Fact]
    public void TheBannerIsDeployedWithAMateStraightAhead()
    {
        var f = Field(Kit(100f, Launcher, BuffBanner, Shovel));
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 0, weapon: Banner), TFField.Ctf(), EnemyAt(800f), (Mate, Mid + new AiVector(200f, 0f, 0f), AiVector.Zero, TFField.Red));
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>A banner out and pressed past its time with the meter still full (the pop is not happening) gives way to the launcher.</summary>
    [Fact]
    public void ABannerThatDoesNotPopGivesWayToTheLauncher()
    {
        var f = Field(Kit(100f, Launcher, BuffBanner, Shovel));
        IntentCommand[] decides = [.. Enumerable.Range(0, 30).Select(_ => f.Step(TFField.Self(Mid, clip: 0, weapon: Banner), TFField.Ctf(), EnemyAt(800f), MateAt(200f)))];
        Assert.Contains(decides, i => Selects(i, TFField.Launcher));
    }

    /// <summary>A banner out with the meter spent goes back to the launcher at once.</summary>
    [Fact]
    public void ASpentBannerInHandGoesBackToTheLauncher()
    {
        var f = Field(Kit(40f, Launcher, BuffBanner, Shovel));
        Assert.True(Selects(f.Step(TFField.Self(Mid, clip: 0, weapon: Banner), TFField.Ctf(), EnemyAt(800f)), TFField.Launcher));
    }

    private static long Counter(TFField f, string name) => f.Decider.ClassCounters().Single(c => c.Key == name).Value;

    /// <summary>A pop is counted when the meter falls from full (the game's deploy draining it), not when the trigger is pressed.</summary>
    [Fact]
    public void APopIsCountedWhenTheMeterFallsNotWhenPressed()
    {
        var f = Field(Kit(100f, Launcher, BuffBanner, Shovel));
        for (int k = 0; k < 3; k++)
            f.Step(TFField.Self(Mid, clip: 0, weapon: Banner), TFField.Ctf(), MateAt(200f));
        Assert.Equal(0, Counter(f, "soldier.banner_pops"));
        f.Hud = Kit(97f, Launcher, BuffBanner, Shovel);
        f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), MateAt(200f));
        Assert.Equal(1, Counter(f, "soldier.banner_pops"));
    }

    // ------------------------------------------------------------------ the Direct Hit, the jump's cost

    /// <summary>The Direct Hit aims at the body of a grounded target 800 u off; the stock launcher at its feet (its 44 u blast misses at the feet).</summary>
    [Fact]
    public void TheDirectHitAimsAtTheBodyWhereTheStockLauncherAimsAtTheFeet()
    {
        AimPoint Aim(TFKitItem launcher) => Field(Kit(launcher, Shotgun, Shovel)).Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), EnemyAt(800f)).Look.Aim;
        Assert.Equal(AimPoint.Feet, Aim(Launcher));
        Assert.Equal(AimPoint.Body, Aim(DirectHit));
    }

    /// <summary>
    /// A jump's cost is the kit's share of the plan's (the Gunboats pay 0.4): with 105 health a jump that costs 30 takes a
    /// stock Soldier under its floor of 90 (75), and a Gunboats Soldier, who pays 12, not (93).
    /// </summary>
    [Fact]
    public void TheGunboatsTakeAJumpTheStockKitCannotAfford()
    {
        int Jumps(TFHud kit)
        {
            var f = Field(kit, new TFJumpAdvice());
            return Enumerable.Range(0, 20).Select(_ => f.Step(TFField.Self(new AiVector(300f, 256f, 0f), health: 105, velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf()))
                             .Count(i => f.BeginsJump(i));
        }

        Assert.Equal(0, Jumps(StockKit()));
        Assert.Equal(1, Jumps(Kit(Launcher, Gunboats, Shovel)));
    }

    /// <summary>
    /// The Direct Hit's rocket does 1.25 times the damage, its self blast too: at 125 health a jump that costs 30 leaves a
    /// stock Soldier 95 (over its floor of 90) and a Direct Hit Soldier 87.5 (under it).
    /// </summary>
    [Fact]
    public void TheDirectHitPaysItsLaunchersDamageForAJump()
    {
        int Jumps(TFHud kit)
        {
            var f = Field(kit, new TFJumpAdvice());
            return Enumerable.Range(0, 20).Select(_ => f.Step(TFField.Self(new AiVector(300f, 256f, 0f), health: 125, velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf()))
                             .Count(i => f.BeginsJump(i));
        }

        Assert.Equal(1, Jumps(StockKit()));
        Assert.Equal(0, Jumps(Kit(DirectHit, Shotgun, Shovel)));
    }

    /// <summary>A kit with no_self_blast_dmg pays nothing for a jump: at 95 health it takes the jump a stock kit cannot.</summary>
    [Fact]
    public void NoSelfBlastDamageMeansAFreeJump()
    {
        int Jumps(TFHud kit)
        {
            var f = Field(kit, new TFJumpAdvice());
            return Enumerable.Range(0, 20).Select(_ => f.Step(TFField.Self(new AiVector(300f, 256f, 0f), health: 95, velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf()))
                             .Count(i => f.BeginsJump(i));
        }

        Assert.Equal(0, Jumps(StockKit()));
        Assert.Equal(1, Jumps(Kit(Item(TFLoadoutSlot.Primary, 237, "tf_weapon_rocketlauncher", TFField.Launcher, new TFOwnKitAttribute("no_self_blast_dmg", 1f)), Shotgun, Shovel)));
    }

    // ------------------------------------------------------------------ targets

    /// <summary>A Sniper is picked over a Soldier a little nearer (research [W2]: fragile high-value classes first).</summary>
    [Fact]
    public void ASniperIsPickedOverANearerSoldier()
    {
        var sniper = new ActorId(6, 0x4000_0001);
        var f = Field(StockKit());
        IntentCommand i = f.StepDrawn(TFField.Self(Mid, clip: 4), TFField.Ctf(),
                                      new ActorFacts(Enemy, Mid + new AiVector(700f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue,
                                                     Class: (int)TFClass.Soldier),
                                      new ActorFacts(sniper, Mid + new AiVector(760f, 64f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue,
                                                     Class: (int)TFClass.Sniper));
        Assert.Equal(sniper, f.Decider.Doing.Target);
    }

    // ------------------------------------------------------------------ the Pyro

    private static ActorFacts Pyro(float distance, float yaw)
        => new(Enemy, Mid + new AiVector(distance, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Pyro,
               Facing: new AiAngles(0f, yaw, 0f));

    /// <summary>A Pyro 300 u off facing the bot gets no rocket (it would come back, research mistake 2): the shotgun comes out instead.</summary>
    [Fact]
    public void APyroFacingTheBotGetsTheShotgunNotARocket()
    {
        IntentCommand i = Field(StockKit()).StepDrawn(TFField.Self(Mid, clip: 4), TFField.Ctf(), Pyro(300f, 180f));
        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
        Assert.True(Selects(i, TFField.Shotgun), $"{i.Action}");
    }

    /// <summary>The same Pyro facing away is rocketed as any target (the control of the fact above).</summary>
    [Fact]
    public void APyroFacingAwayIsRocketed()
    {
        IntentCommand i = Field(StockKit()).StepDrawn(TFField.Self(Mid, clip: 4), TFField.Ctf(), Pyro(300f, 0f));
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    // ------------------------------------------------------------------ sentries

    private static readonly ThingFacts Sentry = new(77, ThingKind.Building, (int)TFThingType.Sentry, new AiVector(3500f, 256f, 0f), AiVector.Zero, TFField.Blue,
                                                    Level: 1, Health: 150);

    /// <summary>An enemy sentry in view from between 1.1 and 1.5 times its range, nobody else in sight: the launcher is fired at its body.</summary>
    [Fact]
    public void ASentryInViewFromTheBandIsShelled()
    {
        var f = Field(StockKit());
        IntentCommand[] decides = [.. Enumerable.Range(0, 5).Select(_ => f.StepSeeing(TFField.Self(Mid, clip: 4), TFField.Ctf(), [Sentry]))];
        Assert.Contains(decides, i => i.Action.Kind == ActionKind.Fire && i.Look.Kind == LookKind.Point && i.Look.Point.DistanceTo(Sentry.Origin) < 64f);
    }

    /// <summary>A sentry seen from afar and out of view now: the bot goes to a spot in the band, along its line to the sentry (stock 1.1-1.5 x its range).</summary>
    [Fact]
    public void ARememberedSentryIsShelledFromTheBand()
    {
        var f = Field(StockKit());
        var far = new AiVector(1000f, 256f, 0f);
        f.StepSeeing(TFField.Self(far, clip: 4), TFField.Ctf(), [Sentry]);
        f.Step(TFField.Self(far, clip: 4), TFField.Ctf());
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        // Stock's band, 1.1 to 1.5 times the sentry's 1,100 u (tf_bot_destroy_enemy_sentry.cpp:244,304,325), by number.
        Assert.Equal(1100f, TFKnowledge.SentryRange);
        Assert.InRange(f.Decider.Doing.Goal.DistanceTo(Sentry.Origin), 1210f, 1650f);
    }

    /// <summary>A sentry seen from past shelling's reach (3,000 u) is not gone for: the bot keeps to its objective.</summary>
    [Fact]
    public void ASentryPastTheReachIsNotShelled()
    {
        var f = Field(StockKit());
        var far = new AiVector(400f, 256f, 0f);
        f.StepSeeing(TFField.Self(far, clip: 4), TFField.Ctf(), [Sentry]);
        f.Step(TFField.Self(far, clip: 4), TFField.Ctf());
        Assert.NotEqual(TFDecider.ClassBranch, f.Branch);
    }

    /// <summary>
    /// At the shelling spot with no sight of the sentry there is no line of fire from it (stock tests one for its spot): the
    /// sentry is given up, and the bot goes back to its objective.
    /// </summary>
    [Fact]
    public void AShellingSpotWithNoLineOfFireIsGivenUp()
    {
        var f = Field(StockKit());
        var far = new AiVector(1000f, 256f, 0f);
        f.StepSeeing(TFField.Self(far, clip: 4), TFField.Ctf(), [Sentry]);
        f.Step(TFField.Self(far, clip: 4), TFField.Ctf());
        AiVector spot = f.Decider.Doing.Goal;
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        for (int k = 0; k < 3; k++)
            f.Step(TFField.Self(spot, clip: 4), TFField.Ctf());
        Assert.NotEqual(TFDecider.ClassBranch, f.Branch);
        Assert.Equal(1, Counter(f, "soldier.shell_give_ups"));
    }

    /// <summary>A sentry shelled past its time (15 s) is given up: still shelled at 10 s, not at 18 s (rockets from the band do not kill a tended nest).</summary>
    [Fact]
    public void ShellingASentryGivesUpAfterItsTime()
    {
        var f = Field(StockKit());
        int Decides(float seconds) => (int)(seconds / (0.015f * DecideSchedule.Period));
        for (int k = 0; k < Decides(10f); k++)
            f.StepSeeing(TFField.Self(Mid, clip: 4), TFField.Ctf(), [Sentry]);
        Assert.Equal((TFDecider.ClassBranch, 0L), (f.Branch, Counter(f, "soldier.shell_give_ups")));
        for (int k = 0; k < Decides(8f); k++)
            f.StepSeeing(TFField.Self(Mid, clip: 4), TFField.Ctf(), [Sentry]);
        Assert.NotEqual(TFDecider.ClassBranch, f.Branch);
        Assert.Equal(1, Counter(f, "soldier.shell_give_ups"));
    }

    /// <summary>
    /// A sentry that does not cover the bot's objective (far from the enemy's flag) is passed by for the objective: by the
    /// scores (shelling's 0.45 under a far Fetch's 0.7, which drops to a quarter only for a flag inside a sentry's range) and by
    /// the cover rule (no shelling of a sentry that guards nothing the bot goes for).
    /// </summary>
    [Fact]
    public void ASentryThatGuardsNoObjectiveIsPassedBy()
    {
        var f = Field(StockKit());
        var aside = Sentry with { Origin = new AiVector(700f, 256f, 0f) };
        f.StepSeeing(TFField.Self(Mid, clip: 4), TFField.Ctf(), [aside]);
        f.StepSeeing(TFField.Self(Mid, clip: 4), TFField.Ctf(), [aside]);
        Assert.NotEqual(TFDecider.ClassBranch, f.Branch);
    }

    // ------------------------------------------------------------------ the meter

    /// <summary>
    /// No two readers of the HUD's meters share a place in their chain (the binding's own census looks only at chains named
    /// "...Hook"): two at one order throw at the chain's first call, and live the medigun's and the Soldier's, both at 0,
    /// made every sense tick throw, so no bot was ever given a class.
    /// </summary>
    [Fact]
    public void NoTwoHudMeterReadersShareAnOrder()
    {
        static int Order(Type t)
            => typeof(IOrderedHook).IsAssignableFrom(t) ? ((IOrderedHook)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).HookOrder : 0;
        Type[] readers = [.. typeof(TFSoldierMeters).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t))];
        Assert.Contains(typeof(TFSoldierMeters), readers);
        Assert.Contains(typeof(TFMedigunMeters), readers);
        Assert.Empty(readers.GroupBy(Order).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Name))}"));
    }
}
