using System;
using System.Collections.Generic;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Managed code never aliases a convar the engine defines. The native
/// registration answers <see cref="ConVarRegistry.EngineOwnedId"/> for such a
/// name (convar_ownership.h) and the registry refuses to start, naming every
/// offending declaration.
/// </summary>
public class ConVarEngineOwnershipTests
{
    private sealed class FakeBackend : IConVarBackend
    {
        public readonly HashSet<string> EngineOwned = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Registered = new();
        private int _next;

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            if (EngineOwned.Contains(name))
                return ConVarRegistry.EngineOwnedId;
            Registered.Add(name);
            return _next++;
        }

        public void Set(int id, string value) { }
        public string Get(int id) => "";
        public string? ValueOf(string name) => null;
    }

    [ConVar("ss_test_ours_a", "1")]
    private sealed class OursA : BoolConVar { }

    [ConVar("ss_test_ours_b", "2")]
    private sealed class OursB : IntConVar { }

    [ConVar("ss_test_engine_one", "1")]
    private sealed class EngineOne : BoolConVar { }

    [ConVar("ss_test_engine_two", "0")]
    private sealed class EngineTwo : BoolConVar { }

    [Fact]
    public void TheEngineOwnedAnswerIsNativesMinusTwo()
    {
        // SOURCESHARP_CONVAR_ENGINE_OWNED in convar_ownership.h.
        Assert.Equal(-2, ConVarRegistry.EngineOwnedId);
    }

    [Fact]
    public void OurOwnNamesRegisterAndBindToTheBackend()
    {
        var backend = new FakeBackend();
        var registry = new ConVarRegistry(backend);

        Assert.Equal(2, registry.RegisterTypes(new[] { typeof(OursA), typeof(OursB) }));

        Assert.Equal(new[] { "ss_test_ours_a", "ss_test_ours_b" }, backend.Registered);
        Assert.Same(backend, registry.Get<OursA>()!.Backend);
    }

    [Fact]
    public void AnEngineOwnedNameStopsRegistration()
    {
        var backend = new FakeBackend();
        backend.EngineOwned.Add("ss_test_engine_one");
        var registry = new ConVarRegistry(backend);

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.RegisterTypes(new[] { typeof(OursA), typeof(EngineOne) }));

        Assert.Contains("'ss_test_engine_one'", ex.Message);
        Assert.Contains(typeof(EngineOne).FullName!, ex.Message);
    }

    [Fact]
    public void EveryEngineOwnedNameIsNamedNotJustTheFirst()
    {
        var backend = new FakeBackend();
        backend.EngineOwned.Add("ss_test_engine_one");
        backend.EngineOwned.Add("ss_test_engine_two");
        var registry = new ConVarRegistry(backend);

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.RegisterTypes(new[] { typeof(EngineOne), typeof(OursA), typeof(EngineTwo) }));

        Assert.Contains("'ss_test_engine_one'", ex.Message);
        Assert.Contains("'ss_test_engine_two'", ex.Message);
    }

    [Fact]
    public void AnEngineOwnedNameIsNeverBoundAsIfItWereOurs()
    {
        var backend = new FakeBackend();
        backend.EngineOwned.Add("ss_test_engine_one");
        var registry = new ConVarRegistry(backend);

        Assert.Throws<InvalidOperationException>(() => registry.RegisterTypes(new[] { typeof(EngineOne) }));

        Assert.Null(registry.Get<EngineOne>());
        Assert.Null(registry.ValueOf("ss_test_engine_one"));
    }

    [Fact]
    public void AnOrdinaryRefusalStillBindsToTheDeclaredDefault()
    {
        // -1 (no engine, or refused for another reason) keeps its old meaning.
        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterTypes(new[] { typeof(OursB) });
        Assert.Equal("2", registry.ValueOf("ss_test_ours_b"));
    }

    /// <summary>
    /// TWO REGISTRIES, TWO CONSOLES (plan_abi_hotreload D8): each registers
    /// through the console it was built over, never a shared or active-side
    /// one.
    /// </summary>
    [Fact]
    public void EachRegistryRegistersThroughItsOwnConsole()
    {
        RecordingEngine Console(int id) => TestServices.Fake()
            .Returns("Console.RegisterConVar", id)
            .Returns("Console.FindConVarValue", "2");

        var first = Console(11);
        var second = Console(22);

        ConVarRegistry.For(first).RegisterTypes(new[] { typeof(OursB) });
        ConVarRegistry.For(second).RegisterTypes(new[] { typeof(OursB) });

        Assert.Equal(1, first.CountOf("Console.RegisterConVar"));
        Assert.Equal(1, second.CountOf("Console.RegisterConVar"));
    }
}
