using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The serialised ABI cleanup pass (2026-10-20, docs/abi-retirements.md)
/// removed every interop slot that had no live caller on the side that calls
/// it. These facts keep each removed slot removed: a row restored to
/// docs/managed-interfaces.tsv, docs/engineapi-areas.tsv or a FORWARD/HAND row
/// of tools/engine-forwarders regenerates a slot, and the generated file it
/// lands in is read here.
///
/// <para>
/// Each row names a live neighbour on the same interface, read from the same
/// files: the positive arm, so a scan that read the wrong file, or nothing,
/// cannot pass.
/// </para>
/// </summary>
public class AbiCleanupPassTests
{
    /// <summary>Native-&gt;managed rows: (interface, removed slot, live slot of the same interface).</summary>
    public static TheoryData<string, string, string> RemovedManagedSlots => new()
    {
        { "IManagedTempEntsClient", "OnTempEntPoolAlloc", "OnTempEntPoolUpdate" },
        { "IManagedTempEntsClient", "OnTempEntEjectBrass", "OnTempEntPoolClear" },
        { "IManagedConsoleOutput", "ConsoleLine", "ConsoleBufferAttach" },
        { "IManagedConsoleOutputClient", "ClientConsoleLine", "ClientConsoleBufferAttach" },
        { "IManagedSoundEnt", "ServerSoundEntInsert", "ServerSoundEntInit" },
        { "IManagedHierarchy", "HierarchyEntityRemoved", "HierarchySetParent" },
        { "IManagedHierarchy", "HierarchyResolveParentNames", "HierarchyToLocal" },
        { "IManagedClients", "OnRespawn", "OnPlayerSpawn" },
        { "IManagedClients", "GetPlayerRelationship", "OnClientActive" },
        { "IManagedResources", "ServerPropDataRandomChunkModel", "ServerDecalIndexForName" },
        { "IManagedEntities", "OnEntityIgnite", "EntityRemoveRequested" },
        { "IManagedEntities", "OnFireAddHeatInRadius", "EntityRemoveRequested" },
        { "IManagedEntities", "OnRagdollMagnetForce", "EntityRemoveRequested" },
        { "IManagedEntities", "ShellLessFindByName", "EntityRemoveRequested" },
        { "IManagedEntities", "ShellLessFindByClassname", "EntityRemoveRequested" },
        { "IManagedEntities", "TemplatesAddNamed", "EntityRemoveRequested" },
        { "IManagedRenderClient", "RenderLeafList", "RenderFrame" },
        { "IManagedRenderClient", "RenderLeafViewModels", "RenderFrame" },
        { "IManagedLeafSystemClient", "ChangeRenderableRenderGroup", "SetRenderGroup" },
        { "IManagedLeafSystemClient", "AddShadow", "AddRenderable" },
        { "IManagedLeafSystemClient", "RemoveShadow", "RemoveRenderable" },
        { "IManagedLeafSystemClient", "ProjectShadow", "PreRender" },
        { "IManagedLeafSystemClient", "ProjectFlashlight", "PreRender" },
        { "IManagedLeafSystemClient", "EnumerateShadowsInLeaves", "BuildRenderablesList" },
    };

    /// <summary>
    /// Interfaces every slot of which went: no row at all, no table, no
    /// accessor native could ask for, and no binding name managed registers.
    /// (interface, the side's live interface the same files carry).
    /// </summary>
    public static TheoryData<string, string> RemovedManagedInterfaces => new()
    {
        { "IManagedSoundEmitter", "IManagedSoundEnt" },
        { "IManagedHudClient", "IManagedUiClient" },
    };

    [Theory]
    [MemberData(nameof(RemovedManagedInterfaces))]
    public void ARemovedManagedInterfaceHasNoRowNoTableAndNoAccessor(string iface, string live)
    {
        bool client = iface.EndsWith("Client", StringComparison.Ordinal);
        string tsv = Repo("docs", "managed-interfaces.tsv");
        string header = Repo("src", "shared", "native", client ? "imanaged_areas_client.h" : "imanaged_areas.h");
        string acquire = Repo("src", "shared", "native", client ? "shell_managed_areas_client.inc" : "shell_managed_areas.inc");
        string tables = Repo("src", client ? "client" : "server", "interop", client ? "ClientManagedTables.g.cs" : "ServerManagedTables.g.cs");

        Assert.DoesNotContain($"\n{iface}\t", tsv);
        Assert.DoesNotContain($"class {iface} ", header);
        Assert.DoesNotContain($"\"{iface}\"", acquire);
        Assert.DoesNotContain($"[ImplementedByManaged(\"{iface}\")]", tables);

        Assert.Contains($"\n{live}\t", tsv);
        Assert.Contains($"class {live} ", header);
        Assert.Contains($"\"{live}\"", acquire);
        Assert.Contains($"[ImplementedByManaged(\"{live}\")]", tables);
    }

    /// <summary>
    /// Managed-&gt;native shim-area rows: (interface, removed slot, live slot of
    /// the same interface). The row in docs/engineapi-areas.tsv, the pure
    /// virtual in the hand-kept header, and the forwarder table's field.
    /// </summary>
    public static TheoryData<string, string, string> RemovedShimSlots => new()
    {
        { "IShimTempEntPoolClient", "TempEntPoolSimulate", "TempEntPoolAddVisible" },
        { "IShimEventsServer", "EventSetFloat", "EventSetInt" },
        { "IShimDebugOverlay", "OverlayResetCounts", "OverlayCounts" },
        { "IShimNetworkServer", "EntityMarkStateChanged", "EntityMarkBlockChanged" },
        { "IShimStandaloneClient", "StandaloneSetShadowCast", "StandaloneShadowCast" },
        { "IShimUiClient", "UiScreenSize", "UiFontAdvances" },
    };

    [Theory]
    [MemberData(nameof(RemovedShimSlots))]
    public void ARemovedShimSlotHasNoRowNoVirtualAndNoForwarder(string iface, string slot, string live)
    {
        bool client = iface.EndsWith("Client", StringComparison.Ordinal);
        string tsv = Repo("docs", "engineapi-areas.tsv");
        string header = Repo("src", "shared", "native", client ? "ishim_areas_client.h" : "ishim_areas.h");
        string forwarders = Repo("src", "abi", "SharedForwarders.g.cs");

        Assert.DoesNotContain($"\n{slot}\tEngineApi\t{iface}\t", tsv);
        Assert.DoesNotContain($" {slot}(", header);
        Assert.DoesNotContain($"{iface}::{slot}(", forwarders);

        Assert.Contains($"\n{live}\tEngineApi\t{iface}\t", tsv);
        Assert.Contains($" {live}(", header);
        Assert.Contains($"{iface}::{live}(", forwarders);
    }

    /// <summary>
    /// StandaloneShadowCast's third out (whether the C++ shadow manager held a
    /// handle) was always passed NULL once the managed manager held the
    /// handles, and left in the pass: the forwarder takes the entity and the
    /// choice's out only. The live slot beside it is the positive arm.
    /// </summary>
    [Fact]
    public void StandaloneShadowCastTakesTheEntityAndOneOut()
    {
        string forwarders = Repo("src", "abi", "SharedForwarders.g.cs");

        Assert.Contains("// int32_t IShimStandaloneClient::StandaloneShadowCast( uint32_t, int32_t * )\n", forwarders);
        Assert.DoesNotContain("IShimStandaloneClient::StandaloneShadowCast( uint32_t, int32_t *, int32_t * )", forwarders);
        Assert.Contains("IShimStandaloneClient::StandaloneShadowCast(", forwarders);
    }

    /// <summary>
    /// FORWARD and HAND slots managed no longer called: (the generated file,
    /// the forwarder struct, the removed field, a live field of the same
    /// struct). A method restored to its tools/engine-forwarders row is a field
    /// of the struct again.
    /// </summary>
    public static TheoryData<string, string, string, string> RemovedForwarders => new()
    {
        { "SharedForwarders.g.cs", "UniformRandomStreamFwd", "RandomFloatExp", "RandomFloat" },
        { "SharedForwarders.g.cs", "BaseFileSystemFwd", "GetFileTime", "FileExists" },
        { "ServerForwarders.g.cs", "EngineServerFwd", "GetMapEntitiesString", "GetArea" },
        { "ServerForwarders.g.cs", "GameEventManagerFwd", "FreeEvent", "FireEvent" },
        { "ClientForwarders.g.cs", "SteamCallbackFwd", "CallbackId", "IsRegistered" },
    };

    [Theory]
    [MemberData(nameof(RemovedForwarders))]
    public void ARemovedForwarderIsNoFieldOfItsTable(string file, string table, string gone, string live)
    {
        string text = file switch
        {
            "SharedForwarders.g.cs" => Repo("src", "abi", file),
            "ServerForwarders.g.cs" => Repo("src", "server", "interop", file),
            _ => Repo("src", "client", "interop", file),
        };
        Match block = Regex.Match(text, @"internal unsafe struct " + table + @" : IForwarderTable\n\{(.*?)\n\}", RegexOptions.Singleline);
        Assert.True(block.Success, $"{table} was not found in {file}");

        var fields = Regex.Matches(block.Groups[1].Value, @"public delegate\* unmanaged<[^>]*> (\w+);")
                          .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(gone, fields);
        Assert.Contains(live, fields);
    }

    private static string Repo(params string[] path)
    {
        string file = RepoLayout.Repo(path);
        Assert.True(File.Exists(file), $"{string.Join('/', path)} was not found; a scan that read nothing would pass");
        return File.ReadAllText(file);
    }

    /// <summary>The TSV row, the C table native reads, and the C# contract managed builds.</summary>
    [Theory]
    [MemberData(nameof(RemovedManagedSlots))]
    public void ARemovedManagedSlotHasNoRowNoTableFieldAndNoContractMember(string iface, string slot, string live)
    {
        bool client = iface.EndsWith("Client", StringComparison.Ordinal);
        string tsv = Repo("docs", "managed-interfaces.tsv");
        string header = Repo("src", "shared", "native", client ? "imanaged_areas_client.h" : "imanaged_areas.h");
        string contract = Repo("src", client ? "client" : "server", "interop", "ManagedAreas.g.cs");

        Assert.DoesNotContain($"\n{iface}\t{slot}\t", tsv);
        Assert.DoesNotContain($"( *{slot} )( {iface} *self", header);
        Assert.DoesNotContain($" {slot}(", contract);

        Assert.Contains($"\n{iface}\t{live}\t", tsv);
        Assert.Contains($"( *{live} )( {iface} *self", header);
        Assert.Contains($" {live}(", contract);
    }
}
