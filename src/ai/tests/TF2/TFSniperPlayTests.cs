using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Shared;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Sniper's play (plan_bots_teams_tf2 P7, the Sniper research's §4.3-§4.4, lane A of the review's split): the
/// scope as a one-shot sent on a change only, the headshot's zoom gate, the charge a kill needs by the class seen, the
/// weapons by range (SMG within 750 u, backing off within 400 u, the Kukri within 200 u), the spot it holds and leaves,
/// the glances behind, and the Jarate archetype's throws.
/// </summary>
public sealed class TFSniperPlayTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Enemy2 = new(6, 0x4000_0001), Mate = new(4, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    internal const int Rifle = 60, Smg = 61, Kukri = 62, JarKind = 63;

    internal static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Rifle, "tf_weapon_sniperrifle", 0, 1.5f, 0f, false, false, 0f),
        new WeaponFacts(Smg, "tf_weapon_smg", 25, 0.1f, 1.1f, true, false, 0f),
        new WeaponFacts(Kukri, "tf_weapon_club", 0, 0.8f, 0f, false, false, 0f),
        new WeaponFacts(JarKind, "tf_weapon_jar", 0, 1f, 0f, false, false, 1000f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind) => new(slot, defindex, itemClass, kind, []);

    internal static TFHud Kit(bool zoomed = false, float charge = 0f, bool jar = false, float jarMeter = 1f)
        => new([Item(TFLoadoutSlot.Primary, 14, "tf_weapon_sniperrifle", Rifle),
                jar ? Item(TFLoadoutSlot.Secondary, 58, "tf_weapon_jar", JarKind) : Item(TFLoadoutSlot.Secondary, 16, "tf_weapon_smg", Smg),
                Item(TFLoadoutSlot.Melee, 3, "tf_weapon_club", Kukri)],
               [new TFMeter(TFSniperMeters.Zoomed, zoomed ? 1f : 0f), new TFMeter(TFSniperMeters.Charge, charge), .. jar ? new[] { new TFMeter(TFSniperMeters.Jar, jarMeter) } : []],
               []);

    internal static SelfState Me(AiVector at, int weapon = Rifle, bool grounded = true)
    {
        // A clipless weapon in hand reads -1 live (Arsenal.cs), as the rifle is: not a dry launcher (the review's F3).
        SelfState s = TFField.Self(at, health: 125, clip: weapon == Smg ? 25 : -1, reserve: 25, weapon: weapon, playerClass: TFClass.Sniper);
        return grounded ? s : s with { Flags = s.Flags & ~SelfFlags.Grounded };
    }

    internal static ActorFacts Drawn(ActorId id, AiVector at, TFClass cls, int team = TFField.Blue, ActorConditions conditions = ActorConditions.None, AiAngles facing = default)
        => new(id, at, AiVector.Zero, Rifle, ActorActivity.None, 1f, team, (int)cls, conditions, 0, facing);

    /// <summary>The first seed whose archetype draw is <paramref name="name"/>: the play's archetype is its seed's (TFDecider.ArchetypeOf).</summary>
    internal static ulong SeedOf(string name)
    {
        TFArchetypeTable table = TFSniperClass.Archetypes();
        for (ulong s = 1; s < 10_000; s++)
        {
            if (table.Draw(s)?.Name == name)
                return s;
        }

        throw new InvalidOperationException(name);
    }

    private static TFField Field(ulong? seed = null, TFFeelSwitches? feel = null) => new(seed: seed ?? SeedOf(TFSniperClass.Marksman), weapons: Weapons, feel: feel);

    private static bool IsZoomPress(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) && row.Name == TFSniperClass.Zoom;

    // ------------------------------------------------------------------ the charge a kill needs

    /// <summary>A headshot does three times max(charge, 50): a 125 or a 150 body needs no charge, a Pyro's 175 needs 59, a Soldier's 200 67.</summary>
    [Fact]
    public void TheChargeAKillNeedsFollowsTheClassesHealth()
        => Assert.Equal([0f, 0f, 59f, 67f], new[] { 125, 150, 175, 200 }.Select(h => TFSniperClass.ChargeToKill(h, medicNear: false)));

    /// <summary>A Heavy's 300 needs 100; with a Medic seen by him, guessed overhealed to 450, the whole 150.</summary>
    [Fact]
    public void AHeavyNeedsAHundredAndAllOfItWithAMedicBySide()
        => Assert.Equal((100f, 150f), (TFSniperClass.ChargeToKill(300, false), TFSniperClass.ChargeToKill(300, true)));

    private static IntentCommand[] Scoped(TFField f, float charge, TFClass cls, int decides, float distance = 1500f)
    {
        f.Hud = Kit(zoomed: true, charge: charge);
        var enemy = Drawn(Enemy, Mid + new AiVector(distance, 0f, 0f), cls);
        return [.. Enumerable.Range(0, decides).Select(_ => f.StepDrawn(Me(Mid), TFField.Ctf(), enemy))];
    }

    /// <summary>A scoped Scout (125) is fired at with no charge at all, once the headshot's zoom gate has passed.</summary>
    [Fact]
    public void AScoutIsFiredAtUncharged()
        => Assert.Contains(Scoped(Field(), 0f, TFClass.Scout, 8), i => i.Action.Kind == ActionKind.Fire);

    /// <summary>A scoped Soldier (200) is not fired at under the 67 a headshot kill needs; the control, at 67, is.</summary>
    [Fact]
    public void ASoldierIsNotFiredAtUnderTheChargeAKillNeeds()
    {
        Assert.DoesNotContain(Scoped(Field(), 66f, TFClass.Soldier, 12), i => i.Action.Kind == ActionKind.Fire);
        Assert.Contains(Scoped(Field(), 67f, TFClass.Soldier, 12), i => i.Action.Kind == ActionKind.Fire);
    }

    /// <summary>No shot inside 0.2 s of the scope coming up (the headshot's gate): the first decides scoped hold the trigger, a later one fires.</summary>
    [Fact]
    public void NoShotBeforeTheHeadshotGate()
    {
        IntentCommand[] decides = Scoped(Field(), 0f, TFClass.Scout, 8);
        int first = Array.FindIndex(decides, i => i.Action.Kind == ActionKind.Fire);
        Assert.True(first * DecideSchedule.Period * 0.015f >= 0.2f, $"fired at decide {first}");
    }

    /// <summary>Scoped, the aim goes at the head of the class drawn, not the body's middle.</summary>
    [Fact]
    public void ScopedTheAimIsAtTheHead()
    {
        IntentCommand last = Scoped(Field(), 0f, TFClass.Scout, 2)[^1];
        Assert.Equal((LookKind.Actor, AimPoint.Head), (last.Look.Kind, last.Look.Aim));
        Assert.True(last.Look.Point.Z > 50f, $"aimed at z {last.Look.Point.Z}");
    }

    // ------------------------------------------------------------------ the scope

    /// <summary>
    /// The scope is a one-shot sent when the HUD's scope differs from what the bot wants: over 5 s on a far target, with the
    /// HUD showing the scope from the decide after the press, one press. (AltFire held as a Tap would toggle it every other tick.)
    /// </summary>
    [Fact]
    public void TheZoomIsPressedOnceForAHeldScope()
    {
        TFField f = Field();
        var enemy = Drawn(Enemy, Mid + new AiVector(1500f, 0f, 0f), TFClass.Soldier);
        int presses = 0;
        bool zoomed = false;
        for (int n = 0; n < 55; n++)
        {
            f.Hud = Kit(zoomed: zoomed, charge: 0f);
            IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), enemy);
            if (IsZoomPress(f, i))
                (presses, zoomed) = (presses + 1, true);
        }

        Assert.Equal(1, presses);
    }

    /// <summary>An enemy inside the SMG's range is no scope's: a scoped bot with an enemy at 600 u presses the scope off.</summary>
    [Fact]
    public void ANearEnemyTakesTheScopeOff()
    {
        TFField f = Field();
        f.Hud = Kit(zoomed: true);
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(600f, 0f, 0f), TFClass.Soldier));
        Assert.True(IsZoomPress(f, i), $"{i.Action.Kind}");
    }

    // ------------------------------------------------------------------ close threats

    private static IntentCommand Near(float distance, int holding = Rifle)
    {
        TFField f = Field();
        f.Hud = Kit();
        return f.StepDrawn(Me(Mid, holding), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(distance, 0f, 0f), TFClass.Soldier));
    }

    /// <summary>The SMG within 750 u (stock's tf_bot.cpp:3394-3398).</summary>
    [Fact]
    public void TheSmgWithinSevenFifty()
        => Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Smg), (Near(600f).Action.Kind, Near(600f).Action.WeaponSlot));

    /// <summary>The Kukri within 200 u (stock's tf_bot_sniper_lurk.cpp:117-121).</summary>
    [Fact]
    public void TheKukriWithinTwoHundred()
        => Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Kukri), (Near(150f, Smg).Action.Kind, Near(150f, Smg).Action.WeaponSlot));

    /// <summary>Past 750 u the rifle: an SMG in hand is put away for it.</summary>
    [Fact]
    public void TheRiflePastSevenFifty()
        => Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Rifle), (Near(1200f, Smg).Action.Kind, Near(1200f, Smg).Action.WeaponSlot));

    /// <summary>
    /// Within 400 u the bot backs off (stock's tf_bot_sniper_attack.cpp:19): at 350 u its step goes away from the enemy, where
    /// the general play's band (320-900 u) would only sidestep.
    /// </summary>
    [Fact]
    public void ItBacksOffWithinFourHundred()
    {
        TFField f = Field(feel: TFFeelSwitches.All with { Fight = false });
        f.Hud = Kit();
        IntentCommand i = f.StepDrawn(Me(Mid, Smg), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(350f, 0f, 0f), TFClass.Soldier));
        Assert.Equal(MoveKind.Strafe, i.Move.Kind);
        Assert.True(i.Move.Direction.X < 0f, $"stepped {i.Move.Direction}");
    }

    // ------------------------------------------------------------------ target choice

    /// <summary>An enemy Sniper comes before a Soldier as near (the research's tiers): the Sniper is the one aimed at.</summary>
    [Fact]
    public void AnEnemySniperComesBeforeASoldier()
    {
        TFField f = Field();
        f.Hud = Kit(zoomed: true);
        IntentCommand i = f.StepDrawn(Me(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(1500f, 100f, 0f), TFClass.Soldier),
                                      Drawn(Enemy2, Mid + new AiVector(1500f, -100f, 0f), TFClass.Sniper));
        Assert.Equal(Enemy2, i.Look.Actor);
    }
}

/// <summary>
/// The Sniper's spots (plan_bots_teams_tf2 C.3.2, the Sniper research's §4.3 "Spots" and "Holding"): vantages onto the
/// objective on the team's own side, in the range band, seeing it by the sight table; the bot goes to one, holds it scoped,
/// and leaves it when nothing comes, after a kill, or when hit by someone it does not see.
/// </summary>
public sealed class TFSniperSpotTests
{
    private static readonly NavGraph Floor = TFField.Floor();

    private static float Flat(AiVector a, AiVector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static readonly AiVector Middle = new(2048f, 256f, 0f);

    /// <summary>
    /// An attacker's vantages onto an objective in the field's middle are 500-1,100 u from it and at least 250 u nearer its
    /// own spawn than the objective is: none on the far side, where the floor goes on as far.
    /// </summary>
    [Fact]
    public void AnAttackersSpotsAreInTheBandOnItsOwnSide()
    {
        var spots = TFSniperSpots.Find(Floor, null, null, Middle, TFField.RedSpawn, attack: true);
        Assert.NotEmpty(spots);
        float objective = Flat(Middle, TFField.RedSpawn);
        Assert.All(spots, s =>
        {
            Assert.InRange(s.At.DistanceTo(Middle), TFSniperSpots.NearestUnits, TFSniperSpots.FarthestUnits);
            Assert.True(Flat(s.At, TFField.RedSpawn) <= objective - TFSniperSpots.AttackBackUnits, $"spot {s.At}");
        });
    }

    /// <summary>A defender's may stand past its flag, but no more than 750 u further from its spawn than the flag.</summary>
    [Fact]
    public void ADefendersSpotsReachNoMoreThanSevenFiftyPast()
    {
        var spots = TFSniperSpots.Find(Floor, null, null, TFField.RedFlag, TFField.RedSpawn, attack: false);
        Assert.NotEmpty(spots);
        float flag = Flat(TFField.RedFlag, TFField.RedSpawn);
        Assert.All(spots, s => Assert.True(Flat(s.At, TFField.RedSpawn) <= flag + TFSniperSpots.DefendPastUnits, $"spot {s.At}"));
    }

    /// <summary>The cells that see one another, by a test's rule; nothing is exposed.</summary>
    private sealed class Sight(Func<int, int, bool> sees) : ICellSight
    {
        public float Range => 1024f;

        public bool Visible(int a, int b) => sees(a, b);

        public float Exposure(int cell) => 0f;
    }

    /// <summary>With a sight table, only a cell that sees the objective is a vantage: here only the floor's south row sees it, and every spot is there.</summary>
    [Fact]
    public void OnlyACellThatSeesTheObjectiveIsASpot()
    {
        var sight = new Sight((a, _) => Floor.Cell(a).Centre.Y < TFField.CellSize);
        var spots = TFSniperSpots.Find(Floor, sight, null, TFField.BlueFlag, TFField.RedSpawn, attack: true);
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.True(s.At.Y < TFField.CellSize, $"spot {s.At}"));
    }

    /// <summary>The field's floor with a raised island beside its west half (y 576-704, z 256) joined to nothing: a roof the bot cannot walk to.</summary>
    private static NavGraph FloorWithIsland()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length, TFField.Width];
        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                ids[x, y] = b.AddCell(new NavCell(x * TFField.CellSize, y * TFField.CellSize, (x + 1) * TFField.CellSize, (y + 1) * TFField.CellSize, 0f,
                                                  ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
                if (x > 0)
                    b.AddBoth(ids[x - 1, y], ids[x, y], NavEdgeType.Walk);
                if (y > 0)
                    b.AddBoth(ids[x, y - 1], ids[x, y], NavEdgeType.Walk);
            }
        }

        CellId? last = null;
        for (int x = 10; x < 30; x++)
        {
            CellId c = b.AddCell(new NavCell(x * TFField.CellSize, 576f, (x + 1) * TFField.CellSize, 704f, 256f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            if (last is { } l)
                b.AddBoth(l, c, NavEdgeType.Walk);
            last = c;
        }

        return b.Build();
    }

    /// <summary>
    /// A cell the bot cannot walk to (another joined-up part of the map: a roof) is no spot, however high: live on cp_granary
    /// the Snipers spent most of their lives walking to spots. The control: without the bot's place the island's cells are spots.
    /// </summary>
    [Fact]
    public void ACellTheBotCannotWalkToIsNoSpot()
    {
        NavGraph g = FloorWithIsland();
        AiVector watch = new(2048f, 256f, 0f);
        var anywhere = TFSniperSpots.Find(g, null, null, watch, TFField.RedSpawn, attack: true);
        Assert.Contains(anywhere, s => s.At.Y > 512f);
        var walkable = TFSniperSpots.Find(g, null, null, watch, TFField.RedSpawn, attack: true, from: new AiVector(1500f, 256f, 0f));
        Assert.NotEmpty(walkable);
        Assert.All(walkable, s => Assert.True(s.At.Y < 512f, $"spot {s.At}"));
    }

    /// <summary>A place refused (an enemy respawn room) is never a spot.</summary>
    [Fact]
    public void ARefusedPlaceIsNoSpot()
    {
        var spots = TFSniperSpots.Find(Floor, null, null, TFField.BlueFlag, TFField.RedSpawn, attack: true, refused: at => at.Y > 200f);
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.True(s.At.Y <= 200f, $"spot {s.At}"));
    }

    /// <summary>The kept spots stand apart, so two Snipers drawing two of them do not stand on each other.</summary>
    [Fact]
    public void TheKeptSpotsStandApart()
    {
        var spots = TFSniperSpots.Find(Floor, null, null, TFField.BlueFlag, TFField.RedSpawn, attack: true);
        for (int i = 0; i < spots.Length; i++)
        {
            for (int j = i + 1; j < spots.Length; j++)
                Assert.True(spots[i].At.DistanceTo(spots[j].At) >= TFSniperSpots.Separation);
        }
    }

    private static readonly AiVector Start = new(1500f, 256f, 0f);

    private static TFField Field(string archetype = TFSniperClass.Marksman) => new(seed: TFSniperPlayTests.SeedOf(archetype), weapons: TFSniperPlayTests.Weapons);

    /// <summary>Steps the bot with nobody in sight, standing it on its goal whenever it walks: where it goes is where it is the next decide.</summary>
    private static List<IntentCommand> Walked(TFField f, int decides, Func<int, AiEvent[]>? events = null)
    {
        var all = new List<IntentCommand>();
        AiVector at = Start;
        for (int n = 0; n < decides; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: false);
            IntentCommand i = f.StepHearing(TFSniperPlayTests.Me(at), TFField.Ctf(), events?.Invoke(n) ?? []);
            all.Add(i);
            if (f.Decider.Doing.HasGoal && f.Decider.Doing.Verdict is "to a sniper spot" or "leaving the spot")
                at = f.Decider.Doing.Goal;
        }

        return all;
    }

    /// <summary>With nobody in sight the Sniper goes to a spot in the band onto a flag, and there holds still, its aim parked on the line, and scopes.</summary>
    [Fact]
    public void ItGoesToASpotAndHoldsItScoped()
    {
        TFField f = Field();
        List<IntentCommand> all = Walked(f, 4);
        Assert.Equal("holding a sight line", f.Decider.Doing.Verdict);
        IntentCommand last = all[^1];
        Assert.Equal((MoveKind.Hold, LookKind.Point), (last.Move.Kind, last.Look.Kind));
        // The scope's own row, not any one-shot (the code review's F4: a voice line passed).
        Assert.Contains(all, i => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row)
                                  && row.Name == TFSniperClass.Zoom);
    }

    private static int FirstLeave(List<IntentCommand> all, TFField f, int from)
    {
        for (int n = from; n < all.Count; n++)
        {
            if (all[n].Move.Kind != MoveKind.Hold)
                return n;
        }

        return -1;
    }

    private static float SecondsOf(int decides) => decides * DecideSchedule.Period * 0.015f;

    /// <summary>At a spot with nobody seen for 10 s (×0.9-1.1, stock's patience) it leaves for another: not before 9 s, by 11 s.</summary>
    [Fact]
    public void NobodySeenForTenSecondsItLeaves()
    {
        TFField f = Field();
        List<IntentCommand> all = Walked(f, 160);
        int held = all.FindIndex(i => i.Move.Kind == MoveKind.Hold);
        int left = FirstLeave(all, f, held);
        Assert.True(held >= 0 && left > held, $"held {held} left {left}");
        Assert.InRange(SecondsOf(left - held), 9f, 11.2f);
    }

    /// <summary>A kill of its own at a spot (the kill feed) and it leaves within 2-8 s, before its patience would have run out.</summary>
    [Fact]
    public void AfterAKillItLeavesWithinEightSeconds()
    {
        TFField f = Field();
        int killAt = -1;
        List<IntentCommand> all = Walked(f, 160, n =>
        {
            if (killAt < 0 && n > 3 && f.Decider.Doing.Verdict == "holding a sight line")
            {
                killAt = n;
                return [AiEvent.KillFeed(f.Bot, new Tick(f.Tick + DecideSchedule.Period), new ActorId(9, 0x4000_0001), f.Bot)];
            }

            return [];
        });
        int left = FirstLeave(all, f, killAt);
        Assert.True(killAt > 0 && left > killAt, $"kill {killAt} left {left}");
        Assert.InRange(SecondsOf(left - killAt), 1.9f, 8.1f);
    }

    /// <summary>Hit at a spot by someone it does not see (a far Sniper), it leaves at once.</summary>
    [Fact]
    public void HitByAnUnseenShooterItLeavesAtOnce()
    {
        TFField f = Field();
        int hitAt = -1;
        List<IntentCommand> all = Walked(f, 40, n =>
        {
            if (hitAt < 0 && n > 3 && f.Decider.Doing.Verdict == "holding a sight line")
            {
                hitAt = n;
                return [AiEvent.Damaged(f.Bot, new Tick(f.Tick + DecideSchedule.Period), new ActorId(9, 0x4000_0001), 50f, new AiVector(1f, 0f, 0f), 0)];
            }

            return [];
        });
        Assert.True(hitAt > 0, "never held");
        Assert.NotEqual(MoveKind.Hold, all[hitAt].Move.Kind);
    }

    private static long Counter(TFField f, string name) => f.Decider.ClassCounters().Single(kv => kv.Key == name).Value;

    /// <summary>Holding spots for two minutes, the Marksman glances behind more often than the Guarded Sniper (its Razorback stops one backstab).</summary>
    [Fact]
    public void TheGuardedSniperGlancesBehindLessOften()
    {
        long Glances(string archetype)
        {
            TFField f = Field(archetype);
            Walked(f, (int)(120f / SecondsOf(1)));
            return Counter(f, "sniper.behind_glances");
        }

        long marksman = Glances(TFSniperClass.Marksman), guarded = Glances(TFSniperClass.Guarded);
        Assert.True(marksman > 0 && guarded < marksman, $"marksman {marksman} guarded {guarded}");
    }
}

/// <summary>
/// The Jarate archetype's throws (the Sniper research's §4.4 B): with the jar's meter full, into a group of two or more
/// enemies in its splash, or onto a burning teammate; never at one lone enemy.
/// </summary>
public sealed class TFSniperJarateTests
{
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId A = new(5, 0x4000_0001), B = new(6, 0x4000_0001), Mate = new(4, 0x4000_0001);

    private static TFField Field(string archetype = TFSniperClass.Jarate) => new(seed: TFSniperPlayTests.SeedOf(archetype), weapons: TFSniperPlayTests.Weapons);

    private static IntentCommand Step(TFField f, int weapon, params ActorFacts[] seen)
    {
        f.Hud = TFSniperPlayTests.Kit(jar: true);
        return f.StepDrawn(TFSniperPlayTests.Me(Mid, weapon), TFField.Ctf(), seen);
    }

    private static bool TakesJar(in IntentCommand i) => i.Action.Kind == ActionKind.SelectWeapon && i.Action.WeaponSlot == EngineBotSelf.ByKind + TFSniperPlayTests.JarKind;

    /// <summary>Two enemies within the splash 800 u off: the jar comes out.</summary>
    [Fact]
    public void AGroupInItsSplashDrawsTheJar()
    {
        TFField f = Field();
        IntentCommand i = Step(f, TFSniperPlayTests.Rifle, TFSniperPlayTests.Drawn(A, Mid + new AiVector(800f, 0f, 0f), TFClass.Soldier),
                               TFSniperPlayTests.Drawn(B, Mid + new AiVector(820f, 80f, 0f), TFClass.HeavyWeapons));
        Assert.True(TakesJar(in i), $"{i.Action.Kind} {i.Action.WeaponSlot}");
    }

    /// <summary>One lone enemy 800 u off is no jar's: the rifle stays out.</summary>
    [Fact]
    public void ALoneEnemyDrawsNoJar()
    {
        TFField f = Field();
        IntentCommand[] decides = [.. Enumerable.Range(0, 10).Select(_ => Step(f, TFSniperPlayTests.Rifle, TFSniperPlayTests.Drawn(A, Mid + new AiVector(800f, 0f, 0f), TFClass.Soldier)))];
        Assert.DoesNotContain(decides, i => TakesJar(in i));
    }

    /// <summary>A burning teammate 400 u off: the jar comes out (it puts him out).</summary>
    [Fact]
    public void ABurningTeammateDrawsTheJar()
    {
        TFField f = Field();
        IntentCommand i = Step(f, TFSniperPlayTests.Rifle, TFSniperPlayTests.Drawn(Mate, Mid + new AiVector(400f, 0f, 0f), TFClass.Soldier, TFField.Red, ActorConditions.Burning));
        Assert.True(TakesJar(in i), $"{i.Action.Kind} {i.Action.WeaponSlot}");
    }

    /// <summary>The Marksman carries no jar and throws none at the same group.</summary>
    [Fact]
    public void AMarksmanThrowsNoJar()
    {
        TFField f = Field(TFSniperClass.Marksman);
        f.Hud = TFSniperPlayTests.Kit();
        IntentCommand i = f.StepDrawn(TFSniperPlayTests.Me(Mid), TFField.Ctf(), TFSniperPlayTests.Drawn(A, Mid + new AiVector(800f, 0f, 0f), TFClass.Soldier),
                                      TFSniperPlayTests.Drawn(B, Mid + new AiVector(820f, 80f, 0f), TFClass.HeavyWeapons));
        Assert.False(TakesJar(in i));
    }

    /// <summary>With the jar out it is thrown: a fire, the look at a place on its arc (not at an actor), within a few decides. Where it lands is AJarComesDownInsideItsSplash's.</summary>
    [Fact]
    public void TheJarOutIsThrownUpItsArc()
    {
        TFField f = Field();
        ActorFacts a = TFSniperPlayTests.Drawn(A, Mid + new AiVector(800f, 0f, 0f), TFClass.Soldier), b = TFSniperPlayTests.Drawn(B, Mid + new AiVector(820f, 80f, 0f), TFClass.HeavyWeapons);
        Assert.True(TakesJar(Step(f, TFSniperPlayTests.Rifle, a, b)));
        IntentCommand[] after = [.. Enumerable.Range(0, 8).Select(_ => Step(f, TFSniperPlayTests.JarKind, a, b))];
        IntentCommand thrown = after.First(i => i.Action.Kind == ActionKind.Fire);
        Assert.Equal(LookKind.Point, thrown.Look.Kind);
    }
}
