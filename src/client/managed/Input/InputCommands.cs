//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>An input command's answer, for the console registry.</summary>
public sealed class InputConCommand(Action<string?> run) : IConCommand
{
    public void Execute(string args) => run(args);
}

/// <summary>One input console command: stock's name, help and flags, and what it does.</summary>
public sealed record InputCommandSpec(string Name, string Help, ConVarFlags Flags, Action<string?> Run);

/// <summary>
/// The console commands stock's input TUs declared (in_main.cpp, in_camera.cpp,
/// in_steamcontroller.cpp), bound to one <see cref="ClientInput"/>. A key
/// command's argument is the key code its binding appended (<c>args[1]</c>).
/// </summary>
public static class InputCommands
{
    public static IReadOnlyList<InputCommandSpec> For(ClientInput input, IInputHost host, InputCvars cvars)
    {
        KeyButtons b = input.Buttons;
        var list = new List<InputCommandSpec>();

        void Key(string name, KeyButton button)
        {
            list.Add(new InputCommandSpec("+" + name, string.Empty, ConVarFlags.None, a => button.KeyDown(ClientInput.FirstToken(a))));
            list.Add(new InputCommandSpec("-" + name, string.Empty, ConVarFlags.None, a => button.KeyUp(ClientInput.FirstToken(a))));
        }

        void Command(string name, string help, ConVarFlags flags, Action<string?> run)
            => list.Add(new InputCommandSpec(name, help, flags, run));

        // in_main.cpp, in its registration order.
        Key("commandermousemove", b.CommanderMouseMove);
        Key("moveup", b.Up);
        Key("movedown", b.Down);
        Key("left", b.Left);
        Key("right", b.Right);
        Key("forward", b.Forward);
        Key("back", b.Back);
        Key("lookup", b.LookUp);
        Key("lookdown", b.LookDown);
        Key("strafe", b.Strafe);
        Key("moveleft", b.MoveLeft);
        Key("moveright", b.MoveRight);
        Key("speed", b.Speed);
        Key("walk", b.Walk);
        Command("+attack", string.Empty, ConVarFlags.None, a => b.Attack.KeyDown(ClientInput.FirstToken(a)));
        Command("-attack", string.Empty, ConVarFlags.None, a =>
        {
            b.Attack.KeyUp(ClientInput.FirstToken(a));
            input.ClearCancel();
        });
        Key("attack2", b.Attack2);
        Key("use", b.Use);
        Key("jump", b.Jump);
        Command("impulse", string.Empty, ConVarFlags.None, input.Impulse);
        Key("klook", b.KLook);
        Key("jlook", b.JLook);
        Key("duck", b.Duck);
        Key("reload", b.Reload);
        Key("alt1", b.Alt1);
        Key("alt2", b.Alt2);
        foreach (string score in new[] { "score", "showscores" })
        {
            Command("+" + score, string.Empty, ConVarFlags.None, a =>
            {
                b.Score.KeyDown(ClientInput.FirstToken(a));
                host.ShowScoreboard(true);
            });
            Command("-" + score, string.Empty, ConVarFlags.None, a =>
            {
                b.Score.KeyUp(ClientInput.FirstToken(a));
                host.ShowScoreboard(false);
            });
        }
        Key("graph", b.Graph);
        // +break also broke into the debugger in a _DEBUG build; there is no
        // debugger to break into from managed code worth a stop here.
        Key("break", b.Break);
        Command("force_centerview", string.Empty, ConVarFlags.None, _ => input.CenterView());
        Command("joyadvancedupdate", string.Empty, ConVarFlags.ClientCmdCanExecute, _ => input.JoystickAdvanced());
        Key("zoom", b.Zoom);
        Key("grenade1", b.Grenade1);
        Key("grenade2", b.Grenade2);
        Key("attack3", b.Attack3);
        // The Xbox 360 stubs: registered, and they do nothing.
        Command("xmove", string.Empty, ConVarFlags.None, _ => { });
        Command("xlook", string.Empty, ConVarFlags.None, _ => { });

        // in_camera.cpp.
        Key("campitchup", b.CamPitchUp);
        Key("campitchdown", b.CamPitchDown);
        Key("camyawleft", b.CamYawLeft);
        Key("camyawright", b.CamYawRight);
        Key("camin", b.CamIn);
        Key("camout", b.CamOut);
        Command("thirdperson_mayamode", "Switch to thirdperson Maya-like camera controls.", ConVarFlags.Cheat, _ => input.ToggleMayaMode());
        Command("thirdperson", "Switch to thirdperson camera.", ConVarFlags.Cheat, _ => input.ThirdPersonCamera.CamToThirdPerson());
        Command("firstperson", "Switch to firstperson camera.", ConVarFlags.None, _ => input.ThirdPersonCamera.CamToFirstPerson());
        Command("camortho", "Switch to orthographic camera.", ConVarFlags.Cheat, _ => input.ThirdPersonCamera.CamToOrthographic());
        Command("+cammousemove", string.Empty, ConVarFlags.None, _ => input.CamStartMouseMove());
        Command("-cammousemove", string.Empty, ConVarFlags.None, _ => input.CamEndMouseMove());
        Command("+camdistance", string.Empty, ConVarFlags.None, _ => input.CamStartDistance());
        Command("-camdistance", string.Empty, ConVarFlags.None, _ => input.CamEndDistance());
        Command("snapto", string.Empty, ConVarFlags.None, _ => input.ToggleSnapTo());

        // in_steamcontroller.cpp.
        Command("sc_show_binding_panel", "Launches the Steam Controller binding panel UI", ConVarFlags.None, _ => input.ShowBindingPanel());
        Command("sc_status", "Show Steam Controller status information", ConVarFlags.None, _ => input.SteamControllerStatus());

        return list;
    }
}
