using SourceSharp.Client.TempEnts;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// WHICH EFFECT FAMILY AN IMPACT IS — the decision that sat above
/// <see cref="ImpactEffectPolicy"/> in C++ and is
/// <see cref="ImpactChoicePolicy"/> now (plan_shim.md §6, lane 4b-effects).
///
/// <para>
/// The facts that held these enums to <c>impact_effects.h</c>, number for
/// number, left with that header in lane M1-C: the choice no longer crosses as
/// numbers, because the managed impact asks the policy itself.
/// </para>
///
/// <para>
/// The rest are about the DECISIONS, one behaviour each, and every one of them
/// was unaskable before the move: the chain and the table were <c>switch</c>
/// and array literals inside a translation unit no managed test can reach.
/// </para>
/// </summary>
public class ImpactChoiceTests
{
    // THE HEADER THE ACTIONS, INPUTS AND OUTPUTS ONCE MATCHED (impact_effects.h)
    // LEFT in lane M1-C with its last reader: BulletImpactEffect asks
    // ImpactChoicePolicy directly, so nothing crosses as these numbers now.

    // (The no-flecks flag is held to stock's fx_impact.h by ImpactOracleTests,
    // over the upstream clone: the client build includes that header no more.)

    // ------------------------------------------------------------------
    // The chain — cl_new_impact_effects 0, which is every shot in this build.
    // One fact per arm, because each arm is a separate claim about a surface.
    // ------------------------------------------------------------------

    private static ImpactChoice Chain(int material, int scale = 1, int flags = 0,
                                      bool drawFlecks = true)
        => ImpactChoicePolicy.Choose(material, scale, flags, drawFlecks, false, 0);

    [Fact]
    public void ConcreteGetsDebrisFlecks() => Assert.Equal(
        ImpactAction.DebrisFlecks, Chain('C').Action);

    [Fact]
    public void TileGetsDebrisFlecks() => Assert.Equal(
        ImpactAction.DebrisFlecks, Chain('T').Action);

    [Fact]
    public void WoodGetsDebrisFlecks() => Assert.Equal(
        ImpactAction.DebrisFlecks, Chain('W').Action);

    [Fact]
    public void DirtGetsDust() => Assert.Equal(ImpactAction.Dust, Chain('D').Action);

    [Fact]
    public void SandGetsDust() => Assert.Equal(ImpactAction.Dust, Chain('N').Action);

    [Fact]
    public void AntlionGetsItsOwnImpact() => Assert.Equal(
        ImpactAction.Antlion, Chain('A').Action);

    [Fact]
    public void MetalGetsAMetalSpark() => Assert.Equal(
        ImpactAction.MetalSpark, Chain('M').Action);

    [Fact]
    public void VentGetsAMetalSpark() => Assert.Equal(
        ImpactAction.MetalSpark, Chain('V').Action);

    [Fact]
    public void ComputerGetsSparks() => Assert.Equal(
        ImpactAction.Sparks, Chain('P').Action);

    [Fact]
    public void WarpShieldDispatchesItsParticleSystemByName()
    {
        var choice = Chain('Z');

        Assert.Equal(ImpactAction.Particle, choice.Action);
        Assert.Equal("warp_shield_impact", choice.ParticleName);
    }

    [Fact]
    public void GlassGetsNothingFromTheChain()
    {
        // NOT AN OVERSIGHT AND NOT A REGRESSION: stock's chain has no glass arm
        // either. FX_GlassImpact is reached from the "GlassImpact" client
        // effect, which the server dispatches by name, and never from here.
        Assert.Equal(ImpactAction.Nothing, Chain('Y').Action);
    }

    [Fact]
    public void BloodyFleshGetsNothingFromTheChain() => Assert.Equal(
        ImpactAction.Nothing, Chain('B').Action);

    [Fact]
    public void AMaterialTheChainHasNoArmForGetsNothing() => Assert.Equal(
        ImpactAction.Nothing, Chain('S').Action);   // CHAR_TEX_SLOSH

    [Fact]
    public void TheChainNamesNoParticleSystemExceptForTheWarpShield()
    {
        foreach (int material in "ACDMNPTVWY")
        {
            if (material == 'Z') continue;

            Assert.Equal("", Chain(material).ParticleName);
        }
    }

    // ------------------------------------------------------------------
    // The table — cl_new_impact_effects 1.
    // ------------------------------------------------------------------

    private static ImpactChoice Table(int material, bool drawFlecks = true, int flags = 0)
        => ImpactChoicePolicy.Choose(material, 1, flags, drawFlecks, true, 0);

    [Fact]
    public void TheTableNamesConcretesParticleSystem()
    {
        var choice = Table('C');

        Assert.Equal(ImpactAction.NewParticle, choice.Action);
        Assert.Equal("impact_concrete", choice.ParticleName);
    }

    [Fact]
    public void SuppressedFlecksTakeTheRowsOtherName() => Assert.Equal(
        "impact_concrete_noflecks", Table('C', drawFlecks: false).ParticleName);

    [Fact]
    public void ARowWithNoSecondNameKeepsItsFirstWhenFlecksAreSuppressed()
    {
        // impact_metal has no _noflecks variant, so suppression changes nothing
        // for it. A managed side that fell back to "" here would have stopped
        // drawing metal impacts for anyone with r_drawflecks 0.
        Assert.Equal("impact_metal", Table('M', drawFlecks: false).ParticleName);
    }

    [Fact]
    public void TheTableGivesDirtAndSandTheSameSystem() => Assert.Equal(
        Table('D').ParticleName, Table('N').ParticleName);

    [Fact]
    public void TheTableHasAGlassRowWhereTheChainHasNoArm()
    {
        // The two paths genuinely disagree and this pins it, because it looks
        // like a bug in whichever one you read second. Both are stock's.
        Assert.Equal("impact_glass", Table('Y').ParticleName);
        Assert.Equal(ImpactAction.Nothing, Chain('Y').Action);
    }

    [Fact]
    public void ARowWithNoNameDrawsNothing() => Assert.Equal(
        ImpactAction.Nothing, Table('B').Action);   // CHAR_TEX_BLOODYFLESH

    [Fact]
    public void AMaterialOutsideTheTableIsRefusedRatherThanRead()
    {
        // THE DEFECT THIS MOVE FIXES. Stock indexes s_pImpactEffect[26] as
        // `[ iMaterial - 'A' ]` with no bounds test, from a material byte that
        // arrives over the wire -- a surfaceprop naming anything outside
        // 'A'..'Z' read past the array and handed CNewParticleEffect::Create
        // whatever pointer happened to be there.
        Assert.Equal(ImpactAction.Nothing, Table('a').Action);
        Assert.Equal(ImpactAction.Nothing, Table('@').Action);
        Assert.Equal(ImpactAction.Nothing, Table(0).Action);
        Assert.Equal(ImpactAction.Nothing, Table(255).Action);
    }

    [Fact]
    public void AMaterialOutsideTheTableStillGetsItsDecal()
    {
        // The bounds check refuses the EFFECT and nothing else: the decal is
        // not indexed by material on this side, and a surface nobody has a
        // particle system for still takes a bullet hole.
        Assert.Equal(ImpactChoicePolicy.DefaultDecal, Table(0).DecalName);
    }

    // ------------------------------------------------------------------
    // The fleck suppression, which stock computed twice.
    // ------------------------------------------------------------------

    [Fact]
    public void FlecksAreOnWhenTheConVarIsOnAndTheCallerIsQuiet() => Assert.False(
        Chain('C', drawFlecks: true).NoFlecks);

    [Fact]
    public void TheConVarAloneSuppressesFlecks() => Assert.True(
        Chain('C', drawFlecks: false).NoFlecks);

    [Fact]
    public void TheCallersFlagAloneSuppressesFlecks() => Assert.True(
        Chain('C', flags: ImpactChoicePolicy.FlagsNoFlecks, drawFlecks: true).NoFlecks);

    [Fact]
    public void AnUnrelatedCallerFlagDoesNotSuppressFlecks() => Assert.False(
        Chain('C', flags: ~ImpactChoicePolicy.FlagsNoFlecks, drawFlecks: true).NoFlecks);

    // ------------------------------------------------------------------
    // The decal.
    // ------------------------------------------------------------------

    [Fact]
    public void EveryImpactNamesTheSameDecalBeforeTranslation()
    {
        // GetImpactDecal's whole body was `decalName = "Impact.Concrete"` and
        // then the engine's translation. The name is one constant because
        // DamageDecal is a C_BaseEntity virtual and this build has no
        // instances of that hierarchy -- so there is no table to consult, and
        // the surfaceprop the shot hit is what the translator uses.
        foreach (int material in "ABCDMNPTVWYZ")
            Assert.Equal("Impact.Concrete", Chain(material).DecalName);
    }

    [Fact]
    public void TheDecalNameFitsTheBufferItCrossesIn() => Assert.True(
        ImpactChoicePolicy.DefaultDecal.Length + 1 <= ImpactChoicePolicy.NameMax);

    [Fact]
    public void EveryParticleSystemNameFitsTheBufferItCrossesIn()
    {
        // The longest is "impact_concrete_noflecks". A name that did not fit
        // would be REFUSED by the writer, not truncated, so this failing means
        // that material silently stops drawing rather than drawing wrong.
        for (int material = 'A'; material <= 'Z'; material++)
        {
            foreach (bool flecks in new[] { true, false })
            {
                string name = Table(material, drawFlecks: flecks).ParticleName;

                Assert.True(name.Length + 1 <= ImpactChoicePolicy.NameMax,
                    $"'{name}' does not fit SS_IMPACT_NAME_MAX");
            }
        }
    }

    // ------------------------------------------------------------------
    // The flat form the ABI slot speaks.
    // ------------------------------------------------------------------

    [Fact]
    public void TheFlatFormWritesTheThreeOutputsInTheHeadersOrder()
    {
        Span<int> input = stackalloc int[ImpactChoicePolicy.InCount];
        input[(int)ImpactChoiceIn.Material] = 'C';
        input[(int)ImpactChoiceIn.Scale] = 4;
        input[(int)ImpactChoiceIn.Flags] = 0;
        input[(int)ImpactChoiceIn.DrawFlecks] = 0;
        input[(int)ImpactChoiceIn.NewEffects] = 0;
        input[(int)ImpactChoiceIn.DamageType] = 0;

        Span<int> output = stackalloc int[ImpactChoicePolicy.OutCount];

        int written = ImpactChoicePolicy.Choose(input, output, out _);

        Assert.Equal(ImpactChoicePolicy.OutCount, written);
        Assert.Equal((int)ImpactAction.DebrisFlecks, output[(int)ImpactChoiceOut.Action]);
        Assert.Equal(4, output[(int)ImpactChoiceOut.Scale]);
        Assert.Equal(1, output[(int)ImpactChoiceOut.NoFlecks]);
    }

    [Fact]
    public void AShortInputIsRefusedRatherThanReadPast()
    {
        Span<int> input = stackalloc int[ImpactChoicePolicy.InCount - 1];
        Span<int> output = stackalloc int[ImpactChoicePolicy.OutCount];

        Assert.Equal(-1, ImpactChoicePolicy.Choose(input, output, out _));
    }

    [Fact]
    public void AnOutputTooSmallIsRefusedRatherThanOverrun()
    {
        Span<int> input = stackalloc int[ImpactChoicePolicy.InCount];
        Span<int> output = stackalloc int[ImpactChoicePolicy.OutCount - 1];

        Assert.Equal(-1, ImpactChoicePolicy.Choose(input, output, out _));
    }

    [Fact]
    public void ARefusalLeavesTheChoiceAtNothing()
    {
        // Native does what stock's fall-through does on a refusal, so the
        // refused choice must not carry an action that would draw something.
        Span<int> input = stackalloc int[0];
        Span<int> output = stackalloc int[ImpactChoicePolicy.OutCount];

        ImpactChoicePolicy.Choose(input, output, out ImpactChoice choice);

        Assert.Equal(ImpactAction.Nothing, choice.Action);
    }

    [Fact]
    public void TheScaleTheCallerSentComesBackUnchanged() => Assert.Equal(
        7, ImpactChoicePolicy.Choose('C', 7, 0, true, false, 0).Scale);
}
