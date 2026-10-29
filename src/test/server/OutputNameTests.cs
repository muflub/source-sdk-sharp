using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// An output whose field cannot carry its map name (it would hide a member:
/// OnDamaged, OnEndTouch, OnSpawn) declares the name on the field, so a class
/// is described under its map names before any instance exists. Found by the
/// contract oracle (2026-10-27): five classes whose constructors take services
/// were described as Damaged, OnLeft and OnRelaySpawn, and their real outputs
/// sat in the contract baseline as gaps.
/// </summary>
public class OutputNameTests
{
    [Theory]
    [InlineData(EntityNames.FuncButton, "OnDamaged", "Damaged")]
    [InlineData(EntityNames.FuncPhysbox, "OnDamaged", "Damaged")]
    [InlineData(EntityNames.FuncBreakableSurf, "OnDamaged", "Damaged")]
    [InlineData(EntityNames.LogicRelay, "OnSpawn", "OnRelaySpawn")]
    [InlineData(EntityNames.TriggerMultiple, "OnEndTouch", "OnLeft")]
    public void A_class_is_described_under_its_outputs_map_names_before_any_instance(string className, string declared, string field)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(BaseButton).Assembly);

        var outputs = registry.DescribeClasses().Single(c => c.ClassName == className).Outputs;

        Assert.Contains(declared, outputs);
        Assert.DoesNotContain(field, outputs);
    }
}
