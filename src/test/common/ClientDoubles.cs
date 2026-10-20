extern alias client;
using C = client::SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// The client's copy of the doubles <see cref="TestServices"/> hands out.
///
/// <para>
/// Each side compiles the shared interop for itself (plan_shim_areas_split
/// phase 3), so the engine areas, the service container and the doubles that
/// implement them exist once per side, and a client object takes only the
/// client's. In the cross-side suite <see cref="TestServices"/> builds the
/// server's (its unaliased names are the server's); a fact that hands a double
/// to client code takes it from here. In the client suite <c>client::</c> names
/// the one client, so this and <see cref="TestServices"/> build the same thing.
/// </para>
/// </summary>
internal static class ClientDoubles
{
    /// <summary>The client's strict double with the universal reads stubbed (<see cref="TestServices.Mock()"/>).</summary>
    public static C.RecordingEngine Fake() => C.RecordingEngine.Default();

    /// <summary>A client container over a fresh <see cref="Fake"/>, as <see cref="TestServices.Services(object[])"/>.</summary>
    public static C.ServiceContainer Services(params object[] extras) => Services(out _, extras);

    /// <summary>A client container over a fresh <see cref="Fake"/>, which comes back out.</summary>
    public static C.ServiceContainer Services(out C.RecordingEngine engine, params object[] extras)
    {
        engine = Fake();
        var services = Areas(engine);
        services.AddInstance<C.IGameClock>(engine);
        foreach (var extra in extras)
            services.AddInstance(extra.GetType(), extra);
        return services;
    }

    /// <summary>Every engine area of the client's container answered by <paramref name="engine"/>.</summary>
    public static C.ServiceContainer Areas(C.RecordingEngine engine)
    {
        var services = new C.ServiceContainer();
        foreach (var (contract, _) in C.EngineServices.Areas(new C.RecordingLog()))
            services.AddInstance(contract, engine);
        services.AddInstance<C.IUserMessageSender>(engine);
        services.AddInstance<SourceSharp.Abi.ILog>(new C.RecordingLog());
        services.AddInstance(new C.ConsoleLines());
        return services;
    }

    /// <summary>
    /// <c>Require</c> on a client container. The locator's extension methods are
    /// shared interop too, and in the cross-side suite the unaliased ones are
    /// the server's, which a client container does not satisfy.
    /// </summary>
    public static T Require<T>(C.IServiceLocator services) where T : class
        => C.ServiceLocatorExtensions.Require<T>(services);

    /// <summary>
    /// The client's engine with nothing native behind it: the side's bindings
    /// over a side of the default areas (<c>TestSides</c>), its mirror and its
    /// clock. What a client registry under test is built over when the fact is
    /// about the registry, not the engine (the server's counterpart is
    /// <c>TestEngines.Server</c>).
    /// </summary>
    public static C.ServiceContainer UnattachedEngine()
    {
        var side = C.TestSides.Create(SourceSharp.Abi.Side.Client);
        var engine = new C.ServiceContainer();
        engine.AddInstance(new C.StandaloneMirror());
        C.EngineServices.AddTo(engine, side);
        engine.AddInstance<C.IGameClock>(new C.ModuleEngine(side).Clock());
        return engine;
    }
}
