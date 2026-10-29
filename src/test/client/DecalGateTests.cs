using SourceSharp.Abi;
using SourceSharp.Client.Flashlight;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client's decal gates, which are the callers' as they are stock's (lane
/// M1-C moved them out of SourceSharp_ClientDecalShoot): c_te_bspdecal.cpp,
/// c_te_worlddecal.cpp and c_te_footprint.cpp each test <c>r_decals</c> before
/// their DecalShoot, and <see cref="DecalConVarsWatch"/> reads the engine's
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

    private sealed class Slots : IClientConVarSlots
    {
        public readonly List<string> Watched = [];

        public int Watch(string name)
        {
            Watched.Add(name);
            return 1000 + Watched.Count;
        }

        public bool SetValue(string name, float value) => false;
    }

    [Fact]
    public void TheWatchReadsTheEnginesTwoConVarsByName()
    {
        var slots = new Slots();
        var watch = new DecalConVarsWatch(new WatchedConVarValues(slots, name => name == "r_decals" ? "1" : "0"));
        watch.Start();

        Assert.Equal(["r_decals", "r_drawmodeldecals"], slots.Watched);
        Assert.True(watch.Decals);
        Assert.False(watch.ModelDecals);
    }

    [Fact]
    public void AChangeToAWatchedConVarIsWhatTheNextDecalReads()
    {
        var slots = new Slots();
        var watch = new DecalConVarsWatch(new WatchedConVarValues(slots, _ => "1"));
        watch.Start();

        Assert.True(watch.OnChanged(1001, "0"), "r_decals' id is the watch's");
        Assert.False(watch.Decals);
        Assert.True(watch.ModelDecals);
        Assert.False(watch.OnChanged(77, "0"), "an id the watch does not hold is someone else's");
    }

    private sealed class NeverLow : SourceSharp.Client.Effects.IClientLowViolence
    {
        public bool IsLowViolence => false;
    }
}
