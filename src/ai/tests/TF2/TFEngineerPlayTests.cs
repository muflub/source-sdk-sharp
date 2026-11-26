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
/// <see cref="TFField"/>'s CTF strip his point is his own flag's home (<see cref="TFField.RedFlag"/>).
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
    {
        var f = new TFField(weapons: Weapons, packs: packs, seed: SeedOf(archetype)) { Hud = hud };
        PinIfUndrawn(f, archetype);
        return f;
    }

    /// <summary>
    /// The first seed whose draw is <paramref name="archetype"/>: the decider plays the archetype its seed draws (TFDecider.ArchetypeOf);
    /// 1 for a pin-only archetype (weight 0), which <see cref="PinIfUndrawn"/> pins as <c>ai_bot_add ... archetype=</c> does.
    /// </summary>
    private static ulong SeedOf(string archetype)
    {
        if (TFEngineerClass.Archetypes().Named(archetype) is not { Weight: > 0f })
            return 1;
        for (ulong seed = 1; ; seed++)
        {
            if (TFEngineerClass.Archetypes().Draw(seed)?.Name == archetype)
                return seed;
        }
    }

    /// <summary>A pin-only archetype is the decider's by its pin, set before its first decide (the field makes no pin of its own).</summary>
    private static void PinIfUndrawn(TFField f, string archetype)
    {
        if (TFEngineerClass.Archetypes().Named(archetype) is { Weight: > 0f })
            return;
        typeof(TFDecider).GetProperty(nameof(TFDecider.Pin))!.SetValue(f.Decider, new SourceSharp.Ai.TF2.TFBotPin(f.Bot, 1, TFClass.Engineer, archetype));
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
        float d = MathF.Sqrt((spot.X - TFField.RedFlag.X) * (spot.X - TFField.RedFlag.X) + (spot.Y - TFField.RedFlag.Y) * (spot.Y - TFField.RedFlag.Y));
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
        IntentCommand i = f.Step(Engineer(spot, yaw: YawTo(spot, TFField.RedFlag)), TFField.Ctf());
        Assert.Equal((ActionKind.Fire, FireStyle.Tap), (i.Action.Kind, i.Action.Style));
    }

    [Fact]
    public void WithTheBlueprintOutButFacingAwayHeTurnsBeforeHeClicks()
    {
        var f = Field(Hud());
        AiVector spot = SpotOf(f);
        f.Hud = Hud(placing: (int)TFObjectType.Sentrygun + 1);
        float yaw = YawTo(spot, TFField.RedFlag);
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
        float yaw = YawTo(spot, TFField.RedFlag);
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

    /// <summary>His point on a control-point field: own point P1 (red's, blue may take it) near his gun, then the front moving.</summary>
    private static ObjectiveKnowledge Points(int stage)
    {
        var p1 = new AiVector(3100f, 256f, 0f);
        // Each move far enough (over 2,500 u from the gun) that the gun is out of position after either.
        var p2 = new AiVector(400f, 256f, 0f);
        var p3 = new AiVector(200f, 256f, 0f);
        int blue = 1 << TFField.Blue;
        return new ObjectiveKnowledge(stage switch
        {
            0 => [new Objective(1, ObjectiveKind.Capture, TFField.Red, blue, ObjectiveState.Open, p1), new Objective(2, ObjectiveKind.Capture, TFField.Red, 0, ObjectiveState.Locked, p2),
                  new Objective(3, ObjectiveKind.Capture, TFField.Red, 0, ObjectiveState.Locked, p3)],
            1 => [new Objective(1, ObjectiveKind.Capture, TFField.Blue, 0, ObjectiveState.Locked, p1), new Objective(2, ObjectiveKind.Capture, TFField.Red, blue, ObjectiveState.Open, p2),
                  new Objective(3, ObjectiveKind.Capture, TFField.Red, 0, ObjectiveState.Locked, p3)],
            _ => [new Objective(1, ObjectiveKind.Capture, TFField.Blue, 0, ObjectiveState.Locked, p1), new Objective(2, ObjectiveKind.Capture, TFField.Blue, 0, ObjectiveState.Locked, p2),
                  new Objective(3, ObjectiveKind.Capture, TFField.Red, blue, ObjectiveState.Open, p3)],
        });
    }

    /// <summary>Steps him at his gun through the stages, each for <paramref name="seconds"/>; true when he orders the gun down.</summary>
    private static bool TakesTheGunDown(int stages, float seconds, bool firing = false)
    {
        AiVector sentry = new(3000f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, max: 216, health: 216)));
        int each = (int)(seconds / (DecideSchedule.Period * 0.015f)), shells = 200;
        for (int stage = 0; stage <= stages; stage++)
        {
            for (int n = 0; n < each; n++)
            {
                // A gun still firing: its shells fall on his panel.
                if (firing)
                    f.Hud = Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, max: 216, health: 216, ammo: shells = shells > 1 ? shells - 1 : 200));
                if (IsRow(f, f.Step(Engineer(me, weapon: Wrench), Points(stage)), "destroy_sentry"))
                    return true;
            }
        }

        return false;
    }

    /// <summary>A gun still firing is kept, its point moved twice or not (stock's: out of position, it is kept while it fires).</summary>
    [Fact]
    public void ANestStillFiringHoldsThroughTwoMoves()
        => Assert.False(TakesTheGunDown(stages: 2, seconds: 15f, firing: true));

    /// <summary>
    /// The nest holds through his point's first move and comes down, quiet, after its second (the review: following every
    /// capture on cp_granary, no gun reached level 3).
    /// </summary>
    [Fact]
    public void ANestHoldsThroughOneMoveOfHisPoint()
        => Assert.False(TakesTheGunDown(stages: 1, seconds: 15f));

    [Fact]
    public void ANestComesDownQuietAfterTwoMovesOfHisPoint()
        => Assert.True(TakesTheGunDown(stages: 2, seconds: 15f));

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

    /// <summary>The Battle Engie fights beside his gun what the Nest leaves to it (round 2: "fights beside its sentry").</summary>
    [Fact]
    public void TheBattleEngieFightsBesideHisGun()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-64f, 0f, 0f);
        TFHud hud = Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 2, health: 180, max: 180));
        var soldier = new ActorFacts(Enemy, me + new AiVector(600f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        IntentCommand b = Field(hud, archetype: TFEngineerClass.BattleEngie).StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), soldier);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Shotgun), (b.Action.Kind, b.Action.WeaponSlot));
    }

    /// <summary>The Short Circuit spends his building metal and its zap is 15 a target: never his gun in a fight.</summary>
    [Fact]
    public void TheBattleEngieNeverFightsWithTheShortCircuit()
    {
        const int Arm = 26;
        var weapons = new WeaponKnowledge([.. Weapons.All, new WeaponFacts(Arm, TFEngineerClass.MechanicalArm, 0, 0.2f, 0f, false, false, 0f)]);
        var hud = new TFHud([Kit[0], new(TFLoadoutSlot.Secondary, 528, TFEngineerClass.MechanicalArm, Arm, []), Kit[2]],
                            [.. Row(TFEngineerPanel.Sentry, new AiVector(700f, 256f, 0f), level: 2, health: 180, max: 180), new TFMeter(TFEngineerPanel.Placing, 0)], []);
        var f = new TFField(weapons: weapons, seed: SeedOf(TFEngineerClass.BattleEngie)) { Hud = hud };
        AiVector me = new(636f, 256f, 0f);
        // A Spy 1,000 u off: fought at any range, and past 900 u the Nest's rule takes the secondary.
        var spy = new ActorFacts(Enemy, me + new AiVector(1000f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Spy);
        for (int n = 0; n < 40; n++)
        {
            IntentCommand i = f.StepDrawn(Engineer(me, weapon: Wrench), TFField.Ctf(), spy);
            Assert.False(i.Action is { Kind: ActionKind.SelectWeapon, WeaponSlot: EngineBotSelf.ByKind + Arm }, $"step {n}: the Short Circuit drawn");
        }
    }

    [Fact]
    public void TheBattleEngieBuildsByThePointHisTeamTakes()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.BattleEngie);
        f.Step(Engineer(Far), PointPair(ownOpen: true));
        Assert.True(f.Decider.Doing.Goal.DistanceTo(TheirPoint) < f.Decider.Doing.Goal.DistanceTo(OwnPoint), $"{f.Decider.Doing.Goal}");
    }

    /// <summary>The Ninjaneer failed the Ready rule (the enemy's first capture at a median of 75 s against 152 s): no seed draws him, a pin does.</summary>
    [Fact]
    public void TheNinjaneerIsPinOnly()
    {
        for (ulong seed = 1; seed <= 2000; seed++)
            Assert.NotEqual(TFEngineerClass.Ninjaneer, TFEngineerClass.Archetypes().Draw(seed)?.Name);
    }

    private static readonly AiVector MidPoint = new(2200f, 256f, 0f), LastPoint = new(3600f, 256f, 0f);

    /// <summary>Three points: his own, the middle blue holds (red may take it), blue's last (locked).</summary>
    private static ObjectiveKnowledge ThreePoints() => new([
        new Objective(1, ObjectiveKind.Capture, TFField.Red, 0, ObjectiveState.Locked, OwnPoint),
        new Objective(2, ObjectiveKind.Capture, TFField.Blue, 1 << TFField.Red, ObjectiveState.Open, MidPoint),
        new Objective(3, ObjectiveKind.Capture, TFField.Blue, 0, ObjectiveState.Locked, LastPoint),
    ]);

    /// <summary>The Ninjaneer's nest is by the enemy's next point behind the one his team takes (the guide: behind their lines).</summary>
    [Fact]
    public void TheNinjaneerBuildsByTheEnemysNextPoint()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.Ninjaneer);
        f.Step(Engineer(new AiVector(1200f, 256f, 0f)), ThreePoints());
        Assert.Equal("to the sentry spot", f.Decider.Doing.Verdict);
        AiVector goal = f.Decider.Doing.Goal;
        Assert.True(goal.DistanceTo(LastPoint) < goal.DistanceTo(MidPoint), $"{goal}");
    }

    [Fact]
    public void TheBattleEngieBuildsByTheFront()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.BattleEngie);
        f.Step(Engineer(new AiVector(1200f, 256f, 0f)), ThreePoints());
        AiVector goal = f.Decider.Doing.Goal;
        Assert.True(goal.DistanceTo(MidPoint) < goal.DistanceTo(LastPoint), $"{goal}");
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
        Assert.True(nest.Decider.Doing.HasGoal && nest.Decider.Doing.Goal.DistanceTo(sentry) <= TFEngineerSpots.WorkReachUnits + 0.5f);
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
    /// Working between his sentry and dispenser he stands inside the dispenser's reach, where it feeds him the upgrades'
    /// metal, and his wrench's of the gun: live, from their middle (60 u off each) his gun never reached level 3 in 156 s up.
    /// </summary>
    [Fact]
    public void BetweenHisBuildingsHeStandsInTheDispensersReach()
    {
        AiVector sentry = new(700f, 256f, 0f), dispenser = new(840f, 256f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 1, health: 150, max: 150), Row(TFEngineerPanel.Dispenser, dispenser)));
        f.Step(Engineer(new AiVector(760f, 400f, 0f), weapon: Wrench, metal: 0), TFField.Ctf());
        Assert.True(f.Decider.Doing.HasGoal);
        Assert.True(f.Decider.Doing.Goal.DistanceTo(dispenser) <= TFEngineerSpots.NestFeedUnits, $"{f.Decider.Doing.Goal}");
        Assert.True(f.Decider.Doing.Goal.DistanceTo(sentry) <= TFEngineerSpots.WorkReachUnits, $"{f.Decider.Doing.Goal}");
    }

    /// <summary>
    /// He walks to a build distance short of his spot, facing the point, so the blueprint lands on the spot's floor: live on
    /// cp_granary, standing on a ledge's spot put the blueprint over its edge for 8 to 22 refusals a game.
    /// </summary>
    [Fact]
    public void HeStandsABuildDistanceShortOfHisSpot()
    {
        // His own point mid-strip (a flag nest's spots hug the strip's edge, where no stand lies behind them).
        var f = Field(Hud());
        f.Step(Engineer(Far), PointPair(ownOpen: true));
        AiVector stand = f.Decider.Doing.Goal;
        AiVector landing = stand + TFEngineerSpots.Forward(TFEngineerSpots.YawTo(stand, OwnPoint)) * 72f;
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
    /// The dispenser goes down from beside his gun, facing back from the point, his box clear of the gun's (round 2: from 40 u
    /// to its side a grown gun held him): live on cp_granary, from 48 u behind the gun it landed 120 u off, and from the
    /// dispenser's reach his wrench never reached the gun (183 swings, no upgrade).
    /// </summary>
    [Fact]
    public void TheDispenserGoesDownFromBesideHisGun()
    {
        AiVector sentry = new(700f, 256f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry)));
        f.Step(Engineer(sentry + new AiVector(-200f, 0f, 0f), metal: 200), TFField.Ctf());
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.True(f.Decider.Doing.HasGoal);
        Assert.InRange(f.Decider.Doing.Goal.DistanceTo(sentry), TFEngineerSpots.WorkClearUnits, 80f);
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

    /// <summary>
    /// His gun hurt while the dispenser's blueprint is out: he takes the wrench, which puts the blueprint away (the review's C1:
    /// he stood by a burning gun holding the blueprint).
    /// </summary>
    [Fact]
    public void AHurtGunWhileTheDispensersBlueprintIsOutTakesTheWrench()
    {
        AiVector sentry = new(700f, 256f, 0f);
        var f = Field(Hud((int)TFObjectType.Dispenser + 1, Row(TFEngineerPanel.Sentry, sentry, health: 60)));
        IntentCommand i = f.Step(Engineer(sentry + new AiVector(-40f, 0f, 0f), weapon: 99), TFField.Ctf());
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Wrench), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>
    /// A dispenser refused five times waits stock's retry: he gives it up, takes the wrench to his gun, and clicks no more for
    /// the retry's 10 s (the review's C2: 68 refusals and 77 s at one spot on ctf_2fort).
    /// </summary>
    [Fact]
    public void ADispenserRefusedFiveTimesIsGivenUpForStocksRetry()
    {
        AiVector sentry = new(700f, 256f, 0f);
        var f = Field(Hud((int)TFObjectType.Dispenser + 1, Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216)));
        // At his dispenser stand: 64 u to the gun's side, his box clear of it.
        AiVector me = sentry + new AiVector(0f, 64f, 0f);
        float yaw = 180f;
        int clicks = 0, wrenchAt = -1;
        int steps = (int)(9f / (DecideSchedule.Period * 0.015f)) + 200;
        for (int n = 0; n < steps; n++)
        {
            IntentCommand i = f.Step(Engineer(me, weapon: 99, yaw: yaw), TFField.Ctf());
            if (i.Action is { Kind: ActionKind.Fire, Style: FireStyle.Tap })
                clicks += wrenchAt < 0 ? 1 : 100;
            if (i.Action is { Kind: ActionKind.SelectWeapon, WeaponSlot: EngineBotSelf.ByKind + Wrench } && wrenchAt < 0)
                wrenchAt = n;
            if (i.Look.Kind == LookKind.Point)
                yaw = YawTo(me, i.Look.Point);
            if (wrenchAt >= 0 && (n - wrenchAt) * DecideSchedule.Period * 0.015f > 9f)
                break;
        }

        Assert.True(wrenchAt >= 0);
        Assert.Equal(EngineerTries, clicks);
    }

    private sealed class SeesNothing : ICellSight
    {
        public float Range => 1024f;

        public bool Visible(int a, int b) => false;

        public float Exposure(int cell) => 0f;
    }

    /// <summary>
    /// With no spot at his point (nothing sees it) the nest does not hold him: an enemy 600 u off is fought as by the general play,
    /// his gun taken out (the review's C3: with the nest still holding him, only one inside 300 u was).
    /// </summary>
    [Fact]
    public void WithNoSpotAtHisPointTheNestDoesNotHoldHim()
    {
        var f = new TFField(weapons: Weapons, seed: SeedOf(TFEngineerClass.Nest), sight: new SeesNothing()) { Hud = Hud() };
        var soldier = new ActorFacts(Enemy, Far + new AiVector(-600f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        IntentCommand i = f.StepDrawn(Engineer(Far, weapon: Wrench), TFField.Ctf(), soldier);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Shotgun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>A nest is not taken down before a spot at the new point is known (the review's C3: a working nest came down and none replaced it).</summary>
    [Fact]
    public void ANestIsNotTakenDownWhileTheNewPointHasNoSpot()
    {
        AiVector sentry = new(3000f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var f = new TFField(weapons: Weapons, seed: SeedOf(TFEngineerClass.Nest), sight: new SeesNothing())
        {
            Hud = Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, max: 216, health: 216)),
        };
        int each = (int)(15f / (DecideSchedule.Period * 0.015f));
        bool down = false;
        for (int stage = 0; stage <= 2 && !down; stage++)
        {
            for (int n = 0; n < each && !down; n++)
                down = IsRow(f, f.Step(Engineer(me, weapon: Wrench), Points(stage)), "destroy_sentry");
        }

        Assert.False(down);
    }

    /// <summary>
    /// His team carrying the enemy's flag, the Gunslinger's point stays the flag's home: his mini by it stays up (the review's
    /// C4: his point went to his own base and the mini came down).
    /// </summary>
    [Fact]
    public void TheGunslingersPointStaysTheEnemyFlagsHomeWhileItIsCarried()
    {
        AiVector mini = new(3400f, 256f, 0f), me = mini + new AiVector(-40f, 0f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, mini, health: 100, max: 100)), archetype: TFEngineerClass.Gunslinger);
        f.Step(Engineer(me, weapon: Shotgun), TFField.Ctf());
        bool down = false;
        for (int n = 0; n < 40 && !down; n++)
            down = IsRow(f, f.Step(Engineer(me, weapon: Shotgun), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: new AiVector(900f, 256f, 0f), blueCarrier: 1)), "destroy_sentry");
        Assert.False(down);
    }

    /// <summary>
    /// The Wrangler lets the gun go when the enemy comes inside its reach and it is whole: the gun fights on its own (the review's
    /// C5: wrangled 344 s of 438 s).
    /// </summary>
    [Fact]
    public void TheWranglerLetsGoWhenTheEnemyComesIntoTheGunsReach()
    {
        AiVector sentry = new(700f, 256f, 0f), me = sentry + new AiVector(-40f, 0f, 0f);
        var f = Field(WranglerHud(Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216),
                                  Row(TFEngineerPanel.Dispenser, sentry + new AiVector(-80f, 40f, 0f), level: 3, health: 216, max: 216)),
                      archetype: TFEngineerClass.Wrangler);
        var far = new ActorFacts(Enemy, sentry + new AiVector(1300f, 0f, 0f), default, Shotgun, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier);
        var near = far with { Origin = sentry + new AiVector(800f, 0f, 0f) };
        Assert.Equal(ActionKind.Fire, f.StepDrawn(Engineer(me, weapon: Laser), TFField.Ctf(), far).Action.Kind);
        IntentCommand i = f.StepDrawn(Engineer(me, weapon: Laser), TFField.Ctf(), near);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Wrench), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>
    /// The round's restart sends him home alive: the spot's patience starts again, as a death's does (the review's C6: the first
    /// spot was given up mid-walk at 70 s in every granary run).
    /// </summary>
    [Fact]
    public void ARoundRestartStartsTheSpotsPatienceAgain()
    {
        var room = new TeamZone(TFField.Red, ZoneKind.RespawnRoom, new AiVector(0f, 0f, -16f), new AiVector(192f, 512f, 200f));
        // His entrance stands (a life before): the walk out goes to the sentry spot.
        var f = new TFField(weapons: Weapons, seed: SeedOf(TFEngineerClass.Nest), zones: [room]) { Hud = Hud(0, Row(TFEngineerPanel.Entrance, new AiVector(400f, 256f, 0f))) };
        AiVector home = new(96f, 256f, 0f);
        f.Step(Engineer(home), TFField.Ctf());
        AiVector spot = f.Decider.Doing.Goal;
        float step = DecideSchedule.Period * 0.015f;
        // Out in the round's first life (the waiting round), never reaching the spot...
        for (int n = 0; n < (int)(25f / step); n++)
            f.Step(Engineer(Far), TFField.Ctf());
        // ...then the restart puts him back in his room, alive: 50 s in all, past the first walk's patience.
        Assert.True(30f + home.DistanceTo(spot) / 150f < 50f);
        for (int n = 0; n < (int)(25f / step); n++)
            f.Step(Engineer(home), TFField.Ctf());
        Assert.Equal(spot, f.Decider.Doing.Goal);
    }

    // ---- the teleporter (stock's CTFBotEngineerBuildTeleportEntrance and the nest's exit) ----

    private static readonly TeamZone RedRoom = new(TFField.Red, ZoneKind.RespawnRoom, new AiVector(0f, 0f, -16f), new AiVector(192f, 512f, 200f));
    private static readonly AiVector InTheRoom = new(96f, 256f, 0f);

    private static readonly TeamZone RedCabinet = new(TFField.Red, ZoneKind.Resupply, new AiVector(16f, 16f, 0f), new AiVector(64f, 64f, 96f));

    private static TFField RoomField(TFHud hud) => new(weapons: Weapons, seed: SeedOf(TFEngineerClass.Nest), zones: [RedRoom, RedCabinet]) { Hud = hud };

    /// <summary>Stock's first job each life: out of his spawn room he walks to an entrance spot on his way, before the sentry.</summary>
    [Fact]
    public void OutOfHisSpawnRoomHeWalksToAnEntranceSpotOnHisWay()
    {
        var f = RoomField(Hud());
        f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
        Assert.Equal(TFDecider.ClassBranch, f.Branch);
        Assert.Equal("to the entrance spot", f.Decider.Doing.Verdict);
        AiVector goal = f.Decider.Doing.Goal;
        Assert.False(RedRoom.Contains(goal + new AiVector(0f, 0f, 16f)), $"{goal}");
        Assert.InRange(goal.DistanceTo(InTheRoom), 64f, TFEngineerSpots.EntranceWalkUnits);
        Assert.True(goal.X > InTheRoom.X && goal.X < TheirPoint.X, $"{goal}: not on his way to the point");
    }

    /// <summary>
    /// Live at 4cd754fc10 on ctf_2fort the round's restart sent him back to the same entrance stand with its patience still
    /// running from the first round, and he gave it up at once: no teleporter in six runs. A death part way to the stand
    /// starts its patience again: 20 s, a death, then 15 s more is still inside a new life's 30 s.
    /// </summary>
    [Fact]
    public void ARespawnStartsTheEntrancesPatienceAgain()
    {
        var f = RoomField(Hud());
        float step = DecideSchedule.Period * 0.015f;
        // Held where he came into the world (the same walks out, so the same stand both lives).
        for (int n = 0; n < (int)(20f / step); n++)
            f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
        Assert.Equal("to the entrance spot", f.Decider.Doing.Verdict);
        f.Step(Engineer(InTheRoom) with { Flags = SelfFlags.None }, PointPair(ownOpen: false));
        for (int n = 0; n < (int)(15f / step); n++)
        {
            f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
            Assert.Equal("to the entrance spot", f.Decider.Doing.Verdict);
        }
    }

    /// <summary>A built exit lost later is walked to again with a new patience (the stand's clock from before it was built is stale).</summary>
    [Fact]
    public void ALostExitIsWalkedToWithANewPatience()
    {
        AiVector at = NestGun + new AiVector(-64f, -64f, 0f);
        var f = Field(NestHud());
        float step = DecideSchedule.Period * 0.015f;
        f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
        Assert.Equal("to the exit spot", f.Decider.Doing.Verdict);
        // Built, standing 10 s, then lost: stock's 30 s retry, then the walk again.
        f.Hud = NestHud(exit: true);
        for (int n = 0; n < (int)(10f / step); n++)
            f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
        f.Hud = NestHud();
        bool walked = false;
        for (int n = 0; n < (int)(45f / step) && !walked; n++)
        {
            f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
            walked = f.Decider.Doing.Verdict == "to the exit spot";
        }

        Assert.True(walked, "the lost exit's stand was given up as soon as stock's retry let him walk to it");
    }

    [Fact]
    public void AtTheEntranceStandHeOrdersTheEntrancesBlueprint()
    {
        var f = RoomField(Hud());
        f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
        AiVector stand = f.Decider.Doing.Goal;
        IntentCommand i = f.Step(Engineer(stand), PointPair(ownOpen: false));
        Assert.True(IsRow(f, i, "build_teleporter_entrance"), $"{i.Action}");
    }

    /// <summary>A life that starts out of his spawn room (no leg out of it) builds no entrance: stock's leg is the walk from spawn.</summary>
    [Fact]
    public void ALifeOutOfHisSpawnRoomBuildsNoEntrance()
    {
        var f = RoomField(Hud());
        f.Step(Engineer(new AiVector(1200f, 256f, 0f)), PointPair(ownOpen: false));
        Assert.NotEqual("to the entrance spot", f.Decider.Doing.Verdict);
    }

    /// <summary>Stock: "go back to our resupply cabinet after planting the teleporter entrance" (IsAmmoFull).</summary>
    [Fact]
    public void WithHisEntranceDownOnTheLegHeRefillsBeforeTheSentry()
    {
        var f = RoomField(Hud());
        f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
        f.Hud = Hud(0, Row(TFEngineerPanel.Entrance, new AiVector(400f, 256f, 0f), building: true));
        f.Step(Engineer(new AiVector(330f, 256f, 0f), metal: 150), PointPair(ownOpen: false));
        Assert.Equal("fetching metal", f.Decider.Doing.Verdict);
    }

    /// <summary>
    /// Ours (stock never moves its entrance): a life that starts in his spawn room 3,000 u or more from his entrance finds it
    /// stale (on cp_granary red respawns by the middle once it holds it, and no teammate passed the base's entrance); he
    /// destroys it, and builds a new one on this leg.
    /// </summary>
    [Fact]
    public void AStaleEntranceIsDestroyedForANewOneOnThisLeg()
    {
        AiVector far = new(3500f, 256f, 0f);
        var f = RoomField(Hud(0, Row(TFEngineerPanel.Entrance, far)));
        f.Step(Engineer(new AiVector(2400f, 256f, 0f)), PointPair(ownOpen: false));
        // The round's restart (or a respawn) puts him in his room: a jump with no death.
        IntentCommand i = f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
        Assert.True(IsRow(f, i, "destroy_teleporter_entrance"), $"{i.Action}");
        f.Hud = Hud();
        f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
        Assert.Equal("to the entrance spot", f.Decider.Doing.Verdict);
    }

    [Fact]
    public void AnEntranceByHisSpawnIsKept()
    {
        var f = RoomField(Hud(0, Row(TFEngineerPanel.Entrance, new AiVector(600f, 256f, 0f))));
        f.Step(Engineer(new AiVector(2400f, 256f, 0f)), PointPair(ownOpen: false));
        for (int n = 0; n < 20; n++)
        {
            IntentCommand i = f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
            Assert.False(IsRow(f, i, "destroy_teleporter_entrance"), $"step {n}: {i.Action}");
        }
    }

    /// <summary>
    /// Live on cp_granary's mid ledge he jumped 50 u short of his dispenser stand for 52 s: held up close for the patience, he
    /// takes his place as the stand and orders the blueprint.
    /// </summary>
    [Fact]
    public void HeldUpCloseToHisStandHeBuildsFromWhereHeStands()
    {
        AiVector sentry = new(700f, 256f, 0f), held = sentry + new AiVector(0f, 64f + 50f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216)));
        float step = DecideSchedule.Period * 0.015f;
        bool ordered = false;
        for (int n = 0; n < (int)(9f / step) && !ordered; n++)
            ordered = IsRow(f, f.Step(Engineer(held, metal: 200), TFField.Ctf()), "build_dispenser");
        Assert.True(ordered, "never ordered the dispenser 50 u short of its stand");
    }

    /// <summary>
    /// The review's 4a: a work stand taken as reached out of the wrench's reach is a stall (he stops, never swings). Held up within
    /// 96 u of his work stand but 140 u from the gun, he keeps walking to it past the patience.
    /// </summary>
    [Fact]
    public void HeldUpBeyondTheWrenchsReachHeKeepsWalkingToHisWorkStand()
    {
        AiVector sentry = new(700f, 256f, 0f), held = sentry + new AiVector(140f, 0f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, health: 60)));
        float step = DecideSchedule.Period * 0.015f;
        for (int n = 0; n < (int)(9f / step); n++)
        {
            f.Step(Engineer(held, weapon: Wrench), TFField.Ctf());
            Assert.True(f.Decider.Doing.HasGoal, $"step {n}: stopped {held.DistanceTo(sentry)} u from the gun");
        }
    }

    /// <summary>
    /// The review's 4b: the patience runs from when he first comes close, not from when the stand was chosen. After a long walk
    /// he arrives 80 u from his build stand: he walks on, he does not build from there.
    /// </summary>
    [Fact]
    public void AfterALongWalkHeStillWalksTheLastStretchToHisStand()
    {
        var f = Field(Hud());
        AiVector stand = SpotOf(f);
        float step = DecideSchedule.Period * 0.015f;
        for (int n = 0; n < (int)(8f / step); n++)
            f.Step(Engineer(Far), TFField.Ctf());
        AiVector close = stand + (Far - stand).Normalised() * 80f;
        IntentCommand i = f.Step(Engineer(close), TFField.Ctf());
        Assert.False(IsRow(f, i, "build_sentry"), $"{i.Action}");
        Assert.True(f.Decider.Doing.HasGoal);
        Assert.True(f.Decider.Doing.Goal.DistanceTo(stand) < 1f, $"{f.Decider.Doing.Goal} against {stand}");
    }

    [Fact]
    public void BeforeItsPatienceHeWalksOnToHisStand()
    {
        AiVector sentry = new(700f, 256f, 0f), held = sentry + new AiVector(0f, 64f + 50f, 0f);
        var f = Field(Hud(0, Row(TFEngineerPanel.Sentry, sentry, level: 3, health: 216, max: 216)));
        float step = DecideSchedule.Period * 0.015f;
        for (int n = 0; n < (int)(4f / step); n++)
        {
            IntentCommand i = f.Step(Engineer(held, metal: 200), TFField.Ctf());
            Assert.False(IsRow(f, i, "build_dispenser"), $"step {n}: ordered before the patience");
            Assert.True(f.Decider.Doing.HasGoal, $"step {n}");
        }
    }

    private const int ArmKindId = 26;

    private static TFField OrbField(bool dispenser = false)
    {
        var weapons = new WeaponKnowledge([.. Weapons.All, new WeaponFacts(ArmKindId, TFEngineerClass.MechanicalArm, 0, 0.67f, 0f, false, false, 0f)]);
        var hud = new TFHud([Kit[0], new(TFLoadoutSlot.Secondary, 528, TFEngineerClass.MechanicalArm, ArmKindId, []), Kit[2]],
                            [.. Row(TFEngineerPanel.Sentry, new AiVector(700f, 256f, 0f), level: 2, health: 180, max: 180),
                             .. dispenser ? Row(TFEngineerPanel.Dispenser, new AiVector(628f, 330f, 0f)) : [], new TFMeter(TFEngineerPanel.Placing, 0)], []);
        return new TFField(weapons: weapons, seed: SeedOf(TFEngineerClass.BattleEngie)) { Hud = hud };
    }

    private static ThingFacts Rocket(AiVector at, AiVector velocity)
        => new(40, ThingKind.Projectile, (int)TFThingType.Rocket, at, velocity, TFField.Blue, OwnerSlot: 5);

    private static readonly AiVector OrbMe = new(636f, 256f, 0f);

    /// <summary>The Short Circuit's orb eats the enemy's projectiles it passes: a rocket closing on him, the arm comes out.</summary>
    [Fact]
    public void ARocketClosingOnTheBattleEngieBringsOutTheShortCircuit()
    {
        IntentCommand i = OrbField().StepSeeing(Engineer(OrbMe, weapon: Wrench, metal: 200), TFField.Ctf(),
                                               [Rocket(OrbMe + new AiVector(500f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + ArmKindId), (i.Action.Kind, i.Action.WeaponSlot));
    }

    [Fact]
    public void WithTheShortCircuitOutHeFiresTheOrbAtTheRocket()
    {
        AiVector rocket = OrbMe + new AiVector(500f, 0f, 0f);
        IntentCommand i = OrbField().StepSeeing(Engineer(OrbMe, weapon: ArmKindId, metal: 200), TFField.Ctf(), [Rocket(rocket, new AiVector(-1100f, 0f, 0f))]);
        Assert.Equal(ActionKind.AltFire, i.Action.Kind);
        Assert.Equal(LookKind.Point, i.Look.Kind);
        Assert.True(i.Look.Point.DistanceTo(rocket) < 200f, $"{i.Look.Point}");
    }

    [Fact]
    public void ARocketGoingAwayIsNoMark()
    {
        IntentCommand i = OrbField().StepSeeing(Engineer(OrbMe, weapon: Wrench, metal: 200), TFField.Ctf(),
                                               [Rocket(OrbMe + new AiVector(-300f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
        Assert.NotEqual((ActionKind.SelectWeapon, EngineBotSelf.ByKind + ArmKindId), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>A rocket closing while he swings at his gun: the swing gives way to the arm (the wrench's fire left the hands no turn).</summary>
    [Fact]
    public void ARocketClosingWhileHeWorksHisGunBringsOutTheShortCircuit()
    {
        // His dispenser stands, so the job is the gun's upgrade: the wrench swings at it.
        IntentCommand i = OrbField(dispenser: true).StepSeeing(Engineer(OrbMe, weapon: Wrench, metal: 100), TFField.Ctf(),
                                                              [Rocket(OrbMe + new AiVector(500f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + ArmKindId), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>The orb costs 65 metal (tf_mecharm_orb_cost): short of it, no orb.</summary>
    [Fact]
    public void ShortOfTheOrbsMetalHeLeavesTheArmAway()
    {
        IntentCommand i = OrbField().StepSeeing(Engineer(OrbMe, weapon: Wrench, metal: 40), TFField.Ctf(),
                                               [Rocket(OrbMe + new AiVector(500f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
        Assert.NotEqual((ActionKind.SelectWeapon, EngineBotSelf.ByKind + ArmKindId), (i.Action.Kind, i.Action.WeaponSlot));
    }

    private static readonly AiVector NestGun = new(2600f, 256f, 0f);

    private static TFHud NestHud(bool dispenser = true, bool exit = false)
        => Hud(0, [.. Row(TFEngineerPanel.Sentry, NestGun, level: 3, health: 216, max: 216), .. Row(TFEngineerPanel.Entrance, new AiVector(400f, 256f, 0f)),
                   .. dispenser ? Row(TFEngineerPanel.Dispenser, NestGun + new AiVector(-72f, 64f, 0f), level: 3, health: 216, max: 216) : [],
                   .. exit ? Row(TFEngineerPanel.Exit, NestGun + new AiVector(-160f, 0f, 0f)) : []]);

    /// <summary>The nest's exit after the dispenser (stock, tf_bot_engineer_building.cpp:425-465): behind the gun from the point.</summary>
    [Fact]
    public void WithHisEntranceUpHeBuildsTheExitBehindHisGun()
    {
        var f = Field(NestHud());
        f.Step(Engineer(NestGun + new AiVector(-64f, -64f, 0f), metal: 200), PointPair(ownOpen: false));
        Assert.Equal("to the exit spot", f.Decider.Doing.Verdict);
        AiVector goal = f.Decider.Doing.Goal;
        Assert.True(goal.DistanceTo(NestGun) < 400f, $"{goal}");
        Assert.True(goal.X < NestGun.X, $"{goal}: not behind the gun from the point at {TheirPoint}");
    }

    /// <summary>
    /// The review's fix 5: a pad stand he never reaches (here he is held 700 u off) is given up after the sentry spot's patience,
    /// as five refused clicks give it up, and the exit waits stock's 30 s retry.
    /// </summary>
    [Fact]
    public void AnExitStandNeverReachedIsGivenUpAfterItsPatience()
    {
        AiVector held = NestGun + new AiVector(-700f, 0f, 0f);
        var f = Field(NestHud());
        float step = DecideSchedule.Period * 0.015f;
        f.Step(Engineer(held, metal: 200), PointPair(ownOpen: false));
        Assert.Equal("to the exit spot", f.Decider.Doing.Verdict);
        int n = 0, gaveUpAt = -1;
        for (; n < (int)(45f / step) && gaveUpAt < 0; n++)
        {
            f.Step(Engineer(held, metal: 200), PointPair(ownOpen: false));
            if (f.Decider.Doing.Verdict != "to the exit spot")
                gaveUpAt = n;
        }

        Assert.True(gaveUpAt >= 0, "still walking at the exit's stand after 45 s");
        Assert.True(gaveUpAt * step >= TFEngineerPatienceSeconds - 1f, $"given up at {gaveUpAt * step} s, before the patience");
        // Given up, not a one-decide blink: off the walk to the exit for stock's 30 s retry (checked to 20 s on) ...
        for (int k = 0; k < (int)(20f / step); k++)
        {
            f.Step(Engineer(held, metal: 200), PointPair(ownOpen: false));
            Assert.NotEqual("to the exit spot", f.Decider.Doing.Verdict);
        }

        // ... and back on it once the retry is done.
        bool again = false;
        for (int k = 0; k < (int)(15f / step) && !again; k++)
        {
            f.Step(Engineer(held, metal: 200), PointPair(ownOpen: false));
            again = f.Decider.Doing.Verdict == "to the exit spot";
        }

        Assert.True(again, "never walked to the exit again after stock's 30 s retry");
    }

    /// <summary>The entrance's twin: an entrance stand never reached is given up for the life, not walked at again a decide later.</summary>
    [Fact]
    public void AnEntranceStandNeverReachedIsGivenUpForTheLife()
    {
        var f = RoomField(Hud());
        float step = DecideSchedule.Period * 0.015f;
        f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
        Assert.Equal("to the entrance spot", f.Decider.Doing.Verdict);
        int gaveUpAt = -1;
        for (int n = 0; n < (int)(45f / step) && gaveUpAt < 0; n++)
        {
            f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
            if (f.Decider.Doing.Verdict != "to the entrance spot")
                gaveUpAt = n;
        }

        Assert.True(gaveUpAt >= 0, "still walking at the entrance's stand after 45 s");
        for (int k = 0; k < (int)(40f / step); k++)
        {
            f.Step(Engineer(InTheRoom), PointPair(ownOpen: false));
            Assert.NotEqual("to the entrance spot", f.Decider.Doing.Verdict);
        }
    }

    /// <summary>The sentry spot's patience a pad stand takes (EngineerPlay's SpotPatienceSeconds).</summary>
    private const float TFEngineerPatienceSeconds = 30f;

    [Fact]
    public void WithNoEntranceHeBuildsNoExit()
    {
        AiVector at = NestGun + new AiVector(-64f, -64f, 0f);
        var f = Field(Hud(0, [.. Row(TFEngineerPanel.Sentry, NestGun, level: 3, health: 216, max: 216),
                              .. Row(TFEngineerPanel.Dispenser, NestGun + new AiVector(-72f, 64f, 0f), level: 3, health: 216, max: 216)]));
        f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
        Assert.NotEqual("to the exit spot", f.Decider.Doing.Verdict);
    }

    /// <summary>Stock's exit retry (30 s): a lost exit is not built again sooner.</summary>
    [Fact]
    public void ALostExitIsNotBuiltAgainForStocksRetry()
    {
        AiVector at = NestGun + new AiVector(-64f, -64f, 0f);
        var f = Field(NestHud(exit: true));
        f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
        f.Hud = NestHud();
        float step = DecideSchedule.Period * 0.015f;
        for (int n = 0; n < (int)(20f / step); n++)
        {
            f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
            Assert.NotEqual("to the exit spot", f.Decider.Doing.Verdict);
        }

        for (int n = 0; n < (int)(12f / step); n++)
            f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
        Assert.Equal("to the exit spot", f.Decider.Doing.Verdict);
    }

    [Fact]
    public void HurtHeBuildsNoExit()
    {
        AiVector at = NestGun + new AiVector(-64f, -64f, 0f);
        var f = Field(NestHud());
        f.Step(Engineer(at, metal: 200), PointPair(ownOpen: false));
        f.Step(Engineer(at, metal: 200, health: 90), PointPair(ownOpen: false));
        Assert.NotEqual("to the exit spot", f.Decider.Doing.Verdict);
    }

    private static readonly AiVector FarZone = new(2000f, 256f, 0f);

    /// <summary>A flag map whose red capture zone lies far from red's flag (ctf_2fort's are one room): his flag's state as given.</summary>
    private static ObjectiveKnowledge FlagApart(ObjectiveState red = ObjectiveState.Home, AiVector? redAt = null) => new([
        new Objective(TFField.RedFlagId, ObjectiveKind.Carry, TFField.Red, 1 << TFField.Blue, red, redAt ?? TFField.RedFlag),
        new Objective(TFField.BlueFlagId, ObjectiveKind.Carry, TFField.Blue, 1 << TFField.Red, ObjectiveState.Home, TFField.BlueFlag),
        new Objective(TFField.RedZoneId, ObjectiveKind.Deliver, TFField.Red, (1 << TFField.Red) | (1 << TFField.Blue), ObjectiveState.Open, FarZone),
        new Objective(TFField.BlueZoneId, ObjectiveKind.Deliver, TFField.Blue, (1 << TFField.Red) | (1 << TFField.Blue), ObjectiveState.Open, TFField.BlueZone),
    ]);

    /// <summary>
    /// The Nest on a flag map defends his flag where it lies at home (stock builds by the capture zone, with Valve's note that it
    /// should be the flag): live on ctf_2fort a gun by the capture zone killed nobody in nine runs while the enemy took the flag.
    /// </summary>
    [Fact]
    public void OnAFlagMapTheNestBuildsByHisFlagNotHisCaptureZone()
    {
        var f = Field(Hud());
        f.Step(Engineer(Far), FlagApart());
        AiVector goal = f.Decider.Doing.Goal;
        Assert.True(Flat(goal, TFField.RedFlag) <= EngineerFlagReach + 80f, $"{goal}");
    }

    /// <summary>His flag carried off when he comes back into the world elsewhere (the spots worked out again): still its home.</summary>
    [Fact]
    public void WithHisFlagCarriedOffTheNestStillDefendsItsHome()
    {
        var f = Field(Hud());
        AiVector carried = new(1800f, 256f, 0f);
        float step = DecideSchedule.Period * 0.015f;
        f.Step(Engineer(Far), FlagApart());
        // Past the points' 2 s look, so the carried flag is read.
        for (int n = 0; n < (int)(3f / step); n++)
            f.Step(Engineer(Far), FlagApart(ObjectiveState.Carried, carried));
        f.Step(Engineer(Far) with { Flags = SelfFlags.None }, FlagApart(ObjectiveState.Carried, carried));
        for (int n = 0; n < (int)(3f / step); n++)
            f.Step(Engineer(new AiVector(600f, 256f, 0f)), FlagApart(ObjectiveState.Carried, carried));
        AiVector goal = f.Decider.Doing.Goal;
        Assert.True(Flat(goal, TFField.RedFlag) <= EngineerFlagReach + 80f, $"{goal}");
    }

    /// <summary>The flag nest's reach (EngineerPlay's FlagNestReach).</summary>
    private const float EngineerFlagReach = 700f;

    private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>On a control-point field he builds by his own point the enemy may take, not the enemy's he may take (stock's GetMyControlPoint for an Engineer).</summary>
    [Fact]
    public void OnAPointFieldTheNestBuildsByHisOwnPointTheEnemyMayTake()
    {
        var f = Field(Hud());
        f.Step(Engineer(Far), PointPair(ownOpen: true));
        Assert.True(f.Decider.Doing.Goal.DistanceTo(OwnPoint) < f.Decider.Doing.Goal.DistanceTo(TheirPoint), $"{f.Decider.Doing.Goal}");
    }

    /// <summary>With no own point in play the Nest builds by the point his team may take (stock).</summary>
    [Fact]
    public void WithNoOwnPointInPlayTheNestBuildsByThePointHisTeamMayTake()
    {
        var f = Field(Hud());
        f.Step(Engineer(Far), PointPair(ownOpen: false));
        Assert.True(f.Decider.Doing.Goal.DistanceTo(TheirPoint) < f.Decider.Doing.Goal.DistanceTo(OwnPoint), $"{f.Decider.Doing.Goal}");
    }

    /// <summary>The Gunslinger attacks: with both in play he builds by the point his team may take.</summary>
    [Fact]
    public void OnAPointFieldTheGunslingerBuildsByThePointHisTeamMayTake()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.Gunslinger);
        f.Step(Engineer(Far), PointPair(ownOpen: true));
        Assert.True(f.Decider.Doing.Goal.DistanceTo(TheirPoint) < f.Decider.Doing.Goal.DistanceTo(OwnPoint), $"{f.Decider.Doing.Goal}");
    }

    /// <summary>A locked point is no point of his: with the enemy's locked and his own open, his own.</summary>
    [Fact]
    public void ALockedPointIsNotHisPoint()
    {
        var f = Field(Hud(), archetype: TFEngineerClass.Gunslinger);
        f.Step(Engineer(Far), PointPair(ownOpen: true, theirLocked: true));
        Assert.True(f.Decider.Doing.Goal.DistanceTo(OwnPoint) < f.Decider.Doing.Goal.DistanceTo(TheirPoint), $"{f.Decider.Doing.Goal}");
    }

    private static readonly AiVector OwnPoint = new(1000f, 256f, 0f), TheirPoint = new(3200f, 256f, 0f);

    private static ObjectiveKnowledge PointPair(bool ownOpen, bool theirLocked = false) => new([
        new Objective(1, ObjectiveKind.Capture, TFField.Red, ownOpen ? 1 << TFField.Blue : 0, ownOpen ? ObjectiveState.Open : ObjectiveState.Locked, OwnPoint),
        new Objective(2, ObjectiveKind.Capture, TFField.Blue, 1 << TFField.Red, theirLocked ? ObjectiveState.Locked : ObjectiveState.Open, TheirPoint),
    ]);
}
