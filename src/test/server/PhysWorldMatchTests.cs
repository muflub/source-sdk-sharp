using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysWorldMatch: the cases of native physworld_match_rule_test.cpp, which it
/// replaces.
/// </summary>
public class PhysWorldMatchTests
{
    [Fact]
    public void The_empty_request_is_not_every_object()
    {
        Assert.False(PhysWorldMatch.AllRequested(""));
        Assert.False(PhysWorldMatch.Matches("prop_physics", ""));
    }

    [Fact]
    public void The_star_request_is_every_object()
    {
        Assert.True(PhysWorldMatch.AllRequested("*"));
        Assert.True(PhysWorldMatch.Matches("prop_physics", "*"));
        Assert.True(PhysWorldMatch.Matches("world", "*"));
    }

    [Fact]
    public void The_star_request_includes_an_object_with_no_name()
        => Assert.True(PhysWorldMatch.Matches(null, "*"));

    [Fact]
    public void An_object_with_no_name_answers_no_substring()
        => Assert.False(PhysWorldMatch.Matches(null, "prop"));

    [Fact]
    public void Only_a_bare_star_widens_the_request()
    {
        Assert.False(PhysWorldMatch.AllRequested("*prop"));
        Assert.False(PhysWorldMatch.AllRequested("prop*"));
        Assert.False(PhysWorldMatch.AllRequested("**"));
        Assert.False(PhysWorldMatch.Matches("prop_physics", "prop*"));
    }

    [Fact]
    public void An_ordinary_substring_still_matches_either_case()
    {
        Assert.True(PhysWorldMatch.Matches("prop_physics", "prop"));
        Assert.True(PhysWorldMatch.Matches("PROP_PHYSICS", "prop"));
        Assert.False(PhysWorldMatch.Matches("prop_physics", "ragdoll"));
    }
}
