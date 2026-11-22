using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Horde.Boss;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The arena's named entities as the boss fight drives them (the ARNA contract of 2026-11-16) over
/// the boss rig's fake map (<see cref="FakeArenaEntities"/>): the leg-1 mouths when the boss is found,
/// a phase's platforms and mouths when it is entered, a station's pillar and rubble when the Tank
/// has drilled it, the exit brush when the boss dies; each name once a level, a name the map does
/// not hold said once and no error, and a level's end forgetting it all.
/// </summary>
public sealed class ArenaStageTests
{
    private static AiVector InArena => Centre(BossRig.ArenaRoom) + At(-150, 0);

    private static BossRig Rig(string boss = "sir_nukesalot")
    {
        var rig = new BossRig(boss);
        rig.ArenaEntities.Full();
        return rig;
    }

    /// <summary>The boss lent, a hero in its arena, and the fight begun.</summary>
    private static BossRig Engaged(string boss = "sir_nukesalot")
    {
        var rig = Rig(boss);
        rig.Lend();
        rig.Hero(InArena);
        rig.Tick(BossRig.Seconds(1));
        Assert.True(rig.Encounter.Engaged);
        return rig;
    }

    [Fact]
    public void The_leg_1_mouths_open_when_the_level_s_boss_is_first_found()
    {
        var rig = Rig();

        rig.Tick(BossRig.Seconds(1));

        Assert.Equal([(ArenaStage.MouthName(1), ArenaStage.Disable)], rig.ArenaEntities.Inputs);
        Assert.Equal(rig.Row, rig.Encounter.Row);
    }

    [Fact]
    public void Entering_a_phase_disables_its_platforms_and_opens_its_mouths()
    {
        var rig = Engaged();

        rig.LifeTo(0.6);   // under 66 %: phase 2
        rig.Tick(BossRig.Seconds(1));

        Assert.Contains((ArenaStage.PlatformName(2), ArenaStage.Disable), rig.ArenaEntities.Inputs);
        Assert.Contains((ArenaStage.MouthName(2), ArenaStage.Disable), rig.ArenaEntities.Inputs);
        Assert.DoesNotContain(rig.ArenaEntities.Inputs, i => i.Name == ArenaStage.PlatformName(3));
        Assert.Equal(2, rig.Arena.Phase);
    }

    [Fact]
    public void A_fall_through_two_phases_enters_both()
    {
        var rig = Engaged();

        rig.LifeTo(0.2);   // under 33 %: phase 3, past phase 2
        rig.Tick(BossRig.Seconds(1));

        Assert.Equal([ArenaStage.PlatformName(2), ArenaStage.PlatformName(3)],
            rig.ArenaEntities.Inputs.Where(i => i.Name.StartsWith("descent_boss_platform_", StringComparison.Ordinal)).Select(i => i.Name));
        Assert.Equal(3, rig.Arena.Phase);
    }

    [Fact]
    public void A_drilled_station_brings_its_pillar_down_and_its_rubble_up()
    {
        var rig = Rig("major_crits");
        var body = rig.Lend();

        rig.Ask(body, HordeAbilities.TankDrilled, 2);

        Assert.Equal([(ArenaStage.PillarName(2), ArenaStage.Disable), (ArenaStage.RubbleName(2), ArenaStage.Enable)], rig.ArenaEntities.Inputs);
    }

    [Fact]
    public void The_horde_s_paths_are_told_each_input_the_stage_fires_by_name()
    {
        var rig = Rig("major_crits");
        var body = rig.Lend();

        rig.Ask(body, HordeAbilities.TankDrilled, 2);
        rig.Arena.Fallen();

        Assert.Equal([(ArenaStage.PillarName(2), false), (ArenaStage.RubbleName(2), true), (ArenaStage.ExitName, false)],
                     rig.ArenaObstacles.Changes.Where(c => !c.Name.StartsWith("descent_boss_mouth_", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_falling_pillar_raises_its_dust_and_crashes_where_it_stands()
    {
        var rig = Rig("major_crits");
        var body = rig.Lend();

        rig.Ask(body, HordeAbilities.TankDrilled, 3);

        var s = TheRules.Content.Bosses.Sounds;
        Assert.Contains(rig.World.Particles, p => p.Name == s.PillarDust && p.At.Equals(new SourceSharp.Abi.Vector(100, 200, 300)));
        Assert.Contains(rig.World.Sounds, x => x.Sound == s.PillarFalls && x.Entity == new SourceSharp.Abi.EntityHandle(0x0002_0040));
    }

    [Fact]
    public void The_roof_brings_the_final_station_s_pillar_down()
    {
        var rig = Rig("major_crits");
        var body = rig.Lend();

        rig.Ask(body, HordeAbilities.TankRoof, 5);

        Assert.Equal([(ArenaStage.PillarName(5), ArenaStage.Disable), (ArenaStage.RubbleName(5), ArenaStage.Enable)], rig.ArenaEntities.Inputs);
    }

    [Fact]
    public void The_tank_reaching_a_station_changes_nothing_until_its_drill_ends()
    {
        var rig = Rig("major_crits");
        var body = rig.Lend();

        rig.Ask(body, HordeAbilities.TankStation, 2);

        Assert.Empty(rig.ArenaEntities.Inputs);
    }

    [Fact]
    public void Each_name_is_fired_once_a_level()
    {
        var rig = Rig();

        rig.Arena.PhaseEntered(2);
        rig.Arena.PhaseEntered(2);
        rig.Arena.StationDrilled(1);
        rig.Arena.StationDrilled(1);
        rig.Arena.Fallen();
        rig.Arena.Fallen();

        Assert.Equal(rig.ArenaEntities.Inputs.Distinct().Count(), rig.ArenaEntities.Inputs.Count);
        Assert.Equal(5, rig.ArenaEntities.Inputs.Count);   // platform 2, mouth 2, pillar 1, rubble 1, exit
    }

    [Fact]
    public void A_name_the_map_does_not_hold_is_said_once_and_is_no_error()
    {
        var rig = new BossRig();   // a map with none of the names

        rig.Arena.Fallen();
        rig.Arena.Fallen();
        rig.Arena.StationDrilled(1);

        Assert.Equal(1, CountOf(rig.Horde.Log.Text, "no descent_boss_exit on this map"));
        Assert.Equal(1, CountOf(rig.Horde.Log.Text, "no descent_boss_pillar_1 on this map"));
        Assert.Equal(3, rig.Arena.Missing);   // exit, pillar 1, rubble 1
        Assert.Empty(rig.World.Particles);
    }

    [Fact]
    public void A_second_load_of_the_level_starts_the_arena_over()
    {
        var rig = Rig();
        rig.Arena.PhaseEntered(3);
        rig.Arena.Fallen();
        int first = rig.ArenaEntities.Inputs.Count;

        rig.Arena.OnLevelShutdown();
        rig.Arena.OnLevelInit("tf2_descent-5-s1");

        Assert.Equal(1, rig.Arena.Phase);
        Assert.Empty(rig.Arena.Fired);
        rig.Arena.PhaseEntered(3);
        rig.Arena.Fallen();
        Assert.Equal(2 * first, rig.ArenaEntities.Inputs.Count);
    }

    [Fact]
    public void The_dev_verbs_fire_what_the_fight_would()
    {
        var rig = Rig();

        Assert.True(rig.Arena.Dev("drill", 4));
        Assert.True(rig.Arena.Dev("phase", 2));
        Assert.True(rig.Arena.Dev("fallen", 0));

        Assert.Equal([(ArenaStage.PillarName(4), ArenaStage.Disable), (ArenaStage.RubbleName(4), ArenaStage.Enable),
                      (ArenaStage.PlatformName(2), ArenaStage.Disable), (ArenaStage.MouthName(2), ArenaStage.Disable),
                      (ArenaStage.ExitName, ArenaStage.Disable)], rig.ArenaEntities.Inputs);
    }

    [Fact]
    public void A_dev_verb_it_does_not_know_fires_nothing()
    {
        var rig = Rig();

        Assert.False(rig.Arena.Dev("explode", 1));

        Assert.Empty(rig.ArenaEntities.Inputs);
    }

    private static int CountOf(string text, string needle)
    {
        int n = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
            n++;
        return n;
    }
}
