// Entities: what they are, how they move, what they look like, and how they are
// hurt.

namespace SourceSharp.Abi;

/// <summary>
/// An entity's identity, where it is, and what it is worth: creation, removal,
/// naming, transform, health and the searches that find one.
///
/// <para>
/// The largest area, and it stays one rather than being split further because
/// these are the calls that nearly every entity makes and splitting them would
/// mean most classes asking for three interfaces to do one obvious thing.
/// </para>
/// </summary>
public interface IEngineEntities
{
    /// <summary>
    /// THE CLIENT'S INTERPOLATION LIST — plan_entity.md step 6a. See
    /// <see cref="IInterpolationBlocks"/> for why it is a list fetched once
    /// rather than an accessor called per entity.
    ///
    /// <para>
    /// <b>A DEFAULT, and a deliberate one.</b> Everything that implements this
    /// area other than the real binding is a test double or a stand-in for a
    /// side that has no such list, and every one of them would answer the same
    /// empty answer. A default keeps that answer in ONE place rather than
    /// copied into each of them, where one copy would eventually differ.
    /// </para>
    /// </summary>
    IInterpolationBlocks InterpolationBlocks => NoInterpolationBlocks.Instance;

    /// <summary>
    /// THE CLIENT'S RENDER-POLICY LIST — plan_entity.md step 6b. See
    /// <see cref="IRenderPolicyBlocks"/> for why it is a list fetched once
    /// rather than an accessor called per entity, and why it is a second list
    /// rather than 6a's.
    ///
    /// <para>
    /// A DEFAULT, for the reason <see cref="InterpolationBlocks"/> gives.
    /// </para>
    /// </summary>
    IRenderPolicyBlocks RenderPolicyBlocks => NoRenderPolicyBlocks.Instance;
    /// THE CLIENT'S REPLAY ENGINE — plan_entity.md step 6f. The three things
    /// the managed prediction cannot answer for itself: the engine's input
    /// ring, the movement bracket, and where native draws the answer.
    ///
    /// <para>
    /// Defaulted for the same reason the list above is: every implementation
    /// other than the real binding is a test double or a side with no replay,
    /// and all of them refuse identically.
    /// </para>
    /// </summary>
    IClientReplayEngine ClientReplay => NoClientReplayEngine.Instance;

    /// <inheritdoc cref="EngineSide.EntityPositionChanged"/>
    void PositionChanged(EntityHandle entity, Vector previousOrigin);

    /// <inheritdoc cref="EngineSide.EntityAddBaseVelocity"/>
    void AddBaseVelocity(EntityHandle entity, Vector velocity);

    /// <inheritdoc cref="EngineSide.EntityApplyVelocityImpulse"/>
    void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround);

    /// <inheritdoc cref="EngineSide.CreateEntity"/>
    EntityHandle Create(string className);

    /// <inheritdoc cref="EngineSide.MapSpawnCreate"/>
    EntityHandle CreateFromMap(string className, byte[] packedKeys);

    /// <summary>
    /// How many entities the engine is holding, against its hard limit of
    /// <see cref="EngineLimits.MaxEdicts"/>.
    ///
    /// <para>
    /// Not a count of managed instances: this is the engine's edict table,
    /// which entities on both hierarchies occupy a slot in. Zero where there is
    /// no engine, which a caller should read as "no reason to refuse".
    /// </para>
    /// </summary>
    int Count();

    // ---- plan_gamedll.md Part 4, Phase 6: two of LevelInit's nine steps.
    //
    // HERE RATHER THAN ON IEngineLevel because they are about the ENTITY LIST
    // and not about the load -- Part 4's own sketch spells them Entities.* --
    // and because CleanupDeleteList has a second caller waiting for it: every
    // point in the frame where something removed itself mid-think.

    /// <summary>Clears the map-entity reference list. Before the parse.</summary>
    void PurgeMapRefs();

    /// <summary>Reaps entities that removed themselves during a precache.</summary>
    void CleanupDeleteList();

    /// <inheritdoc cref="EngineSide.FindEntitiesByClassname"/>
    IEnumerable<EntityHandle> FindAllByClassname(string className);

    /// <inheritdoc cref="EngineSide.FindEntitiesByName"/>
    List<EntityHandle> FindAllByName(string name, int limit = 512);

    /// <inheritdoc cref="EngineSide.FindEntityByName"/>
    EntityHandle FindByName(string name);

    /// <inheritdoc cref="EntityIoRoutes.FireInput"/>
    bool FireInput(EntityHandle target, string input, string? value = null, EntityHandle activator = default);

    /// <inheritdoc cref="EngineSide.EntityGetAngles"/>
    Vector GetAngles(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetAngularVelocity"/>
    Vector GetAngularVelocity(EntityHandle entity);

    /// <summary>Player armour, which the engine keeps separately from health.</summary>
    int GetArmor(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetBounds"/>
    (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetBoundsSize"/>
    Vector GetBoundsSize(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetClassName"/>
    string GetClassName(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetEyeAngles"/>
    Vector GetEyeAngles(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetEyePosition"/>
    Vector GetEyePosition(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetHealth"/>
    int GetHealth(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetIndex"/>
    int GetIndex(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetName"/>
    string GetName(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetOrigin"/>
    Vector GetOrigin(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetSpawnAngles"/>
    Vector GetSpawnAngles(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetSpawnFlags"/>
    int GetSpawnFlags(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetSpawnOrigin"/>
    Vector GetSpawnOrigin(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetVelocity"/>
    Vector GetVelocity(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetWorldSpaceCenter"/>
    Vector GetWorldSpaceCenter(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityIsPlayer"/>
    bool IsPlayer(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityIsValid"/>
    bool IsValid(EntityHandle h);

    /// <inheritdoc cref="EngineSide.RemoveEntity"/>
    void Remove(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntitySetAngles"/>
    void SetAngles(EntityHandle entity, Vector angles);

    /// <inheritdoc cref="EngineSide.EntitySetAngularVelocity"/>
    void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond);

    /// <summary>armour left with CBasePlayer; Armour.cs is the managed owner.</summary>
    void SetArmor(EntityHandle entity, int armor);

    /// <inheritdoc cref="EngineSide.EntitySetHealth"/>
    void SetHealth(EntityHandle h, int health);

    /// <inheritdoc cref="EngineSide.EntitySetKeyValue"/>
    bool SetKeyValue(EntityHandle entity, string key, string value);

    /// <inheritdoc cref="EngineSide.EntitySetOrigin"/>
    void SetOrigin(EntityHandle h, Vector origin);

    /// <inheritdoc cref="EngineSide.EntitySetOwner"/>
    void SetOwner(EntityHandle h, EntityHandle owner);

    /// <inheritdoc cref="EngineSide.EntitySetVelocity"/>
    void SetVelocity(EntityHandle h, Vector velocity);

    /// <inheritdoc cref="EngineSide.SpawnEntity"/>
    void Spawn(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityTeleport"/>
    void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity);

    /// <inheritdoc cref="EngineSide.EntitySetParent"/>
    bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0);

    /// <inheritdoc cref="EngineSide.EntityGetParent"/>
    EntityHandle GetParent(EntityHandle child);

    /// <inheritdoc cref="EngineSide.EntityGetParentAttachment"/>
    int GetParentAttachment(EntityHandle child);

    /// <inheritdoc cref="EngineSide.EntitySetLocalTransform"/>
    void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles);

    /// <inheritdoc cref="EngineSide.EntityGetLocalTransform"/>
    void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles);

    // ---- what managed parenting asks of the world (step 2h, made live) -----
    //
    // ON THIS AREA rather than on a parenting one of their own, and the rule
    // this file already applies is why: an area is the SUBJECT the questions are
    // about, not the caller that asks them. All four are questions about an
    // ENTITY -- which hierarchy holds it, where a point on its model is, what an
    // attachment on it is called, and which entity a name means. That the only
    // caller today is SourceSharp.Abi's parenting is a fact about the caller.

    /// <inheritdoc cref="EngineSide.EntityParentProbe"/>
    ParentProbe ParentProbe(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetAttachmentFrame"/>
    bool GetAttachmentFrame(EntityHandle entity, int attachment,
                            out Vector origin, out QAngle angles);

    /// <summary>
    /// An attachment NAME to a one-based index, on either hierarchy; 0 for a
    /// name the model does not carry, the engine's own spelling. The server's
    /// managed skeletons answer it; managed calls no native slot for it
    /// (2026-10-18).
    /// </summary>
    int LookupAttachmentAny(EntityHandle entity, string attachmentName);

    /// <summary>
    /// THE SDK'S FIND-BY-NAME, which <see cref="FindEntityByName"/> is not: that
    /// one walks the shim's own vector and answers only the parallel hierarchy.
    /// Resolving a map's <c>parentname</c> has to reach both.
    /// </summary>
    EntityHandle FindBaseByName(string name);

    /// <inheritdoc cref="EngineSide.GetEntityBlock"/>
    Span<byte> GetEngineBlock(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityDestroyPhysics"/>
    void DestroyPhysics(EntityHandle entity);

    // ---- plan_gamedll_after.md Phase A2 ------------------------------------
    //
    // HERE RATHER THAN ON IEngineLevel for the same reason PurgeMapRefs is: it
    // is about the ENTITY LIST and not about a load. A model-cache flush is not
    // a level event at all -- it arrives whenever the engine drops its studio
    // data, which an operator can do mid-map.

    /// <inheritdoc cref="EngineSide.EntitiesInvalidateMdlCacheAll"/>
    int InvalidateMdlCacheAll();

    /// <inheritdoc cref="EngineSide.EntitiesUpdateVisibilityAll"/>
    int UpdateVisibilityAll();

    // ---- THE NATIVE HALF OF A SAVEGAME -------------------------------------
    //
    // docs/managed-saverestore.md 7g: a `load` in this port is a `map` plus a
    // managed overlay, so SavedEntity carries a NATIVE STORE and
    // ScriptedEntity.SaveNativeState / LoadNativeState read and write it.
    //
    // THEY GO THROUGH THIS ONE AREA, AND THAT IS THE WHOLE REASON THIS BLOCK
    // IS HERE. `Entities` is the only area ScriptedEntity binds -- see the
    // paragraph on that property -- so a native field reachable only through
    // some other area is a field whose save, restore and APPLY ORDER can be
    // checked in a game and nowhere else. Every field the section carries can
    // be both read and written here, which is what makes the RecordingEngine
    // able to prove the order.
    //
    // Three of the nine are a SECOND SPELLING of a call another area already
    // offers, not a second implementation: GetSolidFlags and SetGravity are
    // also on IEngineEntityPhysics, SetNextThink is also on IEngineScripted,
    // and all three forward to the same static wrapper over the same one ABI
    // slot. There is nothing for the two spellings to disagree about. The
    // alternative was binding a second area onto all 147 entity classes to
    // reach three methods, which is a change to every class's dependencies for
    // the benefit of one section.
    //
    // WHAT IS NOT HERE, and it is not an oversight: the COLLISION GROUP. It
    // has both directions already and could have been given a third spelling
    // exactly like these -- and it is left out because the pair does not round
    // trip. Engine.EntityGetCollisionGroup is many-to-one by design (this mod
    // names 5 of the engine's 22 groups and reads the rest back as None), so a
    // saved player -- COLLISION_GROUP_PLAYER, which is one of the 17 unnamed
    // ones -- would be restored into the neutral group. See NativeField.

    /// <inheritdoc cref="EngineSide.EntityGetGravity"/>
    float GetGravity(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntitySetGravity"/>
    void SetGravity(EntityHandle entity, float gravity);

    /// <inheritdoc cref="EngineSide.EntityGetSolidFlags"/>
    SolidFlags GetSolidFlags(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetSolidType"/>
    SolidType GetSolidType(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntitySetSolidity"/>
    void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags);

    /// <inheritdoc cref="EngineSide.EntitySetBounds"/>
    void SetBounds(EntityHandle entity, Vector mins, Vector maxs);

    /// <inheritdoc cref="EngineSide.EntitySetSpawnFlags"/>
    void SetSpawnFlags(EntityHandle entity, int spawnFlags);
}

/// <summary>
/// An entity as the physics solver sees it: mass, motion, forces, and what it
/// collides with.
///
/// <para>
/// Separate from <see cref="IEngineEntities"/> because most entities never touch
/// any of it. A logic_timer has no mass, and an interface that offered it one
/// would invite the question of what it means.
/// </para>
///
/// <para>
/// <b>Everything here is addressed by <see cref="EntityHandle"/>, and that is
/// the line between this interface and <see cref="IEnginePhysicsConstraints"/>
/// beside it.</b> These are questions about an ENTITY that happens to be
/// simulated — its mass, its collision group, whether it is solid — so the
/// entity names itself and the shell finds whatever physics object it has.
/// A constraint, a spring or a motion controller is not an entity: it is a
/// thing VPhysics owns that no entity owns, so it is named by a
/// <see cref="PhysicsId"/> instead (<c>plan_entity.md</c> §3.8). Both carry a
/// serial and both answer nothing when the thing they named has died; what
/// differs is what they name.
/// </para>
/// </summary>
public interface IEngineEntityPhysics
{
    /// <inheritdoc cref="EngineSide.EntityApplyForceCenter"/>
    void ApplyForceCenter(EntityHandle entity, Vector force);

    /// <inheritdoc cref="EngineSide.EntityApplyForceOffset"/>
    void ApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint);

    /// <inheritdoc cref="EngineSide.EntityApplyTorque"/>
    void ApplyTorque(EntityHandle entity, Vector torque);

    /// <inheritdoc cref="EngineSide.EntityBecomePhysicsTrigger"/>
    void BecomePhysicsTrigger(EntityHandle entity, bool isTrigger);

    /// <inheritdoc cref="EngineSide.EntityEnableMotion"/>
    void EnableMotion(EntityHandle entity, bool enable);

    /// <inheritdoc cref="EngineSide.EntityPhysicsWake"/>
    void Wake(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityPhysicsSleep"/>
    void Sleep(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityAddPhysicsGameFlags"/>
    void AddGameFlags(EntityHandle entity, uint flags);

    /// <inheritdoc cref="EngineSide.EntityGetMass"/>
    float GetMass(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityInitPhysics"/>
    bool InitPhysics(EntityHandle entity, bool asleep = false, bool debris = false);

    /// <inheritdoc cref="EngineSide.EntitySetBounce"/>
    void SetBounce(EntityHandle h, int bounces);

    /// <inheritdoc cref="EngineSide.EntitySetBrushSolidity"/>
    void SetBrushSolidity(EntityHandle entity, BrushSolidity solidity);

    /// <inheritdoc cref="EngineSide.EntityGetCollisionGroup"/>
    CollisionGroup GetCollisionGroup(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntityGetSolidFlags"/>
    SolidFlags GetSolidFlags(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntitySetCollisionGroup"/>
    void SetCollisionGroup(EntityHandle entity, CollisionGroup group);

    /// <summary>a per-entity friction on the movement path is not ported (func_friction, ss_gib ask).</summary>
    void SetFriction(EntityHandle h, float friction);

    /// <inheritdoc cref="EngineSide.EntitySetGravity"/>
    void SetGravity(EntityHandle h, float gravity);

    /// <inheritdoc cref="EngineSide.EntitySetMass"/>
    void SetMass(EntityHandle entity, float mass);

    /// <inheritdoc cref="EngineSide.EntitySetNotSolid"/>
    void SetNotSolid(EntityHandle entity, bool notSolid);


    /// <inheritdoc cref="EngineSide.EntityPhysicsObject"/>
    nint PhysicsObject(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.PhysicsEnvironmentPointer"/>
    nint EnvironmentPointer();


    /// <inheritdoc cref="EngineSide.PhysicsCollideExtent"/>
    Vector CollideExtent(nint physicsObject, Vector direction);

    /// <inheritdoc cref="EngineSide.PhysicsInertia"/>
    Vector Inertia(nint physicsObject);

    /// <inheritdoc cref="EngineSide.PhysicsWorldObject"/>
    nint WorldObject();

    /// <inheritdoc cref="EngineSide.EntitySetPhysicsStatic"/>
    void SetPhysicsStatic(EntityHandle entity, bool isStatic);

}

/// <summary>
/// What an entity looks like: its model, skin, sequence and whether it is drawn
/// at all.
///
/// <para>
/// Includes the parallel hierarchy's own draw controls, which are the same
/// question asked by the client half — how this entity is presented — rather than
/// a separate concern.
/// </para>
/// </summary>
public interface IEngineEntityRender
{
    /// <inheritdoc cref="EngineSide.EntityGetModelName"/>
    string GetModelName(EntityHandle entity);

    /// <summary>
    /// A precached model's <c>$keyvalues</c> text, by model name, or null for a
    /// name that is not a precached model. Through <c>IVModelInfo</c> directly
    /// (plan_server_novalve.md P2.4): no native code parses it any more.
    /// The default body is for test doubles that have no models; the engine
    /// binding and RecordingEngine both implement it.
    /// </summary>
    string? GetModelKeyValueText(string modelName) => null;

    /// <summary>
    /// Which frame of an animated material a brush is showing.
    ///
    /// <para>
    /// The frames belong to the material; which one is up belongs to the map.
    /// </para>
    /// </summary>
    int GetTextureFrame(EntityHandle entity);

    /// <summary>The sequence with this label, or -1. <see cref="ModelDescriptor.LookupSequence"/>.</summary>
    int LookupSequence(EntityHandle entity, string name);

    /// <summary>The FIRST sequence tagged with this activity, or -1. <see cref="ModelDescriptor.LookupSequenceByActivity"/>.</summary>
    int LookupSequenceByActivity(EntityHandle entity, string activityName);

    /// <summary>How long a sequence runs at the model's own default pose. <see cref="ModelDescriptor.SequenceDuration"/>.</summary>
    float SequenceDuration(EntityHandle entity, int sequence);

    /// <summary>How fast a sequence's root moves at the entity's pose, in units a second (0 for none); the skeleton's answer. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    float SequenceGroundSpeed(EntityHandle entity, int sequence);

    /// <summary>A name to its ONE-based attachment; 0 for none. The skeleton's answer. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    int LookupAttachment(EntityHandle entity, string name);

    /// <summary>A ONE-based attachment's world origin and angles; the entity's own place on failure. The skeleton's answer. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles);

    /// <summary>How many attachments the model carries. <see cref="ModelDescriptor.Attachments"/>.</summary>
    int AttachmentCount(EntityHandle entity);

    /// <summary>What the nth attachment is called, one-based. <see cref="ModelDescriptor.AttachmentName"/>.</summary>
    string AttachmentName(EntityHandle entity, int attachment);

    /// <summary>How many bones the model has, and how many the skeleton holds right now. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    int BoneCount(EntityHandle entity, out int cached);

    /// <summary>The skeleton, posed for everything a renderer would ask, or null. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    StandaloneBones? BoneMatrices(EntityHandle entity);

    /// <summary>Whether the model worn can be posed at all. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    bool IsPoseable(EntityHandle entity);

    /// <summary><c>studiohdr_t::name</c> of the model worn, or empty. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    string StudioName(EntityHandle entity);

    /// <summary>How many pose parameters the model declares. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    int PoseParameterCount(EntityHandle entity);

    /// <summary>A pose parameter by name, case-insensitive, or -1. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    int LookupPoseParameter(EntityHandle entity, string name);

    /// <summary>A pose parameter's name, or empty. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    string PoseParameterName(EntityHandle entity, int parameter);

    /// <summary>A pose parameter's declared range. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end);

    /// <summary>Set a pose parameter in model units; answers the value stored, in model units. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    float SetPoseParameter(EntityHandle entity, int parameter, float value);

    /// <summary>A pose parameter's value, in model units. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    float GetPoseParameter(EntityHandle entity, int parameter);

    /// <inheritdoc cref="EngineSide.StandaloneIsDormant"/>
    bool IsDormant(EntityHandle entity);

    /// <summary>How many animation events a sequence fires. <see cref="ModelDescriptor.EventsOf"/>.</summary>
    int SequenceEventCount(EntityHandle entity, int sequence);

    /// <summary>The playing sequence, its cycle now, its duration, loop flag and start count. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    bool SequenceState(EntityHandle entity, out int sequence, out float cycle,
                       out float duration, out bool looping, out int starts);

    /// <summary>The model's procedural bones by kind; answers the bone count. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains);

    /// <summary>One animation layer set, or switched off with a negative sequence. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight,
                      float playbackRate, int order, bool restart);

    /// <summary>What is in a layer slot; answers how many slots carry something. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight,
                  out int order, out float cycle);

    /// <summary>One animation event of a sequence, whole. <see cref="ModelDescriptor.EventsOf"/>.</summary>
    bool SequenceEvent(EntityHandle entity, int sequence, int index,
                       out float cycle, out int eventId, out int type,
                       out string name, out string options);

    /// <inheritdoc cref="EngineSide.StandaloneSetCustomDraw"/>
    void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs);

    /// <inheritdoc cref="EngineSide.StandaloneSetBlending"/>
    void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha);

    /// <inheritdoc cref="EngineSide.StandaloneSetShadowCast"/>
    void SetShadowCast(EntityHandle entity, ShadowCastChoice choice);

    /// <inheritdoc cref="EngineSide.StandaloneShadowCast"/>
    ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked, out bool hasShadow);

    /// <inheritdoc cref="EngineSide.StandaloneSetFade"/>
    void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f);

    /// <inheritdoc cref="EngineSide.SetEntityModel"/>
    void SetModel(EntityHandle entity, string model);

    /// <inheritdoc cref="EngineSide.StandaloneSetOverlayModel"/>
    void SetOverlayModel(EntityHandle entity, int modelIndex);

    /// <inheritdoc cref="EngineSide.StandaloneSetModelScale"/>
    void SetModelScale(EntityHandle entity, float scale);

    /// <summary>EF_BONEMERGE's follower half: the source by client entity index, -1 to stop. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex);

    /// <summary>
    /// Plays a sequence from the start at rate 1 (PlaySequence's fixed shape);
    /// a negative sequence is refused. The server's managed skeletons answer it
    /// (managed calls no native slot for it, 2026-10-18).
    /// </summary>
    void SetSequence(EntityHandle entity, int sequence);

    /// <inheritdoc cref="EngineSide.StandaloneSetViewModel"/>
    void SetViewModel(EntityHandle entity, bool enable);

    /// <summary>Play a sequence, from the start or keeping its clock; the skeleton keeps the clock. (plan physics_bones_native_zero 1.2: the skeleton route.)</summary>
    void PlaySequence(EntityHandle entity, int sequence, float playbackRate, bool restart);

    /// <inheritdoc cref="EngineSide.EntitySetSkin"/>
    void SetSkin(EntityHandle entity, int skin);

    /// <summary>m_nTextureFrameIndex left with CBaseEntity; env_texturetoggle asks.</summary>
    void SetTextureFrame(EntityHandle entity, int frame);

    /// <inheritdoc cref="EngineSide.EntitySetTransmitAlways"/>
    void SetTransmitAlways(EntityHandle entity, bool always);

    /// <summary>
    /// Stock's <c>CBaseEntity::DetectInSkybox</c>: raises <c>EFL_IN_SKYBOX</c>
    /// if this entity stands inside a 3D skybox, and answers whether it does.
    ///
    /// <para>
    /// A query managed code cannot make — it walks the sky camera list and asks
    /// the engine whether two areas are connected. The flag it raises has one
    /// consequence, and it is the one an env_smokestack in a skybox depends on:
    /// the entity is sent to everyone instead of being PVS-checked, because
    /// nobody's PVS contains the 3D skybox.
    /// </para>
    /// </summary>
    bool DetectInSkybox(EntityHandle entity);

    /// <inheritdoc cref="EngineSide.EntitySetVisible"/>
    void SetVisible(EntityHandle entity, bool visible);
}

/// <summary>
/// Hurting things. Direct damage, damage over an area, and the ways an entity can
/// be destroyed rather than merely reduced.
///
/// <para>
/// Its own area rather than part of <see cref="IEngineEntities"/> because damage
/// is where game rules live: a mod that changes nothing else changes this, and a
/// test of a rule wants to assert what damage was ASKED for without an engine to
/// apply it.
/// </para>
/// </summary>
/// <summary>
/// The rules' constants, pushed once at startup. See
/// <c>SourceSharp.Server.GameRulesPolicy</c> for what belongs in the table and
/// what must stay a managed call.
/// </summary>
/// <summary>
/// The collision matrix, pushed once at startup. See
/// <see cref="CollisionRules"/> for what it replaces and why both sides send it.
/// </summary>
public interface IEngineCollisionRules
{
    /// <inheritdoc cref="EngineSide.RegisterCollisionRules"/>
    bool RegisterCollisionRules(uint[] rows);
}

public interface IEngineGameRulesPolicy
{
    /// <inheritdoc cref="EngineSide.RegisterGameRulesPolicy"/>
    void RegisterPolicy(GameRulesPolicyBlob policy);
}

public interface IEngineDamage
{
    /// <inheritdoc cref="EngineSide.ApplyDamage"/>
    void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType);

    /// <inheritdoc cref="EngineSide.ApplyDamageForce"/>
    void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position);

    /// <summary>
    /// Whether this body, when it dies, becomes a ragdoll the server simulates
    /// rather than one the client makes for itself.
    ///
    /// Takes any entity and quietly does nothing unless it is a combat
    /// character, because "is this a combat character" is a native question:
    /// the flag lives on CBaseCombatCharacter and only its BecomeRagdoll reads
    /// it. A volume that sweeps everything walking through it would otherwise
    /// have to ask before every call and could not.
    /// </summary>
    void SetForceServerRagdoll(EntityHandle entity, bool force);

    /// <summary>
    /// Whether an entity may be hurt, answering what it was before.
    ///
    /// The previous value comes back rather than being remembered here, because
    /// invulnerability nests badly: only whoever took it away knows when it is
    /// done, and two things taking it away in turn must give back what each
    /// found rather than a shared idea of "normal".
    /// </summary>
    TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode);

    /// <inheritdoc cref="EngineSide.RegisterDamageRules"/>
    void RegisterRules(DamageRuleMasks masks);

    /// <inheritdoc cref="EngineSide.EntityTakeDamage"/>
    void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false);
}

/// <summary>
/// Moving brushes: doors, lifts, platforms and the panes a surface breaks into.
///
/// <para>
/// Not the physics solver. A door does not fall — it travels to a position at a
/// speed and reports arriving, which is a different mechanism with a different
/// failure mode.
/// </para>
/// </summary>
public interface IEngineBrushMovement
{
    /// <summary>Rotates a brush to an orientation at a constant speed.</summary>
    void MoveToAngles(EntityHandle entity, Vector destination, float speed);

    /// <summary>
    /// Moves a brush to a position at a constant speed, reporting arrival
    /// through OnMoveDone. The stepping is native: it has to stop exactly on the
    /// destination, and a door that stops a fraction short never closes.
    /// </summary>
    void MoveToPosition(EntityHandle entity, Vector destination, float speed);

    /// <inheritdoc cref="EngineSide.SurfaceSetEdges"/>
    void SetSurfaceEdges(EntityHandle entity, byte[] packed, int count);

    /// <summary>retired mover: EntityMotion implements this interface, so nothing reaches the engine binding's.</summary>
    void StopMoving(EntityHandle entity);

    /// <summary>
    /// Spin for ever, in degrees a second about each world axis.
    ///
    /// <para>
    /// On this interface rather than left as <c>IEngineEntities.SetAngularVelocity</c>
    /// because plan_entity.md step 2g made it one of the three things a MOVER is
    /// told: the continuous-spin arm of the integrator is the same arm that
    /// steps a rotation to a target, and a brush told to spin through a
    /// different route than a brush told to turn is two owners for one piece of
    /// state. The entity-level setter stays where it is for the physics
    /// solver's sake, and this forwards to it as well.
    /// </para>
    /// </summary>
    void Spin(EntityHandle entity, Vector degreesPerSecond);
}
