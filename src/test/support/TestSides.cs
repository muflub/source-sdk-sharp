namespace SourceSharp.Abi;

/// <summary>
/// A side with no engine behind it, for a fact: every shim area is the
/// generated default (ShimAreaDefaults.g.cs), which answers its type's
/// default, so the side and everything composed over it run as they would
/// over an engine that has nothing to say. Product code never asks whether
/// an engine is attached (plan_shim_areas_split phase 11 review), so this is
/// how a test without one is built.
/// </summary>
internal static class TestSides
{
    /// <summary>A new <paramref name="side"/> over that side's default areas, with a state of its own.</summary>
    public static EngineSide Create(Side side) => Create(side, new SideState(side));

    /// <summary>A new <paramref name="side"/> over that side's default areas and <paramref name="state"/>.</summary>
    public static EngineSide Create(Side side, SideState state)
    {
        // This interop's own side's areas, whatever side is asked for: a side
        // object of the server's interop holds the server's areas even when a
        // fact names it the client, as the interop's own registration does.
        var areas = new ServiceContainer();
#if TEST_SUPPORT_SERVER
        ServerAreaDefaults.AddTo((type, area) => areas.AddInstance(type, area));
#else
        ClientAreaDefaults.AddTo((type, area) => areas.AddInstance(type, area));
#endif
        EngineSide engine = EngineSide.Create(side, state, areas);
        DefaultForwarders.HoldOn(engine);
        return engine;
    }
}
