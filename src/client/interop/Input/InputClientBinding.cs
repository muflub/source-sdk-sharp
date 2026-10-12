using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Input;

/// <summary>
/// The client's input as the native IInput thunk set asks it (plan_client_novalve
/// §2 cluster 13): one member per IManagedInputClient slot, in managed types.
/// </summary>
public interface IClientInputService
{
    void Init(bool noMouse);
    void Shutdown();
    void LevelInit();
    void CreateMove(int sequenceNumber, float sampleTime, bool active, ref DecodedUserCmd cmd);
    void ExtraMouseSample(float frameTime, bool active);
    int GetButtonBits(bool reset);
    void MakeWeaponSelection(int weaponEntIndex);
    float KeyState(int button);
    int KeyEvent(int down, int keyCode, string? binding);
    bool IsKeyDown(string name, out bool down);
    void JoystickAdvanced();
    void AccumulateMouse();
    void ActivateMouse();
    void DeactivateMouse();
    void ClearStates();
    float GetLookSpring();
    void GetFullscreenMousePos(Span<int> inOut);
    void SetFullscreenMousePos(int x, int y);
    void ResetMouse();
    float GetLastForwardMove();
    void JoystickAxes(Span<float> output);
    void ClearInputButton(int bits);
    void CamThink();
    int CamOp(int op);
    void CamOrthographicSize(out float width, out float height);
    void CamSetCameraThirdData(ReadOnlySpan<float> data, ReadOnlySpan<float> offset);
    void SetPreferredGameActionSet(int actionSet);
    int GetPreferredGameActionSet();
    void SetGameActionSetFlags(int flags);
    bool EnableJoystickMode();
    bool IsSteamControllerActive();
    void ThirdPersonInit();
    void RumbleScreenShake(float shake, float balance);
    void RumbleStopAll();
}

/// <summary>The camera operations IInput's CAM_* virtuals map to (InputCamOp's argument; shell_input.cpp's twin).</summary>
public static class InputCamOps
{
    public const int IsThirdPerson = 0;
    public const int ToThirdPerson = 1;
    public const int ToFirstPerson = 2;
    public const int StartMouseMove = 3;
    public const int EndMouseMove = 4;
    public const int StartDistance = 5;
    public const int EndDistance = 6;
    public const int InterceptingMouse = 7;
    public const int ToOrthographic = 8;
    public const int IsOrthographic = 9;
    public const int CameraThirdThink = 10;
}

/// <summary>
/// <see cref="IManagedInputClient"/>, managed-built: each body counts its
/// crossing and hands typed arguments to the service. A failure answers the
/// generated thunk's 0.
/// </summary>
public sealed unsafe class InputClientBinding(Crossings crossings, IClientInputService input) : IManagedInputClient
{
    private static string? Text(byte* p) => p == null ? null : new string((sbyte*)p);

    public void InputInit(int a0) { crossings.Count(CrossingOp.ClientInputInit); input.Init(a0 != 0); }

    public void InputShutdown() { crossings.Count(CrossingOp.ClientInputShutdown); input.Shutdown(); }

    public void InputLevelInit() { crossings.Count(CrossingOp.ClientInputLevelInit); input.LevelInit(); }

    // THE USERCMD CODEC (plan_client_novalve C9): stateless, so the binding
    // asks UserCmdCodec directly. One crossing per command, each a whole
    // BitBlock native copies into or out of the engine's buffer.
    public int InputEncodeUserCmd(void* a0, void* a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientInputEncodeUserCmd);
        if (a0 == null || a1 == null || a2 == null || a3 < BitBlock.HeaderBytes)
            return -1;
        return UserCmdCodec.EncodeToBlock(*(DecodedUserCmd*)a0, *(DecodedUserCmd*)a1, new Span<byte>(a2, a3));
    }

    public int InputDecodeUserCmd(void* a0, int a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientInputDecodeUserCmd);
        if (a0 == null || a2 == null || a1 < BitBlock.HeaderBytes)
            return -1;
        int bits = UserCmdCodec.DecodeFromBlock(new ReadOnlySpan<byte>(a0, a1), out DecodedUserCmd cmd);
        if (bits >= 0)
            *(DecodedUserCmd*)a2 = cmd;
        return bits;
    }

    public void InputCreateMove(int a0, float a1, int a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientInputCreateMove);
        if (a3 == null)
            return;
        input.CreateMove(a0, a1, a2 != 0, ref *(DecodedUserCmd*)a3);
    }

    public void InputExtraMouseSample(float a0, int a1)
    {
        crossings.Count(CrossingOp.ClientInputExtraMouseSample);
        input.ExtraMouseSample(a0, a1 != 0);
    }

    public int InputGetButtonBits(int a0) { crossings.Count(CrossingOp.ClientInputGetButtonBits); return input.GetButtonBits(a0 != 0); }

    public void InputMakeWeaponSelection(int a0) { crossings.Count(CrossingOp.ClientInputMakeWeaponSelection); input.MakeWeaponSelection(a0); }

    public float InputKeyState(int a0) { crossings.Count(CrossingOp.ClientInputKeyState); return input.KeyState(a0); }

    public int InputKeyEvent(int a0, int a1, byte* a2)
    {
        crossings.Count(CrossingOp.ClientInputKeyEvent);
        return input.KeyEvent(a0, a1, Text(a2));
    }

    public int InputIsKeyDown(byte* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientInputIsKeyDown);
        string? name = Text(a0);
        if (name is null || !input.IsKeyDown(name, out bool down))
            return 0;
        if (a1 != null)
            *(int*)a1 = down ? 1 : 0;
        return 1;
    }

    public void InputJoystickAdvanced() { crossings.Count(CrossingOp.ClientInputJoystickAdvanced); input.JoystickAdvanced(); }

    public void InputAccumulateMouse() { crossings.Count(CrossingOp.ClientInputAccumulateMouse); input.AccumulateMouse(); }

    public void InputActivateMouse() { crossings.Count(CrossingOp.ClientInputActivateMouse); input.ActivateMouse(); }

    public void InputDeactivateMouse() { crossings.Count(CrossingOp.ClientInputDeactivateMouse); input.DeactivateMouse(); }

    public void InputClearStates() { crossings.Count(CrossingOp.ClientInputClearStates); input.ClearStates(); }

    public float InputGetLookSpring() { crossings.Count(CrossingOp.ClientInputGetLookSpring); return input.GetLookSpring(); }

    public void InputGetFullscreenMousePos(void* a0)
    {
        crossings.Count(CrossingOp.ClientInputGetFullscreenMousePos);
        if (a0 != null)
            input.GetFullscreenMousePos(new Span<int>(a0, 4));
    }

    public void InputSetFullscreenMousePos(int a0, int a1) { crossings.Count(CrossingOp.ClientInputSetFullscreenMousePos); input.SetFullscreenMousePos(a0, a1); }

    public void InputResetMouse() { crossings.Count(CrossingOp.ClientInputResetMouse); input.ResetMouse(); }

    public float InputGetLastForwardMove() { crossings.Count(CrossingOp.ClientInputGetLastForwardMove); return input.GetLastForwardMove(); }

    public void InputJoystickAxes(void* a0)
    {
        crossings.Count(CrossingOp.ClientInputJoystickAxes);
        if (a0 != null)
            input.JoystickAxes(new Span<float>(a0, 4));
    }

    public void InputClearInputButton(int a0) { crossings.Count(CrossingOp.ClientInputClearInputButton); input.ClearInputButton(a0); }

    public void InputCamThink() { crossings.Count(CrossingOp.ClientInputCamThink); input.CamThink(); }

    public int InputCamOp(int a0) { crossings.Count(CrossingOp.ClientInputCamOp); return input.CamOp(a0); }

    public void InputCamOrthographicSize(void* a0)
    {
        crossings.Count(CrossingOp.ClientInputCamOrthographicSize);
        input.CamOrthographicSize(out float w, out float h);
        if (a0 != null)
        {
            ((float*)a0)[0] = w;
            ((float*)a0)[1] = h;
        }
    }

    public void InputCamSetCameraThirdData(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientInputCamSetCameraThirdData);
        input.CamSetCameraThirdData(a0 == null ? ReadOnlySpan<float>.Empty : new ReadOnlySpan<float>(a0, 4),
                                    a1 == null ? ReadOnlySpan<float>.Empty : new ReadOnlySpan<float>(a1, 3));
    }

    public void InputSetPreferredGameActionSet(int a0) { crossings.Count(CrossingOp.ClientInputSetPreferredGameActionSet); input.SetPreferredGameActionSet(a0); }

    public int InputGetPreferredGameActionSet() { crossings.Count(CrossingOp.ClientInputGetPreferredGameActionSet); return input.GetPreferredGameActionSet(); }

    public void InputSetGameActionSetFlags(int a0) { crossings.Count(CrossingOp.ClientInputSetGameActionSetFlags); input.SetGameActionSetFlags(a0); }

    public int InputEnableJoystickMode() { crossings.Count(CrossingOp.ClientInputEnableJoystickMode); return input.EnableJoystickMode() ? 1 : 0; }

    public int InputIsSteamControllerActive() { crossings.Count(CrossingOp.ClientInputIsSteamControllerActive); return input.IsSteamControllerActive() ? 1 : 0; }

    public void InputThirdPersonInit() { crossings.Count(CrossingOp.ClientInputThirdPersonInit); input.ThirdPersonInit(); }

    public void InputRumbleScreenShake(float a0, float a1) { crossings.Count(CrossingOp.ClientInputRumbleScreenShake); input.RumbleScreenShake(a0, a1); }

    public void InputRumbleStopAll() { crossings.Count(CrossingOp.ClientInputRumbleStopAll); input.RumbleStopAll(); }
}
