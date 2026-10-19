using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The tables the client's managed code writes (Client.Interop's
/// ClientManagedTables.g.cs, plan_forwarders_everywhere.md phase 4): the same
/// rules ManagedVtableTests holds the Abi's and the server's to, over the
/// assembly only the client's tests see.
/// </summary>
public class ClientManagedTablesTests
{
    private static Type[] Tables()
        => typeof(ManagedRuntimeClientTable).Assembly.GetTypes()
               .Where(t => t.GetCustomAttributes(typeof(ImplementedByManagedAttribute), false).Length > 0)
               .ToArray();

    [Fact]
    public void TheTrapTableCoversEveryClientTable()
    {
        Type[] tables = Tables();

        Assert.True(tables.Length >= 20, $"found {tables.Length} client tables");   // the stimulus
        Assert.All(tables, t => Assert.True(t.GetFields().Length <= ManagedVtableTraps.Count,
            $"{t.Name} has {t.GetFields().Length} slots; the trap table covers {ManagedVtableTraps.Count}"));
    }

    [Fact]
    public void EveryClientTableIsTheClientsAndNoneIsTheServers()
    {
        string[] names = Tables().Select(t => t.Name).ToArray();

        Assert.Contains("ManagedRuntimeClientTable", names);
        Assert.Contains("ManagedSteamCallbackTable", names);
        Assert.DoesNotContain("ManagedRuntimeServerTable", names);
        Assert.DoesNotContain("ManagedRootTable", names);
    }
}
