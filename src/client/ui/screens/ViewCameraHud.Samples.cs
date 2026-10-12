using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class ViewCameraHudSamples
{
    private sealed class FakeCamera : IViewCameraSource
    {
        public ViewCameraMode Mode { get; init; } = ViewCameraMode.ThirdPerson;

        public int Texture => 0;

        public UiRect Uv { get; init; } = new(0, 0, 1, 0.5625f);

        public float Aspect { get; init; } = 16f / 9f;
    }

    [UiSample("third person")]
    private static UiScreen ThirdPerson(UiSampleServices s)
        => s.Add<IViewCameraSource>(new FakeCamera()).Create<ViewCameraHud>();

    [UiSample("overview")]
    private static UiScreen Overview(UiSampleServices s)
        => s.Add<IViewCameraSource>(new FakeCamera { Mode = ViewCameraMode.Overview, Uv = new(0, 0, 0.75f, 0.75f), Aspect = 1f })
            .Create<ViewCameraHud>();
}
