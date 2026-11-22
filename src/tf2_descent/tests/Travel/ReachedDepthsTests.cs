using SourceSharp.Abi;

using TF2.Descent.Server;
using TF2.Descent.Server.Town;
using TF2.Descent.Tests.Floor;
using TF2.Descent.Tests.Hero;

using Xunit;

namespace TF2.Descent.Tests.Travel;

/// <summary>
/// A reached depth (plan_tf2_rpg R6, D8; the user's walk of 2026-11-15, unhosted: town, depth 1,
/// depth 2, back to town, and the depth picker showed depth 2 closed): arriving on a level records
/// its depth on the hero's sheet, the record outlives the level change and the return to town,
/// and the town's depth picker offers every depth reached.
/// </summary>
public sealed class ReachedDepthsTests
{
    private sealed class Rig
    {
        public readonly HeroRig Heroes = new();
        public readonly RigLevel Level = new(1, 1);
        public readonly PlayerService Players;
        public readonly ReachedDepths Reached;

        public Rig()
        {
            Players = Heroes.NewPlayers();
            Reached = new ReachedDepths(Level, Heroes.Heroes, Heroes.Sheets, Players, Heroes.Host, Heroes.Log);
        }

        /// <summary>This process changes level to <paramref name="depth"/>'s level (0: the town), and the hero joins it again.</summary>
        public void Arrive(int depth)
        {
            Heroes.LevelShutdown();
            Level.Current = depth > 0 ? new RigLevel(depth, 1).Current : null;
            Reached.OnLevelInit(depth > 0 ? $"tf2_descent-{depth}-s1" : "tf2_descent_town");
            Heroes.Join();
            Reached.OnThink(0.015f);
        }

        public int Sheet => Heroes.Sheets.Of(HeroRig.Index)!.ReachedDepth;

        /// <summary>The town's depth picker as the engine's town world answers it, unhosted.</summary>
        public IReadOnlyList<int> Picker() =>
            new EngineTownWorld(Heroes.Host, Heroes.Keys(), Heroes.Heroes, null!, null!, null!, null!, null!, null!, null!, Players, new NoKills(),
                                null!, null!, Reached, Heroes.Log).Destinations(HeroRig.Index);
    }

    private sealed class NoKills : IBossKills
    {
        public IReadOnlyCollection<int> Depths(string characterId) => [];
        public IReadOnlyCollection<int> DepthsOf(int player) => [];
    }

    [Fact]
    public void Arriving_on_a_level_records_its_depth_on_the_heros_sheet()
    {
        var rig = new Rig();
        rig.Arrive(1);
        int first = rig.Sheet;

        rig.Arrive(2);

        Assert.Equal((1, 2), (first, rig.Sheet));
    }

    [Fact]
    public void The_record_outlives_the_level_change_and_the_return_to_town()
    {
        var rig = new Rig();
        rig.Arrive(1);
        rig.Arrive(2);

        rig.Arrive(0);

        Assert.Equal((2, 2), (rig.Sheet, rig.Reached.Of(HeroRig.Index)));
    }

    [Fact]
    public void A_shallower_level_never_lowers_the_record()
    {
        var rig = new Rig();
        rig.Arrive(3);

        rig.Arrive(1);

        Assert.Equal(3, rig.Sheet);
    }

    [Fact]
    public void The_town_records_no_depth()
    {
        var rig = new Rig();

        rig.Arrive(0);

        Assert.Equal((0, 0L), (rig.Sheet, rig.Reached.Recorded));
    }

    [Fact]
    public void A_hero_who_joins_a_level_already_running_is_recorded()
    {
        var rig = new Rig();
        rig.Level.Current = new RigLevel(2, 1).Current;
        rig.Reached.OnLevelInit("tf2_descent-2-s1");
        rig.Reached.OnThink(0.015f);
        long before = rig.Reached.Recorded;

        rig.Heroes.Join();
        rig.Reached.OnThink(0.015f);

        Assert.Equal((0L, 2), (before, rig.Sheet));
    }

    [Fact]
    public void The_towns_depth_picker_offers_every_depth_reached_after_the_walk_back_up()
    {
        var rig = new Rig();
        rig.Arrive(0);
        var before = rig.Picker();
        rig.Arrive(1);
        rig.Arrive(2);
        rig.Arrive(1);

        rig.Arrive(0);

        Assert.Equal(("1", "1,2"), (string.Join(',', before), string.Join(',', rig.Picker())));
    }
}
