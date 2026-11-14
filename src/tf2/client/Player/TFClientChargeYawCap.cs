//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The client's charge cap, <c>CTFInput::CAM_CapYaw</c>
/// (tf_input_main.cpp:48-65): with no local player the amount passes
/// unchanged (:51-52); while the local player holds
/// <c>TF_COND_SHIELD_CHARGE</c> (:54) the amount is clamped to
/// ±<see cref="TFChargeCap.Calculate"/>'s cap (:56-62), the charge's
/// degrees per second; otherwise it passes (:64). Stock reads the cap's
/// frametime scale off <c>gpGlobals</c>; this is the client's side, so the
/// frame time and tick interval come from the client engine's globals, and
/// without them the scale is off and the hook's answer stands.
/// </summary>
[SideService(Side.Client, As = [typeof(IChargeYawCap)])]
public sealed class TFClientChargeYawCap(ITFPlayerData players, IEngineHud hud,
                                        ITFWeaponOwners owners, AttributeHooks hooks,
                                        TFDemomanChargeFrametimeScalingConVar scaling,
                                        IEngineEnvironment environment) : IChargeYawCap
{
    /// <inheritdoc/>
    public float CapYaw(float value)
    {
        int index = hud.LocalPlayerIndex;
        if (players.Networked(index) is not { } state)   // :51-52
            return value;

        if (!state.InCond(TFCond.ShieldCharge))          // :54
            return value;

        // The cap, the wearer's own manager answering the hook (:56).
        IAttributeProvider? attribs = owners.ManagerOf(index);
        float cap = environment.Globals is { IsAvailable: true } globals
            ? TFChargeCap.Calculate(globals.FrameTime, globals.IntervalPerTick, scaling.Value, hooks, attribs)
            : TFChargeCap.Calculate(0f, 0f, false, hooks, attribs);

        // The sign-checked clamp (:58-61); outside it, the amount passes (:64).
        if (value > cap)
            return cap;
        if (value < -cap)
            return -cap;
        return value;
    }
}

/// <summary>The TF2 client's charge cap seam: the input's fold chain, which only a registrar can name.</summary>
[SideService(Side.Client)]
public sealed class TFClientChargeYawCapRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IChargeYawCap>();
}
