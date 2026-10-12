//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>
/// CViewRender's freeze frame (viewrender.cpp, mono): how long the captured
/// frame is shown for, and whether the next frame is the one to capture.
/// <see cref="Freeze"/> is CViewRender::FreezeFrame's rule; the render plan
/// reads <see cref="Showing"/> and <see cref="CaptureDue"/> for its frame.
/// </summary>
public sealed class FreezeFrameState
{
    private float _until;

    /// <summary>The next plan frame draws the scene and captures it.</summary>
    public bool CaptureDue { get; private set; }

    /// <summary>
    /// The freeze flash's end time (g_flFreezeFlash): stock zeroes it and nothing
    /// in the tree sets it, so the flash never shows.
    /// </summary>
    public float Flash => 0f;

    /// <summary>
    /// CViewRender::FreezeFrame: 0 ends any freeze; a freeze already running is
    /// lengthened; a new one runs from now and captures the next frame.
    /// </summary>
    public void Freeze(float freezeTime, float curTime)
    {
        if (freezeTime == 0)
        {
            _until = 0;
            CaptureDue = false;
        }
        else if (_until > curTime)
        {
            _until += freezeTime;
        }
        else
        {
            _until = curTime + freezeTime;
            CaptureDue = true;
        }
    }

    /// <summary>Whether a freeze runs at <paramref name="curTime"/>.</summary>
    public bool Showing(float curTime) => _until > curTime;

    /// <summary>The planned frame captured itself.</summary>
    public void Captured() => CaptureDue = false;
}

/// <summary>viewrender.cpp's <c>test_freezeframe</c>: three seconds of freeze frame.</summary>
[ConCommand("test_freezeframe", Help = "Test the freeze frame code.", Flags = ConVarFlags.Cheat)]
public sealed class TestFreezeFrameCommand(RenderPlanHost host, IGameClock clock) : IConCommand
{
    public void Execute(string args) => host.ViewFreezeFrame(3.0f, clock.Now);
}
