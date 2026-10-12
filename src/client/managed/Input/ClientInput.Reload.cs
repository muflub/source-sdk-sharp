namespace SourceSharp.Client.Input;

// cs_cl_reload (plan_client_novalve §3.8): what the input carries across the
// swap. Native keeps the CUserCmd ring and the kbutton_t tokens; every button's
// state, the mouse's accumulators and capture, the stick's memory, the pad's
// handles and the camera are managed and go as bytes. The rumble mixer's
// channels go with them (ClientInputService). Nothing is recomputed but the
// next command.
public sealed partial class ClientInput
{
    /// <summary>The section's shape; a carried section of another shape is refused.</summary>
    private const int ReloadVersion = 2;

    public void Save(ReloadWriter w)
    {
        TakeSampleTimes();
        w.Write(ReloadVersion);
        w.Write((int)_keyboardSampleSeq);
        w.Write((int)_joystickSampleSeq);

        w.Write(Buttons.All.Count);
        foreach (KeyButton b in Buttons.All)
        {
            w.Write(b.Down0);
            w.Write(b.Down1);
            w.Write(b.State);
        }

        w.Write(_impulse);
        w.Write(_cancel);
        w.Write(_clearInputState);
        w.Write(_keyboardSampleTime);
        w.Write(_lastForwardMove);
        WriteAngles(w, _previousViewAngles);
        w.Write(_selectedWeaponIndex);

        w.Write(_mouseInitialized);
        w.Write(_mouseActive);
        w.Write(_accumulatedMouseX);
        w.Write(_accumulatedMouseY);
        w.Write(_previousMouseX);
        w.Write(_previousMouseY);
        w.Write(_captureWorkaroundLastMouseX);
        w.Write(_captureWorkaroundLastMouseY);

        w.Write(_axes.Length);
        foreach (JoyAxis axis in _axes)
        {
            w.Write(axis.AxisMap);
            w.Write(axis.ControlMap);
        }
        w.Write(_envelope.Scale0);
        w.Write(_envelope.Scale1);
        w.Write(_envelope.Pegged0);
        w.Write(_envelope.Pegged1);
        w.Write(_envelope.PeggedDir0);
        w.Write(_envelope.PeggedDir1);
        w.Write(_joystickAdvancedInit);
        w.Write(_hadJoysticks);
        w.Write(_remainingJoystickSampleTime);
        w.Write(_previousJoystickForward);
        w.Write(_previousJoystickSide);
        w.Write(_previousJoystickPitch);
        w.Write(_previousJoystickYaw);

        w.Write(_digitalActions.Length);
        foreach (DigitalActionState state in _digitalActions)
        {
            WriteHandle(w, state.Handle);
            w.Write(state.State);
            w.Write(state.AwaitingDebounce);
        }
        WriteHandle(w, _moveHandle);
        WriteHandle(w, _cameraHandle);
        w.Write(_preferredGameActionSet);
        w.Write(_gameActionSetFlags);
        w.Write(_steamControllerGameActionsInitialized);
        w.Write(_steamControllerSeenInput);

        w.Write(_cameraInterceptingMouse);
        w.Write(_cameraInThirdPerson);
        w.Write(_cameraMovingWithMouse);
        w.Write(_cameraDistanceMove);
        w.Write(_cameraOldX);
        w.Write(_cameraOldY);
        w.Write(_cameraX);
        w.Write(_cameraY);
        w.Write(_cameraIsOrthographic);
        w.Write(_cameraThirdData is not null);
        CameraThirdData data = _cameraThirdData ?? default;
        w.Write(data.Pitch);
        w.Write(data.Yaw);
        w.Write(data.Dist);
        w.Write(data.Lag);
        w.Write(_mayaOldAngles is not null);
        WriteAngles(w, _mayaOldAngles ?? default);
        w.Write(_mayaMode);

        WriteVector(w, _thirdPerson.CameraOffset);
        WriteVector(w, _thirdPerson.DesiredCameraOffset);
        w.Write(_thirdPerson.OverridingThirdPerson);
        w.Write(_thirdPerson.ForcedThirdPerson);
    }

    public void Restore(ReloadReader r)
    {
        int version = r.ReadInt();
        if (version != ReloadVersion)
            throw new ReloadFormatException($"input section version {version}, this build reads {ReloadVersion}");
        _keyboardSampleSeq = (uint)r.ReadInt();
        _joystickSampleSeq = (uint)r.ReadInt();

        int buttons = r.ReadInt();
        if (buttons != Buttons.All.Count)
            throw new ReloadFormatException($"{buttons} buttons carried, this build has {Buttons.All.Count}");
        foreach (KeyButton b in Buttons.All)
        {
            b.Down0 = r.ReadInt();
            b.Down1 = r.ReadInt();
            b.State = r.ReadInt();
        }

        _impulse = r.ReadInt();
        _cancel = r.ReadInt();
        _clearInputState = r.ReadInt();
        _keyboardSampleTime = r.ReadFloat();
        _lastForwardMove = r.ReadFloat();
        _previousViewAngles = ReadAngles(r);
        _selectedWeaponIndex = r.ReadInt();

        _mouseInitialized = r.ReadBool();
        _mouseActive = r.ReadBool();
        _accumulatedMouseX = r.ReadFloat();
        _accumulatedMouseY = r.ReadFloat();
        _previousMouseX = r.ReadFloat();
        _previousMouseY = r.ReadFloat();
        _captureWorkaroundLastMouseX = r.ReadInt();
        _captureWorkaroundLastMouseY = r.ReadInt();

        int axes = r.ReadInt();
        if (axes != _axes.Length)
            throw new ReloadFormatException($"{axes} joystick axes carried, this build has {_axes.Length}");
        for (int i = 0; i < _axes.Length; i++)
        {
            _axes[i].AxisMap = r.ReadInt();
            _axes[i].ControlMap = r.ReadInt();
        }
        _envelope.Scale0 = r.ReadFloat();
        _envelope.Scale1 = r.ReadFloat();
        _envelope.Pegged0 = r.ReadBool();
        _envelope.Pegged1 = r.ReadBool();
        _envelope.PeggedDir0 = r.ReadBool();
        _envelope.PeggedDir1 = r.ReadBool();
        _joystickAdvancedInit = r.ReadBool();
        _hadJoysticks = r.ReadBool();
        _remainingJoystickSampleTime = r.ReadFloat();
        _previousJoystickForward = r.ReadFloat();
        _previousJoystickSide = r.ReadFloat();
        _previousJoystickPitch = r.ReadFloat();
        _previousJoystickYaw = r.ReadFloat();

        int actions = r.ReadInt();
        if (actions != _digitalActions.Length)
            throw new ReloadFormatException($"{actions} controller actions carried, this build has {_digitalActions.Length}");
        for (int i = 0; i < _digitalActions.Length; i++)
        {
            _digitalActions[i].Handle = ReadHandle(r);
            _digitalActions[i].State = r.ReadBool();
            _digitalActions[i].AwaitingDebounce = r.ReadBool();
        }
        _moveHandle = ReadHandle(r);
        _cameraHandle = ReadHandle(r);
        _preferredGameActionSet = r.ReadInt();
        _gameActionSetFlags = r.ReadInt();
        _steamControllerGameActionsInitialized = r.ReadBool();
        _steamControllerSeenInput = r.ReadBool();

        _cameraInterceptingMouse = r.ReadBool();
        _cameraInThirdPerson = r.ReadBool();
        _cameraMovingWithMouse = r.ReadBool();
        _cameraDistanceMove = r.ReadBool();
        _cameraOldX = r.ReadInt();
        _cameraOldY = r.ReadInt();
        _cameraX = r.ReadInt();
        _cameraY = r.ReadInt();
        _cameraIsOrthographic = r.ReadBool();
        bool hasData = r.ReadBool();
        var data = new CameraThirdData(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
        _cameraThirdData = hasData ? data : null;
        bool hasMaya = r.ReadBool();
        InputAngles maya = ReadAngles(r);
        _mayaOldAngles = hasMaya ? maya : null;
        _mayaMode = r.ReadBool();

        _thirdPerson.CameraOffset = ReadVector(r);
        _thirdPerson.DesiredCameraOffset = ReadVector(r);
        _thirdPerson.OverridingThirdPerson = r.ReadBool();
        _thirdPerson.ForcedThirdPerson = r.ReadBool();

        PublishState();
    }

    private static void WriteAngles(ReloadWriter w, InputAngles a)
    {
        w.Write(a.Pitch);
        w.Write(a.Yaw);
        w.Write(a.Roll);
    }

    private static InputAngles ReadAngles(ReloadReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

    private static void WriteVector(ReloadWriter w, Vector3f v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static Vector3f ReadVector(ReloadReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

    private static void WriteHandle(ReloadWriter w, ulong handle)
    {
        w.Write(unchecked((int)(uint)handle));
        w.Write(unchecked((int)(uint)(handle >> 32)));
    }

    private static ulong ReadHandle(ReloadReader r)
    {
        ulong low = unchecked((uint)r.ReadInt());
        ulong high = unchecked((uint)r.ReadInt());
        return low | (high << 32);
    }
}
