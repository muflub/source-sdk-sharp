using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// Where a dispatched effect goes once its name is known: the client's
/// dispatch-by-name table (plan_client_novalve §2 cluster 4). Implemented by the
/// game assembly's <c>ClientEffectDispatch</c>; the interop side only carries
/// a native dispatch to it.
/// </summary>
public interface IClientEffectDispatch
{
    /// <summary>
    /// Runs the registration <paramref name="name"/> resolves to, or counts it
    /// unclaimed. Stock's <c>DispatchEffectToCallback</c>.
    /// </summary>
    void Dispatch(string name, in EffectData data);
}

/// <summary>
/// The client's <c>IEffects</c> methods that decide something (plan_client_novalve
/// §2 cluster 3), as the native thunk carries them. Implemented by the game
/// assembly's <c>ClientEffects</c>.
/// </summary>
public interface IClientEffects
{
    void Smoke(Vector origin, int modelIndex, float scale, float framerate);
    void Sparks(Vector position, int magnitude, int trailLength, Vector? direction);
    void Dust(Vector origin, Vector direction, float size, float speed);
    void MuzzleFlash(Vector origin, QAngle angles, float scale, int type);
    void MetalSparks(Vector position, Vector direction);
    void EnergySplash(Vector position, Vector direction, bool explosive);
    void Ricochet(Vector position, Vector direction);
    void SuppressEffectsSounds(bool suppress);
}

/// <summary>
/// The two effect services a native call reaches, as the module holds them:
/// null before the module has built them (a call then does nothing, as stock's
/// would with nothing registered).
/// </summary>
public interface IClientFxServices
{
    IClientEffectDispatch? Dispatch { get; }

    IClientEffects? Effects { get; }

    IClientEffectListHost? EffectList { get; }

    /// <summary>The sprite renderer's planner (SpriteRenderer).</summary>
    IClientSpriteHost? Sprites { get; }

    /// <summary>The env_spritetrail ribbons (ClientSpriteTrails).</summary>
    IClientSpriteTrailHost? SpriteTrails { get; }

    /// <summary>The client effect precache system's level decisions (ClientEffectPrecache).</summary>
    IClientEffectPrecacheHost? Precache { get; }

    /// <summary>The smoke fog overlay's decision (SmokeFogOverlay).</summary>
    IClientSmokeFogHost? SmokeFog { get; }

    /// <summary>The legacy tempents' decisions (ClientTempEntDecisions).</summary>
    IClientTempEntHost? TempEnts { get; }
}

/// <summary>
/// The client-side effect list as native reaches it (plan_client_novalve §2
/// cluster 3): the view's per-frame draw, the client DLL's flush, the level
/// start, and the <c>FX_Add*</c>/<c>FX_TracerSound</c> entries the fork's effect
/// TUs still call. Implemented by the game assembly over ClientEffectList and
/// ClientSideEffectFactory.
/// </summary>
public interface IClientEffectListHost
{
    void DrawEffects(float frametime);
    void Flush();
    void LevelInit();

    /// <summary><c>FX_AddLine</c>: start, end, their velocities, alpha and scale ramps, die time (17 floats).</summary>
    void AddLine(ReadOnlySpan<float> data, int material);

    /// <summary><c>FX_AddQuad( FXQuadData_t )</c> (19 floats); the material's reference is the caller's.</summary>
    void AddQuad(ReadOnlySpan<float> data, uint flags, int material);

    /// <summary><c>FX_AddQuad( origin, normal, ... )</c> (18 floats), the material by name.</summary>
    void AddQuadNamed(ReadOnlySpan<float> data, uint flags, string material);

    void AddStaticLine(Vector start, Vector end, float scale, float life, string material, int flags);
    void AddDiscreetLine(Vector start, Vector direction, float velocity, float length, float clip, float scale, float life, string material);
    void TracerSound(Vector start, Vector end, int type);
}

/// <summary>
/// The client's effects, managed-built: a native caller's dispatch
/// (<c>DispatchEffectToCallback</c>, shell_effect_dispatch_client.cpp) reaching
/// the managed table, and the IEffects thunk (shell_effects_client.cpp)
/// reaching ClientEffects' decisions. The generated thunk has already made the
/// client the active side and catches what a body throws.
/// </summary>
public sealed unsafe class FxClientBinding(Crossings crossings, IClientFxServices services) : IManagedFxClient
{
    private static Vector V(void* p) => new(((float*)p)[0], ((float*)p)[1], ((float*)p)[2]);

    private static QAngle A(void* p) => new(((float*)p)[0], ((float*)p)[1], ((float*)p)[2]);

    public void DispatchEffect(byte* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientDispatchEffectIn);
        if (a0 is null || a1 is null)
            return;

        string name = new((sbyte*)a0);
        services.Dispatch?.Dispatch(name, in *(EffectData*)a1);
    }

    public void Smoke(void* a0, int a1, float a2, float a3)
    {
        crossings.Count(CrossingOp.ClientFxSmoke);
        if (a0 is not null)
            services.Effects?.Smoke(V(a0), a1, a2, a3);
    }

    public void Sparks(void* a0, int a1, int a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientFxSparks);
        if (a0 is not null)
            services.Effects?.Sparks(V(a0), a1, a2, a3 is null ? null : V(a3));
    }

    public void Dust(void* a0, void* a1, float a2, float a3)
    {
        crossings.Count(CrossingOp.ClientFxDust);
        if (a0 is not null && a1 is not null)
            services.Effects?.Dust(V(a0), V(a1), a2, a3);
    }

    public void MuzzleFlash(void* a0, void* a1, float a2, int a3)
    {
        crossings.Count(CrossingOp.ClientFxMuzzleFlash);
        if (a0 is not null && a1 is not null)
            services.Effects?.MuzzleFlash(V(a0), A(a1), a2, a3);
    }

    public void MetalSparks(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientFxMetalSparks);
        if (a0 is not null && a1 is not null)
            services.Effects?.MetalSparks(V(a0), V(a1));
    }

    public void EnergySplash(void* a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientFxEnergySplash);
        if (a0 is not null && a1 is not null)
            services.Effects?.EnergySplash(V(a0), V(a1), a2 != 0);
    }

    public void Ricochet(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientFxRicochet);
        if (a0 is not null && a1 is not null)
            services.Effects?.Ricochet(V(a0), V(a1));
    }

    public void SuppressEffectsSounds(int a0)
    {
        crossings.Count(CrossingOp.ClientFxSuppressEffectsSounds);
        services.Effects?.SuppressEffectsSounds(a0 != 0);
    }

    public void DrawEffects(float a0)
    {
        crossings.Count(CrossingOp.ClientFxDrawEffects);
        services.EffectList?.DrawEffects(a0);
    }

    public void FlushEffects()
    {
        crossings.Count(CrossingOp.ClientFxFlushEffects);
        services.EffectList?.Flush();
    }

    public void EffectsLevelInit()
    {
        crossings.Count(CrossingOp.ClientFxEffectsLevelInit);
        services.EffectList?.LevelInit();
    }

    public void AddLine(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientFxAddLine);
        if (a0 is not null)
            services.EffectList?.AddLine(new ReadOnlySpan<float>(a0, 17), a1);
    }

    public void AddQuad(void* a0, uint a1, int a2)
    {
        crossings.Count(CrossingOp.ClientFxAddQuad);
        if (a0 is not null)
            services.EffectList?.AddQuad(new ReadOnlySpan<float>(a0, 19), a1, a2);
    }

    public void AddQuadNamed(void* a0, uint a1, byte* a2)
    {
        crossings.Count(CrossingOp.ClientFxAddQuadNamed);
        if (a0 is not null && a2 is not null)
            services.EffectList?.AddQuadNamed(new ReadOnlySpan<float>(a0, 18), a1, new string((sbyte*)a2));
    }

    public void AddStaticLine(void* a0, void* a1, float a2, float a3, byte* a4, int a5)
    {
        crossings.Count(CrossingOp.ClientFxAddStaticLine);
        if (a0 is not null && a1 is not null && a4 is not null)
            services.EffectList?.AddStaticLine(V(a0), V(a1), a2, a3, new string((sbyte*)a4), a5);
    }

    public void AddDiscreetLine(void* a0, void* a1, float a2, float a3, float a4, float a5, float a6, byte* a7)
    {
        crossings.Count(CrossingOp.ClientFxAddDiscreetLine);
        if (a0 is not null && a1 is not null && a7 is not null)
            services.EffectList?.AddDiscreetLine(V(a0), V(a1), a2, a3, a4, a5, a6, new string((sbyte*)a7));
    }

    public void TracerSound(void* a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientFxTracerSound);
        if (a0 is not null && a1 is not null)
            services.EffectList?.TracerSound(V(a0), V(a1), a2);
    }

    public int SpritePlan(void* a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientFxSpritePlan);
        if (a0 is null || a2 is null || a1 < 0 || a3 < 0)
            return -1;
        return services.Sprites?.Plan(new ReadOnlySpan<float>(a0, a1), new Span<float>(a2, a3)) ?? -1;
    }

    public int SpriteTrailTick(uint a0, float a1, void* a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientFxSpriteTrailTick);
        if (a2 is null || a3 is null || services.SpriteTrails is not { } trails)
            return 0;
        if (!trails.Tick(new EntityHandle(a0), a1, V(a2), out Vector mins, out Vector maxs))
            return 0;
        float* box = (float*)a3;
        box[0] = mins.X; box[1] = mins.Y; box[2] = mins.Z;
        box[3] = maxs.X; box[4] = maxs.Y; box[5] = maxs.Z;
        return 1;
    }

    public int SpriteTrailDraw(uint a0, float a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientFxSpriteTrailDraw);
        if (a2 is null)
            return 0;
        return services.SpriteTrails?.Draw(new EntityHandle(a0), a1, V(a2)) ?? 0;
    }

    /// <summary>The precache system's level start (0) and level end (1).</summary>
    public void EffectsPrecache(int a0)
    {
        crossings.Count(CrossingOp.ClientFxEffectsPrecache);
        if (a0 == 0)
            services.Precache?.LevelInit();
        else if (a0 == 1)
            services.Precache?.LevelShutdown();
    }

    public int TempEntIsTransparent(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientFxTempEntIsTransparent);
        return services.TempEnts is { } t ? (t.IsTransparent(a0 != 0, a1) ? 1 : 0) : a0;
    }

    public int TempEntRenderGroup(int a0, int a1, int a2, int a3)
    {
        crossings.Count(CrossingOp.ClientFxTempEntRenderGroup);
        return services.TempEnts?.RenderGroup(a0, a1, a2 != 0, a3 != 0) ?? 7;
    }

    public void TempEntBlend(void* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientFxTempEntBlend);
        if (a0 is not null && a1 is not null)
            services.TempEnts?.Blend(new ReadOnlySpan<float>(a0, 14), new Span<int>((int*)a1, 2));
    }

    public int TempEntOccludedAlpha(int a0, int a1, int a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientFxTempEntOccludedAlpha);
        if (a3 is null || services.TempEnts is not { } t)
            return a1;
        float* f = (float*)a3;
        return t.OccludedAlpha(a0, a1, a2, new Vector(f[0], f[1], f[2]), new Vector(f[3], f[4], f[5]), new Vector(f[6], f[7], f[8]));
    }

    public int TempEntLightingOrigin(int a0)
    {
        crossings.Count(CrossingOp.ClientFxTempEntLightingOrigin);
        return services.TempEnts?.LightingOrigin(a0) ?? -1;
    }

    public void TempEntMuzzleFlash(int a0, uint a1, int a2, int a3, int a4)
    {
        crossings.Count(CrossingOp.ClientFxTempEntMuzzleFlash);
        services.TempEnts?.MuzzleFlash(a0, a1, a2, a3 != 0, a4 != 0);
    }

    public void TempEntEjectBrass(void* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientFxTempEntEjectBrass);
        if (a0 is null)
            return;
        float* f = (float*)a0;
        services.TempEnts?.EjectBrass(new Vector(f[0], f[1], f[2]), new QAngle(f[3], f[4], f[5]), new QAngle(f[6], f[7], f[8]), a1);
    }

    public int TempEntAttach(int a0, int a1, float a2, float a3, int a4)
    {
        crossings.Count(CrossingOp.ClientFxTempEntAttach);
        return services.TempEnts?.Attach(a0, a1, a2, a3, a4) ?? -1;
    }

    public void TempEntsLevelInit()
    {
        crossings.Count(CrossingOp.ClientFxTempEntsLevelInit);
        services.TempEnts?.LevelInit();
    }

    public int SmokeFogPlan(float a0, int a1, void* a2, void* a3)
    {
        crossings.Count(CrossingOp.ClientFxSmokeFogPlan);
        if (a2 is null || a3 is null || services.SmokeFog is not { } fog)
            return 0;
        float* color = (float*)a2;
        var c = new Vector(color[0], color[1], color[2]);
        if (!fog.Plan(a0, a1 != 0, ref c, out Vector draw, out float alpha))
            return 0;
        color[0] = c.X; color[1] = c.Y; color[2] = c.Z;
        float* rgba = (float*)a3;
        rgba[0] = draw.X; rgba[1] = draw.Y; rgba[2] = draw.Z; rgba[3] = alpha;
        return 1;
    }
}
