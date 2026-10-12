using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>IBaseClientDLL::InvalidateMdlCache</c> on the client — lane
/// <c>diag-visibility</c>.
///
/// <para>
/// <b>THE DEFECT THESE FACTS EXIST FOR.</b>
/// <c>CShellClientDLL::InvalidateMdlCache</c> walked
/// <c>ClientEntityList().FirstBaseEntity()</c>, which answers
/// <c>C_BaseEntity</c>. <c>C_StandaloneEntity</c> is not one, so on a map whose
/// entities are all of that hierarchy the loop ran to completion and touched
/// NOTHING — and said nothing, which is what made it invisible. Measured on
/// dm_lockdown: zero entities reached, no console line at all.
/// </para>
///
/// <para>
/// So the fact that matters is not "a flush happened". It is that the line the
/// flush prints CARRIES THE NUMBER THE WALK REACHED and NAMES the population
/// that was there to reach — because a reader has to be able to tell "the walk
/// found nothing because there is nothing" from "the walk found nothing and
/// here are the forty-one entities it missed". Those two states printed
/// identically before, and that is the whole bug class this lane is about.
/// </para>
/// </summary>
public class ClientMdlCacheFlushTests
{
    private static (ClientModule Module, RecordingEngine Engine) Build(int invalidated)
    {
        var engine = TestServices.Fake();
        engine.Returns("Entities.InvalidateMdlCacheAll", invalidated);

        // The walk is the only engine area this path uses, and the module holds
        // it in an internal field precisely so it can be handed a double —
        // Initialize builds a whole client, which a fact about one method has
        // no business doing.
        var module = new ClientModule(new ModuleEngine(new EngineSide(Side.Client))) { _mdlCacheWalk = engine };

        return (module, engine);
    }

    [Fact]
    public void AFlushAsksTheNativeWalkExactlyOnce()
    {
        var (module, engine) = Build(invalidated: 7);

        module.InvalidateMdlCache();

        Assert.Equal(1, engine.CountOf("Entities.InvalidateMdlCacheAll"));
    }

    [Fact]
    public void EveryFlushIsCounted()
    {
        var (module, _) = Build(invalidated: 7);

        module.InvalidateMdlCache();
        module.InvalidateMdlCache();

        Assert.Equal(2, module.MdlCacheInvalidations);
    }

    /// <summary>
    /// THE NUMBER THE WALK REACHED IS KEPT. A counter of flushes cannot tell a
    /// working walk from an empty one; this is the value that can.
    /// </summary>
    [Fact]
    public void TheModuleKeepsHowManyEntitiesTheWalkReached()
    {
        var (module, _) = Build(invalidated: 123);

        module.InvalidateMdlCache();

        Assert.Equal(123, module.MdlCacheEntitiesInvalidated);
    }

    /// <summary>
    /// AND ZERO IS KEPT AS ZERO. This is the exact state the client half was
    /// found in, and the one a "flushed" receipt hid.
    /// </summary>
    [Fact]
    public void AWalkThatReachedNothingIsRecordedAsZero()
    {
        var (module, _) = Build(invalidated: 0);

        module.InvalidateMdlCache();

        Assert.Equal(0, module.MdlCacheEntitiesInvalidated);
    }

    /// <summary>
    /// A flush before <c>Initialize</c> has no walk to ask. It answers 0 rather
    /// than throwing: unwinding into the engine's C++ frames is worse than a
    /// diagnostic that says it reached nothing.
    /// </summary>
    [Fact]
    public void AFlushWithNoWalkBoundAnswersZeroInsteadOfThrowing()
    {
        var module = new ClientModule(new ModuleEngine(new EngineSide(Side.Client)));

        module.InvalidateMdlCache();

        Assert.Equal(0, module.MdlCacheEntitiesInvalidated);
    }

    //=========================================================================
    // The line itself. Pure, so what the diagnostic SAYS is testable without a
    // running client — which is the only way to hold it to naming anything.
    //=========================================================================

    private static string Describe(int flush, int invalidated, int held,
                                   params (string, int)[] census)
        => ClientModule.DescribeMdlCacheFlush(flush, invalidated, held, census);

    [Fact]
    public void TheLineCarriesHowManyTheWalkInvalidated()
    {
        Assert.Contains("123 entities invalidated",
            Describe(1, 123, 41, ("ss_beam", 1)));
    }

    [Fact]
    public void TheLineCarriesWhichFlushThisWas()
    {
        Assert.Contains("model cache flushed (#4)",
            Describe(4, 123, 41, ("ss_beam", 1)));
    }

    /// <summary>
    /// THE NAMING, which is the point of the lane. A count alone is what
    /// `debug-api ents` already gives; what no instrument gave was the classes the
    /// flush was standing in front of.
    /// </summary>
    [Fact]
    public void TheLineNamesEveryClassTheDirectoryHolds()
    {
        string line = Describe(1, 123, 41, ("env_sprite", 23), ("ss_beam", 1));

        Assert.Contains("env_sprite(23)", line);
        Assert.Contains("ss_beam(1)", line);
    }

    [Fact]
    public void TheLineCarriesTheHeldPopulationAndTheClassCount()
    {
        string line = Describe(1, 123, 41, ("env_sprite", 23), ("ss_beam", 1));

        Assert.Contains("the directory holds 41 managed entities in 2 class(es)", line);
    }

    /// <summary>
    /// THE STATE THE BUG WAS: a walk that reached nothing while the directory
    /// held entities. The line has to make that legible, and the two halves
    /// have to disagree in it — 0 reached, 41 there.
    /// </summary>
    [Fact]
    public void AZeroReachedBesideANonEmptyDirectoryIsLegibleInTheLine()
    {
        string line = Describe(1, 0, 41, ("env_sprite", 23), ("ss_beam", 18));

        Assert.Contains("0 entities invalidated", line);
        Assert.Contains("the directory holds 41 managed entities", line);
    }

    [Fact]
    public void AnEmptyDirectoryNamesNoClassesAndSaysSo()
    {
        string line = Describe(1, 0, 0);

        Assert.Contains("in 0 class(es)", line);
        Assert.DoesNotContain("(0):", line);
        Assert.EndsWith("class(es)\n", line);
    }

    /// <summary>
    /// ONE entity is "entity", not "entities". A diagnostic read by a human at
    /// 2am is worth the ternary.
    /// </summary>
    [Fact]
    public void OneInvalidatedEntityIsSingular()
    {
        Assert.Contains("1 entity invalidated", Describe(1, 1, 1, ("ss_beam", 1)));
    }

    [Fact]
    public void OneHeldEntityIsSingular()
    {
        Assert.Contains("holds 1 managed entity in", Describe(1, 1, 1, ("ss_beam", 1)));
    }

    /// <summary>
    /// The classes are SORTED, so two runs of the same map are diffable. A
    /// dictionary's key order is not a promise.
    /// </summary>
    [Fact]
    public void TheClassesAreOrderedByName()
    {
        string line = Describe(1, 3, 3, ("ss_weapon", 1), ("env_sprite", 1), ("move_rope", 1));

        Assert.Contains("env_sprite(1), move_rope(1), ss_weapon(1)", line);
    }

    /// <summary>
    /// The first class is introduced with a colon and the rest with commas —
    /// checked because a naive join produces "class(es), env_sprite(1)", which
    /// reads as a class named nothing.
    /// </summary>
    [Fact]
    public void TheFirstClassFollowsAColonAndTheRestFollowCommas()
    {
        string line = Describe(1, 2, 2, ("env_sprite", 1), ("ss_beam", 1));

        Assert.Contains("class(es): env_sprite(1), ss_beam(1)", line);
    }
}
