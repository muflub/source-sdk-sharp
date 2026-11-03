using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Client.Ui;
using TF2.Shared.Movement;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's composition, held by the shared census
/// (<see cref="CompositionNullBake"/>) over <c>ClientModule.Initialize</c>'s
/// own statement order: no optional constructor parameter the walk builds is
/// baked from its default while the finished container registers the service.
/// ILocalArsenal was exactly that (a0762adb0) — the movement composition
/// baked <c>TFClientWeaponOwners.arsenal</c> null and
/// <c>tf_use_min_viewmodels</c> moved nothing, silently, tests green.
/// </summary>
public class ClientCompositionNullBakeTests
{
    private static string Source()
        => File.ReadAllText(RepoLayout.Sdk("src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs"));

    [Fact]
    public void The_client_composition_bakes_no_optional_parameter_the_container_registers()
    {
        var census = CompositionNullBake.Walk(Source(), Side.Client,
            typeof(ClientCompositionNullBakeTests), typeof(TFMovementRules));

        Assert.True(census.Walks > 100, $"the walk read the client's resolves: {census.Debug}");
        Assert.True(census.ParametersSeen > 20, $"the walk read the client's optional parameters: {census.Debug}");
        Assert.True(census.Bakes.Count == 0,
            "optional parameters baked before their registration: " + string.Join("; ", census.Bakes));
    }
}
