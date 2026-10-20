namespace SourceSharp.Abi;

/// <summary>
/// A server engine with nothing native behind it, for a fact: a side over the
/// default areas (<see cref="TestSides"/>) and its container, composed as the
/// game composes one. What a registry built with no game composition binds and
/// stamps with. It lived in the product as <c>ServerEntityEngine.Unattached</c>,
/// over areas that answered through attach checks; those checks are gone
/// (plan_shim_areas_split phase 11 review), so the defaults are the tests'.
/// </summary>
internal static class TestEngines
{
    /// <summary>A new server side over the default areas, and its engine container.</summary>
    public static (ServiceContainer Engine, ServerEntityEngine Side) Server()
    {
        var side = TestSides.Create(Side.Server);
        EngineServerBinding.Bind(side);
        var engine = new ServiceContainer();

        // The render binding takes the side's mirror in its constructor.
        engine.AddInstance(new StandaloneMirror());
        EngineServices.AddTo(engine, side);

        // And its clock, as the game's composition registers one: what a
        // registry stamps outputs, thinks and saves with.
        engine.AddInstance<IGameClock>(new ModuleEngine(side).Clock());
        return (engine, ServerEntityEngine.For(side));
    }
}
