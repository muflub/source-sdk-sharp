using System.Text.Json;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using TF2.Descent.Rules.World;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Hazard volumes in the rooms (brief p9, an ask of the game-logic session, plan_descent_logic M1):
/// a room declares a volume of a kind of <c>hazards.json</c>, and it is a brush entity named
/// <c>tf2_descent_hazard_&lt;kind&gt;</c>, the name the game's <c>HazardService</c> finds a level's
/// volumes by. Levels 1 to 9 hold none (plan §7); level 12's grey pools hold slime, the proof.
/// </summary>
public sealed class DescentHazardTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static Room Pit(string kind) =>
        new RoomBuilder("hazard_a", Kit, RoomTheme.Grey).WithHeight(512).WithDoors(CellFace.West)
            .WithHazard(kind, new Point(400, 300, 16), new Point(700, 700, 80)).Build();

    private static IEnumerable<RoomEntity> Volumes(Room room) =>
        room.Entities.Where(e => e.Key("targetname") is { } n && n.StartsWith(HazardRules.VolumePrefix, StringComparison.Ordinal));

    /// <summary>The kinds <c>hazards.json</c> lists.</summary>
    private static IReadOnlyList<string> Kinds()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoLayout.Repo(["src", "tf2_descent", "rules", "Content", "hazards.json"])));
        return [.. doc.RootElement.GetProperty("kinds").EnumerateArray().Select(k => k.GetProperty("id").GetString()!)];
    }

    [Fact]
    public void A_hazard_volume_is_a_trigger_named_for_its_kind_that_nothing_fires()
    {
        RoomEntity volume = Assert.Single(Volumes(Pit("slime")));

        Assert.Equal("trigger_multiple", volume.ClassName);
        Assert.Equal("tf2_descent_hazard_slime", volume.Key("targetname"));
        Assert.Equal("0", volume.Key("spawnflags"));
        Assert.Empty(volume.Wires);
        Brush box = Assert.Single(volume.Brushes);
        Assert.Equal((new Point(400, 300, 16), new Point(700, 700, 80)), (box.Mins, box.Maxs));
        Assert.Equal(RoomTheme.Grey.Trigger.Material, box.Material);
    }

    [Fact]
    public void The_game_reads_the_volumes_name_back_as_its_kind()
    {
        Assert.Equal("lava", HazardRules.KindOfVolume(RoomBuilder.HazardName("lava")));
    }

    [Fact]
    public void A_hazard_kind_that_is_no_id_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Pit("Toxic Slime"));
    }

    [Fact]
    public void Level_twelves_pools_hold_slime_a_kind_of_hazards_json()
    {
        var pools = libs.Built(12).Where(r => r.Name.StartsWith("pool_", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(pools);
        Assert.Contains("slime", Kinds());
        Assert.All(pools, p => Assert.Equal("tf2_descent_hazard_slime", Assert.Single(Volumes(p)).Key("targetname")));
        Assert.All(pools, p => Assert.Empty(RoomLint.Check(p)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void A_level_above_the_sump_holds_no_hazard(int depth)
    {
        Assert.Empty(libs.Built(depth).SelectMany(Volumes));
    }
}
