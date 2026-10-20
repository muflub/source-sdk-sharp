using SourceSharp.Client.Input;

namespace SourceSharp.Tests;

/// <summary>A scriptable engine and shell for the managed input's facts.</summary>
internal sealed class FakeInputHost : IInputHost
{
    public InputAngles View;
    public bool Visible { get; set; }
    public bool Locked { get; set; } = true;
    public InputHudFacts HudFacts = new(0, 0, 1);
    public readonly Dictionary<string, string> Engine = new(StringComparer.OrdinalIgnoreCase) { ["sv_cheats"] = "0" };
    public readonly List<string> Commands = [];
    public readonly List<bool> Scoreboard = [];
    public int CameraModeChanges;

    public InputAngles GetViewAngles() => View;
    public void SetViewAngles(InputAngles angles) => View = angles;
    public void GetScreenSize(out int width, out int height) { width = 1280; height = 720; }
    public bool IsPaused { get; set; }
    public void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta) { x = y = 0; }
    public void ClientCmdUnrestricted(string command) => Commands.Add(command);
    public string? EngineConVar(string name) => Engine.TryGetValue(name, out string? v) ? v : null;
    public void Msg(string text) { }
    public void ConsolePrintAt(int line, string text) { }
    public bool CursorVisible => Visible;
    public bool CursorLocked => Locked;
    public void GetCursorPos(out int x, out int y) { x = 640; y = 360; }
    public void SetCursorPos(int x, int y) { }
    public void SetCursorArrow() { }
    public void GetVguiScreenSize(out int width, out int height) { width = 1280; height = 720; }
    public InputHudFacts Hud => HudFacts;
    public void StopPitchDrift() { }
    public void SetLocalViewAngles(InputAngles angles) { }
    public void ViewerCameraModeChanged() => CameraModeChanges++;
    public bool ViewerAimIsAssisted => false;
    public void ShowScoreboard(bool show) => Scoreboard.Add(show);
}

/// <summary>An input system with a raw mouse delta waiting to be read, and nothing else attached.</summary>
internal sealed class FakeInputDevices : IInputDevices
{
    public int RawX, RawY;
    public int RawReads;
    public readonly List<(float L, float R)> Rumble = [];

    public bool GetRawMouseAccumulators(out int x, out int y)
    {
        RawReads++;
        x = RawX;
        y = RawY;
        RawX = RawY = 0;
        return true;
    }

    public int GetAnalogValue(int analogCode) => 0;
    public int JoystickCount => 0;
    public bool IsButtonDown(int buttonCode) => false;
    public ISteamControllerApi? SteamController => null;
    public bool IsSteamControllerActive => false;
    public void ActivateSteamControllerActionSet(int actionSet) { }
    public void SetRumble(float left, float right) => Rumble.Add((left, right));
    public void StopRumble() { }
}

internal sealed class FakeMenuGate : IMenuInputGate
{
    public bool MenuHasInput { get; set; }
}

/// <summary>One managed input over the fakes, initialised and with the mouse captured, as in game.</summary>
internal sealed class InputRig
{
    public readonly FakeInputHost Host = new();
    public readonly FakeInputDevices Devices = new();
    public readonly FakeMenuGate Menu = new();
    public readonly InputCvars Cvars = new();
    public readonly ClientInput Input;
    public readonly Dictionary<string, Action<string?>> Commands;

    public InputRig(bool init = true)
    {
        Input = new ClientInput(Host, Devices, Cvars, Menu);
        Commands = InputCommands.For(Input, Host, Cvars).ToDictionary(c => c.Name, c => c.Run, StringComparer.OrdinalIgnoreCase);
        if (init)
        {
            Input.InitAll(noMouse: false);
            Input.ActivateMouse();
        }
    }

    public void Run(string line)
    {
        int space = line.IndexOf(' ');
        Commands[space < 0 ? line : line[..space]](space < 0 ? null : line[(space + 1)..]);
    }

    public InputCommand Move(int sequence = 1, float frameTime = 0.015f)
    {
        Input.SetJoystickSampleTime(frameTime);
        Input.SetKeyboardSampleTime(frameTime);
        InputCommand cmd = default;
        Input.CreateMove(sequence, frameTime, true, ref cmd);
        return cmd;
    }
}

/// <summary>
/// A mailbox block in native memory, written and read as shell_input.cpp does:
/// the sample-time stores bump their sequence, and the gates skip a call only
/// on what managed published (CSourceSharpInput::Published, ShakeIsNoop).
/// </summary>
internal sealed unsafe class NativeMailboxBlock : IDisposable
{
    private InputMailboxBlock* _block;

    public NativeMailboxBlock()
    {
        _block = (InputMailboxBlock*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)sizeof(InputMailboxBlock));
        _block->StructSize = (uint)sizeof(InputMailboxBlock);
        Mailbox = InputMailbox.Over((nint)_block);
    }

    public InputMailbox Mailbox { get; }

    public uint Published => _block->Published;

    /// <summary>IN_SetSampleTime.</summary>
    public void StoreKeyboardSampleTime(float t)
    {
        _block->KeyboardSampleTime = t;
        _block->KeyboardSampleSeq++;
    }

    /// <summary>Joystick_SetSampleTime.</summary>
    public void StoreJoystickSampleTime(float t)
    {
        _block->JoystickSampleTime = t;
        _block->JoystickSampleSeq++;
    }

    private bool Bit(InputPublished bit)
        => (_block->Published & (uint)InputPublished.Valid) != 0 && (_block->Published & (uint)bit) != 0;

    public bool ActivateSkipped => Bit(InputPublished.ActivateNoop);
    public bool DeactivateSkipped => Bit(InputPublished.DeactivateNoop);

    /// <summary>CAM_IsThirdPerson's answer; null when native would still ask managed.</summary>
    public bool? ThirdPerson => (_block->Published & (uint)InputPublished.Valid) != 0 ? Bit(InputPublished.ThirdPerson) : null;

    public bool ShakeSkipped(float shake)
    {
        if (_block->ShakeSkip == (uint)ShakeSkip.Always)
            return true;
        if (_block->ShakeSkip != (uint)ShakeSkip.WhenEqual)
            return false;
        return BitConverter.SingleToUInt32Bits(shake) == _block->ShakeBits;
    }

    public void Dispose()
    {
        System.Runtime.InteropServices.NativeMemory.Free(_block);
        _block = null;
    }
}
