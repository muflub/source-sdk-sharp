// The world around an entity: what is there, what it sounds like, and what it
// looks like.

namespace SourceSharp.Abi;

/// <summary>
/// Asking the world what is there: traces, volumes and areas.
///
/// <para>
/// The only area with no SUBJECT. Everything else in the entity API is about a
/// particular entity; these ask about a place, and the answer is whatever happens
/// to be in it.
/// </para>
///
/// <para>
/// <see cref="AngleVectorsFromForward"/> is the exception and is here because it
/// has nowhere better: it asks about no place at all, it is pure geometry. It is
/// an engine call, so it belongs on an area rather than nowhere.
/// </para>
/// </summary>
public interface IEngineWorldQuery
{
    /// <inheritdoc cref="EngineSide.AngleVectorsFromForward"/>
    (Vector Right, Vector Up) AngleVectorsFromForward(Vector forward);

    /// <inheritdoc cref="EngineSide.EntitiesInSphere"/>
    int EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into);

    /// <summary>
    /// Which visleaf area a point is in.
    ///
    /// <para>
    /// 255 is the engine's own "nowhere", and it is passed straight through
    /// rather than softened: further down the pipe that exact number is what
    /// says a player has no 3D skybox at all.
    /// </para>
    /// </summary>
    int GetArea(Vector origin);

    /// <inheritdoc cref="EngineSide.TraceHull"/>
    HullResult TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, EntityHandle ignore = default);

    /// <inheritdoc cref="EngineSide.EntityTraceHull"/>
    EntityHullTrace EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs,
                                    uint mask, int collisionGroup);

    /// <inheritdoc cref="EngineSide.TraceLine"/>
    TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default);

    /// <inheritdoc cref="EngineSide.TraceLineSurface"/>
    SurfaceTraceResult TraceLineSurface(Vector start, Vector end, TraceMask mask,
                                        EntityHandle ignore = default);

    /// <inheritdoc cref="EngineSide.ClientTraceLine"/>
    ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask);

    /// <summary>
    /// The same trace, choosing which entities it may hit — and, for
    /// <see cref="TraceFilter.Simple"/>, one to pass through.
    ///
    /// <para>
    /// <paramref name="passEntityIndex"/> is an INDEX and never a handle, for
    /// the reason behaviour-ledger row C8 gives: a client handle read back
    /// through <c>CBaseHandle::Get()</c> is an unfiltered reinterpret of a
    /// handle table that holds managed entities. -1 passes nothing through,
    /// which is NOT the same as <see cref="TraceFilter.HitAll"/> — a simple
    /// filter with no pass entity still applies the collision group and the
    /// game rules.
    /// </para>
    /// </summary>
    ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask,
                                      TraceFilter filter, int passEntityIndex = -1);

    /// <inheritdoc cref="EngineSide.ClientEntityModelType"/>
    ModelType? ClientEntityModelType(int entIndex);
    /// <summary>
    /// A CLIENT entity's MODEL bounds and its angles, BY INDEX. False when the
    /// index names no entity, or names one that has no model.
    ///
    /// <para>
    /// <b>THE MODEL'S BOUNDS, NOT THE COLLISION BOX</b> —
    /// <see cref="IEngineEntities.GetBounds"/> is the collideable's rectangle
    /// and a different one. See <see cref="EngineSide.ClientEntityModelBounds"/>;
    /// behaviour ledger row C6 is what the distinction cost.
    /// </para>
    ///
    /// <para>
    /// Here rather than on <c>IEngineEffects</c> beside
    /// <c>ClientEntityOrigin</c>, which is its nearest neighbour on the ABI: it
    /// ASKS rather than draws, and it asks about this side's copy of the world,
    /// which is what this area is. <see cref="ClientTraceLine"/> is here for
    /// the same reason.
    /// </para>
    /// </summary>
    bool ClientEntityModelBounds(int entityIndex, out Vector mins, out Vector maxs,
                                 out Vector angles);

    /// <inheritdoc cref="EngineSide.PointContents"/>
    Contents PointContents(Vector origin);

    /// <inheritdoc cref="EngineSide.TracePlayerAim"/>
    EntityHandle TracePlayerAim(EntityHandle player, float range, out Vector hitPoint);

    /// <inheritdoc cref="EngineSide.WaterSurfaceBelow"/>
    WaterBelow WaterSurfaceBelow(Vector start);

    /// <inheritdoc cref="EngineSide.WorldChapterTitle"/>
    string WorldChapterTitle();

    /// <inheritdoc cref="EngineSide.EntityModelSurfaceProp"/>
    int EntityModelSurfaceProp(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.SurfacePropIndex"/>
    int SurfacePropIndex(string name);

    /// <inheritdoc cref="EngineSide.SurfacePropGameMaterial"/>
    SurfaceMaterial SurfacePropGameMaterial(int surfaceProp);
}

/// <summary>
/// Making sounds, and the two systems that decide what is heard: soundscapes, and
/// the sounds an AI can hear.
///
/// <para>
/// Which soundscape reaches a player is a decision and is managed —
/// <c>ISoundscapes</c> owns it. What is here is the engine's half: telling a
/// player what they are now hearing.
/// </para>
/// </summary>
public interface IEngineSound
{
    /// <inheritdoc cref="EngineSide.EmitAmbientSound"/>
    void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch);

    /// <inheritdoc cref="EngineSide.EmitWorldSoundScript"/>
    bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume);

    /// <inheritdoc cref="EngineSide.EmitSoundAtPosition"/>
    void EmitSoundAtPosition(string soundName, Vector origin);

    /// <inheritdoc cref="EngineSide.EntityEmitSound"/>
    void EntityEmitSound(EntityHandle entity, string soundName);

    /// <inheritdoc cref="EngineSide.ClientEmitSound"/>
    void ClientEmitSound(EntityHandle entity, string soundName, Vector? origin = null);

    /// <inheritdoc cref="EngineSide.EntityStopSound"/>
    void EntityStopSound(EntityHandle entity, string soundName);


    // plan_gamedll_after.md Phase A3, second pair -- the four things only the
    // shim can do to a sound patch. THESE belong on this area where the two
    // below did not: a recipient filter, an entity's network index and an
    // EmitSound_t are things only the engine can answer.

    /// <inheritdoc cref="EngineSide.SoundPatchEmit"/>
    void SoundPatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption);

    /// <inheritdoc cref="EngineSide.SoundPatchStop"/>
    void SoundPatchStop(int patch);

    /// <inheritdoc cref="EngineSide.SoundPatchState"/>
    void SoundPatchState(int patch, out int entIndex, out bool filterActive);

    /// <inheritdoc cref="EngineSide.SoundPatchFree"/>
    void SoundPatchFree(int patch);

    // Physics stock parity D2: what a managed looping sound needs that only the
    // engine can answer.

    /// <inheritdoc cref="EngineSide.SoundScriptParameters"/>
    SoundScriptParams? SoundScriptParameters(string soundName);

    /// <inheritdoc cref="EngineSide.SoundPatchCreate"/>
    int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel);

    /// <inheritdoc cref="EngineSide.SurfaceScrapeSound"/>
    string? SurfaceScrapeSound(int surfaceProps, bool smooth);

    // SoundEntInsert AND SoundEntQuery WERE HERE, and this area's own test is
    // why they are not: "native is what the ENGINE owns -- an interface it
    // exposes, or a callback it calls into". CSoundEnt was a fixed array of 128
    // records in a game-DLL file, swept by a game-DLL think. It is
    // IServerSoundEnt in SourceSharp.Server now (plan_gamedll_after.md Phase A3,
    // second pair) and its two callers resolve that instead.
}

/// <summary>
/// The small effects: sparks, splashes, blood, beams, smoke and the rest.
///
/// <para>
/// Every one is a temp entity written straight to the wire with no server-side
/// entity behind it. That is what makes them one area despite looking like a list
/// of unrelated things — they share a lifetime, which is none.
/// </para>
/// </summary>
public interface IEngineEffects
{

    /// <inheritdoc cref="EngineSide.BloodEffect"/>
    void BloodEffect(Vector origin, Vector direction, BloodColor color, float amount, bool stream = false, BloodSpray sprayFlags = BloodSpray.None);

    /// <inheritdoc cref="EngineSide.BubbleEffect"/>
    void BubbleEffect(EntityHandle entity, int count, float speed);

    /// <inheritdoc cref="EngineSide.TempEntRegister"/>
    int TempEntRegister(TeClass description);

    /// <inheritdoc cref="EngineSide.TempEntPlayback"/>
    void TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay);

    /// <inheritdoc cref="EngineSide.EffectDispatchIndex"/>
    int EffectDispatchIndex(string name);

    /// <inheritdoc cref="EngineSide.ClientEnergySplash"/>
    void ClientEnergySplash(Vector position, Vector direction, bool explosive);

    // C-Phase 2's fifteen backends (plan_tmp.md Part 4). Client-only, like
    // ClientEnergySplash above and for the same reason: they are DRAWING.

    /// <inheritdoc cref="EngineSide.ClientSparks"/>
    void ClientSparks(Vector position, int magnitude, int trailLength, Vector? direction = null);

    /// <inheritdoc cref="EngineSide.ClientRicochet"/>
    void ClientRicochet(Vector position, Vector direction);

    /// <inheritdoc cref="EngineSide.ClientMetalSparks"/>
    void ClientMetalSparks(Vector position, Vector direction);

    /// <inheritdoc cref="EngineSide.ClientSmoke"/>
    void ClientSmoke(Vector origin, int modelIndex, float scale, float frameRate);

    /// <inheritdoc cref="EngineSide.ClientMuzzleFlash"/>
    void ClientMuzzleFlash(Vector origin, Vector angles, float scale, int type);

    /// <inheritdoc cref="EngineSide.ClientDecalShoot"/>
    void ClientDecalShoot(int decalIndex, int entIndex, Vector position, Vector? sAxis = null, int flags = 0);

    /// <inheritdoc cref="EngineSide.ClientPlayerDecalShoot"/>
    void ClientPlayerDecalShoot(Vector position, int player, int entIndex);

    /// <inheritdoc cref="EngineSide.ClientDecalIndexFromName"/>
    int ClientDecalIndexFromName(string name);

    // C-Phase 3 lane CX's three effect-side capabilities, for the last three
    // client decal halves. The fourth, ClientTraceLine, is on
    // IEngineWorldQuery: a trace is a question about the world, not an effect.

    /// <inheritdoc cref="EngineSide.ClientDecalStaticProp"/>
    void ClientDecalStaticProp(Vector start, Vector end, int staticPropIndex, int decalIndex);

    /// <inheritdoc cref="EngineSide.ClientIsLowViolence"/>
    bool ClientIsLowViolence();

    /// <inheritdoc cref="EngineSide.ClientDecalNameFromIndex"/>
    string ClientDecalNameFromIndex(int decalIndex);

    // C-Phase 3 lane DECALS's two drawing arms, closing behaviour ledger rows
    // C2 and C3. The third slot of that lane, ClientEntityModelType, is on
    // IEngineWorldQuery: what an entity's model IS is a question, and the
    // switch it feeds is C#'s.

    /// <inheritdoc cref="EngineSide.ClientDecalStudioModel"/>
    void ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayEnd, int decalIndex);

    /// <inheritdoc cref="EngineSide.ClientDecalBrushModel"/>
    void ClientDecalBrushModel(int entIndex, Vector decalCenter, int decalIndex);


    // THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP). Eight named
    // primitives, not one kind+params slot; Sparks reuses ClientSparks above.

    /// <inheritdoc cref="EngineSide.ClientExplosionFireball"/>
    void ClientExplosionFireball(Vector origin);




    // THE LEGACY TEMP ENTITY POOL (plan_tmp.md Part 5, C-Phase 4). Six slots,
    // client-only for the reason the twenty-four above them are: they reach
    // c_te_legacytempents.cpp's pool of C_LocalTempEntity, which is a CLIENT
    // pool of drawables. The server has no such thing -- it sends a table and
    // stops.

    /// <inheritdoc cref="EngineSide.TempEntPoolCapacity"/>
    int TempEntPoolCapacity();

    /// <inheritdoc cref="EngineSide.TempEntPoolDestroy"/>
    void TempEntPoolDestroy(int slot);

    /// <inheritdoc cref="EngineSide.TempEntPoolReadLife"/>
    bool TempEntPoolReadLife(int slot, out TempEntLife life);

    /// <inheritdoc cref="EngineSide.TempEntPoolWriteLife"/>

    /// <summary>
    /// The transform C# OWNS while it simulates this tempent — read once on
    /// takeover. See <see cref="TempEntTransform"/> for why the whole thing
    /// crosses as one struct rather than as per-field accessors.
    /// </summary>
    bool TempEntPoolReadTransform(int slot, out TempEntTransform transform);

    /// <summary>Written back once per tempent per frame, which is how it is drawn.</summary>
    bool TempEntPoolWriteTransform(int slot, in TempEntTransform transform);

    /// <inheritdoc cref="EngineSide.TempEntPoolReadStatics"/>
    bool TempEntPoolReadStatics(int slot, out TempEntStatics statics);

    /// <inheritdoc cref="EngineSide.ClientWindSpeed"/>
    Vector ClientWindSpeed(float time);

    /// <summary>
    /// <c>GetCurrentGravity()</c> — a slot rather than a constant because
    /// <c>sv_gravity</c> is a convar.
    /// </summary>
    float CurrentGravity();

    /// <summary>
    /// A CLIENT entity's origin by INDEX, which is what
    /// <c>FTENT_PLYRATTACHMENT</c> stores. False when the index names no
    /// entity.
    /// </summary>
    bool ClientEntityOrigin(int entityIndex, out Vector origin);
    bool TempEntPoolWriteLife(int slot, in TempEntLife life);

    /// <inheritdoc cref="EngineSide.TempEntPoolSimulate"/>
    bool TempEntPoolSimulate(int slot, float frameTime, int frameNumber);

    /// <inheritdoc cref="EngineSide.TempEntPoolAddVisible"/>
    bool TempEntPoolAddVisible(int slot);


    // C-PHASE 5. Four more of the same pool, and the other direction of it: the
    // six above are the managed pool's hands on a tempent NATIVE made, and
    // these are what a MANAGED spawn helper needs. Client-only for the same
    // reason -- a pool of drawables exists on one side.

    /// <inheritdoc cref="EngineSide.TempEntPoolCreate"/>
    bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority);

    /// <inheritdoc cref="EngineSide.TempEntPoolWriteSpawn"/>
    bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn);

    // C-PHASE 3, LANE CE. One string-table read (its drawing primitive, the
    // shattered pane, is C# since lane c5-particles: ShardEffects); Smoke,
    // MuzzleFlash and EffectDispatch reuse the three slots above.

    /// <inheritdoc cref="EngineSide.EffectDispatchName"/>
    string EffectDispatchName(int index);

    /// <inheritdoc cref="EngineSide.DrawSprite"/>
    int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize);

    /// <inheritdoc cref="EngineSide.DrawSpriteOffset"/>
    int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize);

    /// <inheritdoc cref="EngineSide.ParticleSystemStart"/>
    bool ParticleSystemStart(EntityHandle owner, string name);

    /// <inheritdoc cref="EngineSide.ParticleSystemStop"/>
    void ParticleSystemStop(EntityHandle owner);

    /// <inheritdoc cref="EngineSide.ParticleSystemFollowPoints"/>
    bool ParticleSystemFollowPoints(EntityHandle owner, int pointMask);

    /// <inheritdoc cref="EngineSide.SpriteTrailUpdate"/>
    void SpriteTrailUpdate(EntityHandle owner, int renderMode, bool visible);

    /// <inheritdoc cref="EngineSide.SpriteTrailDestroy"/>
    void SpriteTrailDestroy(EntityHandle owner);



    /// <inheritdoc cref="EngineSide.DustEffect"/>
    void DustEffect(Vector origin, Vector direction, float size, float speed);




    /// <inheritdoc cref="EngineSide.ImpactEffect"/>
    void ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity);

    /// <inheritdoc cref="EngineSide.FunnelEffect"/>
    void FunnelEffect(Vector origin, int spriteIndex, bool reversed);

    /// <inheritdoc cref="EngineSide.MuzzleFlashEffect"/>
    void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1);

    /// <inheritdoc cref="EngineSide.PaintDecal"/>
    bool PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance, bool permanent, bool lowPriority);









    /// <inheritdoc cref="EngineSide.SparkEffect"/>
    void SparkEffect(Vector origin, int magnitude, int trailLength, bool makeSound = true, Vector? direction = null);

    /// <inheritdoc cref="EngineSide.ExplosionEffect"/>
    void ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate,
                         int flags, int radius, int magnitude);

    /// <inheritdoc cref="EngineSide.SplashEffect"/>
    void SplashEffect(Vector origin, float scale, bool inSlime = false);




    /// <inheritdoc cref="EngineSide.TracerEffect"/>
    void TracerEffect(Vector start, Vector end);



}

/// <summary>
/// What can be seen, and how the world is lit and coloured: area portals,
/// occluders, dynamic lights, colour correction and the sky.
///
/// <para>
/// One area because they are one question asked at different scales — a portal
/// decides whether a room is drawn, a correction decides what it looks like when
/// it is.
/// </para>
/// </summary>
public interface IEngineVisibility
{
    /// <inheritdoc cref="EngineSide.ActivateOccluder"/>
    void ActivateOccluder(int occluderIndex, bool active);

    /// <inheritdoc cref="EngineSide.AreaPortalCreate"/>
    uint AreaPortalCreate(int portalNumber, int portalVersion);

    /// <inheritdoc cref="EngineSide.AreaPortalDestroy"/>
    void AreaPortalDestroy(uint portal);

    /// <inheritdoc cref="EngineSide.AreaPortalSetOpen"/>
    void AreaPortalSetOpen(uint portal, bool open);

    /// <inheritdoc cref="EngineSide.AreaPortalSetWindow"/>
    void AreaPortalSetWindow(uint portal, EntityHandle entity, float fadeDistance);

    /// <inheritdoc cref="EngineSide.ClearSkyboxParams"/>
    void ClearSkyboxParams();

    /// <inheritdoc cref="EngineSide.ClientSetWorldRenderState"/>
    void ClientSetWorldRenderState(WorldRenderState which, ReadOnlySpan<float> values);

    /// <inheritdoc cref="EngineSide.ColorCorrectionAdd"/>
    int ColorCorrectionAdd(string lookupFile);

    /// <inheritdoc cref="EngineSide.ColorCorrectionRemove"/>
    void ColorCorrectionRemove(int handle);

    /// <inheritdoc cref="EngineSide.ColorCorrectionSetWeight"/>
    void ColorCorrectionSetWeight(int handle, float weight);

    /// <inheritdoc cref="EngineSide.DynamicLightRelease"/>
    void DynamicLightRelease(int key);

    /// <inheritdoc cref="EngineSide.DynamicLightUpdate"/>
    void DynamicLightUpdate(in DynamicLight light);

    /// <summary>
    /// Sets how a numbered lightstyle animates. The pattern is one letter per
    /// tenth of a second: 'a' is black, 'm' is normal brightness, 'z' is about
    /// double. It repeats forever.
    ///
    /// <para>
    /// A style number rather than an entity, because that is what the engine
    /// keeps — 64 slots shared by everything in the map. vbsp reserves 32..62
    /// for switchable lights and assigns one per NAMED light entity, so two
    /// lights the mapper named the same thing share a style and go out
    /// together. That is the reason this is not an entity property.
    /// </para>
    /// </summary>
    void SetLightStyle(int style, string pattern);

    /// <inheritdoc cref="EngineSide.SetSkyboxParams"/>
    void SetSkyboxParams(in SkyboxParams parameters);

    /// <inheritdoc cref="EngineSide.SetViewerFogParams"/>
    void SetViewerFogParams(in FogParams parameters);

    /// <inheritdoc cref="EngineSide.ClearViewerFogParams"/>
    void ClearViewerFogParams();

    /// <inheritdoc cref="EngineSide.SetViewerSkyboxParams"/>
    void SetViewerSkyboxParams(in SkyboxParams parameters);

    /// <inheritdoc cref="EngineSide.ClearViewerSkyboxParams"/>
    void ClearViewerSkyboxParams();

    // ---- plan_server.md Phase 8b / plan-audit C4 ---------------------------
    //
    // The 3D-skybox area of one client, asked of ONE HIERARCHY EACH. The pair
    // is here rather than on IEngineEntities because what CheckTransmit wants
    // is a VISIBILITY fact — which visleaf area this client's skybox camera
    // sits in — and the identity of the entity is only how it is looked up.
    //
    // TWO METHODS AND NOT ONE, and that is the point of C4 rather than an
    // accident of the ABI: the choice between the two hierarchies used to be an
    // `#ifdef SOURCESHARP` block inside CheckTransmit, and moving it means
    // giving C# both reads separately so the ORDER is C#'s.

    /// <summary>
    /// The 3D-skybox area of one client, read off the SDK hierarchy ONLY.
    ///
    /// <para>
    /// plan_server.md Phase 8b, plan-audit C4. Half of a pair;
    /// <see cref="EntitiesStandalonePlayerSkyboxArea"/> is the other half, and
    /// the ORDER they are asked in is <c>ServerEntities.SkyboxAreaForClient</c>'s
    /// — which is the whole content of the crossing, because that order used to
    /// be an <c>#ifdef SOURCESHARP</c> block inside
    /// <c>CServerGameEnts::CheckTransmit</c>.
    /// </para>
    ///
    /// <para>
    /// <b><c>area</c> is untouched on a miss, not zeroed.</b> Area 0 is a real
    /// visleaf area, so "the world's first area" and "no such recipient" cannot
    /// share a value — CheckTransmit returns early for one and walks every
    /// edict for the other.
    /// </para>
    /// </summary>
    bool TrySdkPlayerSkyboxArea(int edictIndex, out int area);

    /// <inheritdoc cref="EngineSide.EntitiesStandalonePlayerSkyboxArea"/>
    bool TryStandalonePlayerSkyboxArea(int edictIndex, out int area);
}

/// <summary>A sound script's CSoundParameters: volume, pitch and its range, and soundlevel.</summary>
public readonly record struct SoundScriptParams(float Volume, int Pitch, int PitchLow, int PitchHigh, int SoundLevel);

/// <summary>UTIL_TraceEntity's answer: where the hull stopped, and whether it started or stayed in solid.</summary>
public readonly record struct EntityHullTrace(Vector EndPos, bool StartSolid, bool AllSolid);
