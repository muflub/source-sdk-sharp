using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.ViewCamera;

/// <summary>
/// cs_cl_viewcam: the second camera's tally (lane camproto). The managed half
/// says what was asked for; <c>drawn</c> is native's count of views actually
/// drawn into the target, which is what the live step's A/B reads: it grows
/// with cs_cl_overview on and stays flat with it off.
/// </summary>
[ConCommand("cs_cl_viewcam", Help = "the second camera: passes, requests, refusals and views drawn into its target")]
public sealed class ViewCameraCommand(ILog log, IViewCameraService camera, IViewTargets targets) : IConCommand
{
    public void Execute(string args)
    {
        var c = camera.Counters;
        targets.Info(ViewCameraService.TargetNumber, out var t);
        log.Msg($"[SourceSharp] cs_cl_viewcam: {c.Passes} pass(es), {c.Requested} requested, {c.Queued} queued, " +
                $"{c.Refused} refused, {t.Renders} drawn; target {t.Width}x{t.Height} texture {t.Texture}\n");
    }
}
