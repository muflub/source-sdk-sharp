using SourceSharp.Abi;
using SourceSharp.Client.Input;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The charge cap as the client input asks it (stock's <c>CAM_CapYaw</c>,
/// tf_input_main.cpp:48-65): the mouse's yaw amount passes through the mods'
/// clamp while the shield charge holds — and a side whose mod registers no
/// rule leaves every yaw unchanged (:64), the base's answer. The clamp here
/// is the shape <c>TFChargeCap</c> answers at a tick-sized frame; the
/// fold's job, asked once per yaw amount, is to put it in the way.
/// </summary>
public sealed class InputChargeCapTests
{
    /// <summary>A clamp to ±0.4625 — the cap at frametime == tick (:58-61).</summary>
    private sealed class ChargeCapRule : IChargeYawCap
    {
        public float CapYaw(float value)
        {
            const float cap = 0.4625f;
            if (value > cap)
                return cap;
            if (value < -cap)
                return -cap;
            return value;
        }
    }

    /// <summary>A hook list that is a list of one rule.</summary>
    private sealed class OneRule(IChargeYawCap rule) : IHooks<IChargeYawCap>
    {
        public System.Collections.Generic.IReadOnlyList<IChargeYawCap> All => [rule];

        public void Each(string what, System.Action<IChargeYawCap> call) => call(rule);
    }

    /// <summary>
    /// The charging mouse turn becomes the cap: 100 counts at sensitivity 3
    /// and m_yaw 0.022 are −6.6 degrees of yaw, the clamp answers −0.4625, and
    /// that is what the command carries (tf_input_main.cpp:48-65 through the
    /// fold).
    /// </summary>
    [Fact]
    public void The_mouse_yaw_while_charging_becomes_the_cap()
    {
        var rig = new InputRig(chargeCap: new OneRule(new ChargeCapRule()));
        rig.Devices.RawX = 100;

        InputCommand cmd = rig.Move(1);

        Assert.Equal(-0.4625f, cmd.ViewAngles.Yaw, 4);
    }

    /// <summary>
    /// With no rule registered the same counts turn the full −6.6, unchanged:
    /// m_yaw 0.022 times sensitivity 3 times the counts (the arithmetic of
    /// ClientInputRouteTests), the fold's empty-chain answer.
    /// </summary>
    [Fact]
    public void The_mouse_yaw_without_a_charge_cap_is_unchanged()
    {
        var rig = new InputRig();
        rig.Devices.RawX = 100;

        InputCommand cmd = rig.Move(1);

        Assert.Equal(-(0.022f * 300.0f), cmd.ViewAngles.Yaw);
    }
}
