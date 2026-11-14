using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules.World;
using TF2.Descent.Server.Hazards;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Hero;
using Xunit;

namespace TF2.Descent.Tests.World;

/// <summary>
/// The level's hazards (HE10, mine plan D8), over a hand-made level: a volume of lava named as a
/// level would name it, a hero standing in it. Each bite goes through the real hero damage layer
/// (<see cref="TF2.Descent.Server.Hero.HeroDamage"/>), so the number a hero loses is the one the
/// game deals: the rules' bite after his resistance to the kind's type.
/// </summary>
public sealed class HazardServiceTests
{
    /// <summary>The hand-made level: its named volumes, a clock, the hero's feet, and the hits it was asked for, through the hero layer.</summary>
    private sealed class World(HeroStatsRig rig) : IHazardWorld
    {
        public double Now { get; set; } = 100;
        public int Depth { get; set; } = 1;
        public List<(string Kind, Vector Min, Vector Max)> Map { get; } = [];
        public Dictionary<EntityHandle, Vector> FeetOf { get; } = new();
        public HashSet<EntityHandle> Dead { get; } = [];
        public List<EntityHandle> Robots { get; } = [];
        public List<(EntityHandle Victim, float Amount, DamageType Bits, float Dealt)> Hits { get; } = [];

        public IReadOnlyList<(string Kind, Vector Min, Vector Max)> MapVolumes(IReadOnlyList<string> kinds) =>
            [.. Map.Where(v => kinds.Contains(v.Kind))];

        public bool IsAlive(EntityHandle player) => !Dead.Contains(player);

        public Vector Feet(EntityHandle player) => FeetOf.GetValueOrDefault(player);

        public void Hurt(EntityHandle victim, float amount, DamageType bits, Vector at) =>
            Hits.Add((victim, amount, bits, rig.Damage.Modify(new DamageContext(victim, EntityHandle.None, amount, (int)bits), amount)));

        public IReadOnlyList<EntityHandle> RobotsIn(Vector min, Vector max) => Robots;
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    private sealed class Bites : IHazardBiteListener
    {
        public List<(int Player, string Kind)> Told { get; } = [];
        public void Bitten(int playerIndex, string kind, double now) => Told.Add((playerIndex, kind));
    }

    private static readonly Vector LavaMin = new(0, 0, -16), LavaMax = new(256, 256, 64);
    private static readonly Vector InLava = new(128, 128, 0), OnStone = new(512, 128, 0);

    private readonly HeroStatsRig _rig;
    private readonly World _world;
    private readonly Bites _bites = new();
    private readonly HazardService _hazards;

    public HazardServiceTests() : this(StatsSheet.Level10) { }

    private HazardServiceTests(StatsSheet sheet)
    {
        _rig = new HeroStatsRig(sheet);
        _world = new World(_rig);
        _world.Map.Add(("lava", LavaMin, LavaMax));
        _hazards = new HazardService(new ModeOn(), _rig.Rig.Heroes, _rig.Rig.Rules, _world,
                                     new HeroRig.ListHooks<IHazardBiteListener>([_bites]), _rig.Rig.Log);
        _hazards.OnLevelReady("hand_made_lava");
        _world.FeetOf[_rig.Player] = InLava;
    }

    private HazardSettings S => _rig.Rig.Rules.Content.Hazards;

    private HazardRow Lava => S.Kind("lava")!;

    private void Think(double at)
    {
        _world.Now = at;
        _hazards.OnThink(0.015f);
    }

    [Fact]
    public void The_levels_named_volume_is_read_at_level_ready()
    {
        var v = Assert.Single(_hazards.Volumes);

        Assert.Equal(("lava", "map"), (v.Kind, v.Source));
        Assert.Equal(LavaMax, v.Max);
    }

    [Fact]
    public void Lava_bites_a_hero_by_the_rules_number_after_his_fire_resistance_and_no_armour()
    {
        var stats = _rig.Hero.Stats!;
        Assert.True(stats.Armour > 0);              // the stimulus: armour a robot's hit would take a share of
        Assert.True(stats.Resist("fire") > 0);      // and a fire resistance (Grit)

        Think(100);

        var hit = Assert.Single(_world.Hits);
        double bite = HazardRules.Bite(S, Lava, 1);
        Assert.Equal(DamageType.Burn, hit.Bits);
        Assert.Equal(bite, hit.Amount, 4);
        Assert.Equal(HazardRules.AfterResist(bite, stats.Resist("fire")), hit.Dealt, 3);
    }

    [Fact]
    public void A_fire_resistance_bonus_cuts_the_bite_by_exactly_its_share()
    {
        Think(100);
        float before = _world.Hits[^1].Dealt;
        double resistBefore = _rig.Hero.Stats!.Resist("fire");

        _rig.Dev.Add(HeroRig.Index, "resist_fire_pct", 40);
        _rig.Hero.Recompute();
        Think(100 + S.Cadence);

        double resistAfter = _rig.Hero.Stats!.Resist("fire");
        Assert.Equal(resistBefore + 0.40, resistAfter, 9);
        Assert.Equal(before * (1 - resistAfter) / (1 - resistBefore), _world.Hits[^1].Dealt, 3);
    }

    [Fact]
    public void A_deeper_level_bites_harder()
    {
        _world.Depth = 13;

        Think(100);

        Assert.Equal(HazardRules.Bite(S, Lava, 13), _world.Hits[^1].Amount, 4);
        Assert.True(_world.Hits[^1].Amount > HazardRules.Bite(S, Lava, 1));
    }

    [Fact]
    public void A_hero_is_bitten_at_once_then_once_a_cadence()
    {
        Think(100);
        Think(100 + S.Cadence * 0.5);
        Think(100 + S.Cadence * 0.99);
        Assert.Single(_world.Hits);

        Think(100 + S.Cadence);
        Think(100 + S.Cadence * 2);

        Assert.Equal(3, _world.Hits.Count);
    }

    [Fact]
    public void Stepping_out_stops_the_bites()
    {
        Think(100);
        _world.FeetOf[_rig.Player] = OnStone;

        Think(100 + S.Cadence);
        Think(100 + S.Cadence * 5);

        Assert.Single(_world.Hits);
    }

    [Fact]
    public void Stepping_out_and_back_in_within_a_cadence_earns_no_extra_bite()
    {
        Think(100);
        _world.FeetOf[_rig.Player] = OnStone;
        Think(100 + S.Cadence * 0.3);
        _world.FeetOf[_rig.Player] = InLava;
        Think(100 + S.Cadence * 0.6);

        Assert.Single(_world.Hits);
    }

    [Fact]
    public void A_downed_hero_takes_no_bite()
    {
        _rig.Hero.ShowDowned(true, 1, 2);

        Think(100);

        Assert.Empty(_world.Hits);
        Assert.NotEqual(0, _rig.Hero.State!.Hazard);   // he still lies in it
    }

    [Fact]
    public void A_dead_hero_takes_no_bite_and_stands_in_nothing()
    {
        _world.Dead.Add(_rig.Player);

        Think(100);

        Assert.Empty(_world.Hits);
        Assert.Equal(0, _rig.Hero.State!.Hazard);
    }

    [Fact]
    public void The_hazard_a_hero_stands_in_is_on_his_networked_state()
    {
        Think(100);
        Assert.Equal(_hazards.KindIndex("lava") + 1, _rig.Hero.State!.Hazard);

        _world.FeetOf[_rig.Player] = OnStone;
        Think(101);

        Assert.Equal(0, _rig.Hero.State.Hazard);
    }

    [Fact]
    public void A_bite_is_told_to_the_listeners()
    {
        Think(100);

        Assert.Equal([(HeroRig.Index, "lava")], _bites.Told);
    }

    [Fact]
    public void A_robot_in_lava_is_bitten_as_fire_at_the_cadence()
    {
        var robot = HeroStatsRig.Robot;
        _world.Robots.Add(robot);

        Think(100);
        Think(100 + S.Cadence * 0.5);
        Think(100 + S.Cadence);

        var robotHits = _world.Hits.Where(h => h.Victim == robot).ToList();
        Assert.Equal(2, robotHits.Count);
        Assert.All(robotHits, h => Assert.Equal(DamageType.Burn, h.Bits));
        Assert.All(robotHits, h => Assert.Equal(HazardRules.Bite(S, Lava, 1), h.Amount, 4));
        Assert.Equal(2, _hazards.RobotBites);
    }

    [Fact]
    public void A_robot_in_slime_is_left_alone()
    {
        _hazards.ClearDev();
        _hazards.OnLevelShutdown();
        _world.Map.Clear();
        _world.Map.Add(("slime", LavaMin, LavaMax));
        _hazards.OnLevelReady("hand_made_slime");
        _world.Robots.Add(HeroStatsRig.Robot);

        Think(100);

        Assert.DoesNotContain(_world.Hits, h => h.Victim == HeroStatsRig.Robot);
        Assert.Single(_world.Hits);   // the hero is bitten
    }

    [Fact]
    public void Slime_bites_a_hero_whole_whatever_his_resistances()
    {
        _hazards.OnLevelShutdown();
        _world.Map.Clear();
        _world.Map.Add(("slime", LavaMin, LavaMax));
        _hazards.OnLevelReady("hand_made_slime");
        _rig.Dev.Add(HeroRig.Index, "resist_all_pct", 50);
        _rig.Hero.Recompute();

        Think(100);

        var hit = Assert.Single(_world.Hits);
        Assert.Equal(DamageType.Poison, hit.Bits);
        Assert.Equal(HazardRules.Bite(S, S.Kind("slime")!, 1), hit.Dealt, 3);
    }

    [Fact]
    public void A_dev_hazard_stands_at_the_feet_it_is_given()
    {
        _hazards.OnLevelShutdown();
        _world.FeetOf[_rig.Player] = OnStone;

        var v = _hazards.Stand("lava", OnStone, 64);
        Think(100);

        Assert.NotNull(v);
        Assert.Equal("dev", v!.Source);
        Assert.Single(_world.Hits);
        Assert.Null(_hazards.Stand("ice", OnStone, 64));
    }

    [Fact]
    public void A_levels_end_forgets_every_volume()
    {
        _hazards.Stand("lava", OnStone, 64);

        _hazards.OnLevelShutdown();
        _world.Map.Clear();
        _hazards.OnLevelReady("tf2_descent-2-s1");
        Think(100);

        Assert.Empty(_hazards.Volumes);
        Assert.Empty(_world.Hits);
    }
}
