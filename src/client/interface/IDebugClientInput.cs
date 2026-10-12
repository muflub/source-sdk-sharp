namespace SourceSharp.Client.Interface;

/// <summary>One key button: its name (the command after the +/-), its state bits and the two keys holding it.</summary>
public sealed record DebugClientInputButton(string Name, int State, int Down0, int Down1);

/// <summary>One user command as CreateMove built it.</summary>
public sealed record DebugClientInputCommand(
    int Command, int Tick, float Pitch, float Yaw, float Roll, float Forward, float Side, float Up,
    int Buttons, int Impulse, int WeaponSelect, int MouseDx, int MouseDy);

/// <summary>
/// The mouse's sensitivity chain as the next command will apply it: the HUD's
/// zoom sensitivity when non-zero, else <c>sensitivity</c>; m_customaccel and
/// its three parameters; m_filter; m_yaw and the m_pitch sv_cheats leaves.
/// </summary>
public sealed record DebugClientInputSensitivity(
    float Sensitivity, float HudSensitivity, int CustomAccel, float AccelScale, float AccelExponent, float AccelMax,
    bool Filter, float Yaw, float Pitch, bool RawInput);

/// <summary>One joystick axis' mapping: the game axis it drives (0 none, 1 forward, 2 look, 3 side, 4 turn) and whether relative.</summary>
public sealed record DebugClientInputAxis(int Axis, int Map, bool Relative);

/// <summary>One Steam Controller digital action: its command, the pad's handle and the last state.</summary>
public sealed record DebugClientInputAction(string Action, string Command, ulong Handle, bool State);

/// <summary>
/// The client's input, as <c>/client/{slot}/input</c> reports it (plan_client_novalve
/// §4.4): key states, the mouse's accumulators, the sensitivity chain, the last
/// 16 commands, and the controller mapping in force.
/// </summary>
public sealed record DebugClientInput(
    IReadOnlyList<DebugClientInputButton> Buttons, bool MouseActive, float AccumulatedX, float AccumulatedY,
    float PreviousX, float PreviousY, DebugClientInputSensitivity Sensitivity, bool ThirdPerson, bool Orthographic,
    bool Joystick, IReadOnlyList<DebugClientInputAxis> Axes, int ActionSet, IReadOnlyList<DebugClientInputAction> Actions,
    IReadOnlyList<DebugClientInputCommand> Commands, int InjectPending, int Injected,
    int LastInjectX, int LastInjectY, int LastInjectDeviceX, int LastInjectDeviceY);

/// <summary>The answer to <c>input/inject</c>: whether the delta or key press was queued, and why not.</summary>
public sealed record DebugClientInputInject(bool Accepted, string Reason, int Dx, int Dy, string? Key);

/// <summary>The client's input (lane c6-input). Game thread.</summary>
public interface IDebugClientInput
{
    DebugClientInput Read();

    /// <summary>
    /// A WRITE, cheats only: "dx,dy" adds a raw mouse delta to the next
    /// command's; "+name" presses the button for one command.
    /// </summary>
    DebugClientInputInject Inject(string argument);
}
