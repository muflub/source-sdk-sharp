using SourceSharp.MapGen;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ss_ai_tf_course's generator (lane G7a, <see cref="TFAiTestMaps.Course"/>): a row of stages over a pit, each reached
/// from the one before only by a jump, with a reset under each jump to its takeoff stage, a control point on each stage
/// after the first that needs the one before, and a cabinet at each stage. The committed sources are the generator's.
/// </summary>
public sealed class TFAiCourseMapTests
{
    private static readonly TFAiTestMaps.CourseStage[] Stages = TFAiTestMaps.CourseStages;

    /// <summary>A body's width (TF2's hull) and the highest ledge it gets up on foot (a crouched jump with its tuck, TFNavProfile's 72.2).</summary>
    private const float BodyWidth = 48f, FootRise = 73f;

    private static string Source(string name) => File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "maps", "src", "ai", name));

    private static List<VmfEntity> Entities(string className) => [.. TFAiTestMaps.Course().Entities.Where(e => e.ClassName == className)];

    private static string Key(VmfEntity e, string key) => e.KeyValues.Single(kv => kv.Key == key).Value;

    private static (float X0, float Y0, float Z0, float X1, float Y1, float Z1) Bounds(VmfEntity e)
    {
        var points = e.Solids.SelectMany(s => s.Sides).SelectMany(side => new[] { side.P0, side.P1, side.P2 }).ToList();
        return (points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z), points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
    }

    /// <summary>The flat gap between two stages' platforms (0 when they overlap along both axes).</summary>
    private static float Gap(TFAiTestMaps.CourseStage a, TFAiTestMaps.CourseStage b)
    {
        float dx = MathF.Max(0f, MathF.Max(a.West - b.East, b.West - a.East)), dy = MathF.Max(0f, MathF.Max(a.South - b.North, b.South - a.North));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    [Fact]
    public void The_committed_course_source_is_the_generators()
    {
        Assert.Equal(TFAiTestMaps.Course().Write(), Source("ss_ai_tf_course.vmf"));
        Assert.Equal(AiTestMaps.Level("ss_ai_tf_course.vmf", ["course"]), Source("ss_ai_tf_course.yaml"));
    }

    [Fact]
    public void The_jump_maps_committed_source_is_still_the_generators()
    {
        Assert.Equal(TFAiTestMaps.Jump().Write(), Source("ss_ai_tf_jump.vmf"));
    }

    [Fact]
    public void Each_stage_is_as_high_as_the_one_before_or_higher_and_no_stage_is_a_walk_from_another()
    {
        Assert.True(Stages.Length >= 6, $"{Stages.Length} stages: a course is five jumps or more");
        for (int s = 1; s < Stages.Length; s++)
            Assert.True(Stages[s].Top >= Stages[s - 1].Top, $"stage {s} at {Stages[s].Top} is under stage {s - 1} at {Stages[s - 1].Top}");

        // A body crosses no gap wider than itself on foot, and gets onto nothing higher than a crouched jump.
        for (int a = 0; a < Stages.Length; a++)
        {
            for (int b = a + 1; b < Stages.Length; b++)
                Assert.True(Gap(Stages[a], Stages[b]) > BodyWidth, $"stages {a} and {b} are {Gap(Stages[a], Stages[b])} apart");
        }

        Assert.True(Stages[0].Top - TFAiTestMaps.CourseFloorZ > 8f * FootRise, "the pit is no deeper than a few jumps");
    }

    [Fact]
    public void Each_stage_after_the_first_is_a_control_point_needing_the_one_before_under_a_master_no_team_wins()
    {
        List<VmfEntity> points = Entities("team_control_point");
        Assert.Equal(Stages.Length - 1, points.Count);
        for (int s = 1; s < Stages.Length; s++)
        {
            VmfEntity p = points.Single(e => Key(e, "targetname") == $"stage{s}");
            Assert.Equal($"{s - 1}", Key(p, "point_index"));
            string before = s == 1 ? "stage1" : $"stage{s - 1}";
            Assert.Equal(before, Key(p, "team_previouspoint_2_0"));
            Assert.Equal(before, Key(p, "team_previouspoint_3_0"));
            Assert.Equal(Stages[s].Centre.ToString(), Key(p, "origin"));

            VmfEntity area = Entities("trigger_capture_area").Single(e => Key(e, "area_cap_point") == $"stage{s}");
            var box = Bounds(area);
            Assert.True(box.X0 <= Stages[s].Centre.X && Stages[s].Centre.X <= box.X1 && box.Y0 <= Stages[s].Centre.Y && Stages[s].Centre.Y <= box.Y1
                        && box.Z0 <= Stages[s].Top && Stages[s].Top < box.Z1, $"stage {s}'s capture area does not hold its point");
        }

        Assert.Equal("1", Key(Entities("team_control_point_master").Single(), "cpm_restrict_team_cap_win"));
        Assert.Empty(Entities("item_teamflag"));
    }

    [Fact]
    public void Each_jump_has_a_reset_under_it_back_to_its_takeoff_stage_and_the_resets_split_the_floor_plan()
    {
        List<VmfEntity> resets = Entities("trigger_teleport");
        HashSet<string> destinations = [.. Entities("info_teleport_destination").Select(e => Key(e, "targetname"))];
        for (int jump = 1; jump < Stages.Length; jump++)
        {
            List<VmfEntity> mine = [.. resets.Where(r => Key(r, "target") == $"stage{jump - 1}_start")];
            Assert.NotEmpty(mine);
            Assert.Contains($"stage{jump - 1}_start", destinations);

            // Under the gap the jump flies over: its middle, between the two platforms.
            TFAiTestMaps.CourseStage a = Stages[jump - 1], b = Stages[jump];
            float midX = (MathF.Max(a.West, b.West) + MathF.Min(a.East, b.East)) / 2f, midY = (MathF.Max(a.South, b.South) + MathF.Min(a.North, b.North)) / 2f;
            if (a.East <= b.West || b.East <= a.West)
                midX = a.East <= b.West ? (a.East + b.West) / 2f : (b.East + a.West) / 2f;
            if (a.North <= b.South || b.North <= a.South)
                midY = a.North <= b.South ? (a.North + b.South) / 2f : (b.North + a.South) / 2f;
            Assert.True(mine.Any(r => Bounds(r) is var x && x.X0 <= midX && midX <= x.X1 && x.Y0 <= midY && midY <= x.Y1),
                        $"jump {jump}'s gap at ({midX} {midY}) has no reset of its own");
        }

        // No two resets overlap, and every one is under every stage top it lies under.
        var boxes = resets.Select(Bounds).ToList();
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(boxes[i].X0 < boxes[j].X1 && boxes[j].X0 < boxes[i].X1 && boxes[i].Y0 < boxes[j].Y1 && boxes[j].Y0 < boxes[i].Y1, $"resets {i} and {j} overlap");
            foreach (TFAiTestMaps.CourseStage st in Stages)
            {
                if (boxes[i].X0 < st.East && st.West < boxes[i].X1 && boxes[i].Y0 < st.North && st.South < boxes[i].Y1)
                    Assert.True(boxes[i].Z1 < st.Top, $"reset {i} reaches {boxes[i].Z1}, over a stage at {st.Top}");
            }
        }
    }

    [Fact]
    public void Each_stage_has_a_cabinet_for_every_team_and_after_the_first_none_on_the_line_between_its_landing_and_takeoff()
    {
        List<VmfEntity> cabinets = Entities("func_regenerate");
        Assert.Equal(Stages.Length, cabinets.Count);
        Assert.All(cabinets, c => Assert.Equal("0", Key(c, "TeamNum")));
        for (int s = 0; s < Stages.Length; s++)
        {
            TFAiTestMaps.CourseStage st = Stages[s];
            Assert.Single(cabinets, c => Bounds(c) is var b && b.Z0 == st.Top
                                         && b.X0 < st.East + TFAiTestMaps.BaySize && st.West - TFAiTestMaps.BaySize < b.X1
                                         && b.Y0 < st.North + TFAiTestMaps.BaySize && st.South - TFAiTestMaps.BaySize < b.Y1);
            if (s == 0 || s == Stages.Length - 1)
                continue;

            // A bay's cabinet: off the platform's own top, so walking across the stage does not refill the bot.
            VmfEntity cabinet = cabinets.Single(c => Bounds(c).Z0 == st.Top && Bounds(c) is var b && b.X0 < st.East + TFAiTestMaps.BaySize
                                                     && st.West - TFAiTestMaps.BaySize < b.X1 && b.Y0 < st.North + TFAiTestMaps.BaySize
                                                     && st.South - TFAiTestMaps.BaySize < b.Y1);
            var box = Bounds(cabinet);
            Assert.False(box.X0 < st.East && st.West < box.X1 && box.Y0 < st.North && st.South < box.Y1, $"stage {s}'s cabinet is on its platform");
        }
    }
}
