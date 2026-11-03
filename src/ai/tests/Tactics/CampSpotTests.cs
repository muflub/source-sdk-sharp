using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// Where a camper goes (plan_hl2mp_ai §5.5, user request 2026-11-02: "camping should
/// prefer item pickups like health good weapons or choke points"): a pickup worth having
/// or a choke, what it needs first, on the graph with a line to it, facing it, and not
/// where another camper is.
/// </summary>
public sealed class CampSpotTests
{
    private static readonly string[] Floor = [.. Enumerable.Repeat(new string('.', 80), 80)];

    private static SkillProfile Camper(params (string Skill, int Level)[] more)
    {
        SkillProfile p = Duel.Preset("normal");
        p = p.With(p.IndexOf("cover"), 10);
        p = p.With(p.IndexOf("cunning"), 10);
        foreach ((string skill, int level) in more)
            p = p.With(p.IndexOf(skill), level);
        return p;
    }

    /// <summary>A sight table over a floor: everyone sees everyone unless the fact says otherwise, and nobody is exposed.</summary>
    private sealed class Sees(Func<int, int, bool> visible, Func<int, float>? exposure = null) : ICellSight
    {
        public float Range => 8192f;

        public bool Visible(int a, int b) => visible(a, b);

        public float Exposure(int cell) => exposure?.Invoke(cell) ?? 0f;
    }

    private static readonly Sees Open = new((_, _) => true);

    /// <summary>Steps a bot until it goes to hold somewhere, and answers where it stands and what it watches.</summary>
    private static (AiVector Spot, IntentCommand Intent) GoesToHold(Duel d, AiVector at, int health = 100, int steps = 100)
    {
        IntentCommand i = default;
        for (int k = 0; k < steps; k++)
        {
            i = d.Step(d.Self(health: health) with { Origin = at + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
            if (d.Decider.Doing.Branch == "Ambush" && i.Move.Kind == MoveKind.Follow)
                return (i.Move.Corner0, i);
        }

        Assert.Fail($"it never went to hold a place: {d.Decider.Doing.Branch}");
        return default;
    }

    private static AiVector Near(NavGraph g, int[,] ids, int x, int y) => g.Cell(Grids.At(ids, x, y)).Centre;

    private static ItemSpawn Item(int index, string name, ItemKind kind, AiVector at) => new(index, name, kind, at, 30f);

    /// <summary>What it faces at the spot is what it holds: the look is on the place, within its sway.</summary>
    private static float LookOff(IntentCommand i, AiVector from, AiVector place)
    {
        AiVector look = (i.Look.Point - from) with { Z = 0f }, to = (place - from) with { Z = 0f };
        return MathF.Acos(Math.Clamp(look.Normalised().Dot(to.Normalised()), -1f, 1f)) * (180f / MathF.PI);
    }

    /// <summary>Nothing holds a place but a pickup worth having: health over ammo at one distance, whichever comes first in the map.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void HealthIsWorthMoreThanAmmo(int healthAt, int ammoAt)
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector[] places = [Near(g, ids, 20, 40), Near(g, ids, 60, 40)];
        var items = new ItemSpawn[2];
        items[healthAt] = Item(healthAt, "item_healthkit", ItemKind.Health, places[0]);
        items[ammoAt] = Item(ammoAt, "item_ammo_smg1", ItemKind.Ammo, places[1]);
        var d = new Duel(Camper(), Duel.Bot, 7, g, Open, null, items);
        (AiVector spot, _) = GoesToHold(d, Near(g, ids, 40, 40));
        Assert.True(spot.DistanceTo(places[0]) < spot.DistanceTo(places[1]), $"it holds from {spot}");
    }

    /// <summary>The good guns are worth more than the poor ones, by the weapons' own ranks: whatever its whim, a bot takes the RPG's place.</summary>
    [Fact]
    public void AGoodGunIsWorthMoreThanAPoorOne()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector rpg = Near(g, ids, 20, 40), pistol = Near(g, ids, 60, 40);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var d = new Duel(Camper(), Duel.Bot, seed, g, Open, null, Item(0, "weapon_pistol", ItemKind.Weapon, pistol), Item(1, "weapon_rpg", ItemKind.Weapon, rpg));
            GoesToHold(d, Near(g, ids, 40, 40));
            Assert.Equal(rpg, d.Decider.HoldingWatch);
        }
    }

    /// <summary>Hurt it prefers health to a battery, at full health the reverse is no more than even (a seed could go either way, so several).</summary>
    [Fact]
    public void AHurtBotPrefersHealth()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector kit = Near(g, ids, 20, 40), battery = Near(g, ids, 60, 40);
        int atKit(int health)
        {
            int n = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var d = new Duel(Camper(), Duel.Bot, seed, g, Open, null, Item(0, "item_battery", ItemKind.Armour, battery), Item(1, "item_healthkit", ItemKind.Health, kit));
                (AiVector spot, _) = GoesToHold(d, Near(g, ids, 40, 40), health);
                n += spot.DistanceTo(kit) < spot.DistanceTo(battery) ? 1 : 0;
            }

            return n;
        }

        int whole = atKit(100), hurt = atKit(45);
        Assert.True(hurt == 20 && hurt > whole - 1, $"at the kit: hurt {hurt}, whole {whole} of 20");
    }

    /// <summary>A bot with only a pistol wants a gun: over seeds it takes the SMG's place far more than one who has an SMG, for whom it is about a battery's worth.</summary>
    [Fact]
    public void AWeaklyArmedBotPrefersAGun()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector gun = Near(g, ids, 20, 40), battery = Near(g, ids, 60, 40), from = Near(g, ids, 40, 40);
        int AtGun(int weapon, int clip)
        {
            int n = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var d = new Duel(Camper(), Duel.Bot, seed, g, Open, null, Item(0, "item_battery", ItemKind.Armour, battery), Item(1, "weapon_smg1", ItemKind.Weapon, gun));
                for (int k = 0; k < 100 && d.Decider.Doing.Branch != "Ambush"; k++)
                    d.Step(d.Self(weapon: weapon, clip: clip) with { Origin = from + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
                Assert.Equal("Ambush", d.Decider.Doing.Branch);
                n += d.Decider.HoldingWatch.DistanceTo(gun) < 1f ? 1 : 0;
            }

            return n;
        }

        int weak = AtGun(Duel.Pistol, 18), strong = AtGun(Duel.Smg1, 45);
        Assert.True(weak >= strong + 3, $"of 20 at the gun: pistol in hand {weak}, SMG in hand {strong}");
    }

    /// <summary>A choke it knows of is a place to hold, and it is told when: before that the map's chokes are not read.</summary>
    [Fact]
    public void AChokeIsAPlaceOnceItIsKnown()
    {
        string[] bridged =
        [
            .. Enumerable.Repeat(new string('.', 30) + "     " + new string('.', 30), 14),
            new string('.', 65),
            .. Enumerable.Repeat(new string('.', 30) + "     " + new string('.', 30), 14),
        ];
        var (g, ids) = Grids.Parse(bridged);
        ChokeMap chokes = ChokeMap.Compute(g);
        AiVector bridge = Near(g, ids, 32, 14);
        AiVector from = Near(g, ids, 20, 14);
        var d = new Duel(Camper(), new ActorId(0, 1), 7, g, Open, chokes);
        // No items, no chokes known: nothing to hold.
        for (int k = 0; k < 40; k++)
            d.Step(d.Self() with { Origin = from + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
        Assert.NotEqual("Ambush", d.Decider.Doing.Branch);
        // Told, it goes to watch the bridge.
        d.Step(d.Self() with { Origin = from }, AiEventsNone, AiEvent.ChokesKnown(new ActorId(0, 1), new Tick(d.Tick + 6)));
        IntentCommand i = default;
        bool held = false;
        for (int k = 0; k < 4000 && !held; k++)
        {
            i = d.Step(d.Self() with { Origin = from + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
            held = d.Decider.Doing.Branch == "Ambush" && i.Move.Kind == MoveKind.Follow;
        }

        Assert.True(held, "it never went to hold the bridge");
        Assert.True(i.Move.Corner0.DistanceTo(bridge) <= 1.5f * Hl2mpChokeRange, $"it holds from {i.Move.Corner0}, the bridge at {bridge}");
    }

    private const float Hl2mpChokeRange = 512f;

    private static readonly (ActorId, AiVector)[] AiEventsNone = [];

    /// <summary>The spot sees what it holds: only cells left of the wall see the item, and the bot comes from the right.</summary>
    [Fact]
    public void ACampSpotSeesItsItem()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector item = Near(g, ids, 40, 40);
        float wall = item.X + 100f;
        int itemCell = Grids.At(ids, 40, 40).Index;
        var sight = new Sees((a, b) => (a == itemCell || b == itemCell) && g.Cell(a == itemCell ? b : a).Centre.X < wall);
        var d = new Duel(Camper(), Duel.Bot, 7, g, sight, null, Item(0, "item_healthkit", ItemKind.Health, item));
        (AiVector spot, _) = GoesToHold(d, item + new AiVector(600f, 0f, 0f));
        Assert.True(spot.X < wall, $"it would hold from {spot}, which cannot see {item}");
        Assert.True(spot.DistanceTo(item) >= Hl2mpDeciderNearest, $"it stands on the item: {spot}");
    }

    private const float Hl2mpDeciderNearest = SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider.CampNearest;

    /// <summary>The only cells that see the item are right beside it: it will not stand on it to hold it.</summary>
    [Fact]
    public void ItWillNotStandOnTheItem()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector item = Near(g, ids, 40, 40);
        var sight = new Sees((a, b) => g.Cell(a).Centre.DistanceTo(g.Cell(b).Centre) < SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider.CampNearest);
        var d = new Duel(Camper(), Duel.Bot, 7, g, sight, null, Item(0, "item_healthkit", ItemKind.Health, item));
        for (int k = 0; k < 40; k++)
            d.Step(d.Self() with { Origin = item + new AiVector(400f + k % 2 * 40f, 0f, 0f) }, []);
        Assert.NotEqual("Ambush", d.Decider.Doing.Branch);
    }

    /// <summary>With no cell that sees it, a place is no place to hold: the control is the fact above.</summary>
    [Fact]
    public void APlaceNothingSeesIsNotHeld()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector item = Near(g, ids, 40, 40);
        var d = new Duel(Camper(), Duel.Bot, 7, g, new Sees((_, _) => false), null, Item(0, "item_healthkit", ItemKind.Health, item));
        for (int k = 0; k < 40; k++)
            d.Step(d.Self() with { Origin = item + new AiVector(400f + k % 2 * 40f, 0f, 0f) }, []);
        Assert.NotEqual("Ambush", d.Decider.Doing.Branch);
    }

    /// <summary>The range suits the weapon: a shotgun's holder stands nearer than an SMG's, and neither on the place.</summary>
    [Fact]
    public void TheRangeSuitsTheWeapon()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector item = Near(g, ids, 40, 40);
        var smg = new Duel(Camper(), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, item));
        (AiVector far, _) = GoesToHold(smg, item + new AiVector(900f, 0f, 0f));
        Assert.InRange(far.DistanceTo(item), 300f, 720f);
        var shotgun = new Duel(Camper(), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, item));
        AiVector from = item + new AiVector(900f, 0f, 0f);
        IntentCommand i = default;
        for (int k = 0; k < 100 && !(shotgun.Decider.Doing.Branch == "Ambush" && i.Move.Kind == MoveKind.Follow); k++)
            i = shotgun.Step(shotgun.Self(weapon: Duel.Shotgun, clip: 6) with { Origin = from + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
        Assert.Equal("Ambush", shotgun.Decider.Doing.Branch);
        Assert.InRange(i.Move.Corner0.DistanceTo(item), SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider.CampNearest, 290f);
    }

    /// <summary>In cover if it can: of two sides that see the item at one range, it takes the one fewer places see.</summary>
    [Fact]
    public void ItPrefersTheLessExposedSide()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector item = Near(g, ids, 40, 40);
        var sight = new Sees((_, _) => true, cell => g.Cell(cell).Centre.Y > item.Y ? 1f : 0f);
        var d = new Duel(Camper(), Duel.Bot, 7, g, sight, null, Item(0, "item_healthkit", ItemKind.Health, item));
        (AiVector spot, _) = GoesToHold(d, item + new AiVector(500f, 0f, 0f));
        Assert.True(spot.Y <= item.Y, $"it holds from {spot}, on the exposed side of {item}");
    }

    /// <summary>At the spot it faces what it holds, within its sway, and does not jiggle.</summary>
    [Fact]
    public void ItFacesThePlaceItHolds()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector item = Near(g, ids, 40, 40);
        var d = new Duel(Camper(), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, item));
        (AiVector spot, _) = GoesToHold(d, item + new AiVector(500f, 0f, 0f));
        for (int k = 0; k < 50; k++)
        {
            IntentCommand i = d.Step(d.Self() with { Origin = spot }, []);
            Assert.Equal("Ambush", d.Decider.Doing.Branch);
            Assert.Equal(MoveKind.Hold, i.Move.Kind);
            Assert.Equal(LookKind.Watch, i.Look.Kind);
            Assert.True(LookOff(i, spot, item) <= SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider.WatchSwayDeg + 1f, $"{LookOff(i, spot, item)} degrees off");
        }
    }

    /// <summary>It moves on when its time is up: from a near place to a far one, though the near one is worth more.</summary>
    [Fact]
    public void ItMovesOnAndDoesNotGoStraightBack()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector near = Near(g, ids, 40, 40), far = Near(g, ids, 70, 40);
        var d = new Duel(Camper(), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, near), Item(1, "item_healthkit", ItemKind.Health, far));
        (AiVector first, _) = GoesToHold(d, near + new AiVector(-100f, 0f, 0f));
        Assert.Equal(near, d.Decider.HoldingWatch);
        bool moved = false;
        for (int k = 0; k < 2000 && !moved; k++)
        {
            IntentCommand i = d.Step(d.Self() with { Origin = first }, []);
            moved = i.Move.Kind == MoveKind.Follow && i.Move.Corner0.DistanceTo(first) > 64f && d.Decider.Doing.Branch == "Ambush";
        }

        Assert.True(moved, "it never moved on");
        Assert.Equal(far, d.Decider.HoldingWatch);
    }

    /// <summary>Two campers with everything else alike, of any two slots a share apart: over many seeds and places they never hold the same place at one time.</summary>
    [Fact]
    public void TwoCampersDoNotShareAPlace()
    {
        var (g, ids) = Grids.Parse(Floor);
        var items = new ItemSpawn[16];
        for (int i = 0; i < items.Length; i++)
            items[i] = Item(i, "item_healthkit", ItemKind.Health, Near(g, ids, 8 + (i % 4) * 20, 8 + (i / 4) * 20));
        AiVector from = Near(g, ids, 40, 40);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var a = new Duel(Camper(), new ActorId(2, 1), seed, g, Open, null, items);
            var b = new Duel(Camper(), new ActorId(3, 1), seed, g, Open, null, items);
            GoesToHold(a, from);
            GoesToHold(b, from);
            Assert.True(a.Decider.HoldingWatch.DistanceTo(b.Decider.HoldingWatch) > 1f, $"seed {seed}: both hold {a.Decider.HoldingWatch}");
        }
    }

    /// <summary>A place someone was just seen at is taken, whatever it is worth: the control goes for it.</summary>
    [Fact]
    public void ASpotSomeoneWasSeenAtIsTaken()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector kit = Near(g, ids, 20, 40), other = Near(g, ids, 60, 40), origin = Near(g, ids, 40, 40);
        Duel New() => new(Camper(("aggression", 0)), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, kit), Item(1, "item_ammo_smg1", ItemKind.Ammo, other));
        (AiVector free, _) = GoesToHold(New(), origin);
        Assert.True(free.DistanceTo(kit) < free.DistanceTo(other), $"the control holds from {free}");

        Duel seen = New();
        seen.Step(seen.Self() with { Origin = origin }, [(Duel.Opponent, kit)], seen.SeeAt(Duel.Opponent, kit));
        seen.Step(seen.Self() with { Origin = origin }, [], AiEvent.SightLost(Duel.Bot, new Tick(seen.Tick + 6), Duel.Opponent, kit, default));
        (AiVector spot, _) = GoesToHold(seen, origin, steps: 200);
        Assert.True(spot.DistanceTo(other) < spot.DistanceTo(kit), $"it holds from {spot}, with someone at the kit");
    }

    /// <summary>A place in a part of the map it cannot walk to is no place: the near item is on an island.</summary>
    [Fact]
    public void AnIslandIsNotHeld()
    {
        string[] rows = [.. Enumerable.Repeat(new string('.', 30) + "   " + new string('.', 30), 30)];
        var (g, ids) = Grids.Parse(rows);
        AiVector across = Near(g, ids, 40, 15), here = Near(g, ids, 5, 15);
        var d = new Duel(Camper(), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, across));
        for (int k = 0; k < 40; k++)
            d.Step(d.Self() with { Origin = here + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
        Assert.NotEqual("Ambush", d.Decider.Doing.Branch);
    }

    /// <summary>Knowing someone's corner does not make a camper hunt: the corner's worth is no less than a plain camp's.</summary>
    [Fact]
    public void ACamperWhoKnowsACornerStillHolds()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector corner = Near(g, ids, 40, 40), from = corner + new AiVector(400f, 0f, 0f), them = corner + new AiVector(0f, 300f, 0f);
        var d = new Duel(Camper(), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, corner));
        d.Memory.HabitsOf(Duel.Opponent).Camped(g.FindCell(corner + new AiVector(0f, 0f, 1f)));
        d.Step(d.Self() with { Origin = from }, [(Duel.Opponent, them)], d.SeeAt(Duel.Opponent, them));
        d.Step(d.Self() with { Origin = from }, [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, them, default));
        for (int k = 0; k < 80 && d.Decider.Doing.Branch != "Ambush"; k++)
            d.Step(d.Self() with { Origin = from + new AiVector(k % 2 * 40f, 0f, 0f) }, []);
        Assert.Equal("Ambush", d.Decider.Doing.Branch);
    }

    /// <summary>Steady: planning where to hold, and holding it, allocates nothing a decide.</summary>
    [Fact]
    public void CampingAllocatesNothing()
    {
        var (g, ids) = Grids.Parse(Floor);
        ChokeMap chokes = ChokeMap.Compute(g);
        AiVector item = Near(g, ids, 40, 40);
        var d = new Duel(Camper(), Duel.Bot, 7, g, Open, chokes, Item(0, "item_healthkit", ItemKind.Health, item), Item(1, "weapon_ar2", ItemKind.Weapon, Near(g, ids, 20, 20)));
        d.Step(d.Self(), [], AiEvent.ChokesKnown(Duel.Bot, new Tick(d.Tick + 6)));
        AiVector from = item + new AiVector(400f, 0f, 0f);
        SelfState self = d.Self() with { Origin = from };
        for (int k = 0; k < 40; k++)
            d.Step(self, []);
        (ActorId, AiVector)[] nobody = [];
        AiEvent[] none = [];
        long bytes = SourceSharp.Ai.Tests.Core.SteadyAllocation.Measure(() => d.Step(self, nobody, none), 500);
        Assert.Equal(0, bytes);
    }

    /// <summary>Someone seen close comes first, whatever it holds (the ruling's): it is in the fight the decide it sees them.</summary>
    [Fact]
    public void SomeoneSeenCloseComesFirst()
    {
        var (g, ids) = Grids.Parse(Floor);
        AiVector item = Near(g, ids, 40, 40);
        var d = new Duel(Camper(), Duel.Bot, 7, g, Open, null, Item(0, "item_healthkit", ItemKind.Health, item));
        (AiVector spot, _) = GoesToHold(d, item + new AiVector(500f, 0f, 0f));
        for (int k = 0; k < 10; k++)
            d.Step(d.Self() with { Origin = spot }, []);
        Assert.Equal("Ambush", d.Decider.Doing.Branch);
        AiVector them = spot + new AiVector(200f, 0f, 0f);
        IntentCommand i = d.Step(d.Self() with { Origin = spot }, [(Duel.Opponent, them)], d.SeeAt(Duel.Opponent, them));
        Assert.Equal("Fight", d.Decider.Doing.Branch);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }
}
