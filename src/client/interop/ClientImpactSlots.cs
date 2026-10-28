using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IClientImpactEngine"/> over the bullet impact's slots
/// (shell_impact_effects.cpp, lane M1-C): one crossing each.
/// </summary>
internal sealed unsafe class ClientImpactSlots(ITempEntBackendsClientArea backends, IParticlesClientArea particles)
    : IClientImpactEngine
{
    // SourceSharp_ClientShotTrace's outs: floats [fraction, end xyz, normal xyz];
    // ints [hit, hitbox, entity, surfaceProps, surfaceFlags, hitWorld].
    private const int ShotFloats = 7;
    private const int ShotInts = 6;

    /// <summary>The world trace's entity argument: no entity, so the line through the world.</summary>
    private const int NoEntity = -1;

    public ShotTrace? ClipToEntity(Vector start, Vector end, int entityIndex)
        => entityIndex < 0 ? null : Shot(start, end, entityIndex);

    public ShotTrace TraceWorld(Vector start, Vector end)
        => Shot(start, end, NoEntity) ?? new ShotTrace(false, 1f, end, default, 0, -1, 0, 0, false);

    private ShotTrace? Shot(Vector start, Vector end, int entityIndex)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* f = stackalloc float[ShotFloats];
        int* i = stackalloc int[ShotInts];

        if (backends.ClientShotTrace(s, e, entityIndex, f, ShotFloats, i, ShotInts) < 0)
            return null;

        return new ShotTrace(i[0] != 0, f[0], new Vector(f[1], f[2], f[3]), new Vector(f[4], f[5], f[6]),
                             i[1], i[2], i[3], i[4], i[5] != 0);
    }

    public (Vector Diffuse, Vector Base) WorldLighting(Vector start, Vector end)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* o = stackalloc float[6];
        new Span<float>(o, 6).Clear();
        backends.ClientWorldLighting(s, e, o);
        return (new Vector(o[0], o[1], o[2]), new Vector(o[3], o[4], o[5]));
    }

    public (Vector Diffuse, Vector Base) StaticPropLighting(Vector start, Vector end, Vector normal, int staticProp)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* n = stackalloc float[3] { normal.X, normal.Y, normal.Z };
        float* o = stackalloc float[6];
        new Span<float>(o, 6).Clear();
        backends.ClientStaticPropLighting(s, e, n, staticProp, o);
        return (new Vector(o[0], o[1], o[2]), new Vector(o[3], o[4], o[5]));
    }

    public void Ledger(int flecks, int dust) => backends.ClientImpactLedger(flecks, dust);

    public bool ImpactParticle(string name, ReadOnlySpan<ImpactControlPoint> points, int entityIndex, float scale,
                               Vector color)
    {
        // Three oriented points, twelve floats each, as the slot reads them.
        const int Points = 3;
        if (points.Length < Points)
            return false;

        float* flat = stackalloc float[Points * 12];
        for (int k = 0; k < Points; k++)
        {
            ImpactControlPoint p = points[k];
            float* at = flat + k * 12;
            at[0] = p.Position.X; at[1] = p.Position.Y; at[2] = p.Position.Z;
            at[3] = p.Forward.X;  at[4] = p.Forward.Y;  at[5] = p.Forward.Z;
            at[6] = p.Right.X;    at[7] = p.Right.Y;    at[8] = p.Right.Z;
            at[9] = p.Up.X;       at[10] = p.Up.Y;      at[11] = p.Up.Z;
        }

        float* c = stackalloc float[3] { color.X, color.Y, color.Z };
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* pName = bytes)
            return particles.ParticleSystemImpact(pName, flat, entityIndex, scale, c) != 0;
    }
}
