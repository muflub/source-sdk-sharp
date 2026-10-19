using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The FCVAR word on a managed console command, from the attribute that
/// declares it to the registry row that reports it.
///
/// <para>
/// <c>Engine.RegisterConCommand</c> took a name and a help string and nothing
/// else, so a C#-declared command was always an UNFLAGGED one and
/// <c>docs/convar-ledger.tsv</c> carried thirteen rows at <c>deferred-abi</c>
/// naming that one gap. Every one of them declares <c>FCVAR_CHEAT</c>, and a
/// cheat command that runs with <c>sv_cheats 0</c> is not the command that was
/// being ported — so "the port compiled and the command appeared in the
/// console" is exactly the evidence that would NOT catch the defect.
/// </para>
///
/// <para>
/// The native half — the word reaching the <c>ConCommand</c> constructor, and
/// the DLL identifier bit the static construction list can no longer supply —
/// is pinned by <c>sourcesharp/test/concommand_flags_test.cpp</c>. This half is
/// the managed path that feeds it.
/// </para>
/// </summary>
public class ConCommandFlagsTests
{
    [ConCommand("ss_test_flagged_command", Help = "a cheat command, for the flag path",
                Flags = ConVarFlags.Cheat | ConVarFlags.ServerCanExecute)]
    private sealed class FlaggedTestCommand : IConCommand
    {
        public void Execute(string args) { }
    }

    [ConCommand("ss_test_unflagged_command", Help = "declares no flags")]
    private sealed class UnflaggedTestCommand : IConCommand
    {
        public void Execute(string args) { }
    }

    /// <summary>
    /// A console that remembers what it was asked to register.
    ///
    /// <para>
    /// The real engine answers -1 outside the game before it looks at any
    /// argument, so a registration carrying the declared FCVAR word and one
    /// that dropped it are indistinguishable through it — and the registry's
    /// own row is built from the attribute, so it reads the right flags either
    /// way. This is the only place the argument is visible.
    /// </para>
    /// </summary>
    private sealed class WatchingConsole : IEngineConsole
    {
        public List<(string Name, string Help, ConVarFlags Flags)> Registered { get; } = new();

        public int RegisterConCommand(string name, string help, ConVarFlags flags)
        {
            Registered.Add((name, help, flags));
            return Registered.Count - 1;
        }

        public void ExecuteServerCommand(string command) { }
        public string? FindConVarValue(string name) => null;
        public string GetConVarValue(int id) => string.Empty;
        public void Msg(string message) { }
        public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                  float? min = null, float? max = null) => 0;
        public void SendClientCommand(EntityHandle player, string command) { }
        public void SetConVarValue(int id, string value) { }
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    private static (ConCommandInfo Row, ConVarFlags Sent) Register(string name)
    {
        var registry = new ConCommandRegistry();
        var console  = new WatchingConsole();

        registry.RegisterAll(typeof(ConCommandFlagsTests).Assembly, services: null, console: console);

        return (registry.All.Single(c => c.Name == name),
                console.Registered.Single(r => r.Name == name).Flags);
    }

    private static ConCommandInfo Find(string name) => Register(name).Row;

    [Fact]
    public void ADeclaredFlagWordReachesTheRegistryRow()
    {
        Assert.Equal(ConVarFlags.Cheat | ConVarFlags.ServerCanExecute,
                     Find("ss_test_flagged_command").Flags);
    }

    /// <summary>
    /// The half the registry row cannot answer: what was actually HANDED to the
    /// engine. The row is built from the attribute, so it reads
    /// <c>Cheat | ServerCanExecute</c> even in a build that registers every
    /// command unflagged.
    /// </summary>
    [Fact]
    public void TheDeclaredFlagWordIsWhatTheEngineIsHanded()
    {
        Assert.Equal(ConVarFlags.Cheat | ConVarFlags.ServerCanExecute,
                     Register("ss_test_flagged_command").Sent);
    }

    [Fact]
    public void ACommandThatDeclaresNoFlagsIsUnflagged()
    {
        Assert.Equal(ConVarFlags.None, Find("ss_test_unflagged_command").Flags);
    }

    /// <summary>
    /// The attribute's default, read off the attribute rather than off a
    /// registry row — the row above could read <see cref="ConVarFlags.None"/>
    /// because the default is None or because the registry dropped the word on
    /// the floor, and those are different bugs.
    /// </summary>
    [Fact]
    public void TheAttributeDefaultsToNoFlags()
    {
        Assert.Equal(ConVarFlags.None, new ConCommandAttribute("ss_test_default").Flags);
    }

    /// <summary>
    /// That the FLAGLESS registration path is gone rather than merely joined.
    ///
    /// <para>
    /// An overload taking only (name, help) would compile every existing
    /// declaration unchanged and silently register a cheat command as an
    /// ordinary one — which is the state this whole change exists to leave, and
    /// a test that only checks the three-argument call still passes in it.
    /// </para>
    ///
    /// <para>
    /// Asked of <see cref="EngineSide"/>, the per-side instance every console
    /// area forwards to, since plan_abi_hotreload D8 retires the static
    /// <c>Engine</c>'s forwarders.
    /// </para>
    /// </summary>
    [Fact]
    public void ThereIsNoFlaglessRegistrationOverloadLeft()
    {
        var overloads = typeof(EngineConsole)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(EngineConsole.RegisterConCommand))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Single(overloads);
        Assert.Equal(new[] { typeof(string), typeof(string), typeof(ConVarFlags) },
                     overloads[0]);
    }

    /// <summary>
    /// And the same question of the interface the modules are handed, which is
    /// what a module actually calls when it does not reach for the static.
    /// </summary>
    [Fact]
    public void TheEngineConsoleAreaTakesAFlagWordToo()
    {
        var method = typeof(IEngineConsole).GetMethod(nameof(IEngineConsole.RegisterConCommand));

        Assert.NotNull(method);
        Assert.Equal(new[] { typeof(string), typeof(string), typeof(ConVarFlags) },
                     method!.GetParameters().Select(p => p.ParameterType).ToArray());
    }
}
