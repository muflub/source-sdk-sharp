using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine interfaces a server composition is handed (EngineServices.AddTo):
/// non-null forwarders over the side's bound slots, acquired on first call.
/// Reached through the registration, as a consumer is handed them.
/// </summary>
public class BoundForwarderTests
{
    private static ServiceContainer Registered(EngineSide side)
    {
        var services = new ServiceContainer();
        EngineServices.AddForwarders(services, side);
        return services;
    }

    [Fact]
    public void EveryEngineInterfaceIsRegisteredWithoutAskingTheEngine()
    {
        // A side whose factory refuses everything (no native half, no
        // defaults): registering must not ask it, or this would throw here.
        ServiceContainer services = Registered(TestSides.WithoutForwarders(Side.Server));

        Assert.NotNull(services.Require<IUniformRandom>());
        Assert.NotNull(services.Require<IFwdEngineServer>());
        Assert.NotNull(services.Require<IFwdEngineSoundServer>());
        Assert.NotNull(services.Require<IFwdFileSystem>());
        Assert.NotNull(services.Require<IFwdSoundEmitter>());
        Assert.NotNull(services.Require<IFwdPhysicsSurfaceProps>());
        Assert.NotNull(services.Require<IFwdBaseFileSystem>());
        Assert.NotNull(services.Require<IFwdModelInfo>());
        Assert.NotNull(services.Require<IFwdDebugOverlay>());
    }

    private sealed class Durations : IFwdEngineSoundServer
    {
        public readonly List<string> Asked = [];

        public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity) { }
        public void StopSound(int entIndex, int channel, string sample) { }

        public float SoundDuration(string sample)
        {
            Asked.Add(sample);
            return 2.5f;
        }

        public bool PrecacheSound(string sample, bool preload) => false;
        public bool IsSoundPrecached(string sample) => false;
        public void PrefetchSound(string sample) { }
    }

    [Fact]
    public void ARegisteredInterfaceAnswersFromTheSidesSlot()
    {
        EngineSide side = TestSides.WithoutForwarders(Side.Server);
        var sounds = new Durations();
        side.Bound.SoundServer.Hold(sounds);

        float seconds = Registered(side).Require<IFwdEngineSoundServer>().SoundDuration("Doors.Move1");

        Assert.Equal((2.5f, "Doors.Move1"), (seconds, Assert.Single(sounds.Asked)));
    }

    [Fact]
    public void ARefusedInterfaceThrowsNamingItAtTheCall()
    {
        IFwdEngineSoundServer sound = Registered(TestSides.WithoutForwarders(Side.Server)).Require<IFwdEngineSoundServer>();

        string why = Assert.Throws<InterfaceAcquisitionException>(() => sound.SoundDuration("Doors.Move1")).Message;

        Assert.Contains(EngineSoundServer.InterfaceVersion, why);
    }

    [Fact]
    public void ASideTheEngineServesNoOverlayDrawsNothingRatherThanThrowing()
    {
        // A dedicated server has no VDebugOverlay003: what draws text is handed
        // one that draws nothing, never a null or a throw.
        EngineSide side = TestSides.WithoutForwarders(Side.Server);
        IFwdDebugOverlay overlay = Registered(side).Require<IFwdDebugOverlay>();

        overlay.TextAtPosition(Vector.Zero, 0, 1f, 255, 255, 255, 255, "hello");

        Assert.Throws<InterfaceAcquisitionException>(() => EngineBound.SoundEmitter(side));
    }
}
