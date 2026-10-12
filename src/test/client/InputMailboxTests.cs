using System.Runtime.InteropServices;
using SourceSharp.Client.Input;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The input mailbox (shell_input.cpp's SourceSharpInputMailbox): its layout,
/// and that a call native skips on managed's published answer is a call that
/// would have changed nothing. Each fact runs the same operations twice, once
/// calling everything (the per-call path) and once through the block's gates
/// (the batched path), and sets the two against each other after every step.
/// </summary>
public class InputMailboxTests
{
    [Fact]
    public void TheBlockIsLaidOutAsShellInputStatesIt()
    {
        // shell_input.cpp's COMPILE_TIME_ASSERTs, the same numbers.
        Assert.Equal(32, Marshal.SizeOf<InputMailboxBlock>());
        Assert.Equal(0, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.StructSize)));
        Assert.Equal(4, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.KeyboardSampleSeq)));
        Assert.Equal(8, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.KeyboardSampleTime)));
        Assert.Equal(12, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.JoystickSampleSeq)));
        Assert.Equal(16, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.JoystickSampleTime)));
        Assert.Equal(20, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.Published)));
        Assert.Equal(24, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.ShakeSkip)));
        Assert.Equal(28, (int)Marshal.OffsetOf<InputMailboxBlock>(nameof(InputMailboxBlock.ShakeBits)));
    }

    [Fact]
    public unsafe void ABlockOfAnotherSizeIsRefused()
    {
        InputMailboxBlock block = default;
        block.StructSize = 28;
        nint address = (nint)(&block);
        Assert.Throws<InvalidOperationException>(() => new InputMailbox(address));
    }

    /// <summary>A recording host: every cursor call the mouse code makes, in order.</summary>
    private sealed class CursorLog : IInputHost
    {
        public readonly List<string> Calls = [];
        public InputAngles View;
        public InputAngles GetViewAngles() => View;
        public void SetViewAngles(InputAngles angles) { View = angles; Calls.Add("view"); }
        public void GetScreenSize(out int width, out int height) { width = 1280; height = 720; }
        public bool IsPaused => false;
        public void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta) { x = y = 0; Calls.Add("delta " + ignoreNextMouseDelta); }
        public void ClientCmdUnrestricted(string command) => Calls.Add("cmd " + command);
        public string? EngineConVar(string name) => name == "sv_cheats" ? "1" : null;
        public void Msg(string text) { }
        public void ConsolePrintAt(int line, string text) { }
        public bool CursorVisible => false;
        public bool CursorLocked => true;
        public void GetCursorPos(out int x, out int y) { x = 640; y = 360; }
        public void SetCursorPos(int x, int y) => Calls.Add($"cursor {x} {y}");
        public void SetCursorArrow() => Calls.Add("arrow");
        public void GetVguiScreenSize(out int width, out int height) { width = 1280; height = 720; }
        public InputHudFacts Hud => new(1, 0, 1);
        public void StopPitchDrift() { }
        public void SetLocalViewAngles(InputAngles angles) { }
        public void ViewerCameraModeChanged() => Calls.Add("camera mode");
        public bool ViewerAimIsAssisted => false;
        public void ShowScoreboard(bool show) { }
    }

    [Fact]
    public void AMouseOrCameraCallTheBlockSkipsChangesNothing()
    {
        var rng = new Random(0x5EED);
        var perCallHost = new CursorLog();
        var batchedHost = new CursorLog();
        var perCall = new ClientInput(perCallHost, new FakeInputDevices(), new InputCvars());
        using var block = new NativeMailboxBlock();
        var batched = new ClientInput(batchedHost, new FakeInputDevices(), new InputCvars(), mailbox: block.Mailbox);

        int skipped = 0, made = 0, answered = 0;
        for (int step = 0; step < 2000; step++)
        {
            int op = rng.Next(8);
            switch (op)
            {
                case 0:
                    perCall.ActivateMouse();
                    if (block.ActivateSkipped) skipped++; else { made++; batched.ActivateMouse(); }
                    break;
                case 1:
                    perCall.DeactivateMouse();
                    if (block.DeactivateSkipped) skipped++; else { made++; batched.DeactivateMouse(); }
                    break;
                case 2:
                {
                    bool noMouse = rng.Next(4) == 0;
                    perCall.InitAll(noMouse);
                    batched.InitAll(noMouse);
                    break;
                }
                case 3:
                    perCall.CamToThirdPerson();
                    batched.CamToThirdPerson();
                    break;
                case 4:
                    perCall.CamToFirstPerson();
                    batched.CamToFirstPerson();
                    break;
                case 5:
                    perCall.CamToOrthographic();
                    batched.CamToOrthographic();
                    break;
                case 6:
                    perCall.ExtraMouseSample(0.01f, true);
                    batched.ExtraMouseSample(0.01f, true);
                    break;
                case 7:
                    perCall.ClearStates();
                    batched.ClearStates();
                    break;
            }

            Assert.True(perCallHost.Calls.SequenceEqual(batchedHost.Calls), $"step {step} (op {op}): the hosts were called differently");
            Assert.Equal(perCall.Snapshot(), batched.Snapshot());
            Assert.Equal(perCall.MouseActive, batched.MouseActive);
            bool? third = block.ThirdPerson;
            Assert.NotNull(third);
            answered++;
            Assert.Equal(perCall.CamIsThirdPerson(), third!.Value);
        }

        // Both arms ran: the block skipped calls, and let others through.
        Assert.True(skipped >= 200, $"the block skipped {skipped} calls");
        Assert.True(made >= 150, $"the block let {made} calls through");
        Assert.True(answered == 2000, $"native answered CAM_IsThirdPerson {answered} times");
    }

    private sealed class SeededRandom(int seed) : SourceSharp.Abi.IUniformRandom
    {
        private readonly Random _rng = new(seed);
        public float RandomFloat(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
        public int RandomInt(int min, int max) => _rng.Next(min, max + 1);
        public float RandomFloatExp(float min, float max, float exponent) => RandomFloat(min, max);
    }

    [Fact]
    public void AScreenShakeTheBlockSkipsChangesNothing()
    {
        var rng = new Random(0xC0FFEE);
        var perCallMotors = new FakeInputDevices();
        var batchedMotors = new FakeInputDevices();
        var perCall = new ClientRumble(perCallMotors, new InputCvars(), new SeededRandom(7));
        using var block = new NativeMailboxBlock();
        var batched = new ClientRumble(batchedMotors, new InputCvars(), new SeededRandom(7), mailbox: block.Mailbox);

        float[] shakes = [0.0f, -0.0f, 0.25f, 0.25f, -0.5f, 1.0f, float.NaN];
        int skipped = 0, made = 0;
        float time = 0.0f;
        for (int step = 0; step < 3000; step++)
        {
            int op = rng.Next(10);
            bool alive = rng.Next(5) != 0;
            switch (op)
            {
                case <= 5:
                {
                    float shake = shakes[rng.Next(shakes.Length)];
                    perCall.ScreenShakeFromView(shake, alive);
                    if (block.ShakeSkipped(shake)) skipped++; else { made++; batched.ScreenShakeFromView(shake, alive); }
                    break;
                }
                case 6:
                {
                    bool enable = rng.Next(3) != 0;
                    perCall.SetOutputEnabled(enable);
                    batched.SetOutputEnabled(enable);
                    break;
                }
                case 7:
                    perCall.StopAllRumble();
                    batched.StopAllRumble();
                    break;
                case 8:
                {
                    int effect = rng.Next(1, 20);
                    perCall.StartEffect(effect, 0, 0, time);
                    batched.StartEffect(effect, 0, 0, time);
                    break;
                }
                default:
                    time += 0.1f;
                    perCall.Frame(time, false, alive);
                    batched.Frame(time, false, alive);
                    break;
            }

            Assert.Equal(BitConverter.SingleToUInt32Bits(perCall.ScreenShake), BitConverter.SingleToUInt32Bits(batched.ScreenShake));
            Assert.Equal(perCall.OutputEnabled, batched.OutputEnabled);
            Assert.True(perCallMotors.Rumble.SequenceEqual(batchedMotors.Rumble), $"step {step} (op {op}): the motors were driven differently");
        }

        Assert.True(skipped >= 300, $"the block skipped {skipped} shakes");
        Assert.True(made >= 300, $"the block let {made} shakes through");
        Assert.True(perCallMotors.Rumble.Count >= 100, $"the motors were driven {perCallMotors.Rumble.Count} times");
    }
}
