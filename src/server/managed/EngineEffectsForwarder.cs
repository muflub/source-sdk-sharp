// Every member of IEngineEffects, forwarded to another one, so that a class
// which means to answer THREE of them does not have to reference the other
// fifty-two.
//
// WHY THIS IS BESIDE ITS ONE SUBCLASS, IN SourceSharp.Server. SideAuditTests
// Audit 2 fails any IEngine* member an assembly reaches that its side leaves
// null, and this class names every client-only effect. It used to live in
// SourceSharp.Abi for that reason alone. Since P1 run 3 the audit (SideReach)
// knows a pure forwarder is pass-through: a member whose whole body is
// `Inner.Same(args)` costs its caller what calling Inner would, so its own
// reference is not a use. What counts is what EffectsOverTempEnts overrides and
// calls, and what callers call through this class.
//
// The subclass then references only the members it actually overrides, which is
// the set Audit 2 is trying to ask about in the first place. Keep every member a
// one-line forward: a member that does anything else is a use again.

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IEngineEffects"/>, every member forwarded to an inner one and
/// every member virtual.
///
/// <para>
/// FOR DECORATING THE EFFECTS AREA, which is the one area that has a reason to
/// be decorated: plan_tmp.md Part 8 moves eight of its members off the engine
/// and onto managed temp entity tables while the other forty-five stay exactly
/// what they were. A subclass overrides the eight and inherits the rest.
/// </para>
///
/// <para>
/// It holds an <see cref="IEngineEffects"/> and never <c>Engine</c> directly, so
/// a test can put a <c>RecordingEngine</c> underneath a decorator and see
/// both halves: what the decorator answered itself, and what it passed on.
/// </para>
///
/// <para>
/// NOTHING HERE DECIDES ANYTHING, which is <c>EngineBinding.cs</c>'s rule
/// carried one layer out. A forwarder that special-cased a member would be a
/// second place for a rule to live, and the rule is already in the subclass that
/// overrode it.
/// </para>
/// </summary>
public abstract class EngineEffectsForwarder : IEngineEffects
{
    /// <summary>The one being decorated. Never <c>this</c>.</summary>
    protected readonly IEngineEffects Inner;

    protected EngineEffectsForwarder(IEngineEffects inner) => Inner = inner;

    public virtual void BloodEffect(Vector origin, Vector direction, BloodColor color, float amount, bool stream = false, BloodSpray sprayFlags = BloodSpray.None) => Inner.BloodEffect(origin, direction, color, amount, stream, sprayFlags);
    public virtual void BubbleEffect(EntityHandle entity, int count, float speed) => Inner.BubbleEffect(entity, count, speed);
    public virtual int TempEntRegister(TeClass description) => Inner.TempEntRegister(description);
    public virtual void TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay) => Inner.TempEntPlayback(handle, blob, filter, delay);
    public virtual int EffectDispatchIndex(string name) => Inner.EffectDispatchIndex(name);
    public virtual void ClientEnergySplash(Vector position, Vector direction, bool explosive) => Inner.ClientEnergySplash(position, direction, explosive);
    public virtual int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => Inner.DrawSprite(owner, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale, hdrColorScale, glowProxySize);
    public virtual int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => Inner.DrawSpriteOffset(owner, offset, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale, hdrColorScale, glowProxySize);
    public virtual bool ParticleSystemStart(EntityHandle owner, string name) => Inner.ParticleSystemStart(owner, name);
    public virtual void ParticleSystemStop(EntityHandle owner) => Inner.ParticleSystemStop(owner);
    public virtual bool ParticleSystemFollowPoints(EntityHandle owner, int pointMask) => Inner.ParticleSystemFollowPoints(owner, pointMask);
    public virtual void SpriteTrailUpdate(EntityHandle owner, int renderMode, bool visible) => Inner.SpriteTrailUpdate(owner, renderMode, visible);
    public virtual void SpriteTrailDestroy(EntityHandle owner) => Inner.SpriteTrailDestroy(owner);
    public virtual void DustEffect(Vector origin, Vector direction, float size, float speed) => Inner.DustEffect(origin, direction, size, speed);
    public virtual void ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity) => Inner.ImpactEffect(start, end, surfaceProp, damageType, entity);
    public virtual void FunnelEffect(Vector origin, int spriteIndex, bool reversed) => Inner.FunnelEffect(origin, spriteIndex, reversed);
    public virtual void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1) => Inner.MuzzleFlashEffect(origin, angles, scale, type);
    public virtual bool PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance, bool permanent, bool lowPriority) => Inner.PaintDecal(texture, origin, angles, projectDistance, permanent, lowPriority);
    public virtual void SparkEffect(Vector origin, int magnitude, int trailLength, bool makeSound = true, Vector? direction = null) => Inner.SparkEffect(origin, magnitude, trailLength, makeSound, direction);
    public virtual void ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate, int flags, int radius, int magnitude) => Inner.ExplosionEffect(origin, modelIndex, scale, frameRate, flags, radius, magnitude);
    public virtual void SplashEffect(Vector origin, float scale, bool inSlime = false) => Inner.SplashEffect(origin, scale, inSlime);
    public virtual void TracerEffect(Vector start, Vector end) => Inner.TracerEffect(start, end);

    // C-Phase 2's fifteen client backends. Added here for the same reason
    // every other member is: this class exists so that a lane adding to
    // IEngineEffects does not have to find every implementer of it.
    public virtual void ClientSparks(Vector position, int magnitude, int trailLength, Vector? direction = null) => Inner.ClientSparks(position, magnitude, trailLength, direction);
    public virtual void ClientRicochet(Vector position, Vector direction) => Inner.ClientRicochet(position, direction);
    public virtual void ClientMetalSparks(Vector position, Vector direction) => Inner.ClientMetalSparks(position, direction);
    public virtual void ClientSmoke(Vector origin, int modelIndex, float scale, float frameRate) => Inner.ClientSmoke(origin, modelIndex, scale, frameRate);
    public virtual void ClientMuzzleFlash(Vector origin, Vector angles, float scale, int type) => Inner.ClientMuzzleFlash(origin, angles, scale, type);
    public virtual void ClientDecalShoot(int decalIndex, int entIndex, Vector position, Vector? sAxis = null, int flags = 0) => Inner.ClientDecalShoot(decalIndex, entIndex, position, sAxis, flags);
    public virtual void ClientPlayerDecalShoot(Vector position, int player, int entIndex) => Inner.ClientPlayerDecalShoot(position, player, entIndex);
    public virtual int ClientDecalIndexFromName(string name) => Inner.ClientDecalIndexFromName(name);

    // THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP). Eight named
    // primitives, not one kind+params slot; Sparks reuses ClientSparks above.
    public virtual void ClientExplosionFireball(Vector origin) => Inner.ClientExplosionFireball(origin);

    // THE LEGACY TEMP ENTITY POOL (plan_tmp.md Part 5, C-Phase 4). Six slots
    // that reach ONE pooled C_LocalTempEntity by slot number.
    public virtual int TempEntPoolCapacity() => Inner.TempEntPoolCapacity();
    public virtual void TempEntPoolDestroy(int slot) => Inner.TempEntPoolDestroy(slot);
    public virtual bool TempEntPoolReadLife(int slot, out TempEntLife life) => Inner.TempEntPoolReadLife(slot, out life);
    public virtual bool TempEntPoolWriteLife(int slot, in TempEntLife life) => Inner.TempEntPoolWriteLife(slot, in life);
    public virtual bool TempEntPoolReadTransform(int slot, out TempEntTransform transform) => Inner.TempEntPoolReadTransform(slot, out transform);
    public virtual bool TempEntPoolWriteTransform(int slot, in TempEntTransform transform) => Inner.TempEntPoolWriteTransform(slot, in transform);
    public virtual float CurrentGravity() => Inner.CurrentGravity();
    public virtual bool ClientEntityOrigin(int entityIndex, out Vector origin) => Inner.ClientEntityOrigin(entityIndex, out origin);
    public virtual bool TempEntPoolSimulate(int slot, float frameTime, int frameNumber) => Inner.TempEntPoolSimulate(slot, frameTime, frameNumber);
    public virtual bool TempEntPoolAddVisible(int slot) => Inner.TempEntPoolAddVisible(slot);

    // C-Phase 5's four, on the same pool. Here for this class's own reason: a
    // lane adding to IEngineEffects must not have to find every implementer of
    // it, and this file plus RecordingEngine are the two the lane brief names
    // as the ones lanes keep missing.
    public virtual bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority) => Inner.TempEntPoolCreate(slot, origin, modelIndex, priority);
    public virtual bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn) => Inner.TempEntPoolWriteSpawn(slot, in spawn);
    public virtual string EffectDispatchName(int index) => Inner.EffectDispatchName(index);
    // C-Phase 3 lane CX's three, for the last three client decal halves. Here
    // for this class's own reason: a lane adding to IEngineEffects must not
    // have to find every implementer of it. The lane brief names this file and
    // RecordingEngine as the two sites lanes keep missing, and the forwarder
    // stopped compiling on the integration tip once because C2 and S5 never met.
    public virtual void ClientDecalStaticProp(Vector start, Vector end, int staticPropIndex, int decalIndex) => Inner.ClientDecalStaticProp(start, end, staticPropIndex, decalIndex);
    public virtual bool ClientIsLowViolence() => Inner.ClientIsLowViolence();
    public virtual string ClientDecalNameFromIndex(int decalIndex) => Inner.ClientDecalNameFromIndex(decalIndex);
    // C-Phase 3 lane DECALS's two drawing arms, closing behaviour ledger rows
    // C2 and C3. Here for the block above's reason.
    public virtual void ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayEnd, int decalIndex) => Inner.ClientDecalStudioModel(entIndex, rayStart, rayEnd, decalIndex);
    public virtual void ClientDecalBrushModel(int entIndex, Vector decalCenter, int decalIndex) => Inner.ClientDecalBrushModel(entIndex, decalCenter, decalIndex);

    // C-Phase 6 lane FRAME's two.
    public virtual bool TempEntPoolReadStatics(int slot, out TempEntStatics statics) => Inner.TempEntPoolReadStatics(slot, out statics);
    public virtual Vector ClientWindSpeed(float time) => Inner.ClientWindSpeed(time);
}
