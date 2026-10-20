using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;
using SourceSharp.Client.Render.Shadows;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Shadow and ShadowModel proxies' managed OnBind (Q1 of
/// docs/plans/c7_shadow_manager.md, "move to c#"): what stock's CShadowProxy
/// and CShadowModelProxy read and write (clientshadowmgr.cpp 4219-4333), on
/// inputs the golden does not hold (a missing var at each place, no atlas, a
/// handle that names no shadow, the atlas made again).
/// </summary>
public class ShadowProxiesTests
{
    private sealed class Source : IShadowProxySource
    {
        public int AtlasPage { get; set; } = 7;

        public readonly Dictionary<int, ShadowEngineInfo> Infos = [];

        public bool TryGetShadowInfo(int clientShadowHandle, out ShadowEngineInfo info) => Infos.TryGetValue(clientShadowHandle, out info);
    }

    private static ShadowEngineInfo Info()
    {
        var info = new ShadowEngineInfo { FalloffOffset = 3, MaxDist = 70, FalloffAmount = 240, TexOriginX = 0.25f, TexOriginY = 0.5f, TexSizeX = 0.125f, TexSizeY = 0.0625f };
        for (int i = 0; i < 16; i++)
            info.WorldToShadow[i] = i + 1;
        return info;
    }

    private static readonly string[] ModelVars =
        ["$basetexture", "$basetextureoffset", "$basetexturescale", "$basetexturetransform", "$falloffoffset", "$falloffdistance", "$falloffamount"];

    [Fact]
    public void TheShadowProxyIsRefusedWithoutABaseTexture()
    {
        var init = new MaterialProxyFactsTests.FakeInit();
        init.Missing.Add("$basetexture");
        Assert.False(new ShadowProxy(new Source()).Init(init));
        Assert.Equal(["$basetexture"], init.Asked);
    }

    [Fact]
    public void TheShadowProxyBindsTheAtlasAndNullWithoutOne()
    {
        var source = new Source();
        var proxy = new ShadowProxy(source);
        var init = new MaterialProxyFactsTests.FakeInit();
        Assert.True(proxy.Init(init));
        var bind = new MaterialProxyFactsTests.FakeBind { Arg = 3 };
        bind.TextureAnswers["_rt_Shadows"] = 42;
        proxy.Bind(bind);
        proxy.Bind(bind);
        Assert.Equal(["0 texture 42", "0 texture 42"], bind.Ops);
        Assert.Equal(["_rt_Shadows"], bind.Textures);   // asked once per atlas

        source.AtlasPage = 0;
        proxy.Bind(bind);
        Assert.Equal("0 texture -1", bind.Ops[^1]);

        // The atlas made again (render-to-texture off and on): asked again.
        source.AtlasPage = 9;
        bind.TextureAnswers["_rt_Shadows"] = 43;
        proxy.Bind(bind);
        Assert.Equal("0 texture 43", bind.Ops[^1]);
        Assert.Equal(2, bind.Textures.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void TheShadowModelProxyIsRefusedAtTheFirstVarItsMaterialLacks(int missing)
    {
        var init = new MaterialProxyFactsTests.FakeInit();
        init.Missing.Add(ModelVars[missing]);
        Assert.False(new ShadowModelProxy(new Source()).Init(init));
        Assert.Equal(ModelVars[..(missing + 1)], init.Asked);
    }

    [Fact]
    public void TheShadowModelProxyWritesTheAtlasAndTheEnginesShadowInfoInStocksOrder()
    {
        var source = new Source();
        source.Infos[5] = Info();
        var proxy = new ShadowModelProxy(source);
        Assert.True(proxy.Init(new MaterialProxyFactsTests.FakeInit()));
        // The bind argument is the client handle, its low 16 bits.
        var bind = new MaterialProxyFactsTests.FakeBind { Arg = 0x7_0005 };
        bind.TextureAnswers["_rt_Shadows"] = 42;
        proxy.Bind(bind);
        Assert.Equal(["0 texture 42", "3 matrix", "1 vecn 2", "2 vecn 2", $"4 float {3f:R}", $"5 float {70f:R}", $"6 float {240f:R}"], bind.Ops);
        Assert.Equal([0.25f, 0.5f], bind.VecSets[0].Values);
        Assert.Equal([0.125f, 0.0625f], bind.VecSets[1].Values);
    }

    [Fact]
    public void AHandleThatNamesNoShadowSetsOnlyTheTexture()
    {
        var proxy = new ShadowModelProxy(new Source());
        Assert.True(proxy.Init(new MaterialProxyFactsTests.FakeInit()));
        var bind = new MaterialProxyFactsTests.FakeBind { Arg = 9 };
        proxy.Bind(bind);
        Assert.Equal(["0 texture -1"], bind.Ops);
    }
}
