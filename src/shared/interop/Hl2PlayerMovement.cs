//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// HL2MP's player inside the movement loop (<see cref="IHl2PlayerMove"/>):
/// <c>HandleSpeedChanges</c>, which picks the walk, run or sprint speed
/// before <c>CheckParameters</c>, and <c>ReduceTimers</c>' suit power, the
/// sprint drawing on it and everything off letting it recharge. The two sides'
/// players run the same arithmetic (hl2_player.cpp, c_hl2mp_player.cpp), over
/// the words the player networks and predicts (m_HL2Local).
/// </summary>
public sealed partial class GameMovement
{
    /// <summary>
    /// HL2MP's player, whose speeds and suit this loop runs; null is base
    /// <c>CGameMovement</c>'s own max speed and no suit.
    /// </summary>
    public IHl2PlayerMove? Hl2Player { get; set; }

    /// <summary>bits_SUIT_DEVICE_SPRINT, and its drain: HL2MP's 25 a second, 100 units in 4 seconds.</summary>
    public const int SuitDeviceSprint = 0x00000001;
    public const float SuitDeviceSprintDrain = 25.0f;

    /// <summary>bits_SUIT_DEVICE_FLASHLIGHT, and its drain (HL2MP's flashlight draws none).</summary>
    public const int SuitDeviceFlashlight = 0x00000002;
    public const float SuitDeviceFlashlightDrain = 2.222f;

    /// <summary>bits_SUIT_DEVICE_BREATHER, and its drain: 100 units in 15 seconds.</summary>
    public const int SuitDeviceBreather = 0x00000004;
    public const float SuitDeviceBreatherDrain = 6.7f;

    /// <summary>SUITPOWER_CHARGE_RATE: 100 units in 8 seconds.</summary>
    public const float SuitPowerChargeRate = 12.5f;

    /// <summary>SUITPOWER_BEGIN_RECHARGE_DELAY: how long every device must be off first.</summary>
    public const float SuitPowerBeginRechargeDelay = 0.5f;

    /// <summary>IN_WALK (in_buttons.h).</summary>
    public const int InWalk = 1 << 18;

    /// <summary>The power a sprint needs to start, and below which one stops.</summary>
    public const float SprintMinimumPower = 10.0f, SuitPowerEmpty = 0.01f;

    /// <summary><c>HandleSpeedChanges</c> (hl2_player.cpp:498).</summary>
    private void HandleSpeedChanges(IHl2PlayerMove hl2)
    {
        int changedButtons = _s.Buttons ^ _s.OldButtons;
        bool justPressedSpeed = (changedButtons & InSpeed) != 0;

        bool wantSprint = hl2.CanSprint(_s.Ducked, _s.Ducking, _s.WaterLevel, _s.SprintDisabled)
                          && _s.SuitEquipped && (_s.Buttons & InSpeed) != 0;
        bool wantsToChangeSprinting = _s.NewSprinting != wantSprint && (changedButtons & InSpeed) != 0;

        bool sprinting = _s.NewSprinting;
        if (wantsToChangeSprinting)
        {
            if (wantSprint)
            {
                if (_s.SuitPower < SprintMinimumPower)
                {
                    if (justPressedSpeed)
                        _w.StartSound(_s.Origin, "HL2Player.SprintNoPower");
                }
                else
                {
                    sprinting = true;
                }
            }
            else
            {
                sprinting = false;
            }
        }

        if (_s.SuitPower < SuitPowerEmpty)
            sprinting = false;

        bool wantWalking = !_s.SuitEquipped
                           || ((_s.Buttons & InWalk) != 0 && !sprinting && (_s.Buttons & InDuck) == 0);

        if (wantWalking)
            sprinting = false;

        _s.NewSprinting = sprinting;

        if (sprinting)
        {
            if (justPressedSpeed)
                _w.StartSound(_s.Origin, "HL2Player.SprintStart");
            _s.ClientMaxSpeed = hl2.SprintSpeed;
        }
        else if (wantWalking)
        {
            _s.ClientMaxSpeed = hl2.WalkSpeed;
        }
        else
        {
            _s.ClientMaxSpeed = hl2.NormSpeed;
        }

        _s.MaxSpeed = _c.MaxSpeed;
    }

    /// <summary>
    /// <c>ReduceTimers</c>' player half (hl2_player.cpp:555): the sprint
    /// device follows the speed this command was given, then the suit is
    /// updated.
    /// </summary>
    private void Hl2ReduceTimers(IHl2PlayerMove hl2)
    {
        bool sprinting = _s.ClientMaxSpeed == hl2.SprintSpeed;

        if (sprinting)
            SuitAddDevice(SuitDeviceSprint, SuitDeviceSprintDrain);
        else
            SuitRemoveDevice(SuitDeviceSprint, SuitDeviceSprintDrain, hl2);

        SuitUpdate(hl2);
    }

    /// <summary><c>SuitPower_Update</c>, HL2MP's: no sticky sprint.</summary>
    private void SuitUpdate(IHl2PlayerMove hl2)
    {
        if (SuitShouldRecharge())
        {
            SuitCharge(SuitPowerChargeRate * _s.FrameTime);
        }
        else if (_s.SuitDevices != 0)
        {
            float load = _s.SuitPowerLoad;

            // If the player's not moving, don't drain sprint juice.
            if ((_s.SuitDevices & SuitDeviceSprint) != 0
                && CloseEnough(MathF.Abs(_s.Velocity.X), 0.0f) && CloseEnough(MathF.Abs(_s.Velocity.Y), 0.0f))
            {
                if (CloseEnough(_s.SuitPowerLoad, SuitDeviceSprintDrain))
                    load = 0.0f;
                else
                    load -= SuitDeviceSprintDrain;
            }

            SuitDrain(load * _s.FrameTime, hl2);
        }
    }

    /// <summary><c>SuitPower_Drain</c>: false, at zero, when there was not enough.</summary>
    private bool SuitDrain(float power, IHl2PlayerMove hl2) => DrainSuit(_s, power, hl2.InfiniteAuxPower);

    /// <summary><c>SuitPower_Drain</c> on a player's state: false, at zero, when there was not enough.</summary>
    public static bool DrainSuit(MoveState s, float power, bool infinite)
    {
        if (infinite)
            return true;

        s.SuitPower -= power;

        if (s.SuitPower < SuitPowerEmpty)
        {
            s.SuitPower = 0.0f;
            return false;
        }

        return true;
    }

    /// <summary><c>SuitPower_Charge</c>, up to full.</summary>
    private void SuitCharge(float power)
    {
        _s.SuitPower += power;

        if (_s.SuitPower > 100.0f)
            _s.SuitPower = 100.0f;
    }

    /// <summary><c>SuitPower_AddDevice</c>: false when it was on already or there is no suit.</summary>
    private bool SuitAddDevice(int device, float drain) => AddSuitDevice(_s, device, drain);

    /// <summary>
    /// <c>SuitPower_AddDevice</c> on a player's state, from the loop or from
    /// outside a command (the breather, from the player's WaterMove).
    /// </summary>
    public static bool AddSuitDevice(MoveState s, int device, float drain)
    {
        if ((s.SuitDevices & device) != 0 || !s.SuitEquipped)
            return false;

        s.SuitDevices |= device;
        s.SuitPowerLoad += drain;
        return true;
    }

    /// <summary>
    /// <c>SuitPower_RemoveDevice</c>: a tenth of a second's drain taken as it
    /// goes off (with the recharge delay, what stops tapping sprint from ever
    /// running out), and the time the suit went quiet when it was the last.
    /// </summary>
    private bool SuitRemoveDevice(int device, float drain, IHl2PlayerMove hl2)
        => RemoveSuitDevice(_s, device, drain, hl2.InfiniteAuxPower, _s.CurTime);

    /// <summary><c>SuitPower_RemoveDevice</c> on a player's state, at <paramref name="now"/>.</summary>
    public static bool RemoveSuitDevice(MoveState s, int device, float drain, bool infinite, float now)
    {
        if ((s.SuitDevices & device) == 0 || !s.SuitEquipped)
            return false;

        DrainSuit(s, drain * 0.1f, infinite);

        s.SuitDevices &= ~device;
        s.SuitPowerLoad -= drain;

        if (s.SuitDevices == 0)
            s.SuitDevicesOffTime = now;

        return true;
    }

    /// <summary><c>SuitPower_ShouldRecharge</c>: every device off, not full, and quiet long enough.</summary>
    private bool SuitShouldRecharge()
        => _s.SuitDevices == 0
           && _s.SuitPower < 100.0f
           && _s.CurTime >= _s.SuitDevicesOffTime + SuitPowerBeginRechargeDelay;

    /// <summary><c>CloseEnough</c> (mathlib.h:2127) at its default, EQUAL_EPSILON.</summary>
    private static bool CloseEnough(float a, float b) => MathF.Abs(a - b) <= 0.001f;
}
