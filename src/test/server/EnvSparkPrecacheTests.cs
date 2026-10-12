using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Behaviour ledger row S7, pinned where it can fail.
///
/// <para>
/// Stock's <c>CEnvSpark::Precache</c> (<c>EnvSpark.cpp:114</c>) is a separate
/// virtual the engine calls on every spark, unconditionally. This shell has no
/// <c>Precache</c> of its own, so the only hook is <c>OnSpawn</c> — and
/// <c>OnSpawn</c> returns early for a spark that does not start on, which is
/// most of them. **The placement is the fix, not the call**, so that is what
/// these assert.
/// </para>
///
/// <para>
/// Without it every audible spark logged <c>SV_StartSound: … not precached (0)</c>
/// and played nothing. Not introduced by the temp entity port — the native shim
/// emitted the same name through the same call — but Part 8's <c>SparkEffect</c>
/// row says "+ the existing <c>DoSpark</c> sound", which is why anyone looked.
/// </para>
/// </summary>
public class EnvSparkPrecacheTests
{
    private const string TheSound = "DoSpark";

    [Fact]
    public void ASparkThatStartsOffStillPrecachesItsSound()
    {
        // It is an ATTRIBUTE now, so this is true by construction rather than by
        // where a line sits. The old form precached inside OnSpawn, and OnSpawn
        // returns early for a spark that does not start on — which is most of
        // them — so the placement was the entire fix and one edit away from
        // being undone. PrecacheAttributeTests owns the sweep; this keeps row
        // S7's own statement where somebody looking up the row will find it.
        var declared = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<PrecacheAttribute>(typeof(SourceSharp.Server.EnvSpark), false);

        Assert.True(declared.Any(a => a.Kind == PrecacheKind.Sound && a.Asset == TheSound),
            "env_spark declares DoSpark on the class, so no early return in any method can "
          + "skip it — which is what row S7 was: every audible spark logged 'not precached' "
          + "and played silence");
    }

    /// <summary>
    /// AND THE SWEEP REALLY REACHES THE ENGINE WITH IT, which the declaration
    /// test above cannot say.
    ///
    /// <para>
    /// Rewriting this row onto an attribute replaced an assertion that
    /// <c>PrecacheSound</c> was CALLED with one that the attribute is WRITTEN,
    /// and those are different claims. Nothing else in the suite calls
    /// <c>PrecacheSound</c> at all: the sound arm of PrecacheAll's switch was
    /// covered by no test, so sending every sound to <c>PrecacheModel</c> would
    /// have left the whole suite green and every spark silent -- which is
    /// exactly the symptom row S7 opened with.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSweepHandsDoSparkToPrecacheSoundAndNotToSomeOtherArm()
    {
        var engine = Fake();
        engine.Strict = false;

        ServerPrecache.PrecacheAll(engine, new RecordingLog(), typeof(EnvSpark).Assembly);

        Assert.True(engine.AllOf("Precache.PrecacheSound").Any(c => c.Args.Contains(TheSound)),
            "the sweep precaches DoSpark as a SOUND -- it reached "
          + $"[{string.Join(", ", engine.AllOf("Precache.PrecacheSound").SelectMany(c => c.Args))}]");

        Assert.True(!engine.AllOf("Precache.PrecacheModel").Any(c => c.Args.Contains(TheSound)),
            "and not as a model, which is the way a miswired switch arm would still have "
          + "logged a precache and still played silence");
    }
}

