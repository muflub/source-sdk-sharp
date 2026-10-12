//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

// in_joystick.cpp.
public sealed partial class ClientInput
{
    // Control like a joystick / like a mouse, spinner or trackball.
    private const int JoyAbsoluteAxis = 0x00000000;
    private const int JoyRelativeAxis = 0x00000010;

    private const int GameAxisNone = 0, GameAxisForward = 1, GameAxisPitch = 2, GameAxisSide = 3, GameAxisYaw = 4;
    private const int MaxGameAxes = 5;

    private const int Pitch = 0, Yaw = 1;

    private struct JoyAxis
    {
        public int AxisMap;
        public int ControlMap;
    }

    /// <summary>in_joystick.cpp's envelope_t controlEnvelope: the look stick's acceleration memory.</summary>
    private struct Envelope
    {
        public float Scale0, Scale1;
        public bool Pegged0, Pegged1;
        public bool PeggedDir0, PeggedDir1;

        public float Scale(int axis) => axis == 0 ? Scale0 : Scale1;

        public void SetScale(int axis, float value)
        {
            if (axis == 0)
                Scale0 = value;
            else
                Scale1 = value;
        }

        public bool Pegged(int axis) => axis == 0 ? Pegged0 : Pegged1;

        public void SetPegged(int axis, bool value)
        {
            if (axis == 0)
                Pegged0 = value;
            else
                Pegged1 = value;
        }

        public bool PeggedDir(int axis) => axis == 0 ? PeggedDir0 : PeggedDir1;

        public void SetPeggedDir(int axis, bool value)
        {
            if (axis == 0)
                PeggedDir0 = value;
            else
                PeggedDir1 = value;
        }
    }

    private readonly JoyAxis[] _axes = new JoyAxis[ButtonCodes.JoystickAxes];
    private Envelope _envelope;
    private bool _joystickAdvancedInit;
    private bool _hadJoysticks;
    private float _remainingJoystickSampleTime;
    private float _previousJoystickForward, _previousJoystickSide, _previousJoystickPitch, _previousJoystickYaw;

    /// <summary>The game axis each stick axis drives, and whether relative: the route's "mapping in force".</summary>
    public (int AxisMap, bool Relative)[] JoystickMapping()
    {
        var map = new (int, bool)[_axes.Length];
        for (int i = 0; i < _axes.Length; i++)
            map[i] = (_axes[i].AxisMap, _axes[i].ControlMap != 0);
        return map;
    }

    /// <summary>The move curves: 0 linear, 1 quadratic, 2 cubic, 3 quadratic extreme, 4 power, 5 two-stage, 6 the vehicle's.</summary>
    private float ResponseCurve(int curve, float x, int axis, float sensitivity)
    {
        switch (curve)
        {
            case 1:
                if (x < 0)
                    return -(x * x) * sensitivity;
                return x * x * sensitivity;

            case 2:
                return x * x * x * sensitivity;

            case 3:
            {
                float extreme = 1.0f;
                if (MathF.Abs(x) >= 0.95f)
                    extreme = 1.5f;
                if (x < 0)
                    return -extreme * x * x * sensitivity;
                return extreme * x * x * sensitivity;
            }

            case 4:
            {
                float scale = sensitivity < 0.0f ? -1.0f : 1.0f;
                sensitivity = MathX.Clamp(MathF.Abs(sensitivity), 1.0e-8f, 1000.0f);
                float oneOverSens = 1.0f / sensitivity;
                if (x < 0.0f)
                    scale = -scale;
                float retval = MathX.Clamp(MathF.Pow(MathF.Abs(x), oneOverSens), 0.0f, 1.0f);
                return retval * scale;
            }

            case 5:
            {
                float output = x;
                if (MathF.Abs(output) <= 0.6f)
                    output *= 0.5f;
                output = output * sensitivity;
                return output;
            }

            case 6:
                // The vehicle's turn: only YAW (left/right) remaps; PITCH falls
                // through to linear.
                if (axis == Yaw)
                {
                    float sign = 1;
                    if (x < 0.0)
                        sign = -1;
                    x = MathF.Abs(x);
                    float lowEnd = _cvars.JoyVehicleTurnLowEnd.Float;
                    float lowMap = _cvars.JoyVehicleTurnLowMap.Float;
                    if (x <= lowEnd)
                        x = MathX.RemapVal(x, 0.0f, lowEnd, 0.0f, lowMap);
                    else
                        x = MathX.RemapVal(x, lowEnd, 1.0f, lowMap, 1.0f);
                    return x * sensitivity * sign;
                }
                break;
        }

        // linear
        return x * sensitivity;
    }

    /// <summary>
    /// AutoAimDampening: with a valid autoaim target, dampen stick movement
    /// away from it. The fact comes from the viewer (view_facts.h), not the
    /// entity.
    /// </summary>
    private float AutoAimDampening(float x)
    {
        if (_cvars.JoyAutoAimDampen.Float > 0.0f && MathF.Abs(x) < _cvars.JoyAutoAimDampenRange.Float)
        {
            if (_host.ViewerAimIsAssisted)
                return _cvars.JoyAutoAimDampen.Float;
        }
        return 1.0f;
    }

    private float ResponseCurveLookDefault(float x, int axis, float dist, float frameTime)
    {
        float inputX = x;
        bool physicallyPegged = dist >= _cvars.JoyPegged.Float;

        bool negative = false;
        if (x < 0.0f)
        {
            negative = true;
            x *= -1;
        }

        if (axis == Yaw && _cvars.JoyVirtualPeg.Bool)
        {
            if (x >= 0.95f)
            {
                // The player pegged the stick.
                _envelope.SetPegged(axis, true);
                _envelope.SetPeggedDir(axis, negative);
            }

            if (_envelope.Pegged(axis))
            {
                // Pegged once; still physically pegged the same way: keep it so.
                if (physicallyPegged && negative == _envelope.PeggedDir(axis))
                    x = 1.0f;
                else
                    _envelope.SetPegged(axis, false);
            }
        }

        // The two-stage mapping.
        float lowEnd = _cvars.JoyLowEnd.Float;
        float lowMap = _cvars.JoyLowMap.Float;
        if (x > lowEnd)
        {
            float highMap = 1.0f - lowMap;
            float xNormal = x - lowEnd;
            float factor = xNormal / (1.0f - lowEnd);
            x = lowMap + (highMap * factor);

            // Accelerate.
            if (_envelope.Scale(axis) < 1.0f)
            {
                _envelope.SetScale(axis, _envelope.Scale(axis) + (frameTime * _cvars.JoyAccelScale.Float));
                if (_envelope.Scale(axis) > 1.0f)
                    _envelope.SetScale(axis, 1.0f);
            }

            float delta = x - lowMap;
            x = lowMap + (delta * _envelope.Scale(axis));
        }
        else
        {
            // Shut off acceleration.
            _envelope.SetScale(axis, 0.0f);
            float factor = x / lowEnd;
            x = lowMap * factor;
        }

        x *= AutoAimDampening(inputX);

        if (axis == Yaw && x > 0.0f && _cvars.JoyDisplayInput.Bool)
            _host.Msg($"In:{inputX:F6} Out:{x:F6} Frametime:{frameTime:F6}\n");

        if (negative)
            x *= -1;

        return x;
    }

    private float ResponseCurveLookAccelerated(float x, int axis, float otherAxis, float frameTime)
    {
        float inputX = x;

        float joyDist = MathF.Sqrt(x * x + otherAxis * otherAxis);
        bool isPegged = joyDist >= _cvars.JoyPegged.Float;

        bool negative = false;
        if (x < 0.0f)
        {
            negative = true;
            x *= -1;
        }

        bool doAcceleration = false;
        float lowMap = _cvars.JoyLowMap.Float;

        if (isPegged && x > _cvars.JoyAccelFilter.Float)
        {
            // Pegged and pushed past the filter: accelerate this axis too.
            x = MathF.Max(lowMap, x);
            doAcceleration = true;
        }
        else
        {
            // Languishing in the low end: no acceleration.
            _envelope.SetScale(axis, 0.0f);
            float factor = x / _cvars.JoyLowEnd.Float;
            x = lowMap * factor;
        }

        if (doAcceleration)
        {
            float max = _cvars.JoyAccelMax.Float;
            if (_envelope.Scale(axis) < max)
            {
                float delta = x - lowMap;
                x = lowMap + (delta * _envelope.Scale(axis));
                _envelope.SetScale(axis, _envelope.Scale(axis) + (frameTime * _cvars.JoyAccelScale.Float));

                if (_envelope.Scale(axis) > max)
                    _envelope.SetScale(axis, max);
            }
        }

        x *= AutoAimDampening(inputX);

        if (axis == Yaw && inputX != 0.0f && _cvars.JoyDisplayInput.Bool)
            _host.Msg($"In:{inputX:F6} Out:{x:F6} Frametime:{frameTime:F6}\n");

        if (negative)
            x *= -1;

        return x;
    }

    private float ResponseCurveLook(int curve, float x, int axis, float otherAxis, float dist, float frameTime)
        => curve == 1
            ? ResponseCurveLookAccelerated(x, axis, otherAxis, frameTime)
            : ResponseCurveLookDefault(x, axis, dist, frameTime);

    /// <summary>CInput::Joystick_Advanced (<c>joyadvancedupdate</c>): the axis map from the joy_advaxis* convars.</summary>
    public void JoystickAdvanced()
    {
        for (int i = 0; i < _axes.Length; i++)
        {
            _axes[i].AxisMap = GameAxisNone;
            _axes[i].ControlMap = JoyAbsoluteAxis;
        }

        if (!_cvars.JoyAdvanced.Bool)
        {
            // Two axes only, with joystick control.
            _axes[ButtonCodes.AxisX].AxisMap = GameAxisYaw;
            _axes[ButtonCodes.AxisY].AxisMap = GameAxisForward;
        }
        else
        {
            if (!string.Equals(_cvars.JoyName.String, "joystick", StringComparison.OrdinalIgnoreCase))
                _host.Msg($"Using joystick '{_cvars.JoyName.String}' configuration\n");

            bool movementStick = _cvars.JoyMovementStick.Bool;
            MapAxis(ButtonCodes.AxisX, "JOY_AXIS_X", movementStick ? _cvars.JoyAdvAxisU.Int : _cvars.JoyAdvAxisX.Int);
            MapAxis(ButtonCodes.AxisY, "JOY_AXIS_Y", movementStick ? _cvars.JoyAdvAxisR.Int : _cvars.JoyAdvAxisY.Int);
            MapAxis(ButtonCodes.AxisZ, "JOY_AXIS_Z", _cvars.JoyAdvAxisZ.Int);
            MapAxis(ButtonCodes.AxisR, "JOY_AXIS_R", movementStick ? _cvars.JoyAdvAxisY.Int : _cvars.JoyAdvAxisR.Int);
            MapAxis(ButtonCodes.AxisU, "JOY_AXIS_U", movementStick ? _cvars.JoyAdvAxisX.Int : _cvars.JoyAdvAxisU.Int);
            MapAxis(ButtonCodes.AxisV, "JOY_AXIS_V", _cvars.JoyAdvAxisV.Int);

            _host.Msg("Advanced Joystick settings initialized\n");
        }

        // An Xbox controller found: its cfg once; none: undo it.
        string? found = _host.EngineConVar("joy_xcontroller_found");
        if (found is not null && (int)(float)CRuntime.Atof(found) != 0 && _cvars.Joystick.Bool)
        {
            if (_cvars.JoyXControllerCfgLoaded.Int < 2)
            {
                _host.ClientCmdUnrestricted("exec 360controller.cfg");
                _host.ClientCmdUnrestricted("exec 360controller-linux.cfg");
                _cvars.JoyXControllerCfgLoaded.Write(2);
            }
        }
        else if (_cvars.JoyXControllerCfgLoaded.Int > 0)
        {
            _host.ClientCmdUnrestricted("exec undo360controller.cfg");
            _cvars.JoyXControllerCfgLoaded.Write(0);
        }
    }

    private void MapAxis(int axis, string name, int value)
    {
        // DWORD: the convar's int as unsigned, masked.
        uint word = unchecked((uint)value);
        _axes[axis].AxisMap = (int)(word & 0x0000000f);
        _axes[axis].ControlMap = (int)(word & JoyRelativeAxis);

        if (_axes[axis].AxisMap == 0)
            _host.Msg($"{name}:  unmapped\n");
        else
            _host.Msg($"{name}:  mapped to {DescribeAxis(_axes[axis].AxisMap)} ({(_axes[axis].ControlMap != 0 ? "relative" : "absolute")})\n");
    }

    private static string DescribeAxis(int index) => index switch
    {
        GameAxisForward => "Forward",
        GameAxisPitch => "Look",
        GameAxisSide => "Side",
        GameAxisYaw => "Turn",
        _ => "Unknown",
    };

    /// <summary>CInput::ScaleAxisValue: the raw axis into [-1, 1] less the dead zone.</summary>
    private float ScaleAxisValue(float axisValue, float axisThreshold)
    {
        float result = 0.0f;
        if (axisValue < -axisThreshold)
            result = (axisValue + axisThreshold) / (ButtonCodes.MaxButtonSample - axisThreshold);
        else if (axisValue > axisThreshold)
            result = (axisValue - axisThreshold) / (ButtonCodes.MaxButtonSample - axisThreshold);
        return result;
    }

    /// <summary>CInput::Joystick_SetSampleTime.</summary>
    public void SetJoystickSampleTime(float frameTime) => _remainingJoystickSampleTime = frameTime;

    public float JoystickForward => _previousJoystickForward;
    public float JoystickSide => _previousJoystickSide;
    public float JoystickPitch => _previousJoystickPitch;
    public float JoystickYaw => _previousJoystickYaw;

    private struct GameAxis
    {
        public float Value;
        public int ControlType;
    }

    /// <summary>CInput::JoyStickMove: the stick's part of a command.</summary>
    private void JoyStickMove(float frameTime, ref InputCommand cmd)
    {
        TakeSampleTimes();
        // Complete initialisation the first time in (the convars were not up at Init).
        if (!_joystickAdvancedInit)
        {
            JoystickAdvanced();
            _joystickAdvancedInit = true;
        }

        if (_cvars.Joystick.Int == 0)
            return;

        // Re-initialise when hot-plugging toggled between some and none.
        bool haveJoysticks = _devices.JoystickCount > 0;
        if (haveJoysticks != _hadJoysticks)
        {
            JoystickAdvanced();
            _hadJoysticks = haveJoysticks;
        }

        if (!haveJoysticks)
            return;

        if (_remainingJoystickSampleTime <= 0)
            return;
        frameTime = MathF.Min(_remainingJoystickSampleTime, frameTime);
        _remainingJoystickSampleTime -= frameTime;

        InputAngles viewAngles = _host.GetViewAngles();

        Span<GameAxis> gameAxes = stackalloc GameAxis[MaxGameAxes];
        gameAxes.Clear();

        for (int i = 0; i < _axes.Length; ++i)
        {
            if (_axes[i].AxisMap == GameAxisNone)
                continue;

            float axisValue = _devices.GetAnalogValue(ButtonCodes.JoystickFirstAxis + i);

            if (_cvars.JoyWingmanHack.Int != 0)
            {
                // The Logitech WingMan Warrior: y = 300 * (|x| / 800) ^ 1.3,
                // capped at 14000, in double as stock's literals are.
                float temp = (float)(300.0 * Math.Pow(MathF.Abs(axisValue) / 800.0, 1.3));
                if (temp > 14000.0)
                    temp = 14000.0f;
                axisValue = axisValue > 0.0 ? temp : -temp;
            }

            int idx = _axes[i].AxisMap;
            gameAxes[idx].Value = axisValue;
            gameAxes[idx].ControlType = _axes[i].ControlMap;
        }

        // joy_advanced 0 and jlook: forward becomes pitch.
        if (_cvars.JoyAdvanced.Int == 0 && (Buttons.JLook.State & 1) != 0)
        {
            gameAxes[GameAxisPitch] = gameAxes[GameAxisForward];
            gameAxes[GameAxisForward].Value = 0;

            // An inverted mouse inverts the stick's pitch.
            if (MPitch() < 0.0)
                gameAxes[GameAxisPitch].Value *= -1;
        }

        if ((Buttons.Strafe.State & 1) != 0 || (_cvars.LookStrafe.Float != 0 && (Buttons.JLook.State & 1) != 0))
        {
            // Yaw becomes side.
            gameAxes[GameAxisSide] = gameAxes[GameAxisYaw];
            gameAxes[GameAxisYaw].Value = 0;
        }

        _previousJoystickForward = ScaleAxisValue(gameAxes[GameAxisForward].Value, ButtonCodes.MaxButtonSample * _cvars.JoyForwardThreshold.Float);
        _previousJoystickSide = ScaleAxisValue(gameAxes[GameAxisSide].Value, ButtonCodes.MaxButtonSample * _cvars.JoySideThreshold.Float);
        _previousJoystickPitch = ScaleAxisValue(gameAxes[GameAxisPitch].Value, ButtonCodes.MaxButtonSample * _cvars.JoyPitchThreshold.Float);
        _previousJoystickYaw = ScaleAxisValue(gameAxes[GameAxisYaw].Value, ButtonCodes.MaxButtonSample * _cvars.JoyYawThreshold.Float);

        // Skip out while vgui is up (or the UI's menu holds the input).
        if (_host.CursorVisible || _menu.MenuHasInput)
            return;

        if (_cvars.JoyInvertY.Bool)
            _previousJoystickPitch *= -1.0f;

        // Drive yaw, pitch and move like a screen-relative platformer.
        if (CamIsThirdPerson() && _cvars.ThirdPersonPlatformer.Int != 0)
        {
            if (_previousJoystickForward != 0 || _previousJoystickSide != 0)
            {
                // Turn, relative to the third-person camera.
                viewAngles.Yaw = MathX.Rad2Deg(MathF.Atan2(-_previousJoystickSide, -_previousJoystickForward)) + _thirdPerson.CameraOffset.Y;
                _host.SetViewAngles(viewAngles);

                float length = MathF.Sqrt(_previousJoystickForward * _previousJoystickForward + _previousJoystickSide * _previousJoystickSide);
                cmd.ForwardMove += length * _cvars.ForwardSpeed.Float;
            }

            if (_previousJoystickPitch != 0 || _previousJoystickYaw != 0)
            {
                Vector3f offset = _thirdPerson.CameraOffset;
                offset.X += _previousJoystickPitch * _cvars.JoyPitchSensitivity.Float;
                offset.Y += _previousJoystickYaw * _cvars.JoyYawSensitivity.Float;
                _thirdPerson.CameraOffset = offset;
            }

            if (_previousJoystickForward != 0 || _previousJoystickSide != 0 || _previousJoystickPitch != 0 || _previousJoystickYaw != 0)
            {
                Vector3f offset = _thirdPerson.CameraOffset;
                _cvars.CamIdealPitch.Write(offset.X - viewAngles.Pitch);
                _cvars.CamIdealYaw.Write(offset.Y - viewAngles.Yaw);
            }
            return;
        }

        float joySideMove = 0.0f;
        float joyForwardMove = 0.0f;
        float aspeed = frameTime * _host.Hud.FovSensitivityAdjust;

        // The on-foot curve: no vehicle reaches this client (view_facts.h).
        int responseCurve = _cvars.JoyResponseMove.Int;

        float val = ResponseCurve(responseCurve, _previousJoystickForward, Pitch, _cvars.JoyForwardSensitivity.Float);
        joyForwardMove += val * _cvars.ForwardSpeed.Float;
        val = ResponseCurve(responseCurve, _previousJoystickSide, Yaw, _cvars.JoySideSensitivity.Float);
        joySideMove += val * _cvars.SideSpeed.Float;

        float dist = MathF.Sqrt(_previousJoystickYaw * _previousJoystickYaw + _previousJoystickPitch * _previousJoystickPitch);

        // Turn.
        float angle;
        if (gameAxes[GameAxisYaw].ControlType == JoyAbsoluteAxis)
        {
            float axisValue = ResponseCurveLook(_cvars.JoyResponseLook.Int, _previousJoystickYaw, Yaw, _previousJoystickPitch, dist, frameTime);
            angle = axisValue * _cvars.JoyYawSensitivity.Float * aspeed * _cvars.YawSpeed.Float;
        }
        else
        {
            angle = (float)(_previousJoystickYaw * _cvars.JoyYawSensitivity.Float * aspeed * 180.0);
        }

        angle = JoyStickAdjustYaw(angle);
        viewAngles.Yaw += angle;
        cmd.MouseDx = (short)(int)angle;

        // Look.
        if ((Buttons.JLook.State & 1) != 0)
        {
            if (gameAxes[GameAxisPitch].ControlType == JoyAbsoluteAxis)
            {
                float axisValue = ResponseCurveLook(_cvars.JoyResponseLook.Int, _previousJoystickPitch, Pitch, _previousJoystickYaw, dist, frameTime);
                angle = axisValue * _cvars.JoyPitchSensitivity.Float * aspeed * _cvars.PitchSpeed.Float;
            }
            else
            {
                angle = (float)(_previousJoystickPitch * _cvars.JoyPitchSensitivity.Float * aspeed * 180.0);
            }
            viewAngles.Pitch += angle;
            cmd.MouseDy = (short)(int)angle;
            _host.StopPitchDrift();
            if (_previousJoystickPitch == 0.0f && _cvars.LookSpring.Float == 0.0f)
            {
                // No pitch movement: no return-to-centre unless asked.
                _host.StopPitchDrift();
            }
        }

        // Player motion relative to screen space.
        if (CamIsThirdPerson() && _cvars.ThirdPersonScreenSpace.Int != 0)
        {
            float idealYaw = _cvars.CamIdealYaw.Float;
            float idealSin = MathF.Sin(MathX.Deg2Rad(idealYaw));
            float idealCos = MathF.Cos(MathX.Deg2Rad(idealYaw));
            float sideMovement = idealCos * joySideMove - idealSin * joyForwardMove;
            float forwardMovement = idealCos * joyForwardMove + idealSin * joySideMove;
            cmd.ForwardMove += forwardMovement;
            cmd.SideMove += sideMovement;
        }
        else
        {
            cmd.ForwardMove += joyForwardMove;
            cmd.SideMove += joySideMove;
        }

        if (_isPc)
        {
            if (MathF.Abs(joyForwardMove) >= _cvars.JoyAutoSprint.Float || MathF.Abs(joySideMove) >= _cvars.JoyAutoSprint.Float)
                Buttons.JoySpeed.KeyDown(null);
            else
                Buttons.JoySpeed.KeyUp(null);
        }

        // Bound the pitch.
        viewAngles.Pitch = MathX.Clamp(viewAngles.Pitch, -_cvars.PitchUp.Float, _cvars.PitchDown.Float);

        _host.SetViewAngles(viewAngles);
    }

    /// <summary>CInput::JoyStickAdjustYaw: a hook for mods; stock's base answers its argument.</summary>
    private static float JoyStickAdjustYaw(float speed) => speed;
}
