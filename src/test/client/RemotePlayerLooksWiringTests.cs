using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's composition gives every player the other players' networked
/// looks and steps them once a frame: a <see cref="ClientSsPlayer"/> built by
/// the activator the entity registry uses, over the composed container, holds
/// the container's <see cref="RemotePlayerLooks"/>, and that same instance is
/// in the frame-think chain. Without either, a remote player is drawn level
/// while the server poses his hitboxes at his look.
/// </summary>
public class RemotePlayerLooksWiringTests
{
    private static ClientModule Composed()
    {
        var module = new ClientModule(new ClientModuleEngine(TestSides.Create(Side.Client, new SideState(Side.Client))),
                                      new FakeClientEngineAdapters());
        module.ComposeEngineAreas();
        // The entity registry and the clock join at Initialize, which needs an
        // engine: stand-ins take their places.
        var services = (ServiceContainer)module.Services;
        services.AddInstance<IClientStandalones>(new NoPlayers());
        services.AddInstance<IGameClock>(new Clock());
        return module;
    }

    private sealed class Clock : IGameClock
    {
        public float Now => 1f;
    }

    private sealed class NoPlayers : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => [];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    [Fact]
    public void AComposedPlayerHoldsTheLooksTheFrameThinksStep()
    {
        ClientModule module = Composed();
        var looks = module.Services.Require<RemotePlayerLooks>();
        var player = (ClientSsPlayer)ServiceActivator.For(typeof(ClientSsPlayer)).Create(module.Services);

        Assert.Same(looks, player.Looks);
        // The chain as Initialize builds it (new Hooks over the module's container).
        var thinks = new Hooks<IClientFrameThink>(module.Services, new RecordingLog());
        Assert.Contains(thinks.All, t => ReferenceEquals(t, looks));
    }
}
