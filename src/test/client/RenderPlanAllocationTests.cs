using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The render plan is built every frame (plan_client_novalve C7). Once warm,
/// <see cref="RenderPlanHost.BuildPlan"/> must make no garbage: at 2026-10-11
/// it allocated 89,720 bytes a frame at dm_lockdown's poses (a growing op list,
/// a copy of it, and one small array per op's arguments), 5 MB a second at
/// 60 fps. The plan it hands native must still be the fresh array path's, op
/// for op.
/// </summary>
public class RenderPlanAllocationTests
{
    private static RenderInputs Fixture(string name)
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "render-stock", name);
        Assert.True(File.Exists(path), "the fixture is missing: " + path);
        RenderInputs? input = RenderTrace.Parse(File.ReadAllText(path)).Inputs();
        Assert.True(input.HasValue, $"{name}: no inputs this build can read");
        return input!.Value;
    }

    [Theory]
    [InlineData("dm_lockdown-pose1.txt")]
    [InlineData("ss_sandbox-underwater.txt")]
    public void AWarmPlanAllocatesNothing(string name)
    {
        RenderInputs input = Fixture(name);
        var host = new RenderPlanHost();
        var ops = new RenderOp[512];
        for (int i = 0; i < 4; i++)
            Assert.True(host.BuildPlan(input, ops) > 50, "the stimulus plans a frame");

        long before = GC.GetAllocatedBytesForCurrentThread();
        int n = 0;
        for (int i = 0; i < 100; i++)
            n += host.BuildPlan(input, ops);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(n > 5000, $"100 frames planned {n} ops");
        Assert.True(allocated == 0, $"100 warm plans allocated {allocated} bytes");
    }

    [Theory]
    [InlineData("dm_lockdown-pose1.txt")]
    [InlineData("dm_lockdown-pose2.txt")]
    [InlineData("ss_sandbox-underwater.txt")]
    public void TheReusedPlanIsTheFreshPlanOpForOp(string name)
    {
        RenderInputs input = Fixture(name);
        RenderOp[] fresh = RenderPlanner.Plan(input)!;
        var host = new RenderPlanHost();
        var ops = new RenderOp[512];

        // A different frame first, so a builder that kept anything would show it.
        host.BuildPlan(Fixture("dm_lockdown-pose3.txt"), ops);
        int n = host.BuildPlan(input, ops);

        Assert.Equal(fresh.Length, n);
        for (int i = 0; i < n; i++)
        {
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<RenderOp>(in ops[i]))
                            .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<RenderOp>(in fresh[i]))),
                        $"{name}: op {i} ({(RenderOpKind)fresh[i].Kind}) differs");
        }
    }
}
