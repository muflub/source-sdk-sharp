using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A set declaring one of everything, so the checks below are about the
/// machinery rather than about a game entity that might change.
///
/// <para>
/// The defaults are deliberately NOT the types' zeroes, and one of them is a
/// Vector — which an attribute argument could never have been, and which is the
/// reason the default is an initialiser.
/// </para>
/// </summary>
[ScriptVars]
public partial class TestVarSet
{
    public float Speed { get; set; } = 250f;

    public int Count { get; set; } = 7;

    public bool Enabled { get; set; } = true;

    public string Label { get; set; } = "unnamed";

    public Vector Where { get; set; } = new(1f, 2f, 3f);

    /// <summary>Three numbers a map writes exactly like Where's, meaning something else.</summary>
    public QAngle Facing { get; set; } = new(10f, 20f, 30f);

    public Color32 Tint { get; set; } = new(1, 2, 3);

    [VarName("in1")]
    public float Lower { get; set; }

    /// <summary>
    /// Not a map key: it has no setter, which is the opt-out. If membership ever
    /// stops meaning what it means, this is what notices.
    /// </summary>
    public float SpeedSquared => Speed * Speed;
}

/// <summary>
/// What a var set knows about itself: what the map wrote, when, and how to
/// forget it.
/// </summary>
public class ScriptedVarSetTests
{

    [Fact]
    public void Declaration()
    {
        var set = new TestVarSet();

        Assert.True(set.Descriptors.Select(d => d.Member).OrderBy(n => n, StringComparer.Ordinal)
               .SequenceEqual(new[]
               {
                   "Count", "Enabled", "Facing", "Label", "Lower", "Speed", "Tint", "Where",
               }), "every settable property is a key, and only those");

        Assert.True(!set.Descriptors.Any(d => d.Member == "SpeedSquared"), "a get-only property is not a key");

        Assert.True(set.Descriptors.Any(d => d.Member == "Speed" && d.Name == "Speed"), "a key is named for its property");

        Assert.True(set.Descriptors.Any(d => d.Member == "Lower" && d.Name == "in1"), "[VarName] renames it");
    }

    [Fact]
    public void SetAndUnset()
    {
        var set = new TestVarSet();

        // THE DISTINCTION THE WHOLE DESIGN IS FOR. A key at its default and a
        // key the map wrote to that same value must not read alike.
        Assert.True(!set.SpeedIsSet, "a default is not an assignment");
        Assert.True(set.Speed == 250f, "and the value is the declared default");
        Assert.True(set.SpeedLastSetAt == ScriptedVarSet.Never, "with no stamp");

        Assert.True(set.OnKeyDataUpdate("speed", "250") && set.SpeedIsSet && set.Speed == 250f, "the map writing the default value still counts as set");

        // Assigning from game code is not the map writing a key, and does not
        // claim to be.
        var other = new TestVarSet();
        other.Count = 99;
        Assert.True(!other.CountIsSet, "game code assigning a var does not make it map-set");

        Assert.True(other.OnKeyDataUpdate("where", "1 2") && other.Where.Equals(new Vector(1f, 2f, 3f)), "an unreadable value leaves the default and still claims the key");

        Assert.True(!other.WhereIsSet, "and does not mark it set");

        Assert.True(!other.OnKeyDataUpdate("nosuchkey", "1"), "an unknown key is not claimed");
        Assert.True(!other.OnKeyDataUpdate("spede", "1"), "and a near miss is not either");

        StructuredKeys();
    }

    /// <summary>
    /// The three-number kinds. A map writes all of them the same way and they
    /// mean different things, which is why they are different types rather than
    /// one Vector with a comment.
    /// </summary>
    private static void StructuredKeys()
    {
        var set = new TestVarSet();

        Assert.True(set.OnKeyDataUpdate("where", "1 -2 3.5")
            && set.Where.Equals(new Vector(1f, -2f, 3.5f)), "a vector key reads three numbers");

        Assert.True(set.OnKeyDataUpdate("facing", "-90 45 0")
            && set.Facing == new QAngle(-90f, 45f, 0f), "an angle key reads the same three numbers as pitch, yaw, roll");

        Assert.True(Angles.Forward(set.Facing).Equals(Angles.Forward(set.Facing.AsVector())), "and an angle is not a vector");

        Assert.True(set.OnKeyDataUpdate("tint", "255 128 0")
            && set.Tint == new Color32(255, 128, 0, 255), "a colour key reads r g b, opaque by default");

        Assert.True(set.OnKeyDataUpdate("tint", "10 20 30 40")
            && set.Tint == new Color32(10, 20, 30, 40), "and r g b a when the map writes one");

        // Maps write "255.0" where 255 is meant, everywhere else in the parse,
        // and a colour channel is not an exception.
        Assert.True(set.OnKeyDataUpdate("tint", "255.0 0 0") && set.Tint.R == 255, "a colour channel reads a decimal");

        var strict = new TestVarSet();

        Assert.True(strict.OnKeyDataUpdate("tint", "255 128") && !strict.TintIsSet
            && strict.Tint == new Color32(1, 2, 3), "a colour short of three channels is refused");

        Assert.True(strict.OnKeyDataUpdate("facing", "90") && !strict.FacingIsSet
            && strict.Facing == new QAngle(10f, 20f, 30f), "and so is an angle short of three numbers");
    }

    [Fact]
    public void Resetting()
    {
        var set = new TestVarSet();

        set.OnKeyDataUpdate("speed", "10");
        set.OnKeyDataUpdate("count", "11");
        set.OnKeyDataUpdate("enabled", "0");
        set.OnKeyDataUpdate("label", "written");
        set.OnKeyDataUpdate("where", "9 9 9");
        set.OnKeyDataUpdate("facing", "9 9 9");
        set.OnKeyDataUpdate("tint", "9 9 9");
        set.OnKeyDataUpdate("in1", "5");

        Assert.True(set.Descriptors.All(d => d.IsSet(set)), "everything is set before the reset");

        set.Reset();

        Assert.True(set.Speed == 250f && set.Count == 7 && set.Enabled
            && set.Label == "unnamed" && set.Where.Equals(new Vector(1f, 2f, 3f))
            && set.Facing == new QAngle(10f, 20f, 30f) && set.Tint == new Color32(1, 2, 3)
            && set.Lower == 0f, "reset restores every declared default");

        // The half that gets missed: a reset object that still reports a key as
        // set, with a stamp from before the reset, is lying about itself.
        Assert.True(set.Descriptors.All(d => !d.IsSet(set)), "reset forgets that the map wrote anything");

        Assert.True(set.Descriptors.All(d => d.LastSetAt(set) == ScriptedVarSet.Never), "and drops every stamp with it");

        // The case the whole thing is for: a second map's keys must not inherit
        // the first's values through a var nobody wrote twice.
        set.OnKeyDataUpdate("speed", "42");

        Assert.True(set.Speed == 42f, "a key written after the reset holds the new value");
        Assert.True(set.Count == 7 && set.Label == "unnamed" && !set.CountIsSet && !set.LabelIsSet, "and one that was not is at its default, not its previous value");
    }
}
