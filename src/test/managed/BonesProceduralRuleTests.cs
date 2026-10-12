using System;
using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="ProceduralBoneRule"/>, the managed twin of
/// src/shared/native/procedural_bone_rule.h. The cases are the native
/// gtest's (src/test/native/procedural_bone_rule_test.cpp), one behaviour
/// each; its "constants agree with studio.h" case becomes an agreement with
/// the ported <see cref="BoneSetupProcedural.CalcProceduralBone"/> itself.
/// </summary>
public class BonesProceduralRuleTests
{
    private const int OrdinaryFlags = 0x100; // BONE_USED_BY_VERTEX_LOD0
    private const int ProcFlags = ProceduralBoneRule.BoneAlwaysProcedural | 0x100;

    [Fact]
    public void ConstantsAreStudioHeaderValues()
    {
        Assert.Equal((0x04, 1, 2, 3, 4, 5),
            (ProceduralBoneRule.BoneAlwaysProcedural, ProceduralBoneRule.ProcAxisInterp,
             ProceduralBoneRule.ProcQuatInterp, ProceduralBoneRule.ProcAimAtBone,
             ProceduralBoneRule.ProcAimAtAttach, ProceduralBoneRule.ProcJiggle));
    }

    private static StudioHdr ProceduralModel(int procType)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = OrdinaryFlags });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "proc", Parent = 0, Flags = ProcFlags });
        b.Procedurals.Add(new StudioHdrBuilder.ProceduralDef
        {
            Bone = 1,
            ProcType = procType,
            QuatInterp = procType == ProceduralBoneRule.ProcQuatInterp
                ? new StudioHdrBuilder.QuatInterpDef { Control = 0, Triggers = { new StudioHdrBuilder.QuatTriggerDef { InvTolerance = 1f } } } : null,
        });
        if (!StudioHdr.TryCreate(b.BuildWithProcedural(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    [Fact]
    public void AComputedKindIsOneCalcProceduralBoneWrites()
    {
        StudioHdr hdr = ProceduralModel(ProceduralBoneRule.ProcQuatInterp);
        CBoneAccessor bones = new(new[] { BoneMatrix3x4.Identity, BoneMatrix3x4.Identity });
        Assert.Equal((ProceduralBoneKind.Computed, true),
            (ProceduralBoneRule.Kind(hdr.BoneFlags(1), ProceduralBoneRule.ProcQuatInterp),
             BoneSetupProcedural.CalcProceduralBone(hdr, 1, bones)));
    }

    [Fact]
    public void AJiggleKindIsOneCalcProceduralBoneDeclines()
    {
        StudioHdr hdr = ProceduralModel(ProceduralBoneRule.ProcJiggle);
        CBoneAccessor bones = new(new[] { BoneMatrix3x4.Identity, BoneMatrix3x4.Identity });
        Assert.Equal((ProceduralBoneKind.Jiggle, false),
            (ProceduralBoneRule.Kind(hdr.BoneFlags(1), ProceduralBoneRule.ProcJiggle),
             BoneSetupProcedural.CalcProceduralBone(hdr, 1, bones)));
    }

    [Theory]
    [InlineData(ProceduralBoneRule.ProcQuatInterp)]
    [InlineData(ProceduralBoneRule.ProcJiggle)]
    [InlineData(0)]
    public void ABoneWithoutTheFlagIsOrdinaryWhateverItsProcTypeSays(int procType)
    {
        Assert.Equal(ProceduralBoneKind.None, ProceduralBoneRule.Kind(OrdinaryFlags, procType));
    }

    [Theory]
    [InlineData(ProceduralBoneRule.ProcAxisInterp)]
    [InlineData(ProceduralBoneRule.ProcQuatInterp)]
    [InlineData(ProceduralBoneRule.ProcAimAtBone)]
    [InlineData(ProceduralBoneRule.ProcAimAtAttach)]
    public void TheFourComputedKindsAreComputed(int procType)
    {
        Assert.Equal(ProceduralBoneKind.Computed, ProceduralBoneRule.Kind(ProcFlags, procType));
    }

    [Fact]
    public void JiggleIsItsOwnKindAndNotComputed()
    {
        Assert.Equal(ProceduralBoneKind.Jiggle, ProceduralBoneRule.Kind(ProcFlags, ProceduralBoneRule.ProcJiggle));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void AnUnknownProcTypeFallsBackToThePose(int procType)
    {
        Assert.Equal(ProceduralBoneKind.None, ProceduralBoneRule.Kind(ProcFlags, procType));
    }

    [Fact]
    public void AJiggleBoneMakesAModelNeedTheClock()
    {
        Assert.True(ProceduralBoneRule.NeedsRealTime(false, true, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoplayStillMakesAModelNeedTheClock(bool jiggle)
    {
        Assert.True(ProceduralBoneRule.NeedsRealTime(true, jiggle, false));
    }

    [Fact]
    public void AnIkChainMakesAModelNeedTheClock()
    {
        Assert.True(ProceduralBoneRule.NeedsRealTime(false, false, true));
    }

    [Fact]
    public void AModelWithNoneOfThemKeepsItsCache()
    {
        Assert.False(ProceduralBoneRule.NeedsRealTime(false, false, false));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AModelWithChainsWantsASolver(int chains)
    {
        Assert.True(ProceduralBoneRule.WantsIk(chains, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AModelWithNoChainsDoesNot(int chains)
    {
        Assert.False(ProceduralBoneRule.WantsIk(chains, false));
    }

    [Fact]
    public void AScaledModelOptsOutOfIkHoweverManyChainsItHas()
    {
        Assert.False(ProceduralBoneRule.WantsIk(4, true));
    }

    [Fact]
    public void ARagdollDoesNotSolveIk()
    {
        Assert.False(ProceduralBoneRule.SolvesIk(true, true));
    }

    [Fact]
    public void ALivingModelWithAContextSolvesIt()
    {
        Assert.True(ProceduralBoneRule.SolvesIk(true, false));
    }

    [Fact]
    public void NoContextSolvesNothing()
    {
        Assert.False(ProceduralBoneRule.SolvesIk(false, false));
    }
}
