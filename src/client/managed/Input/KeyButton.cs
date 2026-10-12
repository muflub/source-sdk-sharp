//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

/// <summary>
/// One button the key commands drive: stock's <c>kbutton_t</c> and its two
/// functions, KeyDown and KeyUp (in_main.cpp).
///
/// <para>
/// Two different keys can hold one button (mouse 1 and the control key on
/// <c>+attack</c>), and the button is released only when both are. Each key
/// command carries its key code so its release can be matched with it. State
/// bit 0 is the current state, bit 1 the up-to-down edge, bit 2 the
/// down-to-up edge.
/// </para>
/// </summary>
public sealed class KeyButton(string name)
{
    public const int Held = 1;
    public const int ImpulseDown = 2;
    public const int ImpulseUp = 4;

    /// <summary>The name the command after the +/- carries (<c>forward</c> for <c>+forward</c>).</summary>
    public string Name { get; } = name;

    /// <summary>The two keys holding it (0 = none).</summary>
    public int Down0 { get; set; }
    public int Down1 { get; set; }
    public int State { get; set; }

    public bool IsHeld => (State & Held) != 0;

    /// <summary>KeyDown. <paramref name="code"/> is the command's first argument, or null for none.</summary>
    public void KeyDown(string? code)
    {
        int k = -1;
        if (!string.IsNullOrEmpty(code))
            k = CRuntime.Atoi(code);

        if (k == Down0 || k == Down1)
            return;     // repeating key

        if (Down0 == 0)
            Down0 = k;
        else if (Down1 == 0)
            Down1 = k;
        else
            return;     // three keys down for one button (stock DevMsg'd it)

        if ((State & Held) != 0)
            return;     // still down
        State |= Held + ImpulseDown;
    }

    /// <summary>KeyUp. A release with no code (the console's -command) clears the button outright.</summary>
    public void KeyUp(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            Down0 = Down1 = 0;
            State = ImpulseUp;
            return;
        }

        int k = CRuntime.Atoi(code);

        if (Down0 == k)
            Down0 = 0;
        else if (Down1 == k)
            Down1 = 0;
        else
            return;     // key up without a matching down (menu pass through)

        if (Down0 != 0 || Down1 != 0)
            return;     // some other key is still holding it down

        if ((State & Held) == 0)
            return;     // still up (this should not happen)

        State &= ~Held;
        State |= ImpulseUp;
    }

    /// <summary>
    /// CInput::KeyState: 0.25 if pressed and released during the frame, 0.5 if
    /// pressed and held, 0 if held then released, 1 if held throughout, 0.75
    /// if released and pressed again. Clears the edges.
    /// </summary>
    public float Sample()
    {
        float val = 0.0f;
        int impulseDown = State & ImpulseDown;
        int impulseUp = State & ImpulseUp;
        int down = State & Held;

        if (impulseDown != 0 && impulseUp == 0)
            val = down != 0 ? 0.5f : 0.0f;

        if (impulseUp != 0 && impulseDown == 0)
            val = 0.0f;

        if (impulseDown == 0 && impulseUp == 0)
            val = down != 0 ? 1.0f : 0.0f;

        if (impulseDown != 0 && impulseUp != 0)
            val = down != 0 ? 0.75f : 0.25f;

        State &= Held;
        return val;
    }

    /// <summary>Back to never pressed (the camera's Maya mode zeroes its buttons).</summary>
    public void Clear()
    {
        Down0 = Down1 = 0;
        State = 0;
    }
}
