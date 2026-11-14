using SourceSharp.MapGen;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ss_ai_tf_double's generator (lane G7b, <see cref="TFAiTestMaps.Double"/>): three stages on one line, the second jump
/// over a beam a second rocket is fired at, a reset under each jump and one on the beam's top, a control point on each
/// stage after the first. The committed source is the generator's.
/// </summary>
public sealed class TFAiDoubleMapTests
{
    private static readonly TFAiTestMaps.CourseStage[] Stages = TFAiTestMaps.DoubleStages;

    private static string Source(string name) => File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "maps", "src", "ai", name));

    private static List<VmfEntity> Entities(string className) => [.. TFAiTestMaps.Double().Entities.Where(e => e.ClassName == className)];

    private static string Key(VmfEntity e, string key) => e.KeyValues.Single(kv => kv.Key == key).Value;

    private static (float X0, float Y0, float Z0, float X1, float Y1, float Z1) Bounds(VmfEntity e)
    {
        var points = e.Solids.SelectMany(s => s.Sides).SelectMany(side => new[] { side.P0, side.P1, side.P2 }).ToList();
        return (points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z), points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
    }

    [Fact]
    public void The_committed_source_is_the_generators_and_the_other_courses_are_unchanged()
    {
        Assert.Equal(TFAiTestMaps.Double().Write(), Source("ss_ai_tf_double.vmf"));
        Assert.Equal(TFAiTestMaps.Course().Write(), Source("ss_ai_tf_course.vmf"));
        Assert.Equal(TFAiTestMaps.Jump().Write(), Source("ss_ai_tf_jump.vmf"));
    }

    [Fact]
    public void The_beam_lies_on_the_line_between_the_last_two_stages_under_the_last_and_narrower_than_a_landing_across_it()
    {
        (float w, float s, float e, float n) = TFAiTestMaps.DoubleBeam;
        TFAiTestMaps.CourseStage s1 = Stages[1], s2 = Stages[2];
        float line = s1.Centre.Y;
        Assert.Equal(line, s2.Centre.Y);
        Assert.True(s1.East < w && e < s2.West, $"the beam {w}..{e} between S1's edge {s1.East} and S2's {s2.West}");
        Assert.True(s <= line && line <= n, "the beam is under the line");
        // JumpPlacesSettings.MinRun: the shortest ledge the link pass lands on. Across the line the beam is shorter.
        Assert.True(n - s < 64f, $"the beam is {n - s} across the line");
        Assert.True(TFAiTestMaps.DoubleBeamTop > s1.Top && TFAiTestMaps.DoubleBeamTop < s2.Top);
    }

    [Fact]
    public void A_body_moved_from_the_last_stage_to_a_spawn_never_crosses_the_beam()
    {
        // A round's restart moves the bot from where the course ended to a spawn; the game touched the beam's reset on the
        // way when S0 was on the second jump's line (lane G7b), and put it on S1. Over the ground, the straight move misses the beam.
        (float w, float s, float e, float n) = TFAiTestMaps.DoubleBeam;
        TFAiTestMaps.CourseStage end = Stages[^1];
        List<VmfEntity> spawns = Entities("info_player_teamspawn");
        Assert.NotEmpty(spawns);
        foreach (VmfEntity spawn in spawns)
        {
            float[] at = [.. Key(spawn, "origin").Split(' ').Select(float.Parse)];
            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f, x = end.Centre.X + (at[0] - end.Centre.X) * t, y = end.Centre.Y + (at[1] - end.Centre.Y) * t;
                Assert.False(x >= w - 24f && x <= e + 24f && y >= s - 24f && y <= n + 24f, $"the move from S2 to the spawn at {at[0]} {at[1]} passes over the beam at {x} {y}");
            }
        }
    }

    [Fact]
    public void Each_jump_has_a_reset_back_to_its_takeoff_and_the_beams_top_one_back_to_the_second_stage()
    {
        List<VmfEntity> resets = Entities("trigger_teleport");
        Assert.Equal(TFAiTestMaps.DoubleResets.Length + 1, resets.Count);
        foreach ((int jump, float x0, float y0, float x1, float y1) in TFAiTestMaps.DoubleResets)
        {
            VmfEntity reset = Assert.Single(resets, r => Bounds(r).X0 == x0 && Bounds(r).Y0 == y0 && Bounds(r).Z0 == TFAiTestMaps.CourseFloorZ);
            Assert.Equal($"stage{jump - 1}_start", Key(reset, "target"));
            Assert.True(Bounds(reset).Z1 < Stages[jump - 1].Top, "nobody standing on its takeoff stage is in it");
        }

        (float w, float s, float e, float n) = TFAiTestMaps.DoubleBeam;
        VmfEntity beam = Assert.Single(resets, r => Bounds(r).Z0 == TFAiTestMaps.DoubleBeamTop);
        Assert.Equal((w, s, e, n, TFAiTestMaps.DoubleBeamTop + TFAiTestMaps.DoubleBeamReset), (Bounds(beam).X0, Bounds(beam).Y0, Bounds(beam).X1, Bounds(beam).Y1, Bounds(beam).Z1));
        Assert.Equal("stage1_start", Key(beam, "target"));
        Assert.Equal(Stages.Length, Entities("info_teleport_destination").Count);
    }

    [Fact]
    public void Each_stage_after_the_first_is_a_point_needing_the_one_before_and_each_has_a_cabinet()
    {
        List<VmfEntity> points = Entities("team_control_point");
        Assert.Equal(Stages.Length - 1, points.Count);
        for (int st = 1; st < Stages.Length; st++)
        {
            VmfEntity point = Assert.Single(points, p => Key(p, "targetname") == $"stage{st}");
            Assert.Equal($"{st - 1}", Key(point, "point_index"));
            Assert.Equal(st == 1 ? "stage1" : $"stage{st - 1}", Key(point, "team_previouspoint_2_0"));
        }

        Assert.Equal("1", Key(Assert.Single(Entities("team_control_point_master")), "cpm_restrict_team_cap_win"));
        Assert.Equal(Stages.Length, Entities("func_regenerate").Count(z => Key(z, "TeamNum") == "0"));
        Assert.Equal(8, Entities("info_player_teamspawn").Count);
    }
}
