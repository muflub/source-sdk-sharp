using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;
using static SourceSharp.Tests.TestServices;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>An entity that asks for a system and a convar, by contract and by class.</summary>
[ScriptedEntity("test_injected")]
public sealed class TestInjectedEntity : ScriptedEntity
{
    public TestInjectedEntity(ITestSystem system, EntityReachConVar reach)
    {
        System = system;
        Reach = reach;
    }

    public ITestSystem System { get; }
    public EntityReachConVar Reach { get; }
    public bool Spawned { get; private set; }

    public override void OnSpawn() => Spawned = true;
}

/// <summary>The same, reaching a convar by the name a player types.</summary>
[ScriptedEntity("test_injected_by_name")]
public sealed class TestNamedInjectionEntity : ScriptedEntity
{
    public TestNamedInjectionEntity([Named("ss_test_entity_reach")] ConVarBase reach)
    {
        Reach = reach;
    }

    public ConVarBase Reach { get; }
}

/// <summary>Asks for something nobody registers. Must be refused, not half-built.</summary>
[ScriptedEntity("test_injection_missing")]
public sealed class TestMissingDependencyEntity : ScriptedEntity
{
    public TestMissingDependencyEntity(IDisposable nobodyRegistersThis)
    {
        _ = nobodyRegistersThis;
    }
}

/// <summary>Asks for nothing, and must keep working with no container at all.</summary>
[ScriptedEntity("test_injection_plain")]
public sealed class TestPlainEntity : ScriptedEntity
{
    public bool Spawned { get; private set; }
    public override void OnSpawn() => Spawned = true;
}

/// <summary>An entity that only wants the directory — the common real case.</summary>
[ScriptedEntity("test_injection_directory")]
public sealed class TestDirectoryEntity : ScriptedEntity
{
    public TestDirectoryEntity(IEntityDirectory directory) { Directory = directory; }

    /// <summary>Named Directory, not Entities: the base now has an Entities of its own.</summary>
    public IEntityDirectory Directory { get; }
}
