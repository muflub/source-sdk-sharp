using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Engineer's play (the research's A1 Nest): he walks to a sentry spot by the point he defends, orders the blueprint,
/// faces it out and clicks it down, gives a refused spot up after stock's five tries, works his nest with the wrench,
/// fetches metal, takes an out-of-place nest down, and fights only what stock's Engineer fights. On
/// <see cref="TFField"/>'s CTF strip his point is his own capture zone (<see cref="TFField.RedZone"/>).
/// </summary>
public sealed class TFEngineerPlayTests
{
    private const int Pistol = 21, Laser = 23, Rescue = 24, Wrench = TFField.Shovel, Shotgun = TFField.Shotgun;
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Far = new(2400f, 256f, 0f);

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Pistol, "tf_weapon_pistol", 12, 0.15f, 1f, true, false, 0f),
        new WeaponFacts(Laser, TFEngineerClass.LaserPointer, 0, 0.1f, 0f, false, false, 0f),
        new WeaponFacts(Rescue, TFEngineerClass.RescueShotgun, 4, 0.625f, 3f, true, false, 2400f),
    ]);

    private static readonly TFKitItem[] Kit =
    [
        new(TFLoadoutSlot.Primary, 9, "tf_weapon_shotgun", Shotgun, []),
        new(TFLoadoutSlot.Secondary, 22, "tf_weapon_pistol", Pistol, []),
        new(TFLoadoutSlot.Melee, 7, "tf_weapon_wrench", Wrench, []),
    ];

    /// <summary>A panel row's meters, as <see cref="TFEngineerMeters"/> writes them.</summary>
    private static IEnumerable<TFMeter> Row(int kind, AiVector at, int health = 150, int max = 150, int level = 1, int ammo = 100, bool building = false)
    {
        string k = TFEngineerPanel.NameOf(kind);
        yield return new(k + TFEngineerPanel.Flags, TFEngineerPanel.Present | (building ? TFEngineerPanel.Building : TFEngineerPanel.Built));
        yield return new(k + TFEngineerPanel.Level, level);
        yield return new(k + TFEngineerPanel.Health, health);
        yield return new(k + TFEngineerPanel.MaxHealth, max);
        yield return new(k + TFEngineerPanel.Ammo, ammo);
        yield return new(k + TFEngineerPanel.X, at.X);
        yield return new(k + TFEngineerPanel.Y, at.Y);
        yield return new(k + TFEngineerPanel.Z, at.Z);
    }

    private static TFHud Hud(int placing = 0, params IEnumerable<TFMeter>[] rows)
        => new([.. Kit], [.. rows.SelectMany(r => r), new TFMeter(TFEngineerPanel.Placing, placing)], []);

    /// <summary>The Rescue Ranger's kit: the Rescue Ranger in the primary slot.</summary>
    private static TFHud RescueHud(params IEnumerable<TFMeter>[] rows)
        => new([new(TFLoadoutSlot.Primary, 997, TFEngineerClass.RescueShotgun, Rescue, []), Kit[1], Kit[2]],
               [.. rows.SelectMany(r => r), new TFMeter(TFEngineerPanel.Placing, 0)], []);

    /// <summary>The Wrangler's kit: the Wrangler in the secondary slot.</summary>
    private static TFHud WranglerHud(params IEnumerable<TFMeter>[] rows)
        => new([Kit[0], new(TFLoadoutSlot.Secondary, 140, TFEngineerClass.LaserPointer, Laser, []), Kit[2]],
               [.. rows.SelectMany(r => r), new TFMeter(TFEngineerPanel.Placing, 0)], []);

    private static SelfState Engineer(AiVector at, int weapon = Shotgun, int metal = 200, float yaw = 0f, int health = 125)
        => TFField.Self(at, health: health, weapon: weapon, clip: 6, playerClass: TFClass.Engineer) with { Metal = metal, Eye = new AiAngles(0f, yaw, 0f) };

    private static TFField Field(TFHud hud, ItemSpawn[]? packs = null, string archetype = TFEngineerClass.Nest)
        => new(weapons: Weapons, packs: packs, seed: SeedOf(archetype)) { Hud = hud };

    /// <summary>The first seed whose draw is <paramref name="archetype"/>: the decider plays the archetype its seed draws (TFDecider.ArchetypeOf).</summary>
    private static ulong SeedOf(string archetype)
    {
        for (ulong seed = 1; ; seed++)
        {
            if (TFEngineerClass.Archetypes().Draw(seed)?.Name == archetype)
                return seed;
        }
    }

    private static bool IsRow(TFField f, in IntentCommand i, string name)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) && row.Name == name;

    /// <summary>The spot an Engineer with nothing built walks to, read off his first decide's goal.</summary>
    private static AiVector SpotOf(TFField f)
    {
        f.Step(Engineer(Far), TFField.Ctf());
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.True(f.Decider.Doing.HasGoal);
        return f.Decider.Doing.Goal;
    }

    private static float YawTo(AiVector from, AiVector to) => TFEngineerSpots.YawTo(from, to);

    [Fact]
    public void WithNothingBuiltHeWalksToASentrySpotByHisPoint()
    {
        var f = Field(Hud());
        AiVector spot = SpotOf(f);
        float d = MathF.Sqrt((spot.X - TFField.RedZone.X) * (spot.X - TFField.RedZone.X) + (spot.Y - TFField.RedZone.Y) * (spot.Y - TFField.RedZone.Y));
        // He stands a build distance short of the spot, the blueprint going that far ahead of him.
        Assert.InRange(d, TFEngineerSpots.OffPoint, TFEngineerSpots.SentryRange * TFEngineerSpots.RangeTolerance + 72f);
    }

    [Fact]
    public void AtHisSpotHeOrdersTheSentrysBlueprint()
    {
        var f = Field(Hud());
        AiVector spot = SpotOf(f);
        IntentCommand i = f.Step(Engineer(spot), TFField.Ctf());
        Assert.True(IsRow(f, i, "build_sentry"), $"{i.Action}");
    }

    [Fact]
    public void WithTheBlueprintOutAndFacingThePointHeClicksItDown()
    {
        var f = Field(Hud());
        AiVector spot = SpotOf(f);
        f.Hud = Hud(placing: (int)TFObjectType.Sentrygun + 1);
        IntentCommand i = f.Step(Engineer(spot, yaw: YawTo(spot, TFField.RedZone)), TFField.Ctf());
        Assert.Equal((ActionKind.Fire, FireStyle.Tap), (i.Action.Kind, i.Action.Style));
    }

    [Fact]
    public void WithTheBlueprintOutButFacingAwayHeTurnsBeforeHeClicks()
    {
        var f = Field(Hud());
        AiVector spot = SpotOf(f);
        f.Hud = Hud(placing: (int)TFObjectType.Sentrygun + 1);
        float yaw = YawTo(spot, TFField.RedZone);
        IntentCommand i = f.Step(Engineer(spot, yaw: yaw + 90f), TFField.Ctf());
        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
        Assert.Equal(LookKind.Point, i.Look.Kind);
        Assert.InRange(MathF.Abs(YawTo(f.Decider.LastSelf.Origin, i.Look.Point) - yaw), 0f, 1f);
    }

    [Fact]
    public void RefusedFiveTimesHeGivesTheSpotUp()
    {
        var f = Field(Hud(placing: (int)TFObjectType.Sentrygun + 1));
        AiVector spot = SpotOf(f);
        float yaw = YawTo(spot, TFField.RedZone);
        int clicks = 0;
        bool moved = false;
        for (int n = 0; n < 200 && !moved; n++)
        {
            IntentCommand i = f.Step(Engineer(spot, yaw: yaw), TFField.Ctf());
            if (i.Action.Kind == ActionKind.Fire)
                clicks++;
            if (i.Look.Kind == LookKind.Point)
                yaw = YawTo(spot, i.Look.Point);
            moved = f.Decider.Doing.HasGoal && f.Decider.Doing.Goal.DistanceTo(spot) > 1f;
        }

        Assert.True(moved);
        Assert.Equal(EngineerTries, clicks);
    }

    private const int EngineerTries = 5;

    [Fact]
    public void BesideHisHurtSentryHeSwingsTheWrenchAtIt()
    {
        AiVector sentry = new(700f, 256f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 60)));
        IntentCommand i = f.Step(Engineer(sentry + new AiVector(-40f, 0f, 0f), weapon: Wrench), TFField.Ctf());
        Assert.Equal((ActionKind.Fire, FireStyle.Spray), (i.Action.Kind, i.Action.Style));
        Assert.Equal(LookKind.Point, i.Look.Kind);
        Assert.True(i.Look.Point.DistanceTo(sentry) < 40f);
    }

    [Fact]
    public void AtHisNestWithTheShotgunOutHeTakesOutTheWrench()
    {
        AiVector sentry = new(700f, 256f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 60)));
        IntentCommand i = f.Step(Engineer(sentry + new AiVector(-40f, 0f, 0f), weapon: Shotgun), TFField.Ctf());
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Wrench), (i.Action.Kind, i.Action.WeaponSlot));
    }

    [Fact]
    public void ASpyAtHisNestIsFoughtWithTheShotgun()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 60)));
        var spy = new ActorFacts(Enemy, me + new AiVector(-300f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Spy);
        IntentCommand i = f.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), spy);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Shotgun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    [Fact]
    public void ASoldierFarOffIsLeftToTheSentryWhileHeWorks()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 60)));
        var soldier = new ActorFacts(Enemy, sentry + new AiVector(900f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        IntentCommand i = f.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), soldier);
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
        Assert.True(i.Look.Point.DistanceTo(sentry) < 40f, $"{i.Look.Point}");
    }

    [Fact]
    public void WithNoSentryAndNoMetalHeFetchesItFromAnAmmoPack()
    {
        var pack = new ItemSpawn(0, "item_ammopack_medium", ItemKind.AmmoMedium, new AiVector(1200f, 64f, 0f), 10f);
        var f = Field(Hud(), packs: [pack]);
        f.Step(Engineer(Far, metal: 0), TFField.Ctf());
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.Equal(pack.Position, f.Decider.Doing.Goal);
    }

    [Fact]
    public void ANestFarFromHisPointThatHasGoneQuietComesDown()
    {
        // His sentry stands far down the strip from his zone and has not fired: after ten quiet seconds it comes down.
        AiVector sentry = new(3000f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, max: 216, health: 216)));
        bool down = false;
        int steps = 0;
        for (; steps < 400 && !down; steps++)
            down = IsRow(f, f.Step(Engineer(me, weapon: Wrench), TFField.Ctf()), "destroy_sentry");
        Assert.True(down);
        Assert.True(steps * DecideSchedule.Period * 0.015f >= 10f, $"{steps} decides");
    }

    [Fact]
    public void TheArchetypeTheFactsPinIsTheOneTheDeciderPlays()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.Gunslinger);
        f.Step(Engineer(Far), TFField.Ctf());
        Assert.Equal(TFEngineerClass.Gunslinger, f.Decider.Archetype);
    }

    /// <summary>
    /// The battle Engineer attacks: on a flag map his mini goes by the enemy's flag, within a mini's reach of it (the Nest's gun
    /// goes by his own capture zone).
    /// </summary>
    [Fact]
    public void TheGunslingersSpotIsWithinTheMinisReachOfTheEnemysFlag()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.Gunslinger);
        AiVector spot = SpotOf(f);
        float d = MathF.Sqrt((spot.X - TFField.BlueFlag.X) * (spot.X - TFField.BlueFlag.X) + (spot.Y - TFField.BlueFlag.Y) * (spot.Y - TFField.BlueFlag.Y));
        Assert.InRange(d, TFEngineerSpots.OffPoint, 800f + 72f);
    }

    /// <summary>With his mini up he does not sit by it: he goes with his team (the Nest stays and works).</summary>
    [Fact]
    public void WithHisMiniUpTheGunslingerLeavesTheNest()
    {
        // Each by his own point: the mini by the enemy's flag, the Nest's gun by his zone.
        AiVector mini = new(3400f, 256f, 0f), sentry = new(700f, 256f, 0f);
        var battle = Field(Hud(0, Row(TFEngineerPanel.Sentry, mini, health: 100, max: 100)), archetype: TFEngineerClass.Gunslinger);
        var nest = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 100, max: 100)));
        battle.Step(Engineer(mini + new AiVector(-40f, 0f, 0f), weapon: Wrench), TFField.Ctf());
        nest.Step(Engineer(sentry + new AiVector(-40f, 0f, 0f), weapon: Wrench), TFField.Ctf());
        Assert.Equal(TFDecider.ClassBranch, nest.Branch);
        Assert.NotEqual(TFDecider.ClassBranch, battle.Branch);
    }

    /// <summary>A Soldier 600 u off: the Gunslinger fights him with his gun; the Nest, by his sentry, leaves him to it.</summary>
    [Fact]
    public void TheGunslingerFightsWhatTheNestLeavesToTheSentry()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        TFHud hud = Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 100, max: 100));
        var soldier = new ActorFacts(Enemy, me + new AiVector(600f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        var battle = Field(hud, archetype: TFEngineerClass.Gunslinger);
        var nest = Field(hud);
        IntentCommand b = battle.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), soldier);
        IntentCommand n = nest.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), soldier);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Shotgun), (b.Action.Kind, b.Action.WeaponSlot));
        Assert.NotEqual(ActionKind.SelectWeapon, n.Action.Kind);
    }

    /// <summary>The Gunslinger's mini is never upgraded and he builds no dispenser: with metal and his mini whole he has no work at it.</summary>
    [Fact]
    public void TheGunslingerBuildsNoDispenser()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        TFHud hud = Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 100, max: 100));
        var battle = Field(hud, archetype: TFEngineerClass.Gunslinger);
        for (int n = 0; n < 20; n++)
        {
            IntentCommand i = battle.Step(Engineer(me, weapon: Shotgun), TFField.Ctf());
            Assert.False(IsRow(battle, i, "build_dispenser"));
        }
    }

    /// <summary>His mini far from his point comes down at once: no ten quiet seconds as a nest's gun waits.</summary>
    [Fact]
    public void AGunslingersMiniFarFromHisPointComesDownAtOnce()
    {
        AiVector sentry = new(1500f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 100, max: 100)), archetype: TFEngineerClass.Gunslinger);
        bool down = false;
        int steps = 0;
        for (; steps < 20 && !down; steps++)
            down = IsRow(f, f.Step(Engineer(me, weapon: Shotgun), TFField.Ctf()), "destroy_sentry");
        Assert.True(down);
        Assert.True(steps * DecideSchedule.Period * 0.015f < 3f, $"{steps} decides");
    }

    /// <summary>An enemy he sees beyond his sentry's reach of it: the Wrangler takes the gun; the Nest does not.</summary>
    [Fact]
    public void AnEnemyBeyondTheSentrysReachIsWrangled()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var soldier = new ActorFacts(Enemy, sentry + new AiVector(1300f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        var wrangler = Field(WranglerHud(Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216)), archetype: TFEngineerClass.Wrangler);
        var nest = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216)));
        IntentCommand w = wrangler.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), soldier);
        IntentCommand n = nest.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), soldier);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Laser), (w.Action.Kind, w.Action.WeaponSlot));
        Assert.NotEqual(ActionKind.SelectWeapon, n.Action.Kind);
    }

    /// <summary>With the Wrangler in hand he aims and fires at the enemy: the sentry fires where he aims.</summary>
    [Fact]
    public void WithTheWranglerOutHeAimsTheGunAtTheEnemy()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var soldier = new ActorFacts(Enemy, sentry + new AiVector(1300f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        var f = Field(WranglerHud(Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216)), archetype: TFEngineerClass.Wrangler);
        IntentCommand i = f.StepDrawn(Engineer(me, weapon: Laser), TFField.Ctf(), soldier);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
        Assert.Equal((LookKind.Actor, Enemy), (i.Look.Kind, i.Look.Actor));
    }

    /// <summary>A sentry under siege (under 60 %) with an enemy in its reach: wrangled too, for the shield and the fire rate.</summary>
    [Fact]
    public void ASentryUnderSiegeIsWrangled()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var soldier = new ActorFacts(Enemy, sentry + new AiVector(800f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        var f = Field(WranglerHud(Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 100, max: 216)), archetype: TFEngineerClass.Wrangler);
        IntentCommand i = f.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), soldier);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Laser), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>With nothing in sight past the quiet seconds he puts the Wrangler away for the wrench.</summary>
    [Fact]
    public void WithNothingInSightHePutsTheWranglerAway()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var soldier = new ActorFacts(Enemy, sentry + new AiVector(1300f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        var f = Field(WranglerHud(Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 200, max: 216)), archetype: TFEngineerClass.Wrangler);
        f.StepDrawn(Engineer(me, weapon: Laser), TFField.Ctf(), soldier);
        bool wrench = false;
        int steps = 0;
        for (; steps < 100 && !wrench; steps++)
        {
            IntentCommand i = f.Step(Engineer(me, weapon: Laser), TFField.Ctf());
            wrench = i.Action is { Kind: ActionKind.SelectWeapon, WeaponSlot: EngineBotSelf.ByKind + Wrench };
        }

        Assert.True(wrench);
        Assert.True(steps * DecideSchedule.Period * 0.015f >= 2f, $"{steps} decides");
    }

    /// <summary>A hurt level-3 gun 300 u off: the Rescue Ranger bolts it from where he stands; the Nest walks up to it.</summary>
    [Fact]
    public void TheRescueRangerRepairsHisGunFromRange()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(300f, 0f, 0f);
        var rescue = Field(RescueHud(Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 120, max: 216)), archetype: TFEngineerClass.RescueRanger);
        IntentCommand r = rescue.Step(Engineer(me, weapon: Rescue), TFField.Ctf());
        Assert.Equal((ActionKind.Fire, LookKind.Point), (r.Action.Kind, r.Look.Kind));
        Assert.True(r.Look.Point.DistanceTo(sentry) < 40f);
        var nest = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 120, max: 216)));
        IntentCommand n = nest.Step(Engineer(me, weapon: Shotgun), TFField.Ctf());
        Assert.NotEqual(ActionKind.Fire, n.Action.Kind);
        Assert.True(nest.Decider.Doing.HasGoal && nest.Decider.Doing.Goal.DistanceTo(sentry) < 80f);
    }

    /// <summary>With his nest whole at level 3 the Rescue Ranger stands back from his gun, away from the point it covers.</summary>
    [Fact]
    public void TheRescueRangerStandsBackFromHisNest()
    {
        AiVector sentry = new(900f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        TFHud hud = RescueHud(Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216),
                              Row(TFEngineerPanel.Dispenser, sentry + new AiVector(-100f, 0f, 0f), level: 3, health: 216, max: 216));
        var rescue = Field(hud, archetype: TFEngineerClass.RescueRanger);
        rescue.Step(Engineer(me, weapon: Rescue), TFField.Ctf());
        Assert.True(rescue.Decider.Doing.HasGoal);
        Assert.InRange(rescue.Decider.Doing.Goal.DistanceTo(sentry), 250f, 600f);
    }

    /// <summary>
    /// A spot he never reaches is given up after 30 s and its walk at 150 u/s, for another (live on cp_granary a ledge's way
    /// the map did not give had him walk for 70 s).
    /// </summary>
    [Fact]
    public void AStillUnreachedSpotIsGivenUpAfterItsPatience()
    {
        var f = Field(Hud());
        AiVector spot = SpotOf(f);
        int steps = 1;
        bool other = false;
        for (; steps < 2000 && !other; steps++)
        {
            f.Step(Engineer(Far), TFField.Ctf());
            other = f.Decider.Doing.HasGoal && f.Decider.Doing.Goal.DistanceTo(spot) > 1f;
        }

        Assert.True(other);
        float seconds = steps * DecideSchedule.Period * 0.015f;
        Assert.InRange(seconds, 30f + Far.DistanceTo(spot) / 150f - 1f, 30f + Far.DistanceTo(spot) / 150f + 2f);
    }

    /// <summary>A gun lost at his spot long after he chose it is rebuilt there: the spot's patience runs from when he last had to walk to it.</summary>
    [Fact]
    public void AGunLostAtHisSpotIsRebuiltThere()
    {
        var f = Field(Hud());
        AiVector spot = SpotOf(f);
        f.Hud = Hud(0, Row(TFEngineerPanel.Sentry, spot, level: 3, health: 216, max: 216));
        int stay = (int)((30f + Far.DistanceTo(spot) / 150f + 10f) / (DecideSchedule.Period * 0.015f));
        AiVector near = spot + new AiVector(0f, 70f, 0f);
        for (int n = 0; n < stay; n++)
            f.Step(Engineer(near, weapon: Wrench), TFField.Ctf());
        f.Hud = Hud();
        f.Step(Engineer(near, weapon: Wrench), TFField.Ctf());
        Assert.True(f.Decider.Doing.HasGoal);
        Assert.Equal(spot, f.Decider.Doing.Goal);
    }

    /// <summary>
    /// Working between his sentry and dispenser he stands inside the dispenser's 64 u reach, where it feeds him the upgrades'
    /// metal: live, from their middle (60 u off each) his gun never reached level 3 in 156 s up.
    /// </summary>
    [Fact]
    public void BetweenHisBuildingsHeStandsInTheDispensersReach()
    {
        AiVector sentry = new(700f, 256f, 0f), dispenser = new(840f, 256f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 1, health: 150, max: 150), Row(TFEngineerPanel.Dispenser, dispenser)));
        f.Step(Engineer(new AiVector(760f, 400f, 0f), weapon: Wrench, metal: 0), TFField.Ctf());
        Assert.True(f.Decider.Doing.HasGoal);
        Assert.True(f.Decider.Doing.Goal.DistanceTo(dispenser) < 64f, $"{f.Decider.Doing.Goal}");
        Assert.True(f.Decider.Doing.Goal.DistanceTo(sentry) < TFDecider.MeleeReach + 40f, $"{f.Decider.Doing.Goal}");
    }

    /// <summary>
    /// He walks to a build distance short of his spot, facing the point, so the blueprint lands on the spot's floor: live on
    /// cp_granary, standing on a ledge's spot put the blueprint over its edge for 8 to 22 refusals a game.
    /// </summary>
    [Fact]
    public void HeStandsABuildDistanceShortOfHisSpot()
    {
        var f = Field(Hud());
        AiVector stand = SpotOf(f);
        AiVector landing = stand + TFEngineerSpots.Forward(TFEngineerSpots.YawTo(stand, TFField.RedZone)) * 72f;
        float cx = MathF.Floor(landing.X / TFField.CellSize) * TFField.CellSize + TFField.CellSize / 2f;
        float cy = MathF.Floor(landing.Y / TFField.CellSize) * TFField.CellSize + TFField.CellSize / 2f;
        Assert.True(MathF.Abs(landing.X - cx) < 0.5f && MathF.Abs(landing.Y - cy) < 0.5f, $"{stand} lands at {landing}");
    }

    /// <summary>On his way to build with his gun down, an enemy 500 u off is shot as he walks, not stopped for (the class branch keeps him going).</summary>
    [Fact]
    public void OnHisWayToBuildHeShootsWhatHeSees()
    {
        var f = Field(Hud());
        var soldier = new ActorFacts(Enemy, Far + new AiVector(-500f, 100f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        IntentCommand i = f.StepDrawn(Engineer(Far), TFField.Ctf(), soldier);
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
        Assert.Equal((LookKind.Actor, Enemy), (i.Look.Kind, i.Look.Actor));
    }

    /// <summary>An enemy within 300 u while his gun is down stops him to fight.</summary>
    [Fact]
    public void AnEnemyCloseWhileHisGunIsDownIsFought()
    {
        var f = Field(Hud());
        var soldier = new ActorFacts(Enemy, Far + new AiVector(-200f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        f.StepDrawn(Engineer(Far), TFField.Ctf(), soldier);
        Assert.Equal(TFDecider.Fight, f.Branch);
    }

    /// <summary>
    /// Come into the world by the enemy's flag, the battle Engineer's mini goes within its own reach of the flag, at its best range
    /// (350 u, the stand 72 u further): nearer than a nest gun's 600 u.
    /// </summary>
    [Fact]
    public void TheGunslingersMiniGoesNearerThanANestsGun()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.Gunslinger);
        f.Step(Engineer(new AiVector(3300f, 256f, 0f)), TFField.Ctf());
        Assert.True(f.Decider.Doing.HasGoal);
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        float d = f.Decider.Doing.Goal.DistanceTo(TFField.BlueFlag);
        Assert.InRange(d, TFEngineerSpots.OffPoint, 560f);
    }

    /// <summary>
    /// The dispenser goes down from beside his gun, facing back from the point, so the two stand about 87 u apart and the stand
    /// between them reaches both: live on cp_granary, from 48 u behind the gun it landed 120 u off, and from the dispenser's
    /// reach his wrench never reached the gun (183 swings, no upgrade).
    /// </summary>
    [Fact]
    public void TheDispenserGoesDownFromBesideHisGun()
    {
        AiVector sentry = new(700f, 256f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry)));
        f.Step(Engineer(sentry + new AiVector(-200f, 0f, 0f), metal: 200), TFField.Ctf());
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.True(f.Decider.Doing.HasGoal);
        Assert.InRange(f.Decider.Doing.Goal.DistanceTo(sentry), 35f, 45f);
    }

    /// <summary>
    /// Within 48 u of his build stand he orders the blueprint: live, a stand the follower could not quite reach (24 u asked) kept
    /// him walking to it for 77 s without a build.
    /// </summary>
    [Fact]
    public void FortyUnitsFromHisStandHeOrdersTheBlueprint()
    {
        var f = Field(Hud());
        AiVector stand = SpotOf(f);
        IntentCommand i = f.Step(Engineer(stand + new AiVector(0f, 40f, 0f)), TFField.Ctf());
        Assert.True(IsRow(f, i, "build_sentry"), $"{i.Action}");
    }
}
