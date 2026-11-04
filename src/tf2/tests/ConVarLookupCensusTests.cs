extern alias client;

using System.Reflection;
using SourceSharp.Tests;
using Xunit;

namespace TF2.Tests;

/// <summary>
/// The core's ConVarLookupCensusTests over TF2's assemblies, which that project
/// does not load: no class of the game walks the registry to find a convar.
/// </summary>
public class ConVarLookupCensusTests
{
    private static Assembly[] Game
        => [typeof(TF2.Server.Player.TFPlayerGib).Assembly, typeof(client::TF2.Client.Entities.ClientTeamRoundTimer).Assembly];

    [Fact]
    public void NoClassOfTheGameReadsTheRegistrysList()
    {
        Assert.NotEmpty(IlCallCensus.Callers(m => m.DeclaringType?.Name == "ConVar`1" && m.Name == "get_Value", Game));
        Assert.Empty(IlCallCensus.Callers(
            m => m.DeclaringType?.FullName == "SourceSharp.Abi.ConVarRegistry" && m.Name == "get_All", Game));
    }
}
