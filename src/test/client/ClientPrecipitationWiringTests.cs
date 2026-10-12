using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The weather's render call as native reaches it (lane c2-sims):
/// IManagedPrecipitationClient counts its crossing, hands the view to the
/// renderer and writes back the splash count.
/// </summary>
public class ClientPrecipitationWiringTests
{
    private sealed class Renderer : IClientPrecipitationRenderer
    {
        public float[] LastView = [];

        public int Render(ReadOnlySpan<float> view, Span<float> tracers, Span<float> splashes, out int splashCount)
        {
            LastView = view.ToArray();
            tracers[0] = 7f;
            splashes[0] = 9f;
            splashCount = 1;
            return 1;
        }
    }

    [Fact]
    public unsafe void TheRenderCallIsCountedAndCarriesThePlanBack()
    {
        var crossings = new Crossings();
        var renderer = new Renderer();
        IManagedPrecipitationClient binding = new PrecipitationClientBinding(crossings, renderer);

        float[] view = Enumerable.Range(0, 24).Select(i => (float)i).ToArray();
        float[] tracers = new float[20];
        float[] splashes = new float[12];
        int splashCount = -1;
        int n;
        fixed (float* v = view)
        fixed (float* t = tracers)
        fixed (float* s = splashes)
            n = binding.PrecipitationRender(v, t, 2, s, 2, &splashCount);

        Assert.True(n == 1 && splashCount == 1 && tracers[0] == 7f && splashes[0] == 9f, $"n {n} splashes {splashCount}");
        Assert.True(renderer.LastView.Length == 24 && renderer.LastView[23] == 23f, "the whole view reached the renderer");
        Assert.True(crossings.Read(CrossingOp.ClientPrecipitationRender) == 1, "the render counts its crossing");
    }
}
