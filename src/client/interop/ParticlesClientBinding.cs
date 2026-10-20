using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The managed particle sims, as the native draw executor asks for them
/// (shell_managed_particles.cpp, plan_client_novalve C2, lane c2-sims): by
/// owner handle and channel. Implemented by the client module over its entity
/// registry; -1 means no sim answers for that owner and channel.
/// </summary>
public interface IClientParticleSims
{
    /// <summary>Stock's <c>Update</c>: the spawns' ids (in spawn order) and the bounds, written into <paramref name="emit"/>.</summary>
    int Emit(uint owner, int channel, ref ParticleEmit emit, Span<int> idsOut);

    /// <summary>Stock's <c>SimulateParticles</c>: 1 and <paramref name="aliveOut"/> per id, or 0 when stock's step returned early.</summary>
    int Simulate(uint owner, int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut);

    /// <summary>Stock's <c>RenderParticles</c> arithmetic: every live particle's quad, ids descending; the count (more than the span: nothing written).</summary>
    int Draw(uint owner, int channel, Span<ParticleQuadRaw> quadsOut);

    /// <summary>The ids the last <see cref="Simulate"/> added and kept (<c>AddParticle</c> inside stock's step); none by default.</summary>
    int TakeSimulateSpawns(uint owner, int channel, Span<int> idsOut) => 0;
}

/// <summary>
/// <c>CParticleSystemQuery</c>'s answers, decided managed (lane c5-particles):
/// the five methods particles.a calls (C0 probe 1). The native thunk in
/// shell_managed_particles.cpp carries each out.
/// </summary>
/// <summary>
/// The old-style particle manager, as the native executors ask it (C7
/// particles phase 2.3; SourceSharp.Client.Effects.ClientParticleManager).
/// </summary>
public interface IClientParticleBindings
{
    int Frame(in ParticleFrameHeader header, ReadOnlySpan<ParticleFrameIn> inputs, float aabbBuffer, Span<ParticleCommit> commits);

    /// <summary>How many quads the executor's next draw lists (its binding's particles), 0 for none.</summary>
    int DrawQuadCount(uint owner, int channel);

    int DrawBegin(in ParticleDrawIn input, out ParticleDrawOut output, Span<ParticleQuadRaw> quads);

    void DrawEnd(uint owner, int channel, ReadOnlySpan<float> keys);

    void IncrementFrameCode();

    void PostRender();
}

/// <summary>
/// The new-style effects' manager, as the native shells ask it (C7 particles
/// phase 3; SourceSharp.Client.Effects.NewParticleManager).
/// </summary>
public interface IClientNewParticles
{
    int Frame(in NewParticleFrameHeader header, ReadOnlySpan<NewParticleIn> effects, Span<NewParticleCommit> commits);

    NewParticleDrawOutRaw Draw(in NewParticleDrawInRaw input);
}

public interface IClientParticleQuery
{
    /// <summary><c>GetCollisionGroupFromName</c>.</summary>
    int CollisionGroupFromName(string name);

    /// <summary><c>GetLightingAtPoint</c>: the tint's four bytes.</summary>
    (byte R, byte G, byte B, byte A) LightingAtPoint(Vector origin);

    /// <summary><c>TraceLine</c>: true to trace exactly as the library asked; false for the out-of-game miss.</summary>
    bool ShouldTrace();

    /// <summary><c>GetRandomPointsOnControllingObjectHitBox</c>; the spans the library did not ask for are empty.</summary>
    void RandomPointsOnControllingObjectHitBox(Vector controlPoint, bool hasObject, Span<Vector> points,
                                               Span<int> hitBoxIndices, Span<Vector> relativeCoords);

    /// <summary><c>GetControllingObjectHitBoxInfo</c>: the hit boxes written (none are).</summary>
    int ControllingObjectHitBoxInfo(bool hasObject);
}

/// <summary>
/// <see cref="IManagedParticlesClient"/>, managed-built: each body counts its
/// own crossing first and keeps its own catch, since the generated one would
/// answer 0, which is a count.
/// </summary>
public sealed unsafe class ParticlesClientBinding(Crossings crossings, ILog log, IClientParticleBindings bindings,
                                                  IClientParticleQuery query, IClientImpactSprays sprays,
                                                  IClientImpactSparks sparks, IClientWaterSplashes water,
                                                  IClientTracerGeometry tracers,
                                                  Func<bool, string, List<string>?>? manifestFiles = null,
                                                  IClientNewParticles? newEffects = null) : IManagedParticlesClient
{
    // C7 particles phase 3: the new-style effects' frame, once a frame; the
    // library steps the listed collections during it. One length-prefixed
    // buffer per direction, as Frame's.
    public int NewEffectsFrame(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientParticlesNewEffectsFrame);
        try
        {
            if (newEffects == null || a0 == null || a2 == null || a1 < BitBlock.HeaderBytes || a3 < BitBlock.HeaderBytes)
                return -1;
            var inBlock = new ReadOnlySpan<byte>(a0, a1);
            int inBytes = BitBlock.Bits(inBlock) / 8;
            int headerSize = Unsafe.SizeOf<NewParticleFrameHeader>();
            if (inBytes < headerSize || inBytes > a1 - BitBlock.HeaderBytes)
                return -1;
            ReadOnlySpan<byte> payload = BitBlock.Payload(inBlock)[..inBytes];
            NewParticleFrameHeader header = MemoryMarshal.Read<NewParticleFrameHeader>(payload);
            ReadOnlySpan<NewParticleIn> effects = MemoryMarshal.Cast<byte, NewParticleIn>(payload[headerSize..]);

            var outBlock = new Span<byte>(a2, a3);
            Span<NewParticleCommit> commits = MemoryMarshal.Cast<byte, NewParticleCommit>(BitBlock.Payload(outBlock));
            int n = newEffects.Frame(header, effects, commits);
            BitBlock.SetBits(outBlock, n * Unsafe.SizeOf<NewParticleCommit>() * 8);
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.NewEffectsFrame", e); return -1; }
    }

    // One CNewParticleEffect::DrawModel's decisions.
    public int NewEffectDraw(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientParticlesNewEffectDraw);
        try
        {
            if (newEffects == null || a0 == null || a1 == null)
                return -1;
            *(NewParticleDrawOutRaw*)a1 = newEffects.Draw(*(NewParticleDrawInRaw*)a0);
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.NewEffectDraw", e); return -1; }
    }

    // C9 KeyValues K1: the game's (bMap 0) or a map's (1) particle manifest,
    // read in C# (SourceSharp.Shared's ParticleManifestFiles, handed in as a
    // function); its file entries come back NUL-separated in one BitBlock.
    // Answers the entries, -1 for a map with no manifest, or -2 when the block
    // is too small (nothing written).
    public int ManifestFiles(int a0, byte* a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientParticlesManifestFiles);
        try
        {
            if (a2 == null || a3 < BitBlock.HeaderBytes) return -2;
            List<string>? files = manifestFiles?.Invoke(a0 != 0, a1 == null ? "" : new string((sbyte*)a1));
            if (files == null) return -1;

            var block = new Span<byte>(a2, a3);
            Span<byte> payload = BitBlock.Payload(block);
            int at = 0;
            foreach (string f in files)
            {
                int n = System.Text.Encoding.UTF8.GetByteCount(f);
                if (at + n + 1 > payload.Length) return -2;
                at += System.Text.Encoding.UTF8.GetBytes(f, payload[at..]);
                payload[at++] = 0;
            }
            BitBlock.SetBits(block, at * 8);
            return files.Count;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.ManifestFiles", e); return -2; }
    }

    // C7 particles phase 2.3: the old-style manager's crossings. Each list
    // crosses as ONE length-prefixed buffer per direction (BitBlock: an int32
    // bit count, then the bytes); a list's count is what its prefix leaves room for.

    public int Frame(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientParticlesFrame);
        try
        {
            if (a0 == null || a2 == null || a1 < BitBlock.HeaderBytes || a3 < BitBlock.HeaderBytes)
                return -1;
            var inBlock = new ReadOnlySpan<byte>(a0, a1);
            int inBytes = BitBlock.Bits(inBlock) / 8;
            int headerSize = Unsafe.SizeOf<ParticleFrameHeader>();
            if (inBytes < headerSize || inBytes > a1 - BitBlock.HeaderBytes)
                return -1;
            ReadOnlySpan<byte> payload = BitBlock.Payload(inBlock)[..inBytes];
            ParticleFrameHeader header = MemoryMarshal.Read<ParticleFrameHeader>(payload);
            ReadOnlySpan<ParticleFrameIn> inputs = MemoryMarshal.Cast<byte, ParticleFrameIn>(payload[headerSize..]);

            var outBlock = new Span<byte>(a2, a3);
            Span<ParticleCommit> commits = MemoryMarshal.Cast<byte, ParticleCommit>(BitBlock.Payload(outBlock));
            int n = bindings.Frame(header, inputs, header.AabbBuffer, commits);
            BitBlock.SetBits(outBlock, n * Unsafe.SizeOf<ParticleCommit>() * 8);
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.Frame", e); return -1; }
    }

    // The draw's decisions and quads: the bytes written (prefix included), or
    // more than a2 holds, before any decision, when it needs more room.
    public int DrawBegin(void* a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientParticlesDrawBegin);
        try
        {
            int headerSize = Unsafe.SizeOf<ParticleDrawOut>(), quadSize = Unsafe.SizeOf<ParticleQuadRaw>();
            if (a0 == null || a1 == null || a2 < BitBlock.HeaderBytes + headerSize)
                return -1;
            ref readonly ParticleDrawIn input = ref *(ParticleDrawIn*)a0;
            var block = new Span<byte>(a1, a2);
            Span<byte> payload = BitBlock.Payload(block);
            int capacity = (payload.Length - headerSize) / quadSize;
            int needed = bindings.DrawQuadCount(input.Owner, input.Channel);
            if (needed > capacity)
                return BitBlock.HeaderBytes + headerSize + needed * quadSize;

            Span<ParticleQuadRaw> quads = MemoryMarshal.Cast<byte, ParticleQuadRaw>(payload[headerSize..]);
            int n = bindings.DrawBegin(input, out ParticleDrawOut output, quads);
            MemoryMarshal.Write(payload, in output);
            int bytes = headerSize + Math.Max(n, 0) * quadSize;
            BitBlock.SetBits(block, bytes * 8);
            return BitBlock.HeaderBytes + bytes;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.DrawBegin", e); return -1; }
    }

    public int DrawEnd(uint a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientParticlesDrawEnd);
        try
        {
            if (a2 == null || a3 < BitBlock.HeaderBytes)
                return -1;
            var block = new ReadOnlySpan<byte>(a2, a3);
            int bytes = BitBlock.Bits(block) / 8;
            if (bytes < 0 || bytes > a3 - BitBlock.HeaderBytes)
                return -1;
            bindings.DrawEnd(a0, a1, MemoryMarshal.Cast<byte, float>(BitBlock.Payload(block)[..bytes]));
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.DrawEnd", e); return -1; }
    }

    public int FrameCode()
    {
        crossings.Count(CrossingOp.ClientParticlesFrameCode);
        try
        {
            bindings.IncrementFrameCode();
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.FrameCode", e); return -1; }
    }

    public int PostRender()
    {
        crossings.Count(CrossingOp.ClientParticlesPostRender);
        try
        {
            bindings.PostRender();
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.PostRender", e); return -1; }
    }

    // The query's answers. -1 means managed could not answer and native keeps
    // its refusal answer (the interface's own default); it is a count of
    // failures, never a group or a colour.

    public int QueryCollisionGroup(byte* a0)
    {
        crossings.Count(CrossingOp.ClientParticleQueryCollisionGroup);
        try
        {
            if (a0 == null) return -1;
            return query.CollisionGroupFromName(System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)a0) ?? "");
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.QueryCollisionGroup", e); return -1; }
    }

    public int QueryLighting(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientParticleQueryLighting);
        try
        {
            if (a0 == null || a1 == null) return -1;
            var (r, g, b, a) = query.LightingAtPoint(*(Vector*)a0);
            byte* rgba = (byte*)a1;
            rgba[0] = r; rgba[1] = g; rgba[2] = b; rgba[3] = a;
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.QueryLighting", e); return -1; }
    }

    public int QueryTrace(void* a0, void* a1, uint a2, int a3, int a4)
    {
        crossings.Count(CrossingOp.ClientParticleQueryTrace);
        try
        {
            return query.ShouldTrace() ? 1 : 0;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.QueryTrace", e); return -1; }
    }

    public int QueryHitBoxPoints(int a0, void* a1, int a2, int a3, void* a4, void* a5, void* a6)
    {
        crossings.Count(CrossingOp.ClientParticleQueryHitBoxPoints);
        try
        {
            if (a1 == null || a3 < 0 || (a3 > 0 && a4 == null)) return -1;
            query.RandomPointsOnControllingObjectHitBox(*(Vector*)a1, a2 != 0, new Span<Vector>(a4, a3),
                                                        a5 == null ? default : new Span<int>(a5, a3),
                                                        a6 == null ? default : new Span<Vector>(a6, a3));
            return a3;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.QueryHitBoxPoints", e); return -1; }
    }

    public int ImpactDebris(void* a0, void* a1, void* a2, int a3, int a4, int a5, float a6)
    {
        crossings.Count(CrossingOp.ClientParticlesImpactDebris);
        try
        {
            if (a0 == null || a1 == null || a2 == null) return -1;
            return sprays.Debris(*(Vector*)a0, *(Vector*)a1, *(Vector*)a2, a3, a4, a5 != 0, a6);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.ImpactDebris", e); return -1; }
    }

    public int ImpactDust(void* a0, void* a1, void* a2, float a3, float a4)
    {
        crossings.Count(CrossingOp.ClientParticlesImpactDust);
        try
        {
            if (a0 == null || a1 == null || a2 == null) return -1;
            return sprays.Dust(*(Vector*)a0, *(Vector*)a1, *(Vector*)a2, a3, a4);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.ImpactDust", e); return -1; }
    }

    public int ImpactMetalSpark(void* a0, void* a1, void* a2, int a3, float a4)
    {
        crossings.Count(CrossingOp.ClientParticlesImpactMetalSpark);
        try
        {
            if (a0 == null || a1 == null || a2 == null) return -1;
            return sparks.ImpactMetalSpark(*(Vector*)a0, *(Vector*)a1, *(Vector*)a2, a3, a4);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.ImpactMetalSpark", e); return -1; }
    }

    public int WaterSplash(int a0, void* a1, void* a2, float a3, void* a4, float a5, float a6)
    {
        crossings.Count(CrossingOp.ClientParticlesWaterSplash);
        try
        {
            if (a1 == null || a0 < 0 || a0 > 2 || (a0 < 2 && a2 == null)) return -1;
            water.Splash(a0, *(Vector*)a1, a2 == null ? default : *(Vector*)a2, a3, a4 == null ? null : *(Vector*)a4, a5, a6);
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.WaterSplash", e); return -1; }
    }

    public int SplashTint(void* a0, void* a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientParticlesSplashTint);
        try
        {
            if (a0 == null) return -1;
            var (tint, luminosity) = water.Tint(*(Vector*)a0);
            if (a1 != null) *(Vector*)a1 = tint;
            if (a2 != null) *(float*)a2 = luminosity;
            return 1;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.SplashTint", e); return -1; }
    }

    /// <summary>
    /// <c>Tracer_ComputeVerts</c> for a batch (the rain's tracers, one crossing
    /// a render): seven floats in per tracer (view-space start, delta, width),
    /// twelve out (the four corners in stock's order) and whether it drew.
    /// </summary>
    public int TracerQuads(int a0, void* a1, void* a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientParticlesTracerQuads);
        try
        {
            if (a0 < 0 || (a0 > 0 && (a1 == null || a2 == null || a3 == null))) return -1;
            var input = new ReadOnlySpan<float>(a1, a0 * 7);
            var verts = new Span<Vector>(a2, a0 * 4);
            var valid = new Span<byte>(a3, a0);
            for (int i = 0; i < a0; i++)
            {
                ReadOnlySpan<float> t = input.Slice(i * 7, 7);
                valid[i] = tracers.ComputeVerts(new Vector(t[0], t[1], t[2]), new Vector(t[3], t[4], t[5]), t[6],
                                                       verts.Slice(i * 4, 4)) ? (byte)1 : (byte)0;
            }
            return a0;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.TracerQuads", e); return -1; }
    }

    public int QueryHitBoxInfo(int a0, int a1, int a2)
    {
        crossings.Count(CrossingOp.ClientParticleQueryHitBoxInfo);
        try
        {
            return query.ControllingObjectHitBoxInfo(a1 != 0);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedParticlesClient.QueryHitBoxInfo", e); return -1; }
    }
}

/// <summary>The real one, over IShimParticlesClient::ParticlesTraceLine.</summary>
internal sealed unsafe class ClientPlaneTrace(IParticlesClientArea particlesClient) : IClientPlaneTrace
{
    public (float Fraction, Vector Normal, float PlaneDist) TraceLine(Vector start, Vector end, int mask)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* o = stackalloc float[5];
        return particlesClient.ParticlesTraceLine(s, e, mask, o, 5) < 5
            ? (1f, default, 0f)
            : (o[0], new Vector(o[1], o[2], o[3]), o[4]);
    }
}

/// <summary>The real one, over this side's IVModelInfoClient.</summary>
internal sealed class ClientModelNames(EngineSide side) : IClientModelNames
{
    public string? NameOf(int modelIndex)
    {
        var info = EngineBound.ModelInfo(side);
        nint model = info.Model(modelIndex);
        return model == 0 ? null : info.ModelName(model);
    }

    public int IndexOf(string name) => EngineBound.ModelInfo(side).ModelIndex(name);
}

/// <summary>The real one, over the api.</summary>
internal sealed unsafe class ClientParticleExecutor(IParticlesClientArea particlesClient) : INativeParticleExecutor
{
    public bool Create(EntityHandle owner, int channel, string material, float cullRadius, bool alwaysSimulate)
    {
        byte[] name = Vtable.Utf8(material);
        fixed (byte* p = name)
            return particlesClient.ParticlesCreate(owner.Value, channel, p, cullRadius, alwaysSimulate ? 1 : 0) != 0;
    }

    public bool CreateAt(int id, string material, Vector origin, float cullRadius, bool alwaysSimulate)
    {
        byte[] name = Vtable.Utf8(material);
        float* at = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        fixed (byte* p = name)
            return particlesClient.ParticlesCreateAt(id, p, at, cullRadius, alwaysSimulate ? 1 : 0) != 0;
    }

    public void Configure(EntityHandle owner, int channel, float cullRadius)
    {
        particlesClient.ParticlesConfigure(owner.Value, channel, cullRadius);
    }

    public void Destroy(EntityHandle owner, int channel)
    {
        particlesClient.ParticlesDestroy(owner.Value, channel);
    }

    public ParticleFollow SetSpace(EntityHandle owner, int channel, uint entity, int attachment, ParticleSpace flags)
    {
        ParticleFollow follow = default;
        particlesClient.ParticlesSetSpace(owner.Value, channel, entity, attachment, (int)flags, &follow);
        return follow;
    }

    public void SetLights(EntityHandle owner, int channel, ReadOnlySpan<float> lights)
    {
        fixed (float* p = lights)
            particlesClient.ParticlesSetLights(owner.Value, channel, p, lights.Length);
    }

    public bool MaterialVarInt(EntityHandle owner, int channel, string var, out int value)
    {
        value = 0;
        byte[] name = Vtable.Utf8(var);
        int v = 0;
        bool found;
        fixed (byte* p = name)
            found = particlesClient.ParticlesMaterialVarInt(owner.Value, channel, p, &v) != 0;
        value = v;
        return found;
    }
}

/// <summary>
/// The managed point_worldtext layouts, as the native text renderer asks for
/// them (shell_worldtext_render.cpp, lane c2-sims): by owner handle. -1 means
/// no layout answers for that owner.
/// </summary>
public interface IClientWorldTexts
{
    /// <summary>The draw's vertices (four per glyph); the count (more than the span: nothing past it written).</summary>
    int Draw(uint owner, in WorldTextView view, Span<WorldTextVertexRaw> verticesOut);
}

/// <summary>
/// <see cref="IManagedWorldTextClient"/>, managed-built: the body counts its own
/// crossing first and keeps its own catch.
/// </summary>
public sealed unsafe class WorldTextClientBinding(Crossings crossings, ILog log, IClientWorldTexts texts) : IManagedWorldTextClient
{
    public int Draw(uint a0, void* a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientWorldTextDraw);
        try
        {
            if (a1 == null || a3 < 0 || (a3 > 0 && a2 == null)) return -1;
            return texts.Draw(a0, in *(WorldTextView*)a1, new Span<WorldTextVertexRaw>(a2, a3));
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedWorldTextClient.Draw", e); return -1; }
    }
}

/// <summary>The real one, over IShimParticlesClient::WorldTextMeshUpdate/Destroy.</summary>
internal sealed unsafe class ClientWorldTextMeshes(IParticlesClientArea particlesClient) : IClientWorldTextMeshes
{
    public bool Update(EntityHandle owner, string material, Vector mins, Vector maxs)
    {
        byte[] name = Vtable.Utf8(material);
        float* bounds = stackalloc float[6] { mins.X, mins.Y, mins.Z, maxs.X, maxs.Y, maxs.Z };
        fixed (byte* p = name)
            return particlesClient.WorldTextMeshUpdate(owner.Value, p, bounds, 6) != 0;
    }

    public void Destroy(EntityHandle owner)
    {
        particlesClient.WorldTextMeshDestroy(owner.Value);
    }
}

/// <summary>The real one, over IShimEffectsRenderClient::WindPublish.</summary>
internal sealed unsafe class ClientWindPublisher(IEffectsRenderClientArea effectsRenderClient) : IClientWindPublisher
{
    public void Publish(Vector wind, Vector? sway, int winds)
    {
        float* w = stackalloc float[3] { wind.X, wind.Y, wind.Z };
        if (sway is { } v)
        {
            float* sw = stackalloc float[3] { v.X, v.Y, v.Z };
            effectsRenderClient.WindPublish(w, sw, winds);
        }
        else
            effectsRenderClient.WindPublish(w, null, winds);
    }
}
