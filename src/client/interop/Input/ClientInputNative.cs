using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

//=============================================================================
// THE MANAGED INPUT'S NATIVE SIDE (plan_client_novalve §2 cluster 13): the
// engine's own vtables where the engine answers (IVEngineClient's view angles,
// screen size, pause, mouse delta and command buffer; IInputSystem's devices),
// libsteam_api's flat API for the Steam Controller (the input system's own
// ISteamController, so the pad is the one the engine initialised), and
// IShimInputClient for what only the native client knows (vgui's cursor, the
// HUD, the view's pitch drift, prediction's view angles, the viewport).
//=============================================================================

/// <summary><see cref="IInputHost"/> in the game.</summary>
public sealed unsafe class ClientInputHost : IInputHost
{
    // The side's input area (plan_shim_areas_split phase 11): the cursor,
    // the HUD's facts and the view-angle calls below are its slots.
    private readonly InputClientArea _input;
    private readonly IEngineConsole _console;

    // The engine client, handed in: null where there is none (the server, a
    // side with no engine), and then every engine-client answer is its default.
    private readonly IEngineClientInput? _engineClient;

    internal ClientInputHost(InputClientArea input, IEngineConsole console, IEngineClientInput? engineClient)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _engineClient = engineClient;
    }

    /// <summary>
    /// The client's input host, over the engine of <paramref name="engine"/>:
    /// its input area and its engine client, taken here once.
    /// </summary>
    public static IInputHost For(ModuleEngine engine, IEngineConsole console)
        => new ClientInputHost(engine.Api.Areas.Require<InputClientArea>(), console,
                               EngineClient.Acquire(engine, out _) as IEngineClientInput);

    public InputAngles GetViewAngles() => _engineClient?.GetViewAngles() ?? default;

    public void SetViewAngles(InputAngles angles) => _engineClient?.SetViewAngles(angles);

    public void GetScreenSize(out int width, out int height)
    {
        width = height = 0;
        _engineClient?.GetScreenSize(out width, out height);
    }

    public bool IsPaused => _engineClient?.IsPaused ?? false;

    public void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta)
    {
        x = y = 0;
        _engineClient?.GetMouseDelta(out x, out y, ignoreNextMouseDelta);
    }

    public void ClientCmdUnrestricted(string command) => _engineClient?.ClientCmdUnrestricted(command);

    public string? EngineConVar(string name) => _console.FindConVarValue(name);

    public void Msg(string text) => _console.Msg(text);

    public void ConsolePrintAt(int line, string text) => _engineClient?.ConsolePrintAt(line, text);

    public bool CursorVisible
    {
        get
        {
            int visible = 0, locked = 0;
            _input.InputCursorState(&visible, &locked);
            return visible != 0;
        }
    }

    public bool CursorLocked
    {
        get
        {
            int visible = 0, locked = 0;
            _input.InputCursorState(&visible, &locked);
            return locked != 0;
        }
    }

    public void GetCursorPos(out int x, out int y)
    {
        int cx = 0, cy = 0;
        _input.InputGetCursorPos(&cx, &cy);
        x = cx;
        y = cy;
    }

    public void SetCursorPos(int x, int y) => _input.InputSetCursorPos(x, y);

    public void SetCursorArrow() => _input.InputSetCursorArrow();

    public void GetVguiScreenSize(out int width, out int height)
    {
        int w = 0, h = 0;
        _input.InputVguiScreenSize(&w, &h);
        width = w;
        height = h;
    }

    public InputHudFacts Hud
    {
        get
        {
            float sensitivity = 0, fov = 1;
            int keyBits = 0;
            _input.InputHudFacts(&sensitivity, &keyBits, &fov);
            return new InputHudFacts(sensitivity, keyBits, fov);
        }
    }

    public void StopPitchDrift() => _input.InputStopPitchDrift();

    public void SetLocalViewAngles(InputAngles angles)
    {
        float* a = stackalloc float[3] { angles.Pitch, angles.Yaw, angles.Roll };
        _input.InputSetLocalViewAngles(a);
    }

    public void ViewerCameraModeChanged() => _input.InputViewerCameraModeChanged();

    public bool ViewerAimIsAssisted => _input.InputViewerAimIsAssisted() != 0;

    public void ShowScoreboard(bool show) => _input.InputShowScoreboard(show ? 1 : 0);
}

/// <summary><see cref="IInputDevices"/> in the game: the engine's IInputSystem.</summary>
public sealed unsafe class ClientInputDevices : IInputDevices
{
    /// <summary>INPUTSYSTEM_INTERFACE_VERSION.</summary>
    public const string InterfaceVersion = "InputSystemVersion001";

    private const int InvalidUserId = -1;

    private readonly NativeInterfaceFactory _interfaces;
    private readonly SteamControllerFlat _flat;
    private void* _self;
    private InputSystemFwd* _fwd;
    private bool _asked;

    internal ClientInputDevices(ModuleEngine engine, ILog log)
    {
        _interfaces = engine.Api.Interfaces;
        _flat = new SteamControllerFlat(log);
    }

    /// <summary>The client's input system, from the engine's app-system factory.</summary>
    public static IInputDevices For(ModuleEngine engine, ILog log) => new ClientInputDevices(engine, log);

    private bool Bound()
    {
        if (!_asked)
        {
            _asked = true;
            if (Forwarders.TryAcquire<InputSystemFwd>(_interfaces, SourceSharpFactoryType.AppSystem,
                                                   InterfaceVersion, out void* self, out InputSystemFwd* table, out _))
            {
                _self = self;
                _fwd = table;
            }
        }
        return _fwd != null;
    }

    public bool GetRawMouseAccumulators(out int x, out int y)
    {
        int ax = 0, ay = 0;
        bool ok = Bound() && _fwd->GetRawMouseAccumulators(_self, &ax, &ay) != 0;
        x = ax;
        y = ay;
        return ok;
    }

    public int GetAnalogValue(int analogCode) => Bound() ? _fwd->GetAnalogValue(_self, analogCode) : 0;

    public int JoystickCount => Bound() ? _fwd->GetJoystickCount(_self) : 0;

    public bool IsButtonDown(int buttonCode) => Bound() && _fwd->IsButtonDown(_self, buttonCode) != 0;

    public ISteamControllerApi? SteamController
    {
        get
        {
            if (!Bound())
                return null;
            nint controller = (nint)_fwd->SteamControllerInterface(_self);
            return controller == 0 ? null : _flat.Over(controller);
        }
    }

    public bool IsSteamControllerActive => Bound() && _fwd->IsSteamControllerActive(_self) != 0;

    public void ActivateSteamControllerActionSet(int actionSet)
    {
        // IInputSystem::ActivateSteamControllerActionSet, the header's inline
        // helper: every slot.
        if (Bound())
            _fwd->ActivateSteamControllerActionSetForSlot(_self, 0xffffffffffffffff, actionSet);
    }

    public void SetRumble(float left, float right)
    {
        if (Bound())
            _fwd->SetRumble(_self, left, right, InvalidUserId);
    }

    public void StopRumble()
    {
        if (Bound())
            _fwd->StopRumble(_self);
    }
}

/// <summary>
/// The Steam Controller through libsteam_api.so's flat C API, over the
/// ISteamController the input system holds (SteamController008, the version
/// this SDK's header and the shipped library both are). The library is the
/// engine's copy or none (<see cref="LoadedLibrary"/>), as SteamUserTickets binds it.
/// </summary>
public sealed unsafe class SteamControllerFlat(ILog log)
{
    public static string Library => LoadedLibrary.SteamApi;


    /// <summary>ControllerDigitalActionData_t, pack(1): bState, bActive.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DigitalData
    {
        public byte State;
        public byte Active;
    }

    /// <summary>ControllerAnalogActionData_t, pack(1): eMode, x, y, bActive (13 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AnalogData
    {
        public int Mode;
        public float X;
        public float Y;
        public byte Active;
    }

    private bool _asked;
    private bool _bound;
    private delegate* unmanaged<nint, ulong*, int> _connected;
    private delegate* unmanaged<nint, byte*, ulong> _digitalHandle;
    private delegate* unmanaged<nint, ulong, ulong, DigitalData> _digitalData;
    private delegate* unmanaged<nint, byte*, ulong> _analogHandle;
    private delegate* unmanaged<nint, ulong, ulong, AnalogData> _analogData;
    private delegate* unmanaged<nint, ulong, byte> _bindingPanel;

    private bool Bind()
    {
        if (_asked)
            return _bound;
        _asked = true;

        IntPtr library = LoadedLibrary.Open(Library);
        if (library == IntPtr.Zero)
        {
            log.Msg("[SourceSharp/Client] input: " + Library + " is not loaded (" + LoadedLibrary.NoLoad + "): no Steam Controller\n");
            return false;
        }

        if (!NativeLibrary.TryGetExport(library, "SteamAPI_ISteamController_GetConnectedControllers", out IntPtr connected) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_ISteamController_GetDigitalActionHandle", out IntPtr digitalHandle) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_ISteamController_GetDigitalActionData", out IntPtr digitalData) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_ISteamController_GetAnalogActionHandle", out IntPtr analogHandle) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_ISteamController_GetAnalogActionData", out IntPtr analogData) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_ISteamController_ShowBindingPanel", out IntPtr bindingPanel))
        {
            log.Msg("[SourceSharp/Client] input: " + Library + " does not export the ISteamController flat API\n");
            return false;
        }

        _connected = (delegate* unmanaged<nint, ulong*, int>)connected;
        _digitalHandle = (delegate* unmanaged<nint, byte*, ulong>)digitalHandle;
        _digitalData = (delegate* unmanaged<nint, ulong, ulong, DigitalData>)digitalData;
        _analogHandle = (delegate* unmanaged<nint, byte*, ulong>)analogHandle;
        _analogData = (delegate* unmanaged<nint, ulong, ulong, AnalogData>)analogData;
        _bindingPanel = (delegate* unmanaged<nint, ulong, byte>)bindingPanel;
        _bound = true;
        return true;
    }

    /// <summary>The API over one ISteamController, or null with no library.</summary>
    public ISteamControllerApi? Over(nint controller) => Bind() ? new Pad(this, controller) : null;

    private sealed class Pad(SteamControllerFlat api, nint self) : ISteamControllerApi
    {
        public int GetConnectedControllers(Span<ulong> handles)
        {
            // STEAM_CONTROLLER_MAX_COUNT is 16: the API writes up to that many.
            Span<ulong> all = stackalloc ulong[16];
            int n;
            fixed (ulong* p = all)
                n = api._connected(self, p);
            all[..Math.Min(Math.Max(n, 0), Math.Min(16, handles.Length))].CopyTo(handles);
            return n;
        }

        public ulong GetDigitalActionHandle(string action)
        {
            byte[] bytes = Vtable.Utf8(action);
            fixed (byte* p = bytes)
                return api._digitalHandle(self, p);
        }

        public ControllerDigital GetDigitalActionData(ulong controller, ulong action)
        {
            DigitalData d = api._digitalData(self, controller, action);
            return new ControllerDigital(d.State != 0, d.Active != 0);
        }

        public ulong GetAnalogActionHandle(string action)
        {
            byte[] bytes = Vtable.Utf8(action);
            fixed (byte* p = bytes)
                return api._analogHandle(self, p);
        }

        public ControllerAnalog GetAnalogActionData(ulong controller, ulong action)
        {
            AnalogData d = api._analogData(self, controller, action);
            return new ControllerAnalog(d.Mode, d.X, d.Y, d.Active != 0);
        }

        public bool ShowBindingPanel(ulong controller) => api._bindingPanel(self, controller) != 0;
    }
}
