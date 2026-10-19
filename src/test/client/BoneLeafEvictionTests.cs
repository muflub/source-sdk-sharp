using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client bone leaf library leaving client.so and the repository
/// (bone_leaf_library.h, shell_bone_leaf_library.cpp: the bone-access stack,
/// the model-bone counter, the thread-pool stubs and the sweep registry), with
/// the retention half: the frame's bones are still managed's, asked by native.
/// </summary>
public class BoneLeafEvictionTests
{
    private static bool Lists(string file) =>
        CMakeGraph.ListedSources(CMakeGraph.Client).Any(s => s.EndsWith("/" + file, System.StringComparison.Ordinal));

    private static string Code(params string[] path) =>
        Regex.Replace(File.ReadAllText(RepoLayout.Repo(path)), @"//[^\n]*", "");

    /// <summary>Every name the library gave client.so.</summary>
    private static readonly string[] Names =
    {
        "SourceSharp_BoneAccessPush", "SourceSharp_BoneAccessPop", "SourceSharp_BoneAccessAllowed",
        "CSourceSharpBoneAccessScope", "SourceSharp_InvalidateBoneCaches", "SourceSharp_ModelBoneCounter",
        "SourceSharp_InitBoneSetupThreadPool", "SourceSharp_ShutdownBoneSetupThreadPool",
        "SourceSharp_RegisterBoneSweep", "SourceSharp_ThreadedBoneSetup", "SourceSharp_UpdateClientSideAnimations",
        "SourceSharp_ClientBoneSetupLevelShutdown", "SourceSharp_BoneSweepCount", "bone_leaf_library.h",
    };

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheLeafLibraryLeftTheClientAndTheRepository()
    {
        Assert.True(Lists("shell_clientdll.cpp"));   // the reader reads the client's list
        Assert.False(Lists("shell_bone_leaf_library.cpp"));
        Assert.False(File.Exists(RepoLayout.Repo("src", "client", "native", "shell_bone_leaf_library.cpp")));
        Assert.False(File.Exists(RepoLayout.Repo("src", "client", "native", "bone_leaf_library.h")));
    }

    [Fact]
    public void NoClientFileSpellsItsNames()
    {
        string dir = RepoLayout.Repo("src", "client", "native");
        int read = 0;
        foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                                         .Where(f => f.EndsWith(".cpp", System.StringComparison.Ordinal) || f.EndsWith(".h", System.StringComparison.Ordinal)))
        {
            string code = Code("src", "client", "native", Path.GetFileName(file));
            read++;
            foreach (string name in Names)
                Assert.False(code.Contains(name, System.StringComparison.Ordinal), $"{Path.GetFileName(file)} spells {name}");
        }
        Assert.True(read > 50, $"read {read} client native files");

        // kClientSystems[]'s sweep row went with the sweeps it ran.
        Assert.DoesNotContain("C_BaseAnimatingGameSystem", Code("src", "client", "native", "shell_clientsystems.cpp"));
    }

    [Fact]
    public void TheFramesBonesAreStillManagedsAndNativeAsksForThem()
    {
        string standalone = Code("src", "client", "native", "shell_standalone_client.cpp");
        Assert.Contains("pBones->BoneBatch( gpGlobals->framecount, gpGlobals->curtime )", standalone);
        Assert.Contains("pBones->SetupBones( GetRefEHandle().ToInt(), &Block(), pOut, nOut, boneMask, currentTime,", standalone);
        Assert.NotNull(typeof(SourceSharp.Client.ClientSkeletons).GetMethod("BoneBatch"));
    }
}
