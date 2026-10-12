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
        var first = new EngineSide(Side.Server) { ManagedDirectory = "first/bin/managed" };
        var second = new EngineSide(Side.Server) { ManagedDirectory = "second/bin/managed" };

        var firstServices = new ServiceContainer();
        var secondServices = new ServiceContainer();
        firstServices.AddInstance(new StandaloneMirror());
        secondServices.AddInstance(new StandaloneMirror());

        Assert.True(new ModuleEngine(first).AddTo(firstServices) > 20, "every engine area registered");
        new ModuleEngine(second).AddTo(secondServices);

        Assert.Equal("first/bin/managed", firstServices.Require<IEngineHost>().ManagedDirectory);
        Assert.Equal("second/bin/managed", secondServices.Require<IEngineHost>().ManagedDirectory);
    }

    [Fact]
    public unsafe void TwoClocksReadTheirOwnEnginesGlobals()
    {
        CGlobalVarsRaw early = Globals(curtime: 3f);
        CGlobalVarsRaw late = Globals(curtime: 7.5f);

        var first = new EngineSide(Side.Server);
        var second = new EngineSide(Side.Server);
        first.BindGlobalVars(Side.Server, (nint)(&early));
        second.BindGlobalVars(Side.Server, (nint)(&late));

        Assert.Equal(3f, new ModuleEngine(first).Clock().Now);
        Assert.Equal(7.5f, new ModuleEngine(second).Clock().Now);
    }

    [Fact]
    public void LivenessAttachesToTheHandedEngineOnly()
    {
        var first = new EngineSide(Side.Server);
        var second = new EngineSide(Side.Server);
        var mirror = new LivenessMirror();

        using (new ModuleEngine(first).AttachLiveness(mirror))
        {
            Assert.Same(mirror, first.Liveness);
            Assert.Null(second.Liveness);
        }

        Assert.Null(first.Liveness);
    }

    [Fact]
    public void AModuleEngineSaysWhichSideItIs()
    {
        Assert.Equal(Side.Client, new ModuleEngine(new EngineSide(Side.Client)).Side);
        Assert.Equal(Side.Server, new ModuleEngine(new EngineSide(Side.Server)).Side);
    }

    [Fact]
    public void AHostAnswersTheSideItIsBoundTo()
    {
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, new EngineSide(Side.Client));

        Assert.Equal(Side.Client, services.Require<IEngineHost>().ActiveSide);
    }

    [Fact]
    public void EachEngineAcquiresThroughAFactoryOfItsOwn()
    {
        var first = new EngineSide(Side.Server);
        var second = new EngineSide(Side.Server);

        Assert.Same(first.Interfaces, first.Interfaces);
        Assert.NotSame(first.Interfaces, second.Interfaces);
    }
}
