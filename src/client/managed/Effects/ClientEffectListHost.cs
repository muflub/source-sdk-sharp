using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The client-side effect list as native reaches it (IManagedFxClient): the
/// view's per-frame draw, the client DLL's flush, the level start, and the
/// <c>FX_Add*</c> entries the fork's effect TUs still call, unpacked from the
/// floats native sent into the factory's calls.
/// </summary>
public sealed class ClientEffectListHost(ClientEffectList list, ClientSideEffectFactory factory) : IClientEffectListHost
{
    private static Vector V(ReadOnlySpan<float> f, int at) => new(f[at], f[at + 1], f[at + 2]);

    public void DrawEffects(float frametime) => list.DrawEffects(frametime);

    public void Flush() => list.Flush();

    public void LevelInit() => factory.ResetWhizTimer();

    public void AddLine(ReadOnlySpan<float> f, int material)
        => factory.AddLine(new FxLineData
        {
            Start = V(f, 0), End = V(f, 3), StartVelocity = V(f, 6), EndVelocity = V(f, 9),
            StartAlpha = f[12], EndAlpha = f[13], StartScale = f[14], EndScale = f[15], DieTime = f[16],
            Material = material,
        });

    public void AddQuad(ReadOnlySpan<float> f, uint flags, int material)
        => factory.AddQuad(new FxQuadData
        {
            Flags = (FxQuadFlags)flags, Material = material, Origin = V(f, 0), Normal = V(f, 3),
            StartScale = f[6], EndScale = f[7], DieTime = f[8], LifeTime = f[9], StartAlpha = f[10], EndAlpha = f[11],
            Color = V(f, 12), Yaw = f[15], DeltaYaw = f[16], ScaleBias = f[17], AlphaBias = f[18],
        });

    public void AddQuadNamed(ReadOnlySpan<float> f, uint flags, string material)
        => factory.AddQuad(V(f, 0), V(f, 3), f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], V(f, 14), f[17],
                           material, (FxQuadFlags)flags);

    public void AddStaticLine(Vector start, Vector end, float scale, float life, string material, int flags)
        => factory.AddStaticLine(start, end, scale, life, material, (FxStaticLineFlags)(byte)flags);

    public void AddDiscreetLine(Vector start, Vector direction, float velocity, float length, float clip, float scale, float life, string material)
        => factory.AddDiscreetLine(start, direction, velocity, length, clip, scale, life, material);

    public void TracerSound(Vector start, Vector end, int type) => factory.TracerSound(start, end, type);
}
