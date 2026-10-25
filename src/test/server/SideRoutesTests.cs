using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's late routes (plan_engineside_removal Phase 3): made empty by
/// the side, registered by its container before the bindings, filled by the
/// composition through the routing helpers, and never shared between two
/// compositions.
/// </summary>
public class SideRoutesTests
{
    private sealed class Sounds : IServerSoundRoute
    {
        public void EntityEmitSound(EntityHandle entity, string soundName) { }
        public void EntityStopSound(EntityHandle entity, string soundName) { }
        public void EmitSoundAtPosition(string soundName, Vector origin) { }
        public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume) => false;
        public void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch) { }
        public int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel) => 0;
        public void SoundPatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption) { }
        public void SoundPatchStop(int patch) { }
        public void SoundPatchState(int patch, out int entIndex, out bool filterActive) => (entIndex, filterActive) = (0, false);
        public void SoundPatchFree(int patch) { }
        public void PrecacheSound(string soundName) { }
    }

    private static (EngineSide Side, ServiceContainer Services) Composed()
    {
        EngineSide side = TestSides.Create(Side.Server);
        var services = new ServiceContainer();
        EngineServices.AddBindingServices(services, side);
        return (side, services);
    }

    [Fact]
    public void AnEmptyRouteAnswersNothingUntilFilledAndAgainOnceEmptied()
    {
        var route = new SideRoute<IServerSoundRoute>();
        var sounds = new Sounds();

        Assert.Null(route.Target);
        route.Fill(sounds);
        Assert.Same(sounds, route.Target);
        route.Fill(null);
        Assert.Null(route.Target);
    }

    [Fact]
    public void ARouteWithAnEmptyAnswerAnswersItUntilFilledAndAgainOnceEmptied()
    {
        var own = new ServerTraceCost();
        var registered = new ServerTraceCost();
        var route = new SideRoute<ServerTraceCost>(own);

        Assert.Same(own, route.Target);
        route.Fill(registered);
        Assert.Same(registered, route.Target);
        route.Fill(null);
        Assert.Same(own, route.Target);
    }

    /// <summary>
    /// The six are the side's own instances in its container, and each reads
    /// as the empty route did before any composition filled it: no sound, no
    /// tracer, no movement, no physics init, nobody told of a teleport, and
    /// trace meters of the side's own.
    /// </summary>
    [Fact]
    public void TheSixRoutesAreRegisteredEmptyAsTheSidesOwn()
    {
        var (side, services) = Composed();

        Assert.Same(side.ServerSounds, services.Require<SideRoute<IServerSoundRoute>>());
        Assert.Same(side.Tracer, services.Require<SideRoute<IServerTraceRoute>>());
        Assert.Same(side.PlayerMoves, services.Require<SideRoute<IServerPlayerMovementRoute>>());
        Assert.Same(side.PhysicsInit, services.Require<SideRoute<IEntityPhysicsRoute>>());
        Assert.Same(side.Teleported, services.Require<SideRoute<Action<EntityHandle, Vector, bool>>>());
        Assert.Same(side.TraceCost, services.Require<SideRoute<ServerTraceCost>>());

        Assert.Null(side.ServerSounds.Target);
        Assert.Null(side.Tracer.Target);
        Assert.Null(side.PlayerMoves.Target);
        Assert.Null(side.PhysicsInit.Target);
        Assert.Null(side.Teleported.Target);
        Assert.NotNull(side.TraceCost.Target);
    }

    /// <summary>The routing helpers fill the routes the composition registered.</summary>
    [Fact]
    public void TheRoutingHelpersFillTheRegisteredRoutes()
    {
        var (side, services) = Composed();
        var sounds = new Sounds();
        var cost = new ServerTraceCost();

        ServerSoundRouting.Route(services, sounds);
        ServerTraceRouting.RouteCost(services, cost);

        Assert.Same(sounds, side.ServerSounds.Target);
        Assert.Same(cost, side.TraceCost.Target);

        ServerSoundRouting.Route(services, null);
        Assert.Null(side.ServerSounds.Target);
    }

    /// <summary>Two compositions' routes are their own: filling one fills nothing of the other's.</summary>
    [Fact]
    public void TwoCompositionsRoutesAreSeparate()
    {
        var (first, firstServices) = Composed();
        var (second, secondServices) = Composed();

        ServerSoundRouting.Route(firstServices, new Sounds());
        ServerTraceRouting.RouteCost(firstServices, new ServerTraceCost());

        Assert.NotNull(first.ServerSounds.Target);   // the stimulus landed
        Assert.NotSame(firstServices.Require<SideRoute<IServerSoundRoute>>(),
                       secondServices.Require<SideRoute<IServerSoundRoute>>());
        Assert.Null(second.ServerSounds.Target);
        Assert.NotSame(first.TraceCost.Target, second.TraceCost.Target);
    }
}
