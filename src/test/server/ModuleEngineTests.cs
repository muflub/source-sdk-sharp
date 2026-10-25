using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D8: a module is composed over the <see cref="ModuleEngine"/>
/// it is handed, never over a side the static <c>Engine</c> looks up. Two
/// compositions over two engines share nothing: each one's areas, clock and
/// directory answer from its own engine.
/// </summary>
public class ModuleEngineTests
{
    private static CGlobalVarsRaw Globals(float curtime)
    {
        CGlobalVarsRaw raw = default;
        raw.nTimestampNetworkingBase = 100;
        raw.nTimestampRandomizeWindow = 32;
        raw.m_bClient = 0;
        raw.curtime = curtime;
        return raw;
    }

    [Fact]
    public void TwoCompositionsReadTheirOwnEnginesManagedDirectory()
    {
        var first = TestSides.Create(Side.Server);
        first.Attachment.ManagedDirectory = "first/bin/managed";
        var second = TestSides.Create(Side.Server);
        second.Attachment.ManagedDirectory = "second/bin/managed";

        var firstServices = new ServiceContainer();
        var secondServices = new ServiceContainer();
        firstServices.AddInstance(new StandaloneMirror());
        secondServices.AddInstance(new StandaloneMirror());

        Assert.True(new ServerModuleEngine(first).AddTo(firstServices) > 20, "every engine area registered");
        new ServerModuleEngine(second).AddTo(secondServices);

        Assert.Equal("first/bin/managed", firstServices.Require<IEngineHost>().ManagedDirectory);
        Assert.Equal("second/bin/managed", secondServices.Require<IEngineHost>().ManagedDirectory);
    }

    [Fact]
    public unsafe void TwoClocksReadTheirOwnEnginesGlobals()
    {
        CGlobalVarsRaw early = Globals(curtime: 3f);
        CGlobalVarsRaw late = Globals(curtime: 7.5f);

        var first = TestSides.Create(Side.Server);
        var second = TestSides.Create(Side.Server);
        first.BindGlobalVars(Side.Server, (nint)(&early));
        second.BindGlobalVars(Side.Server, (nint)(&late));

        Assert.Equal(3f, new ServerModuleEngine(first).Clock().Now);
        Assert.Equal(7.5f, new ServerModuleEngine(second).Clock().Now);
    }

    [Fact]
    public void LivenessAttachesToTheHandedEngineOnly()
    {
        var first = TestSides.Create(Side.Server);
        var second = TestSides.Create(Side.Server);
        var mirror = new LivenessMirror();

        using (new ServerModuleEngine(first).AttachLiveness(mirror))
        {
            Assert.Same(mirror, first.Liveness);
            Assert.Null(second.Liveness);
        }

        Assert.Null(first.Liveness);
    }

    [Fact]
    public void AModuleEngineSaysWhichSideItIs()
    {
        Assert.Equal(Side.Client, new ServerModuleEngine(TestSides.Create(Side.Client)).Side);
        Assert.Equal(Side.Server, new ServerModuleEngine(TestSides.Create(Side.Server)).Side);
    }

    [Fact]
    public void AHostAnswersTheSideItIsBoundTo()
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, TestSides.Create(Side.Client));

        Assert.Equal(Side.Client, services.Require<IEngineHost>().ActiveSide);
    }

    [Fact]
    public void EachEngineAcquiresThroughAFactoryOfItsOwn()
    {
        var first = TestSides.Create(Side.Server);
        var second = TestSides.Create(Side.Server);

        Assert.Same(first.Interfaces, first.Interfaces);
        Assert.NotSame(first.Interfaces, second.Interfaces);
    }
}
