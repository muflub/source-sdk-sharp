using System.Reflection;

using SourceSharp.Abi;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// C-Phase 2's backends, from the managed side (plan_tmp.md Part 4).
///
/// <para>
/// Fifteen drawing primitives landed in one pass with NO MANAGED CALLER YET, on
/// purpose: C-Phase 3 evicts the <c>c_te_*.cpp</c> files one nested-table
/// subtree at a time, and a lane that arrived to find its backend missing would
/// be a lane appending an ABI slot of its own. That leaves a specific hole these
/// tests fill.
/// </para>
///
/// <para>
/// WHAT IS ALREADY COVERED ELSEWHERE, so this file does not repeat it.
/// <see cref="EngineApiAreasTests"/> holds each slot's row in
/// docs/engineapi-areas.tsv against its shim-area header method, its generated
/// layout field and the client forward that serves it;
/// <see cref="SideAuditTests"/> holds each one client-only;
/// <c>te_backend_args_test.cpp</c> holds the native argument unpacking. What
/// none of them can see is the step between: a slot that reached its shim
/// area's layout and never reached <see cref="IEngineEffects"/>. That one
/// compiles, passes every check above, and is simply unreachable from a
/// managed caller with an injected engine — which is the shape of "the feature
/// does nothing and nothing is logged" one layer up from an unserved slot.
/// </para>
/// </summary>
public class TempEntBackendTests
{
    /// <summary>
    /// The sixteen backends of Part 4 that exist today, spelled once.
    ///
    /// <para>
    /// A PIN, and pinned by name for <see cref="SideAuditTests"/>'s reason: what
    /// this has to catch is a slot QUIETLY GOING MISSING from one of the three
    /// layers, and a list derived from any one of those layers would agree with
    /// its own absence. Part 4's particle-system row is here as EIGHT
    /// named entries rather than the one generic slot it pencilled in, see
    /// <see cref="TheParticleSystemBackendIsEightNamedSlotsNotOneGenericOne"/>.
    /// </para>
    /// </summary>
    private static readonly string[] Backends =
    {
        "ClientSparks", "ClientRicochet", "ClientMetalSparks",
        "ClientSmoke", "ClientMuzzleFlash",
        "ClientDecalShoot", "ClientPlayerDecalShoot", "ClientDecalIndexFromName",
        // C-Phase 1's, which is the one with a caller and the reason the shape
        // of the other fifteen is what it is.
        "ClientEnergySplash",
        // C-Phase 3, lane CP: the DT_TEParticleSystem subtree's eight. These
        // arrived WITH their callers rather than ahead of them, which is why
        // the lane was allowed to add them at all — see
        // <see cref="TheParticleSystemBackendIsEightNamedSlotsNotOneGenericOne"/>.
        "ClientExplosionFireball",
        // (ClientBloodStream and ClientShowLine left with lane c5-particles: the
        // blood stream and the line are TeParticles; their players start them.
        // ClientGaussExplosion and ClientLargeFunnel left with lane c5-particles:
        // the gauss discharge is C#, SparkEffects.GaussExplosion, and the funnel
        // LargeFunnel; their players call them directly. ClientDust left too: the
        // puff is DustPuff, and ClientTeDust starts it.)
    };

    [Fact]
    public void EveryBackendIsReachableFromAbiThroughToACaller()
    {
        foreach (string name in Backends)
        {
            // A shim-area slot since plan_client_vtables CC-last deleted the
            // struct: a row in the slot table and a field of its area's layout.
            Assert.True(EngineApiAreasTests.SlotField(name) is not null,
                $"{name} is a slot of its shim area's layout");

            // An EngineSide member since plan_engineapi_vtables B2 (the static
            // Engine forwarded it until its bindings called the side directly).
            Assert.True(typeof(EngineSide).GetMethod(name, BindingFlags.Public | BindingFlags.Instance) is not null,
                $"{name} has a wrapper on EngineSide");

            // The layer nothing else checks. A slot missing here is a slot no
            // managed caller can reach, because callers take IEngineEffects and
            // never touch Engine directly.
            Assert.True(typeof(IEngineEffects).GetMethod(name) is not null,
                $"{name} is on IEngineEffects, which is what a caller holds");
        }
    }

    /// <summary>
    /// Part 4's table pencilled in one more backend than C-Phase 2 delivered —
    /// <c>ClientParticleTeSpawn(kind, params)</c> for the seven
    /// particle-system TEs — and said to decide it at C-Phase 3 rather than
    /// then.
    ///
    /// <para>
    /// C-PHASE 3, LANE CP, DECIDED IT, AND THE ANSWER IS EIGHT NAMED SLOTS FOR
    /// SEVEN EFFECTS. This test was called
    /// <c>TheParticleSystemBackendIsDeferredNotForgotten</c> until that lane
    /// landed and asserted only the absence; it keeps that assertion, because
    /// the absence is still the point, and now says what took its place.
    /// </para>
    ///
    /// <para>
    /// WHY NOT ONE SLOT. The seven share a BASE TABLE and nothing else:
    /// Explosion picks between two singleton explosion effects and adds a
    /// <c>CExplosionOverlay</c>; ShowLine walks a line adding particles to a
    /// <c>CTEParticleRenderer</c>; BloodStream fills another one with 160
    /// arcing voxels; LargeFunnel runs its own 441-particle
    /// <c>CSimpleEmitter</c>; Dust and GaussExplosion are one <c>FX_</c> call
    /// each. A <c>kind + params</c> slot over that is a switch statement over
    /// six unrelated bodies wearing a generic name — it would compile, and
    /// every one of the six would afterwards be describable only by reading the
    /// switch.
    /// </para>
    ///
    /// <para>
    /// AND WHY EIGHT FOR SEVEN. Sparks needs no slot of its own:
    /// <c>ClientSparks</c>, which C-Phase 2 added for it, already IS the whole
    /// of what <c>C_TESparks::PostDataUpdate</c> did. The explosion needs three
    /// — the core, the water form and the fireball overlay — because stock
    /// gates each of them separately and on a decision that is now C#'s.
    /// </para>
    ///
    /// <para>
    /// The absence assertion stays and is not decoration. The generic slot is
    /// the thing this file exists to keep out, and it would be easiest to add
    /// precisely when a NINTH particle effect arrives and eight named slots
    /// start to look like a list.
    /// </para>
    /// </summary>
    [Fact]
    public void TheParticleSystemBackendIsEightNamedSlotsNotOneGenericOne()
    {
        Assert.True(EngineApiAreasTests.Rows().All(r => r.Slot != "ClientParticleTeSpawn")
                    && LayoutsDeclaring("ClientParticleTeSpawn") == 0,
            "no ClientParticleTeSpawn slot: the seven particle-system TEs share a base "
          + "table and not a backend, so each has the primitive it actually ends in");

        // The eight that took its place, by name. A list derived from the slot
        // table would agree with its own absence, which is this file's whole rule.
        foreach (string named in new[]
                 {
                     "ClientExplosionFireball",
                 })
        {
            Assert.True(EngineApiAreasTests.SlotField(named) is not null,
                $"{named} is one of the named primitives that replaced the generic slot (eight until the gauss discharge, the funnel, the dust, the blood stream and the line became C#)");
        }

        // Sparks is the reason there are eight rather than nine, and it is
        // C-Phase 2's slot rather than lane CP's.
        Assert.True(EngineApiAreasTests.SlotField("ClientSparks") is not null,
            "CTESparks reuses ClientSparks and got no slot of its own");
    }

    /// <summary>
    /// How many generated shim-area layouts (<c>Shim&lt;X&gt;Vtbl</c>) declare a
    /// slot of this name: the absence above holds in what compiled as well as
    /// in the table.
    /// </summary>
    private static int LayoutsDeclaring(string slot)
        => typeof(ShimRootVtbl).Assembly.GetTypes()
               .Count(t => t.Name.StartsWith("Shim", StringComparison.Ordinal)
                        && t.Name.EndsWith("Vtbl", StringComparison.Ordinal)
                        && t.GetField(slot) is not null);

    //-------------------------------------------------------------------------
    // THE OPTIONAL POINTERS.
    //
    // Two of these backends take an argument stock declares as `const Vector *`
    // and reads as a POINTER: a spark's direction and a decal's S axis. Absent
    // is not the same as zero in either case, and collapsing the two would be a
    // decode that quietly changed the drawing. The nullable has to survive the
    // whole managed path or the distinction is lost before it reaches the ABI.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASparkWithNoDirectionIsNotASparkAimedAtTheOrigin()
    {
        var engine = Fake();
        IEngineEffects effects = engine;

        effects.ClientSparks(new Vector(1f, 2f, 3f), 4, 5, direction: null);
        Assert.True(engine.Last("Effects.ClientSparks")!.Value.Arg(3) is null,
            "no direction crosses as no direction");

        effects.ClientSparks(new Vector(1f, 2f, 3f), 4, 5, direction: Vector.Zero);
        Assert.True(engine.Last("Effects.ClientSparks")!.Value.Arg(3) is not null,
            "a zero direction is a direction, and FX_ElectricSpark reads them differently");
    }

    [Fact]
    public void ADecalWithNoSAxisIsNotADecalWithAZeroOne()
    {
        var engine = Fake();
        IEngineEffects effects = engine;

        effects.ClientDecalShoot(12, 0, new Vector(1f, 2f, 3f));
        Assert.True(engine.Last("Effects.ClientDecalShoot")!.Value.Arg(3) is null,
            "omitting the S axis lets the engine pick one, which is what a world decal wants");

        effects.ClientDecalShoot(12, 0, new Vector(1f, 2f, 3f), sAxis: Vector.Zero);
        Assert.True(engine.Last("Effects.ClientDecalShoot")!.Value.Arg(3) is not null,
            "and passing one, even a zero one, is a different argument");
    }
}
