using System.Globalization;
using System.IO.Compression;

using SourceSharp.Client.Input;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed input (<see cref="ClientInput"/>) against stock's in_main /
/// in_mouse / in_joystick / in_steamcontroller / in_camera, recorded live
/// (<c>Fixtures/input-stock.tsv.gz</c>, README beside it): 420 user commands,
/// 373 extra mouse samples and 373 camera thinks over keys, every mouse
/// acceleration mode, every joystick curve, a Steam Controller and third
/// person. Every device read the stock code made is served back from the tape
/// IN ORDER, and every float it wrote is compared BIT FOR BIT.
/// </summary>
public class InputStockOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static List<string[]> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "input-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded stock input is missing: " + path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<string[]>();
        while (reader.ReadLine() is string line)
        {
            if (line.Length > 0 && line[0] != '#')
                rows.Add(line.Split('\t'));
        }
        Assert.True(rows.Count(r => r[0] == "C") >= 400, "the oracle parsed: 420 commands");
        return rows;
    }

    //-------------------------------------------------------------------------
    // The tape: every device read, in the order stock made it.
    //-------------------------------------------------------------------------

    private sealed class Tape
    {
        private readonly Queue<string[]> _reads = new();
        public bool Recording { get; set; }

        public void Load(IEnumerable<string[]> reads)
        {
            _reads.Clear();
            foreach (string[] r in reads)
                _reads.Enqueue(r);
        }

        public int Left => _reads.Count;

        public string[] Next(string tag, string what)
        {
            if (_reads.Count == 0)
                throw new InvalidOperationException($"managed read {what} ({tag}) where stock read nothing more");
            string[] r = _reads.Dequeue();
            if (r[0] != tag)
                throw new InvalidOperationException($"managed read {what} ({tag}) where stock read {string.Join(' ', r)}");
            return r;
        }
    }

    private sealed class TapedController(Tape tape) : ISteamControllerApi
    {
        public int GetConnectedControllers(Span<ulong> handles)
        {
            if (!tape.Recording)
                return 0;
            int n = I(tape.Next("SC", "GetConnectedControllers")[1]);
            if (n > 0)
                handles[0] = 77;
            return n;
        }

        public ulong GetDigitalActionHandle(string action)
        {
            if (!tape.Recording)
                return 0;
            string[] r = tape.Next("SDH", "GetDigitalActionHandle " + action);
            Assert.Equal(r[1], action);
            return (ulong)I(r[2]);
        }

        public ControllerDigital GetDigitalActionData(ulong controller, ulong action)
        {
            string[] r = tape.Next("SD", "GetDigitalActionData " + action);
            Assert.Equal(77ul, controller);
            Assert.Equal(I(r[1]), (int)action);
            return new ControllerDigital(r[2] == "1", r[3] == "1");
        }

        public ulong GetAnalogActionHandle(string action)
        {
            if (!tape.Recording)
                return 0;
            string[] r = tape.Next("SAH", "GetAnalogActionHandle " + action);
            Assert.Equal(r[1], action);
            return (ulong)I(r[2]);
        }

        public ControllerAnalog GetAnalogActionData(ulong controller, ulong action)
        {
            string[] r = tape.Next("SAD", "GetAnalogActionData " + action);
            Assert.Equal(I(r[1]), (int)action);
            return new ControllerAnalog(I(r[2]), F(r[3]), F(r[4]), r[5] == "1");
        }

        public bool ShowBindingPanel(ulong controller) => false;
    }

    private sealed class TapedDevices(Tape tape) : IInputDevices
    {
        private readonly TapedController _controller = new(tape);

        public bool GetRawMouseAccumulators(out int x, out int y)
        {
            if (!tape.Recording)
            {
                x = y = 0;
                return true;
            }
            string[] r = tape.Next("R", "GetRawMouseAccumulators");
            x = I(r[2]);
            y = I(r[3]);
            return r[1] == "1";
        }

        public int GetAnalogValue(int analogCode)
        {
            string[] r = tape.Next("A", "GetAnalogValue " + analogCode);
            Assert.Equal(I(r[1]), analogCode);
            return I(r[2]);
        }

        public int JoystickCount => tape.Recording ? I(tape.Next("N", "GetJoystickCount")[1]) : 0;

        public bool IsButtonDown(int buttonCode)
        {
            string[] r = tape.Next("B", "IsButtonDown " + buttonCode);
            Assert.Equal(I(r[1]), buttonCode);
            return r[2] == "1";
        }

        public ISteamControllerApi? SteamController
            => !tape.Recording ? _controller : tape.Next("SP", "SteamControllerInterface")[1] == "1" ? _controller : null;

        public bool IsSteamControllerActive => tape.Next("SI", "IsSteamControllerActive")[1] == "1";

        public void ActivateSteamControllerActionSet(int actionSet)
        {
            if (!tape.Recording)
                return;
            Assert.Equal(I(tape.Next("SA", "ActivateSteamControllerActionSet")[1]), actionSet);
        }

        public void SetRumble(float left, float right) { }
        public void StopRumble() { }
    }

    private sealed class ReplayHost : IInputHost
    {
        public InputAngles View;
        public bool Visible, Locked;
        public InputHudFacts HudFacts = new(0, 0, 1);
        public readonly Dictionary<string, string> Engine = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Commands = [];

        public InputAngles GetViewAngles() => View;
        // The engine normalises what it is given (the recording's yaw stays in
        // [-180, 180] while the input code's sums run past it).
        public void SetViewAngles(InputAngles angles)
            => View = new InputAngles(Normalize(angles.Pitch), Normalize(angles.Yaw), Normalize(angles.Roll));

        private static float Normalize(float angle)
        {
            angle %= 360.0f;
            if (angle > 180.0f)
                angle -= 360.0f;
            if (angle < -180.0f)
                angle += 360.0f;
            return angle;
        }
        public void GetScreenSize(out int width, out int height) { width = 1280; height = 720; }
        public bool IsPaused => false;
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
        public void ViewerCameraModeChanged() { }
        public bool ViewerAimIsAssisted => false;
        public void ShowScoreboard(bool show) { }
    }

    //-------------------------------------------------------------------------
    // The replay.
    //-------------------------------------------------------------------------

    private sealed class Replay
    {
        public readonly List<string> CommandMismatches = [];
        public readonly List<string> ExtraMismatches = [];
        public readonly List<string> CameraMismatches = [];
        public readonly List<string> ButtonMismatches = [];
        public readonly List<string> ControllerCommandMismatches = [];
        public int Commands, Extras, Thinks, ButtonRows, ControllerCommands, MovedCommands, ReadsServed;

        /// <summary>Every event's result, bit for bit, to set one replay against another.</summary>
        public readonly List<string> Trace = [];

        /// <summary>Through the mailbox: events where the published word was not the input's state.</summary>
        public readonly List<string> PublishMismatches = [];
        public int ThirdPersonAnswers;

        private readonly Tape _tape = new();
        private readonly ReplayHost _host = new();
        private readonly InputCvars _cvars = new();
        private readonly ClientInput _input;
        private readonly Dictionary<string, Action<string?>> _commands;

        private static readonly HashSet<string> DeviceTags = ["R", "A", "N", "B", "SP", "SI", "SA", "SC", "SDH", "SAH", "SD", "SAD"];

        private readonly NativeMailboxBlock? _block;

        /// <summary>
        /// <paramref name="block"/>: the sample times go through it as
        /// shell_input.cpp stores them, rather than through the setters.
        /// </summary>
        public Replay(Action<ClientInput>? mutate = null, NativeMailboxBlock? block = null)
        {
            _block = block;
            _input = new ClientInput(_host, new TapedDevices(_tape), _cvars, mailbox: block?.Mailbox);
            _commands = InputCommands.For(_input, _host, _cvars).ToDictionary(c => c.Name, c => c.Run, StringComparer.OrdinalIgnoreCase);
            mutate?.Invoke(_input);
        }

        private void SetVar(string name, string value)
        {
            if (_cvars.TryGet(name, out InputCvar cvar))
                cvar.Set(value);
            else
                _host.Engine[name] = value;
        }

        public void Run(List<string[]> rows)
        {
            // The game had been running before the probe armed: the mouse was
            // captured, the joystick's advanced init had run, and the Steam
            // Controller's handles came from the real pad (all 0). Bring a fresh
            // input to that state without the tape.
            int start = rows.FindIndex(r => r[0] != "V");
            foreach (string[] v in rows.Take(start))
                SetVar(v[1], v[2]);

            _input.InitAll(noMouse: false);
            _input.ActivateMouse();
            _input.ExtraMouseSample(0.0f, true);
            bool primed = false;
            _tape.Recording = true;

            for (int i = start; i < rows.Count; i++)
            {
                string[] r = rows[i];
                switch (r[0])
                {
                    case "V":
                        SetVar(r[1], r[2]);
                        break;

                    case "D":
                    {
                        // A +/- key command arrives as its K row (whatever sent it).
                        string line = r[1];
                        if (line[0] == '+' || line[0] == '-')
                            break;
                        int space = line.IndexOf(' ');
                        string name = space < 0 ? line : line[..space];
                        string? args = space < 0 ? null : line[(space + 1)..];
                        Assert.True(_commands.TryGetValue(name, out Action<string?>? run), "the input commands answer " + name);
                        run!(args);
                        break;
                    }

                    case "K":
                    {
                        if (r[3] == "<null>")
                            break;      // the joystick's own joyspeed: checked through KS
                        KeyButton? button = _input.Buttons.Find(r[1]);
                        Assert.NotNull(button);
                        string? arg = r[3].Length == 0 ? null : r[3];
                        if (r[2] == "d")
                            button!.KeyDown(arg);
                        else
                            button!.KeyUp(arg);
                        break;
                    }

                    case "S":
                        SampleTimes(F(r[1]), F(r[1]));
                        break;

                    case "SINIT":
                    {
                        int end = rows.FindIndex(i + 1, x => x[0] != "SP" && x[0] != "SDH" && x[0] != "SAH");
                        _tape.Load(rows.Skip(i + 1).Take(end - i - 1));
                        _input.InitializeSteamControllerGameActionSets();
                        Assert.True(_tape.Left == 0, "SINIT left reads unserved");
                        i = end - 1;
                        break;
                    }

                    case "C":
                    case "E":
                    case "T":
                    {
                        string close = r[0].ToLowerInvariant();
                        int end = rows.FindIndex(i + 1, x => x[0] == close);
                        Assert.True(end > i, "an event without its end row: " + string.Join(' ', r));
                        List<string[]> between = rows.Skip(i + 1).Take(end - i - 1).ToList();
                        _tape.Load(between.Where(x => DeviceTags.Contains(x[0])));
                        int reads = _tape.Left;
                        List<string> expectedQ = between.Where(x => x[0] == "Q").Select(x => x[1]).ToList();
                        _host.Commands.Clear();

                        if (!primed && r[0] != "T")
                        {
                            SampleTimes(F(r[15]), F(r[16]));
                            primed = true;
                        }

                        if (r[0] == "T")
                            RunThink(r, rows[end]);
                        else
                            RunMove(r, rows[end]);
                        CheckPublished(r);

                        if (_tape.Left != 0)
                            throw new InvalidOperationException($"{string.Join(' ', r)}: stock read {_tape.Left} more device values than managed");
                        ReadsServed += reads;

                        if (!expectedQ.SequenceEqual(_host.Commands))
                            ControllerCommandMismatches.Add($"{string.Join(' ', r.Take(3))}: stock sent [{string.Join(", ", expectedQ)}], managed [{string.Join(", ", _host.Commands)}]");
                        ControllerCommands += expectedQ.Count;
                        i = end;
                        break;
                    }

                    case "KS":
                        CompareButtons(r);
                        break;
                }
            }
        }

        // The shell sets the joystick's first, then the keyboard's.
        private void SampleTimes(float keyboard, float joystick)
        {
            if (_block is null)
            {
                _input.SetJoystickSampleTime(joystick);
                _input.SetKeyboardSampleTime(keyboard);
                return;
            }
            _block.StoreJoystickSampleTime(joystick);
            _block.StoreKeyboardSampleTime(keyboard);
        }

        // What native reads in the block is the input's state after the event.
        private void CheckPublished(string[] r)
        {
            if (_block is null)
                return;
            if (_block.Published != (uint)_input.Published())
                PublishMismatches.Add($"{string.Join(' ', r.Take(3))}: published {_block.Published:x}, state {(uint)_input.Published():x}");
            if (_block.ThirdPerson is bool third)
            {
                ThirdPersonAnswers++;
                if (third != _input.CamIsThirdPerson())
                    PublishMismatches.Add($"{string.Join(' ', r.Take(3))}: CAM_IsThirdPerson {third}, input {_input.CamIsThirdPerson()}");
            }
        }

        private void RunMove(string[] r, string[] o)
        {
            _host.View = new InputAngles(F(r[4]), F(r[5]), F(r[6]));
            _host.Visible = r[7] == "1";
            _host.Locked = r[8] == "1";
            _host.HudFacts = new InputHudFacts(F(r[11]), I(r[12]), F(r[13]));

            var problems = new List<string>();
            if ((r[9] == "1") != _input.MouseActive)
                problems.Add($"mouse active {_input.MouseActive} vs stock {r[9]}");

            InputCommand cmd = default;
            bool create = r[0] == "C";
            if (create)
                _input.CreateMove(I(r[1]), F(r[2]), r[3] == "1", ref cmd);
            else
                cmd = _input.ExtraMouseSample(F(r[2]), r[3] == "1");

            void Float(string what, float managed, string stock)
            {
                if (Bits(managed) != Bits(F(stock)))
                    problems.Add($"{what} {managed:R} vs stock {stock}");
            }

            void Int(string what, int managed, string stock)
            {
                if (managed != I(stock))
                    problems.Add($"{what} {managed} vs stock {stock}");
            }

            Float("cmd pitch", cmd.ViewAngles.Pitch, o[1]);
            Float("cmd yaw", cmd.ViewAngles.Yaw, o[2]);
            Float("cmd roll", cmd.ViewAngles.Roll, o[3]);
            Float("forwardmove", cmd.ForwardMove, o[4]);
            Float("sidemove", cmd.SideMove, o[5]);
            Float("upmove", cmd.UpMove, o[6]);
            Int("buttons", cmd.Buttons, o[7]);
            Int("impulse", cmd.Impulse, o[8]);
            Int("weaponselect", cmd.WeaponSelect, o[9]);
            Int("mousedx", cmd.MouseDx, o[10]);
            Int("mousedy", cmd.MouseDy, o[11]);
            if (create)
                Int("random_seed", cmd.RandomSeed, o[12]);
            Float("engine pitch", _host.View.Pitch, o[13]);
            Float("engine yaw", _host.View.Yaw, o[14]);
            Float("engine roll", _host.View.Roll, o[15]);

            InputSnapshot s = _input.Snapshot();
            Float("previous mouse x", s.PreviousMouseX, o[16]);
            Float("previous mouse y", s.PreviousMouseY, o[17]);
            Float("accumulated x", s.AccumulatedMouseX, o[18]);
            Float("accumulated y", s.AccumulatedMouseY, o[19]);
            Float("keyboard sample time", s.KeyboardSampleTime, o[20]);
            Float("joystick sample time", s.JoystickSampleTime, o[21]);
            Float("joystick forward", s.JoystickForward, o[22]);
            Float("joystick side", s.JoystickSide, o[23]);
            Float("joystick pitch", s.JoystickPitch, o[24]);
            Float("joystick yaw", s.JoystickYaw, o[25]);
            Float("envelope 0", s.Envelope0, o[26]);
            Float("envelope 1", s.Envelope1, o[27]);
            Float("previous view pitch", s.PreviousViewAngles.Pitch, o[28]);
            Float("previous view yaw", s.PreviousViewAngles.Yaw, o[29]);
            string pegged = $"{(s.Pegged0 ? 1 : 0)}{(s.Pegged1 ? 1 : 0)}{(s.PeggedDir0 ? 1 : 0)}{(s.PeggedDir1 ? 1 : 0)}";
            if (pegged != o[30])
                problems.Add($"pegged {pegged} vs stock {o[30]}");
            Float("last forward move", s.LastForwardMove, o[31]);
            Vector3f offset = _input.ThirdPersonCamera.CameraOffset;
            Float("camera offset pitch", offset.X, o[32]);
            Float("camera offset yaw", offset.Y, o[33]);
            Float("camera offset dist", offset.Z, o[34]);
            Float("cam_idealyaw", _cvars.CamIdealYaw.Float, o[35]);
            Float("cam_idealpitch", _cvars.CamIdealPitch.Float, o[36]);
            Int("third person", _input.CamIsThirdPerson() ? 1 : 0, o[37]);
            Int("pending impulse", s.Impulse, o[38]);
            Int("clear input state", s.ClearInputState, o[39]);

            Trace.Add($"{r[0]} {r[1]} {Bits(cmd.ViewAngles.Pitch):x} {Bits(cmd.ViewAngles.Yaw):x} {Bits(cmd.ViewAngles.Roll):x} " +
                      $"{Bits(cmd.ForwardMove):x} {Bits(cmd.SideMove):x} {Bits(cmd.UpMove):x} {cmd.Buttons} {cmd.Impulse} " +
                      $"{cmd.WeaponSelect} {cmd.MouseDx} {cmd.MouseDy} {cmd.RandomSeed} {Bits(_host.View.Pitch):x} " +
                      $"{Bits(_host.View.Yaw):x} {Bits(s.KeyboardSampleTime):x} {Bits(s.JoystickSampleTime):x} {s}");

            if (create)
            {
                Commands++;
                if (cmd.ForwardMove != 0 || cmd.SideMove != 0 || cmd.MouseDx != 0 || cmd.Buttons != 0)
                    MovedCommands++;
            }
            else
            {
                Extras++;
            }

            if (problems.Count > 0)
                (create ? CommandMismatches : ExtraMismatches).Add($"{r[0]} {r[1]}: {string.Join("; ", problems)}");
        }

        private void RunThink(string[] r, string[] o)
        {
            _host.View = new InputAngles(F(r[2]), F(r[3]), F(r[4]));
            _input.CamThink();
            Thinks++;
            Vector3f thought = _input.ThirdPersonCamera.CameraOffset;
            Trace.Add($"T {Bits(thought.X):x} {Bits(thought.Y):x} {Bits(thought.Z):x} {_input.CamIsThirdPerson()}");

            var problems = new List<string>();

            void Float(string what, float managed, string stock)
            {
                if (Bits(managed) != Bits(F(stock)))
                    problems.Add($"{what} {managed:R} vs stock {stock}");
            }

            Vector3f offset = _input.ThirdPersonCamera.CameraOffset;
            Float("offset pitch", offset.X, o[1]);
            Float("offset yaw", offset.Y, o[2]);
            Float("offset dist", offset.Z, o[3]);
            Float("cam_idealpitch", _cvars.CamIdealPitch.Float, o[4]);
            Float("cam_idealyaw", _cvars.CamIdealYaw.Float, o[5]);
            Float("cam_idealdist", _cvars.CamIdealDist.Float, o[6]);
            if ((_input.CamIsThirdPerson() ? "1" : "0") != o[7])
                problems.Add($"third person {_input.CamIsThirdPerson()} vs stock {o[7]}");
            if ((_input.CamInterceptingMouse() ? "1" : "0") != o[8])
                problems.Add($"intercepting {_input.CamInterceptingMouse()} vs stock {o[8]}");
            KeyButtons b = _input.Buttons;
            string cams = string.Join(' ', new[] { b.CamPitchUp, b.CamPitchDown, b.CamYawLeft, b.CamYawRight, b.CamIn, b.CamOut }.Select(x => x.State));
            if (cams != o[9])
                problems.Add($"camera buttons {cams} vs stock {o[9]}");

            if (problems.Count > 0)
                CameraMismatches.Add($"T {Thinks}: {string.Join("; ", problems)}");
        }

        private void CompareButtons(string[] r)
        {
            ButtonRows++;
            string managed = string.Join(' ', _input.Buttons.All.Take(KeyButtons.MainCount).Select(b => b.State));
            if (managed != r[1])
                ButtonMismatches.Add($"KS {ButtonRows}: {managed} vs stock {r[1]}");
            if (_input.Snapshot().Cancel != I(r[2]))
                ButtonMismatches.Add($"KS {ButtonRows}: in_cancel {_input.Snapshot().Cancel} vs stock {r[2]}");
        }
    }

    private static Replay Run(NativeMailboxBlock? block = null)
    {
        var replay = new Replay(block: block);
        replay.Run(Stock());
        return replay;
    }

    [Fact]
    public void TheReplayThroughTheMailboxBuildsEveryEventAsThePerCallReplay()
    {
        Replay perCall = Run();
        using var block = new NativeMailboxBlock();
        Replay batched = Run(block);

        Assert.True(perCall.Trace.Count >= 1100, $"the per-call replay traced {perCall.Trace.Count} events");
        Assert.Equal(perCall.Trace.Count, batched.Trace.Count);
        int first = Enumerable.Range(0, perCall.Trace.Count).FirstOrDefault(i => perCall.Trace[i] != batched.Trace[i], -1);
        Assert.True(first < 0, first < 0 ? "" : $"event {first}: per call\n{perCall.Trace[first]}\nthrough the mailbox\n{batched.Trace[first]}");
    }

    [Fact]
    public void TheReplayThroughTheMailboxIsStillStocks()
    {
        using var block = new NativeMailboxBlock();
        Replay replay = Run(block);
        Assert.True(replay.Commands >= 400 && replay.Extras >= 300 && replay.Thinks >= 300,
                    $"replayed {replay.Commands} commands, {replay.Extras} extra samples, {replay.Thinks} thinks");
        Assert.True(replay.CommandMismatches.Count + replay.ExtraMismatches.Count + replay.CameraMismatches.Count == 0,
                    $"{First(replay.CommandMismatches)}\n{First(replay.ExtraMismatches)}\n{First(replay.CameraMismatches)}");
    }

    [Fact]
    public void ThePublishedWordIsTheInputsStateAfterEveryReplayedEvent()
    {
        using var block = new NativeMailboxBlock();
        Replay replay = Run(block);
        Assert.True(replay.ThirdPersonAnswers >= 1100, $"native could answer CAM_IsThirdPerson after {replay.ThirdPersonAnswers} events");
        Assert.True(replay.PublishMismatches.Count == 0,
                    $"{replay.PublishMismatches.Count} events left the block stale:\n{First(replay.PublishMismatches)}");
    }

    private static string First(List<string> mismatches) => string.Join("\n", mismatches.Take(5));

    [Fact]
    public void EveryRecordedUserCommandIsBuiltAsStockBuiltIt()
    {
        Replay replay = Run();
        Assert.True(replay.Commands >= 400, $"replayed {replay.Commands} commands");
        Assert.True(replay.MovedCommands >= 200, $"only {replay.MovedCommands} commands moved, turned or pressed anything");
        Assert.True(replay.CommandMismatches.Count == 0,
                    $"{replay.CommandMismatches.Count} of {replay.Commands} commands differ from stock:\n{First(replay.CommandMismatches)}");
    }

    [Fact]
    public void EveryExtraMouseSampleTurnsTheViewAsStockDid()
    {
        Replay replay = Run();
        Assert.True(replay.Extras >= 300, $"replayed {replay.Extras} extra samples");
        Assert.True(replay.ExtraMismatches.Count == 0,
                    $"{replay.ExtraMismatches.Count} of {replay.Extras} extra samples differ from stock:\n{First(replay.ExtraMismatches)}");
    }

    [Fact]
    public void EveryCameraThinkMovesTheThirdPersonOffsetsAsStockDid()
    {
        Replay replay = Run();
        Assert.True(replay.Thinks >= 300, $"replayed {replay.Thinks} camera thinks");
        Assert.True(replay.CameraMismatches.Count == 0,
                    $"{replay.CameraMismatches.Count} of {replay.Thinks} camera thinks differ from stock:\n{First(replay.CameraMismatches)}");
    }

    [Fact]
    public void EveryKeyButtonStateMatchesStockAfterEveryEvent()
    {
        Replay replay = Run();
        Assert.True(replay.ButtonRows >= 700, $"compared {replay.ButtonRows} button rows");
        Assert.True(replay.ButtonMismatches.Count == 0,
                    $"{replay.ButtonMismatches.Count} button rows differ from stock:\n{First(replay.ButtonMismatches)}");
    }

    [Fact]
    public void TheSteamControllerSendsTheCommandsStockSent()
    {
        Replay replay = Run();
        Assert.True(replay.ControllerCommands >= 50, $"stock sent {replay.ControllerCommands} controller commands");
        Assert.True(replay.ControllerCommandMismatches.Count == 0,
                    $"{replay.ControllerCommandMismatches.Count} events differ:\n{First(replay.ControllerCommandMismatches)}");
    }

    [Fact]
    public void EveryDeviceReadIsServedInStocksOrder()
    {
        // Run throws on the first read out of order or left over; this counts
        // that the tape was really read from.
        Replay replay = Run();
        Assert.True(replay.ReadsServed >= 5000, $"served {replay.ReadsServed} device reads");
    }
}
