using SourceSharp.Abi;
using SourceSharp.Client.MaterialProxies;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The providers' seam: a mod's proxies are asked after the core's table, the
/// core's names are unchanged by their presence, an unknown name is still null,
/// and a name two sides define is refused (a material's answer must not depend
/// on the order its definers loaded).
/// </summary>
public class MaterialProxyProviderFacts
{
    private sealed class Provider(string name, MaterialProxy? answer) : IMaterialProxyProvider
    {
        public IReadOnlyCollection<string> Names => [name];

        public MaterialProxy? Create(string asked) => asked == name ? answer : null;
    }

    private sealed class NamedProxy : MaterialProxy
    {
        public override bool Init(IProxyInit init) => true;

        public override void Bind(IProxyBind bind)
        {
        }
    }

    private static MaterialProxyFactory Factory(params IMaterialProxyProvider[] providers)
        => new(new StandardErrorLog(), new StockProxyMath(), () => false, null, null,
               () => providers);

    [Fact]
    public void AProviderProxyIsCreatedByItsName()
    {
        var proxy = new NamedProxy();
        var factory = Factory(new Provider("ModelGlowColor", proxy));

        Assert.Same(proxy, factory.Create("ModelGlowColor"));
    }

    [Fact]
    public void TheCoreNamesAreUnchangedByAProvider()
    {
        var factory = Factory(new Provider("ModelGlowColor", new NamedProxy()));

        Assert.IsType<EqualsProxy>(factory.Create("Equals"));
        Assert.IsType<AddProxy>(factory.Create("Add"));
    }

    [Fact]
    public void AnUnknownNameIsStillNull()
        // "PlayerLogoOnModel" is a name stock exposes and this core declaredly
        // refuses (its EXPOSE_INTERFACE sits inside stock's "we don't want
        // people to use it" block), so neither side of this seam answers it.
        => Assert.Null(Factory(new Provider("ModelGlowColor", new NamedProxy())).Create("PlayerLogoOnModel"));

    [Fact]
    public void AProviderIsAskedOnlyWhenTheCoreDoesNotKnowTheName()
    {
        var provider = new Provider("Equals", new NamedProxy());

        // "Equals" is the core's; the provider is never reached, and its
        // presence changes nothing about the core's answer.
        Assert.IsType<EqualsProxy>(Factory(provider).Create("Equals"));
    }

    [Fact]
    public void ANameBothTheCoreAndAProviderDefineIsRefused()
    {
        var factory = Factory(new Provider("Equals", new NamedProxy()));

        var refusal = Assert.Throws<InvalidOperationException>(() => factory.Create("ModelGlowColor"));
        Assert.Contains("Equals", refusal.Message);
        Assert.Contains("Provider", refusal.Message);
    }

    [Fact]
    public void ANameTwoProvidersDefineIsRefused()
    {
        var factory = Factory(new Provider("ModelGlowColor", new NamedProxy()),
                              new Provider("ModelGlowColor", new NamedProxy()));

        var refusal = Assert.Throws<InvalidOperationException>(() => factory.Create("ModelGlowColor"));
        Assert.Contains("ModelGlowColor", refusal.Message);
    }
}
