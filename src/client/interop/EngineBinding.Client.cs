namespace SourceSharp.Abi;

// The client's half of the engine bindings (plan_shim_areas_split phase 7): the
// members of the shared seams that only the client's engine serves, forwarded
// as in EngineBinding.cs, and the server's members, answered as a client
// engine answered them, from the server's attach guard, which is never
// attached here. Each class's other part, with its interface, is in
// src/shared/interop/EngineBinding.cs.

internal sealed partial class EngineBrushMovement
{
    public void SetSurfaceEdges(EntityHandle entity, byte[] packed, int count) => E.SurfaceSetEdges(entity, packed, count);


    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void Spin(EntityHandle entity, Vector degreesPerSecond) { }
}

internal sealed partial class EngineConsole
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void SendClientCommand(EntityHandle player, string command) { }
}

internal sealed partial class EngineDamage
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType) { }
    public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position) { }
    public void RegisterRules(DamageRuleMasks masks) { }
    public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false) { }
}

internal sealed partial class EngineDiagnostics
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public int PlayerShadowCounts(Span<int> into) => 0;
    public int ExitWatchArm(string recordPath) => 0;
    public int ExitWatchRead(Span<int> into) => 0;
    public int ExitWatchDisarm() => 0;
}

internal sealed partial class EngineEffects
{
    public void ClientEnergySplash(Vector position, Vector direction, bool explosive) => E.ClientEnergySplash(position, direction, explosive);

    public void ClientSparks(Vector position, int magnitude, int trailLength, Vector? direction = null) => E.ClientSparks(position, magnitude, trailLength, direction);

    public void ClientRicochet(Vector position, Vector direction) => E.ClientRicochet(position, direction);

    public void ClientMetalSparks(Vector position, Vector direction) => E.ClientMetalSparks(position, direction);

    public void ClientSmoke(Vector origin, int modelIndex, float scale, float frameRate) => E.ClientSmoke(origin, modelIndex, scale, frameRate);

    public void ClientMuzzleFlash(Vector origin, Vector angles, float scale, int type) => E.ClientMuzzleFlash(origin, angles, scale, type);

    public void ClientDecalShoot(int decalIndex, int entIndex, Vector position, Vector? sAxis = null, int flags = 0) => E.ClientDecalShoot(decalIndex, entIndex, position, sAxis, flags);

    public void ClientPlayerDecalShoot(Vector position, int player, int entIndex) => E.ClientPlayerDecalShoot(position, player, entIndex);

    public int ClientDecalIndexFromName(string name) => E.ClientDecalIndexFromName(name);

    // C-Phase 3 lane CX's three. ClientTraceLine is on EngineWorldQuery above.
    public void ClientDecalStaticProp(Vector start, Vector end, int staticPropIndex, int decalIndex) => E.ClientDecalStaticProp(start, end, staticPropIndex, decalIndex);

    public bool ClientIsLowViolence() => E.ClientIsLowViolence();

    public string ClientDecalNameFromIndex(int decalIndex) => E.ClientDecalNameFromIndex(decalIndex);

    // C-Phase 3 lane DECALS's two drawing arms, closing ledger rows C2 and C3.
    // ClientEntityModelType is on EngineWorldQuery above.
    public void ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayEnd, int decalIndex) => E.ClientDecalStudioModel(entIndex, rayStart, rayEnd, decalIndex);

    public void ClientDecalBrushModel(int entIndex, Vector decalCenter, int decalIndex) => E.ClientDecalBrushModel(entIndex, decalCenter, decalIndex);

    // C-Phase 6 lane FRAME's two.
    public bool TempEntPoolReadStatics(int slot, out TempEntStatics statics) => E.TempEntPoolReadStatics(slot, out statics);

    public Vector ClientWindSpeed(float time) => E.ClientWindSpeed(time);

    // THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP): the last,
    // ClientExplosionFireball, left with the glow overlays' port (C7).

    // THE LEGACY TEMP ENTITY POOL (plan_tmp.md Part 5, C-Phase 4).
    public int TempEntPoolCapacity() => E.TempEntPoolCapacity();

    public void TempEntPoolDestroy(int slot) => E.TempEntPoolDestroy(slot);

    public bool TempEntPoolReadLife(int slot, out TempEntLife life) => E.TempEntPoolReadLife(slot, out life);

    public bool TempEntPoolWriteLife(int slot, in TempEntLife life) => E.TempEntPoolWriteLife(slot, in life);

    public bool TempEntPoolReadTransform(int slot, out TempEntTransform transform) => E.TempEntPoolReadTransform(slot, out transform);

    public bool TempEntPoolWriteTransform(int slot, in TempEntTransform transform) => E.TempEntPoolWriteTransform(slot, in transform);

    public bool ClientEntityOrigin(int entityIndex, out Vector origin) => E.ClientEntityOrigin(entityIndex, out origin);

    public bool TempEntPoolSimulate(int slot, float frameTime, int frameNumber) => E.TempEntPoolSimulate(slot, frameTime, frameNumber);

    public bool TempEntPoolAddVisible(int slot) => E.TempEntPoolAddVisible(slot);

    // C-PHASE 5. What a MANAGED spawn helper needs from the same pool.
    public bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority) => E.TempEntPoolCreate(slot, origin, modelIndex, priority);

    public bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn) => E.TempEntPoolWriteSpawn(slot, in spawn);

    public string EffectDispatchName(int index) => E.EffectDispatchName(index);

    public int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => E.DrawSprite(owner, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale, hdrColorScale, glowProxySize);

    public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => E.DrawSpriteOffset(owner, offset, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale, hdrColorScale, glowProxySize);

    public bool ParticleSystemStart(EntityHandle owner, string name) => E.ParticleSystemStart(owner, name);

    public void ParticleSystemStop(EntityHandle owner) => E.ParticleSystemStop(owner);

    public bool ParticleSystemFollowPoints(EntityHandle owner, int pointMask) => E.ParticleSystemFollowPoints(owner, pointMask);

    public void SpriteTrailUpdate(EntityHandle owner, int renderMode, bool visible) => E.SpriteTrailUpdate(owner, renderMode, visible);

    public void SpriteTrailDestroy(EntityHandle owner) => E.SpriteTrailDestroy(owner);


    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void BloodEffect(Vector origin, Vector direction, BloodColor color, float amount, bool stream = false, BloodSpray sprayFlags = BloodSpray.None) { }
    public void BubbleEffect(EntityHandle entity, int count, float speed) { }
    public void TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay) { }
    public int EffectDispatchIndex(string name) => 0;
    public void DustEffect(Vector origin, Vector direction, float size, float speed) { }
    public void ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity) { }
    public void FunnelEffect(Vector origin, int spriteIndex, bool reversed) { }
    public void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1) { }
    public bool PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance, bool permanent, bool lowPriority) => false;
    public void SparkEffect(Vector origin, int magnitude, int trailLength, bool makeSound = true, Vector? direction = null) { }
    public void ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate, int flags, int radius, int magnitude) { }
    public void SplashEffect(Vector origin, float scale, bool inSlime = false) { }
    public void TracerEffect(Vector start, Vector end) { }
}

internal sealed partial class EngineEntities
{
    public int UpdateVisibilityAll() => E.EntitiesUpdateVisibilityAll();


    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void AddBaseVelocity(EntityHandle entity, Vector velocity) { }
    public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) { }
    public EntityHandle Create(string className) => EntityHandle.None;
    public EntityHandle CreateFromMap(string className, byte[] packedKeys) => EntityHandle.None;
    public Vector GetAngularVelocity(EntityHandle entity) => default;
    public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => (Vector.Zero, Vector.Zero);
    public Vector GetBoundsSize(EntityHandle entity) => default;
    public string GetClassName(EntityHandle h) => string.Empty;
    public Vector GetEyeAngles(EntityHandle h) => default;
    public Vector GetEyePosition(EntityHandle h) => default;
    public int GetHealth(EntityHandle h) => 0;
    public string GetName(EntityHandle entity) => string.Empty;
    public Vector GetSpawnAngles(EntityHandle entity) => default;
    public int GetSpawnFlags(EntityHandle entity) => 0;
    public Vector GetSpawnOrigin(EntityHandle entity) => default;
    public Vector GetVelocity(EntityHandle h) => default;
    public Vector GetWorldSpaceCenter(EntityHandle h) => default;
    public bool IsPlayer(EntityHandle h) => false;
    public void SetAngles(EntityHandle entity, Vector angles) { }
    public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) { }
    public void SetHealth(EntityHandle h, int health) { }
    public bool SetKeyValue(EntityHandle entity, string key, string value) => false;
    public void SetOrigin(EntityHandle h, Vector origin) { }
    public void SetOwner(EntityHandle h, EntityHandle owner) { }
    public void SetVelocity(EntityHandle h, Vector velocity) { }
    public void Spawn(EntityHandle entity) { }
    public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) { }
    public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => false;
    public EntityHandle GetParent(EntityHandle child) => EntityHandle.None;
    public int GetParentAttachment(EntityHandle child) => 0;
    public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) { }
    public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles)
    {
        origin = default;
        angles = default;
    }
    public ParentProbe ParentProbe(EntityHandle entity) => SourceSharp.Abi.ParentProbe.None;
    public Span<byte> GetEngineBlock(EntityHandle entity) => Span<byte>.Empty;
    public void DestroyPhysics(EntityHandle entity) { }
    public float GetGravity(EntityHandle entity) => 0f;
    public void SetGravity(EntityHandle entity, float gravity) { }
    public SolidFlags GetSolidFlags(EntityHandle entity) => SolidFlags.None;
    public SolidType GetSolidType(EntityHandle entity) => SolidType.None;
    public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) { }
    public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) { }
    public void SetSpawnFlags(EntityHandle entity, int spawnFlags) { }
    public void PositionChanged(EntityHandle entity, Vector previousOrigin) { }
    public IEnumerable<EntityHandle> FindAllByClassname(string className) => Array.Empty<EntityHandle>();
    public void Remove(EntityHandle entity) => E.Removal.Remove(entity);
    private partial bool EngineAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles)
    {
        origin = default;
        angles = default;
        return false;
    }
}

internal sealed partial class EngineEntityPhysics
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void ApplyForceCenter(EntityHandle entity, Vector force) { }
    public void ApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint) { }
    public void ApplyTorque(EntityHandle entity, Vector torque) { }
    public void BecomePhysicsTrigger(EntityHandle entity, bool isTrigger) { }
    public void EnableMotion(EntityHandle entity, bool enable) { }
    public void Wake(EntityHandle entity) { }
    public void Sleep(EntityHandle entity) { }
    public void AddGameFlags(EntityHandle entity, uint flags) { }
    public float GetMass(EntityHandle entity) => 0f;
    public bool InitPhysics(EntityHandle entity, bool asleep = false, bool debris = false) => false;
    public void SetBounce(EntityHandle h, int bounces) { }
    public void SetBrushSolidity(EntityHandle entity, BrushSolidity solidity) { }
    public CollisionGroup GetCollisionGroup(EntityHandle entity) => CollisionGroup.None;
    public SolidFlags GetSolidFlags(EntityHandle entity) => SolidFlags.None;
    public void SetCollisionGroup(EntityHandle entity, CollisionGroup group) { }
    public void SetGravity(EntityHandle h, float gravity) { }
    public void SetMass(EntityHandle entity, float mass) { }
    public void SetNotSolid(EntityHandle entity, bool notSolid) { }
    public nint PhysicsObject(EntityHandle entity) => 0;
    public nint EnvironmentPointer() => 0;
    public Vector CollideExtent(nint physicsObject, Vector direction) => Vector.Zero;
    public Vector Inertia(nint physicsObject) => Vector.Zero;
    public nint WorldObject() => 0;
    public void SetPhysicsStatic(EntityHandle entity, bool isStatic) { }
}

internal sealed partial class EngineEntityRender
{
    public void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs) => E.StandaloneSetCustomDraw(entity, mins, maxs);

    public void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha) => E.StandaloneSetBlending(entity, translucent, ignoreZ, alpha);

    public void SetShadowCast(EntityHandle entity, ShadowCastChoice choice) => E.StandaloneSetShadowCast(entity, choice);

    public ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked, out bool hasShadow) => E.StandaloneShadowCast(entity, out asked, out hasShadow);

    public void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f) => E.StandaloneSetFade(entity, style, startDistance, endDistance, minimumAlpha);

    public void SetOverlayModel(EntityHandle entity, int modelIndex) => E.StandaloneSetOverlayModel(entity, modelIndex);

    public void SetViewModel(EntityHandle entity, bool enable) => E.StandaloneSetViewModel(entity, enable);

    public void SetSkin(EntityHandle entity, int skin) => E.EntitySetSkin(entity, skin);

    private partial bool EngineIsDormant(EntityHandle entity) => E.StandaloneIsDormant(entity);

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void SetModel(EntityHandle entity, string model) { }
    public void SetTransmitAlways(EntityHandle entity, bool always) { }
    public void SetVisible(EntityHandle entity, bool visible) { }
}

internal sealed partial class EngineGameEvents
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void EventSetInt(IntPtr handle, string key, int value) { }
    public void EventSetFloat(IntPtr handle, string key, float value) { }
    public void EventSetBool(IntPtr handle, string key, bool value) { }
    public void EventSetString(IntPtr handle, string key, string value) { }
}

internal sealed partial class EngineGameRulesPolicy
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void RegisterPolicy(GameRulesPolicyBlob policy) { }
}

internal sealed partial class EngineHud
{
    public int LocalPlayerIndex => E.GetLocalPlayerIndex();

    public string Localise(string token) => E.HudLocalise(token);

    public string LookupBinding(string binding) => E.HudLookupBinding(binding);

    public TextMessage? TextMessageGet(string name) => E.HudTextMessageGet(name);

    public ViewportFacts ViewportQuery(string panel) => E.ClientViewportQuery(panel);

    public void ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions) => E.ClientViewportShow(panel, show, data, actions);
    public int LocalPlayerHealth => E.LocalPlayerHealth;
    public Vector? LocalPlayerOrigin => E.LocalPlayerOrigin;

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void DrawWorldText(Vector origin, int line, string text, float duration, int r = 255, int g = 255, int b = 255, int a = 255) { }
    public bool DrawEntityOverlay(EntityHandle entity, int line, string? text, float duration, int r = 255, int g = 255, int b = 255, int a = 255) => false;
    public (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet) OverlayReadout() => (0, 0, 0, 0, false);
    public void OverlayResetCounts() { }
    public void AddLineOverlay(Vector start, Vector end, int r, int g, int b, bool noDepthTest = true, float duration = 0f) { }
    public void ViewPunch(Vector origin, Vector punch, float radius, bool inAir = false) { }
}

internal sealed partial class EngineNetworking
{
    public bool SendServerMessage(string key, ReadOnlySpan<byte> payload) => E.SendServerMessage(key, payload);


    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public PayloadAgreement CheckPayloadAgreement(EntityHandle h) => PayloadAgreements.NotSupplied;
    public EntityState EntityGetState(EntityHandle entity, out MoveType moveType)
    {
        moveType = MoveType.None;
        return EntityState.None;
    }
    public Span<byte> GetEntityBlob(EntityHandle h) => Span<byte>.Empty;
    public void MarkEntityStateChanged(EntityHandle h) { }
    public void MarkEntityBlockChanged(EntityHandle h, int blockOffset) { }
}

internal sealed partial class EngineOverlaps
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public int OverlapReport(Span<OverlapPair> into) => 0;
}

internal sealed partial class EnginePlayerMovement
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void SetSlowMovement(EntityHandle player, bool slow) { }
    public MoveType SetMoveType(EntityHandle player, MoveType moveType) => MoveType.None;
    public void SnapEyeAngles(EntityHandle player, Vector pitchYawRoll) { }
}

internal sealed partial class EnginePlayers
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles) { }
    public EntityHandle GetHandle(int playerIndex) => EntityHandle.None;
    public EntityHandle GetCommandClient() => EntityHandle.None;
    public ObserverMode GetObserverMode(EntityHandle player) => ObserverMode.None;
    public Vector GetShootOrigin(EntityHandle player) => default;
    public int GetTeam(EntityHandle player) => -1;
    public bool IsAlive(EntityHandle player) => false;
    public void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ) { }
    public void Respawn(EntityHandle player) { }
    public void SetObserverMode(EntityHandle player, ObserverMode mode) { }
    public void SetTeam(EntityHandle player, int team) { }
    public void SetPickupEnabled(EntityHandle entity, bool enabled) { }
    public bool TryStandalonePlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = default;
        return false;
    }
    public EntityHandle FindByUserId(int userId) => EntityHandle.None;
    private partial Vector EyePosition(EntityHandle player) => default;
}

internal sealed partial class EnginePrecache
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public int PrecacheDecal(string name) => -1;
    public void PrecacheMaterial(string material) { }
    public void PrecacheParticleSystem(string name) { }
    public int PrecacheModel(string model) => -1;
    public void PrecacheSound(string soundName) { }
}

internal sealed partial class EngineScripted
{
    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void RegisterEntityClass(string className, string[] outputNames, ScriptedEntityKind kind) { }
    public void RegisterEntityClassOps(string className, uint opMask) { }
    public List<EntityHandle> TemplateSpawnKeyed(IReadOnlyList<KeyedSpawn> spawns, int limit = 64) => new List<EntityHandle>();
}

internal sealed partial class EngineSound
{


    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch) { }
    public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume) => false;
    public void EmitSoundAtPosition(string soundName, Vector origin) { }
    public void EntityStopSound(EntityHandle entity, string soundName) { }
    public void EntityEmitSound(EntityHandle entity, string soundName) { }
}

internal sealed partial class EngineVisibility
{
    public void ActivateOccluder(int occluderIndex, bool active) => E.ActivateOccluder(occluderIndex, active);

    public void ClientSetWorldRenderState(WorldRenderState which, ReadOnlySpan<float> values) => E.ClientSetWorldRenderState(which, values);

    public int ColorCorrectionAdd(string lookupFile) => E.ColorCorrectionAdd(lookupFile);

    public void ColorCorrectionRemove(int handle) => E.ColorCorrectionRemove(handle);

    public void ColorCorrectionSetWeight(int handle, float weight) => E.ColorCorrectionSetWeight(handle, weight);

    public void DynamicLightRelease(int key) => E.DynamicLightRelease(key);

    public void DynamicLightUpdate(in DynamicLight light) => E.DynamicLightUpdate(in light);

    public void SetViewerFogParams(in FogParams parameters) => E.SetViewerFogParams(in parameters);

    public void ClearViewerFogParams() => E.ClearViewerFogParams();

    /// <inheritdoc/>
    public void SetViewerSkyboxParams(in SkyboxParams parameters) => E.SetViewerSkyboxParams(in parameters);

    /// <inheritdoc/>
    public void ClearViewerSkyboxParams() => E.ClearViewerSkyboxParams();


    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public uint AreaPortalCreate(int portalNumber, int portalVersion) => 0;
    public void AreaPortalDestroy(uint portal) { }
    public void AreaPortalSetOpen(uint portal, bool open) { }
    public void AreaPortalSetWindow(uint portal, EntityHandle entity, float fadeDistance) { }
    public void ClearSkyboxParams() { }
    public void SetSkyboxParams(in SkyboxParams parameters) { }
    public bool TryStandalonePlayerSkyboxArea(int edictIndex, out int area)
    {
        area = 0;
        return false;
    }
}

internal sealed partial class EngineWeapons
{
    public void RegisterAmmoType(string name, DamageType damageType, int maxCarry, int tracerType = 0, float damageForce = 0f, float damage = 0f) => E.RegisterAmmoType(name, damageType, maxCarry, tracerType, damageForce, damage);
}

internal sealed partial class EngineWorldQuery
{
    // C-Phase 3 lane DECALS. What a client entity's model IS, which is the
    // switch C_BaseEntity::AddDecal makes -- made in C#, over this.
    public ModelType? ClientEntityModelType(int entIndex) => E.ClientEntityModelType(entIndex);

    public bool ClientEntityModelBounds(int entityIndex, out Vector mins, out Vector maxs,
                                        out Vector angles)
        => E.ClientEntityModelBounds(entityIndex, out mins, out maxs, out angles);
    public ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask) => E.ClientTraceLine(start, end, mask);

    public ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask,
                                             TraceFilter filter, int passEntityIndex = -1)
        => E.ClientTraceLine(start, end, mask, filter, passEntityIndex);

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public (Vector Right, Vector Up) AngleVectorsFromForward(Vector forward) => (default, default);
    public int EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into) => 0;
    public HullResult TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, EntityHandle ignore = default) => new HullResult(false, false, EntityHandle.None);
    public EntityHullTrace EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs, uint mask, int collisionGroup) => new EntityHullTrace(start, true, true);
    public TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default) => new TraceResult(false, 1f, end, Vector.Zero, EntityHandle.None);
    public SurfaceTraceResult TraceLineSurface(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default) => new SurfaceTraceResult(false, 1f, end, Vector.Zero, EntityHandle.None, -1, 0);
    public EntityHandle TracePlayerAim(EntityHandle player, float range, out Vector hitPoint)
    {
        hitPoint = default;
        return EntityHandle.None;
    }
    public WaterBelow WaterSurfaceBelow(Vector start) => new WaterBelow(false, false, false, start);
    public string WorldChapterTitle() => string.Empty;
    public int EntityModelSurfaceProp(EntityHandle entity) => -1;
    public int SurfacePropIndex(string name) => -1;
}
