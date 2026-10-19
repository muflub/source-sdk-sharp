namespace Debug.Api.Client.Interfaces;

/// <summary>
/// The debug camera (lane camproto; user ruling 2026-10-11: a debug tool, not
/// gameplay), as <c>/client/{slot}/viewcam</c> reports it: what cs_cl_overview
/// asks for, whether it is drawing, and what the executor did.
/// </summary>
/// <param name="Mode">cs_cl_overview's value by name: "off", "thirdperson" or "overview".</param>
/// <param name="Active">The mode in effect: "off" without sv_cheats 1 or before the target exists.</param>
/// <param name="Every">cs_cl_overview_every: one frame in this many.</param>
/// <param name="Cheats">sv_cheats is 1: the camera may draw and the route may write.</param>
/// <param name="Passes">Render-start passes with the camera on and an eye known.</param>
/// <param name="Requested">Views asked of the executor.</param>
/// <param name="Queued">Of those, the ones it took.</param>
/// <param name="Refused">And the ones it refused.</param>
/// <param name="Drawn">Views the native executor drew into the target since load.</param>
/// <param name="Width">The target's width; 0 before the game made it.</param>
/// <param name="Height">The target's height.</param>
/// <param name="Texture">The vgui texture id over the target; 0 before the UI first asked.</param>
/// <param name="Write">For a write: "set", or why it was refused; null for a read.</param>
public sealed record DebugClientViewCamera(
    string Mode, string Active, int Every, bool Cheats, long Passes, long Requested, long Queued, long Refused,
    int Drawn, int Width, int Height, int Texture, string? Write = null, long MonitorPasses = 0);

/// <summary>The debug camera's state, and a cheat-gated switch for it. Game thread.</summary>
public interface IDebugClientViewCamera
{
    DebugClientViewCamera Read();

    /// <summary>A WRITE: sets cs_cl_overview to <paramref name="mode"/> ("0", "1" or "2"). Needs sv_cheats 1.</summary>
    DebugClientViewCamera Set(string mode);
}
