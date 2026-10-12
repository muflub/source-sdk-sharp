using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// BreakModelText: the "break" blocks of a model's collision text, and the
/// rules CBreakParser applied to each key (shell_propdata_system.cpp). The live
/// oracle compares the whole pipeline with the native slots over every
/// breakable model on dm_lockdown; these are the rules, one per fact.
/// </summary>
public class BreakModelTextTests
{
    private const string Crate = """
        solid {
        "index" "0"
        "mass" "50.000000"
        }
        break {
        "model" "props_junk/wood_crate001a_chunk01"
        "health" "5"
        "fadetime" "10"
        }
        editparams { "rootname" "" "totalmass" "50" }
        Break {
        "model" "models/props_junk/wood_crate001a_chunk02.mdl"
        "offset" "1 2 3"
        }
        """;

    private static BreakModelFields One(params (string Key, string Value)[] keys)
    {
        var list = new List<BreakKey>();
        foreach (var (k, v) in keys)
            list.Add(new BreakKey(k, v));
        return BreakModelText.Resolve(list, 1.5f, CollisionGroup.Interactive);
    }

    [Fact]
    public void OnlyBreakBlocksAreKeptInTextOrderAndTheNameIsCaseInsensitive()
    {
        List<IReadOnlyList<BreakKey>> blocks = BreakModelText.Blocks(Crate);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(new BreakKey("model", "props_junk/wood_crate001a_chunk01"), blocks[0][0]);
        Assert.Equal(new BreakKey("offset", "1 2 3"), blocks[1][1]);
    }

    [Fact]
    public void EveryTokenIsLowercasedAsVPhysicsKeyParserHandsItOver()
    {
        // The live oracle's finding: wood_crate001a's .phy names
        // "wood_crate001a_Chunk01", and the native list answered "..._chunk01".
        var blocks = BreakModelText.Blocks("BREAK { \"Model\" \"props_junk/Wood_Crate001a_Chunk01\" }");

        Assert.Single(blocks);
        Assert.Equal(new BreakKey("model", "props_junk/wood_crate001a_chunk01"), blocks[0][0]);
    }

    [Fact]
    public void LowercasingIsAsciiOnly()
    {
        var blocks = BreakModelText.Blocks("break { \"model\" \"ÄB\" }");
        Assert.Equal("Äb", blocks[0][0].Value);
    }

    [Fact]
    public void NoTextHasNoBlocks()
    {
        Assert.Empty(BreakModelText.Blocks(null));
        Assert.Empty(BreakModelText.Blocks(""));
    }

    [Fact]
    public void ANestedBlockInsideABreakBlockIsSkippedNotReadAsKeys()
    {
        var blocks = BreakModelText.Blocks("break { \"model\" \"a\" inner { \"x\" \"y\" } \"health\" \"3\" }");

        Assert.Single(blocks);
        Assert.Equal(new[] { new BreakKey("model", "a"), new BreakKey("health", "3") }, blocks[0]);
    }

    [Fact]
    public void AnEmptyBlockIsStillAPiece()
    {
        var blocks = BreakModelText.Blocks("break { }");
        Assert.Single(blocks);
        Assert.Empty(blocks[0]);
    }

    [Fact]
    public void TheDefaultsAreStocksAndTheCallersTwo()
    {
        BreakModelFields f = One();

        Assert.Equal(string.Empty, f.ModelName);
        Assert.Equal(1f, f.Health);
        Assert.Equal(20f, f.FadeTime);
        Assert.Equal(0f, f.FadeMinDist);
        Assert.Equal(0f, f.FadeMaxDist);
        Assert.Equal(1.5f, f.BurstScale);
        Assert.Equal(CollisionGroup.Interactive, f.CollisionGroup);
        Assert.False(f.IsRagdoll);
        Assert.False(f.IsMotionDisabled);
        Assert.Equal(0, f.MultiplayerBreakMode);
        Assert.Equal(Vector.Zero, f.Offset);
        Assert.Equal(Vector.Zero, f.Velocity);
    }

    [Fact]
    public void FadeTimeMakesThePieceDebrisWhenNoDebrisKeyCameFirst()
        => Assert.Equal(CollisionGroup.Debris, One(("fadetime", "5")).CollisionGroup);

    [Fact]
    public void ADebrisKeyBeforeFadeTimeIsKept()
        => Assert.Equal(CollisionGroup.Interactive, One(("debris", "0"), ("fadetime", "5")).CollisionGroup);

    [Fact]
    public void ADebrisKeyAfterFadeTimeDecides()
    {
        Assert.Equal(CollisionGroup.Interactive, One(("fadetime", "5"), ("debris", "0")).CollisionGroup);
        Assert.Equal(CollisionGroup.Debris, One(("debris", "1")).CollisionGroup);
    }

    [Fact]
    public void KeysAreCaseInsensitiveAndAnUnknownKeyIsIgnored()
    {
        BreakModelFields f = One(("HEALTH", "7"), ("nonsense", "9"));
        Assert.Equal(7f, f.Health);
    }

    [Fact]
    public void ARagdollKeyNamesTheModelAndMarksIt()
    {
        BreakModelFields f = One(("ragdoll", "humans/corpse1"));
        Assert.Equal("models/humans/corpse1.mdl", f.ModelName);
        Assert.True(f.IsRagdoll);
    }

    [Fact]
    public void MotionDisabledIsSetByThePresenceOfTheKeyWhateverItsValue()
        => Assert.True(One(("motiondisabled", "0")).IsMotionDisabled);

    [Fact]
    public void TheBurstKeyReplacesTheCallersDefault()
        => Assert.Equal(3.25f, One(("burst", "3.25")).BurstScale);

    [Fact]
    public void MultiplayerBreakReadsServerAndClientAndIgnoresAnythingElse()
    {
        Assert.Equal(1, One(("multiplayer_break", "SERVER")).MultiplayerBreakMode);
        Assert.Equal(2, One(("multiplayer_break", "client")).MultiplayerBreakMode);
        Assert.Equal(0, One(("multiplayer_break", "both")).MultiplayerBreakMode);
    }

    [Theory]
    [InlineData("props/crate", "models/props/crate.mdl")]
    [InlineData("models/props/crate.mdl", "models/props/crate.mdl")]
    [InlineData("MODELS/props/crate.MDL", "MODELS/props/crate.MDL")]
    [InlineData("models/props/crate", "models/props/crate.mdl")]
    [InlineData("", "models/.mdl")]
    public void TheModelNameIsFixedUpAsStockDid(string value, string expected)
        => Assert.Equal(expected, BreakModelText.FixupModelName(value));

    [Fact]
    public void AVectorTheTextRunsOutOfIsZeroFromThere()
    {
        Assert.Equal(new Vector(5f, 0f, 0f), BreakModelText.StringToVector("5"));
        Assert.Equal(new Vector(1f, 2f, 0f), BreakModelText.StringToVector("1 2"));
        Assert.Equal(new Vector(1f, 2f, 3f), BreakModelText.StringToVector("1 2 3 4"));
    }

    [Fact]
    public void EachVectorComponentIsAtofOfItsToken()
    {
        // atof takes a prefix and answers 0 for no number, unlike TryParse.
        Assert.Equal(new Vector(1.5f, 0f, -2f), BreakModelText.StringToVector("1.5x junk -2"));
    }

    [Fact]
    public void ARepeatedKeysLastValueWins()
        => Assert.Equal(new Vector(0f, 9f, 0f), One(("offset", "1 2 3"), ("offset", "0 9")).Offset);
}
