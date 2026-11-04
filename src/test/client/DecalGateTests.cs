using SourceSharp.Abi;
using SourceSharp.Client.Flashlight;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;
using SourceSharp.Client;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client's decal gates, which are the callers' as they are stock's (lane
/// M1-C moved them out of SourceSharp_ClientDecalShoot): c_te_bspdecal.cpp,
/// c_te_worlddecal.cpp and c_te_footprint.cpp each test <c>r_decals</c> before
/// their DecalShoot, and <see cref="DecalConVars"/> reads the engine's
/// two ConVars by name.
/// </summary>
public class DecalGateTests
{
    private static readonly DecalSettings Off = new(() => false, () => true);

    [Fact]
    public void ABspDecalDrawsWithDecalsOn()
    {
        var engine = Fake();
        new ClientTeBspDecal(engine, DecalSettings.On()).Play(new TeBspDecal { Index = 3, Entity = 0 });

        Assert.Equal(1, engine.CountOf("Effects.ClientDecalShoot"));
    }

    [Fact]
    public void ABspDecalDrawsNothingWithRDecalsOff()
    {
        var engine = Fake();
        new ClientTeBspDecal(engine, Off).Play(new TeBspDecal { Index = 3, Entity = 0 });

        Assert.Equal(0, engine.CountOf("Effects.ClientDecalShoot"));
    }

    [Fact]
    public void AWorldDecalAsksNothingWithRDecalsOff()
    {
        // Stock tests r_decals before it asks about low violence or the decal's name.
        var engine = Fake();
        new ClientTeWorldDecal(engine, Off, new NeverLow()).Play(new TeWorldDecal { Index = 3 });

        Assert.Empty(engine.Calls);
    }

    [Fact]
    public void AFootprintDrawsNothingWithRDecalsOff()
    {
        var engine = Fake();
        new ClientTeFootprintDecal(engine, Off).Play(new TeFootprintDecal { Index = 3, Entity = 1 });

        Assert.Equal(0, engine.CountOf("Effects.ClientDecalShoot"));
    }

    [Fact]
    public void AFootprintDrawsWithDecalsOn()
    {
        var engine = Fake();
        new ClientTeFootprintDecal(engine, DecalSettings.On()).Play(new TeFootprintDecal { Index = 3, Entity = 1 });

        Assert.Equal(1, engine.CountOf("Effects.ClientDecalShoot"));
    }

    [Fact]
    public void TheSettingsReadTheEnginesTwoConVars()
    {
        var settings = new DecalConVars(DeclaredConVars.Holding<RDecalsConVar>("1"), DeclaredConVars.Holding<RDrawModelDecalsConVar>("0"));

        Assert.True(settings.Decals);
        Assert.False(settings.ModelDecals);
    }

    [Fact]
    public void AChangeIsWhatTheNextDecalReads()
    {
        var decals = DeclaredConVars.Holding<RDecalsConVar>("1");
        var settings = new DecalConVars(decals, DeclaredConVars.Holding<RDrawModelDecalsConVar>("1"));

        decals.Hold("0");

        Assert.False(settings.Decals);
        Assert.True(settings.ModelDecals);
    }

    private sealed class NeverLow : SourceSharp.Client.Effects.IClientLowViolence
    {
        public bool IsLowViolence => false;
    }
}
