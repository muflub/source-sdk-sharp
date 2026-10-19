using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Client.Input;
using Debug.Api.Client.Interfaces;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientInput"/> over the client's input service (lane
/// c6-input, plan_client_novalve §4.4's <c>input</c> and <c>input/inject</c>).
/// The inject is a write: it answers only with sv_cheats on, and says so
/// otherwise.
/// </summary>
public sealed class ClientDebugInput(ClientInputService service, InputCvars cvars, IInputHost host, Func<string?> cheats)
    : IDebugClientInput
{
    public DebugClientInput Read()
    {
        ClientInput input = service.Input;
        InputSnapshot s = input.Snapshot();
        var buttons = input.Buttons.All.Select(b => new DebugClientInputButton(b.Name, b.State, b.Down0, b.Down1)).ToArray();
        var sensitivity = new DebugClientInputSensitivity(
            cvars.Sensitivity.Float, host.Hud.Sensitivity, cvars.CustomAccel.Int, cvars.CustomAccelScale.Float,
            cvars.CustomAccelExponent.Float, cvars.CustomAccelMax.Float, cvars.MFilter.Bool, cvars.MYaw.Float,
            input.EffectivePitch(), cvars.RawInput.Bool);
        var axes = input.JoystickMapping().Select((m, i) => new DebugClientInputAxis(i, m.AxisMap, m.Relative)).ToArray();
        var actions = input.ControllerMapping().Select(a => new DebugClientInputAction(a.Action, a.Command, a.Handle, a.State)).ToArray();
        var commands = service.Recent().Select(c => new DebugClientInputCommand(
            c.CommandNumber, c.TickCount, c.Pitch, c.Yaw, c.Roll, c.ForwardMove, c.SideMove, c.UpMove,
            c.Buttons, c.Impulse, c.WeaponSelect, c.MouseDx, c.MouseDy)).ToArray();

        return new DebugClientInput(buttons, s.MouseActive, s.AccumulatedMouseX, s.AccumulatedMouseY,
                                    s.PreviousMouseX, s.PreviousMouseY, sensitivity, s.ThirdPerson, s.Orthographic,
                                    input.EnableJoystickMode(), axes, input.PreferredGameActionSet, actions, commands,
                                    input.InjectPending ? 1 : 0, input.InjectedCount,
                                    input.LastInjectRead.X, input.LastInjectRead.Y,
                                    input.LastInjectRead.DeviceX, input.LastInjectRead.DeviceY);
    }

    public DebugClientInputInject Inject(string argument)
    {
        if (cheats() is not "1")
            return new DebugClientInputInject(false, "a write: needs sv_cheats 1", 0, 0, null);

        string text = (argument ?? string.Empty).Trim();
        if (text.StartsWith('+'))
        {
            string name = text[1..];
            return service.Input.InjectKey(name)
                ? new DebugClientInputInject(true, "pressed for the next command", 0, 0, name)
                : new DebugClientInputInject(false, $"no button '{name}'", 0, 0, name);
        }

        string[] parts = text.Split(',');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dx)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dy))
        {
            service.Input.InjectMouse(dx, dy);
            return new DebugClientInputInject(true, "added to the next read of the mouse", dx, dy, null);
        }

        return new DebugClientInputInject(false, "expected \"dx,dy\" or \"+button\"", 0, 0, null);
    }
}
