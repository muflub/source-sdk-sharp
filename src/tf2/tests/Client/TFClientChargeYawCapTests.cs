extern alias client;

using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

using AbiGlobals = SourceSharp.Abi.Globals;
using AbiGlobalsRaw = SourceSharp.Abi.CGlobalVarsRaw;
using ChargeCapEngine = client::SourceSharp.Abi.RecordingEngine;
using ChargeCapProbe = client::TF2.Client.Player.TFClientChargeYawCap;
using ChargeCapVar = client::TF2.Shared.TFDemomanChargeFrametimeScalingConVar;
using Cond = client::TF2.Shared.TFCond;
using CondMask = client::TF2.Shared.Conditions.TFCondMask;
using Hooks = client::TF2.Shared.Econ.AttributeHooks;
using NetState = client::TF2.Shared.Player.TFPlayerNetState;
using PlayersData = client::TF2.Shared.Player.ITFPlayerData;
using Views = client::TF2.Shared.Player.TFPlayerNetStateViews;

namespace TF2.Tests.Client;

/// <summary>
/// The TF client's charge cap (<c>TFClientChargeYawCap</c>, over stock's
/// <c>CTFInput::CAM_CapYaw</c> tf_input_main.cpp:48-65 and
/// <c>CalculateChargeCap</c> tf_player_shared.cpp:6100-6115): while the local
/// player's networked state holds <c>TF_COND_SHIELD_CHARGE</c> the amount is
/// clamped to the cap — 0.4625 when the frame is one tick and the scaling
/// convar is on, exactly 0.45 when it is off — and otherwise it passes
/// unchanged (:51-52, :64).
/// </summary>
public sealed class TFClientChargeYawCapTests
{
    private const int Local = 3;

    /// <summary>The tick both fields of the fixture's globals carry.</summary>
    private const float Tick = PredictionDeps.Tick;

    /// <summary>One local player's networked state, handed to every read.</summary>
    private sealed class LocalPlayer : PlayersData
    {
        public NetState? State;

        public NetState? Predicted(int playerIndex) => State;
        public NetState? Networked(int playerIndex) => playerIndex == Local ? State : null;
    }

    /// <summary>A globals block with both fields at the tick: frametime == interval_per_tick.</summary>
    private static nint TickBlock()
    {
        nint block = Marshal.AllocHGlobal(Marshal.SizeOf<AbiGlobalsRaw>());
        Marshal.WriteInt32(block, (int)Marshal.OffsetOf<AbiGlobalsRaw>(nameof(AbiGlobalsRaw.frametime)),
                           BitConverter.SingleToInt32Bits(Tick));
        Marshal.WriteInt32(block, (int)Marshal.OffsetOf<AbiGlobalsRaw>(nameof(AbiGlobalsRaw.interval_per_tick)),
                           BitConverter.SingleToInt32Bits(Tick));
        return block;
    }

    /// <summary>The stub engine: the tick block as the environment's globals, the local index the fixture's.</summary>
    private static ChargeCapEngine Engine()
    {
        var engine = new ChargeCapEngine();
        engine.Returns("Environment.Globals", new AbiGlobals(TickBlock()));
        engine.Returns("Hud.LocalPlayerIndex", Local);
        return engine;
    }

    /// <summary>The cap over one networked state and one answer of the scaling convar.</summary>
    private static ChargeCapProbe Probe(NetState? state, ChargeCapVar scaling)
    {
        var engine = Engine();
        return new ChargeCapProbe(new LocalPlayer { State = state }, engine,
                                  new client::TF2.Shared.NoTFWeaponOwners(), new Hooks(), scaling, engine);
    }

    /// <summary>A bound networked state, the carrier's own buffer.</summary>
    private static NetState Bound()
    {
        var state = new NetState();
        state.BindLocal();
        state.OwnerIndex = Local;
        return state;
    }

    /// <summary>The charge as the carrier's words hold it.</summary>
    private static NetState Charging()
    {
        NetState state = Bound();
        Views.SetConditions(state, CondMask.Of(Cond.ShieldCharge));
        return state;
    }

    /// <summary>
    /// Charging, the mouse's amount is the cap and its sign survives
    /// (:54-62): ten degrees of yaw becomes 0.4625 — the base 0.45 times the
    /// remap of a tick-sized frame — and half a degree the other way becomes
    /// −0.4625; inside the cap the amount passes (:64).
    /// </summary>
    [Fact]
    public void ChargingClampsTheYawToTheCap()
    {
        ChargeCapProbe probe = Probe(Charging(), DeclaredClientConVars.Fresh<ChargeCapVar>());

        Assert.Equal(0.4625f, probe.CapYaw(10f), 4);
        Assert.Equal(-0.4625f, probe.CapYaw(-0.5f), 4);
        Assert.Equal(0.2f, probe.CapYaw(0.2f), 4);
    }

    /// <summary>
    /// Not charging, the amount passes unchanged (:54, :64): the condition
    /// missing from the networked words leaves ten degrees ten degrees.
    /// </summary>
    [Fact]
    public void NotChargingLeavesTheYawAsItIs()
    {
        ChargeCapProbe probe = Probe(Bound(), DeclaredClientConVars.Fresh<ChargeCapVar>());

        Assert.Equal(10f, probe.CapYaw(10f), 4);
    }

    /// <summary>
    /// The scaling convar off (:6107-6112 skipped) the cap is the base
    /// through the hook alone: ten degrees becomes exactly 0.45, ten the
    /// other way becomes −0.45.
    /// </summary>
    [Fact]
    public void WithTheScalingOffTheCapIsTheBase()
    {
        ChargeCapProbe probe = Probe(Charging(), DeclaredClientConVars.Holding<ChargeCapVar>("0"));

        Assert.Equal(0.45f, probe.CapYaw(10f), 4);
        Assert.Equal(-0.45f, probe.CapYaw(-10f), 4);
    }
}
