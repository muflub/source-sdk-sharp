using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>The rules module's <c>GenerateLayout</c>: the generator for a codec-named pool, the old row for a free-named one.</summary>
public sealed class GenerateLayoutTests
{
    private static RoomInfo R(string name, params string[] tags) => new(name, tags, 1, 1, 15);

    private static List<RoomInfo> Named() =>
    [
        R("arrival_cage-e-c", "start"),
        R("exit_cage-w-c", "stairs"),
        R("drift_a-ew-l", "corridor"),
        R("bend_a-ne-l", "corridor"),
        R("tee_a-esw-l", "junction"),
        R("cross_a-nesw-l", "junction"),
        R("stub_a-e-l", "shrine"),
        R("split_a-ew-c-j.s2.s3"),
        R("pit_a-ne-c-j.s2.s3"),
        R("gallery_a-ew-h-j.b1.b2"),
        R("stope_a-ne-h-j.b1.b2"),
        R("vault_a-e-c", "treasure"),
    ];

    // The host's fake libraries name rooms freely (Host.FakeRules' Samples).
    private static List<RoomInfo> Free() => [R("start", "start"), R("hall"), R("corner"), R("stairs", "stairs")];

    [Fact]
    public void A_pool_named_in_the_codec_is_laid_out_by_the_generator()
    {
        var key = new LayoutKey("lib", 3, 0, 42, Named());

        var plan = new TF2DescentRules().GenerateLayout(key);

        var expected = new LevelLayout().Generate(3, 42, Named()).Plan!;
        Assert.Equal((expected.Rows, expected.Columns), (plan.Rows, plan.Columns));
        Assert.Equal(expected.Placements, plan.Placements);
        Assert.Equal((expected.UpRoom, expected.DownRoom), (plan.UpRoom, plan.DownRoom));
    }

    [Fact]
    public void A_pool_of_free_names_keeps_the_three_room_row()
    {
        var plan = new TF2DescentRules().GenerateLayout(new LayoutKey("lib", 3, 0, 42, Free()));

        Assert.Equal((1, 3), (plan.Rows, plan.Columns));
        Assert.Equal(["up", "fill", "down"], plan.Placements.Select(p => p.Role));
        Assert.Equal(("start", "stairs"), (plan.UpRoom, plan.DownRoom));
        Assert.Contains(plan.Placements[1].Room, new[] { "hall", "corner" });
    }

    [Fact]
    public void A_pool_that_mixes_free_and_codec_names_is_refused_naming_the_room()
    {
        var rules = new TF2DescentRules();

        var thrown = Assert.Throws<InvalidOperationException>(() => rules.GenerateLayout(new LayoutKey("lib", 3, 0, 42, [.. Named(), R("hall")])));

        Assert.Contains("room name 'hall'", thrown.Message);
        Assert.Contains("library 'lib' at depth 3, seed 42", thrown.Message);
    }

    [Fact]
    public void A_codec_pool_the_generator_cannot_lay_out_is_refused()
    {
        var pool = Named().Where(r => !r.Tags.Contains("stairs")).ToList();

        var thrown = Assert.Throws<InvalidOperationException>(() => new TF2DescentRules().GenerateLayout(new LayoutKey("lib", 3, 0, 42, pool)));

        Assert.Contains("no exit", thrown.Message);
    }
}
