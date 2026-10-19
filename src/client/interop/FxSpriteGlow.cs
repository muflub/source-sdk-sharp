using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real answers, over the slots; a missing shim answers "not visible".</summary>
internal sealed unsafe class ClientFxSpriteGlowSlots(FxClientArea fxClient) : IFxSpriteGlow
{
    public float FractionVisible(in Vector position, float proxySize, float proxyAspect)
    {
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        return fxClient.FxSpriteGlowFraction(p, proxySize, proxyAspect);
    }

    public float SightDistance(in Vector position)
    {
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        return fxClient.FxGlowSightDistance(p);
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

internal sealed unsafe class ClientFxSpriteDrawSlots(FxClientArea fxClient) : IFxSpriteDraw
{
    public bool Begin(EntityHandle owner, in Vector offset, int modelIndex, float frame, int renderMode, int renderFx,
                      int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                      float glowProxySize, Span<float> input)
    {
        if (input.Length < ClientSpriteRequest.InFloats)
            return false;
        float* o = stackalloc float[] { offset.X, offset.Y, offset.Z };
        fixed (float* p = input)
            return fxClient.FxSpriteDrawBegin(owner.Value, o, modelIndex, frame, renderMode, renderFx, r, g, b, a,
                                                scale, worldSpaceScale ? 1 : 0, hdrColorScale, glowProxySize, p)
                   == ClientSpriteRequest.InFloats;
    }

    public int End(ReadOnlySpan<float> output)
    {
        if (output.Length < ClientSpriteRequest.OutFloats)
            return fxClient.FxSpriteDrawEnd(null);
        fixed (float* p = output)
            return fxClient.FxSpriteDrawEnd(p);
    }

    public bool Store(ReadOnlySpan<float> output)
    {
        if (output.IsEmpty)
            return fxClient.FxSpriteDrawStore(null) != 0;
        fixed (float* p = output)
            return fxClient.FxSpriteDrawStore(p) != 0;
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

/// <summary>The real registry, over the slots; a missing shim has no registrations.</summary>
internal sealed class ClientFxPrecacheSlots(FxClientArea fxClient) : IFxPrecache
{
    public int Count() => fxClient.FxPrecacheCount();

    public void Cache(int index, bool precache)
    {
        fxClient.FxPrecacheCache(index, precache ? 1 : 0);
    }

    public void CacheUsedMaterials()
    {
        fxClient.FxCacheUsedMaterials();
    }

    public void CacheMaterialHandles()
    {
        fxClient.FxCacheMaterialHandles();
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
