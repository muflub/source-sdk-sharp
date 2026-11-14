using SourceSharp.MapGen;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ss_ai_tf_ends's generator (lane Ge, <see cref="TFAiTestMaps.Ends"/>): shaped as jump_academy2_rc9a is, every course's
/// end a capture area naming a point that does not exist, one real point at the hub, locked and named by no area, a
/// cabinet of team 0 at the hub with the spawns in it; each end a single rocket up and no more than a safe drop back. The
/// committed source is the generator's.
/// </summary>
public sealed class TFAiEndsMapTests
{
    /// <summary>TF2's safe drop: PLAYER_MAX_SAFE_FALL_SPEED 650 under gravity 800.</summary>
    private const float SafeDrop = 650f * 650f / (2f * 800f);

    private static string Source(string name) => File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "maps", "src", "ai", name));

    private static List<VmfEntity> Entities(string className) => [.. TFAiTestMaps.Ends().Entities.Where(e => e.ClassName == className)];

    private static string? Key(VmfEntity e, string key) => e.KeyValues.Where(kv => kv.Key == key).Select(kv => kv.Value).SingleOrDefault();

    private static (float X0, float Y0, float Z0, float X1, float Y1, float Z1) Bounds(VmfEntity e)
    {
        var points = e.Solids.SelectMany(s => s.Sides).SelectMany(side => new[] { side.P0, side.P1, side.P2 }).ToList();
        return (points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z), points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
    }

    [Fact]
    public void The_committed_source_is_the_generators()
        => Assert.Equal(TFAiTestMaps.Ends().Write(), Source("ss_ai_tf_ends.vmf"));

    [Fact]
    public void Every_capture_area_is_a_courses_end_naming_an_entity_the_level_does_not_place()
    {
        HashSet<string> names = [.. TFAiTestMaps.Ends().Entities.Select(e => Key(e, "targetname")).OfType<string>()];
        List<VmfEntity> areas = Entities("trigger_capture_area");
        Assert.Equal(TFAiTestMaps.EndsCourses.Select(c => c.Title), areas.Select(a => Key(a, "area_cap_point")));
        Assert.All(areas, a => Assert.DoesNotContain(Key(a, "area_cap_point")!, names));
        foreach ((VmfEntity area, TFAiTestMaps.EndsCourse course) in areas.Zip(TFAiTestMaps.EndsCourses))
            Assert.Equal((course.End.West, course.End.South, course.End.Top), (Bounds(area).X0, Bounds(area).Y0, Bounds(area).Z0));
    }

    [Fact]
    public void The_one_real_point_is_at_the_hub_locked_and_named_by_no_area_under_a_master_no_team_wins()
    {
        VmfEntity point = Assert.Single(Entities("team_control_point"));
        Assert.Equal(("1", null), (Key(point, "point_start_locked"), Key(point, "targetname")));
        Assert.Equal("1", Key(Assert.Single(Entities("team_control_point_master")), "cpm_restrict_team_cap_win"));
    }

    [Fact]
    public void The_spawns_stand_in_the_hubs_cabinet_of_every_team()
    {
        (float x0, float y0, _, float x1, float y1, _) = Bounds(Assert.Single(Entities("func_regenerate"), z => Key(z, "TeamNum") == "0"));
        List<VmfEntity> spawns = Entities("info_player_teamspawn");
        Assert.Equal(8, spawns.Count);
        foreach (VmfEntity spawn in spawns)
        {
            float[] at = [.. Key(spawn, "origin")!.Split(' ').Select(float.Parse)];
            Assert.True(at[0] > x0 && at[0] < x1 && at[1] > y0 && at[1] < y1, $"the spawn at {at[0]} {at[1]} is in the cabinet");
        }
    }

    [Fact]
    public void Each_end_is_over_the_floor_by_more_than_a_jump_and_no_more_than_a_safe_drop()
    {
        foreach (TFAiTestMaps.EndsCourse course in TFAiTestMaps.EndsCourses)
        {
            float rise = course.End.Top - TFAiTestMaps.FloorZ;
            Assert.True(rise > 128f && rise <= SafeDrop, $"{course.Title}: {rise} u over the floor");
        }
    }
}
