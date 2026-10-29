using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// func_areaportalwindow's solidity: CFuncAreaPortalWindow::Spawn
/// (func_areaportalwindow.cpp) never sets a solid type, so the window is
/// SOLID_NONE. Found by stock-ab's solidity census (2026-10-26): here it kept
/// a brush's SOLID_BSP and stood as an invisible wall.
/// </summary>
public class FuncAreaPortalWindowTests
{
    private static readonly EntityHandle Window = new(0x0004_0331u);

    [Fact]
    public void A_window_is_nothing_to_walk_into()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncAreaPortalWindow).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IAreaPortals>(new AreaPortals(new DefaultForwarders.NoEngineServer()));
        registry.Services = services;
        engine.Strict = false;

        registry.Spawn(Window, EntityNames.FuncAreaportalwindow);

        var set = Assert.Single(engine.AllOf("Entities.SetSolidity"));
        Assert.Equal((SolidType.None, SolidFlags.None), (set.ArgAs<SolidType>(1), set.ArgAs<SolidFlags>(2)));
        Assert.False(engine.Called("EntityPhysics.SetBrushSolidity"));
    }
}
