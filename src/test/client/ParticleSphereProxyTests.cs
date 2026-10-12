using SourceSharp.Abi;
using SourceSharp.Client.MaterialProxies;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ParticleSphereProxy against particle_proxies.cpp as written: Init finds
/// $light_position and $light_color without complaint and never refuses;
/// OnBind does nothing without the manager, else writes the light's position
/// through the manager's model view (VMatrix * Vector, the 3x4 part) and its
/// colour times its intensity, three components each.
/// </summary>
public class ParticleSphereProxyTests
{
    private static VMatrix ModelView()
    {
        VMatrix m = default;
        float[] rows = [1, 2, 3, 10, 4, 5, 6, 20, 7, 8, 9, 30, 0, 0, 0, 1];
        for (int i = 0; i < 16; i++)
            m[i] = rows[i];
        return m;
    }

    private static (ParticleSphereProxy Proxy, MaterialProxyFactsTests.FakeInit Init) Make(params string[] missing)
    {
        var proxy = new ParticleSphereProxy();
        var init = new MaterialProxyFactsTests.FakeInit();
        foreach (string m in missing)
            init.Missing.Add(m);
        Assert.True(proxy.Init(init));
        return (proxy, init);
    }

    [Fact]
    public void InitFindsBothVarsAndAcceptsEvenWithoutThem()
    {
        (_, MaterialProxyFactsTests.FakeInit init) = Make("$light_position", "$light_color");
        Assert.Equal(["$light_position", "$light_color"], init.Asked);
    }

    [Fact]
    public void WithoutTheManagerTheBindReadsAndWritesNothing()
    {
        (ParticleSphereProxy proxy, _) = Make();
        var bind = new MaterialProxyFactsTests.FakeBind { Arg = 0 };
        proxy.Bind(bind);
        Assert.Equal(0, bind.ParticleReads);
        Assert.Empty(bind.Ops);
    }

    [Fact]
    public void ThePositionIsTheLightThroughTheModelView()
    {
        (ParticleSphereProxy proxy, _) = Make();
        var bind = new MaterialProxyFactsTests.FakeBind
        {
            Arg = 1,
            Particle = new ParticleBindLight(new Vector(1, -2, 0.5f), new Vector(0.5f, 0.25f, 1), 4, ModelView()),
        };
        proxy.Bind(bind);
        // x = 1*1 + 2*-2 + 3*0.5 + 10 = 8.5; y = 4 - 10 + 3 + 20 = 17; z = 7 - 16 + 4.5 + 30 = 25.5
        Assert.Equal(0, bind.VecSets[0].Slot);
        Assert.Equal(new float[] { 8.5f, 17f, 25.5f }, bind.VecSets[0].Values);
    }

    [Fact]
    public void TheColourIsScaledByTheIntensity()
    {
        (ParticleSphereProxy proxy, _) = Make();
        var bind = new MaterialProxyFactsTests.FakeBind
        {
            Arg = 1,
            Particle = new ParticleBindLight(Vector.Zero, new Vector(0.5f, 0.25f, 1), 4, ModelView()),
        };
        proxy.Bind(bind);
        Assert.Equal(["0 vecn 3", "1 vecn 3"], bind.Ops);
        Assert.Equal(new float[] { 2f, 1f, 4f }, bind.VecSets[1].Values);
    }

    // Stock keeps the dummy var FindVar answers for a missing name and writes to it;
    // a var with no slot at all (-1) is the only one skipped.
    [Fact]
    public void AVarStockDidNotFindStillTakesItsWrite()
    {
        (ParticleSphereProxy proxy, _) = Make("$light_color");
        var bind = new MaterialProxyFactsTests.FakeBind { Arg = 1, Particle = new ParticleBindLight(Vector.Zero, Vector.Zero, 1, ModelView()) };
        proxy.Bind(bind);
        Assert.Equal(2, bind.Ops.Count);
    }
}
