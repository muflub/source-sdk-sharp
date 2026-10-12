using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The two engine answers a sprite's glow blend reads, asked while the sprite
/// is being planned (IShimFxClient's FxSpriteGlowFraction and
/// FxGlowSightDistance over the renderer native left for the plan).
/// </summary>
public interface IFxSpriteGlow
{
    /// <summary><c>PixelVisibility_FractionVisible</c> on the planned sprite's query handle.</summary>
    float FractionVisible(in Vector position, float proxySize, float proxyAspect);

    /// <summary><c>GlowSightDistance( position, false )</c>.</summary>
    float SightDistance(in Vector position);
}

/// <summary>The real answers, over the slots; a missing shim answers "not visible".</summary>
internal sealed unsafe class ClientFxSpriteGlowSlots(EngineSide api) : IFxSpriteGlow
{
    public float FractionVisible(in Vector position, float proxySize, float proxyAspect)
    {
        if (!api.ClientAttached)
            return 0;
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        return api.ClientShims.FxSpriteGlowFraction(p, proxySize, proxyAspect);
    }

    public float SightDistance(in Vector position)
    {
        if (!api.ClientAttached)
            return 0;
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        return api.ClientShims.FxGlowSightDistance(p);
    }
}

/// <summary>What native's sprite plan reaches (IManagedFxClient::SpritePlan's body).</summary>
public interface IClientSpriteHost
{
    /// <summary>
    /// One <c>DrawSprite</c>: the request's floats in, the plan's out
    /// (shell_sprite_leaf.cpp's SS_SPRITE_IN_* / SS_SPRITE_OUT_*); how many
    /// were written, or -1 when the request is not one.
    /// </summary>
    int Plan(ReadOnlySpan<float> input, Span<float> output);
}

/// <summary>
/// A managed sprite's draw in two halves (IShimFxClient's FxSpriteDrawBegin and
/// FxSpriteDrawEnd), so the plan between them is made here without a crossing
/// back: <see cref="Begin"/> fills the request (SS_SPRITE_IN_*), answers false
/// when there is nothing to draw; <see cref="End"/> carries the plan out (an
/// empty span drops the pending draw) and answers whether the sprite drew.
/// </summary>
public interface IFxSpriteDraw
{
    bool Begin(EntityHandle owner, in Vector offset, int modelIndex, float frame, int renderMode, int renderFx,
               int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
               float glowProxySize, Span<float> input);

    int End(ReadOnlySpan<float> output);

    /// <summary>
    /// The batched path (C7 item 7): the plan kept against its sprite instead of
    /// drawn (IShimFxClient's FxSpriteDrawStore), carried out at the sprite's
    /// DrawModel. False when nothing was pending or the plan is empty.
    /// </summary>
    bool Store(ReadOnlySpan<float> output);
}

internal sealed unsafe class ClientFxSpriteDrawSlots(EngineSide api) : IFxSpriteDraw
{
    public bool Begin(EntityHandle owner, in Vector offset, int modelIndex, float frame, int renderMode, int renderFx,
                      int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                      float glowProxySize, Span<float> input)
    {
        if (!api.ClientAttached || input.Length < ClientSpriteRequest.InFloats)
            return false;
        float* o = stackalloc float[] { offset.X, offset.Y, offset.Z };
        fixed (float* p = input)
            return api.ClientShims.FxSpriteDrawBegin(owner.Value, o, modelIndex, frame, renderMode, renderFx, r, g, b, a,
                                                scale, worldSpaceScale ? 1 : 0, hdrColorScale, glowProxySize, p)
                   == ClientSpriteRequest.InFloats;
    }

    public int End(ReadOnlySpan<float> output)
    {
        if (!api.ClientAttached)
            return 0;
        if (output.Length < ClientSpriteRequest.OutFloats)
            return api.ClientShims.FxSpriteDrawEnd(null);
        fixed (float* p = output)
            return api.ClientShims.FxSpriteDrawEnd(p);
    }

    public bool Store(ReadOnlySpan<float> output)
    {
        if (!api.ClientAttached)
            return false;
        if (output.IsEmpty)
            return api.ClientShims.FxSpriteDrawStore(null) != 0;
        fixed (float* p = output)
            return api.ClientShims.FxSpriteDrawStore(p) != 0;
    }
}

/// <summary>The request's and the plan's float counts (SS_SPRITE_IN_FLOATS, SS_SPRITE_OUT_FLOATS).</summary>
public static class ClientSpriteRequest
{
    public const int InFloats = 38;
    public const int OutFloats = 22;
}

/// <summary>What native's sprite-trail renderable reaches (IManagedFxClient's SpriteTrailTick / SpriteTrailDraw).</summary>
public interface IClientSpriteTrailHost
{
    /// <summary>A frame's tick at the followed origin: the box about it, false for a trail this side does not know.</summary>
    bool Tick(EntityHandle owner, float curTime, in Vector origin, out Vector mins, out Vector maxs);

    /// <summary>The draw: how many segments went to the strip.</summary>
    int Draw(EntityHandle owner, float curTime, in Vector origin);
}

/// <summary>
/// The client effect precache registry native keeps (every CLIENTEFFECT_REGISTER
/// object adds itself at static init) and the three level-time calls on it
/// (IShimFxClient's FxPrecacheCount .. FxCacheMaterialHandles).
/// </summary>
public interface IFxPrecache
{
    /// <summary>How many registrations there are.</summary>
    int Count();

    /// <summary><c>IClientEffect::Cache( precache )</c> on registration <paramref name="index"/>.</summary>
    void Cache(int index, bool precache);

    /// <summary><c>materials-&gt;CacheUsedMaterials()</c>.</summary>
    void CacheUsedMaterials();

    /// <summary><c>FX_CacheMaterialHandles()</c>: the impact effects' particle material handles.</summary>
    void CacheMaterialHandles();
}

/// <summary>The real registry, over the slots; a missing shim has no registrations.</summary>
internal sealed class ClientFxPrecacheSlots(EngineSide api) : IFxPrecache
{
    public int Count() => api.ClientAttached ? api.ClientShims.FxPrecacheCount() : 0;

    public void Cache(int index, bool precache)
    {
        if (api.ClientAttached)
            api.ClientShims.FxPrecacheCache(index, precache ? 1 : 0);
    }

    public void CacheUsedMaterials()
    {
        if (api.ClientAttached)
            api.ClientShims.FxCacheUsedMaterials();
    }

    public void CacheMaterialHandles()
    {
        if (api.ClientAttached)
            api.ClientShims.FxCacheMaterialHandles();
    }
}

/// <summary>What native's precache system reaches (IManagedFxClient::EffectsPrecache).</summary>
public interface IClientEffectPrecacheHost
{
    /// <summary>LevelInitPreEntity: precache every registration, then the two caches.</summary>
    void LevelInit();

    /// <summary>LevelShutdownPostEntity: release every registration.</summary>
    void LevelShutdown();
}

/// <summary>What native's smoke fog overlay reaches (IManagedFxClient::SmokeFogPlan).</summary>
public interface IClientSmokeFogHost
{
    /// <summary>
    /// One <c>DrawSmokeFogOverlay</c>: whether the quad is drawn, and with what.
    /// <paramref name="color"/> is the overlay's global colour, which stock
    /// overwrites on a draw.
    /// </summary>
    bool Plan(float alpha, bool hasMaterial, ref Vector color, out Vector drawColor, out float drawAlpha);
}

/// <summary>A sound script's parameters as the engine resolved them (CSoundParameters): the wave it picked included.</summary>
public readonly record struct GameSoundParams(string Wave, float Volume, int Pitch, int PitchLow, int PitchHigh, int SoundLevel, int Channel);

/// <summary>The client's sound scripts: resolve one, and play what was resolved at a place for the local player.</summary>
public interface IFxGameSounds
{
    /// <summary><c>GameSound_GetParameters</c>; false when the script is unknown.</summary>
    bool Parameters(string script, out GameSoundParams parameters);

    /// <summary><c>GameSound_Emit</c> to the local player, from the world, at <paramref name="origin"/>.</summary>
    void EmitAtPosition(in GameSoundParams parameters, float volume, int pitch, in Vector origin);
}
