using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The two answers a sprite's glow blend reads, asked while the sprite is
/// being planned: the managed pixel visibility's, on the handle the request
/// carries from the renderer (SS_SPRITE_IN_QUERY_HANDLE), which the plan hands
/// back for the renderer to keep (SS_SPRITE_OUT_QUERY_HANDLE).
/// </summary>
public interface IFxSpriteGlow
{
    /// <summary><c>PixelVisibility_FractionVisible</c> on the planned sprite's query handle, which the renderer keeps.</summary>
    float FractionVisible(in Vector position, float proxySize, float proxyAspect, ref int queryHandle);

    /// <summary><c>GlowSightDistance( position, false )</c>.</summary>
    float SightDistance(in Vector position);
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

/// <summary>A sound script's parameters as the engine resolved them (CSoundParameters): the wave it picked included.</summary>
public readonly record struct GameSoundParams(string Wave, float Volume, int Pitch, int PitchLow, int PitchHigh, int SoundLevel, int Channel);

/// <summary>The client's sound scripts: resolve one, and play what was resolved at a place for the local player.</summary>
public interface IFxGameSounds
{
    /// <summary><c>GameSound_GetParameters</c>; false when the script is unknown.</summary>
    bool Parameters(string script, out GameSoundParams parameters);

    /// <summary><c>GameSound_Emit</c> to the local player, from the world, at <paramref name="origin"/>.</summary>
    void EmitAtPosition(in GameSoundParams parameters, float volume, int pitch, in Vector origin);

    /// <summary>
    /// <c>C_BaseEntity::EmitSound( CLocalPlayerFilter, SOUND_FROM_WORLD, script, &amp;origin )</c>:
    /// the script by name, resolved and traced as a script.
    /// </summary>
    void EmitScript(string script, in Vector origin);
}
