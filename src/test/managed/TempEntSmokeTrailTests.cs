//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
extern alias client;
using RandomSeeds = client::SourceSharp.Abi.RandomSeeds;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's <c>FTENT_SMOKETRAIL</c> is unimplemented: `c_te_legacytempents.cpp:1029`
/// is `Assert( !"FIXME:  Rework smoketrail to be client side" )` and nothing
/// else. Handing the flag to native therefore only halts a debug client.
///
/// <para>
/// **The computation stays faithful and the handing over does not**, and these
/// pin both halves — because either one alone is a bug. Dropping the
/// computation would lose stock's `BREAK_SMOKE` + `random(0,255) &lt; 100` rule
/// with nothing recording it; passing the flag on crashes a breaking
/// `func_breakable`, which is what made this reachable in the first place
/// (nothing in this build had ever sent a `TE_BreakModel` until
/// `IEngineEffects` was rewired onto the managed pool).
/// </para>
/// </summary>
public class TempEntSmokeTrailTests
{
    /// <summary>A pool double that records exactly what crossed to native.</summary>
    private sealed class Recorder : EngineEffectsForwarder
    {
        public Recorder(IEngineEffects inner) : base(inner) { }

        public readonly List<TempEntSpawn> Handed = [];

        public override int TempEntPoolCapacity() => 500;

        public override bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
            => true;

        public override bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
        {
            Handed.Add(spawn);
            return true;
        }
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    private static (TempEntSpawns Spawns, Recorder Native) Rig()
    {
        var engine = TestServices.Fake();
        engine.Strict = false;
        engine.Returns("Precache.ModelFrameCount", 8);
        engine.Returns("Precache.ModelType", (int)ModelType.Studio);

        var native = new Recorder(engine);
        var clock = new Clock { Now = 100f };
        var pool = new ClientTempEntPool(new RecordingLog(), native, clock, new TempEntPoolTests.NoSimulation());

        return (new TempEntSpawns(new RecordingLog(), pool, engine, engine, clock, new RandomSeeds(1)), native);
    }

    [Fact]
    public void NoGibIsEverHandedTheFlagNativeOnlyAssertsOn()
    {
        var (spawns, native) = Rig();

        // Enough gibs that stock's 100-in-256 gate is overwhelmingly likely to
        // fire at least once; the assertion does not depend on it firing.
        for (int i = 0; i < 20; i++)
        {
            spawns.BreakModel(
                pos: Vector.Zero, angles: Vector.Zero,
                size: new Vector(32f, 32f, 32f), dir: new Vector(0f, 0f, 64f),
                randRange: 100f, life: 5f, count: 8, modelIndex: 3,
                flags: BreakFlags.Smoke);
        }

        Assert.True(native.Handed.Count > 0, "gibs were handed to native at all");

        Assert.True(native.Handed.All(s => (s.Flags & (int)TempEntFlags.SmokeTrail) == 0),
            "not one of them carried FTENT_SMOKETRAIL across the seam — native implements "
          + "that flag as an Assert and nothing else, so handing it over halts a debug client");
    }

    [Fact]
    public void ButTheRuleThatComputesItIsStillStocksOwn()
    {
        // Read off disk, which is the only thing that can tell "masked at the
        // seam" from "quietly stopped being computed". Both look identical from
        // outside the pool, and only one of them is faithful.
        string source = System.IO.File.ReadAllText(TempEntSpawnsSource());

        Assert.True(source.Contains("BreakFlags.Smoke"),
            "TempEntSpawns still asks whether the break carried BREAK_SMOKE");

        Assert.True(source.Contains("_random.Int(0, 255) < 100"),
            "and still gates it on stock's own random(0,255) < 100, which is the line "
          + "c_te_legacytempents.cpp:1557 spells the same way");

        Assert.True(source.Contains("TempEntFlags.SmokeTrail"),
            "and still SETS the flag — the masking is at the seam in ClientTempEntPool, so "
          + "deleting the rule here would leave every test above green while losing stock's "
          + "decision with nothing recording it");
    }

    private static string TempEntSpawnsSource()
        => RepoLayout.Sdk("src", "sourcesharp", "managed",
            "SourceSharp.Client", "TempEnts", "TempEntSpawns.cs");
}
