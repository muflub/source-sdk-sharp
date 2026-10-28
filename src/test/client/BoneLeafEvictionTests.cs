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

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheLeafLibraryLeftTheClientAndTheRepository()
    {
        Assert.True(Lists("shell_clientdll.cpp"));   // the reader reads the client's list
        Assert.False(Lists("shell_bone_leaf_library.cpp"));
        Assert.False(File.Exists(RepoLayout.Repo("src", "client", "native", "shell_bone_leaf_library.cpp")));
        Assert.False(File.Exists(RepoLayout.Repo("src", "client", "native", "bone_leaf_library.h")));
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
