//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

// in_steamcontroller.cpp.
public sealed partial class ClientInput
{
    /// <summary>GAME_ACTION_SET_MENUCONTROLS.</summary>
    public const int ActionSetMenuControls = 0;

    /// <summary>GAME_ACTION_SET_FLAGS_TAUNTING.</summary>
    public const int ActionSetFlagTaunting = 1;

    private const int ActionFlagsNone = 0;
    private const int ActionFlagStopsTaunt = 1 << 0;
    private const int ActionFlagNeedsDebounce = 1 << 1;

    /// <summary>Action names to commands: mostly identity, but a command may hold spaces an action name cannot.</summary>
    // An instance table, not a static: a mutable static is a global (SS0001).
    private readonly (string Action, string Command, int Flags)[] DigitalGameActions =
    [
        ("duck", "+duck", ActionFlagsNone),
        ("attack", "+attack", ActionFlagsNone),
        ("attack2", "+attack2", ActionFlagsNone),
        ("attack3", "+attack3", ActionFlagsNone),
        ("jump", "+jump", ActionFlagStopsTaunt),
        ("use_action_slot_item", "+use_action_slot_item", ActionFlagsNone),
        ("invprev", "invprev", ActionFlagStopsTaunt | ActionFlagNeedsDebounce),
        ("invnext", "invnext", ActionFlagStopsTaunt | ActionFlagNeedsDebounce),
        ("reload", "+reload", ActionFlagsNone),
        ("dropitem", "dropitem", ActionFlagStopsTaunt | ActionFlagNeedsDebounce),
        ("changeclass", "changeclass", ActionFlagNeedsDebounce),
        ("changeteam", "changeteam", ActionFlagNeedsDebounce),
        ("open_charinfo_direct", "open_charinfo_direct", ActionFlagNeedsDebounce),
        ("open_charinfo_backpack", "open_charinfo_backpack", ActionFlagNeedsDebounce),
        ("inspect", "+inspect", ActionFlagNeedsDebounce),
        ("taunt", "+taunt", ActionFlagNeedsDebounce),
        ("voicerecord", "+voicerecord", ActionFlagsNone),
        ("show_quest_log", "show_quest_log", ActionFlagNeedsDebounce),
        ("showscores", "+showscores", ActionFlagsNone),
        ("callvote", "callvote", ActionFlagsNone),
        ("cl_trigger_first_notification", "cl_trigger_first_notification", ActionFlagNeedsDebounce),
        ("cl_decline_first_notification", "cl_decline_first_notification", ActionFlagNeedsDebounce),
        ("cl_trigger_first_notification", "vote option1", ActionFlagNeedsDebounce),   // twice: one action, both commands
        ("cl_decline_first_notification", "vote option2", ActionFlagNeedsDebounce),
        ("vote_option3", "vote option3", ActionFlagNeedsDebounce),
        ("vote_option4", "vote option4", ActionFlagNeedsDebounce),
        ("vote_option5", "vote option5", ActionFlagNeedsDebounce),
        ("next_target", "spec_next", ActionFlagNeedsDebounce),           // the spectator set only
        ("prev_target", "spec_prev", ActionFlagNeedsDebounce),
        ("voice_medic", "voicemenu 0 0", ActionFlagNeedsDebounce),
        ("voice_thanks", "voicemenu 0 1", ActionFlagNeedsDebounce),
        ("voice_gogogo", "voicemenu 0 2", ActionFlagNeedsDebounce),
        ("voice_moveup", "voicemenu 0 3", ActionFlagNeedsDebounce),
        ("voice_spy", "voicemenu 1 1", ActionFlagNeedsDebounce),
        ("voice_uberready", "voicemenu 1 7", ActionFlagNeedsDebounce),
        ("voice_help", "voicemenu 2 0", ActionFlagNeedsDebounce),
    ];

    private struct DigitalActionState
    {
        public ulong Handle;
        public bool State;
        public bool AwaitingDebounce;
    }

    private readonly DigitalActionState[] _digitalActions;
    private ulong _moveHandle;
    private ulong _cameraHandle;
    private int _preferredGameActionSet = ActionSetMenuControls;
    private int _gameActionSetFlags;
    private bool _steamControllerGameActionsInitialized;
    private bool _steamControllerSeenInput;

    /// <summary>The action table as the route shows it: action, command, handle, state.</summary>
    public IEnumerable<(string Action, string Command, ulong Handle, bool State)> ControllerMapping()
    {
        for (int i = 0; i < DigitalGameActions.Length; i++)
            yield return (DigitalGameActions[i].Action, DigitalGameActions[i].Command, _digitalActions[i].Handle, _digitalActions[i].State);
    }

    public int PreferredGameActionSet => _preferredGameActionSet;

    /// <summary>CInput::InitializeSteamControllerGameActionSets: the action handles, from the pad.</summary>
    public bool InitializeSteamControllerGameActionSets()
    {
        ISteamControllerApi? controller = _devices.SteamController;
        if (controller is null)
            return false;

        for (int i = 0; i < DigitalGameActions.Length; ++i)
        {
            _digitalActions[i].Handle = controller.GetDigitalActionHandle(DigitalGameActions[i].Action);
            _digitalActions[i].State = false;
            _digitalActions[i].AwaitingDebounce = false;
        }

        _moveHandle = controller.GetAnalogActionHandle("Move");
        _cameraHandle = controller.GetAnalogActionHandle("Camera");

        _preferredGameActionSet = ActionSetMenuControls;
        return true;
    }

    /// <summary>CInput::ApplySteamControllerCameraMove: the camera pad's delta into the view.</summary>
    private void ApplySteamControllerCameraMove(ref InputAngles viewAngles, ref InputCommand cmd, float positionX, float positionY)
    {
        // Stock reads these through ConVarRefs by name; they are this module's.
        float pitchDown = _cvars.PitchDown.Float;
        float pitchUp = _cvars.PitchUp.Float;

        float yaw = CamCapYaw(_cvars.ScYawSensitivity.Float * positionX);
        float pitch = CamCapPitch(_cvars.ScPitchSensitivity.Float * positionY);

        if (CamIsThirdPerson())
        {
            if (positionX != 0)
            {
                Vector3f offset = _thirdPerson.CameraOffset;
                // The orbit, and the ideal angle it implies; stock's `auto` copy
                // is never written back.
                offset.Y -= yaw;
                _cvars.CamIdealYaw.Write(offset.Y - viewAngles.Yaw);
                viewAngles.Yaw -= yaw;
            }
        }
        else
        {
            viewAngles.Yaw -= yaw;
        }

        if (CamIsThirdPerson() && _cvars.ThirdPersonPlatformer.Int != 0)
        {
            if (positionY != 0)
            {
                Vector3f offset = _thirdPerson.CameraOffset;
                offset.X += pitch;
                _cvars.CamIdealPitch.Write(offset.X - viewAngles.Pitch);
            }
        }
        else
        {
            viewAngles.Pitch -= pitch;

            // Check the pitch bounds (stock's own order: up is the minimum's negation).
            viewAngles.Pitch = MathX.Clamp(viewAngles.Pitch, -pitchDown, pitchUp);
        }

        cmd.MouseDx = (short)(int)positionX;
        cmd.MouseDy = (short)(int)positionY;
    }

    /// <summary>CInput::SteamControllerMove: the pad's part of a command.</summary>
    private void SteamControllerMove(float frameTime, ref InputCommand cmd)
    {
        ISteamControllerApi? controller = _devices.SteamController;
        if (controller is null)
            return;

        // Is a pad connected? Asked before the handles, so a machine with none
        // does not thrash the handle queries.
        Span<ulong> handles = stackalloc ulong[16];
        int count = controller.GetConnectedControllers(handles);
        if (count <= 0)
            return;

        if (!_steamControllerGameActionsInitialized)
        {
            _steamControllerGameActionsInitialized = InitializeSteamControllerGameActionSets();
            if (!_steamControllerGameActionsInitialized)
                return;
        }

        _devices.ActivateSteamControllerActionSet(_preferredGameActionSet);

        InputAngles viewAngles = _host.GetViewAngles();

        _host.StopPitchDrift();

        bool taunting = (_gameActionSetFlags & ActionSetFlagTaunting) != 0;

        ulong pad = handles[0];
        for (int i = 0; i < _digitalActions.Length; ++i)
        {
            ref DigitalActionState state = ref _digitalActions[i];
            ControllerDigital data = controller.GetDigitalActionData(pad, state.Handle);

            if (!_steamControllerSeenInput && data.State)
            {
                _host.Msg("Seen controller input\n");
                _steamControllerSeenInput = true;
            }

            if (data.Active)
            {
                state.AwaitingDebounce = state.AwaitingDebounce && data.State;

                if (data.State != state.State)
                {
                    string command = DigitalGameActions[i].Command;
                    if ((data.State && !state.AwaitingDebounce) || command[0] == '+')
                    {
                        string line = data.State ? command : "-" + command[1..];
                        _host.ClientCmdUnrestricted(line);

                        // Taunting: some inputs also stop the taunt.
                        if (taunting && data.State && (DigitalGameActions[i].Flags & ActionFlagStopsTaunt) != 0)
                            _host.ClientCmdUnrestricted("stop_taunt");
                    }

                    state.State = data.State;
                }
            }
        }

        ControllerAnalog moveData = controller.GetAnalogActionData(pad, _moveHandle);

        // At most one unit long: a hacked pad's corners are caught here.
        float moveX = moveData.X, moveY = moveData.Y;
        if (moveX * moveX + moveY * moveY > 1.0)
        {
            float length = MathF.Sqrt(moveX * moveX + moveY * moveY);
            if (length != 0.0f)
            {
                float inverse = 1.0f / length;
                moveX *= inverse;
                moveY *= inverse;
            }
            else
            {
                moveX = moveY = 0.0f;
            }
        }

        if (moveY > 0.0)
            cmd.ForwardMove += _cvars.ForwardSpeed.Float * moveY;
        else
            cmd.ForwardMove += _cvars.BackSpeed.Float * moveY;

        cmd.SideMove += _cvars.SideSpeed.Float * moveX;

        ControllerAnalog action = controller.GetAnalogActionData(pad, _cameraHandle);

        float sensitivity = _cvars.ScLookSensitivityScale.Float;
        float deltaX = action.X * sensitivity;
        float deltaY = -action.Y * sensitivity;

        if (MathF.Sqrt(deltaX * deltaX + deltaY * deltaY) > 0)
        {
            if (!_cameraInterceptingMouse)
                ApplySteamControllerCameraMove(ref viewAngles, ref cmd, deltaX, deltaY);
        }

        _host.SetViewAngles(viewAngles);
    }

    /// <summary>CInput::SetPreferredGameActionSet: a change debounces the actions that need it.</summary>
    public void SetPreferredGameActionSet(int actionSet)
    {
        if (_preferredGameActionSet != actionSet)
        {
            for (int i = 0; i < _digitalActions.Length; i++)
            {
                if ((DigitalGameActions[i].Flags & ActionFlagNeedsDebounce) != 0)
                    _digitalActions[i].AwaitingDebounce = true;
            }

            _preferredGameActionSet = actionSet;
            _devices.ActivateSteamControllerActionSet(_preferredGameActionSet);
        }
    }

    /// <summary>CInput::SetGameActionSetFlags.</summary>
    public void SetGameActionSetFlags(int flags) => _gameActionSetFlags = flags;

    /// <summary>CInput::IsSteamControllerActive: initialised, the input system says active, and input seen.</summary>
    public bool IsSteamControllerActive()
    {
        bool active = _steamControllerGameActionsInitialized && _devices.IsSteamControllerActive;
        if (!active)
            return false;

        if (_steamControllerSeenInput)
            return true;

        // No input seen yet: is any pad button down now?
        for (int button = ButtonCodes.SteamControllerFirst; button <= ButtonCodes.SteamControllerLast; button++)
        {
            if (_devices.IsButtonDown(button))
            {
                _steamControllerSeenInput = true;
                break;
            }
        }

        return _steamControllerSeenInput;
    }

    /// <summary><c>sc_show_binding_panel</c>.</summary>
    public void ShowBindingPanel()
    {
        ISteamControllerApi? controller = _devices.SteamController;
        if (controller is null)
        {
            _host.Msg("Steam Controller interface not initialized.\n");
            return;
        }

        Span<ulong> handles = stackalloc ulong[16];
        int connected = controller.GetConnectedControllers(handles);
        if (connected > 0)
        {
            _host.Msg($"{connected} controller(s) connected. Launching binding panel for first connected controller.\n");
            if (!controller.ShowBindingPanel(handles[0]))
                _host.Msg("Unable to show binding panel. Steam overlay disabled, or Steam not in Big Picture mode.\n");
        }
        else
        {
            _host.Msg("No Steam Controllers connected.\n");
        }
    }

    /// <summary><c>sc_status</c>.</summary>
    public void SteamControllerStatus()
    {
        ISteamControllerApi? controller = _devices.SteamController;
        if (controller is null)
        {
            _host.Msg("Steam Controller interface not initialized.\n");
            return;
        }

        Span<ulong> handles = stackalloc ulong[16];
        int connected = controller.GetConnectedControllers(handles);
        _host.Msg(connected != 0 ? $"{connected} Steam Controller(s) connected.\n" : "No Steam Controllers(s) connected.\n");

        _host.Msg(IsSteamControllerActive()
            ? "Steam Controller considered active (controller connected and all action handles initialized).\n"
            : "Steam Controller considered inactive (no controller connected, no input observed, not all action handles initialized, or sc_disable set).\n");

        _host.Msg($"Current action set = {_preferredGameActionSet}\n");

        for (int i = 0; i < _digitalActions.Length; ++i)
            _host.Msg($"Action: '{DigitalGameActions[i].Action}' handle = {(int)_digitalActions[i].Handle}\n");
    }
}
