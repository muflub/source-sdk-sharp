//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>
/// view.cpp's demo free camera (CalcDemoViewOverride, cl_demoviewoverride):
/// while a demo plays with the override on, the view is a camera the player
/// flies with the movement keys, looking where the mouse turns the engine's
/// view angles; with it off, the camera keeps up with the demo's own view.
/// Native asks it twice a frame while a demo plays: for the angles to put on
/// the engine before the extra mouse sample, then with the angles the mouse
/// left, for the view.
/// </summary>
/// <param name="keyState">The input's KeyState by button token id (in_forward 5, in_back 6, in_moveleft 7, in_moveright 8).</param>
/// <param name="viewOverride">cl_demoviewoverride: the camera's speed, 320 units a second at 1.</param>
public sealed class DemoViewCamera(Func<int, float> keyState, Func<float> viewOverride)
{
    private const int InForward = 5, InBack = 6, InMoveLeft = 7, InMoveRight = 8;

    private Vector _view;
    private Vector _angles;

    /// <summary>
    /// SetUpViews' demo step for the view <paramref name="origin"/>,
    /// <paramref name="angles"/>: with the override off, the camera takes the
    /// demo's view and false is answered; with it on, true and the angles to
    /// put on the engine before the mouse sample.
    /// </summary>
    public bool Begin(Vector origin, Vector angles, out Vector engineAngles)
    {
        if (viewOverride() > 0.0f)
        {
            engineAngles = _angles;
            return true;
        }
        _view = origin;
        _angles = angles;
        engineAngles = default;
        return false;
    }

    /// <summary>The camera moved by the keys over <paramref name="frameTime"/>, looking along <paramref name="mouseAngles"/>.</summary>
    public (Vector Origin, Vector Angles) Move(Vector mouseAngles, float frameTime)
    {
        _angles = mouseAngles;
        MoveVec.AngleVectors(_angles, out Vector forward, out Vector right, out _);
        float speed = frameTime * viewOverride() * 320;
        _view = Add(_view, Scale(forward, speed * keyState(InForward)));
        _view = Sub(_view, Scale(forward, speed * keyState(InBack)));
        _view = Add(_view, Scale(right, speed * keyState(InMoveRight)));
        _view = Sub(_view, Scale(right, speed * keyState(InMoveLeft)));
        return (_view, _angles);
    }

    private static Vector Scale(Vector v, float s) => new(v.X * s, v.Y * s, v.Z * s);
    private static Vector Add(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static Vector Sub(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}
