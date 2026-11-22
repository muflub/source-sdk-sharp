using System.Collections.Immutable;
using SourceSharp.Abi;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Server.Loot;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// A boss's death (plan_descent_logic HO13; mine plan D4, D6): the kill is recorded on each paid
/// hero's sheet once and only once, a kill that opens a camp says so, the camps read the sheet
/// back, every hero on the level is told the way down is open, and a robot that is no boss does
/// none of it.
/// </summary>
public sealed class BossKillTests
{
    private sealed class Party(params int[] players) : ILootParty
    {
        public IReadOnlyList<LootMember> Near(Vector at, int killer) => [.. players.Select(p => new LootMember(p, "hero-" + p, at))];
        public string? HeroOf(int player) => "hero-" + player;
        public Vector? Origin(int player) => default(Vector);
        public IReadOnlyList<string> PileMembers(string killer, string claimer) => [.. players.Select(p => "hero-" + p)];
    }

    private sealed class Fixture
    {
        public Fixture(params int[] paid)
        {
            Boss = new BossRig();
            Fallen = new BossFallen(new RigRules(), Boss.World, new Party(paid), Hero.Sheets, Boss.Horde.Heroes, new RigHordeCurrent(Boss.Horde), Boss.Arena, Boss.Horde.Log);
        }

        public BossRig Boss { get; }
        public HeroRig Hero { get; } = new();
        public BossFallen Fallen { get; }
        public FakeBossWorld World => Boss.World;
    }

    private static RobotKill Kill(string template, bool boss, uint sequence = 0, int killer = HeroRig.Index)
    {
        var t = TheRules.Robots.Template(template);
        var stats = TheRules.Robots.Stats(t, t.ScaleMin, 9, []);
        return new RobotKill(sequence, new RobotId(0, 1), template, 9, t.ScaleMin, ImmutableArray<string>.Empty, boss, stats, default, killer, EntityHandle.None);
    }

    [Fact]
    public void A_boss_kill_is_recorded_on_each_paid_hero_s_sheet_and_tells_him_the_camp_it_opens()
    {
        var f = new Fixture(HeroRig.Index, 5);

        f.Fallen.OnRobotKilled(Kill("sir_nukesalot", boss: true));

        Assert.Equal(["sir_nukesalot"], f.Hero.Sheets.Of(HeroRig.Index)!.BossKills);
        Assert.Equal(["sir_nukesalot"], f.Hero.Sheets.Of(5)!.BossKills);
        var camps = f.World.Notices.Where(n => n.Token == BossFallen.CampToken).ToList();
        Assert.Equal([(HeroRig.Index, "6"), (5, "6")], camps.Select(n => (n.Player, n.Arg)));
        Assert.Equal(2, f.Fallen.Records);
    }

    [Fact]
    public void A_second_kill_of_the_same_boss_records_nothing_new()
    {
        var f = new Fixture(HeroRig.Index);
        f.Fallen.OnRobotKilled(Kill("sir_nukesalot", boss: true));
        var once = f.Hero.Sheets.Of(HeroRig.Index)!;
        int notices = f.World.Notices.Count(n => n.Token == BossFallen.CampToken);

        f.Fallen.OnRobotKilled(Kill("sir_nukesalot", boss: true, sequence: 1));

        Assert.Same(once, f.Hero.Sheets.Of(HeroRig.Index));
        Assert.Equal(notices, f.World.Notices.Count(n => n.Token == BossFallen.CampToken));
        Assert.Equal((1, 1), (f.Fallen.Records, f.Fallen.AlreadyHeld));
    }

    [Fact]
    public void The_last_boss_s_kill_is_recorded_and_opens_no_camp()
    {
        var f = new Fixture(HeroRig.Index);

        f.Fallen.OnRobotKilled(Kill("captain_punch", boss: true));

        Assert.Equal(["captain_punch"], f.Hero.Sheets.Of(HeroRig.Index)!.BossKills);
        Assert.DoesNotContain(f.World.Notices, n => n.Token == BossFallen.CampToken);
    }

    [Fact]
    public void A_robot_that_is_no_boss_records_nothing_and_opens_nothing()
    {
        var f = new Fixture(HeroRig.Index);

        f.Fallen.OnRobotKilled(Kill("giant_demoman", boss: false));

        Assert.Empty(f.Hero.Sheets.Of(HeroRig.Index)!.BossKills);
        Assert.Empty(f.World.Notices);
        Assert.Equal(0, f.Fallen.Kills);
    }

    [Fact]
    public void Every_hero_on_the_level_is_told_the_way_down_is_open()
    {
        var f = new Fixture();
        f.Boss.Hero(Centre(BossRig.ArenaRoom));
        f.Boss.Hero(Centre(0));

        f.Fallen.OnRobotKilled(Kill("sir_nukesalot", boss: true));

        Assert.Equal([1, 2], f.World.Notices.Where(n => n.Token == BossFallen.OpenToken).Select(n => n.Player));
        Assert.Equal(2, f.World.Sounds.Count(s => s.Sound == TheRules.Content.Bosses.Sounds.ExitOpens));
    }

    [Fact]
    public void The_tank_s_death_leaves_its_wreck_and_records_nothing()
    {
        var f = new Fixture(HeroRig.Index);

        f.Fallen.OnRobotKilled(Kill("tank", boss: false));

        Assert.Contains(f.World.Particles, p => p.Name == TheRules.Content.Bosses.Sounds.TankDeathParticle);
        Assert.Equal(1, f.Fallen.Tanks);
        Assert.Empty(f.Hero.Sheets.Of(HeroRig.Index)!.BossKills);
    }

    [Fact]
    public void A_boss_s_death_takes_the_brush_off_the_way_down()
    {
        var f = new Fixture();
        f.Boss.ArenaEntities.Full();

        f.Fallen.OnRobotKilled(Kill("sir_nukesalot", boss: true));

        Assert.Equal([(ArenaStage.ExitName, ArenaStage.Disable)], f.Boss.ArenaEntities.Inputs);
        Assert.Contains("[TF2Descent/Boss] arena: Disable descent_boss_exit (1 entity): the boss is dead", f.Boss.Horde.Log.Text);
    }

    [Fact]
    public void The_tank_s_death_leaves_the_way_down_s_brush_standing()
    {
        var f = new Fixture();
        f.Boss.ArenaEntities.Full();

        f.Fallen.OnRobotKilled(Kill("tank", boss: false));

        Assert.Empty(f.Boss.ArenaEntities.Inputs);
        Assert.Equal(1, f.Fallen.Tanks);   // the stimulus: the Tank's death was taken
    }

    [Fact]
    public void A_level_with_no_exit_brush_still_opens_its_way_down()
    {
        var f = new Fixture();   // the map holds none of the arena's names
        f.Boss.Roster.Hurt(f.Boss.Row, f.Boss.Roster.Life(f.Boss.Row));

        f.Fallen.OnRobotKilled(Kill("sir_nukesalot", boss: true));

        Assert.True(f.Boss.Exit.MayLeave(1));
        Assert.Equal(1, f.Fallen.Opened);
        Assert.Contains("no descent_boss_exit on this map", f.Boss.Horde.Log.Text);
    }

    [Fact]
    public void The_camps_read_the_boss_depths_a_hero_s_sheet_holds()
    {
        var f = new Fixture(HeroRig.Index);
        var kills = new SheetBossKills(f.Hero.NewPlayers(), f.Hero.Sheets, new RigRules());
        Assert.Empty(kills.DepthsOf(HeroRig.Index));

        f.Fallen.OnRobotKilled(Kill("sir_nukesalot", boss: true));

        Assert.Equal([5], kills.DepthsOf(HeroRig.Index));
        Assert.Contains(TownDescents.FirstCamp, TownDescents.Destinations(0, kills.DepthsOf(HeroRig.Index)));
        Assert.Empty(kills.Depths("no-such-character"));
    }

    [Fact]
    public void The_kill_chain_records_the_camp_after_the_experience_and_the_loot()
    {
        Assert.True(BossFallen.Order > LootRewards.Order);
        Assert.True(LootRewards.Order > HeroExperience.Order);
    }
}
