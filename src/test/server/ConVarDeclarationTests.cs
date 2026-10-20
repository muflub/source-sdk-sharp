using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a C# convar declaration is allowed to say.
///
/// It used to be able to ask for six of the thirty-two FCVAR bits and nothing
/// else — no bounds, no hidden, no protected — while every convar the engine
/// itself declares could ask for all of them. These check that the gap is
/// closed: that a declaration carries the flags and the bounds it stated, all
/// the way through the registry that registers it.
///
/// The engine is absent here, so registration is refused and each convar binds
/// with an id of -1. That is the interesting half anyway: a refused convar keeps
/// what it declared, which is what makes any of this testable at all.
///
/// <para>
/// The bulk <c>[Fact]</c> these replace had an <c>if (bounded is null) return;</c>
/// two assertions in, so a registry that found nothing reported ONE failure and
/// silently skipped the other five. Each fact now resolves what it needs.
/// </para>
/// </summary>
public class ConVarDeclarationTests
{
    private static ConVarRegistry Registered()
    {
        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(ConVarDeclarationTests).Assembly);

        return registry;
    }

    private static BoundedTestConVar Bounded()
    {
        var bounded = Registered().Get<BoundedTestConVar>();

        Assert.True(bounded is not null, "a declared convar is found by its class");

        return bounded!;
    }

    [Fact]
    public void ADeclaredConVarIsFoundByItsClass()
    {
        var registry = Registered();

        Assert.True(registry.Get<BoundedTestConVar>() is not null && registry.Get<UnboundedTestConVar>() is not null, "a declared convar is found by its class");
    }

    [Fact]
    public void DeclaredBoundsAreKept()
    {
        var bounded = Bounded();

        Assert.True(bounded.MinValue == 0f && bounded.MaxValue == 100f, "declared bounds are kept");
    }

    [Fact]
    public void ZeroIsABoundLikeAnyOther()
    {
        // Zero as a minimum is the case a nullable exists for: a sentinel would
        // have to pick some number to mean "none", and this is a convar that
        // legitimately clamps to that number.
        Assert.True(Bounded().MinValue == 0f, "zero is a bound like any other");
    }

    [Fact]
    public void AConVarThatStatedNoBoundsHasNone()
    {
        var plain = Registered().Get<UnboundedTestConVar>();

        Assert.True(plain is not null, "the unbounded convar is found by its class");
        Assert.True(plain!.MinValue is null && plain.MaxValue is null, "a convar that stated no bounds has none");
    }

    [Fact]
    public void FlagsOutsideTheOldSubsetSurviveTheDeclaration()
    {
        // Flags no earlier version of this enum could name. The point is not
        // that these three do anything here — nothing is registered — but that
        // a declaration can ask for them at all.
        Assert.True(Bounded().Flags == (ConVarFlags.Hidden | ConVarFlags.NotConnected | ConVarFlags.Protected), "flags outside the old subset survive the declaration");
    }

    [Fact]
    public void ADeclarationsFlagsRenderLikeTheEngines()
        => Assert.True(ConVarFlagNames.Describe(Bounded().Flags) == "Hidden, Protected, NotConnected", "a declaration's flags render like the engine's");

    [Fact]
    public void TheValueDeclaredIsTheValueHeld()
    {
        var bounded = Bounded();

        Assert.True(bounded.Value == 50f && !bounded.IsRegistered, "the value declared is the value held");
    }
}
