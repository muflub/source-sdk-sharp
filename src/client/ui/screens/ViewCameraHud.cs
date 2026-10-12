using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The second camera's panel (lane camproto): top left, the picture the game
/// draws into a view render target (<c>ui.Texture</c> over the target's
/// texture, sampling the part the view fills) under a label naming
/// the camera. Nothing when the camera is off (cs_cl_overview 0). The picture
/// keeps the camera's shape: wide for the third-person view, square for the
/// overview.
/// </summary>
public sealed class ViewCameraHud : UiScreen
{
    private readonly IViewCameraSource _camera;

    public ViewCameraHud(IViewCameraSource camera) => _camera = camera;

    public override string Name => "viewcam";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override int Order => 4;

    public override bool Visible => _camera.Mode != ViewCameraMode.Off;

    /// <summary>The picture's width; smaller on a narrow screen.</summary>
    public const float WidthDu = 400, NarrowWidthDu = 280;

    public override void Describe(ref UiFrame ui)
    {
        var mode = _camera.Mode;
        if (mode == ViewCameraMode.Off)
            return;

        float width = ui.Narrow ? NarrowWidthDu : WidthDu;
        float aspect = _camera.Aspect > 0.1f ? _camera.Aspect : 16f / 9f;
        float height = MathF.Round(width / aspect);

        using (ui.Panel(Space.S, Space.XS).At(UiAnchor.TopLeft).Inset(Space.XXL))
        {
            ui.Label(mode == ViewCameraMode.Overview ? "#SS_UI_ViewCam_Overview" : "#SS_UI_ViewCam_ThirdPerson");
            ui.Texture(_camera.Texture, Size.Du(width), Size.Du(height), _camera.Uv).Name("picture");
        }
    }
}
