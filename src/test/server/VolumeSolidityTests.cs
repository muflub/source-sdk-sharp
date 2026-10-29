using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The brush volumes stock leaves SOLID_NONE (out of every partition list),
/// and the one it makes a trigger. Found beside stock on ss_sandbox
/// (2026-10-27): here each was a not-solid SOLID_BSP brush, which no trace
/// meets either, but which sat in the partition stock keeps it out of.
/// </summary>
public class VolumeSolidityTests
{
    private static readonly EntityHandle Volume = new(0x0004_0351u);

    private static RecordingEngine Spawned(string className)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncIllusionary).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Precache.PrecacheModel", 3)
              .Returns("EntityRender.GetModelKeyValueText", null);

        registry.KeyValue(Volume, className, "model", "*3");
        registry.Spawn(Volume, className);
        return engine;
    }

    [Theory]
    [InlineData(EntityNames.FuncIllusionary, SolidFlags.None)]
    [InlineData(EntityNames.FuncOccluder, SolidFlags.None)]
    [InlineData(EntityNames.FuncPrecipitation, SolidFlags.None)]
    [InlineData(EntityNames.FuncSmokevolume, SolidFlags.None)]
    [InlineData(EntityNames.FuncTraincontrols, SolidFlags.None)]
    [InlineData(EntityNames.GameZonePlayer, SolidFlags.None)]
    [InlineData(EntityNames.Trigger, SolidFlags.None)]
    [InlineData(EntityNames.EnvBubbles, SolidFlags.None)]
    [InlineData(EntityNames.EnvEmbers, SolidFlags.None)]
    [InlineData(EntityNames.FuncDustcloud, SolidFlags.VolumeContents)]
    [InlineData(EntityNames.FuncDustmotes, SolidFlags.VolumeContents)]
    [InlineData(EntityNames.PropDynamicOrnament, SolidFlags.NotSolid)]
    public void It_is_solid_none_with_stocks_flags(string className, SolidFlags flags)
    {
        var engine = Spawned(className);

        var set = engine.Calls.Last(c => c.Member is "Entities.SetSolidity" or "EntityPhysics.SetBrushSolidity");
        Assert.Equal("Entities.SetSolidity", set.Member);
        Assert.Equal((SolidType.None, flags), (set.ArgAs<SolidType>(1), set.ArgAs<SolidFlags>(2)));
    }

    [Fact]
    public void A_colour_correction_volume_is_a_trigger_as_stock_s_InitTrigger_makes_it()
    {
        var engine = Spawned(EntityNames.ColorCorrectionVolume);

        var set = engine.Calls.Last(c => c.Member == "EntityPhysics.SetBrushSolidity");
        Assert.Equal(BrushSolidity.Trigger, set.ArgAs<BrushSolidity>(1));
    }
}
