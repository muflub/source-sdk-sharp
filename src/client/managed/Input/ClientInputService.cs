using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>A command as the input route shows it: what CreateMove built.</summary>
public readonly record struct RecentCommand(int CommandNumber, int TickCount, float Pitch, float Yaw, float Roll,
                                            float ForwardMove, float SideMove, float UpMove, int Buttons, int Impulse,
                                            int WeaponSelect, int MouseDx, int MouseDy, int RandomSeed);

/// <summary>
/// The native IInput thunk set's managed half (plan_client_novalve §2 cluster
/// 13): the IManagedInputClient slots, answered by <see cref="ClientInput"/>
/// and <see cref="ClientRumble"/>. Also what the input dev route reads: the
/// last 16 commands, and an injected mouse delta for the next one
/// (<c>input/inject</c>, cheats only).
/// </summary>
public sealed class ClientInputService : IClientInputService, IReloadParticipant
{
    public const int RecentCount = 16;

    private readonly ClientInput _input;
    private readonly ClientRumble? _rumble;
    private readonly Func<bool> _localPlayerAlive;
    private readonly Func<float> _curTime;
    private readonly Func<bool> _paused;
    private readonly Action<UserCmd, float, bool>? _afterCommand;
    private readonly RecentCommand[] _recent = new RecentCommand[RecentCount];
    private int _recentCount;
    private int _recentNext;

    public ClientInputService(ClientInput input, ClientRumble? rumble, Func<bool> localPlayerAlive,
                              Action<UserCmd, float, bool>? afterCommand = null,
                              Func<float>? curTime = null, Func<bool>? paused = null)
    {
        _input = input;
        _rumble = rumble;
        _localPlayerAlive = localPlayerAlive;
        _curTime = curTime ?? (() => 0.0f);
        _paused = paused ?? (() => false);
        _afterCommand = afterCommand;
    }

    public ClientInput Input => _input;

    public ClientRumble? Rumble => _rumble;

    /// <summary>The commands built so far, oldest first, at most <see cref="RecentCount"/>.</summary>
    public IReadOnlyList<RecentCommand> Recent()
    {
        var list = new List<RecentCommand>(_recentCount);
        int start = (_recentNext - _recentCount + RecentCount) % RecentCount;
        for (int i = 0; i < _recentCount; i++)
            list.Add(_recent[(start + i) % RecentCount]);
        return list;
    }

    /// <summary>cs_cl_reload's save: the input's state, then the rumble mixer's when there is one.</summary>
    public void Save(ReloadWriter writer)
    {
        _input.Save(writer);
        writer.Write(_rumble is not null);
        _rumble?.Save(writer);
    }

    /// <summary>cs_cl_reload's restore, into this freshly built service (native does not Init it again).</summary>
    public void Restore(ReloadReader reader)
    {
        _input.Restore(reader);
        bool hadRumble = reader.ReadBool();
        if (hadRumble && _rumble is not null)
            _rumble.Restore(reader);
        else if (hadRumble)
            throw new ReloadFormatException("a rumble mixer was carried and this build has none");
    }

    public void Init(bool noMouse)
    {
        _input.InitAll(noMouse);
        _input.RawInputOnetimeReset();
    }

    public void Shutdown() => _input.ShutdownAll();

    public void LevelInit() => _input.LevelInit();

    public void CreateMove(int sequenceNumber, float sampleTime, bool active, ref DecodedUserCmd cmd)
    {
        InputCommand c = default;
        c.CommandNumber = cmd.Cmd.CommandNumber;
        c.TickCount = cmd.Cmd.TickCount;

        _input.CreateMove(sequenceNumber, sampleTime, active, ref c);

        cmd.Cmd.Pitch = c.ViewAngles.Pitch;
        cmd.Cmd.Yaw = c.ViewAngles.Yaw;
        cmd.Cmd.Roll = c.ViewAngles.Roll;
        cmd.Cmd.ForwardMove = c.ForwardMove;
        cmd.Cmd.SideMove = c.SideMove;
        cmd.Cmd.UpMove = c.UpMove;
        cmd.Cmd.Buttons = c.Buttons;
        cmd.Cmd.Impulse = c.Impulse;
        cmd.Cmd.WeaponSelect = c.WeaponSelect;
        cmd.Cmd.MouseDx = c.MouseDx;
        cmd.Cmd.MouseDy = c.MouseDy;
        cmd.WeaponSubtype = c.WeaponSubtype;
        cmd.RandomSeed = c.RandomSeed;

        _recent[_recentNext] = new RecentCommand(c.CommandNumber, c.TickCount, c.ViewAngles.Pitch, c.ViewAngles.Yaw,
                                                 c.ViewAngles.Roll, c.ForwardMove, c.SideMove, c.UpMove, c.Buttons,
                                                 c.Impulse, c.WeaponSelect, c.MouseDx, c.MouseDy, c.RandomSeed);
        _recentNext = (_recentNext + 1) % RecentCount;
        _recentCount = Math.Min(_recentCount + 1, RecentCount);

        _afterCommand?.Invoke(cmd.Cmd, sampleTime, active);
    }

    public void ExtraMouseSample(float frameTime, bool active) => _input.ExtraMouseSample(frameTime, active);

    public int GetButtonBits(bool reset) => _input.GetButtonBits(reset);

    public void MakeWeaponSelection(int weaponEntIndex) => _input.MakeWeaponSelection(weaponEntIndex);

    public float KeyState(int button)
    {
        IReadOnlyList<KeyButton> all = _input.Buttons.All;
        return button >= 0 && button < all.Count ? _input.KeyState(all[button]) : 0.0f;
    }

    public int KeyEvent(int down, int keyCode, string? binding) => _input.KeyEvent(down, keyCode, binding);

    public bool IsKeyDown(string name, out bool down) => _input.TryIsKeyDown(name, out down);

    public void JoystickAdvanced() => _input.JoystickAdvanced();

    public void AccumulateMouse() => _input.AccumulateMouse();

    public void ActivateMouse() => _input.ActivateMouse();

    public void DeactivateMouse() => _input.DeactivateMouse();

    public void ClearStates() => _input.ClearStates();

    public float GetLookSpring() => _input.GetLookSpring();

    public void GetFullscreenMousePos(Span<int> inOut)
    {
        int mx = inOut[0], my = inOut[1], ux = inOut[2], uy = inOut[3];
        _input.GetFullscreenMousePos(ref mx, ref my, ref ux, ref uy);
        inOut[0] = mx;
        inOut[1] = my;
        inOut[2] = ux;
        inOut[3] = uy;
    }

    public void SetFullscreenMousePos(int x, int y) => _input.SetFullscreenMousePos(x, y);

    public void ResetMouse() => _input.ResetMouse();

    public float GetLastForwardMove() => _input.GetLastForwardMove();

    public void JoystickAxes(Span<float> output)
    {
        output[0] = _input.JoystickForward;
        output[1] = _input.JoystickSide;
        output[2] = _input.JoystickPitch;
        output[3] = _input.JoystickYaw;
    }

    public void ClearInputButton(int bits) => _input.ClearInputButton(bits);

    public void CamThink() => _input.CamThink();

    public int CamOp(int op)
    {
        switch (op)
        {
            case InputCamOps.IsThirdPerson: return _input.CamIsThirdPerson() ? 1 : 0;
            case InputCamOps.ToThirdPerson: _input.CamToThirdPerson(); return 0;
            case InputCamOps.ToFirstPerson: _input.CamToFirstPerson(); return 0;
            case InputCamOps.StartMouseMove: _input.CamStartMouseMove(); return 0;
            case InputCamOps.EndMouseMove: _input.CamEndMouseMove(); return 0;
            case InputCamOps.StartDistance: _input.CamStartDistance(); return 0;
            case InputCamOps.EndDistance: _input.CamEndDistance(); return 0;
            case InputCamOps.InterceptingMouse: return _input.CamInterceptingMouse() ? 1 : 0;
            case InputCamOps.ToOrthographic: _input.CamToOrthographic(); return 0;
            case InputCamOps.IsOrthographic: return _input.CamIsOrthographic() ? 1 : 0;
            case InputCamOps.CameraThirdThink: _input.CamCameraThirdThink(); return 0;
            default: return 0;
        }
    }

    public void CamOrthographicSize(out float width, out float height) => _input.CamOrthographicSize(out width, out height);

    public void CamSetCameraThirdData(ReadOnlySpan<float> data, ReadOnlySpan<float> offset)
    {
        CameraThirdData? third = data.Length >= 4 ? new CameraThirdData(data[0], data[1], data[2], data[3]) : null;
        InputAngles angles = offset.Length >= 3 ? new InputAngles(offset[0], offset[1], offset[2]) : default;
        _input.CamSetCameraThirdData(third, angles);
    }

    public void SetPreferredGameActionSet(int actionSet) => _input.SetPreferredGameActionSet(actionSet);

    public int GetPreferredGameActionSet() => _input.PreferredGameActionSet;

    public void SetGameActionSetFlags(int flags) => _input.SetGameActionSetFlags(flags);

    public bool EnableJoystickMode() => _input.EnableJoystickMode();

    public bool IsSteamControllerActive() => _input.IsSteamControllerActive();

    public void ThirdPersonInit() => _input.ThirdPersonCamera.Init();

    public void RumbleScreenShake(float shake, float balance) => _rumble?.ScreenShakeFromView(shake, _localPlayerAlive());

    public void RumbleStopAll() => _rumble?.StopAllRumble();

    /// <summary>The Rumble user message's effect (RumbleEffect), at the client's current time.</summary>
    public void RumbleEffect(int effect, int data, int flags) => _rumble?.StartEffect(effect, data, flags, _curTime());

    /// <summary>
    /// UpdateRumbleEffects, once a client frame: stock's caller was
    /// ClientModeShared::Update, which this build does not call, and its
    /// local-player check answered NULL (an arm we bypassed, ported).
    /// </summary>
    public void RumbleFrame() => _rumble?.Frame(_curTime(), _paused(), _localPlayerAlive());
}

/// <summary>The UI's menu layer as the input's gate (plan_client_ui U3's UiSetMenuInput).</summary>
public sealed class UiMenuInputGate(Func<Vgui.IUiRuntime?> ui) : IMenuInputGate
{
    public bool MenuHasInput => ui() is { MenuInput: true };
}
