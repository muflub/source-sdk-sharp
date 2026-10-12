using System.Text.RegularExpressions;
using SourceSharp.Client.TempEnts;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// WHICH EFFECT FAMILY AN IMPACT IS — the decision that sat above
/// <see cref="ImpactEffectPolicy"/> in C++ and is
/// <see cref="ImpactChoicePolicy"/> now (plan_shim.md §6, lane 4b-effects).
///
/// <para>
/// The first three facts are the same check
/// <see cref="ImpactEffectParamsTests"/> makes for the block: these enums cross
/// as numbers, so a value inserted in the middle of either side renumbers every
/// one after it and a concrete impact starts drawing an antlion's, with nothing
/// failing to compile. They read the header off disk.
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
    private const string Header = "src/sourcesharp/game/client/impact_effects.h";

    private static string HeaderText()
    {
        string candidate = RepoLayout.Sdk(Header.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(candidate), $"{Header} was not found in {RepoLayout.SdkTree}");
        return File.ReadAllText(candidate);
    }

    // ------------------------------------------------------------------
    // The two declarations are one declaration.
    // ------------------------------------------------------------------

    [Fact]
    public void TheManagedActionsAreTheOnesTheHeaderDeclares()
    {
        var native = new Dictionary<int, string>();

        foreach (Match m in Regex.Matches(HeaderText(), @"SS_IMPACT_ACT_(\w+)\s*=\s*(\d+),"))
        {
            // SS_IMPACT_ACT_COUNT is the terminator, not an action.
            if (m.Groups[1].Value == "COUNT")
                continue;

            native[int.Parse(m.Groups[2].Value)] = m.Groups[1].Value;
        }

        Assert.Equal(8, native.Count);

        foreach ((int id, string screaming) in native)
        {
            Assert.True(Enum.IsDefined(typeof(ImpactAction), id),
                $"the header declares action {id} ({screaming}) and the managed enum has none");
            Assert.Equal(screaming.Replace("_", ""),
                         ((ImpactAction)id).ToString().ToUpperInvariant());
        }

        // AND NO MANAGED VALUE THE HEADER DOES NOT DECLARE — the loop above
        // walks the header, so a managed-only value would pass it.
        foreach (ImpactAction a in Enum.GetValues<ImpactAction>())
            Assert.True(native.ContainsKey((int)a), $"{a} is managed-only");
    }

    [Fact]
    public void TheManagedInputOrderIsTheOneTheHeaderDeclares()
    {
        var native = new List<(int Value, string Name)>();

        foreach (Match m in Regex.Matches(HeaderText(), @"SS_CHOICE_IN_(\w+)\s*=\s*(\d+),"))
        {
            if (m.Groups[1].Value == "COUNT")
                continue;

            native.Add((int.Parse(m.Groups[2].Value), m.Groups[1].Value));
        }

        Assert.Equal(ImpactChoicePolicy.InCount, native.Count);

        foreach ((int value, string screaming) in native)
        {
            Assert.True(Enum.IsDefined(typeof(ImpactChoiceIn), value),
                $"the header declares input {value} ({screaming}) and the managed enum has none");
            Assert.Equal(screaming.Replace("_", ""),
                         ((ImpactChoiceIn)value).ToString().ToUpperInvariant());
        }
    }

    [Fact]
    public void TheManagedOutputOrderIsTheOneTheHeaderDeclares()
    {
        var native = new List<(int Value, string Name)>();

        foreach (Match m in Regex.Matches(HeaderText(), @"SS_CHOICE_OUT_(\w+)\s*=\s*(\d+),"))
        {
            if (m.Groups[1].Value == "COUNT")
                continue;

            native.Add((int.Parse(m.Groups[2].Value), m.Groups[1].Value));
        }

        Assert.Equal(ImpactChoicePolicy.OutCount, native.Count);

        foreach ((int value, string screaming) in native)
        {
            Assert.True(Enum.IsDefined(typeof(ImpactChoiceOut), value),
                $"the header declares output {value} ({screaming}) and the managed enum has none");
            Assert.Equal(screaming.Replace("_", ""),
                         ((ImpactChoiceOut)value).ToString().ToUpperInvariant());
        }
    }

    [Fact]
    public void TheNameCeilingIsTheOneTheHeaderDeclares()
    {
        var m = Regex.Match(HeaderText(), @"#define\s+SS_IMPACT_NAME_MAX\s+(\d+)");

        Assert.True(m.Success, "the header no longer declares SS_IMPACT_NAME_MAX");
        Assert.Equal(ImpactChoicePolicy.NameMax, int.Parse(m.Groups[1].Value));
    }

    [Fact]
    public void TheNoFlecksFlagIsTheOneTheClientHeaderDeclares()
    {
        // FLAGS_CUSTIOM_EFFECTS_NOFLECKS -- stock's spelling of "custom",
        // preserved because it is the name the constant actually has.
        string text = File.ReadAllText(RepoLayout.Sdk("src", "game", "client", "fx_impact.h"));

        var m = Regex.Match(text, @"FLAGS_CUSTIOM_EFFECTS_NOFLECKS\s*=\s*0x([0-9A-Fa-f]+)");

        Assert.True(m.Success, "game/client/fx_impact.h no longer declares the no-flecks flag");
        Assert.Equal(ImpactChoicePolicy.FlagsNoFlecks,
                     Convert.ToInt32(m.Groups[1].Value, 16));
    }

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
