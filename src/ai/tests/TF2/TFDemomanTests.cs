using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Demoman's play, the stock Hybrid (plan_bots_teams_tf2 P7; .scratch/p7-research/demoman.md §4.3, §4.5): the
/// weapon by range and ammunition, the stickies lobbed and detonated by an enemy who stands by an armed one, the
/// sentry killed with stickies from past its range, the kit read off the bot's own HUD.
/// </summary>
public sealed class TFDemomanTests
{
    internal static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0001);
    internal static readonly AiVector Mid = new(2048f, 256f, 0f);

    internal const int Pipes = 40, Stickies = 41, Bottle = 42, Sword = 43;

    internal static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Pipes, "tf_weapon_grenadelauncher", 4, 0.6f, 2.4f, true, true, 1200f, ProjectileGravity: TFKnowledge.ProjectileGravity(TFProjectile.Pipebomb),
                       LaunchUp: TFKnowledge.LaunchUp(TFProjectile.Pipebomb)),
        new WeaponFacts(Stickies, "tf_weapon_pipebomblauncher", 8, 0.6f, 4f, true, true, 900f, ProjectileGravity: TFKnowledge.ProjectileGravity(TFProjectile.PipebombRemote), ChargeSeconds: 4f,
                       LaunchUp: TFKnowledge.LaunchUp(TFProjectile.PipebombRemote)),
        new WeaponFacts(Bottle, "tf_weapon_bottle", 0, 0.8f, 0f, false, false, 0f),
        new WeaponFacts(Sword, "tf_weapon_sword", 0, 0.8f, 0f, false, false, 0f),
    ]);

    internal static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    /// <summary>The stock kit with the Quickiebomb Launcher (1150) for the stickies: its charge rate 0.3.</summary>
    internal static TFHud QuickieKit()
        => new([Item(TFLoadoutSlot.Primary, 19, "tf_weapon_grenadelauncher", Pipes),
                Item(TFLoadoutSlot.Secondary, 1150, "tf_weapon_pipebomblauncher", Stickies, new TFOwnKitAttribute(TFProjectileBehaviour.ChargeRateAttribute, 0.3f)),
                Item(TFLoadoutSlot.Melee, 1, "tf_weapon_bottle", Bottle)], [], []);

    /// <summary>A sticky launcher's full charge is its own (GetChargeMaxTime): the stock launcher's 4 s, the Quickiebomb's 1.2 s.</summary>
    [Fact]
    public void EachStickyLauncherChargesForItsOwnTime()
    {
        Assert.Equal(TFProjectileBehaviour.MaxChargeTime, TFDemomanKit.Of(StockKit()).StickyChargeSeconds);
        Assert.Equal(1.2f, TFDemomanKit.Of(QuickieKit()).StickyChargeSeconds, 3);
    }

    /// <summary>The stock Hybrid's kit: the Grenade Launcher, the Stickybomb Launcher, the Bottle.</summary>
    internal static TFHud StockKit(params TFMeter[] meters)
        => new([Item(TFLoadoutSlot.Primary, 19, "tf_weapon_grenadelauncher", Pipes), Item(TFLoadoutSlot.Secondary, 20, "tf_weapon_pipebomblauncher", Stickies),
                Item(TFLoadoutSlot.Melee, 1, "tf_weapon_bottle", Bottle)], [.. meters], []);

    internal static TFField Demo(int slot = 2, ulong seed = 7) => new(slot: slot, seed: seed, weapons: Weapons) { Hud = StockKit() };

    internal static SelfState Self(AiVector at, int weapon, int clip, int reserve = 20, int health = 175)
        => TFField.Self(at, health: health, clip: clip, reserve: reserve, weapon: weapon, playerClass: TFClass.Demoman);

    internal static ActorFacts Drawn(ActorId id, AiVector at, int team, ActorConditions conditions = ActorConditions.None)
        => new(id, at, AiVector.Zero, Pipes, ActorActivity.None, 1f, team, (int)TFClass.Soldier, conditions);

    internal static ThingFacts Sticky(int id, AiVector at, int team, AiVector velocity = default)
        => new(id, ThingKind.Projectile, (int)TFThingType.Sticky, at, velocity, team);

    private static bool Detonates(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row)
           && row.Name == TFDemomanClass.Detonate;

    // ------------------------------------------------------------------ the kit

    /// <summary>The stock kit reads as pipes and stickies, eight out, armed in 0.8 s.</summary>
    [Fact]
    public void TheStockKitReadsAsPipesAndStickies()
    {
        TFDemomanKit k = TFDemomanKit.Of(StockKit());
        Assert.Equal((true, true, false, false, 8, 0.8f), (k.Pipes, k.Stickies, k.Shield, k.InViewDetonate, k.MaxStickies, k.ArmSeconds));
    }

    /// <summary>The Scottish Resistance's attributes read as its own: detonated in view, fourteen out, armed 0.8 s later.</summary>
    [Fact]
    public void TheScottishResistanceReadsAsInViewWithFourteen()
    {
        var hud = new TFHud([Item(TFLoadoutSlot.Secondary, 130, "tf_weapon_pipebomblauncher", Stickies, new("set_detonate_mode", 1f),
                                  new("add_max_pipebombs", 6f), new("sticky_arm_time", 0.8f))], [], []);
        TFDemomanKit k = TFDemomanKit.Of(hud);
        Assert.Equal((true, 14, 1.6f), (k.InViewDetonate, k.MaxStickies, k.ArmSeconds));
    }

    /// <summary>A shield in the secondary slot reads as a shield and no stickies.</summary>
    [Fact]
    public void AShieldReadsAsNoStickies()
    {
        var hud = new TFHud([Item(TFLoadoutSlot.Secondary, 131, "tf_wearable_demoshield", -1)], [], []);
        TFDemomanKit k = TFDemomanKit.Of(hud);
        Assert.Equal((true, false), (k.Shield, k.Stickies));
    }

    // ------------------------------------------------------------------ the hands

    /// <summary>With the stickies in hand and a target at 700 u, the hands take out the pipes.</summary>
    [Fact]
    public void AtMidRangeThePipesAreTakenOut()
    {
        TFField f = Demo();
        IntentCommand i = f.StepDrawn(Self(Mid, Stickies, 8), null, Drawn(Enemy, Mid + new AiVector(700f, 0f, 0f), TFField.Blue));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Pipes), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>With the pipes in hand and a target at 280 u, the hands take out the stickies to lay at it.</summary>
    [Fact]
    public void ACloseTargetBringsOutTheStickies()
    {
        TFField f = Demo();
        IntentCommand i = f.StepDrawn(Self(Mid, Pipes, 4), null, Drawn(Enemy, Mid + new AiVector(280f, 0f, 0f), TFField.Blue));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Stickies), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>The pipes dry at 700 u: the stickies, loaded, are taken out rather than waiting on a reload.</summary>
    [Fact]
    public void DryPipesGiveWayToTheStickies()
    {
        TFField f = Demo();
        IntentCommand i = f.StepDrawn(Self(Mid, Pipes, 0), null, Drawn(Enemy, Mid + new AiVector(700f, 0f, 0f), TFField.Blue));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Stickies), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>A target inside 180 u: the melee weapon, no blast weapon at point blank.</summary>
    [Fact]
    public void AtPointBlankTheMeleeWeaponIsTakenOut()
    {
        TFField f = Demo();
        IntentCommand i = f.StepDrawn(Self(Mid, Pipes, 4), null, Drawn(Enemy, Mid + new AiVector(120f, 0f, 0f), TFField.Blue));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Bottle), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>In a lull with the stickies in hand and not full, they are reloaded before the pipes come out.</summary>
    [Fact]
    public void InALullTheStickiesAreReloadedFirst()
    {
        TFField f = Demo();
        IntentCommand i = f.Step(Self(Mid, Stickies, 5), null);
        Assert.Equal(ActionKind.Reload, i.Action.Kind);
    }

    /// <summary>In a lull with the stickies in hand and full, the pipes come out.</summary>
    [Fact]
    public void InALullFullStickiesGiveWayToThePipes()
    {
        TFField f = Demo();
        IntentCommand i = f.Step(Self(Mid, Stickies, 8), null);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Pipes), (i.Action.Kind, i.Action.WeaponSlot));
    }

    // ------------------------------------------------------------------ the stickies

    /// <summary>
    /// A sticky at a target 300 u off is tapped (uncharged) on the launch's lob: the look drops below the target's feet, the
    /// game's 200 u/s up-kick lifting the sticky onto them (a lob blind to the kick looked above them and flew long).
    /// </summary>
    [Fact]
    public void AStickyIsTappedOnALob()
    {
        TFField f = Demo();
        IntentCommand i = f.StepDrawn(Self(Mid, Stickies, 8), null, Drawn(Enemy, Mid + new AiVector(300f, 0f, 0f), TFField.Blue));
        Assert.Equal((ActionKind.Fire, FireStyle.Tap, LookKind.Actor), (i.Action.Kind, i.Action.Style, i.Look.Kind));
        Assert.True(i.Look.Point.Z < -5f, $"aimed at z {i.Look.Point.Z}");
    }

    /// <summary>
    /// A sticky laid by a target (the clip fell after the tap; the team's sticky seen resting there) is detonated once
    /// armed, with the enemy in sight by it. Returns the decides from the launch to the detonation's, or -1 for none in 30.
    /// </summary>
    internal static int DecidesToDetonate(TFField f, Func<int, ActorFacts[]> sightedAt, Func<AiVector, ThingFacts[]>? things = null, int clipAfter = 7)
    {
        AiVector landing = Mid + new AiVector(300f, 0f, 0f);
        f.StepDrawn(Self(Mid, Stickies, 8), null, sightedAt(0));
        for (int n = 1; n <= 30; n++)
        {
            ThingFacts[] seen = things?.Invoke(landing) ?? [Sticky(77, landing, TFField.Red)];
            IntentCommand i = f.StepSeeing(Self(Mid, Stickies, clipAfter), null, seen, null, sightedAt(n));
            if (Detonates(f, in i))
                return n;
        }

        return -1;
    }

    /// <summary>An own armed sticky with an enemy in sight 100 u from it (within stock's 150) is detonated.</summary>
    [Fact]
    public void AnEnemyByAnArmedStickyIsDetonatedOn()
    {
        TFField f = Demo();
        int at = DecidesToDetonate(f, n => [Drawn(Enemy, Mid + new AiVector(300f, n == 0 ? 0f : 100f, 0f), TFField.Blue)]);
        Assert.True(at > 0, "no detonation");
    }

    /// <summary>An enemy in sight 220 u from the bot's armed sticky, past stock's 150, is not detonated on.</summary>
    [Fact]
    public void AnEnemyPastTheBlastIsNotDetonatedOn()
    {
        TFField f = Demo();
        int at = DecidesToDetonate(f, n => [Drawn(Enemy, Mid + new AiVector(300f, n == 0 ? 0f : 220f, 0f), TFField.Blue)]);
        Assert.Equal(-1, at);
    }

    /// <summary>A sticky not yet armed (0.8 s from its launch) is not detonated, though the enemy stands on it.</summary>
    [Fact]
    public void AnUnarmedStickyIsNotDetonated()
    {
        TFField f = Demo();
        int at = DecidesToDetonate(f, _ => [Drawn(Enemy, Mid + new AiVector(300f, 0f, 0f), TFField.Blue)]);
        float seconds = at * DecideSchedule.Period * 0.015f;
        Assert.True(seconds >= TFProjectileArm, $"detonated at {seconds} s");
    }

    private const float TFProjectileArm = 0.8f;

    /// <summary>A teammate by the bot's armed sticky is no reason to detonate (the enemy who led it there has gone).</summary>
    [Fact]
    public void ATeammateByAStickyIsNotDetonatedOn()
    {
        TFField f = Demo();
        int at = DecidesToDetonate(f, n => n == 0 ? [Drawn(Enemy, Mid + new AiVector(300f, 0f, 0f), TFField.Blue)]
                                                  : [Drawn(Mate, Mid + new AiVector(300f, 0f, 0f), TFField.Red)]);
        Assert.Equal(-1, at);
    }

    /// <summary>
    /// An enemy's sticky is never taken for the bot's own: the bot laid one toward a spot, an enemy's lands 250 u from it
    /// with an enemy by it (and none by the spot), and nothing is detonated.
    /// </summary>
    [Fact]
    public void AnEnemysStickyIsNotTheBots()
    {
        TFField f = Demo();
        AiVector theirs = Mid + new AiVector(300f, 250f, 0f);
        int at = DecidesToDetonate(f, n => n == 0 ? [Drawn(Enemy, Mid + new AiVector(300f, 0f, 0f), TFField.Blue)] : [Drawn(Enemy, theirs, TFField.Blue)],
                                   _ => [Sticky(78, theirs, TFField.Blue)]);
        Assert.Equal(-1, at);
    }

    /// <summary>With an own armed sticky by the bot itself, an enemy by another is not detonated on: the blast would take the bot.</summary>
    [Fact]
    public void NoDetonationWithAStickyByTheBot()
    {
        TFField f = Demo();
        int at = DecidesToDetonate(f, _ => [Drawn(Enemy, Mid + new AiVector(300f, 0f, 0f), TFField.Blue)],
                                   landing => [Sticky(77, landing, TFField.Red), Sticky(79, Mid + new AiVector(40f, 0f, 0f), TFField.Red)], clipAfter: 6);
        // Two left (the clip fell by two): the second came to rest at the bot's feet.
        Assert.Equal(-1, at);
    }

    /// <summary>
    /// A sticky of the bot's own on a ledge 197 u over its feet and 25 u aside, armed: 199 u from the feet, but about 115 u
    /// from the top of its hull, inside the blast. An enemy by another armed sticky is not detonated on (live, cp_granary:
    /// a guard measured from the feet passed this and the bot killed himself).
    /// </summary>
    [Fact]
    public void NoDetonationWithAStickyOverTheBot()
    {
        TFField f = Demo();
        int at = DecidesToDetonate(f, _ => [Drawn(Enemy, Mid + new AiVector(300f, 0f, 0f), TFField.Blue)],
                                   landing => [Sticky(77, landing, TFField.Red), Sticky(79, Mid + new AiVector(25f, 0f, 197f), TFField.Red)], clipAfter: 6);
        Assert.Equal(-1, at);
    }

    /// <summary>
    /// A sticky of the bot's own seen still moving 60 u from it, older than its arm time (the game detonates by age alone),
    /// though aimed far off (its flight not yet over by the bot's reckoning): an enemy by another armed sticky is not
    /// detonated on.
    /// </summary>
    [Fact]
    public void NoDetonationWithAMovingStickyByTheBot()
    {
        TFField f = Demo();
        AiVector near = Mid + new AiVector(300f, 0f, 0f), far = Mid + new AiVector(1200f, 0f, 0f);
        f.StepDrawn(Self(Mid, Stickies, 8), null, Drawn(Enemy, far, TFField.Blue));
        f.StepDrawn(Self(Mid, Stickies, 7), null, Drawn(Enemy, near, TFField.Blue));
        IntentCommand[] decides = [.. Enumerable.Range(0, 30).Select(_ => f.StepSeeing(Self(Mid, Stickies, 6), null,
            [Sticky(77, near, TFField.Red), Sticky(81, Mid + new AiVector(60f, 0f, 0f), TFField.Red, new AiVector(150f, 0f, 0f))], null, Drawn(Enemy, near, TFField.Blue)))];
        Assert.DoesNotContain(decides, i => Detonates(f, in i));
    }

    // ------------------------------------------------------------------ the sentry

    internal static ThingFacts Sentry(AiVector at, int level = 1) => new(900, ThingKind.Building, (int)TFThingType.Sentry, at, default, TFField.Blue, 0, level, -1);

    /// <summary>A sentry seen 1,600 u off: the Demoman goes to its stand, past its range on his side, by the class's own branch.</summary>
    [Fact]
    public void ASeenSentryIsGoneAfterFromPastItsRange()
    {
        TFField f = Demo();
        AiVector sentry = Mid + new AiVector(1600f, 0f, 0f);
        IntentCommand i = f.StepSeeing(Self(Mid, Pipes, 4), null, [Sentry(sentry)]);
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        float stand = new AiVector(f.Decider.Doing.Goal.X - sentry.X, f.Decider.Doing.Goal.Y - sentry.Y, 0f).Length;
        Assert.True(stand > TFKnowledge.SentryRange, $"stand {stand} u from the sentry");
    }

    /// <summary>
    /// At the stand (1,250 u, past a tap's 1,060 u reach) with the sentry in sight and the stickies in hand, the sticky is
    /// charged and let go on the arc that lands on it: the trigger held (Spray) over decides, then a decide with no fire throws
    /// it, the look on an arc above the sentry. Was a tap aimed straight at the sentry, the solver having no arc for 900 u/s,
    /// which this fact passed.
    /// </summary>
    [Fact]
    public void AtTheStandAStickyIsChargedAndLetGoOnItsArc()
    {
        TFField f = Demo();
        AiVector sentry = Mid + new AiVector(1250f, 0f, 0f);
        var decides = new List<(IntentCommand Intent, string Verdict)>();
        for (int i = 0; i < 40; i++)
            decides.Add((f.StepSeeing(Self(Mid, Stickies, 8), null, [Sentry(sentry)]), f.Decider.Doing.Verdict));
        int charging = decides.FindIndex(d => d.Verdict == "charging a sticky at a sentry");
        Assert.True(charging >= 0, "no charge");
        (IntentCommand held, _) = decides[charging];
        Assert.Equal((ActionKind.Fire, FireStyle.Spray), (held.Action.Kind, held.Action.Style));
        Assert.True(held.Look.Point.Z > sentry.Z + 100f, $"look z {held.Look.Point.Z}"); // an arc, not the sentry itself
        WeaponFacts sticky = Weapons.All.First(w => w.Kind == Stickies);
        (_, AiVector look) = TFDemomanClass.SentryThrow(f.Decider.OwnEye, sentry + new AiVector(0f, 0f, 8f), in sticky)!.Value;
        Assert.True(held.Look.Point.DistanceTo(look) < 1f, $"look {held.Look.Point}, the throw's {look}");
        int release = decides.FindIndex(charging, d => d.Intent.Action.Kind != ActionKind.Fire);
        Assert.True(release > charging + 1, $"released at {release}"); // held over decides, then let go
        Assert.DoesNotContain(decides, d => d.Intent.Action.Kind == ActionKind.Fire && d.Intent.Action.Style != FireStyle.Spray);
    }

    /// <summary>
    /// <see cref="TFDemomanClass.SentryReach"/> (2,500 u) is a throw a sticky can make: a full charge flown with the game's
    /// launch, fall and drag has an arc onto a sentry that far off.
    /// </summary>
    [Fact]
    public void AFullChargeHasAnArcOntoASentryAtTheSentryReach()
    {
        AiVector eye = new(0f, 0f, 68f), sentry = new(TFDemomanClass.SentryReach, 0f, 8f);
        float gravity = TFKnowledge.ProjectileGravity(TFProjectile.PipebombRemote), up = TFKnowledge.LaunchUp(TFProjectile.PipebombRemote);
        Assert.NotNull(AimLead.LobLaunchDragged(eye, sentry, global::TF2.Server.Projectiles.TFProjectileDefs.StickyMaxChargeSpeed, gravity, up, TFKnowledge.GrenadeDrag));
        Assert.Null(AimLead.LobLaunchDragged(eye, sentry, global::TF2.Server.Projectiles.TFProjectileDefs.StickyMinChargeSpeed, gravity, up, TFKnowledge.GrenadeDrag));
    }

    /// <summary>
    /// The throw from the stand, flown as the game flies it (the launch's kick, the fall, the drag), lands within half a body of
    /// the sentry at 1,250 and 1,600 u, and is charged past a tap.
    /// </summary>
    [Theory]
    [InlineData(1250f)]
    [InlineData(1600f)]
    public void TheSentryThrowLandsOnTheSentry(float distance)
    {
        WeaponFacts sticky = Weapons.All.First(w => w.Kind == Stickies);
        AiVector eye = new(0f, 0f, 68f), aim = new(distance, 0f, 8f);
        (float speed, AiVector look) = TFDemomanClass.SentryThrow(eye, aim, in sticky)!.Value;
        Assert.True(speed > sticky.ProjectileSpeed, $"speed {speed}");
        AiVector landed = AimLead.LandsAt(eye, look, speed, sticky.ProjectileGravity, sticky.LaunchUp, TFKnowledge.GrenadeDrag, aim.Z)!.Value;
        Assert.True(MathF.Abs(landed.X - distance) <= 41f, $"landed at {landed.X:F0} u");
    }

    /// <summary>
    /// The trigger is held for the charge that gives <see cref="TFDemomanClass.SentryThrow"/>'s speed
    /// (<see cref="TFDemomanClass.StickyChargeFor"/>), to within one decide (6 ticks of 0.015 s): the length of the hold is
    /// what sets the throw's speed. The charge is the held launcher's own: 4 s for the stock launcher, 1.2 s for the
    /// Quickiebomb (stickybomb_charge_rate 0.3), from 1,600 u where its hold is long enough to tell.
    /// </summary>
    [Theory]
    [InlineData(false, 1250f)]
    [InlineData(true, 1600f)]
    public void AtTheStandTheChargeIsHeldForTheThrowsSpeed(bool quickie, float distance)
    {
        TFField f = Demo();
        if (quickie)
            f.Hud = QuickieKit();
        AiVector sentry = Mid + new AiVector(distance, 0f, 0f);
        var decides = new List<(IntentCommand Intent, string Verdict)>();
        for (int i = 0; i < 60; i++)
            decides.Add((f.StepSeeing(Self(Mid, Stickies, 8), null, [Sentry(sentry)]), f.Decider.Doing.Verdict));
        int charging = decides.FindIndex(d => d.Verdict == "charging a sticky at a sentry");
        Assert.True(charging >= 0, "no charge");
        int release = decides.FindIndex(charging, d => d.Intent.Action.Kind != ActionKind.Fire);
        Assert.True(release > charging, "never let go");

        WeaponFacts sticky = Weapons.All.First(w => w.Kind == Stickies);
        (float speed, _) = TFDemomanClass.SentryThrow(f.Decider.OwnEye, sentry + new AiVector(0f, 0f, 8f), in sticky)!.Value;
        float want = TFDemomanClass.StickyChargeFor(speed, quickie ? 1.2f : TFProjectileBehaviour.MaxChargeTime);
        const float decide = DecideSchedule.Period * 0.015f;
        float held = (release - charging) * decide;
        Assert.True(want > 2f * decide, $"the charge {want:F2} s is too short to tell");
        Assert.True(MathF.Abs(held - want) <= decide + 1e-3f, $"held {held:F2} s for a {want:F2} s charge");
    }

    /// <summary>A tap's best reach on level ground is short of the stand, a full charge's past it: the reason the throw charges.</summary>
    [Fact]
    public void ATapFallsShortOfTheStandAndAFullChargeReachesIt()
    {
        float stand = TFKnowledge.SentryRange + TFDemomanClass.SentryStandOff;
        Assert.True(TFDemomanClass.StickyLevelReach(global::TF2.Server.Projectiles.TFProjectileDefs.StickyMinChargeSpeed) < stand);
        Assert.True(TFDemomanClass.StickyLevelReach(global::TF2.Server.Projectiles.TFProjectileDefs.StickyMaxChargeSpeed) > stand);
    }

    /// <summary>Inside the sentry's range no sticky is lobbed at it (the bot backs out instead).</summary>
    [Fact]
    public void InsideTheRangeNoStickyIsLobbedAtTheSentry()
    {
        TFField f = Demo();
        AiVector sentry = Mid + new AiVector(900f, 0f, 0f);
        IntentCommand[] decides = [.. Enumerable.Range(0, 8).Select(_ => f.StepSeeing(Self(Mid, Stickies, 8), null, [Sentry(sentry)]))];
        Assert.DoesNotContain(decides, i => i.Action.Kind == ActionKind.Fire);
    }

    /// <summary>
    /// The sentry kill's detonation: at the stand with the sentry in sight, the stickies charged at it (each thrown on the
    /// decide that lets the held trigger go, the clip falling after it) come to rest by it; once <paramref name="level"/>'s count (3, or 4 on a level 3) lie armed and at rest
    /// there, the detonation goes. Returns the stickies laid when it went (0: it never did in 160 decides).
    /// </summary>
    private static int LaidWhenTheSentryKillDetonates(int level)
    {
        TFField f = Demo();
        AiVector sentry = Mid + new AiVector(1250f, 0f, 0f);
        int clip = 8, laid = 0;
        bool held = false;
        var resting = new List<ThingFacts> { Sentry(sentry, level) };
        for (int n = 0; n < 160; n++)
        {
            IntentCommand i = f.StepSeeing(Self(Mid, Stickies, clip), null, [.. resting]);
            if (Detonates(f, in i))
                return laid;
            // The launcher throws a charged sticky when the held trigger is let go.
            bool holding = i.Action.Kind == ActionKind.Fire;
            bool thrown = held && !holding;
            held = holding;
            if (!thrown)
                continue;
            clip--;
            laid++;
            // The sticky lands and rests by the sentry, a little apart from the others.
            resting.Add(Sticky(200 + laid, sentry + new AiVector(-40f, 25f * laid - 50f, 0f), TFField.Red));
        }

        return 0;
    }

    /// <summary>A level 1 sentry: the kill detonates once three stickies rest by it (stock's three).</summary>
    [Fact]
    public void TheSentryKillDetonatesOnThreeAtRest() => Assert.Equal(3, LaidWhenTheSentryKillDetonates(level: 1));

    /// <summary>A level 3 sentry takes one more: four (the research's for its 216 health).</summary>
    [Fact]
    public void ALevelThreeSentryTakesFour() => Assert.Equal(4, LaidWhenTheSentryKillDetonates(level: 3));

    /// <summary>A sight table that answers one way for every pair of cells: a wall between, or none.</summary>
    private sealed class FlatSight(bool clear) : ICellSight
    {
        public float Range => 3000f;

        public bool Visible(int a, int b) => clear;

        public float Exposure(int cell) => 0.5f;
    }

    /// <summary>Facing the place a seen sentry stood, the sentry gone from the view for 40 decides: what the Demoman remembers after.</summary>
    private static int SentriesAfterLookingAway(bool clearSight)
    {
        var f = new TFField(weapons: Weapons, sight: new FlatSight(clearSight)) { Hud = StockKit() };
        AiVector sentry = Mid + new AiVector(1250f, 0f, 0f);
        f.StepSeeing(Self(Mid, Stickies, 8), null, [Sentry(sentry)]);
        Assert.Single(f.Decider.Sentries.ToArray());
        // Facing the place (yaw 0, along +x), the sentry gone from the view.
        SelfState facing = Self(Mid, Stickies, 8) with { Eye = new AiAngles(0f, 0f, 0f) };
        for (int n = 0; n < 40; n++)
            f.StepSeeing(facing, null, []);
        return f.Decider.Sentries.Length;
    }

    /// <summary>A sentry the Demoman looks at where it stood, in clear sight, and no longer sees is forgotten (it was destroyed).</summary>
    [Fact]
    public void ASentryNoLongerSeenWhereItStoodIsForgotten() => Assert.Equal(0, SentriesAfterLookingAway(clearSight: true));

    /// <summary>A sentry behind a wall (the sight table says the bot's cell does not see its cell) is kept, though not seen.</summary>
    [Fact]
    public void ASentryBehindAWallIsKept() => Assert.Equal(1, SentriesAfterLookingAway(clearSight: false));

    // ------------------------------------------------------------------ the counters

    /// <summary>The Demoman's counters reach the harness: a sticky laid is counted.</summary>
    [Fact]
    public void AStickyLaidIsCounted()
    {
        TFField f = Demo();
        DecidesToDetonate(f, _ => [Drawn(Enemy, Mid + new AiVector(300f, 0f, 0f), TFField.Blue)]);
        Dictionary<string, long> counts = f.Decider.ClassCounters().ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal((1L, 1L), (counts[TFDemomanClass.StickiesLaid], counts[TFDemomanClass.Detonations]));
    }
}

/// <summary>
/// The chain of HUD meter readers (<see cref="ITFHudMeters"/>) resolves only when every reader declares its own order
/// (the core's Hooks: "both declare HookOrder 0" threw in AiSenses.OnPostThink every tick, live, once the Demoman's reader
/// joined the Medic's, and no bot ever saw itself alive). Each reader the TF2 AI ships has an order no other has.
/// </summary>
public sealed class TFHudMeterOrderTests
{
    [Fact]
    public void EveryHudMeterReaderDeclaresItsOwnOrder()
    {
        Type[] readers = [.. typeof(ITFHudMeters).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t))];
        int[] orders = [.. readers.Select(t => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t) is SourceSharp.Abi.IOrderedHook o ? o.HookOrder : 0)];
        Assert.True(readers.Length >= 2, $"{readers.Length} readers");
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }
}
