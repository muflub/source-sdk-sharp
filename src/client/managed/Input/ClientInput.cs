//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>
/// The command a <see cref="ClientInput.CreateMove"/> builds: the fields of a
/// <c>CUserCmd</c> the input code writes. The native shell owns the CUserCmd
/// ring and the wire codec; this is a field copy (docs/abi-parity.tsv says why
/// CUserCmd itself is no K2 row).
/// </summary>
public struct InputCommand
{
    public int CommandNumber;
    public int TickCount;
    public InputAngles ViewAngles;
    public float ForwardMove, SideMove, UpMove;
    public int Buttons;
    public int Impulse;
    public int WeaponSelect;
    public int WeaponSubtype;
    public int RandomSeed;
    /// <summary>CUserCmd's are <c>short</c>: what the input code writes wraps to 16 bits there, and here.</summary>
    public int MouseDx, MouseDy;

    /// <summary><c>CUserCmd::Reset</c>.</summary>
    public void Reset()
    {
        this = default;
    }
}

/// <summary>
/// The client's input, ported from stock's <c>CInput</c> (in_main.cpp,
/// in_mouse.cpp, in_joystick.cpp, in_steamcontroller.cpp, in_camera.cpp):
/// the key buttons, the user command a frame's input makes, the mouse's
/// filtering and acceleration, the joystick's curves, the Steam Controller's
/// actions and the third-person camera's offsets.
///
/// <para>
/// THE ARITHMETIC IS STOCK'S, IN STOCK'S TYPES. The oracle
/// (InputStockOracleTests over input-stock.tsv.gz) replays a recording of the
/// stock code and compares every float bit for bit, so a float expression
/// stays a float expression and a double one (180.0, 300.0 * pow) stays
/// double, and the transcendental calls are the float ones the C++ overloads
/// resolved to (MathF, which is glibc's sinf/powf/atan2f underneath).
/// </para>
/// </summary>
public sealed partial class ClientInput
{
    private readonly IInputHost _host;
    private readonly IInputDevices _devices;
    private readonly InputCvars _cvars;
    private readonly IMenuInputGate _menu;
    private readonly ThirdPersonManager _thirdPerson;

    // in_main.cpp's file statics.
    private int _impulse;
    private int _cancel;
    private int _clearInputState;

    // CInput's members.
    private float _keyboardSampleTime;
    private float _lastForwardMove;
    private InputAngles _previousViewAngles;
    private int _selectedWeaponIndex;

    public ClientInput(IInputHost host, IInputDevices devices, InputCvars cvars,
                       IMenuInputGate? menu = null, bool isPc = true, IInputMailbox? mailbox = null)
    {
        _mailbox = mailbox;
        _host = host;
        _devices = devices;
        _cvars = cvars;
        _menu = menu ?? new NoMenuInputGate();
        _isPc = isPc;
        Buttons = new KeyButtons();
        _digitalActions = new DigitalActionState[DigitalGameActions.Length];
        _thirdPerson = new ThirdPersonManager(this, host, cvars);
        _joyMovementStickHook = _ => _host.ClientCmdUnrestricted("joyadvancedupdate");
        _cvars.JoyMovementStick.Changed += _joyMovementStickHook;
        _cvars.ThirdPerson.Changed += cvar => _thirdPerson.ToggleThirdPerson(cvar.Bool);
        PublishState();
    }

    //-------------------------------------------------------------------------
    // The mailbox (shell_input.cpp): native stores the sample times there, and
    // reads there which of its calls would change nothing.
    //-------------------------------------------------------------------------

    private readonly IInputMailbox? _mailbox;
    private uint _keyboardSampleSeq;
    private uint _joystickSampleSeq;
    private InputPublished _published;

    /// <summary>
    /// IN_SetSampleTime and Joystick_SetSampleTime's stores since the last
    /// read, applied. Each is a plain assignment that only this class reads,
    /// so taking the last one right before a read is the same as taking each
    /// when it was made. Every reader of the two fields calls this first.
    /// </summary>
    private void TakeSampleTimes()
    {
        if (_mailbox is null)
            return;
        if (_mailbox.TakeKeyboardSampleTime(ref _keyboardSampleSeq, out float keyboard))
            _keyboardSampleTime = keyboard;
        if (_mailbox.TakeJoystickSampleTime(ref _joystickSampleSeq, out float joystick))
            _remainingJoystickSampleTime = joystick;
    }

    /// <summary>
    /// Which IInput calls would change nothing now, and CAM_IsThirdPerson's
    /// answer: stock's own early returns in ActivateMouse and DeactivateMouse,
    /// over this instance's state.
    /// </summary>
    public InputPublished Published()
    {
        InputPublished p = InputPublished.Valid;
        if (_mouseActive || !_mouseInitialized)
            p |= InputPublished.ActivateNoop;
        if (!_mouseActive || !_mouseInitialized)
            p |= InputPublished.DeactivateNoop;
        if (_cameraInThirdPerson)
            p |= InputPublished.ThirdPerson;
        return p;
    }

    /// <summary>Republishes <see cref="Published"/> when it moved. Every write of a field it reads calls this.</summary>
    private void PublishState()
    {
        InputPublished p = Published();
        if (p == _published)
            return;
        _published = p;
        _mailbox?.Publish(p);
    }

    private readonly bool _isPc;
    private readonly Action<InputCvar> _joyMovementStickHook;

    /// <summary>Every button the key commands drive.</summary>
    public KeyButtons Buttons { get; }

    /// <summary>The third-person manager (cam_thirdperson.cpp's CThirdPersonManager).</summary>
    public ThirdPersonManager ThirdPersonCamera => _thirdPerson;

    /// <summary>
    /// <c>g_iAlive</c>: stock's FIXME ("tie to entity state parsing for
    /// player"), initialised to 1 and written by nothing in the SDK, so the
    /// alive arm is the only one ever taken.
    /// </summary>
    private const bool Alive = true;

    //-------------------------------------------------------------------------
    // Init_All / Shutdown_All / LevelInit
    //-------------------------------------------------------------------------

    /// <summary>
    /// CInput::Init_All. <paramref name="noMouse"/> is <c>-nomouse</c> on the
    /// command line.
    /// </summary>
    public void InitAll(bool noMouse)
    {
        _mouseInitialized = false;
        _mouseActive = false;
        PublishState();
        _joystickAdvancedInit = false;
        _hadJoysticks = false;
        _lastForwardMove = 0.0f;

        // Stock activated the client Steam context here "so steam controller
        // stuff works"; the input system's own controller is what is read.
        if (_isPc)
        {
            InitMouse(noMouse);
            // Init_Keyboard: the engine may ask for in_graph and in_jlook by
            // name; KeyButtons answers for every button, which includes both.
        }

        InitCamera();

        _steamControllerGameActionsInitialized = InitializeSteamControllerGameActionSets();
    }

    /// <summary>CInput::Shutdown_All (the command ring is the shell's).</summary>
    public void ShutdownAll() => DeactivateMouse();

    /// <summary>CInput::LevelInit: HL2's IK ground contacts only, not compiled here.</summary>
    public void LevelInit()
    {
    }

    //-------------------------------------------------------------------------
    // Key commands
    //-------------------------------------------------------------------------

    /// <summary><c>impulse N</c>.</summary>
    public void Impulse(string? args) => _impulse = CRuntime.Atoi(FirstToken(args));

    /// <summary>+attack's release also ends a cancel (IN_AttackUp).</summary>
    internal void ClearCancel() => _cancel = 0;

    /// <summary>The first argument of a command line, as <c>args[1]</c> reads it; null for none.</summary>
    public static string? FirstToken(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return null;
        string trimmed = args.TrimStart();
        if (trimmed.StartsWith('"'))
        {
            int close = trimmed.IndexOf('"', 1);
            return close < 0 ? trimmed[1..] : trimmed[1..close];
        }
        int space = trimmed.IndexOfAny([' ', '\t']);
        return space < 0 ? trimmed : trimmed[..space];
    }

    /// <summary>
    /// CInput::KeyEvent: 0 swallows the key, 1 lets the engine run its
    /// binding, -1 asks the client mode (<c>g_pClientMode-&gt;KeyInput</c>),
    /// which the shell carries out.
    /// </summary>
    public int KeyEvent(int down, int keyCode, string? currentBinding)
    {
        // The UI's menu layer holds the keyboard: the press is the menu's, so
        // its binding does not also run. A RELEASE always goes through, or a
        // key held before the menu opened (+forward) would stick down.
        if (down != 0 && _menu.MenuHasInput)
            return 0;

        // Deal with the camera intercepting the mouse.
        if (keyCode == ButtonCodes.MouseLeft || keyCode == ButtonCodes.MouseRight)
        {
            if (_cameraInterceptingMouse)
                return 0;
        }

        return -1;
    }

    /// <summary><c>force_centerview</c> (IN_CenterView_f): the pitch level, when keyboard-looking.</summary>
    public void CenterView()
    {
        if (_cvars.MouseLook.Bool)
            return;
        if (_cameraInterceptingMouse)
            return;
        InputAngles viewAngles = _host.GetViewAngles();
        viewAngles.Pitch = 0;
        _host.SetViewAngles(viewAngles);
    }

    /// <summary>CInput::IN_SetSampleTime (the shell calls Joystick_SetSampleTime with the same time first).</summary>
    public void SetKeyboardSampleTime(float frameTime) => _keyboardSampleTime = frameTime;

    /// <summary>CInput::KeyState over one button: the fraction of the frame it was down.</summary>
    public float KeyState(KeyButton key) => key.Sample();

    /// <summary>The engine's IN_IsKeyDown(name): false when no button has that name.</summary>
    public bool TryIsKeyDown(string name, out bool down)
    {
        KeyButton? key = Buttons.FindForEngine(name);
        down = key is not null && (key.State & KeyButton.Held) != 0;
        return key is not null;
    }

    private float DetermineKeySpeed(float frameTime)
    {
        TakeSampleTimes();
        if (_cvars.UseKeyboardSampleTime.Bool)
        {
            if (_keyboardSampleTime <= 0)
                return 0.0f;

            frameTime = MathF.Min(_keyboardSampleTime, frameTime);
            _keyboardSampleTime -= frameTime;
        }

        float speed = frameTime;

        if ((Buttons.Speed.State & 1) != 0)
            speed *= _cvars.AngleSpeedKey.Float;

        return speed;
    }

    private void AdjustYaw(float speed, ref InputAngles viewAngles)
    {
        if ((Buttons.Strafe.State & 1) == 0)
        {
            viewAngles.Yaw -= speed * _cvars.YawSpeed.Float * KeyState(Buttons.Right);
            viewAngles.Yaw += speed * _cvars.YawSpeed.Float * KeyState(Buttons.Left);
        }

        // Third-person platformer: the movement keys aim the player relative to
        // the third-person camera.
        if (CamIsThirdPerson() && _cvars.ThirdPersonPlatformer.Int != 0)
        {
            float side = KeyState(Buttons.MoveLeft) - KeyState(Buttons.MoveRight);
            float forward = KeyState(Buttons.Forward) - KeyState(Buttons.Back);

            if (side != 0 || forward != 0)
                viewAngles.Yaw = MathX.Rad2Deg(MathF.Atan2(side, forward)) + _thirdPerson.CameraOffset.Y;

            if (side != 0 || forward != 0 || KeyState(Buttons.Right) != 0 || KeyState(Buttons.Left) != 0)
                _cvars.CamIdealYaw.Write(_thirdPerson.CameraOffset.Y - viewAngles.Yaw);
        }
    }

    private void AdjustPitch(float speed, ref InputAngles viewAngles)
    {
        // Keyboard looking only when mouse look is off.
        if (_cvars.MouseLook.Bool)
            return;

        if ((Buttons.KLook.State & 1) != 0)
        {
            _host.StopPitchDrift();
            viewAngles.Pitch -= speed * _cvars.PitchSpeed.Float * KeyState(Buttons.Forward);
            viewAngles.Pitch += speed * _cvars.PitchSpeed.Float * KeyState(Buttons.Back);
        }

        float up = KeyState(Buttons.LookUp);
        float down = KeyState(Buttons.LookDown);

        viewAngles.Pitch -= speed * _cvars.PitchSpeed.Float * up;
        viewAngles.Pitch += speed * _cvars.PitchSpeed.Float * down;

        if (up != 0 || down != 0)
            _host.StopPitchDrift();
    }

    private void ClampAngles(ref InputAngles viewAngles)
    {
        if (viewAngles.Pitch > _cvars.PitchDown.Float)
            viewAngles.Pitch = _cvars.PitchDown.Float;
        if (viewAngles.Pitch < -_cvars.PitchUp.Float)
            viewAngles.Pitch = -_cvars.PitchUp.Float;

        if (viewAngles.Roll > 50)
            viewAngles.Roll = 50;
        if (viewAngles.Roll < -50)
            viewAngles.Roll = -50;
    }

    private void AdjustAngles(float frameTime)
    {
        float speed = DetermineKeySpeed(frameTime);
        if (speed <= 0.0f)
            return;

        InputAngles viewAngles = _host.GetViewAngles();
        AdjustYaw(speed, ref viewAngles);
        AdjustPitch(speed, ref viewAngles);
        ClampAngles(ref viewAngles);
        _host.SetViewAngles(viewAngles);
    }

    private void ComputeSideMove(ref InputCommand cmd)
    {
        // Third-person platformer: no sideways movement.
        if (CamIsThirdPerson() && _cvars.ThirdPersonPlatformer.Int != 0)
            return;

        // Third-person screen space.
        if (CamIsThirdPerson() && _cvars.ThirdPersonScreenSpace.Int != 0)
        {
            float idealYaw = _cvars.CamIdealYaw.Float;
            float idealSin = MathF.Sin(MathX.Deg2Rad(idealYaw));
            float idealCos = MathF.Cos(MathX.Deg2Rad(idealYaw));

            float movement = idealCos * KeyState(Buttons.MoveRight)
                + idealSin * KeyState(Buttons.Back)
                + -idealCos * KeyState(Buttons.MoveLeft)
                + -idealSin * KeyState(Buttons.Forward);

            cmd.SideMove += _cvars.SideSpeed.Float * movement;
            return;
        }

        // Strafing: left and right act as moveleft and moveright.
        if ((Buttons.Strafe.State & 1) != 0)
        {
            cmd.SideMove += _cvars.SideSpeed.Float * KeyState(Buttons.Right);
            cmd.SideMove -= _cvars.SideSpeed.Float * KeyState(Buttons.Left);
        }

        cmd.SideMove += _cvars.SideSpeed.Float * KeyState(Buttons.MoveRight);
        cmd.SideMove -= _cvars.SideSpeed.Float * KeyState(Buttons.MoveLeft);
    }

    private void ComputeUpwardMove(ref InputCommand cmd)
    {
        cmd.UpMove += _cvars.UpSpeed.Float * KeyState(Buttons.Up);
        cmd.UpMove -= _cvars.UpSpeed.Float * KeyState(Buttons.Down);
    }

    private void ComputeForwardMove(ref InputCommand cmd)
    {
        // Third-person platformer: movement is always forward.
        if (CamIsThirdPerson() && _cvars.ThirdPersonPlatformer.Int != 0)
        {
            // Stock's `||` chain: each KeyState runs only until one is non-zero,
            // and the edges of the ones after it are left alone.
            bool any = KeyState(Buttons.Forward) != 0
                || KeyState(Buttons.MoveRight) != 0
                || KeyState(Buttons.Back) != 0
                || KeyState(Buttons.MoveLeft) != 0;
            float movement = any ? 1.0f : 0.0f;

            cmd.ForwardMove += _cvars.ForwardSpeed.Float * movement;
            return;
        }

        // Third-person screen space.
        if (CamIsThirdPerson() && _cvars.ThirdPersonScreenSpace.Int != 0)
        {
            float idealYaw = _cvars.CamIdealYaw.Float;
            float idealSin = MathF.Sin(MathX.Deg2Rad(idealYaw));
            float idealCos = MathF.Cos(MathX.Deg2Rad(idealYaw));

            float movement = idealCos * KeyState(Buttons.Forward)
                + idealSin * KeyState(Buttons.MoveRight)
                + -idealCos * KeyState(Buttons.Back)
                + -idealSin * KeyState(Buttons.MoveLeft);

            cmd.ForwardMove += _cvars.ForwardSpeed.Float * movement;
            return;
        }

        if ((Buttons.KLook.State & 1) == 0)
        {
            cmd.ForwardMove += _cvars.ForwardSpeed.Float * KeyState(Buttons.Forward);
            cmd.ForwardMove -= _cvars.BackSpeed.Float * KeyState(Buttons.Back);
        }
    }

    // ScaleMovements: stock returns at its top ("FIXME FIXME: This doesn't
    // work") and the clip to max speed below the return is commented out.

    private void ControllerMove(float frameTime, ref InputCommand cmd)
    {
        if (_isPc)
        {
            // An input/inject delta (a dev-API write) is read whether or not the
            // window holds the mouse: the engine releases it every frame the
            // window lacks focus, and the scripted delta is "as if captured".
            if (!_cameraInterceptingMouse && (_mouseActive || _injectPending))
                MouseMove(ref cmd);
        }

        SteamControllerMove(frameTime, ref cmd);
        JoyStickMove(frameTime, ref cmd);

        // NVNT haptics: no Novint device is ever present (the haptics
        // interface is not created in this build), so the block never ran.
    }

    /// <summary>CInput::MakeWeaponSelection: the entity index the next command reports (iinput.h says why an index).</summary>
    public void MakeWeaponSelection(int weaponEntIndex) => _selectedWeaponIndex = weaponEntIndex;

    //-------------------------------------------------------------------------
    // CreateMove / ExtraMouseSample
    //-------------------------------------------------------------------------

    /// <summary>
    /// CInput::ExtraMouseSample: a frame between commands turns the view by
    /// the keys, mouse and sticks so the view does not wait for the next tick.
    /// </summary>
    public InputCommand ExtraMouseSample(float frameTime, bool active)
    {
        InputCommand cmd = default;

        if (active)
        {
            AdjustAngles(frameTime);
            ComputeSideMove(ref cmd);
            ComputeUpwardMove(ref cmd);
            ComputeForwardMove(ref cmd);
            ControllerMove(frameTime, ref cmd);
        }

        InputAngles viewAngles = _host.GetViewAngles();

        cmd.Buttons = GetButtonBits(false);

        // Alive: the new view angles (see Alive).
        cmd.ViewAngles = viewAngles;
        _previousViewAngles = viewAngles;

        // The client mode accepts every command (ClientModeShared::CreateMove
        // answers true), so the angles it might have tweaked go back.
        _host.SetViewAngles(cmd.ViewAngles);
        _host.SetLocalViewAngles(cmd.ViewAngles);

        // The head tracker's override needs VR, which this build deleted.
        return cmd;
    }

    /// <summary>
    /// CInput::CreateMove: the user command for <paramref name="sequenceNumber"/>.
    /// <paramref name="cmd"/> arrives with the command number and tick count the
    /// shell set; the random seed is the shell's wire concern too, but is
    /// computed here so the whole command is one decision.
    /// </summary>
    public void CreateMove(int sequenceNumber, float inputSampleFrameTime, bool active, ref InputCommand cmd)
    {
        if (active || _cvars.NoclipDuringPause.Int != 0)
        {
            AdjustAngles(inputSampleFrameTime);
            ComputeSideMove(ref cmd);
            ComputeUpwardMove(ref cmd);
            ComputeForwardMove(ref cmd);
            ControllerMove(inputSampleFrameTime, ref cmd);
        }
        else
        {
            // Run and reset the mouse, so unpausing does not pop the view.
            if (!_cameraInterceptingMouse && _mouseActive)
            {
                GetAccumulatedMouseDeltasAndResetAccumulators(out _, out _);
                ResetMouse();
            }
        }

        InputAngles viewAngles = _host.GetViewAngles();

        // Latch and clear the impulse.
        cmd.Impulse = _impulse;
        _impulse = 0;

        // Latch and clear the weapon selection: an entity index (iinput.h);
        // the subtype is 0, as every SDK weapon's m_iSubType was.
        if (_selectedWeaponIndex != 0)
        {
            cmd.WeaponSelect = _selectedWeaponIndex;
            cmd.WeaponSubtype = 0;
            _selectedWeaponIndex = 0;
        }

        cmd.Buttons = GetButtonBits(true);

        // Using the joystick? The stick's forward sets the forward/back bits.
        if (_cvars.Joystick.Int != 0)
        {
            if (cmd.ForwardMove > 0)
                cmd.Buttons |= (int)InputButtons.Forward;
            else if (cmd.ForwardMove < 0)
                cmd.Buttons |= (int)InputButtons.Back;
        }

        cmd.ViewAngles = viewAngles;
        _previousViewAngles = viewAngles;

        // The client mode accepts the command (see ExtraMouseSample).
        _host.SetViewAngles(cmd.ViewAngles);

        _lastForwardMove = cmd.ForwardMove;

        cmd.RandomSeed = (int)(UserCmdCodec.Md5PseudoRandom(unchecked((uint)sequenceNumber)) & 0x7fffffff);

        // An injected key press lasts one command (input/inject).
        if (_injectedKey is not null)
        {
            _injectedKey.KeyUp(InjectedKeyCode);
            _injectedKey = null;
        }
    }

    /// <summary>The key code an injected press carries, so its release matches it and no real key's.</summary>
    private const string InjectedKeyCode = "9999";

    private KeyButton? _injectedKey;

    /// <summary>
    /// The dev API's <c>input/inject</c> key press: the button goes down now and
    /// up after the next command, as one frame's press. False for no such button.
    /// </summary>
    public bool InjectKey(string name)
    {
        KeyButton? button = Buttons.Find(name);
        if (button is null)
            return false;
        _injectedKey?.KeyUp(InjectedKeyCode);
        button.KeyDown(InjectedKeyCode);
        _injectedKey = button;
        return true;
    }

    //-------------------------------------------------------------------------
    // Buttons to bits
    //-------------------------------------------------------------------------

    private void CalcButtonBits(ref int bits, InputButtons inButton, int ignore, KeyButton button, bool reset)
    {
        // Down or still down?
        if ((button.State & 3) != 0)
            bits |= (int)inButton;

        int clearMask = ~2;
        if ((ignore & (int)inButton) != 0)
        {
            // Remove "still down" as well as "just down".
            clearMask = ~3;
        }

        if (reset)
            button.State &= clearMask;
    }

    /// <summary>CInput::GetButtonBits: the IN_ bits the buttons say; reset clears the down edges.</summary>
    public int GetButtonBits(bool resetState)
    {
        int bits = 0;
        int ignore = _clearInputState;
        KeyButtons b = Buttons;

        CalcButtonBits(ref bits, InputButtons.Speed, ignore, b.Speed, resetState);
        CalcButtonBits(ref bits, InputButtons.Walk, ignore, b.Walk, resetState);
        CalcButtonBits(ref bits, InputButtons.Attack, ignore, b.Attack, resetState);
        CalcButtonBits(ref bits, InputButtons.Duck, ignore, b.Duck, resetState);
        CalcButtonBits(ref bits, InputButtons.Jump, ignore, b.Jump, resetState);
        CalcButtonBits(ref bits, InputButtons.Forward, ignore, b.Forward, resetState);
        CalcButtonBits(ref bits, InputButtons.Back, ignore, b.Back, resetState);
        CalcButtonBits(ref bits, InputButtons.Use, ignore, b.Use, resetState);
        CalcButtonBits(ref bits, InputButtons.Left, ignore, b.Left, resetState);
        CalcButtonBits(ref bits, InputButtons.Right, ignore, b.Right, resetState);
        CalcButtonBits(ref bits, InputButtons.MoveLeft, ignore, b.MoveLeft, resetState);
        CalcButtonBits(ref bits, InputButtons.MoveRight, ignore, b.MoveRight, resetState);
        CalcButtonBits(ref bits, InputButtons.Attack2, ignore, b.Attack2, resetState);
        CalcButtonBits(ref bits, InputButtons.Reload, ignore, b.Reload, resetState);
        CalcButtonBits(ref bits, InputButtons.Alt1, ignore, b.Alt1, resetState);
        CalcButtonBits(ref bits, InputButtons.Alt2, ignore, b.Alt2, resetState);
        CalcButtonBits(ref bits, InputButtons.Score, ignore, b.Score, resetState);
        CalcButtonBits(ref bits, InputButtons.Zoom, ignore, b.Zoom, resetState);
        CalcButtonBits(ref bits, InputButtons.Grenade1, ignore, b.Grenade1, resetState);
        CalcButtonBits(ref bits, InputButtons.Grenade2, ignore, b.Grenade2, resetState);
        CalcButtonBits(ref bits, InputButtons.Attack3, ignore, b.Attack3, resetState);

        if (KeyState(b.DuckToggle) != 0)
            bits |= (int)InputButtons.Duck;

        // Cancel is a special flag.
        if (_cancel != 0)
            bits |= (int)InputButtons.Cancel;

        int hudBits = _host.Hud.KeyBits;
        if ((hudBits & (int)InputButtons.Weapon1) != 0)
            bits |= (int)InputButtons.Weapon1;
        if ((hudBits & (int)InputButtons.Weapon2) != 0)
            bits |= (int)InputButtons.Weapon2;

        // Clear out any residual.
        bits &= ~_clearInputState;

        if (resetState)
            _clearInputState = 0;

        return bits;
    }

    /// <summary>CInput::ClearInputButton: the bits have to be pressed again to count.</summary>
    public void ClearInputButton(int bits) => _clearInputState |= bits;

    /// <summary>CInput::GetLookSpring.</summary>
    public float GetLookSpring() => _cvars.LookSpring.Int;

    /// <summary>CInput::GetLastForwardMove.</summary>
    public float GetLastForwardMove() => _lastForwardMove;

    /// <summary>CInput::ControllerCommands: "not currently used" (controller buttons come through the window procs).</summary>
    public void ControllerCommands()
    {
    }

    /// <summary>CInput::EnableJoystickMode (never a console here).</summary>
    public bool EnableJoystickMode() => _cvars.Joystick.Bool;

    /// <summary>The state the input code keeps across commands, for the dev route and the oracle.</summary>
    public InputSnapshot Snapshot()
    {
        TakeSampleTimes();
        return SnapshotNow();
    }

    private InputSnapshot SnapshotNow() => new(
        _impulse, _cancel, _clearInputState, _keyboardSampleTime, _remainingJoystickSampleTime,
        _lastForwardMove, _previousViewAngles, _previousMouseX, _previousMouseY,
        _accumulatedMouseX, _accumulatedMouseY, _previousJoystickForward, _previousJoystickSide,
        _previousJoystickPitch, _previousJoystickYaw, _envelope.Scale0, _envelope.Scale1,
        _envelope.Pegged0, _envelope.Pegged1, _envelope.PeggedDir0, _envelope.PeggedDir1,
        _mouseActive, _cameraInterceptingMouse, _cameraInThirdPerson, _cameraIsOrthographic);
}

/// <summary>What <see cref="ClientInput.Snapshot"/> reports.</summary>
public readonly record struct InputSnapshot(
    int Impulse, int Cancel, int ClearInputState, float KeyboardSampleTime, float JoystickSampleTime,
    float LastForwardMove, InputAngles PreviousViewAngles, float PreviousMouseX, float PreviousMouseY,
    float AccumulatedMouseX, float AccumulatedMouseY, float JoystickForward, float JoystickSide,
    float JoystickPitch, float JoystickYaw, float Envelope0, float Envelope1,
    bool Pegged0, bool Pegged1, bool PeggedDir0, bool PeggedDir1,
    bool MouseActive, bool CameraInterceptingMouse, bool ThirdPerson, bool Orthographic);

/// <summary>The button and analog codes input compares against (inputsystem/ButtonCode.h, AnalogCode.h, Linux).</summary>
public static class ButtonCodes
{
    public const int MouseLeft = 107;
    public const int MouseRight = 108;
    public const int SteamControllerFirst = 171;
    public const int SteamControllerLast = 634;

    /// <summary>JOYSTICK_FIRST_AXIS.</summary>
    public const int JoystickFirstAxis = 4;

    /// <summary>MAX_JOYSTICK_AXES; Linux orders them X, Y, Z, U, R, V.</summary>
    public const int JoystickAxes = 6;

    public const int AxisX = 0, AxisY = 1, AxisZ = 2, AxisU = 3, AxisR = 4, AxisV = 5;

    /// <summary>MAX_BUTTONSAMPLE.</summary>
    public const int MaxButtonSample = 32768;
}

/// <summary>mathlib's float angle conversions, with its own float constants.</summary>
public static class MathX
{
    /// <summary>RAD2DEG: <c>(float)x * (float)(180.f / M_PI_F)</c>.</summary>
    public static float Rad2Deg(float x) => x * Rad2DegFactor();

    /// <summary>DEG2RAD: <c>(float)x * (float)(M_PI_F / 180.f)</c>.</summary>
    public static float Deg2Rad(float x) => x * Deg2RadFactor();

    // Computed in float at run time, as the C++ macro is, rather than folded
    // by the compiler at whatever precision it chooses.
    private static float Rad2DegFactor()
    {
        float pi = (float)Math.PI;
        return 180.0f / pi;
    }

    private static float Deg2RadFactor()
    {
        float pi = (float)Math.PI;
        return pi / 180.0f;
    }

    /// <summary>
    /// mathlib's clamp: the max wins when the bounds cross. (The release
    /// build's SSE body differs only for a NaN or crossed bounds.)
    /// </summary>
    public static float Clamp(float val, float minVal, float maxVal)
    {
        if (maxVal < minVal)
            return maxVal;
        if (val < minVal)
            return minVal;
        if (val > maxVal)
            return maxVal;
        return val;
    }

    /// <summary>mathlib's template clamp, for ints.</summary>
    public static int Clamp(int val, int minVal, int maxVal)
    {
        if (maxVal < minVal)
            return maxVal;
        if (val < minVal)
            return minVal;
        if (val > maxVal)
            return maxVal;
        return val;
    }

    /// <summary>mathlib's RemapVal.</summary>
    public static float RemapVal(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        return c + (d - c) * (val - a) / (b - a);
    }
}
