//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

/// <summary>
/// Every <see cref="KeyButton"/> the input commands drive: in_main.cpp's
/// globals and statics, in their declaration order (the order the oracle
/// records them in), then in_camera.cpp's six. Each has an id, its index in
/// <see cref="All"/>, which is what the native shell's kbutton_t tokens name.
/// </summary>
public sealed class KeyButtons
{
    public KeyButtons()
    {
        All =
        [
            Speed, Walk, JLook, Strafe, CommanderMouseMove, Forward, Back, MoveLeft, MoveRight, Graph,
            JoySpeed, KLook, Left, Right, LookUp, LookDown, Use, Jump, Attack, Attack2,
            Up, Down, Duck, Reload, Alt1, Alt2, Score, Break, Zoom, Grenade1,
            Grenade2, Attack3, DuckToggle,
            CamPitchUp, CamPitchDown, CamYawLeft, CamYawRight, CamIn, CamOut,
        ];

        foreach (KeyButton button in All)
            _byName[button.Name] = button;
    }

    private readonly Dictionary<string, KeyButton> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The main buttons in in_main.cpp's order (33), then the camera's (6).</summary>
    public IReadOnlyList<KeyButton> All { get; }

    /// <summary>How many of <see cref="All"/> are in_main.cpp's.</summary>
    public const int MainCount = 33;

    public KeyButton Speed { get; } = new("speed");
    public KeyButton Walk { get; } = new("walk");
    public KeyButton JLook { get; } = new("jlook");
    public KeyButton Strafe { get; } = new("strafe");
    public KeyButton CommanderMouseMove { get; } = new("commandermousemove");
    public KeyButton Forward { get; } = new("forward");
    public KeyButton Back { get; } = new("back");
    public KeyButton MoveLeft { get; } = new("moveleft");
    public KeyButton MoveRight { get; } = new("moveright");
    public KeyButton Graph { get; } = new("graph");
    public KeyButton JoySpeed { get; } = new("joyspeed");
    public KeyButton KLook { get; } = new("klook");
    public KeyButton Left { get; } = new("left");
    public KeyButton Right { get; } = new("right");
    public KeyButton LookUp { get; } = new("lookup");
    public KeyButton LookDown { get; } = new("lookdown");
    public KeyButton Use { get; } = new("use");
    public KeyButton Jump { get; } = new("jump");
    public KeyButton Attack { get; } = new("attack");
    public KeyButton Attack2 { get; } = new("attack2");
    public KeyButton Up { get; } = new("moveup");
    public KeyButton Down { get; } = new("movedown");
    public KeyButton Duck { get; } = new("duck");
    public KeyButton Reload { get; } = new("reload");
    public KeyButton Alt1 { get; } = new("alt1");
    public KeyButton Alt2 { get; } = new("alt2");
    public KeyButton Score { get; } = new("score");
    public KeyButton Break { get; } = new("break");
    public KeyButton Zoom { get; } = new("zoom");
    public KeyButton Grenade1 { get; } = new("grenade1");
    public KeyButton Grenade2 { get; } = new("grenade2");
    public KeyButton Attack3 { get; } = new("attack3");
    public KeyButton DuckToggle { get; } = new("ducktoggle");
    public KeyButton CamPitchUp { get; } = new("campitchup");
    public KeyButton CamPitchDown { get; } = new("campitchdown");
    public KeyButton CamYawLeft { get; } = new("camyawleft");
    public KeyButton CamYawRight { get; } = new("camyawright");
    public KeyButton CamIn { get; } = new("camin");
    public KeyButton CamOut { get; } = new("camout");

    /// <summary>
    /// The buttons the ENGINE may ask for by name (IN_IsKeyDown): stock's
    /// FindKey list held exactly in_graph and in_jlook (Init_Keyboard).
    /// </summary>
    public KeyButton? FindForEngine(string name)
        => string.Equals(name, "in_graph", StringComparison.OrdinalIgnoreCase) ? Graph
         : string.Equals(name, "in_jlook", StringComparison.OrdinalIgnoreCase) ? JLook
         : null;

    /// <summary>A button by its own name (<c>forward</c>), for the oracle and the dev route.</summary>
    public KeyButton? Find(string name) => _byName.TryGetValue(name, out KeyButton? button) ? button : null;

    /// <summary>The id the native kbutton_t tokens carry, or -1.</summary>
    public int IdOf(KeyButton button)
    {
        for (int i = 0; i < All.Count; i++)
            if (ReferenceEquals(All[i], button))
                return i;
        return -1;
    }
}
