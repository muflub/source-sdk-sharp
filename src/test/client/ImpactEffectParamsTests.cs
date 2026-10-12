using System.Text.RegularExpressions;
using SourceSharp.Client.TempEnts;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The impact effect parameter block is ONE layout declared on two sides, and
/// this is what stops them drifting (plan_entity.md §3.9, SHIM_WAVE Lane 3).
///
/// <para>
/// THESE ARE ARRAY INDICES IN ALL BUT NAME. A value inserted in the middle of
/// either enum renumbers everything after it, and every field past that point
/// silently becomes a different number — a fleck count read as a gravity, with
/// nothing failing to compile on either side. <c>TempEntForwardIdTests</c> is
/// the same test for the same reason and this is written to its shape.
/// </para>
///
/// <para>
/// The rest of the facts here are about the DECISIONS the block carries, one
/// behaviour each, and every one of them was red before the move because there
/// was nothing on this side to ask.
/// </para>
/// </summary>
public class ImpactEffectParamsTests
{
    private const string Header = "src/sourcesharp/game/client/impact_effects.h";

    private static string HeaderText()
    {
        string path = RepoLayout.Sdk(Header.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{Header} was not found in {RepoLayout.SdkTree}");
        return File.ReadAllText(path);
    }

    // ------------------------------------------------------------------
    // The two declarations are one declaration.
    // ------------------------------------------------------------------

    [Fact]
    public void TheManagedEffectIdsAreTheOnesTheHeaderDeclares()
    {
        // SOURCESHARP_IMPACT_GLASS = 1,   // FX_GlassImpact -> Glass
        var native = new Dictionary<int, string>();

        foreach (Match m in Regex.Matches(HeaderText(),
                     @"SOURCESHARP_IMPACT_(\w+)\s*=\s*(\d+),\s*//\s*(\w+)\s*->\s*(\w+)"))
        {
            native[int.Parse(m.Groups[2].Value)] = m.Groups[4].Value;
        }

        Assert.Equal(7, native.Count);

        foreach ((int id, string managed) in native)
        {
            Assert.True(Enum.IsDefined(typeof(ImpactEffect), id),
                $"the header declares effect id {id} ({managed}) and the managed enum has no such value");
            Assert.Equal(managed, ((ImpactEffect)id).ToString());
        }

        // AND NO MANAGED VALUE THE HEADER DOES NOT DECLARE, which the loop above
        // cannot see: it walks the header, so a managed value added alone would
        // pass it.
        foreach (ImpactEffect e in Enum.GetValues<ImpactEffect>())
            Assert.True(native.ContainsKey((int)e), $"{e} is managed-only; the header does not declare it");
    }

    [Fact]
    public void TheBurstFieldNamesAndOrderAreTheOnesTheHeaderDeclares()
    {
        var native = new List<(int Value, string Name)>();

        foreach (Match m in Regex.Matches(HeaderText(), @"SS_BURST_(\w+)\s*=\s*(\d+),"))
            native.Add((int.Parse(m.Groups[2].Value), m.Groups[1].Value));

        Assert.Equal(27, native.Count);

        // SS_BURST_COUNT_MIN -> CountMin. The two spellings are one word list.
        foreach ((int value, string name) in native)
        {
            string managed = string.Concat(name.Split('_')
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

            Assert.True(Enum.IsDefined(typeof(ImpactBurst), value),
                $"SS_BURST_{name} = {value} has no managed ImpactBurst value");
            Assert.Equal(managed, ((ImpactBurst)value).ToString());
        }
    }

    [Fact]
    public void TheBlockWidthsAgreeWithTheHeader()
    {
        string text = HeaderText();

        Assert.Equal(ImpactEffectPolicy.HeaderFloats,
            int.Parse(Regex.Match(text, @"SS_IMPACT_HEADER_COUNT\s*=\s*(\d+)").Groups[1].Value));
        Assert.Equal(ImpactEffectPolicy.BurstFloats,
            int.Parse(Regex.Match(text, @"SS_IMPACT_BURST_FIELDS\s*=\s*(\d+)").Groups[1].Value));
        Assert.Equal(ImpactEffectPolicy.MaxBursts,
            int.Parse(Regex.Match(text, @"#define SS_IMPACT_MAX_BURSTS\s+(\d+)").Groups[1].Value));
    }

    // ------------------------------------------------------------------
    // The decisions themselves.
    // ------------------------------------------------------------------

    private static float[] Block(ImpactEffect effect, int material = 0, float scale = 1.0f,
                                 bool noFlecks = false)
    {
        var block = new float[ImpactEffectPolicy.MaxFloats];
        int wrote = ImpactEffectPolicy.Build(effect, material, scale, noFlecks, block);

        Assert.True(wrote >= ImpactEffectPolicy.HeaderFloats,
            $"{effect} refused to answer a full-width block (wrote {wrote})");

        return block;
    }

    private static float Burst(float[] block, int burst, ImpactBurst field)
        => block[ImpactEffectPolicy.BurstAt(burst) + (int)field];

    /// <summary>
    /// THE FILE'S HEADLINE DECISION: wood gets the wood sheet.
    /// <c>CHAR_TEX_WOOD</c> is 'W'.
    /// </summary>
    [Fact]
    public void WoodGetsTheWoodFleckSheet()
        => Assert.Equal(ImpactMaterial.FleckWood, ImpactEffectPolicy.FleckSheet('W'));

    [Fact]
    public void ConcreteGetsTheCementFleckSheet()
        => Assert.Equal(ImpactMaterial.FleckCement, ImpactEffectPolicy.FleckSheet('C'));

    [Fact]
    public void TileGetsTheCementFleckSheetToo()
        => Assert.Equal(ImpactMaterial.FleckCement, ImpactEffectPolicy.FleckSheet('T'));

    /// <summary>
    /// A surface type this build has never seen still throws grey chips.
    /// Stock's own default arm, which is a decision and not an omission.
    /// </summary>
    [Fact]
    public void AnUnknownSurfaceStillThrowsCementChips()
        => Assert.Equal(ImpactMaterial.FleckCement, ImpactEffectPolicy.FleckSheet('?'));

    [Fact]
    public void ShootingWoodPutsTheWoodSheetInTheFleckBurst()
        => Assert.Equal((float)ImpactMaterial.FleckWood,
                        Burst(Block(ImpactEffect.DebrisFlecks, 'W'), 0, ImpactBurst.Material));

    [Fact]
    public void ShootingConcretePutsTheCementSheetInTheFleckBurst()
        => Assert.Equal((float)ImpactMaterial.FleckCement,
                        Burst(Block(ImpactEffect.DebrisFlecks, 'C'), 0, ImpactBurst.Material));

    /// <summary>
    /// FOUR TO SIXTEEN FLECKS. The count that was
    /// <c>random-&gt;RandomInt( 4, 16 )</c> in the emitter.
    /// </summary>
    [Fact]
    public void TheFleckCountIsFourToSixteen()
    {
        float[] b = Block(ImpactEffect.DebrisFlecks, 'C');
        Assert.Equal(4.0f, Burst(b, 0, ImpactBurst.CountMin));
        Assert.Equal(16.0f, Burst(b, 0, ImpactBurst.CountMax));
    }

    /// <summary>
    /// <c>r_drawflecks 0</c> suppresses the flecks with a COUNT OF ZERO and not
    /// with a missing burst, so the block's shape does not depend on a ConVar.
    /// </summary>
    [Fact]
    public void SuppressedFlecksAreACountOfZeroAndNotAMissingBurst()
    {
        float[] b = Block(ImpactEffect.DebrisFlecks, 'C', noFlecks: true);

        Assert.Equal(0.0f, Burst(b, 0, ImpactBurst.CountMin));
        Assert.Equal(0.0f, Burst(b, 0, ImpactBurst.CountMax));
        Assert.Equal(4.0f, b[(int)ImpactHeader.Bursts]);
    }

    /// <summary>Suppressing the flecks leaves the dust trail alone.</summary>
    [Fact]
    public void SuppressingFlecksDoesNotSuppressTheDustTrail()
        => Assert.Equal(2.0f, Burst(Block(ImpactEffect.DebrisFlecks, 'C', noFlecks: true),
                                    1, ImpactBurst.CountMin));

    [Fact]
    public void TheFleckGravityIsEightHundred()
        => Assert.Equal(800.0f, Block(ImpactEffect.DebrisFlecks, 'C')[(int)ImpactHeader.Gravity]);

    [Fact]
    public void TheFleckDampeningIsThreeTenths()
        => Assert.Equal(0.3f, Block(ImpactEffect.DebrisFlecks, 'C')[(int)ImpactHeader.Dampen]);

    /// <summary>
    /// MORE POWER MAKES THE SPRAY MORE CONTROLLED — the emitter narrows the
    /// 0.6 spray by 0.2 per unit of scale and never below 0.2.
    /// </summary>
    [Fact]
    public void TheFleckSprayNarrowsWithScaleAndHasAFloor()
    {
        float[] b = Block(ImpactEffect.DebrisFlecks, 'C');
        Assert.Equal(0.6f, b[(int)ImpactHeader.AngularSpray]);
        Assert.Equal(0.2f, b[(int)ImpactHeader.HeadA]);
        Assert.Equal(0.2f, b[(int)ImpactHeader.HeadB]);
    }

    [Fact]
    public void GlassBreaksIntoTwoToFourShards()
    {
        float[] b = Block(ImpactEffect.Glass);
        Assert.Equal(2.0f, Burst(b, 0, ImpactBurst.CountMin));
        Assert.Equal(4.0f, Burst(b, 0, ImpactBurst.CountMax));
    }

    /// <summary>Glass shards are near-white; the dust behind them is not.</summary>
    [Fact]
    public void GlassShardsAndGlassDustAreDifferentColours()
    {
        float[] b = Block(ImpactEffect.Glass);
        Assert.Equal(210.0f, Burst(b, 0, ImpactBurst.TintB));
        Assert.Equal(92.0f, Burst(b, 1, ImpactBurst.TintB));
    }

    [Fact]
    public void GlassShardsLiveTwoAndAHalfToFiveSeconds()
    {
        float[] b = Block(ImpactEffect.Glass);
        Assert.Equal(2.5f, Burst(b, 0, ImpactBurst.LifeMin));
        Assert.Equal(5.0f, Burst(b, 0, ImpactBurst.LifeMax));
    }

    [Fact]
    public void AntlionImpactsThrowEightToSixteenChitinFlecks()
    {
        float[] b = Block(ImpactEffect.Antlion);
        Assert.Equal(8.0f, Burst(b, 0, ImpactBurst.CountMin));
        Assert.Equal(16.0f, Burst(b, 0, ImpactBurst.CountMax));
    }

    [Fact]
    public void AntlionFlecksUseTheAntlionSheet()
        => Assert.Equal((float)ImpactMaterial.FleckAntlion,
                        Burst(Block(ImpactEffect.Antlion), 0, ImpactBurst.Material));

    /// <summary>
    /// Bug blood is yellow. Not ramped, not lit, not a surface colour — it is
    /// what the creature is, on all three of its bursts.
    /// </summary>
    [Fact]
    public void BugBloodIsYellowOnEveryBurst()
    {
        float[] b = Block(ImpactEffect.BugBlood);

        for (int burst = 0; burst < 3; burst++)
        {
            Assert.Equal(255.0f, Burst(b, burst, ImpactBurst.TintR));
            Assert.Equal(200.0f, Burst(b, burst, ImpactBurst.TintG));
            Assert.Equal(32.0f, Burst(b, burst, ImpactBurst.TintB));
        }
    }

    /// <summary>
    /// THIRTY-TWO, SIXTEEN AND SIXTEEN. Stock declared each of these twice, for
    /// two consoles; this build is neither, and the numbers are written once.
    /// </summary>
    [Fact]
    public void BugBloodSpraysThirtyTwoThenSixteenThenSixteen()
    {
        float[] b = Block(ImpactEffect.BugBlood);
        Assert.Equal(32.0f, Burst(b, 0, ImpactBurst.CountMin));
        Assert.Equal(16.0f, Burst(b, 1, ImpactBurst.CountMin));
        Assert.Equal(16.0f, Burst(b, 2, ImpactBurst.CountMin));
    }

    /// <summary>
    /// The gauss ring is sixteen to thirty-two sparks — the number
    /// <c>tools/7a-effect-gate</c> watches move on <c>cs_te_fire all</c>.
    /// </summary>
    [Fact]
    public void TheGaussRingIsSixteenToThirtyTwoSparks()
    {
        float[] b = Block(ImpactEffect.Gauss);
        Assert.Equal(16.0f, Burst(b, 0, ImpactBurst.CountMin));
        Assert.Equal(32.0f, Burst(b, 0, ImpactBurst.CountMax));
    }

    [Fact]
    public void TheGaussSparkSpeedIsOneTwentyEightToFiveTwelve()
    {
        float[] b = Block(ImpactEffect.Gauss);
        Assert.Equal(128.0f, Burst(b, 0, ImpactBurst.SpeedMin));
        Assert.Equal(512.0f, Burst(b, 0, ImpactBurst.SpeedMax));
    }

    /// <summary>
    /// FOUR OF EACH, IDEALLY — and the three rounding biases are what makes the
    /// most visible type the last one the particle throttle throws away.
    /// </summary>
    [Fact]
    public void TheMostVisibleDustBurstSurvivesTheThrottleLongest()
    {
        float[] b = Block(ImpactEffect.Dust);
        Assert.Equal(4.0f, b[(int)ImpactHeader.HeadA]);
        Assert.True(Burst(b, 1, ImpactBurst.ExtraA) > Burst(b, 0, ImpactBurst.ExtraA));
        Assert.True(Burst(b, 0, ImpactBurst.ExtraA) > Burst(b, 2, ImpactBurst.ExtraA));
    }

    /// <summary>Only the first dust burst scales its start size by the impact.</summary>
    [Fact]
    public void OnlyTheFirstDustBurstScalesItsSizeWithTheImpact()
    {
        float[] b = Block(ImpactEffect.Dust);
        Assert.NotEqual(0.0f, Burst(b, 0, ImpactBurst.ExtraB));
        Assert.Equal(0.0f, Burst(b, 1, ImpactBurst.ExtraB));
        Assert.Equal(0.0f, Burst(b, 2, ImpactBurst.ExtraB));
    }

    /// <summary>
    /// THE FIRST BLOOD PARTICLE DOES NOT MOVE. Stock multiplies the speed by
    /// the loop index rather than index+1, so one puff stays at the wound.
    /// </summary>
    [Fact]
    public void BothBloodBurstsScaleTheirSpeedByTheLoopIndex()
    {
        float[] b = Block(ImpactEffect.Blood);
        Assert.NotEqual(0.0f, Burst(b, 0, ImpactBurst.ExtraA));
        Assert.NotEqual(0.0f, Burst(b, 1, ImpactBurst.ExtraA));
    }

    /// <summary>Only the blood cloud sinks; the second sheet does not.</summary>
    [Fact]
    public void OnlyTheBloodCloudSinks()
    {
        float[] b = Block(ImpactEffect.Blood);
        Assert.Equal(-32.0f, Burst(b, 0, ImpactBurst.ZMin));
        Assert.Equal(0.0f, Burst(b, 1, ImpactBurst.ZMin));
        Assert.Equal(0.0f, Burst(b, 1, ImpactBurst.ZMax));
    }

    // ------------------------------------------------------------------
    // The refusals.
    // ------------------------------------------------------------------

    /// <summary>
    /// An effect id this side does not know is REFUSED, and native draws
    /// nothing on a refusal rather than drawing with whatever the buffer held.
    /// </summary>
    [Fact]
    public void AnUnknownEffectIdIsRefused()
        => Assert.Equal(-1, ImpactEffectPolicy.Build((ImpactEffect)99, 'C', 1.0f, false,
                                                     new float[ImpactEffectPolicy.MaxFloats]));

    /// <summary>A block too small for the answer is refused, not truncated.</summary>
    [Fact]
    public void ABlockTooSmallForTheAnswerIsRefused()
        => Assert.Equal(-1, ImpactEffectPolicy.Build(ImpactEffect.DebrisFlecks, 'C', 1.0f, false,
                                                     new float[ImpactEffectPolicy.HeaderFloats]));

    /// <summary>
    /// EVERY EFFECT FITS THE WIDEST BLOCK NATIVE ALLOCATES. A managed side that
    /// grew a burst past SS_IMPACT_MAX_BURSTS would be refused at runtime and
    /// draw nothing, which is a black impact rather than a crash — so it is
    /// caught here instead.
    /// </summary>
    [Fact]
    public void EveryEffectFitsTheBlockNativeAllocates()
    {
        foreach (ImpactEffect e in Enum.GetValues<ImpactEffect>())
        {
            var block = new float[ImpactEffectPolicy.MaxFloats];
            int wrote = ImpactEffectPolicy.Build(e, 'C', 1.0f, false, block);

            Assert.True(wrote > 0, $"{e} refused a full-width block");
            Assert.True(wrote <= ImpactEffectPolicy.MaxFloats,
                $"{e} wants {wrote} floats and native allocates {ImpactEffectPolicy.MaxFloats}");
        }
    }

    /// <summary>
    /// Every effect declares at least one burst. An effect with none would
    /// build, answer a positive length and draw nothing.
    /// </summary>
    [Fact]
    public void EveryEffectDeclaresAtLeastOneBurst()
    {
        foreach (ImpactEffect e in Enum.GetValues<ImpactEffect>())
            Assert.True(Block(e, 'C')[(int)ImpactHeader.Bursts] >= 1.0f,
                $"{e} declares no bursts at all");
    }

    /// <summary>
    /// Every burst of every effect names a count. A burst whose count range is
    /// zero at both ends draws nothing, and only the suppressed fleck burst is
    /// allowed to be that.
    /// </summary>
    [Fact]
    public void EveryBurstOfEveryEffectDrawsSomething()
    {
        foreach (ImpactEffect e in Enum.GetValues<ImpactEffect>())
        {
            float[] b = Block(e, 'C');
            int bursts = (int)b[(int)ImpactHeader.Bursts];

            for (int i = 0; i < bursts; i++)
                Assert.True(Burst(b, i, ImpactBurst.CountMax) >= 1.0f,
                    $"{e} burst {i} has a maximum count of {Burst(b, i, ImpactBurst.CountMax)}");
        }
    }
}
