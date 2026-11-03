using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// <c>TeleporterSend</c>'s speed boost arm (tf_obj_teleporter.cpp:153-158):
/// the builder's <c>mod_teleporter_speed_boost</c>, read with
/// <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c> on the builder's own manager, buys
/// the traveller four seconds of <c>TF_COND_SPEED_BOOST</c> at the send.
/// </summary>
public class TFTeleporterSpeedBoostFacts
{
    [Fact]
    public void TheBuildersBoostAttributeSendsTheTravellerOffFast()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);   // the pair to READY
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        rig.Engine.Returns("Entities.GetIndex", 7);
        rig.Owners.Managers[7] = rig.AttributeItem("mod_teleporter_speed_boost",
                                                   AttributeDescriptionFormat.Additive, 1f);

        entrance.OnTouch(rider);
        TeleporterHarness.Tick(rig, entrance, exit, 0.2f);   // the send

        // The provider is AddCond's default handle, whose EntryIndex is the
        // invalid answer (Entity.cs:121).
        Assert.Contains($"add {rider.EntryIndex} SpeedBoost d4 p{default(EntityHandle).EntryIndex}",
                        rig.HealingConditions.Calls);
    }

    [Fact]
    public void WithoutTheAttributeNobodyGetsTheBoost()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        TeleporterHarness.Tick(rig, entrance, exit, 1f);
        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        rig.Engine.Returns("Entities.GetIndex", 7);   // the builder asks, and carries nothing

        entrance.OnTouch(rider);
        TeleporterHarness.Tick(rig, entrance, exit, 0.2f);

        Assert.Empty(rig.HealingConditions.Calls);   // no send-time conditions at all
    }
}
