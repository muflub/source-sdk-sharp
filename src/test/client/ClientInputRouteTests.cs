using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using Debug.Api.Client.Interfaces;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's input routes (plan_client_novalve §4.4, lane c6-input):
/// <c>/client/{slot}/input</c> and the cheat-gated write <c>input/inject</c>,
/// each with its golden and a fact that reads a non-zero through it.
/// </summary>
public class ClientInputRouteTests
{
    private sealed class FixedInput(DebugClientInput input, DebugClientInputInject inject) : IDebugClientInput
    {
        public DebugClientInput Read() => input;
        public DebugClientInputInject Inject(string argument) => inject;
    }

    private static (InputRig Rig, ClientInputService Service, ClientDebugInput Route) Live(string cheats)
    {
        var rig = new InputRig();
        var service = new ClientInputService(rig.Input, null, () => true);
        return (rig, service, new ClientDebugInput(service, rig.Cvars, rig.Host, () => cheats));
    }

    private static InputCommand Move(InputRig rig, ClientInputService service, int sequence)
    {
        rig.Input.SetJoystickSampleTime(0.015f);
        rig.Input.SetKeyboardSampleTime(0.015f);
        var cmd = new DecodedUserCmd();
        cmd.Cmd.CommandNumber = sequence;
        cmd.Cmd.TickCount = 100 + sequence;
        service.CreateMove(sequence, 0.015f, true, ref cmd);
        return new InputCommand
        {
            ForwardMove = cmd.Cmd.ForwardMove, Buttons = cmd.Cmd.Buttons, MouseDx = cmd.Cmd.MouseDx,
            ViewAngles = new InputAngles(cmd.Cmd.Pitch, cmd.Cmd.Yaw, cmd.Cmd.Roll),
        };
    }

    [Fact]
    public void The_input_answer_renders_exactly()
    {
        var input = new DebugClientInput(
            Buttons: [new DebugClientInputButton("forward", 1, 87, 0), new DebugClientInputButton("jump", 0, 0, 0)],
            MouseActive: true, AccumulatedX: 0, AccumulatedY: 0, PreviousX: 12, PreviousY: -3,
            Sensitivity: new DebugClientInputSensitivity(3, 0, 1, 0.04f, 1.5f, 0, true, 0.022f, 0.022f, true),
            ThirdPerson: false, Orthographic: false, Joystick: true,
            Axes: [new DebugClientInputAxis(0, 4, false), new DebugClientInputAxis(1, 1, true)],
            ActionSet: 1, Actions: [new DebugClientInputAction("jump", "+jump", 5, true)],
            Commands: [new DebugClientInputCommand(41, 141, 1.5f, -90, 0, 450, -225, 0, 8, 101, 0, 36, -9)],
            InjectPending: 1, Injected: 2, LastInjectX: 100, LastInjectY: 0, LastInjectDeviceX: -3, LastInjectDeviceY: 0);
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(input: new FixedInput(input, null!)));
        Assert.Equal(DebugApiClientGoldenTests.Golden("input"), client.Answer(DebugChannelQuery.Input));
    }

    [Fact]
    public void The_inject_answer_renders_exactly()
    {
        var inject = new DebugClientInputInject(true, "added to the next read of the mouse", 100, -4, null);
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(input: new FixedInput(null!, inject)));
        Assert.Equal(DebugApiClientGoldenTests.Golden("input-inject"), client.Answer(DebugChannelQuery.InputInject, "100,-4"));
    }

    /// <summary>
    /// The daily gate's input-usercmd flake (2026-10-11, a4bc36394: +100
    /// counts turned the view -1.914 deg, want -6.6): a device delta read in
    /// the same sample as an inject sums with it, as stock sums a real mouse
    /// with the accumulator. The read now says how many of its counts were the
    /// device's, so the gate can tell "the inject was lost" from "the mouse
    /// moved too".
    /// </summary>
    [Fact]
    public void A_device_delta_read_with_an_inject_is_reported_beside_it()
    {
        var (rig, service, route) = Live("1");
        Assert.True(route.Inject("100,0").Accepted);
        rig.Devices.RawX = -71;   // a real mouse moving in the same read (m_rawinput's accumulators)

        InputCommand cmd = Move(rig, service, 1);

        Assert.Equal(-(0.022f * 3.0f * 29), cmd.ViewAngles.Yaw, 4);
        DebugClientInput read = route.Read();
        Assert.Equal((100, 0, -71, 0), (read.LastInjectX, read.LastInjectY, read.LastInjectDeviceX, read.LastInjectDeviceY));
    }

    [Fact]
    public void An_inject_read_alone_reports_no_device_counts()
    {
        var (rig, service, route) = Live("1");
        Assert.True(route.Inject("-100,0").Accepted);

        Move(rig, service, 1);

        DebugClientInput read = route.Read();
        Assert.Equal((-100, 0, 0, 0), (read.LastInjectX, read.LastInjectY, read.LastInjectDeviceX, read.LastInjectDeviceY));
    }

    [Fact]
    public void The_route_reads_a_held_button_and_the_command_it_built()
    {
        var (rig, service, route) = Live("0");
        rig.Run("+forward 87");
        Move(rig, service, 1);

        DebugClientInput read = route.Read();
        Assert.Contains(read.Buttons, b => b.Name == "forward" && b.State == 1 && b.Down0 == 87);
        DebugClientInputCommand cmd = Assert.Single(read.Commands);
        Assert.Equal((1, 101, 225.0f), (cmd.Command, cmd.Tick, cmd.Forward));
        Assert.Equal((int)InputButtons.Forward, cmd.Buttons & (int)InputButtons.Forward);
        Assert.True(read.MouseActive);
        Assert.Equal(3.0f, read.Sensitivity.Sensitivity);
    }

    [Fact]
    public void The_route_keeps_the_last_sixteen_commands()
    {
        var (rig, service, route) = Live("0");
        for (int i = 1; i <= 20; i++)
            Move(rig, service, i);

        Assert.Equal(Enumerable.Range(5, 16), route.Read().Commands.Select(c => c.Command));
    }

    [Fact]
    public void The_inject_is_refused_without_cheats_and_changes_nothing()
    {
        var (rig, service, route) = Live("0");
        DebugClientInputInject answer = route.Inject("100,0");

        Assert.False(answer.Accepted);
        Assert.Equal(0, Move(rig, service, 1).MouseDx);
        Assert.Equal(0.0f, rig.Host.View.Yaw);
    }

    [Fact]
    public void An_injected_delta_turns_the_next_command_by_the_sensitivity_scaled_amount()
    {
        var (rig, service, route) = Live("1");
        Assert.True(route.Inject("100,0").Accepted);
        Assert.Equal(1, route.Read().InjectPending);

        InputCommand cmd = Move(rig, service, 1);

        // m_yaw 0.022 * sensitivity 3 * 100 counts, to the left (yaw falls).
        Assert.Equal(300, cmd.MouseDx);
        Assert.Equal(-(0.022f * 300.0f), cmd.ViewAngles.Yaw);
        Assert.Equal((0, 1), (route.Read().InjectPending, route.Read().Injected));

        // One command's worth: the next is still.
        Assert.Equal(0, Move(rig, service, 2).MouseDx);
    }

    [Fact]
    public void An_injected_delta_lands_when_the_window_does_not_hold_the_mouse()
    {
        // The engine releases the mouse every frame a window lacks focus; a
        // real delta is not read then (stock), the scripted one is.
        var (rig, service, route) = Live("1");
        rig.Input.DeactivateMouse();
        Assert.False(rig.Input.MouseActive);

        rig.Devices.RawX = 50;                          // a real delta: not read uncaptured
        Assert.Equal(0, Move(rig, service, 1).MouseDx);
        rig.Devices.RawX = 0;                           // (re-capture would drain it: ClearStates)

        Assert.True(route.Inject("100,0").Accepted);
        Assert.Equal(300, Move(rig, service, 2).MouseDx);
    }

    [Fact]
    public void An_injected_key_lasts_one_command()
    {
        var (rig, service, route) = Live("1");
        Assert.True(route.Inject("+jump").Accepted);
        Assert.False(route.Inject("+nosuchbutton").Accepted);

        Assert.Equal((int)InputButtons.Jump, Move(rig, service, 1).Buttons & (int)InputButtons.Jump);
        Assert.Equal(0, Move(rig, service, 2).Buttons & (int)InputButtons.Jump);
    }
}
