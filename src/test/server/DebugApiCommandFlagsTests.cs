using System;

using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The debug-api command listing reports a C#-registered command's flags. It used
/// to print "" for every one, from before the registration slot carried a flag
/// word, so a Cheat command read as unflagged (plan_server_novalve.md P2.3c's
/// snapshot diff tripped on it).
/// </summary>
public class DebugApiCommandFlagsTests
{
    private static readonly ConCommandInfo[] Declared =
    {
        new("ss_probe_cheat", "a cheat", "Probe", true, ConVarFlags.Cheat),
        new("ss_probe_plain", "plain", "Probe", true, ConVarFlags.None),
    };

    /// <summary>Through the server's IDebugConsole, as the route reads them (G1).</summary>
    private static string Json(ConCommandInfo[] declared, NativeConsoleEntry[] native)
    {
        var console = new SourceSharp.Server.DebugConsoleHook(() => [], () => declared, _ => null, () => native, _ => null, _ => { });
        return DebugApiServer.CommandsJson(console.Commands(), console.NativeEntries(), ours: true);
    }

    [Fact]
    public void AManagedCommandCarriesTheEnginesFlagsWhenTheTableWasRead()
    {
        var native = new[]
        {
            new NativeConsoleEntry("ss_probe_cheat", "", "", "a cheat",
                                   unchecked((int)(ConVarFlags.GameDll | ConVarFlags.Cheat)), true),
        };

        string json = Json(Declared, native);

        Assert.Contains("\"name\":\"ss_probe_cheat\",\"help\":\"a cheat\",\"declaredBy\":\"Probe\"," +
                        "\"flags\":\"" + ConVarFlagNames.Describe(ConVarFlags.GameDll | ConVarFlags.Cheat) + "\"",
                        json, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheEnginesTableTheDeclaredFlagsAreReported()
    {
        string json = Json(Declared, Array.Empty<NativeConsoleEntry>());

        Assert.Contains("\"declaredBy\":\"Probe\",\"flags\":\"" + ConVarFlagNames.Describe(ConVarFlags.Cheat) + "\"",
                        json, StringComparison.Ordinal);
        Assert.Contains("Cheat", ConVarFlagNames.Describe(ConVarFlags.Cheat), StringComparison.Ordinal);
    }
}
