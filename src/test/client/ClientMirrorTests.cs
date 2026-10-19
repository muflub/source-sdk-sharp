extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Phase X4's two client mirrors: the cached entity index, and the
/// dynamic-light key ledger.
///
/// <para>
/// Both replace a per-frame question with a record managed already had the
/// answer to, so what these facts check is not "the number is right" — the
/// engine would agree either way — but that the QUESTION STOPPED BEING ASKED,
/// and that the record is filled and cleared at the two moments that make it
/// safe.
/// </para>
/// </summary>
public class ClientMirrorTests
{
    /// <summary>
    /// An engine that answers a fixed entity index, and nothing else it is not
    /// asked for.
    ///
    /// <para>
    /// <c>Entities.GetIndex</c> is the slot under test, so it is stubbed rather
    /// than ignored: a test counting how often it is called must be able to see
    /// every call, and an ignored member is still recorded but reads as one the
    /// test does not care about.
    /// </para>
    /// </summary>
    private static RecordingEngine EngineAt(int index)
        => Fake().Returns("Entities.GetIndex", index);

    private static ClientStandaloneRegistry RegistryOn(RecordingEngine engine,
                                                       out DynamicLights lights)
    {
        lights = new DynamicLights(engine);

        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(ClientDynamicLight).Assembly);
        registry.Services = new ServiceContainer()
            .AddInstance<IDynamicLights>(lights)
            .AddInstance<IEngineEntities>(engine)
            .AddInstance<IEngineEntityRender>(engine)
            .AddInstance<IEngineVisibility>(engine);

        return registry;
    }

    private static readonly EntityHandle Light = new(0x0701_0011u);

    //-------------------------------------------------------------------------
    // The index mirror.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheIndexIsReadOnceAtTheCreationPush()
    {
        var engine = EngineAt(42);
        var registry = RegistryOn(engine, out _);

        Assert.True(registry.Create(Light, EntityNames.LightDynamic), "the light is created");

        Assert.True(engine.CountOf("Entities.GetIndex") == 1,
            "creating an entity asks the engine for its index exactly once");
    }

    [Fact]
    public void TheEntityCarriesTheIndexItWasCreatedWith()
    {
        var engine = EngineAt(42);
        var registry = RegistryOn(engine, out _);

        registry.Create(Light, EntityNames.LightDynamic);

        Assert.True(registry.Get(Light)!.Index == 42,
            "the entity answers the index the engine gave at the creation push");
    }

    [Fact]
    public void ReadingTheIndexAgainAsksNobody()
    {
        var engine = EngineAt(42);
        var registry = RegistryOn(engine, out _);

        registry.Create(Light, EntityNames.LightDynamic);
        engine.Clear();

        var entity = registry.Get(Light)!;

        for (int i = 0; i < 100; i++)
            Assert.True(entity.Index == 42, "and answers the same every time");

        Assert.True(engine.CountOf("Entities.GetIndex") == 0,
            "a hundred reads of the index cross the boundary no times at all");
    }

    [Fact]
    public void ARemovedEntityForgetsItsIndex()
    {
        var engine = EngineAt(42);
        var registry = RegistryOn(engine, out _);

        registry.Create(Light, EntityNames.LightDynamic);

        var entity = registry.Get(Light)!;

        registry.Remove(Light);

        Assert.True(entity.Index == -1,
            "a removed entity answers -1 rather than an index another entity may now own");
    }

    [Fact]
    public void AnUncreatedEntityHasNoIndex()
    {
        // The state every ClientStandaloneEntity starts in, and the same answer
        // SourceSharp_ClientEntityGetIndex gives for a handle this side cannot
        // resolve.
        var entity = new ClientDynamicLight(new DynamicLights(Fake()), Fake());

        Assert.True(entity.Index == -1, "an entity no registry has created answers -1");
    }

    [Fact]
    public void ADarkSpotlightNeverAsksForItsIndex()
    {
        // dm_lockdown's shape: nine spotlight_ends whose point_spotlight carries
        // NoDynamicLight, so LightScale is zero and every frame took the release
        // branch — which read the index to build the key.
        var engine = EngineAt(7);
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.SpotlightEnd);
        registry.Get(Light)!.OnSpawn();

        engine.Clear();

        for (int frame = 0; frame < 60; frame++)
            lights.Frame();

        Assert.True(engine.CountOf("Entities.GetIndex") == 0,
            "sixty frames of a dark spotlight ask the engine for its index no times");
    }

    //-------------------------------------------------------------------------
    // The dynamic-light key ledger.
    //-------------------------------------------------------------------------

    [Fact]
    public void ADarkSpotlightNeverReleasesAKeyItNeverLit()
    {
        var engine = EngineAt(7);
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.SpotlightEnd);
        registry.Get(Light)!.OnSpawn();

        for (int frame = 0; frame < 60; frame++)
            lights.Frame();

        Assert.True(engine.CountOf("Visibility.DynamicLightRelease") == 0,
            "sixty frames of a dark spotlight put out a light no times: there was never one to put out");
    }

    [Fact]
    public void ReleasingAKeyNobodyLitCrossesNothing()
    {
        var engine = Fake();
        var lights = new DynamicLights(engine);

        lights.Release(11);

        Assert.True(engine.CountOf("Visibility.DynamicLightRelease") == 0,
            "a key this side never allocated is not the engine's problem");
    }

    [Fact]
    public void ReleasingALitKeyCrossesOnce()
    {
        var engine = Fake();
        var lights = new DynamicLights(engine);

        lights.Refresh(new DynamicLight { Key = 11 });
        lights.Release(11);

        Assert.True(engine.CountOf("Visibility.DynamicLightRelease") == 1,
            "a light that was lit is put out, which is what the crossing is for");
    }

    [Fact]
    public void ReleasingTheSameKeyTwiceCrossesOnce()
    {
        var engine = Fake();
        var lights = new DynamicLights(engine);

        lights.Refresh(new DynamicLight { Key = 11 });
        lights.Release(11);
        lights.Release(11);
        lights.Release(11);

        Assert.True(engine.CountOf("Visibility.DynamicLightRelease") == 1,
            "and only the first release crosses — the rest are about a key already out");
    }

    [Fact]
    public void ALightRelitAndPutOutAgainCrossesAgain()
    {
        var engine = Fake();
        var lights = new DynamicLights(engine);

        lights.Refresh(new DynamicLight { Key = 11 });
        lights.Release(11);
        lights.Refresh(new DynamicLight { Key = 11 });
        lights.Release(11);

        Assert.True(engine.CountOf("Visibility.DynamicLightRelease") == 2,
            "the ledger is a record of what is lit now, not of what was ever lit");
    }

    [Fact]
    public void TheLedgerSaysWhichKeysAreLit()
    {
        var engine = Fake();
        var lights = new DynamicLights(engine);

        lights.Refresh(new DynamicLight { Key = 11 });

        Assert.True(lights.IsLit(11) && !lights.IsLit(12),
            "the key that was refreshed is lit and one nobody asked for is not");
    }

    [Fact]
    public void ALevelChangeForgetsTheLedgerWithoutReleasing()
    {
        var engine = Fake();
        var lights = new DynamicLights(engine);

        lights.Refresh(new DynamicLight { Key = 11 });
        lights.Reset();

        Assert.True(!lights.IsLit(11),
            "the pool goes with the level, so a key remembered across one would be a claim about nothing");

        lights.Release(11);

        Assert.True(engine.CountOf("Visibility.DynamicLightRelease") == 0,
            "and the first release after a level change does not cross for a slot that no longer exists");
    }

    [Fact]
    public void ALitLightIsPutOutWhenItsEntityGoes()
    {
        var engine = EngineAt(7).Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f));
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.SpotlightEnd);

        var spot = (ClientSpotlightEnd)registry.Get(Light)!;
        spot.State.LightScale = 100f;
        spot.State.Tint = new Color32(255, 255, 255, 255);
        spot.OnSpawn();

        lights.Frame();

        Assert.True(lights.IsLit(7), "the spotlight has a pool slot");

        registry.Remove(Light);

        Assert.True(engine.CountOf("Visibility.DynamicLightRelease") == 1,
            "and it is handed back when the entity goes — the crossing the slot exists for");
    }

    [Fact]
    public void ALitLightGoingDarkCrossesExactlyOnceAndThenStops()
    {
        // The forced transition, in miniature: the same one the live gate makes
        // with TurnOff on a light_dynamic.
        var engine = EngineAt(7).Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f));
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.SpotlightEnd);

        var spot = (ClientSpotlightEnd)registry.Get(Light)!;
        spot.State.LightScale = 100f;
        spot.State.Tint = new Color32(255, 255, 255, 255);
        spot.OnSpawn();

        for (int frame = 0; frame < 10; frame++)
            lights.Frame();

        spot.State.LightScale = 0f;

        for (int frame = 0; frame < 60; frame++)
            lights.Frame();

        Assert.True(lights.Releases == 1,
            "sixty frames after a light goes dark carry exactly one release between them");
    }

    [Fact]
    public void ALitSpotlightStillRefreshesEveryFrame()
    {
        // The other half, and the one that would catch a ledger that swallowed
        // the update as well: what this phase removes is the polling RELEASE,
        // not the refresh a live light needs.
        var engine = EngineAt(7).Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f));
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.SpotlightEnd);

        var spot = (ClientSpotlightEnd)registry.Get(Light)!;
        spot.State.LightScale = 100f;
        spot.State.Tint = new Color32(255, 255, 255, 255);
        spot.OnSpawn();

        for (int frame = 0; frame < 60; frame++)
            lights.Frame();

        Assert.True(engine.CountOf("Visibility.DynamicLightUpdate") == 60,
            "a live light is still asked for once a frame, which is what keeps it from expiring");
    }

    [Fact]
    public void ALitLightKeysItsSlotByTheCachedIndex()
    {
        var engine = EngineAt(7).Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f));
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.SpotlightEnd);

        var spot = (ClientSpotlightEnd)registry.Get(Light)!;
        spot.State.LightScale = 100f;
        spot.State.Tint = new Color32(255, 255, 255, 255);
        spot.OnSpawn();

        lights.Frame();

        var light = engine.Last("Visibility.DynamicLightUpdate")!.Value.ArgAs<DynamicLight>(0);

        Assert.True(light.Key == 7,
            "the pool key is the entity index the creation push cached, not a fresh ask");
    }

    //-------------------------------------------------------------------------
    // The divergence check itself.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheDivergenceCheckAgreesWhenTheCacheIsRight()
    {
        var engine = EngineAt(42);
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.LightDynamic);

        var report = IndexMirrorCheck.Report(registry.Live(), engine, lights);

        Assert.True(report[^1] == "VERDICT ok",
            "a cache that matches the engine's answer reports ok");
    }

    [Fact]
    public void TheDivergenceCheckNamesAMirrorThatDrifted()
    {
        var engine = EngineAt(42);
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.LightDynamic);

        // The failure this check exists for: the engine's answer moves and the
        // mirror does not. Nothing in the shipping tree can do this — m_nEntIndex
        // has one writer and it runs once — which is exactly why the check has
        // to be given a divergence to find, or it is a check that cannot fail.
        engine.Returns("Entities.GetIndex", 43);

        var report = IndexMirrorCheck.Report(registry.Live(), engine, lights);

        Assert.True(report[^1] == "VERDICT 1 DIVERGED",
            "a mirror that no longer matches the slot is reported, not smoothed over");
        Assert.True(report.Any(l => l.Contains("cached index 42") && l.Contains("says 43")),
            "and the report names both numbers so a reader can tell which is wrong");
    }

    [Fact]
    public void TheDivergenceCheckReportsTheLedger()
    {
        var engine = EngineAt(42);
        var registry = RegistryOn(engine, out var lights);

        registry.Create(Light, EntityNames.LightDynamic);

        lights.Refresh(new DynamicLight { Key = 42 });
        lights.Release(42);

        var report = IndexMirrorCheck.Report(registry.Live(), engine, lights);

        Assert.True(report.Any(l => l.Contains("0 key(s) lit")
                                 && l.Contains("1 refresh crossing(s)")
                                 && l.Contains("1 release crossing(s)")),
            "the report carries the tallies the live gate reads a forced transition off");
    }
}
