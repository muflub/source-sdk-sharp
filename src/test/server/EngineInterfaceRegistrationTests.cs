using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine interfaces a server composition is handed
/// (EngineServices.AddForwarders, from AddTo): acquired as they are
/// registered, each the object the side's bound slot holds, never null.
/// </summary>
public class EngineInterfaceRegistrationTests
{
    private static ServiceContainer Registered(EngineSide side)
    {
        var services = new ServiceContainer();
        EngineServices.AddForwarders(services, side);
        return services;
    }

    [Fact]
    public void ATestSideRegistersEveryEngineInterfaceAsItsDefault()
    {
        ServiceContainer services = Registered(TestSides.Create(Side.Server));

        Assert.IsType<DefaultForwarders.LowestRandom>(services.Require<IUniformRandom>());
        Assert.IsType<DefaultForwarders.NoEngineServer>(services.Require<IFwdEngineServer>());
        Assert.IsType<DefaultForwarders.NoSoundServer>(services.Require<IFwdEngineSoundServer>());
        Assert.IsType<DefaultForwarders.NoFileSystem>(services.Require<IFwdFileSystem>());
        Assert.IsType<DefaultForwarders.NoSoundEmitter>(services.Require<IFwdSoundEmitter>());
        Assert.IsType<DefaultForwarders.NoSurfaceProps>(services.Require<IFwdPhysicsSurfaceProps>());
        Assert.IsType<DefaultForwarders.NoFiles>(services.Require<IFwdBaseFileSystem>());
        Assert.IsType<DefaultForwarders.NoModelInfo>(services.Require<IFwdModelInfo>());
        Assert.IsType<DefaultForwarders.NoOverlay>(services.Require<IFwdDebugOverlay>());
    }

    private sealed class Durations : IFwdEngineSoundServer
    {
        public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity) { }
        public void StopSound(int entIndex, int channel, string sample) { }
        public float SoundDuration(string sample) => 2.5f;
        public bool PrecacheSound(string sample, bool preload) => false;
        public bool IsSoundPrecached(string sample) => false;
        public void PrefetchSound(string sample) { }
    }

    [Fact]
    public void TheRegisteredInterfaceIsTheObjectTheSlotHolds()
    {
        // No forwarder between the consumer and the engine's wrapper: what is
        // registered is what the slot acquired, so a call is one call.
        EngineSide side = TestSides.Create(Side.Server);
        var sounds = new Durations();
        side.Bound.SoundServer.Hold(sounds);

        Assert.Same(sounds, Registered(side).Require<IFwdEngineSoundServer>());
    }

    [Fact]
    public void TheEngineServersSlicesAreTheEngineServerItself()
    {
        ServiceContainer services = Registered(TestSides.Create(Side.Server));
        IFwdEngineServer server = services.Require<IFwdEngineServer>();

        Assert.Same(server, services.Require<ISaveMemory>());
        Assert.Same(server, services.Require<SourceSharp.Server.Interop.IAreaPortalEngine>());
        Assert.Same(services.Require<IFwdPhysicsSurfaceProps>(), services.Require<IVPhysicsSurfaceSounds>());
    }

    [Fact]
    public void AnInterfaceTheEngineRefusesFailsTheRegistrationNamingIt()
    {
        // A side whose factory refuses everything (no native half, no
        // defaults): the composition stops at the first interface, by name,
        // as native's Init refuses to start without one.
        string why = Assert.Throws<InterfaceAcquisitionException>(
            () => Registered(TestSides.WithoutForwarders(Side.Server))).Message;

        Assert.Contains(UniformRandomStream.InterfaceVersion, why);
    }

    [Fact]
    public void ASideTheEngineServesNoOverlayIsHandedOneThatDrawsNothing()
    {
        // A dedicated server has no VDebugOverlay003: what draws text is handed
        // one that draws nothing, never a null or a throw.
        IFwdDebugOverlay overlay = EngineBound.DebugOverlay(TestSides.WithoutForwarders(Side.Server));

        overlay.TextAtPosition(Vector.Zero, 0, 1f, 255, 255, 255, 255, "hello");

        Assert.IsType<NoDebugOverlay>(overlay);
    }
}
