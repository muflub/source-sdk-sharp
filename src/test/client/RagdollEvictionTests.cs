using System.IO;
using System.Linq;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's native ragdoll code leaving client.so (plan
/// physics_bones_native_zero Phase 3), one removal half per file, each with
/// the retention half that doing less cannot satisfy: what replaced it is
/// still there and still the client's.
/// </summary>
public class RagdollEvictionTests
{
    private static bool Lists(string target, string file) =>
        CMakeGraph.ListedSources(target).Any(s => s.EndsWith("/" + file, System.StringComparison.Ordinal));

    private static bool DebugLists(string target, string file) =>
        CMakeGraph.DebugListedSources(target).Any(s => s.EndsWith("/" + file, System.StringComparison.Ordinal));

    private static bool Linked(string list, string path) =>
        File.ReadAllLines(RepoLayout.Repo("sdk-links", list)).Any(l => l.Trim() == path);

    // ---- game/client/ragdoll.cpp ---------------------------------------------------

    [Fact]
    public void StocksRagdollCppLeftTheClient()
    {
        Assert.False(Lists(CMakeGraph.Client, "ragdoll.cpp"));
        Assert.False(DebugLists(CMakeGraph.Client, "ragdoll.cpp"));
        Assert.False(Linked("client.txt", "src/game/client/ragdoll.cpp"));
    }

    [Fact]
    public void CRagdollsSettleAndItsConVarAreTheClientsOwn()
    {
        // The settle detector and the convar it reads, in managed code.
        Assert.NotNull(typeof(RagdollSolver).GetMethod(nameof(RagdollSolver.CheckSettleStationary)));
        var convar = typeof(RagdollSleepAfterTimeConVar).GetCustomAttributes(typeof(SourceSharp.Abi.ConVarAttribute), false)
                                                       .Cast<SourceSharp.Abi.ConVarAttribute>().Single();
        Assert.Equal(("ragdoll_sleepaftertime", "5.0f"), (convar.Name, convar.DefaultValue));
    }

    // ---- src/shared/native/ragdoll_shared.cpp --------------------------------------

    [Fact]
    public void RagdollSharedCppLeftTheClientAndTheRepository()
    {
        Assert.False(Lists(CMakeGraph.Client, "ragdoll_shared.cpp"));
        Assert.False(DebugLists(CMakeGraph.Client, "ragdoll_shared.cpp"));
        Assert.False(Lists(CMakeGraph.Server, "ragdoll_shared.cpp"));
        Assert.False(File.Exists(RepoLayout.Repo("src", "shared", "native", "ragdoll_shared.cpp")));
    }

    [Fact]
    public void RagdollSharedsPrimitivesAndKeyHandlersAreTheClientsOwn()
    {
        // ragdoll_shared.cpp's entry points, each a public member of the managed solver.
        string[] ported =
        {
            nameof(RagdollSolver.Create), nameof(RagdollSolver.Activate), nameof(RagdollSolver.SetupCollisions),
            nameof(RagdollSolver.Destroy), nameof(RagdollSolver.GetBoneMatrix), nameof(RagdollSolver.ComputeExactBbox),
            nameof(RagdollSolver.IsAsleep), nameof(RagdollSolver.ApplyAnimationAsVelocity),
            nameof(RagdollSolver.SolveSeparation),
        };
        foreach (string name in ported)
            Assert.Contains(typeof(RagdollSolver).GetMethods(), m => m.Name == name && m.IsPublic);

        // CRagdollCollisionRules and CRagdollAnimatedFriction: the .phy key sinks.
        Assert.True(typeof(IRagdollKeySink).IsAssignableFrom(typeof(RagdollCollisionRules)));
        Assert.True(typeof(IRagdollKeySink).IsAssignableFrom(typeof(RagdollAnimatedFrictionKeys)));
    }

    // ---- src/shared/native/bone_setup.cpp ------------------------------------------

    [Fact]
    public void BoneSetupLeftTheClientAndTheRepository()
    {
        Assert.False(Lists(CMakeGraph.Client, "bone_setup.cpp"));
        Assert.False(DebugLists(CMakeGraph.Client, "bone_setup.cpp"));
        Assert.False(Lists(CMakeGraph.Server, "bone_setup.cpp"));
        Assert.False(File.Exists(RepoLayout.Repo("src", "shared", "native", "bone_setup.cpp")));
    }

    [Fact]
    public void TheBoneMathItsLastCallerAskedIsManaged()
    {
        // ragdoll_shared.cpp's three asks of bone_setup.cpp: CalcBoneDerivatives
        // (ApplyAnimationAsVelocity), Studio_BoneIndexByName and
        // Studio_CalcBoneToBoneTransform (the animated-friction and the
        // collision set-up). Each is a managed port the solver calls.
        Assert.NotNull(typeof(BoneSetupAdj).GetMethod(nameof(BoneSetupAdj.CalcBoneDerivatives)));
        Assert.NotNull(typeof(BoneSetupAdj).GetMethod(nameof(BoneSetupAdj.CalcBoneToBoneTransform)));
        Assert.NotNull(typeof(BoneSetupQuery).GetMethod(nameof(BoneSetupQuery.BoneIndexByName)));
    }
}
