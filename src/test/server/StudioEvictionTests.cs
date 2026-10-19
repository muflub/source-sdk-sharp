using System.IO;
using System.Linq;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// studio.cpp's eviction (2026-10-18): no native code reads the mdl through
/// CStudioHdr any more. The model descriptor is the managed
/// StudioModelDescriptor's (ModelDescriptorTests, StudioModelDescriptorTests),
/// so model_descriptor.cpp and its IShimModels slot went; the client's render
/// counts are ClientRenderPolicy's (ClientRenderPolicyTests). Each removal
/// fact here reads the CMake lists and the disk, with a positive arm that the
/// same read still finds a file that stays.
/// </summary>
public class StudioEvictionTests
{
    // A source line names the file: a non-comment line of a CMakeLists, which
    // covers every list in it (server_sourcesharp_sources and the novalve row).
    private static int SourceLinesNaming(string target, string file) =>
        CMakeGraph.ListText(target).Split('\n')
            .Select(l => l.Trim())
            .Count(l => !l.StartsWith('#') && l.EndsWith("/" + file, System.StringComparison.Ordinal));

    private static void LeftBothSidesAndTheRepository(string file)
    {
        foreach (string target in new[] { CMakeGraph.Server, CMakeGraph.Client })
        {
            // The positive arm: the read finds a shared file both lists keep.
            Assert.True(SourceLinesNaming(target, "movevars_shared.cpp") > 0,
                        $"{target}'s CMakeLists reads as naming no movevars_shared.cpp: the read is broken");

            Assert.Equal(0, SourceLinesNaming(target, file));
            Assert.DoesNotContain(CMakeGraph.DebugListedSources(target),
                                  s => s.EndsWith("/" + file, System.StringComparison.Ordinal));
        }

        Assert.True(File.Exists(RepoLayout.Repo("src", "shared", "native", "movevars_shared.cpp")));
        Assert.False(File.Exists(RepoLayout.Repo("src", "shared", "native", file)));
    }

    /// <summary>model_descriptor.cpp left both DLLs and the repository: the
    /// descriptor is read from the side's managed header.</summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheDescriptorWalkLeftBothSidesAndTheRepository()
    {
        LeftBothSidesAndTheRepository("model_descriptor.cpp");
        Assert.False(File.Exists(RepoLayout.Repo("src", "shared", "native", "model_descriptor.h")));
    }

    /// <summary>studio.cpp left both DLLs and the repository: its last
    /// consumers (model_descriptor.cpp and the client shell's
    /// PublishModelFacts) read the managed StudioHdr.</summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void StudioCppLeftBothSidesAndTheRepository()
    {
        LeftBothSidesAndTheRepository("studio.cpp");
    }

    /// <summary>studio_mdlcache.cpp went after it: the four model-cache
    /// questions studio.h declares had studio.cpp as their only caller.</summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheModelCacheAccessorsLeftBothSidesAndTheRepository()
    {
        LeftBothSidesAndTheRepository("studio_mdlcache.cpp");
    }

    /// <summary>The slot went with it: no area row, no generated method.</summary>
    [Fact]
    public void TheDescriptorSlotIsNoAreaRow()
    {
        string[] rows = File.ReadAllLines(RepoLayout.Repo("docs", "engineapi-areas.tsv"));

        // The positive arm: IShimModels' remaining slots are read.
        Assert.Contains(rows, r => r.StartsWith("EntityGetModelName\tEngineApi\tIShimModels\t", System.StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.StartsWith("ModelDescriptor\t", System.StringComparison.Ordinal));
        foreach (string name in new[] { "SourceSharp.Abi.ShimAreas", "SourceSharp.Abi.ClientShimAreas" })
        {
            System.Type areas = typeof(SourceSharp.Abi.EngineBlock).Assembly.GetType(name, throwOnError: true)!;
            // A container holds areas and no calls (plan_shim_areas_split
            // phase 9): the slot is a method of an area it holds.
            var held = areas.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                            .Select(f => f.FieldType).ToList();
            Assert.Contains(held, t => t.GetMethod("EntitiesInvalidateMdlCacheAll") is not null);
            Assert.DoesNotContain(held, t => t.GetMethod("ModelDescriptor") is not null);
        }
    }
}
