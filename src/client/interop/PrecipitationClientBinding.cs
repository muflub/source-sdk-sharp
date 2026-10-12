using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// IManagedPrecipitationClient: DrawPrecipitation (shell_precipitation_client.cpp)
/// asking the managed weather for one render pass's plan.
/// </summary>
public sealed unsafe class PrecipitationClientBinding(Crossings crossings, IClientPrecipitationRenderer renderer) : IManagedPrecipitationClient
{
    private const int ViewFloats = 24;
    private const int TracerFloats = 10;
    private const int SplashFloats = 6;

    public int PrecipitationRender(void* a0, void* a1, int a2, void* a3, int a4, void* a5)
    {
        crossings.Count(CrossingOp.ClientPrecipitationRender);
        if (a0 == null || a1 == null || a3 == null || a5 == null || a2 < 0 || a4 < 0)
            return 0;

        int count = renderer.Render(new ReadOnlySpan<float>(a0, ViewFloats),
                                    new Span<float>(a1, a2 * TracerFloats),
                                    new Span<float>(a3, a4 * SplashFloats),
                                    out int splashes);
        *(int*)a5 = splashes;
        return count;
    }
}
