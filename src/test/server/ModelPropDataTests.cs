using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.4: a model's prop data, parsed in C#. The
/// semantics are SourceSharp_EntityGetPropData's and ReadPropDataSection's
/// (shell_scripted.cpp), held to them live on dm_lockdown (180 props, every
/// answer identical) before the native parse went; these pin the rules.
/// </summary>
public class ModelPropDataTests
{
    private const string File = @"
""PropData""
{
    ""Wooden.Small""   { ""health"" ""30""  ""breakable_model"" ""WoodChunks"" }
    ""Metal.Barrel""   { ""base"" ""Wooden.Small"" ""health"" ""20"" ""explosive_damage"" ""120"" }
    ""Loop.A""         { ""base"" ""Loop.B"" ""health"" ""7"" }
    ""Loop.B""         { ""base"" ""Loop.A"" ""explosive_radius"" ""9"" }
    ""BreakableModels"" { ""WoodChunks"" { ""models/gibs/wood_gib01a.mdl"" ""1"" } }
}
""Second.Root"" { ""Hidden"" { ""health"" ""99"" } }
";

    private static PropData Loaded()
    {
        var engine = TestServices.Fake();
        engine.Returns("Files.ReadGameFile", File);
        var props = new PropData(new RecordingLog(), engine, engine, engine);
        props.Load();
        return props;
    }

    private static int Resolve(string? text, out float damage, out float radius)
    {
        var values = Loaded().ModelPropValues(text);
        damage = values.ExplosiveDamage ?? 0f;
        radius = values.ExplosiveRadius ?? 0f;
        return values.Health ?? 0;
    }

    [Fact]
    public void TheModelsOwnSectionIsRead()
    {
        Assert.Equal(40, Resolve("mdlkeyvalue { prop_data { health 40 explosive_radius 64 } }", out float d, out float r));
        Assert.Equal(0f, d);
        Assert.Equal(64f, r);
    }

    [Fact]
    public void TheBaseIsReadFirstAndTheModelOverridesIt()
    {
        Assert.Equal(20, Resolve("mdlkeyvalue { prop_data { base Metal.Barrel explosive_radius 256 } }",
                                 out float d, out float r));
        Assert.Equal(120f, d);   // from Metal.Barrel
        Assert.Equal(256f, r);   // the model's own
    }

    [Fact]
    public void ABaseChainIsFollowedThroughTheFile()
        => Assert.Equal(20, Resolve("mdlkeyvalue { prop_data { base Metal.Barrel } }", out _, out _));

    [Fact]
    public void ABaseCycleStopsAtTheDepthLimit()
    {
        Assert.Equal(7, Resolve("mdlkeyvalue { prop_data { base Loop.A } }", out _, out float r));
        Assert.Equal(9f, r);
    }

    [Fact]
    public void OnlyTheFilesFirstRootHoldsBases()
        => Assert.Equal(0, Resolve("mdlkeyvalue { prop_data { base Hidden } }", out _, out _));

    [Fact]
    public void AModelWithNoTextNoSectionOrNoParseIsZero()
    {
        Assert.Equal(0, Resolve(null, out _, out _));
        Assert.Equal(0, Resolve("", out _, out _));
        Assert.Equal(0, Resolve("mdlkeyvalue { physgun_interactions { onbreak explode } }", out _, out _));
        Assert.Equal(0, Resolve("mdlkeyvalue { prop_data { health 5 }", out _, out _));
    }

    [Fact]
    public void PropDataIsLookedForUnderTheTextsFirstRootOnly()
        => Assert.Equal(0, Resolve("first { } mdlkeyvalue { prop_data { health 40 } }", out _, out _));
}

/// <summary>KeyValues' typed reads, as tier1 types and converts a value.</summary>
public class KeyValuesTypedReadTests
{
    private static KeyValuesText Block(string body) => KeyValuesText.Parse("root { " + body + " }").Children[0];

    [Theory]
    [InlineData("\"30\"", 30)]
    [InlineData("\"30.9\"", 30)]      // a float, truncated
    [InlineData("\"-2.5\"", -2)]
    [InlineData("\"30abc\"", 30)]     // a string, atoi
    [InlineData("\"abc\"", 0)]
    [InlineData("\"\"", 0)]
    [InlineData("\"1e2\"", 100)]      // strtod reads the exponent: a float
    [InlineData("\"0x10\"", 0)]       // hex is undone: a string, atoi stops at x
    [InlineData("{ }", 0)]            // a block
    public void GetInt(string value, int expected)
        => Assert.Equal(expected, Block("k " + value).GetInt("k", 77));

    [Theory]
    [InlineData("\"30\"", 30f)]
    [InlineData("\"0.5\"", 0.5f)]
    [InlineData("\"30abc\"", 30f)]    // a string, atof
    [InlineData("\".5\"", 0.5f)]
    [InlineData("{ }", 0f)]
    public void GetFloat(string value, float expected)
        => Assert.Equal(expected, Block("k " + value).GetFloat("k", 77f));

    [Fact]
    public void AMissingKeyAnswersTheDefault()
    {
        Assert.Equal(77, Block("k 1").GetInt("other", 77));
        Assert.Equal(7.5f, Block("k 1").GetFloat("other", 7.5f));
        Assert.Null(Block("k 1").GetString("other", null));
    }

    [Fact]
    public void GetStringOfABlockIsTheDefaultAndOfAnIntIsItsDecimal()
    {
        Assert.Equal("d", Block("k { }").GetString("k", "d"));
        Assert.Equal("12", Block("k \"012\"").GetString("k", null));
        Assert.Equal("Wooden.Small", Block("k Wooden.Small").GetString("k", null));
    }

    [Fact]
    public void FindPathDescendsOneBlockPerSlash()
    {
        var root = Block("a { b { c 5 } }");
        Assert.Equal(5, root.GetInt("a/b/c", 0));
        Assert.Null(root.FindPath("a/x"));
    }
}

/// <summary>
/// A model's prop interactions (CPropData::ParsePropFromKV). Found 2026-10-28:
/// the server read none, so dm_lockdown's 21 explosive barrels neither caught
/// fire at half health nor broke on the first impact after a physcannon throw.
/// </summary>
public class ModelInteractionsTests
{
    /// <summary>oildrum001_explosive.mdl's $keyvalues, as the model holds them.</summary>
    private const string Barrel = "mdlkeyvalue\n{\nprop_data {\n\"dmg.bullets\" \"1.0\" \"dmg.club\" \"1.0\" \"dmg.explosive\" \"1.0\" \"health\" \"20\" \"explosive_damage\" \"120\" \"explosive_radius\" \"256\"  }\nphysgun_interactions {\n\"onfirstimpact\" \"break\"  }\nfire_interactions {\n\"flammable\" \"yes\" \"explosive_resist\" \"yes\" \"ignite\" \"halfhealth\"  }\n}\n";

    private const string File = @"
""PropData""
{
    ""Sharp.Base""   { ""health"" ""5"" ""physgun_interactions"" { ""onlaunch"" ""spin_zaxis"" } }
    ""Sharp.Blade""  { ""base"" ""Sharp.Base"" }
}
";

    private static PropInteractions Read(string? text)
    {
        var engine = TestServices.Fake();
        engine.Returns("Files.ReadGameFile", File);
        var props = new PropData(new RecordingLog(), engine, engine, engine);
        props.Load();
        return props.ModelInteractions(text);
    }

    [Fact]
    public void The_explosive_barrel_breaks_on_first_impact_and_burns()
        => Assert.Equal(PropInteractions.PhysgunFirstBreak | PropInteractions.FireFlammable
                        | PropInteractions.FireExplosiveResist | PropInteractions.FireIgniteHalfHealth,
                        Read(Barrel));

    /// <summary>A base reads its interactions from its own propdata.txt section, under the model's.</summary>
    [Fact]
    public void A_base_sections_interactions_are_the_models_too()
        => Assert.Equal(PropInteractions.PhysgunLaunchSpinZ | PropInteractions.FireFlammable,
                        Read("mdlkeyvalue { prop_data { base Sharp.Blade } fire_interactions { flammable yes } }"));

    [Fact]
    public void A_value_that_is_not_the_one_is_nothing()
        => Assert.Equal(PropInteractions.None, Read("mdlkeyvalue { prop_data { health 5 } fire_interactions { flammable no } }"));

    /// <summary>ParsePropData reaches the interactions only through prop_data.</summary>
    [Fact]
    public void A_model_with_no_prop_data_has_none()
        => Assert.Equal(PropInteractions.None, Read("mdlkeyvalue { fire_interactions { flammable yes } }"));

    [Fact]
    public void Values_compare_as_stricmp_does()
        => Assert.Equal(PropInteractions.FireFlammable, Read("mdlkeyvalue { prop_data { health 5 } fire_interactions { flammable YES } }"));
}
