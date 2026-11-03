extern alias client;

using Xunit;

using ClientClass = client::TF2.Shared.TFClass;
using ClientCond = client::TF2.Shared.Conditions.TFConditions;
using ClientOffset = client::TF2.Client.Weapons.TFViewmodelOffset;
using ClientMin = client::TF2.Client.TFUseMinViewmodelsConVar;
using ClientOverride = client::TF2.Client.TFViewmodelsOffsetOverrideConVar;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using Econ = client::TF2.Shared.Econ;
using OwnersContract = client::TF2.Shared.ITFWeaponOwners;

namespace TF2.Tests.Weapons;

/// <summary>
/// The viewmodels' min mode (tf_viewmodel.cpp:195-221): the offset triple the
/// pin's host adds along the eye's axes — the override when it stands, else
/// the active weapon's <c>min_viewmodel_offset</c> — and only under
/// <c>tf_use_min_viewmodels</c> or a nonempty override.
/// </summary>
public class TFViewmodelOffsetFacts
{
    [Fact]
    public void Unset_the_offset_stands_down()
    {
        var rig = Rig(min: "0", forced: "");
        rig.Give("10 0 -7");

        Assert.False(rig.Pluck());
    }

    [Fact]
    public void The_min_mode_holds_the_items_offset()
    {
        var rig = Rig(min: "1");
        rig.Give("5 -3 -2");

        Assert.True(rig.Pluck());
        Assert.Equal(new[] { 5f, -3f, -2f }, rig.Taken);
    }

    [Fact]
    public void The_min_mode_without_an_item_offsets_nothing()
    {
        var rig = Rig(min: "1");

        Assert.True(rig.Pluck());
        Assert.Equal(new float[] { 0, 0, 0 }, rig.Taken);
    }

    [Fact]
    public void The_override_overrides_the_item()
    {
        var rig = Rig(min: "1", forced: "8 0 -6");
        rig.Give("5 -3 -2");

        Assert.True(rig.Pluck());
        Assert.Equal(new[] { 8f, 0f, -6f }, rig.Taken);
    }

    [Fact]
    public void The_override_stands_alone()
    {
        var rig = Rig(min: "0", forced: "8 0 -6");

        Assert.True(rig.Pluck());
        Assert.Equal(new[] { 8f, 0f, -6f }, rig.Taken);
    }

    [Fact]
    public void A_partial_override_zeroes_its_tail()
    {
        var rig = Rig(min: "0", forced: "8");

        Assert.True(rig.Pluck());
        Assert.Equal(new[] { 8f, 0f, 0f }, rig.Taken);
    }

    [Fact]
    public void A_nonsense_number_reads_zero()
    {
        var rig = Rig(min: "1");
        rig.Give("x 0 -7");

        Assert.True(rig.Pluck());
        Assert.Equal(new float[] { 0, 0, -7 }, rig.Taken);
    }

    // ---------------------------------------------------------------------

    private static OffsetRig Rig(string min, string forced = "")
    {
        var engine = new ClientEngine();
        engine.Returns("Hud.LocalPlayerIndex", 7);

        var minConVar = new ClientMin();
        minConVar.Bind(-1, "tf_use_min_viewmodels", min);
        var overrideConVar = new ClientOverride();
        overrideConVar.Bind(-1, "tf_viewmodels_offset_override", forced);

        var owners = new RigOwners();
        return new OffsetRig(new ClientOffset(minConVar, overrideConVar, engine, owners), owners);
    }

    private sealed class RigOwners : OwnersContract
    {
        private int _nextDef = 9000;

        /// <summary>The active weapon's item, or null.</summary>
        public Econ.IAttributeProvider? Item { get; private set; }

        public void Give(string value)
        {
            var container = new Econ.AttributeContainer(null, Econ.AttributeProviderType.Generic);
            container.Set(new Econ.AttributeValue(
                new Econ.AttributeDefinition(_nextDef++, "min viewmodel offset", "min_viewmodel_offset",
                                             Econ.AttributeDescriptionFormat.None, Econ.AttributeEffectType.Neutral,
                                             false, false, Econ.AttributeValueType.String, Econ.MergedKeys.Block("x")),
                0, value));
            Item = container;
        }

        public Econ.IAttributeProvider? ActiveItemOf(int playerIndex) => Item;

        public ClientClass ClassOf(int playerIndex) => ClientClass.Undefined;

        public ClientCond? ConditionsOf(int playerIndex) => null;

        public float CritMult(int playerIndex) => 1f;

        public bool WinsCrits(int playerIndex) => false;

        public Econ.IAttributeProvider? ItemOf(int playerIndex, string weaponName) => Item;

        public Econ.IAttributeProvider? ManagerOf(int playerIndex) => null;

        public bool OffGround(int playerIndex) => false;

        public bool EyesUnderwater(int playerIndex) => false;

        public bool DuckingOf(int playerIndex) => false;
    }

    private sealed class OffsetRig(ClientOffset offset, RigOwners owners)
    {
        public readonly float[] Taken = new float[3];

        public void Give(string value) => owners.Give(value);

        public bool Pluck() => offset.Offset(Taken);
    }
}
