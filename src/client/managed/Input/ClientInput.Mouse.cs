//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

// in_mouse.cpp. The Windows-only mouse parameters (SystemParametersInfo,
// -useforcedmparms, m_mousespeed / m_mouseaccel1 / m_mouseaccel2) are inert on
// Linux -- m_fMouseParmsValid is never set there, so CheckMouseAcclerationVars
// returned at its top every frame -- and the convars stay declared for the
// console and config.cfg.
public sealed partial class ClientInput
{
    private bool _mouseInitialized;
    private bool _mouseActive;
    private float _accumulatedMouseX, _accumulatedMouseY;
    private float _previousMouseX, _previousMouseY;
    private int _captureWorkaroundLastMouseX, _captureWorkaroundLastMouseY;

    private bool _injectPending;
    private int _injectX, _injectY;

    /// <summary>Injected deltas that reached a command (or an extra sample) so far.</summary>
    public int InjectedCount { get; private set; }

    /// <summary>Whether an injected delta is still waiting for the mouse's next read.</summary>
    public bool InjectPending => _injectPending;

    /// <summary>
    /// The last read that took an injected delta: the inject, and the counts
    /// the device reported in the same read, which sum with it as a real mouse
    /// sums with the accumulator. A gate that sees device counts here knows the
    /// turn it measured was not the inject's alone.
    /// </summary>
    public (int X, int Y, int DeviceX, int DeviceY) LastInjectRead { get; private set; }

    /// <summary>
    /// The dev API's <c>input/inject</c>: a raw mouse delta added to the next
    /// read of the mouse, as if the device had reported it. Accumulates until read.
    /// </summary>
    public void InjectMouse(int dx, int dy)
    {
        _injectX = (_injectPending ? _injectX : 0) + dx;
        _injectY = (_injectPending ? _injectY : 0) + dy;
        _injectPending = true;
    }

    /// <summary>m_pitch as the next command applies it (the sv_cheats bound included).</summary>
    public float EffectivePitch() => MPitch();

    /// <summary>Whether the mouse is captured (ActivateMouse ran and DeactivateMouse has not).</summary>
    public bool MouseActive => _mouseActive;

    private void InitMouse(bool noMouse)
    {
        if (noMouse)
            return;

        _previousMouseX = 0.0f;
        _previousMouseY = 0.0f;
        _captureWorkaroundLastMouseX = 0;
        _captureWorkaroundLastMouseY = 0;
        _mouseInitialized = true;
        PublishState();
    }

    /// <summary>
    /// Stock's CRawInputConvarOnetimeReset client system (PostInit): m_rawinput
    /// is switched on once per config, the first time this build runs on it.
    /// </summary>
    public void RawInputOnetimeReset()
    {
        if (!_cvars.RawInputOnetimeReset.Bool)
        {
            _cvars.RawInput.Write(1);
            _cvars.RawInputOnetimeReset.Write(1);
        }
    }

    /// <summary>CInput::ActivateMouse: hides the cursor and starts accumulating.</summary>
    public void ActivateMouse()
    {
        if (_mouseActive)
            return;

        if (_mouseInitialized)
        {
            _mouseActive = true;
            PublishState();

            ResetMouse();
            _host.GetMouseDelta(out _, out _, true);

            // Clear the accumulated error, and the raw accumulators.
            _accumulatedMouseX = 0;
            _accumulatedMouseY = 0;
            _devices.GetRawMouseAccumulators(out _, out _);
        }
    }

    /// <summary>CInput::DeactivateMouse: gives the cursor back.</summary>
    public void DeactivateMouse()
    {
        if (!_mouseActive)
            return;

        if (_mouseInitialized)
        {
            _mouseActive = false;
            PublishState();
            _host.SetCursorArrow();
            // Put the mouse back in the middle of the screen.
            ResetMouse();

            _accumulatedMouseX = 0;
            _accumulatedMouseY = 0;
        }
    }

    private void GetWindowCenter(out int x, out int y)
    {
        _host.GetScreenSize(out int w, out int h);
        x = w >> 1;
        y = h >> 1;
    }

    /// <summary>CInput::ResetMouse: back to the window's centre.</summary>
    public void ResetMouse()
    {
        GetWindowCenter(out int x, out int y);

        // Under RDP (and perhaps VNC) the mouse can be teleported into the
        // window but not within it: keep the last position and use deltas until
        // it escapes.
        if (_cvars.LimitedCaptureWorkaround.Bool)
        {
            _host.GetCursorPos(out _captureWorkaroundLastMouseX, out _captureWorkaroundLastMouseY);
            _host.GetScreenSize(out int w, out int h);
            if (_captureWorkaroundLastMouseX < w && _captureWorkaroundLastMouseY < h &&
                _captureWorkaroundLastMouseX > 0 && _captureWorkaroundLastMouseY > 0)
            {
                return;
            }

            // Fall through to recapture.
            _captureWorkaroundLastMouseX = x;
            _captureWorkaroundLastMouseY = y;
        }

        _host.SetCursorPos(x, y);
    }

    private void GetAccumulatedMouseDeltasAndResetAccumulators(out float mx, out float my)
    {
        mx = _accumulatedMouseX;
        my = _accumulatedMouseY;

        if (_cvars.RawInput.Bool)
        {
            if (_devices.GetRawMouseAccumulators(out int rawX, out int rawY))
            {
                mx = rawX;
                my = rawY;
            }
        }

        // The dev API's input/inject (a write, cheats only): the delta arrives
        // as though the mouse had moved it.
        if (_injectPending)
        {
            LastInjectRead = (_injectX, _injectY, (int)mx, (int)my);
            mx += _injectX;
            my += _injectY;
            _injectPending = false;
            InjectedCount++;
        }

        _accumulatedMouseX = 0;
        _accumulatedMouseY = 0;
    }

    /// <summary>CInput::GetMouseDelta: m_filter averages over the last two samples.</summary>
    private void GetMouseDelta(float inMouseX, float inMouseY, out float outMouseX, out float outMouseY)
    {
        if (_cvars.MFilter.Bool)
        {
            outMouseX = (inMouseX + _previousMouseX) * 0.5f;
            outMouseY = (inMouseY + _previousMouseY) * 0.5f;
        }
        else
        {
            outMouseX = inMouseX;
            outMouseY = inMouseY;
        }

        _previousMouseX = inMouseX;
        _previousMouseY = inMouseY;
    }

    /// <summary>
    /// CInput::ScaleMouse: the sensitivity, and m_customaccel's three curves
    /// (1 and 2: <c>min(max, pow(dist, exp) * scale + sens)</c>, 2 also by
    /// m_yaw / m_pitch; 3: <c>pow(dist², (exp - 1) / 2) * sens</c>).
    /// </summary>
    private void ScaleMouse(ref float x, ref float y)
    {
        float mx = x;
        float my = y;

        float hudSensitivity = _host.Hud.Sensitivity;
        float mouseSensitivity = hudSensitivity != 0 ? hudSensitivity : _cvars.Sensitivity.Float;

        int customAccel = _cvars.CustomAccel.Int;
        if (customAccel == 1 || customAccel == 2)
        {
            float rawDistance = MathF.Sqrt(mx * mx + my * my);
            float accelerationScale = _cvars.CustomAccelScale.Float;
            float acceleratedMax = _cvars.CustomAccelMax.Float;
            float acceleratedExponent = _cvars.CustomAccelExponent.Float;
            float accelerated = MathF.Pow(rawDistance, acceleratedExponent) * accelerationScale + mouseSensitivity;

            if (acceleratedMax > 0.0001f && accelerated > acceleratedMax)
                accelerated = acceleratedMax;

            x *= accelerated;
            y *= accelerated;

            // Mode 2 (and stock's unreachable 4) re-scales by yaw and pitch.
            if (customAccel == 2 || customAccel == 4)
            {
                x *= _cvars.MYaw.Float;
                y *= MPitch();
            }
        }
        else if (customAccel == 3)
        {
            float rawDistanceSquared = mx * mx + my * my;
            float exponent = MathF.Max(0.0f, (_cvars.CustomAccelExponent.Float - 1.0f) / 2.0f);
            float accelerated = MathF.Pow(rawDistanceSquared, exponent) * mouseSensitivity;

            x *= accelerated;
            y *= accelerated;
        }
        else
        {
            x *= mouseSensitivity;
            y *= mouseSensitivity;
        }
    }

    /// <summary>
    /// m_pitch as stock's ConVar_ServerBounded reads it: anything under
    /// sv_cheats, otherwise only ±0.022 (the sign is the player's inverted
    /// mouse).
    /// </summary>
    private float MPitch()
    {
        float baseValue = _cvars.MPitch.Float;
        string? cheats = _host.EngineConVar("sv_cheats");
        if (cheats is null || (int)(float)CRuntime.Atof(cheats) != 0)
            return baseValue;
        return baseValue > 0 ? 0.022f : -0.022f;
    }

    /// <summary>CInput::ApplyMouse: the mouse's deltas into the view angles (or, strafing, the moves).</summary>
    private void ApplyMouse(ref InputAngles viewAngles, ref InputCommand cmd, float mouseX, float mouseY)
    {
        if (!((Buttons.Strafe.State & 1) != 0 || _cvars.LookStrafe.Int != 0))
        {
            if (CamIsThirdPerson() && _cvars.ThirdPersonPlatformer.Int != 0)
            {
                if (mouseX != 0)
                {
                    // The mouse orbits the camera around the player.
                    Vector3f offset = _thirdPerson.CameraOffset;
                    offset.Y -= _cvars.MYaw.Float * mouseX;
                    _cvars.CamIdealYaw.Write(offset.Y - viewAngles.Yaw);
                    _thirdPerson.CameraOffset = offset;
                }
            }
            else
            {
                // Otherwise the mouse spins the view about the vertical axis.
                viewAngles.Yaw -= CamCapYaw(_cvars.MYaw.Float * mouseX);
            }
        }
        else
        {
            // Holding strafe (or lookstrafe): the horizontal mouse side-steps.
            cmd.SideMove += _cvars.MSide.Float * mouseX;
        }

        if ((Buttons.Strafe.State & 1) == 0)
        {
            if (CamIsThirdPerson() && _cvars.ThirdPersonPlatformer.Int != 0)
            {
                if (mouseY != 0)
                {
                    Vector3f offset = _thirdPerson.CameraOffset;
                    offset.X += MPitch() * mouseY;
                    _cvars.CamIdealPitch.Write(offset.X - viewAngles.Pitch);
                    _thirdPerson.CameraOffset = offset;
                }
            }
            else
            {
                viewAngles.Pitch += CamCapPitch(MPitch() * mouseY);
            }

            // Check the pitch bounds.
            if (viewAngles.Pitch > _cvars.PitchDown.Float)
                viewAngles.Pitch = _cvars.PitchDown.Float;
            if (viewAngles.Pitch < -_cvars.PitchUp.Float)
                viewAngles.Pitch = -_cvars.PitchUp.Float;
        }
        else
        {
            // Strafing: the vertical mouse is a forward key press.
            cmd.ForwardMove -= _cvars.MForward.Float * mouseY;
        }

        // Finally the mouse state into the command (truncated, as stock's int casts).
        cmd.MouseDx = (short)(int)mouseX;
        cmd.MouseDy = (short)(int)mouseY;
    }

    /// <summary>CInput::AccumulateMouse: the non-raw path's sample (SDL's delta), between commands.</summary>
    public void AccumulateMouse()
    {
        if (!_cvars.MouseEnable.Bool)
            return;
        if (!_cvars.MouseLook.Bool)
            return;
        if (_cvars.RawInput.Bool)
            return;

        _host.GetScreenSize(out int w, out int h);

        // Only when the camera is not taking the mouse, and the cursor is locked.
        if (!_cameraInterceptingMouse && _host.CursorLocked)
        {
            _host.GetMouseDelta(out int dx, out int dy, false);
            _accumulatedMouseX += dx;
            _accumulatedMouseY += dy;

            // Force the mouse to the centre, so there is room to move.
            ResetMouse();
        }
        else if (_mouseActive)
        {
            // Clamp.
            _host.GetCursorPos(out int ox, out int oy);
            ox = MathX.Clamp(ox, 0, w - 1);
            oy = MathX.Clamp(oy, 0, h - 1);
            _host.SetCursorPos(ox, oy);
        }
    }

    /// <summary>CInput::MouseMove: the mouse's part of a command.</summary>
    private void MouseMove(ref InputCommand cmd)
    {
        InputAngles viewAngles = _host.GetViewAngles();

        // No pitch drift while mouse-looking.
        _host.StopPitchDrift();

        // Not while the camera takes the mouse, nor while the cursor is up --
        // nor while the UI's menu layer holds the mouse.
        if (!_cameraInterceptingMouse && !_host.CursorVisible && !_menu.MenuHasInput)
        {
            AccumulateMouse();
            GetAccumulatedMouseDeltasAndResetAccumulators(out float mx, out float my);
            GetMouseDelta(mx, my, out float mouseX, out float mouseY);
            ScaleMouse(ref mouseX, ref mouseY);

            // The client mode's OverrideMouseInput: the active weapon's, and no
            // entity in this build is a weapon (clientmode_shared.cpp).

            ApplyMouse(ref viewAngles, ref cmd, mouseX, mouseY);

            // Re-centre the mouse -- only one this window holds (an injected
            // delta can reach here uncaptured, and must not move the desktop's).
            if (_mouseActive)
                ResetMouse();
        }

        _host.SetViewAngles(viewAngles);
    }

    /// <summary>CInput::GetFullscreenMousePos: the cursor in screen space while it is visible.</summary>
    public void GetFullscreenMousePos(ref int mx, ref int my, ref int unclampedX, ref int unclampedY)
    {
        if (!_host.CursorVisible)
            return;

        GetWindowCenter(out int x, out int y);
        _host.GetCursorPos(out int currentX, out int currentY);

        currentX -= x;
        currentY -= y;

        // Add back the middle of the viewport.
        _host.GetVguiScreenSize(out int w, out int h);
        currentX += w / 2;
        currentY += h / 2;

        unclampedX = currentX;
        unclampedY = currentY;

        currentX = Math.Max(0, currentX);
        currentX = Math.Min(w, currentX);
        currentY = Math.Max(0, currentY);
        currentY = Math.Min(h, currentY);

        mx = currentX;
        my = currentY;
    }

    /// <summary>CInput::SetFullscreenMousePos.</summary>
    public void SetFullscreenMousePos(int mx, int my) => _host.SetCursorPos(mx, my);

    /// <summary>CInput::ClearStates: drops what accumulated, so a re-trapped mouse does not pop.</summary>
    public void ClearStates()
    {
        if (!_mouseActive)
            return;

        _accumulatedMouseX = 0;
        _accumulatedMouseY = 0;
        _devices.GetRawMouseAccumulators(out _, out _);
    }
}
