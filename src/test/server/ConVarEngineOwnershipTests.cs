using System;
using System.Collections.Generic;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Managed code never aliases a convar the engine defines. The registry asks
/// native for the flags of what the engine already holds under each name and
/// refuses one carrying neither DLL's bit (<see cref="ConVarOwnership"/>,
/// convar_ownership.h's rule until plan_server_native_v2 T4e), then refuses to
/// start, naming every offending declaration.
/// </summary>
public class ConVarEngineOwnershipTests
{
    private sealed class FakeBackend : IConVarBackend
    {
        public readonly HashSet<string> EngineOwned = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Held = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Registered = new();
        private int _next;

        public int ExistingFlags(string name)
            => EngineOwned.Contains(name) ? (int)ConVarFlags.Replicated
             : Held.TryGetValue(name, out int flags) ? flags : -1;

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
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

    // ---- the rule, from convar_ownership.h (whose gtests these are now) -------

    [Fact]
    public void NoDllFlagIsTheEngines()
        => Assert.True(ConVarOwnership.OwnedByEngine(0));

    [Fact]
    public void OtherFlagsDoNotMakeItOurs()
        => Assert.True(ConVarOwnership.OwnedByEngine(
            (int)(ConVarFlags.Replicated | ConVarFlags.Archive | ConVarFlags.Cheat | ConVarFlags.Notify)));

    [Fact]
    public void TheServersIsNotTheEngines()
    {
        Assert.False(ConVarOwnership.OwnedByEngine((int)ConVarFlags.GameDll));
        Assert.False(ConVarOwnership.OwnedByEngine((int)(ConVarFlags.GameDll | ConVarFlags.Replicated)));
    }

    [Fact]
    public void TheClientsIsNotTheEngines()
        => Assert.False(ConVarOwnership.OwnedByEngine((int)ConVarFlags.ClientDll));

    /// <summary>A name the engine holds nothing under is nobody's yet: -1 refuses nothing.</summary>
    [Fact]
    public void NoSuchVariableIsNobodys()
        => Assert.False(ConVarOwnership.OwnedByEngine(-1));

    /// <summary>
    /// Any answer below zero is "none", not a flag word read bit by bit: -16
    /// carries neither DLL bit, and is still nobody's.
    /// </summary>
    [Fact]
    public void ANegativeAnswerIsNeverReadAsFlags()
        => Assert.False(ConVarOwnership.OwnedByEngine(-16));

    // ---- the registry's use of it -----------------------------------------

    /// <summary>
    /// A name the server already registered (its FCVAR_GAMEDLL bit) is a
    /// replicated pair, not the engine's: the declaration registers.
    /// </summary>
    [Fact]
    public void ANameAGameDllHoldsIsRegisteredNotRefused()
    {
        var backend = new FakeBackend();
        backend.Held["ss_test_ours_a"] = (int)(ConVarFlags.GameDll | ConVarFlags.Replicated);
        var registry = new ConVarRegistry(backend);

        Assert.Equal(1, registry.RegisterTypes(new[] { typeof(OursA) }));
        Assert.Equal(new[] { "ss_test_ours_a" }, backend.Registered);
    }

    /// <summary>The refusal says so in the words native printed, and builds no carrier.</summary>
    [Fact]
    public void AnEngineOwnedNameIsRefusedOutLoudAndNeverRegistered()
    {
        var backend = new FakeBackend();
        backend.EngineOwned.Add("ss_test_engine_one");
        var log = new RecordingLog();
        var registry = new ConVarRegistry(backend, log);

        Assert.Throws<InvalidOperationException>(() => registry.RegisterTypes(new[] { typeof(EngineOne) }));

        Assert.Empty(backend.Registered);
        Assert.Contains(log.Lines, l => l.Warning && l.Text ==
            "[SourceSharp] C# declared convar 'ss_test_engine_one', which the engine defines: refused. "
            + "Read the engine's by name instead of declaring it.\n");
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
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
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
            .Returns("Console.FindConVarFlags", -1)
            .Returns("Console.RegisterConVar", id)
            .Returns("Console.FindConVarValue", "2");

        var first = Console(11);
        var second = Console(22);

        new ConVarRegistry(first).RegisterTypes(new[] { typeof(OursB) });
        new ConVarRegistry(second).RegisterTypes(new[] { typeof(OursB) });

        Assert.Equal(1, first.CountOf("Console.RegisterConVar"));
        Assert.Equal(1, second.CountOf("Console.RegisterConVar"));
    }
}
