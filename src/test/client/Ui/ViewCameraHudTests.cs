using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The second camera's HUD panel (lane camproto): the camera's picture, or nothing when it is off.</summary>
public class ViewCameraHudTests
{
    private static readonly UiRect Uv = new(0, 0, 1f, 0.5625f);

    private static UiContext Described(TestScreen.Describer describe)
        => UiTestKit.Frame(UiTestKit.Context(), UiTestKit.Screen(1920, 1080), describe, layer: UiLayerKind.Hud);

    private sealed class Camera : IViewCameraSource
    {
        public ViewCameraMode Mode { get; set; }
        public int Texture => 3;
        public UiRect Uv => ViewCameraHudTests.Uv;
        public float Aspect => 16f / 9f;
    }

    [Fact]
    public void The_hud_panel_shows_the_cameras_target_and_hides_when_it_is_off()
    {
        var camera = new Camera { Mode = ViewCameraMode.Off };
        var screen = new ViewCameraHud(camera);
        Assert.False(screen.Visible);
        var off = Described(screen.Describe);
        Assert.Equal(0, off.List.AsDrawList().TexturedQuads.Length);

        camera.Mode = ViewCameraMode.ThirdPerson;
        Assert.True(screen.Visible);
        var on = Described(screen.Describe);
        var quad = Assert.Single(on.List.AsDrawList().TexturedQuads.ToArray());
        Assert.Equal((3, Uv), (quad.Texture, quad.Uv));
        Assert.Equal(16f / 9f, quad.Rect.W / quad.Rect.H, 1);
    }
}
