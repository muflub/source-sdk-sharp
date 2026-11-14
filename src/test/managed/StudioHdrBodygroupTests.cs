using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the body-parts read and the two stock helpers over it
/// (<c>::SetBodygroup</c> animation.cpp:863-879,
/// <c>FindBodygroupByName</c> :927-943): the header walk through the same
/// image seam the bone tests build, with the digits of one group never
/// touching another group's.
/// </summary>
public class StudioHdrBodygroupTests
{
    /// <summary>The one-bone image, bodyparts after the header — the seam
    /// every bone fact reads, with the groups on top of it.</summary>
    private static StudioHdr Hdr(params StudioHdrBuilder.BodyPartDef[] parts)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1 });
        foreach (StudioHdrBuilder.BodyPartDef p in parts)
        {
            b.BodyParts.Add(p);
        }

        Assert.True(StudioHdr.TryCreate(b.Build(), null, out StudioHdr? hdr) && hdr is not null, "image rejected");
        return hdr!;
    }

    private static StudioHdr TwoGroups() => Hdr(
        new StudioHdrBuilder.BodyPartDef { Name = "mini_sentry_light", NumModels = 2 },
        new StudioHdrBuilder.BodyPartDef { Name = "blur", NumModels = 2, Base = 2 });

    [Fact]
    public void FindBodygroupByNameAnswersEachGroupsIndex()
    {
        StudioHdr hdr = TwoGroups();

        Assert.Equal(0, hdr.FindBodygroupByName("mini_sentry_light"));
        Assert.Equal(1, hdr.FindBodygroupByName("blur"));
    }

    [Fact]
    public void FindBodygroupByNameFoldsCaseAsStockDoes()
    {
        StudioHdr hdr = TwoGroups();

        // Q_strcasecmp: the ASCII fold, the answer the same index.
        Assert.Equal(0, hdr.FindBodygroupByName("MINI_SENTRY_LIGHT"));
    }

    [Fact]
    public void ANameNoGroupCarriesHasNoIndex()
    {
        StudioHdr hdr = TwoGroups();

        Assert.Equal(-1, hdr.FindBodygroupByName("shadow"));
    }

    [Fact]
    public void SettingTwoGroupsLeavesEachOthersDigitInPlace()
    {
        StudioHdr hdr = TwoGroups();

        // Light on: the lone digit of the one-model-wide first group.
        int light = hdr.SetBodygroup(0, 0, 1);
        Assert.Equal(1, light);

        // Blur on: the second digit rides above the first, untouched.
        int both = hdr.SetBodygroup(light, 1, 1);
        Assert.Equal(3, both);

        // Light off: the second digit keeps its place.
        int blurOnly = hdr.SetBodygroup(both, 0, 0);
        Assert.Equal(2, blurOnly);
    }

    [Fact]
    public void AValuePastTheGroupsModelsLeavesTheBodyAsItWas()
    {
        StudioHdr hdr = Hdr(new StudioHdrBuilder.BodyPartDef { Name = "mini_sentry_light", NumModels = 2 });

        // Stock's iValue >= nummodels arm — the boundary value included.
        Assert.Equal(1, hdr.SetBodygroup(1, 0, 2));
    }

    [Fact]
    public void AGroupOutsideTheHeaderCountLeavesTheBodyAsItWas()
    {
        StudioHdr hdr = TwoGroups();

        Assert.Equal(1, hdr.SetBodygroup(1, 2, 0));    // numbodyparts is 2
        Assert.Equal(1, hdr.SetBodygroup(1, -1, 0));   // stock's lower-bound arm too
    }
}
