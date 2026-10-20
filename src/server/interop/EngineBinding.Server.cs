namespace SourceSharp.Abi;

// The server's half of the engine bindings (plan_shim_areas_split phase 7): the
// members of the shared seams that only the server's engine serves, forwarded
// as in EngineBinding.cs, and the client's members, answered as a server
// engine answered them, from the client's attach guard, which is never
// attached here. Each class's other part, with its interface, is in
// src/shared/interop/EngineBinding.cs.

internal sealed partial class EngineBrushMovement
{
    public void Spin(EntityHandle entity, Vector degreesPerSecond) => E.EntitySetAngularVelocity(entity, degreesPerSecond);


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void SetSurfaceEdges(EntityHandle entity, byte[] packed, int count) { }
}

internal sealed partial class EngineConsole
{
    public void SendClientCommand(EntityHandle player, string command) => E.SendClientCommand(player, command);
}

internal sealed partial class EngineDamage
{
    public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType) => E.ApplyDamage(victim, attacker, damage, damageType);

    public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position)
        => E.ApplyDamageForce(victim, attacker, damage, damageType, force, position);

    public void RegisterRules(DamageRuleMasks masks) => E.RegisterDamageRules(masks);

    public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false) => E.EntityTakeDamage(entity, attacker, damage, damageType, direction, forceOrigin, physicsOnly);
}

internal sealed partial class EngineDiagnostics
{
    public int PlayerShadowCounts(Span<int> into) => E.PlayerShadowCounts(into);

    public int ExitWatchArm(string recordPath) => E.ExitWatchArm(recordPath);

    public int ExitWatchRead(Span<int> into) => E.ExitWatchRead(into);

    public int ExitWatchDisarm() => E.ExitWatchDisarm();
}

internal sealed partial class EngineEffects
{
    public void BloodEffect(Vector origin, Vector direction, BloodColor color, float amount, bool stream = false, BloodSpray sprayFlags = BloodSpray.None) => E.BloodEffect(origin, direction, color, amount, stream, sprayFlags);

    // No engine answer since plan_server_native_v2 T4f: the server answers this from a managed trace and a managed temp entity (EffectsOverTempEnts).
    public void BubbleEffect(EntityHandle entity, int count, float speed) { }

    public void TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay) => E.TempEntPlayback(handle, blob, filter, delay);

    public int EffectDispatchIndex(string name) => E.EffectDispatchIndex(name);

    // No engine answer since plan_server_native_v2 T2: the server answers this from managed temp entities (EffectsOverTempEnts, Part 8), which never fell through to the slot.
    public void DustEffect(Vector origin, Vector direction, float size, float speed) { }

    // No engine answer since plan_server_native_v2 T4d: the server answers this from managed temp entities (EffectsOverTempEnts).
    public void ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity) { }

    // No engine answer since plan_server_native_v2 T4d: the server answers this from managed temp entities (EffectsOverTempEnts).
    public void FunnelEffect(Vector origin, int spriteIndex, bool reversed) { }

    // No engine answer since plan_server_native_v2 T2: the server answers this from managed temp entities (EffectsOverTempEnts, Part 8), which never fell through to the slot.
    public void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1) { }

    // No engine answer since plan_server_native_v2 T4f: the server answers this from a managed trace, managed temp entities and IVEngineServer::StaticDecal (EffectsOverTempEnts).
    public bool PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance, bool permanent, bool lowPriority) => false;

    // No engine answer since plan_server_native_v2 T2: the server answers this from managed temp entities (EffectsOverTempEnts, Part 8), which never fell through to the slot.
    public void SparkEffect(Vector origin, int magnitude, int trailLength, bool makeSound = true, Vector? direction = null) { }

    // No engine answer since plan_server_native_v2 T4d: the server answers this from managed temp entities (EffectsOverTempEnts).
    public void ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate, int flags, int radius, int magnitude) { }

    // No engine answer since plan_server_native_v2 T4d: the server answers this from managed temp entities (EffectsOverTempEnts).
    public void SplashEffect(Vector origin, float scale, bool inSlime = false) { }

    // No engine answer since plan_server_native_v2 T4d: the server answers this from managed temp entities (EffectsOverTempEnts).
    public void TracerEffect(Vector start, Vector end) { }


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void ClientEnergySplash(Vector position, Vector direction, bool explosive) { }
    public void ClientSparks(Vector position, int magnitude, int trailLength, Vector? direction = null) { }
    public void ClientRicochet(Vector position, Vector direction) { }
    public void ClientMetalSparks(Vector position, Vector direction) { }
    public void ClientSmoke(Vector origin, int modelIndex, float scale, float frameRate) { }
    public void ClientMuzzleFlash(Vector origin, Vector angles, float scale, int type) { }
    public void ClientDecalShoot(int decalIndex, int entIndex, Vector position, Vector? sAxis = null, int flags = 0) { }
    public void ClientPlayerDecalShoot(Vector position, int player, int entIndex) { }
    public int ClientDecalIndexFromName(string name) => -1;
    public void ClientDecalStaticProp(Vector start, Vector end, int staticPropIndex, int decalIndex) { }
    public bool ClientIsLowViolence() => false;
    public string ClientDecalNameFromIndex(int decalIndex) => string.Empty;
    public void ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayEnd, int decalIndex) { }
    public void ClientDecalBrushModel(int entIndex, Vector decalCenter, int decalIndex) { }
    public bool TempEntPoolReadStatics(int slot, out TempEntStatics statics)
    {
        statics = default;
        return false;
    }
    public Vector ClientWindSpeed(float time) => Vector.Zero;
    public int TempEntPoolCapacity() => 0;
    public void TempEntPoolDestroy(int slot) { }
    public bool TempEntPoolReadLife(int slot, out TempEntLife life)
    {
        life = default;
        return false;
    }
    public bool TempEntPoolWriteLife(int slot, in TempEntLife life) => false;
    public bool TempEntPoolReadTransform(int slot, out TempEntTransform transform)
    {
        transform = default;
        return false;
    }
    public bool TempEntPoolWriteTransform(int slot, in TempEntTransform transform) => false;
    public bool ClientEntityOrigin(int entityIndex, out Vector origin)
    {
        origin = Vector.Zero;
        return false;
    }
    public bool TempEntPoolSimulate(int slot, float frameTime, int frameNumber) => false;
    public bool TempEntPoolAddVisible(int slot) => false;
    public bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority) => false;
    public bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn) => false;
    public string EffectDispatchName(int index) => string.Empty;
    public int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => 0;
    public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => 0;
    public bool ParticleSystemStart(EntityHandle owner, string name) => false;
    public void ParticleSystemStop(EntityHandle owner) { }
    public bool ParticleSystemFollowPoints(EntityHandle owner, int pointMask) => false;
    public void SpriteTrailUpdate(EntityHandle owner, int renderMode, bool visible) { }
    public void SpriteTrailDestroy(EntityHandle owner) { }
}

internal sealed partial class EngineEntities
{
    public void AddBaseVelocity(EntityHandle entity, Vector velocity) => E.EntityAddBaseVelocity(entity, velocity);

    public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => E.EntityApplyVelocityImpulse(h, impulse, clearGround);

    public EntityHandle Create(string className) => E.CreateEntity(className);

    public EntityHandle CreateFromMap(string className, byte[] packedKeys)
        => E.MapSpawnCreate(className, packedKeys);

    public Vector GetAngularVelocity(EntityHandle entity) => E.EntityGetAngularVelocity(entity);

    public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => E.EntityGetBounds(entity);

    public Vector GetBoundsSize(EntityHandle entity) => E.EntityGetBoundsSize(entity);

    public string GetClassName(EntityHandle h) => E.EntityGetClassName(h);

    public Vector GetEyeAngles(EntityHandle h) => E.EntityGetEyeAngles(h);

    public Vector GetEyePosition(EntityHandle h) => E.EntityGetEyePosition(h);

    public int GetHealth(EntityHandle h) => E.EntityGetHealth(h);

    public string GetName(EntityHandle entity) => E.EntityGetName(entity);

    public Vector GetSpawnAngles(EntityHandle entity) => E.EntityGetSpawnAngles(entity);

    public int GetSpawnFlags(EntityHandle entity) => E.EntityGetSpawnFlags(entity);

    public Vector GetSpawnOrigin(EntityHandle entity) => E.EntityGetSpawnOrigin(entity);

    public Vector GetVelocity(EntityHandle h) => E.EntityGetVelocity(h);

    public Vector GetWorldSpaceCenter(EntityHandle h) => E.EntityGetWorldSpaceCenter(h);

    public bool IsPlayer(EntityHandle h) => E.EntityIsPlayer(h);

    public void SetAngles(EntityHandle entity, Vector angles) => E.EntitySetAngles(entity, angles);

    public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => E.EntitySetAngularVelocity(entity, degreesPerSecond);

    public void SetHealth(EntityHandle h, int health) => E.EntitySetHealth(h, health);

    public bool SetKeyValue(EntityHandle entity, string key, string value) => E.EntitySetKeyValue(entity, key, value);

    public void SetOrigin(EntityHandle h, Vector origin) => E.EntitySetOrigin(h, origin);

    public void SetOwner(EntityHandle h, EntityHandle owner) => E.EntitySetOwner(h, owner);

    public void SetVelocity(EntityHandle h, Vector velocity) => E.EntitySetVelocity(h, velocity);

    public void Spawn(EntityHandle entity) => E.SpawnEntity(entity);

    public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) => E.EntityTeleport(h, origin, angles, zeroVelocity);

    public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => E.EntitySetParent(child, parent, parentAttachment);

    public EntityHandle GetParent(EntityHandle child) => E.EntityGetParent(child);

    public int GetParentAttachment(EntityHandle child) => E.EntityGetParentAttachment(child);

    public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) => E.EntitySetLocalTransform(child, origin, angles);

    public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles) => E.EntityGetLocalTransform(child, out origin, out angles);

    public ParentProbe ParentProbe(EntityHandle entity) => E.EntityParentProbe(entity);

    public Span<byte> GetEngineBlock(EntityHandle entity) => E.GetEntityBlock(entity);

    public void DestroyPhysics(EntityHandle entity) => E.EntityDestroyPhysics(entity);
    public void PositionChanged(EntityHandle entity, Vector previousOrigin) => E.EntityPositionChanged(entity, previousOrigin);
    public IEnumerable<EntityHandle> FindAllByClassname(string className) => E.FindEntitiesByClassname(className);
    public void Remove(EntityHandle entity) => E.RemoveEntity(entity);

    private partial bool EngineAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles)
        => E.EntityGetAttachmentFrame(entity, attachment, out origin, out angles);

    // The native half of a savegame. Three of these are a second spelling of a
    // call another area also offers, over the same static wrapper and the same
    // one ABI slot -- see the block at the end of IEngineEntities.
    public float GetGravity(EntityHandle entity) => E.EntityGetGravity(entity);

    public void SetGravity(EntityHandle entity, float gravity) => E.EntitySetGravity(entity, gravity);

    public SolidFlags GetSolidFlags(EntityHandle entity) => E.EntityGetSolidFlags(entity);

    public SolidType GetSolidType(EntityHandle entity) => E.EntityGetSolidType(entity);

    public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) => E.EntitySetSolidity(entity, solidType, solidFlags);

    public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) => E.EntitySetBounds(entity, mins, maxs);

    public void SetSpawnFlags(EntityHandle entity, int spawnFlags) => E.EntitySetSpawnFlags(entity, spawnFlags);


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public int UpdateVisibilityAll() => 0;
}

internal sealed partial class EngineEntityPhysics
{
    public void ApplyForceCenter(EntityHandle entity, Vector force) => E.EntityApplyForceCenter(entity, force);

    public void ApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint) => E.EntityApplyForceOffset(entity, force, worldPoint);

    public void ApplyTorque(EntityHandle entity, Vector torque) => E.EntityApplyTorque(entity, torque);

    public void BecomePhysicsTrigger(EntityHandle entity, bool isTrigger) => E.EntityBecomePhysicsTrigger(entity, isTrigger);

    public void EnableMotion(EntityHandle entity, bool enable) => E.EntityEnableMotion(entity, enable);

    public void Wake(EntityHandle entity) => E.EntityPhysicsWake(entity);

    public void Sleep(EntityHandle entity) => E.EntityPhysicsSleep(entity);

    public void AddGameFlags(EntityHandle entity, uint flags) => E.EntityAddPhysicsGameFlags(entity, flags);

    public float GetMass(EntityHandle entity) => E.EntityGetMass(entity);

    public bool InitPhysics(EntityHandle entity, bool asleep = false, bool debris = false) => E.EntityInitPhysics(entity, asleep, debris);

    public void SetBounce(EntityHandle h, int bounces) => E.EntitySetBounce(h, bounces);

    public void SetBrushSolidity(EntityHandle entity, BrushSolidity solidity) => E.EntitySetBrushSolidity(entity, solidity);

    public CollisionGroup GetCollisionGroup(EntityHandle entity) => E.EntityGetCollisionGroup(entity);

    public SolidFlags GetSolidFlags(EntityHandle entity) => E.EntityGetSolidFlags(entity);

    public void SetCollisionGroup(EntityHandle entity, CollisionGroup group) => E.EntitySetCollisionGroup(entity, group);

    public void SetGravity(EntityHandle h, float gravity) => E.EntitySetGravity(h, gravity);

    public void SetMass(EntityHandle entity, float mass) => E.EntitySetMass(entity, mass);

    public void SetNotSolid(EntityHandle entity, bool notSolid) => E.EntitySetNotSolid(entity, notSolid);

    public nint PhysicsObject(EntityHandle entity) => E.EntityPhysicsObject(entity);

    public nint EnvironmentPointer() => E.PhysicsEnvironmentPointer();

    public Vector CollideExtent(nint physicsObject, Vector direction) => E.PhysicsCollideExtent(physicsObject, direction);

    public Vector Inertia(nint physicsObject) => E.PhysicsInertia(physicsObject);

    public nint WorldObject() => E.PhysicsWorldObject();

    public void SetPhysicsStatic(EntityHandle entity, bool isStatic) => E.EntitySetPhysicsStatic(entity, isStatic);
}

internal sealed partial class EngineEntityRender
{
    public void SetModel(EntityHandle entity, string model) => E.SetEntityModel(entity, model);

    public void SetTransmitAlways(EntityHandle entity, bool always) => E.EntitySetTransmitAlways(entity, always);

    public void SetVisible(EntityHandle entity, bool visible) => E.EntitySetVisible(entity, visible);


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs) { }
    public void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha) { }
    public void SetShadowCast(EntityHandle entity, ShadowCastChoice choice) { }
    public ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked, out bool hasShadow)
    {
        asked = ShadowCastChoice.Auto;
        hasShadow = false;
        return SourceSharp.Abi.ShadowCast.Unknown;
    }
    public void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f) { }
    public void SetOverlayModel(EntityHandle entity, int modelIndex) { }
    public void SetViewModel(EntityHandle entity, bool enable) { }
    public void SetSkin(EntityHandle entity, int skin) { }
    private partial bool EngineIsDormant(EntityHandle entity) => false;
}

internal sealed partial class EngineGameEvents
{
    public void EventSetInt(IntPtr handle, string key, int value) => E.EventSetInt(handle, key, value);

    public void EventSetFloat(IntPtr handle, string key, float value) => E.EventSetFloat(handle, key, value);

    public void EventSetBool(IntPtr handle, string key, bool value) => E.EventSetBool(handle, key, value);

    public void EventSetString(IntPtr handle, string key, string value) => E.EventSetString(handle, key, value);
}

internal sealed partial class EngineGameRulesPolicy
{
    public void RegisterPolicy(GameRulesPolicyBlob policy) => E.RegisterGameRulesPolicy(policy);
}

internal sealed partial class EngineHud
{
    // Through IVDebugOverlay's forwarders since 2026-10-19, where the
    // DrawWorldText slot made the same call through overlay_library. A server
    // with no overlay (a dedicated one) draws nothing, as HaveOverlay did.
    public void DrawWorldText(Vector origin, int line, string text, float duration, int r = 255, int g = 255, int b = 255, int a = 255)
    {
        if (!E.ServerAttached || text is null)
            return;

        Overlay()?.TextAtPosition(origin, line, duration, r, g, b, a, text);
    }

    private IVtableDebugOverlay? _overlay;
    private bool _overlayAsked;

    private IVtableDebugOverlay? Overlay()
    {
        if (!_overlayAsked)
        {
            _overlayAsked = true;
            _overlay = EngineDebugOverlay.Acquire(E.Interfaces, out _);
        }

        return _overlay;
    }

    public bool DrawEntityOverlay(EntityHandle entity, int line, string? text, float duration, int r = 255, int g = 255, int b = 255, int a = 255) => E.DrawEntityOverlay(entity, line, text, duration, r, g, b, a);

    public (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet) OverlayReadout() => E.OverlayReadout();

    public void OverlayResetCounts() => E.OverlayResetCounts();

    public void AddLineOverlay(Vector start, Vector end, int r, int g, int b, bool noDepthTest = true, float duration = 0f) => E.AddLineOverlay(start, end, r, g, b, noDepthTest, duration);

    public void ViewPunch(Vector origin, Vector punch, float radius, bool inAir = false) => E.ViewPunch(origin, punch, radius, inAir);


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public int LocalPlayerIndex => 0;
    public string Localise(string token) => token;
    public string LookupBinding(string binding) => string.Empty;
    public TextMessage? TextMessageGet(string name) => null;
    public ViewportFacts ViewportQuery(string panel) => ViewportFacts.None;
    public void ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions) { }
    public int LocalPlayerHealth => -1;
    public Vector? LocalPlayerOrigin => null;
}

internal sealed partial class EngineNetworking
{
    public PayloadAgreement CheckPayloadAgreement(EntityHandle h) => E.CheckPayloadAgreement(h);

    public EntityState EntityGetState(EntityHandle entity, out MoveType moveType) => E.EntityGetState(entity, out moveType);

    public Span<byte> GetEntityBlob(EntityHandle h) => E.GetEntityBlob(h);

    public void MarkEntityStateChanged(EntityHandle h) => E.MarkEntityStateChanged(h);

    public void MarkEntityBlockChanged(EntityHandle h, int blockOffset) => E.MarkEntityBlockChanged(h, blockOffset);


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public bool SendServerMessage(string key, ReadOnlySpan<byte> payload) => false;
}

internal sealed partial class EngineOverlaps
{
    public int OverlapReport(Span<OverlapPair> into) => E.EntityOverlapReport(into);
}

internal sealed partial class EnginePlayerMovement
{
    public void SetSlowMovement(EntityHandle player, bool slow) => E.PlayerSetSlowMovement(player, slow);

    public MoveType SetMoveType(EntityHandle player, MoveType moveType) => E.PlayerSetMoveType(player, moveType);

    public void SnapEyeAngles(EntityHandle player, Vector pitchYawRoll) => E.PlayerSnapEyeAngles(player, pitchYawRoll);
}

internal sealed partial class EnginePlayers
{
    public void PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles) => E.PlayerPlaceAtSpawn(player, origin, angles);

    public EntityHandle GetHandle(int playerIndex) => E.GetPlayerHandle(playerIndex);

    public EntityHandle GetCommandClient() => E.GetCommandClient();

    public ObserverMode GetObserverMode(EntityHandle player) => E.PlayerGetObserverMode(player);

    public Vector GetShootOrigin(EntityHandle player) => E.GetPlayerShootOrigin(player);

    public int GetTeam(EntityHandle player) => E.GetPlayerTeam(player);

    public bool IsAlive(EntityHandle player) => E.PlayerIsAlive(player);

    public void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ) => E.MakePickup(entity, sizeX, sizeY, sizeZ);

    public void Respawn(EntityHandle player) => E.PlayerRespawn(player);

    public void SetObserverMode(EntityHandle player, ObserverMode mode) => E.PlayerSetObserverMode(player, mode);

    public void SetTeam(EntityHandle player, int team) => E.PlayerSetTeam(player, team);

    public void SetPickupEnabled(EntityHandle entity, bool enabled) => E.EntitySetPickupEnabled(entity, enabled);

    public bool TryStandalonePlayerEarPosition(int edictIndex, out Vector ears)
        => E.ClientsStandalonePlayerEarPosition(edictIndex, out ears);
    public EntityHandle FindByUserId(int userId) => E.FindPlayerByUserId(userId);

    private partial Vector EyePosition(EntityHandle player) => E.EntityGetEyePosition(player);
}

internal sealed partial class EnginePrecache
{
    public int PrecacheDecal(string name) => E.PrecacheDecal(name);

    // Through the string tables' forwarders since 2026-10-19, where the
    // PrecacheMaterial and PrecacheParticleSystem slots made the same
    // AddString( true, name ) on the table shell_stringtables.cpp creates.
    // Creating and adding are the server's: the client never had either slot.
    private void Precache(string table, string name)
    {
        if (!E.ServerAttached || string.IsNullOrEmpty(name))
            return;

        int id = Tables.Find(table);
        if (id >= 0)
            Tables.AddString(id, name);
    }

    private ForwardedStringTables? _tables;
    private ForwardedStringTables Tables => _tables ??= ForwardedStringTables.Acquire(E);

    public void PrecacheMaterial(string material) => Precache("Materials", material);

    public void PrecacheParticleSystem(string name) => Precache("ParticleEffectNames", name);

    public int PrecacheModel(string model) => E.PrecacheModel(model);

    public void PrecacheSound(string soundName) => E.PrecacheSound(soundName);
}

internal sealed partial class EngineScripted
{
    public void RegisterEntityClass(string className, string[] outputNames, ScriptedEntityKind kind) => E.RegisterScriptedEntity(className, outputNames, kind);

    public void RegisterEntityClassOps(string className, uint opMask) => E.RegisterClassOps(className, opMask);
    public List<EntityHandle> TemplateSpawnKeyed(IReadOnlyList<KeyedSpawn> spawns, int limit = 64) => E.TemplateSpawnKeyed(spawns, limit);
}

internal sealed partial class EngineSound
{
    public void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch) => E.EmitAmbientSound(entIndex, origin, soundName, volume, soundLevel, flags, pitch);

    public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume) => E.EmitWorldSoundScript(soundName, origin, channel, volume);

    public void EmitSoundAtPosition(string soundName, Vector origin) => E.EmitSoundAtPosition(soundName, origin);

    public void EntityStopSound(EntityHandle entity, string soundName) => E.EntityStopSound(entity, soundName);
    public void EntityEmitSound(EntityHandle entity, string soundName) => E.EntityEmitSound(entity, soundName);

    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void ClientEmitSound(EntityHandle entity, string soundName, Vector? origin = null) { }
}

internal sealed partial class EngineVisibility
{




    public void ClearSkyboxParams() => E.ClearSkyboxParams();

    public void SetSkyboxParams(in SkyboxParams parameters) => E.SetSkyboxParams(in parameters);

    public bool TryStandalonePlayerSkyboxArea(int edictIndex, out int area)
        => E.EntitiesStandalonePlayerSkyboxArea(edictIndex, out area);


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void ActivateOccluder(int occluderIndex, bool active) { }
    public void ClientSetWorldRenderState(WorldRenderState which, ReadOnlySpan<float> values) { }
    public int ColorCorrectionAdd(string lookupFile) => 0;
    public void ColorCorrectionRemove(int handle) { }
    public void ColorCorrectionSetWeight(int handle, float weight) { }
    public void DynamicLightRelease(int key) { }
    public void DynamicLightUpdate(in DynamicLight light) { }
    public void SetViewerFogParams(in FogParams parameters) { }
    public void ClearViewerFogParams() { }
    public void SetViewerSkyboxParams(in SkyboxParams parameters) { }
    public void ClearViewerSkyboxParams() { }
}

internal sealed partial class EngineWeapons
{
    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void RegisterAmmoType(string name, DamageType damageType, int maxCarry, int tracerType = 0, float damageForce = 0f, float damage = 0f) { }
}

internal sealed partial class EngineWorldQuery
{
    public (Vector Right, Vector Up) AngleVectorsFromForward(Vector forward) => E.AngleVectorsFromForward(forward);

    public int EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into) => E.EntitiesInSphere(center, radius, into);

    public HullResult TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, EntityHandle ignore = default) => E.TraceHull(start, end, mins, maxs, mask, ignore);

    public EntityHullTrace EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs, uint mask, int collisionGroup) => E.EntityTraceHull(entity, start, end, mins, maxs, mask, collisionGroup);

    public TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default) => E.TraceLine(start, end, mask, ignore);

    public SurfaceTraceResult TraceLineSurface(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default) => E.TraceLineSurface(start, end, mask, ignore);

    public EntityHandle TracePlayerAim(EntityHandle player, float range, out Vector hitPoint) => E.TracePlayerAim(player, range, out hitPoint);

    public WaterBelow WaterSurfaceBelow(Vector start) => E.WaterSurfaceBelow(start);


    public int EntityModelSurfaceProp(EntityHandle entity) => E.EntityModelSurfaceProp(entity);

    // Through IPhysicsSurfaceProps' forwarders since 2026-10-19, where a slot made
    // the same call: the guard is its.
    public int SurfacePropIndex(string name)
        => string.IsNullOrEmpty(name) ? -1 : EngineBound.SurfaceProps(E).SurfaceIndex(name);


    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public ModelType? ClientEntityModelType(int entIndex) => null;
    public bool ClientEntityModelBounds(int entityIndex, out Vector mins, out Vector maxs,
                                        out Vector angles)
    {
        mins = Vector.Zero;
        maxs = Vector.Zero;
        angles = Vector.Zero;
        return false;
    }
    public ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask)
        => new ClientTraceResult(false, 1f, end, Vector.Zero, 0, -1, -1, 0);
    public ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask,
                                             TraceFilter filter, int passEntityIndex = -1)
        => new ClientTraceResult(false, 1f, end, Vector.Zero, 0, -1, -1, 0);
}
