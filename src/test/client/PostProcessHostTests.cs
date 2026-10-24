using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The post crossing's managed side: a program comes back through
/// <see cref="IClientRenderPlan.BuildPostProgram"/>, and the post proxies read
/// the program's values.
/// </summary>
public class PostProcessHostTests
{
    [Fact]
    public void AProgramComesBackThroughThePlanAndCountsItsKind()
    {
        var post = new PostProcessHost(new DefaultForwarders.NoEngineClient(), new ListHooks<IPostProcessContributor>());
        IClientRenderPlan host = new RenderPlanHost(post);
        var input = new PostProcessInputs { Kind = (int)PostProgramKind.EnginePost, Width = 1280, Height = 720, HdrType = 0, EngineDxLevel = 95 };
        var ops = new RenderOp[64];
        int n = host.BuildPostProgram(input, ops);
        Assert.True(n > 0, "no program");
        Assert.Equal((int)RenderOpKind.PostReadbackNext, ops[n - 1].Kind);
        Assert.Equal(1, post.EnginePostPrograms);
        Assert.Equal(-1, new RenderPlanHost().BuildPostProgram(input, ops));
    }

    [Fact]
    public void AProgramLongerThanNativesBufferIsDeclined()
    {
        var post = new PostProcessHost(new DefaultForwarders.NoEngineClient(), new ListHooks<IPostProcessContributor>());
        var input = new PostProcessInputs { Kind = (int)PostProgramKind.EnginePost, Width = 1280, Height = 720, EngineDxLevel = 95 };
        Assert.Equal(-1, post.Build(input, Span<RenderOp>.Empty));
    }

    [Fact]
    public void TheProxiesReadTheProgramsValues()
    {
        var post = new PostProcessHost(new DefaultForwarders.NoEngineClient(), new ListHooks<IPostProcessContributor>());
        SourceSharp.Client.MaterialProxies.IPostProxyValues values = post;
        Assert.Equal(1, values.EnginePost.BloomEnable);
        post.Planner.MotionBlur.Update(new MotionBlurFrame(1f, default, default, 90f, 1.5f, false, 10f, 20f, 1f, 1f, 1f));
        post.Planner.MotionBlur.Update(new MotionBlurFrame(1f + 1f / 60f, default, new SourceSharp.Abi.Vector(0, -2, 0), 90f, 1.5f, false, 10f, 20f, 1f, 1f, 1f));
        Assert.NotEqual(0f, values.MotionBlur.X);
        Assert.Equal(post.Planner.MotionBlur.Values, values.MotionBlur);
    }
}
