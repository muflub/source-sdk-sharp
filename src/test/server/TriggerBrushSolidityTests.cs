using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// trigger_brush is solid as well as touched: CTriggerBrush::Spawn
/// (modelentities.cpp) sets SOLID_BSP and FSOLID_TRIGGER and never
/// FSOLID_NOT_SOLID. Found by stock-ab's solidity census (2026-10-26):
/// stock's flags 0x8, here 0xc, a volume walked through.
/// </summary>
public class TriggerBrushSolidityTests
{
    [Fact]
    public void A_trigger_brush_is_a_solid_trigger()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerBrush).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        registry.Spawn(new EntityHandle(0x0004_0361u), EntityNames.TriggerBrush);

        var last = engine.Last("Entities.SetSolidity")!.Value;
        Assert.Equal((SolidType.Bsp, SolidFlags.Trigger), (last.ArgAs<SolidType>(1), last.ArgAs<SolidFlags>(2)));
    }
}
