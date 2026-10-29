using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.3c: every command whose body is native is declared
/// in <see cref="NativeCommands.Server"/>, and every declaration there has a
/// body. A body with no declaration answers to nothing; a declaration with no
/// body reaches <see cref="NativeBodiedCommand"/>, which only complains.
/// </summary>
public class NativeCommandTableTests
{
    // debugFiles: the bodies in *_debug.cpp (compiled into a debug server.so
    // only, declared in NativeCommands.Debug) or the rest (NativeCommands.Server).
    [Fact]
    public void NoCommandIsDeclaredTwice()
    {
        var names = NativeCommands.Server.Concat(NativeCommands.Debug).Select(c => c.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Nor also as a managed [ConCommand] class: two registrations of one
        // name leave one of them answering to nothing.
        var registry = new ConCommandRegistry();
        registry.RegisterAll(typeof(ServerModule).Assembly, services: null, console: new WatchingConsole());
        var classes = new HashSet<string>(registry.All.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        Assert.True(classes.Count > 0);
        Assert.DoesNotContain(names, classes.Contains);
    }

    [Fact]
    public void RegistrationSendsEachDeclarationsNameHelpAndFlags()
    {
        var console  = new WatchingConsole();
        var registry = new ConCommandRegistry();

        int n = registry.RegisterNativeBodied(NativeCommands.Server, console);

        Assert.Equal(NativeCommands.Server.Length, n);
        Assert.Equal(NativeCommands.Server.Select(c => (c.Name, c.Help, c.Flags)), console.Registered);
        // No native-bodied server command is cheat-flagged since air_density,
        // the one that was, became managed's (plan_physics_bones_native_zero
        // 2.3); the flags are still sent as declared, which the line above holds.
        Assert.DoesNotContain(console.Registered, r => r.Flags.HasFlag(ConVarFlags.Cheat));

        // So the positive arm is a declaration made here: a flag is carried.
        var flaggedConsole = new WatchingConsole();
        new ConCommandRegistry().RegisterNativeBodied(
            [new ConCommandAttribute("x_flagged") { Help = "flagged", Flags = ConVarFlags.Cheat }], flaggedConsole);
        Assert.Contains(flaggedConsole.Registered, r => r.Flags.HasFlag(ConVarFlags.Cheat));
        Assert.All(registry.All, r => Assert.Equal(nameof(NativeBodiedCommand), r.DeclaringType));
        Assert.IsType<NativeBodiedCommand>(registry.ByName("use"));
    }

    [Fact]
    public void EveryDeclarationHasHelpText()
        => Assert.All(NativeCommands.Server.Concat(NativeCommands.Debug),
                      c => Assert.False(string.IsNullOrWhiteSpace(c.Help), c.Name));

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
}
