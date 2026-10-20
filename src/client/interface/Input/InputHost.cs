using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>
/// Three floats in stock's angle order (pitch, yaw, roll), mutable the way the
/// input code edits a <c>QAngle</c> in place.
/// </summary>
public struct InputAngles(float pitch, float yaw, float roll)
{
    public float Pitch = pitch, Yaw = yaw, Roll = roll;
}

/// <summary>What the HUD knows that input reads: CHud's zoom sensitivity, key bits and FOV adjust.</summary>
public readonly record struct InputHudFacts(float Sensitivity, int KeyBits, float FovSensitivityAdjust);

/// <summary>
/// The engine and the native shell, as the input code asks them. In the game:
/// the engine's own vtable (view angles, screen size, pause, the command
/// buffer) and IShimInputClient's slots (vgui's cursor, the HUD's facts, the
/// view's pitch drift, prediction's view angles, the viewport). In a test: a
/// fake that answers what a recording says.
/// </summary>
public interface IInputHost
{
    InputAngles GetViewAngles();
    void SetViewAngles(InputAngles angles);
    void GetScreenSize(out int width, out int height);
    bool IsPaused { get; }

    /// <summary>The engine's SDL mouse delta (<c>IVEngineClient::GetMouseDelta</c>): the source when m_rawinput is 0.</summary>
    void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta);

    /// <summary><c>ClientCmd_Unrestricted</c>: the Steam Controller's actions, the controller cfg files.</summary>
    void ClientCmdUnrestricted(string command);

    /// <summary>A convar input does not own, by name (sv_cheats, joy_xcontroller_found), or null.</summary>
    string? EngineConVar(string name);

    /// <summary>A console line (Msg).</summary>
    void Msg(string text);

    /// <summary><c>Con_NPrintf</c>: cam_showangles' overlay.</summary>
    void ConsolePrintAt(int line, string text);

    bool CursorVisible { get; }
    bool CursorLocked { get; }
    void GetCursorPos(out int x, out int y);
    void SetCursorPos(int x, int y);

    /// <summary>The cursor back to an arrow (DeactivateMouse).</summary>
    void SetCursorArrow();

    /// <summary>vgui's screen size (GetFullscreenMousePos and ScreenWidth/ScreenHeight).</summary>
    void GetVguiScreenSize(out int width, out int height);

    InputHudFacts Hud { get; }

    /// <summary><c>view-&gt;StopPitchDrift()</c>.</summary>
    void StopPitchDrift();

    /// <summary><c>prediction-&gt;SetLocalViewAngles</c>, after the client mode accepted the command.</summary>
    void SetLocalViewAngles(InputAngles angles);

    /// <summary>The viewer's model comes into (or leaves) the leaf system: stock's ThirdPersonSwitch.</summary>
    void ViewerCameraModeChanged();

    /// <summary>The joystick's autoaim dampening ask (view_facts.h).</summary>
    bool ViewerAimIsAssisted { get; }

    /// <summary>+score / -score: the viewport's scoreboard (and, on release, the voice squelch mode ends).</summary>
    void ShowScoreboard(bool show);
}

/// <summary>A Steam Controller digital action's state (<c>ControllerDigitalActionData_t</c>).</summary>
public readonly record struct ControllerDigital(bool State, bool Active);

/// <summary>An analog action's state (<c>ControllerAnalogActionData_t</c>).</summary>
public readonly record struct ControllerAnalog(int Mode, float X, float Y, bool Active);

/// <summary>
/// The Steam Controller, as in_steamcontroller.cpp asks it: in the game,
/// libsteam_api's flat API over the input system's own ISteamController.
/// </summary>
public interface ISteamControllerApi
{
    /// <summary>The connected pads' handles, at most 16; the count.</summary>
    int GetConnectedControllers(Span<ulong> handles);
    ulong GetDigitalActionHandle(string action);
    ControllerDigital GetDigitalActionData(ulong controller, ulong action);
    ulong GetAnalogActionHandle(string action);
    ControllerAnalog GetAnalogActionData(ulong controller, ulong action);
    bool ShowBindingPanel(ulong controller);
}

/// <summary>
/// The input system (<c>IInputSystem</c>), as the input code reads it: the
/// raw mouse, the joystick, the Steam Controller and the rumble motors.
/// </summary>
public interface IInputDevices
{
    bool GetRawMouseAccumulators(out int x, out int y);
    int GetAnalogValue(int analogCode);
    int JoystickCount { get; }
    bool IsButtonDown(int buttonCode);

    /// <summary>The input system's Steam Controller, or null when it has none.</summary>
    ISteamControllerApi? SteamController { get; }

    bool IsSteamControllerActive { get; }
    void ActivateSteamControllerActionSet(int actionSet);
    void SetRumble(float left, float right);
    void StopRumble();
}

/// <summary>
/// The engine client's calls the input host makes (<c>IVEngineClient</c>, through
/// its forwarder table): the view angles, the screen, pause, the SDL mouse delta,
/// the unrestricted command buffer and the overlay line. A seam of its own, the
/// minimum the input host needs, so the host is handed the engine client rather
/// than binding its table again (plan_shim_areas_split phase 11 review).
/// </summary>
public interface IEngineClientInput
{
    InputAngles GetViewAngles();
    void SetViewAngles(InputAngles angles);
    void GetScreenSize(out int width, out int height);
    bool IsPaused { get; }

    /// <summary>The SDL mouse delta; zero where the engine is built without SDL and has no slot.</summary>
    void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta);

    /// <summary><c>ClientCmd_Unrestricted</c>, for <see cref="IInputHost.ClientCmdUnrestricted"/>.</summary>
    void ClientCmdUnrestricted(string command);

    /// <summary><c>Con_NPrintf</c>.</summary>
    void ConsolePrintAt(int position, string text);
}
