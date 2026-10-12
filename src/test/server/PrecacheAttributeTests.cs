using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A precache that is not done is a missing model or a silent sound at runtime,
/// never a build error — so the whole value of declaring it on the class is that
/// a test can see it. These are that test.
/// </summary>
public class PrecacheAttributeTests
{
    /// <summary>
    /// The server's game assembly: since plan_side_isolation P7 the shared
    /// source (the temp entities and their models) compiles into it beside the
    /// server's own (env_spark and its sound), so ONE assembly carries both.
    /// </summary>
    private static Assembly[] Game => [TestServices.ServerGame];

    [Fact]
    public void EveryAssetStockPrecachedForATempEntityIsStillDeclared()
    {
        var declared = PrecacheRegistry.Declared(new RecordingLog(), Game)
            .Select(d => (d.Kind, d.Asset))
            .ToHashSet();

        // Stock's four, from the Precache virtuals of the classes that had them.
        (PrecacheKind Kind, string Asset, string Stock)[] required =
        [
            (PrecacheKind.Model, "sprites/gunsmoke.vmt",   "CTESprite::Precache"),
            (PrecacheKind.Model, "sprites/bubble.vmt",     "CTEFizz::Precache"),
            (PrecacheKind.Model, "models/gibs/hgibs.mdl",  "CTEBreakModel/CTEPhysicsProp::Precache"),
            (PrecacheKind.Sound, "DoSpark",                "CEnvSpark::Precache"),
        ];

        foreach (var (kind, asset, stock) in required)
        {
            Assert.True(declared.Contains((kind, asset)),
                $"{stock} precached '{asset}' and some class still declares it — evicting the "
              + "C++ took its Precache with it, and nothing but this says so");
        }
    }

    [Fact]
    public void DoSparkIsDeclaredOnTheClassRatherThanInSomeMethodThatCanReturnEarly()
    {
        var onEnvSpark = typeof(EnvSpark)
            .GetCustomAttributes<PrecacheAttribute>(inherit: false)
            .Any(a => a.Kind == PrecacheKind.Sound && a.Asset == "DoSpark");

        Assert.True(onEnvSpark,
            "env_spark declares DoSpark on the class. It used to precache it inside OnSpawn, "
          + "BEFORE a StartOn check that returns early for a spark which starts off — so the "
          + "sound was precached for exactly the sparks that were already audible. An "
          + "attribute has no early return, which is the whole reason this moved");
    }

    [Fact]
    public void TheSweepPrecachesEachAssetOnceAndSaysHowMany()
    {
        var engine = Fake();
        engine.Strict = false;

        int count = ServerPrecache.PrecacheAll(engine, new RecordingLog(), Game);

        Assert.True(count > 0,
            $"the sweep found something ({count}) — a zero is indistinguishable from a sweep "
          + "that ran over the wrong assembly, so it is asserted rather than assumed");

        // hgibs is declared by BOTH TeBreakModel and TePhysicsProp, exactly as
        // two stock classes precached it. The engine's own precache is
        // idempotent, so this is about the COUNT being trustworthy.
        int hgibs = engine.AllOf("Precache.PrecacheModel")
                          .Count(c => c.Args.Contains("models/gibs/hgibs.mdl"));

        Assert.True(hgibs == 1,
            $"an asset two classes declare is precached once ({hgibs}) — otherwise the count "
          + "cannot be used as a check");

        Assert.True(PrecacheRegistry.Declared(new RecordingLog(), Game).Count(d => d.Asset == "models/gibs/hgibs.mdl") == 2,
            "and both classes really do declare it, so the de-duplication is doing something");
    }

    [Fact]
    public void BothAssembliesAreScannedBecauseTheAssetsLiveInBoth()
    {
        // Both kinds of source, now that they are one assembly: the shared
        // source's classes and the server's own each declare their assets.
        var owners = PrecacheRegistry.Declared(new RecordingLog(), Game)
            .ToLookup(d => d.Owner);

        Assert.True(owners[typeof(TeBreakModel)].Any(),
            "the temp entity models are declared in the shared source, beside their declarations");

        Assert.True(owners[typeof(EnvSpark)].Any(),
            "and env_spark's sound in the server's, beside EnvSpark — a sweep that missed "
          + "either source would silently miss its assets, which is the failure this exists to end");
    }
}
