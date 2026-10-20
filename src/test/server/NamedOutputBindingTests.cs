using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// An output named apart from its field, on a class the registry cannot build
/// without its services: the shape of trigger_*'s <c>OnLeft = new("OnEndTouch")</c>
/// and func_physbox's <c>Damaged = new("OnDamaged")</c>. Registration reads
/// declared names off a throwaway instance, which a constructor-injected class
/// cannot give it, so it fell back to the field name.
/// </summary>
[ScriptedEntity("test_named_output_injected")]
public sealed class TestNamedOutputInjectedEntity(ILog log) : ScriptedEntity
{
    public readonly Output Damaged = new("OnDamaged");

    public ILog Injected => log;
}

/// <summary>The control: the same output on a class the probe CAN build.</summary>
[ScriptedEntity("test_named_output_plain")]
public sealed class TestNamedOutputPlainEntity : ScriptedEntity
{
    public readonly Output Damaged = new("OnDamaged");
}

public class NamedOutputBindingTests
{
    private static readonly EntityHandle Ent = new(0x0005_0031u);

    private static ScriptedEntityRegistry Spawned(string className)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(NamedOutputBindingTests).Assembly);
        registry.Services = Services();
        registry.Spawn(Ent, className);
        Assert.NotNull(registry.Get(Ent));
        return registry;
    }

    [Theory]
    [InlineData("test_named_output_plain")]
    [InlineData("test_named_output_injected")]
    public void The_declared_name_resolves_and_the_field_name_does_not(string className)
    {
        var registry = Spawned(className);

        Assert.Equal(0, registry.ResolveOutput(Ent, "OnDamaged"));
        Assert.Equal(-1, registry.ResolveOutput(Ent, "Damaged"));
    }

    [Theory]
    [InlineData("test_named_output_plain")]
    [InlineData("test_named_output_injected")]
    public void A_connection_on_the_declared_name_fires(string className)
    {
        var registry = Spawned(className);
        var instance = registry.Get(Ent)!;
        Assert.True(registry.AddOutput(Ent, instance, "OnDamaged sink:Kill::0:-1"));

        // FireOutput refuses a name the class never declared, posting nothing;
        // the declared name must reach the connection.
        Assert.Equal(1, registry.FireOutput(Ent, "OnDamaged", null, EntityHandle.None));
    }
}
