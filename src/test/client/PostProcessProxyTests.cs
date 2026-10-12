using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// viewpostprocess.cpp's two proxies, managed since the TU left client.so (C7):
/// "MotionBlur" sets $MotionBlurInternal to the post program's four values,
/// and refuses a material without that var; "engine_post" sets the AA values,
/// the bloom UV transform and the bloom switch. Both bind only for an entity
/// argument (CEntityMaterialProxy), which the post draws pass as the world.
/// </summary>
public class PostProcessProxyTests
{
    private sealed class Values : IPostProxyValues
    {
        public MotionBlurValues MotionBlur { get; set; }

        public EnginePostValues EnginePost { get; set; }
    }

    // The setter calls, without the tools-recording op every entity proxy ends a bind with.
    private static List<ProxyOp> Sets(List<ProxyOp> ops) => ops.Where(o => o.Kind != 8).ToList();

    private static MaterialProxyHost Host(MaterialProxyHostTests.FakeExecutor exec, Values values) =>
        new(exec, new MaterialProxyHostTests.QueueUniform(), () => false, new StandardErrorLog(), matStub: null, post: values);

    [Fact]
    public void MotionBlurBindsTheProgramsFourValuesForAnEntity()
    {
        var exec = new MaterialProxyHostTests.FakeExecutor();
        exec.Vars.Add("$MotionBlurInternal");
        var values = new Values { MotionBlur = new MotionBlurValues(0.5f, -0.25f, 0.125f, 0.0625f) };
        var host = Host(exec, values);
        Assert.True(host.Init(5, "MotionBlur", "dev/motion_blur", []));

        List<ProxyOp> ops = Sets(MaterialProxyHostTests.Bind(host, 5, 1f, 1, new ProxyVar { Type = (int)MaterialVarType.Vector }, arg: 7));
        ProxyOp set = Assert.Single(ops);
        Assert.Equal([0.5f, -0.25f, 0.125f, 0.0625f], new[] { set.Value(0), set.Value(1), set.Value(2), set.Value(3) });

        // No argument: CEntityMaterialProxy's OnBindNotEntity, which sets nothing.
        Assert.Empty(MaterialProxyHostTests.Bind(host, 5, 1f, 1, new ProxyVar { Type = (int)MaterialVarType.Vector }, arg: 0));
    }

    [Fact]
    public void MotionBlurRefusesAMaterialWithoutItsVar()
    {
        var host = Host(new MaterialProxyHostTests.FakeExecutor(), new Values());
        Assert.False(host.Init(6, "MotionBlur", "dev/other", []));
    }

    [Fact]
    public void EnginePostBindsItsFourVarsFromTheProgram()
    {
        var exec = new MaterialProxyHostTests.FakeExecutor();
        foreach (string v in new[] { "$AAInternal1", "$AAInternal2", "$AAInternal3", "$bloomEnable" })
            exec.Vars.Add(v);
        var values = new Values { EnginePost = new EnginePostValues(0.75f, 0.5f, 1, 0, 1, 1, 0, 0, 0.1f, 0.2f, 1.5f, 2.5f, 1) };
        var host = Host(exec, values);
        Assert.True(host.Init(7, "engine_post", "dev/engine_post", []));
        List<ProxyOp> ops = Sets(MaterialProxyHostTests.Bind(host, 7, 1f, 8, new ProxyVar { Type = (int)MaterialVarType.Vector }, arg: 7));
        Assert.Equal(4, ops.Count);
        Assert.Contains(ops, o => o.Value(0) == 0.75f && o.Value(1) == 0.5f);
        Assert.Contains(ops, o => o.Value(0) == 0.1f && o.Value(3) == 2.5f);
    }
}
