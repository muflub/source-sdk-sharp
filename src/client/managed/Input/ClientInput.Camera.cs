//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

/// <summary>CameraThirdData_t: a third-person camera a mod sets (pitch, yaw, distance, lag).</summary>
public readonly record struct CameraThirdData(float Pitch, float Yaw, float Dist, float Lag);

// in_camera.cpp: the third-person camera's offsets and the cam_* commands.
// Where the camera is finally PLACED (the hull sweep, the view origin) is the
// shim's third-person pass over ClientPlayerPrediction's ThirdPersonCamera, as
// before; this is what moves the offsets and the mode.
public sealed partial class ClientInput
{
    // CAM_ANGLE_MOVE, CAM_MIN_DIST (cam_thirdperson.h).
    private const double CamAngleMove = .5;
    private const double CamMinDist = 30.0;
    private const int Dist = 2;

    private bool _cameraInterceptingMouse;
    private bool _cameraInThirdPerson;
    private bool _cameraMovingWithMouse;
    private bool _cameraDistanceMove;
    private int _cameraOldX, _cameraOldY;
    private int _cameraX, _cameraY;
    private bool _cameraIsOrthographic;
    private CameraThirdData? _cameraThirdData;

    // CAM_Think's function static s_oldAngles, and Is_CAM_ThirdPerson_MayaMode.
    private InputAngles? _mayaOldAngles;
    private bool _mayaMode;

    /// <summary>Whether a mod's CameraThirdData_t drives the camera instead of the cam_ideal* convars.</summary>
    public bool HasCameraThirdData => _cameraThirdData is not null;

    private void InitCamera() => _cameraIsOrthographic = false;

    private static float CamCapYaw(float value) => value;

    private static float CamCapPitch(float value) => value;

    public bool CamIsThirdPerson() => _cameraInThirdPerson;

    public bool CamInterceptingMouse() => _cameraInterceptingMouse;

    public bool CamIsOrthographic() => _cameraIsOrthographic;

    public void CamOrthographicSize(out float w, out float h)
    {
        w = _cvars.COrthoWidth.Float;
        h = _cvars.COrthoHeight.Float;
    }

    /// <summary><c>thirdperson_mayamode</c>: toggles the Maya-like camera controls.</summary>
    public void ToggleMayaMode() => _mayaMode = !_mayaMode;

    /// <summary><c>snapto</c>.</summary>
    public void ToggleSnapTo() => _cvars.CamSnapTo.Write(_cvars.CamSnapTo.Int == 0 ? 1 : 0);

    private static float MoveToward(float cur, float goal, float lag)
    {
        if (cur != goal)
        {
            if (MathF.Abs(cur - goal) > 180.0)
            {
                if (cur < goal)
                    cur = (float)(cur + 360.0);
                else
                    cur = (float)(cur - 360.0);
            }

            if (cur < goal)
            {
                if (cur < goal - 1.0)
                    cur += (goal - cur) / lag;
                else
                    cur = goal;
            }
            else
            {
                if (cur > goal + 1.0)
                    cur -= (cur - goal) / lag;
                else
                    cur = goal;
            }
        }

        // Back into range.
        if (cur < 0)
            cur = (float)(cur + 360.0);
        else if (cur >= 360)
            cur -= 360;

        return cur;
    }

    /// <summary>CInput::CAM_Think, once a frame before the view is set up.</summary>
    public void CamThink()
    {
        if (_cameraThirdData is not null)
        {
            CamCameraThirdThink();
            return;
        }

        switch (_cvars.CamCommand.Int)
        {
            case 1:
                _thirdPerson.CamToThirdPerson();
                break;
            case 2:
                _thirdPerson.CamToFirstPerson();
                break;
        }

        _thirdPerson.Update();

        if (!_cameraInThirdPerson)
            return;

        // Maya mode: no camera move unless explicitly moving it. Stock read the
        // alt keys and the mouse buttons through vgui::input(); this build's
        // vgui input is the engine's, so the keys are the input system's.
        if (_mayaMode)
        {
            _cameraInterceptingMouse = _cameraMovingWithMouse =
                _devices.IsButtonDown(ButtonCodesKeyboard.LeftAlt) || _devices.IsButtonDown(ButtonCodesKeyboard.RightAlt);
            if (!_cameraMovingWithMouse)
                return;

            Buttons.CamPitchUp.Clear();
            Buttons.CamPitchDown.Clear();
            Buttons.CamYawLeft.Clear();
            Buttons.CamYawRight.Clear();
            Buttons.CamIn.Clear();
            Buttons.CamOut.Clear();

            bool left = _devices.IsButtonDown(ButtonCodes.MouseLeft);
            bool right = _devices.IsButtonDown(ButtonCodes.MouseRight);
            bool middle = _devices.IsButtonDown(ButtonCodesKeyboard.MouseMiddle);
            if (left && middle)
            {
                _cameraDistanceMove = true;
            }
            else if (left)
            {
                _cameraDistanceMove = false;
            }
            else if (right)
            {
                _cameraDistanceMove = true;
            }
            else
            {
                ResetMouse();
                return;
            }
        }

        float idealPitch = _cvars.CamIdealPitch.Float;
        float idealYaw = _cvars.CamIdealYaw.Float;
        float idealDist = _cvars.CamIdealDist.Float;

        // The camera moved with the mouse.
        if (_cameraMovingWithMouse)
        {
            _host.GetCursorPos(out int cpx, out int cpy);
            _cameraX = cpx;
            _cameraY = cpy;

            // Not while zooming.
            if (!_cameraDistanceMove)
            {
                GetWindowCenter(out int x, out int y);

                // Keep the camera within limits around the player.
                if (_cameraX > x)
                {
                    if (idealYaw < _cvars.CMaxYaw.Float)
                        idealYaw = (float)(idealYaw + CamAngleMove * ((_cameraX - x) / 2));
                    if (idealYaw > _cvars.CMaxYaw.Float)
                        idealYaw = _cvars.CMaxYaw.Float;
                }
                else if (_cameraX < x)
                {
                    if (idealYaw > _cvars.CMinYaw.Float)
                        idealYaw = (float)(idealYaw - CamAngleMove * ((x - _cameraX) / 2));
                    if (idealYaw < _cvars.CMinYaw.Float)
                        idealYaw = _cvars.CMinYaw.Float;
                }

                if (_cameraY > y)
                {
                    if (idealPitch < _cvars.CMaxPitch.Float)
                        idealPitch = (float)(idealPitch + CamAngleMove * ((_cameraY - y) / 2));
                    if (idealPitch > _cvars.CMaxPitch.Float)
                        idealPitch = _cvars.CMaxPitch.Float;
                }
                else if (_cameraY < y)
                {
                    if (idealPitch > _cvars.CMinPitch.Float)
                        idealPitch = (float)(idealPitch - CamAngleMove * ((y - _cameraY) / 2));
                    if (idealPitch < _cvars.CMinPitch.Float)
                        idealPitch = _cvars.CMinPitch.Float;
                }

                // Old to current, now that the mouse is used.
                float sensitivity = _host.Hud.Sensitivity;
                if (sensitivity != 0)
                {
                    _cameraOldX = (int)(_cameraX * sensitivity);
                    _cameraOldY = (int)(_cameraY * sensitivity);
                }
                else
                {
                    _cameraOldX = _cameraX;
                    _cameraOldY = _cameraY;
                }
                ResetMouse();
            }
        }

        // The camera keys.
        if (KeyState(Buttons.CamPitchUp) != 0)
            idealPitch += _cvars.CamIdealDelta.Float;
        else if (KeyState(Buttons.CamPitchDown) != 0)
            idealPitch -= _cvars.CamIdealDelta.Float;

        if (KeyState(Buttons.CamYawLeft) != 0)
            idealYaw -= _cvars.CamIdealDelta.Float;
        else if (KeyState(Buttons.CamYawRight) != 0)
            idealYaw += _cvars.CamIdealDelta.Float;

        if (KeyState(Buttons.CamIn) != 0)
        {
            idealDist -= 2 * _cvars.CamIdealDelta.Float;
            if (idealDist < CamMinDist)
            {
                // Back into first person: reset the angle too.
                idealPitch = 0;
                idealYaw = 0;
                idealDist = (float)CamMinDist;
            }
        }
        else if (KeyState(Buttons.CamOut) != 0)
        {
            idealDist += 2 * _cvars.CamIdealDelta.Float;
        }

        if (_cameraDistanceMove)
        {
            GetWindowCenter(out _, out int y);

            if (_cameraY > y)
            {
                if (idealDist < _cvars.CMaxDistance.Float)
                    idealDist += _cvars.CamIdealDelta.Float * ((_cameraY - y) / 2);
                if (idealDist > _cvars.CMaxDistance.Float)
                    idealDist = _cvars.CMaxDistance.Float;
            }
            else if (_cameraY < y)
            {
                if (idealDist > _cvars.CMinDistance.Float)
                    idealDist -= _cvars.CamIdealDelta.Float * ((y - _cameraY) / 2);
                if (idealDist < _cvars.CMinDistance.Float)
                    idealDist = _cvars.CMinDistance.Float;
            }

            _cameraOldX = (int)(_cameraX * _host.Hud.Sensitivity);
            _cameraOldY = (int)(_cameraY * _host.Hud.Sensitivity);
            ResetMouse();
        }

        // The engine's view angles; in Maya mode, a pop while the camera was
        // still moves the camera with it.
        InputAngles viewAngles = _host.GetViewAngles();
        _mayaOldAngles ??= viewAngles;
        InputAngles old = _mayaOldAngles.Value;
        if (_mayaMode && (old.Pitch != viewAngles.Pitch || old.Yaw != viewAngles.Yaw || old.Roll != viewAngles.Roll))
        {
            idealPitch += old.Pitch - viewAngles.Pitch;
            idealYaw += old.Yaw - viewAngles.Yaw;
            _mayaOldAngles = viewAngles;
        }

        // Pitch and yaw into the range MoveToward handles.
        if (idealPitch > 180)
            idealPitch -= 360;
        else if (idealPitch < -180)
            idealPitch += 360;

        // >= 180 and <= -180: a discontinuity facing the model, but smooth
        // left-right transitions (stock's own reasoning).
        if (idealYaw >= 180)
            idealYaw -= 360;
        else if (idealYaw <= -180)
            idealYaw += 360;

        idealPitch = MathX.Clamp(idealPitch, _cvars.CMinPitch.Float, _cvars.CMaxPitch.Float);
        idealYaw = MathX.Clamp(idealYaw, _cvars.CMinYaw.Float, _cvars.CMaxYaw.Float);
        idealDist = MathX.Clamp(idealDist, _cvars.CMinDistance.Float, _cvars.CMaxDistance.Float);

        _cvars.CamIdealPitch.Write(idealPitch);
        _cvars.CamIdealYaw.Write(idealYaw);
        _cvars.CamIdealDist.Write(idealDist);

        // The camera offset moves toward the ideal (offset = view + ideal).
        Vector3f camOffset = _thirdPerson.CameraOffset;

        if (_cvars.CamSnapTo.Int != 0)
        {
            camOffset.Y = _cvars.CamIdealYaw.Float + viewAngles.Yaw;
            camOffset.X = _cvars.CamIdealPitch.Float + viewAngles.Pitch;
            camOffset.Z = _cvars.CamIdealDist.Float;
        }
        else
        {
            float lag = MathF.Max(1, 1 + _cvars.CamIdealLag.Float);

            if (camOffset.Y - viewAngles.Yaw != _cvars.CamIdealYaw.Float)
                camOffset.Y = MoveToward(camOffset.Y, _cvars.CamIdealYaw.Float + viewAngles.Yaw, lag);

            if (camOffset.X - viewAngles.Pitch != _cvars.CamIdealPitch.Float)
                camOffset.X = MoveToward(camOffset.X, _cvars.CamIdealPitch.Float + viewAngles.Pitch, lag);

            if (MathF.Abs(camOffset.Z - _cvars.CamIdealDist.Float) < 2.0)
                camOffset.Z = _cvars.CamIdealDist.Float;
            else
                camOffset.Z += (_cvars.CamIdealDist.Float - camOffset.Z) / lag;
        }

        // The hull sweep that pulled the camera in is the shim's third-person
        // pass (thirdperson_camera.h), from the frame's own eye.

        if (_cvars.CamShowAngles.Int != 0)
        {
            _host.ConsolePrintAt(4, $"Pitch: {viewAngles.Pitch,6:F1}   Yaw: {viewAngles.Yaw,6:F1} {"view angles",38}");
            _host.ConsolePrintAt(6, $"Pitch: {_cvars.CamIdealPitch.Float,6:F1}   Yaw: {_cvars.CamIdealYaw.Float,6:F1}   Dist: {_cvars.CamIdealDist.Float,6:F1} {"ideal angles",19}");
            Vector3f o = _thirdPerson.CameraOffset;
            _host.ConsolePrintAt(8, $"Pitch: {o.X,6:F1}   Yaw: {o.Y,6:F1}   Dist: {o.Z,6:F1} {"camera offset",16}");
        }

        _thirdPerson.CameraOffset = camOffset;
    }

    private static void ClampRange180(ref float value)
    {
        if (value >= 180.0f)
            value -= 360.0f;
        else if (value <= -180.0f)
            value += 360.0f;
    }

    /// <summary>CInput::CAM_SetCameraThirdData: null hands the camera back to the convars.</summary>
    public void CamSetCameraThirdData(CameraThirdData? data, InputAngles cameraOffset)
    {
        _cameraThirdData = data;
        _thirdPerson.CameraOffset = new Vector3f(cameraOffset.Pitch, cameraOffset.Yaw, cameraOffset.Roll);
    }

    /// <summary>CInput::CAM_CameraThirdThink: the mod's camera data, chased the same way.</summary>
    public void CamCameraThirdThink()
    {
        if (_cameraThirdData is not CameraThirdData data)
            return;
        if (!_cameraInThirdPerson)
            return;

        InputAngles view = _host.GetViewAngles();
        Vector3f camOffset = _thirdPerson.CameraOffset;

        float lag = MathF.Max(1, 1 + data.Lag);
        if (camOffset.X - view.Pitch != data.Pitch)
            camOffset.X = MoveToward(camOffset.X, data.Pitch + view.Pitch, lag);
        if (camOffset.Y - view.Yaw != data.Yaw)
            camOffset.Y = MoveToward(camOffset.Y, data.Yaw + view.Yaw, lag);
        if (MathF.Abs(camOffset.Z - data.Dist) < 2.0)
            camOffset.Z = data.Dist;
        else
            camOffset.Z += (data.Dist - camOffset.Z) / lag;

        ClampRange180(ref camOffset.X);
        ClampRange180(ref camOffset.Y);

        _thirdPerson.CameraOffset = camOffset;
    }

    /// <summary>CInput::CAM_ToThirdPerson.</summary>
    public void CamToThirdPerson()
    {
        InputAngles viewAngles = _host.GetViewAngles();

        if (!_cameraInThirdPerson)
        {
            _cameraInThirdPerson = true;
            PublishState();
            _thirdPerson.CameraOffset = new Vector3f(viewAngles.Pitch, viewAngles.Yaw, (float)CamMinDist);
        }

        _cvars.CamCommand.Write(0);
    }

    /// <summary>CInput::CAM_ToFirstPerson.</summary>
    public void CamToFirstPerson()
    {
        _thirdPerson.DesiredCameraOffset = default;
        _cameraInThirdPerson = false;
        PublishState();
        _cvars.CamCommand.Write(0);

        // Let the viewer know.
        _host.ViewerCameraModeChanged();
    }

    /// <summary>CInput::CAM_ToOrthographic.</summary>
    public void CamToOrthographic()
    {
        _cameraInThirdPerson = false;
        PublishState();
        _cameraIsOrthographic = true;
        _cvars.CamCommand.Write(0);
    }

    /// <summary>CInput::CAM_StartMouseMove (+cammousemove).</summary>
    public void CamStartMouseMove()
    {
        if (_cameraInThirdPerson)
        {
            if (!_cameraMovingWithMouse)
            {
                _cameraMovingWithMouse = true;
                _cameraInterceptingMouse = true;
                _host.GetCursorPos(out int cpx, out int cpy);
                _cameraX = cpx;
                _cameraY = cpy;

                float sensitivity = _host.Hud.Sensitivity;
                if (sensitivity != 0)
                {
                    _cameraOldX = (int)(_cameraX * sensitivity);
                    _cameraOldY = (int)(_cameraY * sensitivity);
                }
                else
                {
                    _cameraOldX = _cameraX;
                    _cameraOldY = _cameraY;
                }
            }
        }
        else
        {
            // Not in third person: no camera movement.
            _cameraMovingWithMouse = false;
            _cameraInterceptingMouse = false;
        }
    }

    /// <summary>CInput::CAM_EndMouseMove (-cammousemove).</summary>
    public void CamEndMouseMove()
    {
        _cameraMovingWithMouse = false;
        _cameraInterceptingMouse = false;
    }

    /// <summary>CInput::CAM_StartDistance (+camdistance).</summary>
    public void CamStartDistance()
    {
        if (_cameraInThirdPerson)
        {
            if (!_cameraDistanceMove)
            {
                _cameraDistanceMove = true;
                _cameraMovingWithMouse = true;
                _cameraInterceptingMouse = true;
                _host.GetCursorPos(out int cpx, out int cpy);
                _cameraX = cpx;
                _cameraY = cpy;
                _cameraOldX = (int)(_cameraX * _host.Hud.Sensitivity);
                _cameraOldY = (int)(_cameraY * _host.Hud.Sensitivity);
            }
        }
        else
        {
            _cameraDistanceMove = false;
            _cameraMovingWithMouse = false;
            _cameraInterceptingMouse = false;
        }
    }

    /// <summary>CInput::CAM_EndDistance (-camdistance).</summary>
    public void CamEndDistance()
    {
        _cameraDistanceMove = false;
        _cameraMovingWithMouse = false;
        _cameraInterceptingMouse = false;
    }
}

/// <summary>The keyboard and mouse codes the Maya camera reads (inputsystem/ButtonCode.h).</summary>
public static class ButtonCodesKeyboard
{
    public const int LeftAlt = 81;
    public const int RightAlt = 82;
    public const int MouseMiddle = 109;
}
