using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.ViewCamera;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientViewCamera"/> over the debug camera (lane camproto;
/// user ruling 2026-10-11): its convars, the service's counters and the
/// executor's own drawn count. The switch is a write: it needs sv_cheats 1,
/// as the cheat convar does, and says so otherwise.
/// </summary>
public sealed class ClientDebugViewCamera(
    ViewCameraService camera, IViewTargets targets, IEngineConsole console,
    ViewCameraModeConVar mode, ViewCameraEveryConVar every, Render.RenderPlanHost? plan = null) : IDebugClientViewCamera
{
    public DebugClientViewCamera Read() => Snapshot(null);

    public DebugClientViewCamera Set(string value)
    {
        if (value is not ("0" or "1" or "2"))
            return Snapshot("refused: mode is 0, 1 or 2");
        if (!Cheats())
            return Snapshot("refused: needs sv_cheats 1");

        console.SetConVarValueByName("cs_cl_overview", value);
        return Snapshot("set");
    }

    private bool Cheats() => console.FindConVarValue("sv_cheats") is "1";

    private DebugClientViewCamera Snapshot(string? write)
    {
        var c = camera.Counters;
        targets.Info(ViewCameraService.TargetNumber, out var t);
        return new DebugClientViewCamera(
            Name((ViewCameraMode)Math.Clamp(mode.Value, 0, 2)), Name(((IViewCameraSource)camera).Mode), every.Value, Cheats(),
            c.Passes, c.Requested, c.Queued, c.Refused, t.Renders, t.Width, t.Height, t.Texture, write,
            // The plan's monitor passes, read in the same game-thread snapshot as
            // native's drawn count: the two move together within one RenderView.
            plan?.MonitorPasses ?? 0);
    }

    private static string Name(ViewCameraMode m) => m switch
    {
        ViewCameraMode.ThirdPerson => "thirdperson",
        ViewCameraMode.Overview => "overview",
        _ => "off",
    };
}
