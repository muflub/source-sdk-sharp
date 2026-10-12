using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.4: a model's auto-emitting particles, from its
/// $keyvalues, as Precache_ModelComponents read them. Held live to the native
/// parse on dm_lockdown (620 model precaches, every per-model count equal; no
/// model there emits any), so the non-empty rules are pinned here.
/// </summary>
public class ModelParticlesTests
{
    [Fact]
    public void EachEntrysNameIsPrecachedInOrder()
        => Assert.Equal(new[] { "fire_small", "smoke_trail" },
                        ModelParticles.Names(
                            "mdlkeyvalue { Particles { effect { name fire_small attachment_point a } " +
                            "effect { name smoke_trail } } prop_data { health 5 } }"));

    [Fact]
    public void AnEntryWithNoNameIsPrecachedAsTheEmptyString()
        => Assert.Equal(new[] { "" }, ModelParticles.Names("mdlkeyvalue { particles { effect { attachment_type follow_origin } } }"));

    [Fact]
    public void NoTextNoSectionOrNoParseIsNothing()
    {
        Assert.Empty(ModelParticles.Names(null));
        Assert.Empty(ModelParticles.Names("mdlkeyvalue { prop_data { health 5 } }"));
        Assert.Empty(ModelParticles.Names("mdlkeyvalue { Particles { effect { name x } }"));
    }

    [Fact]
    public void OnlyTheTextsFirstRootIsSearched()
        => Assert.Empty(ModelParticles.Names("first { } mdlkeyvalue { Particles { e { name x } } }"));
}
