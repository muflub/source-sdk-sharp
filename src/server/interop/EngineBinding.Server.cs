using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

// The server's half of the engine bindings (plan_shim_areas_split phase 7): the
// members of the shared seams that only the server's engine serves, forwarded
// as in EngineBinding.cs, and the client's members, answered as a server
// engine answered them, from the client's attach guard, which is never
// attached here. Each class's other part, with its interface, is in
// src/shared/interop/EngineBinding.cs.

internal sealed unsafe partial class EngineBrushMovement
{
    public EngineBrushMovement(EngineSide side)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
    }

    public void Spin(EntityHandle entity, Vector degreesPerSecond) => E.EntitySetAngularVelocity(entity, degreesPerSecond);

    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void SetSurfaceEdges(EntityHandle entity, byte[] packed, int count) { }
}

internal sealed unsafe partial class EngineConsole
{
    private readonly ConsoleServerArea _consoleServerArea;

    public EngineConsole(EngineSide side, ConsoleArea consoleArea, ConsoleServerArea consoleServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _consoleArea = consoleArea ?? throw new ArgumentNullException(nameof(consoleArea));
        _consoleServerArea = consoleServerArea ?? throw new ArgumentNullException(nameof(consoleServerArea));
    }

    //-------------------------------------------------------------------------
    // Host.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Runs a console command on ONE client, as if that player had typed it.
    ///
    /// Whether it is obeyed is the client's business: a command it does not have,
    /// or one the client marks server-only, does nothing there and reports
    /// nothing back. So this cannot be used to ask a client a question — only to
    /// tell it something.
    /// </summary>
    public void SendClientCommand(EntityHandle player, string command)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        var bytes = Encoding.UTF8.GetBytes(command + "\0");
        fixed (byte* p = bytes) { _consoleServerArea.SendClientCommand(player.Value, p); }
    }
}

internal sealed unsafe partial class EngineDamage
{
    private readonly DamageArea _damageArea;
    private readonly GameRulesArea _gameRulesArea;

    public EngineDamage(EngineSide side, DamageArea damageArea, GameRulesArea gameRulesArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _damageArea = damageArea ?? throw new ArgumentNullException(nameof(damageArea));
        _gameRulesArea = gameRulesArea ?? throw new ArgumentNullException(nameof(gameRulesArea));
    }

    /// <summary>
    /// Applies damage through the normal pipeline, so the gamerules damage
    /// hooks and the death event still run.
    /// </summary>
    public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _damageArea.ApplyDamage(victim.Value, attacker.Value, damage, (int)damageType);
    }

    /// <summary>
    /// Dissolves an entity.
    /// </summary>
    /// <returns>
    /// False when there was nothing to dissolve. The effect is a shader over a
    /// STUDIO MODEL, so a brush or a logical entity has nothing for it to act
    /// on — worth reporting rather than swallowing, because a map that pointed
    /// one at a func_wall gets silence from stock.
    /// </returns>
    /// <summary>
    /// Damage carrying the physics force and the point it acts at, as stock's
    /// <c>CalculateBulletDamageForce</c> fills a <c>CTakeDamageInfo</c>: the
    /// victim's <c>OnTakeDamage</c> pushes its physics object with them
    /// (plan_physics_managed A30). The attacker resolves on either hierarchy.
    /// </summary>
    public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* f = stackalloc float[3] { force.X, force.Y, force.Z };
        float* p = stackalloc float[3] { position.X, position.Y, position.Z };
        _damageArea.ApplyDamageForce(victim.Value, attacker.Value, damage, (int)damageType, f, p);
    }

    /// <summary>
    /// Which kinds of damage behave which way. Stated once at startup, for the
    /// same reason the movement parameters are.
    ///
    /// The native side carries stock's values as its defaults, so a build where
    /// this is never called behaves like Half-Life rather than treating no
    /// damage as special — which matters because the table is read from damage
    /// paths that can run before the managed runtime is up on a listen server
    /// still loading.
    /// </summary>
    public void RegisterRules(DamageRuleMasks masks)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        _gameRulesArea.RegisterDamageRules(&masks);
    }

    /// <summary>
    /// Damage with a blast force behind it.
    ///
    /// <paramref name="direction"/> is a direction, not a force: the magnitude
    /// comes from the engine's own explosive-force calculation, which clamps it
    /// and jitters it per victim so a crowd caught in one blast does not fly
    /// apart in formation. Null applies no force at all.
    ///
    /// <paramref name="physicsOnly"/> takes the push without the health loss,
    /// which is what an explosion set to do no damage means.
    /// </summary>
    public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        if (direction is not Vector dir)
        {
            _damageArea.EntityTakeDamage(entity.Value, attacker.Value, damage, (int)damageType,
                                 null, null, physicsOnly ? 1 : 0);
            return;
        }

        float* d = stackalloc float[3] { dir.X, dir.Y, dir.Z };
        float* o = stackalloc float[3] { forceOrigin.X, forceOrigin.Y, forceOrigin.Z };

        _damageArea.EntityTakeDamage(entity.Value, attacker.Value, damage, (int)damageType,
                             d, o, physicsOnly ? 1 : 0);
    }
}

internal sealed unsafe partial class EngineDiagnostics
{
    private readonly DiagnosticsArea _diagnosticsArea;

    public EngineDiagnostics(EngineSide side, DiagnosticsArea diagnosticsArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _diagnosticsArea = diagnosticsArea ?? throw new ArgumentNullException(nameof(diagnosticsArea));
    }

    /// <summary>
    /// The player-shadow counters <c>cs_sv_playershadow</c> prints, copied into
    /// <paramref name="into"/> in native's order (enabled, built, updates,
    /// contacts, touches, snaps, live, outlived); how many were written. Zero
    /// with no engine.
    /// </summary>
    public int PlayerShadowCounts(Span<int> into)
    {
        if (!E.IsInitialized || !E.ShimsAttached || into.IsEmpty) return 0;

        fixed (int* p = into) { return _diagnosticsArea.PlayerShadowCounts(p, into.Length); }
    }

    /// <summary>
    /// Installs native's exit recorders -- the SIGTERM/SIGINT/SIGHUP handlers
    /// that note the sender and then run the handler that was there, and the
    /// SDL quit/window-close watch -- and names the file a fatal stop writes
    /// its record to. Once per process; a second arm only replaces the path.
    /// What was installed, as native's kExitWatch* bits.
    /// </summary>
    public int ExitWatchArm(string recordPath)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return 0;

        var bytes = Encoding.UTF8.GetBytes(recordPath + "\0");
        fixed (byte* p = bytes) { return _diagnosticsArea.ExitWatchArm(p); }
    }

    /// <summary>
    /// The exit recorders' facts, in native's ExitWatchFact_t order; how many
    /// were written. Zero with no engine.
    /// </summary>
    public int ExitWatchRead(Span<int> into)
    {
        if (!E.IsInitialized || !E.ShimsAttached || into.IsEmpty) return 0;

        fixed (int* p = into) { return _diagnosticsArea.ExitWatchRead(p, into.Length); }
    }

    /// <summary>
    /// Takes the exit recorders back out: the signal handlers are restored
    /// and SDL's quit/window-close watch is REMOVED, so the last engine
    /// frames after this assembly's module is unmapped can never call into
    /// freed code. The facts already recorded are kept. What was installed,
    /// as native's kExitWatch* bits (0 when nothing was armed).
    /// </summary>
    public int ExitWatchDisarm()
    {
        if (!E.IsInitialized || !E.ShimsAttached) return 0;

        return _diagnosticsArea.ExitWatchDisarm();
    }
}

internal sealed unsafe partial class EngineEffects
{
    private readonly EffectsServerArea _effectsServerArea;

    public EngineEffects(EngineSide side,
                         EffectsArea effectsArea,
                         EffectsServerArea effectsServerArea,
                         MovementArea movementArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _effectsArea = effectsArea ?? throw new ArgumentNullException(nameof(effectsArea));
        _effectsServerArea = effectsServerArea ?? throw new ArgumentNullException(nameof(effectsServerArea));
        _movementArea = movementArea ?? throw new ArgumentNullException(nameof(movementArea));
    }

    //-------------------------------------------------------------------------
    // The small effects.
    //
    // Each is a temp entity written straight to the wire with no server-side
    // object behind it, which is why none of them answers anything: there is
    // nothing to hold on to afterwards. What the calling entity decides — when,
    // how much, which way, and from which of several positions — never appears
    // here.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Blood at a point, thrown along a direction.
    /// </summary>
    /// <param name="stream">
    /// The arterial spurt rather than the drips. A different injury rather than
    /// more of the same one, which is why it is a choice and not an amount.
    /// </param>
    /// <param name="sprayFlags">
    /// Source's newer cloud/drops/gore effects, as <see cref="BloodSpray"/>
    /// bits. <see cref="BloodSpray.None"/> is what a map predating them gets.
    /// </param>
    public void BloodEffect(Vector origin, Vector direction, BloodColor color, float amount, bool stream = false, BloodSpray sprayFlags = BloodSpray.None)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* d = stackalloc float[3] { direction.X, direction.Y, direction.Z };

        _effectsServerArea.BloodEffect(o, d, (int)color, amount, stream ? 1 : 0, (int)sprayFlags);
    }

    /// <summary>
    /// Bubbles rising out of an entity's volume.
    ///
    /// <para>
    /// Takes the entity rather than a pair of corners, because the volume is
    /// the brush the mapper drew and its world-space bounds are the engine's to
    /// read.
    /// </para>
    /// </summary>
    public void BubbleEffect(EntityHandle entity, int count, float speed)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;
        _effectsServerArea.BubbleEffect(entity.Value, count, speed);
    }

    /// <summary>
    /// Sends one. The blob is the packed fields, addressed by the offsets the
    /// description carries; native refuses a blob shorter than the declared size
    /// rather than sending whatever follows it.
    /// </summary>
    public void TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay)
    {
        if (!E.IsInitialized || !E.ShimsAttached || handle < 0 || blob.IsEmpty)
            return;

        var wire = new TeFilterDesc
        {
            Kind                 = (int)filter.Kind,
            PlayerIndex          = filter.PlayerIndex,
            Attenuation          = filter.Attenuation,
            IgnorePredictionCull = filter.IgnorePredictionCull ? 1 : 0,
        };

        wire.Origin[0] = filter.Origin.X;
        wire.Origin[1] = filter.Origin.Y;
        wire.Origin[2] = filter.Origin.Z;

        fixed (byte* pBlob = blob)
        {
            _effectsServerArea.TempEntPlayback(handle, pBlob, blob.Length, &wire, delay);
        }
    }

    /// <summary>
    /// An effect NAME, as the index the wire carries for it.
    ///
    /// <para>
    /// SERVER ONLY. The "EffectDispatch" network string table is a server-side
    /// engine interface; the client only ever turns an index back into a name
    /// to pick a callback. A client that asked gets 0 — the table's first
    /// entry — which is what every null-guarded slot in this file answers and
    /// is not a failure a caller can do anything about.
    /// </para>
    ///
    /// <para>
    /// THERE IS ONE NUMBERING AND THE ENGINE OWNS IT. Resolving a name in
    /// managed code instead would be a second numbering of a table the engine
    /// already keeps, and the symptom of the two disagreeing is not a missing
    /// effect but the WRONG one — which reads as a bug in that effect rather
    /// than in this port. Adding a name already present answers the index it
    /// already has, so this is idempotent for the life of the level.
    /// </para>
    /// </summary>
    public int EffectDispatchIndex(string name)
    {
        if (!E.IsInitialized || !E.ShimsAttached || name.Length == 0)
            return 0;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");

        fixed (byte* p = bytes) { return _effectsServerArea.EffectDispatchIndex(p); }
    }

    /// <summary>
    /// A puff of dust, thrown along a direction.
    ///
    /// <para>
    /// The direction is normalised on the far side, because the engine asserts
    /// on one that is not a unit vector — a debug build stops on it rather than
    /// drawing something odd.
    /// </para>
    /// </summary>
    public void DustEffect(Vector origin, Vector direction, float size, float speed)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* d = stackalloc float[3] { direction.X, direction.Y, direction.Z };

        _effectsServerArea.DustEffect(o, d, size, speed);
    }

    /// <summary>
    /// The engine's own bullet impact at a point: the decal, the particles and
    /// the sound, all of which it picks from the surfaceprop it is handed.
    ///
    /// <para>
    /// <paramref name="surfaceProp"/> is passed in rather than traced for,
    /// which is the whole point of the call: a caller that knows what the
    /// thing it hit is really made of can say so. Negative means "whatever the
    /// struck face says", which is what every caller with nothing better to
    /// offer passes — see <see cref="EntityModelSurfaceProp"/>.
    /// </para>
    /// </summary>
    public void ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };

        _effectsServerArea.ImpactEffect(s, e, surfaceProp, (int)damageType, entity.Value);
    }

    /// <summary>
    /// The funnel of sprites that converges on a point, or flies out of one.
    /// </summary>
    /// <param name="spriteIndex">A precached sprite, as its model index.</param>
    /// <param name="reversed">Outward rather than inward.</param>
    public void FunnelEffect(Vector origin, int spriteIndex, bool reversed)
    {
        if (!E.IsInitialized || !E.ShimsAttached || spriteIndex <= 0) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        _effectsServerArea.FunnelEffect(o, spriteIndex, reversed ? 1 : 0);
    }

    /// <summary>
    /// A muzzle flash at a point, pointed somewhere.
    ///
    /// <para>
    /// A temp entity carrying a position, a set of angles and a size — and no
    /// entity index. That is the whole of why this one crosses: the rest of the
    /// particle and trail family delivers its effect by being a networked entity
    /// whose stock client class runs the emitter, and a managed entity never
    /// appears on the wire as that class.
    /// </para>
    /// </summary>
    /// <param name="angles">Pitch, yaw and roll, in the order an entity's angles are already held.</param>
    /// <param name="type">Stock's MUZZLEFLASH_TYPE_DEFAULT is 1, which is the only value stock's env_muzzleflash ever passes.</param>
    public void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[3] { angles.X, angles.Y, angles.Z };

        _effectsServerArea.MuzzleFlashEffect(o, a, scale, type);
    }

    /// <summary>
    /// Paints one map decal. See PaintDecal in sourcesharp.h.
    ///
    /// <para>
    /// <paramref name="angles"/> picks the FORM: null traces a short line
    /// through <paramref name="origin"/> and stamps whatever it found, which is
    /// <c>infodecal</c>; a value throws the mark along that facing for
    /// <paramref name="projectDistance"/> units, which is
    /// <c>info_projecteddecal</c>.
    /// </para>
    ///
    /// <para>
    /// <paramref name="permanent"/> picks the DELIVERY, and it is the whole
    /// difference between a decal a map was built with and one it sprayed
    /// later: permanent goes into the signon data and reaches a client that
    /// joins an hour afterwards, temporary goes out once to whoever is
    /// connected now.
    /// </para>
    /// </summary>
    public bool PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance, bool permanent, bool lowPriority)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return false;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[3];

        if (angles.HasValue)
        {
            a[0] = angles.Value.X;
            a[1] = angles.Value.Y;
            a[2] = angles.Value.Z;
        }

        return _effectsServerArea.PaintDecal(texture, o, angles.HasValue ? a : null, projectDistance,
                              permanent ? 1 : 0, lowPriority ? 1 : 0) != 0;
    }

    /// <summary>
    /// A shower of sparks at a point.
    /// </summary>
    /// <param name="direction">
    /// Null throws them in every direction, which is what a spark with no
    /// facing means — not a direction of zero length.
    /// </param>
    public void SparkEffect(Vector origin, int magnitude, int trailLength, bool makeSound = true, Vector? direction = null)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        if (direction is not Vector dir)
        {
            _effectsServerArea.SparkEffect(o, magnitude, trailLength, makeSound ? 1 : 0, null);
            return;
        }

        float* d = stackalloc float[3] { dir.X, dir.Y, dir.Z };
        _effectsServerArea.SparkEffect(o, magnitude, trailLength, makeSound ? 1 : 0, d);
    }

    // Engine.EntityFireInput WAS HERE, AND IS DELETED WITH ITS SLOT --
    // plan_entity.md step 2a. Firing an input at an entity does not touch the
    // engine any more: the delivery is EntityDirectory's and the route is
    // EntityIoRoutes.FireInput, which is what IEngineEntities.FireInput calls.
    // A managed wrapper around a deleted slot would be a member EngineAreaTests
    // correctly reports as forwarded by nothing.

    /// <summary>
    /// The explosion temp entity: fireball, sound, particles, dynamic lights.
    ///
    /// <para>
    /// Presentation ONLY — this hurts nobody. <see cref="ExplosionDamage"/> is
    /// the other half, and env_explosion fires both because its spawnflags can
    /// suppress either one independently.
    /// </para>
    ///
    /// <para>
    /// <paramref name="flags"/> is the TE_EXPLFLAG_* set, NOT the entity's
    /// spawnflags. The recipient set is a PVS query made native-side from the
    /// origin: an explosion written to an empty filter is silently invisible.
    /// </para>
    /// </summary>
    public void ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate, int flags, int radius, int magnitude)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* p = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        _effectsServerArea.ExplosionEffect(p, modelIndex, scale, frameRate, flags, radius, magnitude);
    }

    /// <summary>A splash of water at a point. Slime is the green one.</summary>
    public void SplashEffect(Vector origin, float scale, bool inSlime = false)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        _effectsServerArea.SplashEffect(o, scale, inSlime ? 1 : 0);
    }

    /// <summary>One bullet streak, drawn between two points.</summary>
    public void TracerEffect(Vector start, Vector end)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };

        _effectsServerArea.TracerEffect(s, e);
    }

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
    public void ClientExplosionFireball(Vector origin) { }
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

internal sealed unsafe partial class EngineEntities
{
    private readonly NetworkServerArea _networkServerArea;
    private readonly CollisionServerArea _collisionServerArea;
    private readonly EntitiesServerArea _entitiesServerArea;
    private readonly EntityLifecycleArea _entityLifecycleArea;
    private readonly HierarchyArea _hierarchyArea;
    private readonly PhysicsArea _physicsArea;
    private readonly TransformArea _transformArea;

    /// <summary>
    /// A side with no client fetches: the server, and every container that
    /// registers none. Each answers nothing, which is exactly what the real
    /// ones answered there (their slots are client-only, so every Has_ was
    /// false).
    /// </summary>
    public EngineEntities(EngineSide side,
                          EntitiesArea entitiesArea,
                          ModelsArea modelsArea,
                          NetworkServerArea networkServerArea,
                          CollisionServerArea collisionServerArea,
                          EntitiesServerArea entitiesServerArea,
                          EntityLifecycleArea entityLifecycleArea,
                          HierarchyArea hierarchyArea,
                          PhysicsArea physicsArea,
                          TransformArea transformArea)
        : this(side, NoInterpolationBlocks.Instance, NoRenderPolicyBlocks.Instance, NoClientReplayEngine.Instance,
               entitiesArea, modelsArea, networkServerArea, collisionServerArea, entitiesServerArea, entityLifecycleArea, hierarchyArea, physicsArea, transformArea)
    {
    }

    /// <summary>
    /// The server's: no client fetches, and its attachment frames answered by
    /// its managed skeletons once the composition fills the route (plan
    /// physics_bones_native_zero step 1.2).
    /// </summary>
    public EngineEntities(EngineSide side,
                          StandaloneSkeletonRoute skeletons,
                          EntitiesArea entitiesArea,
                          ModelsArea modelsArea,
                          NetworkServerArea networkServerArea,
                          CollisionServerArea collisionServerArea,
                          EntitiesServerArea entitiesServerArea,
                          EntityLifecycleArea entityLifecycleArea,
                          HierarchyArea hierarchyArea,
                          PhysicsArea physicsArea,
                          TransformArea transformArea)
        : this(side, NoInterpolationBlocks.Instance, NoRenderPolicyBlocks.Instance, NoClientReplayEngine.Instance,
               entitiesArea, modelsArea, networkServerArea, collisionServerArea, entitiesServerArea, entityLifecycleArea, hierarchyArea, physicsArea, transformArea)
    {
        _skeletons = skeletons ?? throw new ArgumentNullException(nameof(skeletons));
    }

    /// <summary>
    /// The client's: the three held objects read the client's own slots, and
    /// the client composition builds them and hands them in (P1 run 3: they
    /// are client code, so the shared binding names only their interfaces).
    /// EngineServices.Build picks this constructor when the container holds
    /// all three.
    /// </summary>
    public EngineEntities(EngineSide side,
                          IInterpolationBlocks interpolationBlocks,
                          IRenderPolicyBlocks renderPolicyBlocks,
                          IClientReplayEngine clientReplay,
                          EntitiesArea entitiesArea,
                          ModelsArea modelsArea,
                          NetworkServerArea networkServerArea,
                          CollisionServerArea collisionServerArea,
                          EntitiesServerArea entitiesServerArea,
                          EntityLifecycleArea entityLifecycleArea,
                          HierarchyArea hierarchyArea,
                          PhysicsArea physicsArea,
                          TransformArea transformArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _interpolationBlocks = interpolationBlocks ?? throw new ArgumentNullException(nameof(interpolationBlocks));
        _renderPolicyBlocks = renderPolicyBlocks ?? throw new ArgumentNullException(nameof(renderPolicyBlocks));
        _clientReplay = clientReplay ?? throw new ArgumentNullException(nameof(clientReplay));
        _entitiesArea = entitiesArea ?? throw new ArgumentNullException(nameof(entitiesArea));
        _modelsArea = modelsArea ?? throw new ArgumentNullException(nameof(modelsArea));
        _networkServerArea = networkServerArea ?? throw new ArgumentNullException(nameof(networkServerArea));
        _collisionServerArea = collisionServerArea ?? throw new ArgumentNullException(nameof(collisionServerArea));
        _entitiesServerArea = entitiesServerArea ?? throw new ArgumentNullException(nameof(entitiesServerArea));
        _entityLifecycleArea = entityLifecycleArea ?? throw new ArgumentNullException(nameof(entityLifecycleArea));
        _hierarchyArea = hierarchyArea ?? throw new ArgumentNullException(nameof(hierarchyArea));
        _physicsArea = physicsArea ?? throw new ArgumentNullException(nameof(physicsArea));
        _transformArea = transformArea ?? throw new ArgumentNullException(nameof(transformArea));
    }

    /// <summary>
    /// Adds to base velocity — the engine's "carried along by something"
    /// channel, and the only way to shove a player that the movement code will
    /// not undo on the next tick.
    /// </summary>
    public void AddBaseVelocity(EntityHandle entity, Vector velocity)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* v = stackalloc float[3] { velocity.X, velocity.Y, velocity.Z };
        _transformArea.EntityAddBaseVelocity(entity.Value, v);
    }

    public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => E.EntityApplyVelocityImpulse(h, impulse, clearGround);
    //-------------------------------------------------------------------------
    // World items. Enough engine support for C# to define a pickup; what the
    // thing is and what touching it does stays in managed code.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Creates an entity of a registered class. Returns
    /// <see cref="EntityHandle.None"/> if no such class exists.
    /// </summary>
    public EntityHandle Create(string className)
    {
        // A CLASS OF KIND Logical MAKES NO SHELL, SO IT DOES NOT CROSS TO MAKE
        // ONE -- plan_entity.md step 2e. This is the runtime creator's route
        // (ent_create, a point_template instantiating its sources, a class
        // spawning a helper); MapSpawnCreate below is the map's.
        if (E.ShellLess.ClassIsShellLess(className))
            return E.ShellLess.Mine!.Create(className, default);

        if (!E.IsInitialized || !E.ShimsAttached) return EntityHandle.None;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");
        fixed (byte* p = bytes) { return new EntityHandle(_entityLifecycleArea.CreateEntity(p)); }
    }

    public EntityHandle CreateFromMap(string className, byte[] packedKeys)
        => MapSpawnCreate(className, packedKeys);

    public Vector GetAngularVelocity(EntityHandle entity) => E.EntityGetAngularVelocity(entity);
    /// <summary>
    /// The collision box as offsets from the entity's origin — what a hull trace
    /// wants.
    ///
    /// <para>
    /// Not interchangeable with <see cref="EntityGetBoundsSize"/>: a size can
    /// only be centred on the origin, and most prop models put their origin on
    /// the BASE. Halving a size therefore builds a hull that sinks through the
    /// floor, and a room check made with it is blocked everywhere.
    /// </para>
    /// </summary>
    public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity)
    {
        // A POINT -- step 2e. SOLID_NONE with no model is what the kind table
        // installed for Logical, so the shell's own bounds were zero too.
        if (E.ShellLess.Claims(entity))
            return (Vector.Zero, Vector.Zero);

        if (!E.IsInitialized || !E.ShimsAttached)
            return (Vector.Zero, Vector.Zero);

        float* lo = stackalloc float[3];
        float* hi = stackalloc float[3];

        _collisionServerArea.EntityGetBounds(entity.Value, lo, hi);

        return (new Vector(lo[0], lo[1], lo[2]), new Vector(hi[0], hi[1], hi[2]));
    }

    /// <summary>How big a brush is, as its collision bounds.</summary>
    public Vector GetBoundsSize(EntityHandle entity)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return default;

        float* xyz = stackalloc float[3];
        _collisionServerArea.EntityGetBoundsSize(entity.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    public string GetClassName(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (E.ShellLess.State(h) is { } shellLess)
            return shellLess.ClassName;

        if (!E.IsInitialized || !E.ShimsAttached) return string.Empty;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        _entitiesServerArea.EntityGetClassName(h.Value, buf, bufSize);
        return EngineSide.FromUtf8(buf);
    }

    /// <summary>Which way something is looking. Not its body angles.</summary>
    public Vector GetEyeAngles(EntityHandle h)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return default;

        float* pyr = stackalloc float[3];
        _transformArea.EntityGetEyeAngles(h.Value, pyr);
        return new Vector(pyr[0], pyr[1], pyr[2]);
    }

    public Vector GetEyePosition(EntityHandle h) => E.EntityGetEyePosition(h);
    public int GetHealth(EntityHandle h)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (E.ShellLess.State(h) is { } shellLess)
            return shellLess.Health;

        return (E.IsInitialized && E.ShimsAttached) ? _entitiesServerArea.EntityGetHealth(h.Value) : 0;
    }

    /// <summary>The name a mapper gave an entity. Empty if it has none.</summary>
    public string GetName(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (E.ShellLess.State(entity) is { } shellLess)
            return shellLess.Name;

        if (!E.IsInitialized || !E.ShimsAttached) return string.Empty;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        _entitiesServerArea.EntityGetName(entity.Value, buf, bufSize);
        return EngineSide.FromUtf8(buf);
    }

    public Vector GetSpawnAngles(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (E.ShellLess.State(entity) is { } shellLess)
            return shellLess.SpawnAngles;

        if (!E.IsInitialized || !E.ShimsAttached) return default;

        float* pyr = stackalloc float[3];
        _entitiesServerArea.EntityGetSpawnAngles(entity.Value, pyr);
        return new Vector(pyr[0], pyr[1], pyr[2]);
    }

    /// <summary>Spawnflags the map set on an entity.</summary>
    public int GetSpawnFlags(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (E.ShellLess.State(entity) is { } shellLess)
            return shellLess.SpawnFlags;

        return (E.IsInitialized && E.ShimsAttached) ? _entitiesServerArea.EntityGetSpawnFlags(entity.Value) : 0;
    }

    /// <summary>
    /// Where the mapper drew the brush. Every door in Source is described as an
    /// offset from this rather than in world coordinates.
    /// </summary>
    public Vector GetSpawnOrigin(EntityHandle entity)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (E.ShellLess.State(entity) is { } shellLess)
            return shellLess.SpawnOrigin;

        if (!E.IsInitialized || !E.ShimsAttached) return default;

        float* xyz = stackalloc float[3];
        _entitiesServerArea.EntityGetSpawnOrigin(entity.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    public Vector GetVelocity(EntityHandle h) => E.EntityGetVelocity(h);
    /// <summary>
    /// The middle of something's bounding box. A brush entity's origin is
    /// usually at the map's world origin rather than anywhere near the brush.
    /// </summary>
    public Vector GetWorldSpaceCenter(EntityHandle h)
    {
        // THE ORIGIN, because a point entity's bounds are a point -- step 2e.
        // WorldSpaceCenter is the middle of the collision bounds, and a
        // shell-less entity has none: stock's CBaseEntity::WorldSpaceCenter
        // over a zero-extent OBB answers the origin exactly.
        if (E.ShellLess.State(h) is { } shellLess)
            return shellLess.Origin;

        if (!E.IsInitialized || !E.ShimsAttached) return default;

        float* xyz = stackalloc float[3];
        _transformArea.EntityGetWorldSpaceCenter(h.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>
    /// Whether a handle names a player.
    ///
    /// Cheap enough for a touch path — it is a virtual call and a comparison —
    /// which matters, because the rule that uses it runs every tick something
    /// is standing in a trigger.
    /// </summary>
    public bool IsPlayer(EntityHandle h)
    {
        // FALSE WITHOUT ASKING -- step 2e. A player is kind Player, which is
        // networked and has a shell; a shell-less entity can never be one.
        if (E.ShellLess.Claims(h))
            return false;

        return E.IsInitialized && E.ShimsAttached && _entitiesServerArea.EntityIsPlayer(h.Value) != 0;
    }

    /// <summary>Sets an entity's orientation.</summary>
    public void SetAngles(EntityHandle entity, Vector angles)
    {
        if (E.ShellLess.State(entity) is { } shellLess)
        {
            shellLess.Angles = angles;
            return;
        }

        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* pyr = stackalloc float[3] { angles.X, angles.Y, angles.Z };
        _transformArea.EntitySetAngles(entity.Value, pyr);
    }

    public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => E.EntitySetAngularVelocity(entity, degreesPerSecond);
    public void SetHealth(EntityHandle h, int health)
    {
        if (E.ShellLess.State(h) is { } shellLess)
        {
            shellLess.Health = health;
            return;
        }

        if (E.IsInitialized && E.ShimsAttached)
            _entitiesServerArea.EntitySetHealth(h.Value, health);
    }

    /// <summary>
    /// Hands an entity one of a map's own keys, after it has spawned.
    ///
    /// The general form rather than a setter per field, because a keyvalue is
    /// the one channel every entity in this build understands and it is the
    /// only one that reaches both kinds of storage. A stock entity resolves the
    /// key against its datadesc and writes the member bound to it; an entity on
    /// the parallel hierarchy checks the few keys the shell claims and then
    /// offers it to its managed class, where it lands in a [ScriptVars] key.
    /// Neither kind has to be identified by the caller.
    ///
    /// Returns whether anything claimed the key. A key nobody wanted is a
    /// mapper's typo, and only the caller is in a position to say so.
    /// </summary>
    public bool SetKeyValue(EntityHandle entity, string key, string value)
    {
        // A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES -- step 2e.
        if (E.ShellLess.Claims(entity))
            return E.ShellLess.Mine!.KeyValue(entity, key, value);

        if (!E.IsInitialized || !E.ShimsAttached) return false;

        var keyBytes = Encoding.UTF8.GetBytes(key + "\0");
        var valueBytes = Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* pKey = keyBytes)
        fixed (byte* pValue = valueBytes)
        {
            return _entitiesServerArea.EntitySetKeyValue(entity.Value, pKey, pValue) != 0;
        }
    }

    public void SetOrigin(EntityHandle h, Vector origin)
    {
        if (E.ShellLess.State(h) is { } shellLess)
        {
            shellLess.Origin = origin;
            return;
        }

        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* xyz = stackalloc float[3];
        xyz[0] = origin.X; xyz[1] = origin.Y; xyz[2] = origin.Z;
        _transformArea.EntitySetOrigin(h.Value, xyz);
    }

    /// <summary>
    /// Who owns an entity, which for a projectile is who fired it.
    ///
    /// This is what stops a projectile hitting its own shooter, and it has to
    /// be the engine's business rather than the entity's: the trace that
    /// carries a projectile forward is what decides the flight ended, and a
    /// managed check on the touch happens after that decision.
    /// </summary>
    public void SetOwner(EntityHandle h, EntityHandle owner)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _entitiesServerArea.EntitySetOwner(h.Value, owner.Value);
    }

    public void SetVelocity(EntityHandle h, Vector velocity) => E.EntitySetVelocity(h, velocity);
    /// <summary>
    /// Spawns an entity that <see cref="CreateEntity"/> made.
    ///
    /// Two steps rather than one because what goes BETWEEN them matters: Source
    /// hands an entity its keyvalues and its position before it spawns, and
    /// spawning is what reads them. An entity created and never spawned has no
    /// model, no physics and no think — it sits where it was put, inert, and
    /// looks like one that spawned wrong rather than one that never spawned.
    /// </summary>
    public void Spawn(EntityHandle entity)
    {
        // A shell-less entity spawns in managed code, in the same position of
        // the same pass -- step 2e. The crossing this replaces was outbound and
        // is therefore not in any Crossings counter; it is recorded in the
        // step's handover rather than left for the performance pass to find.
        if (E.ShellLess.Claims(entity))
        {
            E.ShellLess.Mine!.Spawn(entity);
            return;
        }

        if (E.IsInitialized && E.ShimsAttached)
            _entityLifecycleArea.EntitySpawn(entity.Value);
    }

    /// <summary>
    /// Moves something somewhere, optionally facing a new way. One call rather
    /// than origin and angles separately, because the engine unlinks and relinks
    /// as one operation.
    ///
    /// <para>
    /// <b>AND IT SAYS SO, which is the half the client needs</b> — see
    /// <see cref="TeleportDiscontinuity"/>. Every teleport on this hierarchy
    /// arrives here, so bumping the counter on THIS side of the crossing covers
    /// <c>point_teleport</c>, <c>trigger_teleport</c>, the trains, the dev
    /// commands and any managed caller yet to be written, without a line of C++.
    /// Without it the new pose is just a pose a long way from the last one, and
    /// the client draws the entity travelling the whole distance.
    /// </para>
    ///
    /// <para>
    /// The bump is announced with <see cref="MarkEntityBlockChanged"/> rather
    /// than left to the origin's own mark: the two are different SendProps and
    /// <c>MapPropOffsetsToIndices</c> matches an offset EXACTLY, so a marked
    /// origin says nothing at all about the word beside it.
    /// </para>
    /// </summary>
    public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        if (TeleportDiscontinuity.Bump(E.GetEntityBlock(_networkServerArea, h), out _))
            E.MarkEntityBlockChanged(_networkServerArea, h, TeleportDiscontinuity.FrameOffset);

        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        if (angles is not Vector a)
        {
            _transformArea.EntityTeleport(h.Value, xyz, null, zeroVelocity ? 1 : 0);
        }
        else
        {
            float* pyr = stackalloc float[3] { a.X, a.Y, a.Z };
            _transformArea.EntityTeleport(h.Value, xyz, pyr, zeroVelocity ? 1 : 0);
        }

        // The solver half, after the entity has moved (it reads the facing
        // the entity now has when no angles were given).
        E.Motion.TeleportObject(h, origin, angles, zeroVelocity);

        E.Teleported?.Invoke(h, origin, zeroVelocity);
    }

    /// <summary>
    /// Makes <paramref name="parent"/> carry <paramref name="child"/>: the child
    /// keeps a fixed offset in the parent's frame and follows it from then on.
    ///
    /// <para>
    /// A default <paramref name="parent"/> unparents, the way the SDK's own
    /// SetParent reads a null parent. <paramref name="parentAttachment"/> is
    /// one-based, matching the skeleton's attachment lookup, and 0 means
    /// the parent's own origin rather than a frame on its model.
    /// </para>
    ///
    /// <para>
    /// Answers false when the link was refused — a cycle, an entity that has not
    /// spawned, or a child on the SDK's hierarchy, which is not supported. Each
    /// of those says so on the console; none of them asserts.
    /// </para>
    /// </summary>
    public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return false;

        return _hierarchyArea.EntitySetParent(child.Value, parent.Value, parentAttachment) != 0;
    }

    /// <summary>
    /// What carries this entity, or <see cref="EntityHandle.None"/>. A handle rather than a
    /// pointer: the parent is resolved on every walk, so one whose entity has
    /// gone comes back as nothing rather than as a number that names a stranger.
    /// </summary>
    public EntityHandle GetParent(EntityHandle child)
    {
        // EntityHandle.None: INVALID_EHANDLE_INDEX, which is what native
        // answers for "nothing carries this" (and default, since 2026-10-05).
        if (!E.IsInitialized || !E.ShimsAttached) return EntityHandle.None;

        return new EntityHandle(_hierarchyArea.EntityGetParent(child.Value));
    }

    /// <summary>
    /// Which frame on the parent this entity hangs from: one-based, 0 for the
    /// parent's own origin. Asked separately from <see cref="EntityGetParent"/>
    /// because "parented to the origin" and "not parented" are different answers
    /// and one number could not carry both.
    /// </summary>
    public int GetParentAttachment(EntityHandle child)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return 0;

        return _hierarchyArea.EntityGetParentAttachment(child.Value);
    }

    /// <summary>
    /// The offset this entity keeps in its parent's frame — what parenting
    /// actually is, rather than a position that happens to follow.
    ///
    /// <para>
    /// This is NOT a teleport, and the difference is the whole split the
    /// hierarchy is built on. Writing an ABSOLUTE position of a parented entity
    /// means "put it here, once, and relative to the parent from then on", so it
    /// re-derives the offset. This replaces the offset itself, which is what a
    /// child sliding along its parent wants. Each half left null keeps what the
    /// child already has.
    /// </para>
    /// </summary>
    public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* xyz = null;
        float* pyr = null;

        if (origin is Vector o)
        {
            float* buf = stackalloc float[3] { o.X, o.Y, o.Z };
            xyz = buf;
        }

        if (angles is Vector a)
        {
            float* buf = stackalloc float[3] { a.X, a.Y, a.Z };
            pyr = buf;
        }

        _hierarchyArea.EntitySetLocalTransform(child.Value, xyz, pyr);
    }

    /// <summary>
    /// That same offset, read back. Answers the entity's ABSOLUTE origin and
    /// angles when nothing is carrying it, so a caller can read a local frame
    /// without first asking whether the entity has a parent.
    /// </summary>
    public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles)
    {
        origin = default;
        angles = default;

        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* xyz = stackalloc float[3];
        float* pyr = stackalloc float[3];

        _hierarchyArea.EntityGetLocalTransform(child.Value, xyz, pyr);

        origin = new Vector(xyz[0], xyz[1], xyz[2]);
        angles = new Vector(pyr[0], pyr[1], pyr[2]);
    }

    /// <summary>
    /// WHICH HIERARCHY HOLDS THIS ENTITY, AND WHETHER IT CAN POSE — three
    /// answers in one call.
    ///
    /// <para>
    /// One call rather than three because the three are asked together and only
    /// together: <see cref="ParentFrameRule.KindFor"/> takes all of them and
    /// answers which frame a child hangs from. Asking separately would also
    /// admit an answer no entity ever has — on both hierarchies at once —
    /// because the two questions could be asked a frame apart.
    /// </para>
    ///
    /// <para><see cref="ParentProbe.None"/> for a handle that resolves to nothing.</para>
    /// </summary>
    public ParentProbe ParentProbe(EntityHandle entity)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return SourceSharp.Abi.ParentProbe.None;

        return (ParentProbe)_hierarchyArea.EntityParentProbe(entity.Value);
    }

    public Span<byte> GetEngineBlock(EntityHandle entity) => E.GetEntityBlock(_networkServerArea, entity);
    /// <summary>
    /// THE CHILD'S SOLVER OBJECT, DESTROYED — what making a parent link does
    /// besides arithmetic, and a stated divergence from stock.
    ///
    /// <para>
    /// Stock shadows a simulated child instead. There is no shadow support on
    /// this hierarchy, and leaving the object alone is strictly worse than
    /// destroying it: a static object cannot be moved by writing the entity's
    /// origin, so the model would follow its parent while an invisible hull
    /// stayed behind, stopping bullets and players in mid-air. A parented child
    /// stops being simulated and becomes a thing that follows.
    /// </para>
    ///
    /// <para>
    /// NOT UNDONE BY UNPARENTING. The object destroyed here was built from the
    /// model's compiled collision data by whoever asked for it, and only they
    /// know whether it should be static, asleep or debris.
    /// </para>
    /// </summary>
    public void DestroyPhysics(EntityHandle entity)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _physicsArea.EntityDestroyPhysics(entity.Value);
    }
    /// <summary>
    /// MANAGED MOVED THE ENTITY; NATIVE HAS WORK TO DO ABOUT IT
    /// (<c>.claude/entity-port/abi-requests/1b.md</c> §2).
    ///
    /// <para>
    /// The four things the tail of <c>CStandaloneEntity::SetAbsTransform</c>
    /// does, in that order and for the reasons written out there: move it in
    /// the spatial partition (which is what a trace and a trigger overlap
    /// actually query), mark the origin and angle offsets, carry the children
    /// eagerly so parenting stays ordering-free, and touch the triggers LAST
    /// because the answer is about where the entity now is.
    /// </para>
    ///
    /// <para>
    /// <paramref name="previousOrigin"/> is what the trigger half needs and is
    /// the one thing native cannot read for itself once managed has written the
    /// new position. That is why it is carried rather than fetched.
    /// </para>
    /// </summary>
    public void PositionChanged(EntityHandle entity, Vector previousOrigin)
    {
        float* previous = stackalloc float[3];
        previous[0] = previousOrigin.X;
        previous[1] = previousOrigin.Y;
        previous[2] = previousOrigin.Z;

        var args = default(OpArgs);
        args.Vec0 = previous;

        EntityDispatch(entity, EntityOp.PositionChanged, &args);
    }

    /// <summary>
    /// Every entity of a class, as a lazy sequence.
    ///
    /// Wraps Source's start-after iteration idiom. Handle-based throughout, so
    /// an entity removed mid-iteration fails to resolve rather than dangling —
    /// which matters because a caller can quite reasonably remove or spawn
    /// things while walking the list.
    /// </summary>
    public IEnumerable<EntityHandle> FindAllByClassname(string className)
    {
        // The pointer work lives in FindNextByClassname because an iterator's
        // generated state machine does not inherit the unsafe context.
        uint current = EntityHandle.None.Value;

        while (true)
        {
            uint next = FindNextByClassname(current, className);
            if (next == EntityHandle.None.Value)
                break;

            current = next;
            yield return new EntityHandle(next);
        }

        // ---- AND THE ENTITIES THE ENGINE CANNOT SEE -- step 2e -------------
        //
        // THIS IS THE CLAUSE THAT MAKES `debug-api ents "*"` REPORT THE SAME
        // CLASSNAMES. The native walk is over the engine's entity list, and a
        // shell-less entity is not in it — without this second half every
        // logic_relay, filter and math_counter in the map would simply vanish
        // from every find-by-class in the build, silently, and the first thing
        // to notice would be a map whose logic had stopped working.
        //
        // AFTER the native ones and not interleaved: stock's walk is in entity
        // creation order and this preserves that for everything that has a
        // shell, rather than inventing an order for the whole list.
        if (E.ShellLess.Mine is not { } side)
            yield break;

        var extra = new List<EntityHandle>();
        side.FindByClassName(className, extra);

        foreach (var handle in extra)
            yield return handle;
    }

    /// <summary>
    /// Removes an entity from the world — deferred to the end of the frame, as
    /// <c>UTIL_Remove</c> is. The handle keeps resolving until then.
    ///
    /// <para>
    /// <b>THE DIRECTORY FIRST, AND IT COSTS NO CROSSING</b> —
    /// <c>plan_entity.md</c> step 2c. A managed entity is marked in its side's
    /// <see cref="EntityDirectory"/> through <see cref="EntityRemovalRoutes"/>
    /// and freed by that directory's end-of-frame pass, in a batch. Before this
    /// step every removal left managed code here, reached
    /// <c>CStandaloneEntity::Remove</c> and was appended to a native list.
    /// </para>
    ///
    /// <para>
    /// <b>The slot is still the fall-back and still has work to do.</b> A handle
    /// the directory does not hold — a stock <c>CBaseEntity</c>, which is what
    /// <c>gib.cpp</c>, <c>physics_prop_ragdoll.cpp</c> and
    /// <c>EntityDissolve.cpp</c> still make — is not the directory's to mark,
    /// and it goes out through <c>EntityRemove</c> to <c>UTIL_Remove</c>. That
    /// arm leaves with those classes in Phase 4.
    /// </para>
    /// </summary>
    public void Remove(EntityHandle entity)
    {
        if (E.Removal.Remove(entity))
            return;

        if (E.IsInitialized && E.ShimsAttached)
            _entityLifecycleArea.EntityRemove(entity.Value);
    }

    /// <summary>
    /// WHERE A ONE-BASED ATTACHMENT IS, IN THE WORLD, on an entity of either
    /// hierarchy.
    ///
    /// <para>
    /// THE ONE THING MANAGED PARENTING CANNOT DERIVE. An attachment frame is a
    /// posed skeleton, and both hierarchies pose on their own clock from the
    /// model's own bone data. Two answers to "where is this model's muzzle" is
    /// the shape of bug nobody finds by reading, so nothing here re-derives it.
    /// </para>
    ///
    /// <para>
    /// False means the model does not carry that attachment. The out values are
    /// written either way — native fills the entity's own place, which is what
    /// both sides' <c>GetAttachment</c> does — but a caller that composes
    /// against them anyway has silently parented a muzzle flash to an origin,
    /// so parenting reads the result.
    /// </para>
    /// </summary>
    private partial bool EngineAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles)
    {
        origin = default;
        angles = default;

        if (!E.IsInitialized || !E.ShimsAttached) return false;

        float* xyz = stackalloc float[3];
        float* pyr = stackalloc float[3];

        int got = _hierarchyArea.EntityGetAttachmentFrame(entity.Value, attachment, xyz, pyr);

        origin = new Vector(xyz[0], xyz[1], xyz[2]);
        angles = new QAngle(pyr[0], pyr[1], pyr[2]);

        return got != 0;
    }

    /// <summary>
    /// This entity's gravity scale — the pair to
    /// <see cref="EntitySetGravity"/>. A multiple of the world's, so 1 is no
    /// change.
    /// </summary>
    public float GetGravity(EntityHandle entity)
        => (E.IsInitialized && E.ShimsAttached)
               ? _transformArea.EntityGetGravity(entity.Value)
               : 0f;

    public void SetGravity(EntityHandle entity, float gravity) => E.EntitySetGravity(entity, gravity);

    public SolidFlags GetSolidFlags(EntityHandle entity) => E.EntityGetSolidFlags(_collisionServerArea, entity);
    /// <summary>
    /// What SHAPE this entity presents to a trace — the half of solidity
    /// <see cref="EntityGetSolidFlags"/> qualifies but could not name.
    ///
    /// <para>
    /// <see cref="SolidType.None"/> where there is no engine or the handle
    /// resolves to nothing, which is the same zero every other entity getter
    /// answers.
    /// </para>
    /// </summary>
    public SolidType GetSolidType(EntityHandle entity)
        => (E.IsInitialized && E.ShimsAttached)
               ? (SolidType)_collisionServerArea.EntityGetSolidType(entity.Value)
               : SolidType.None;

    /// <summary>
    /// Sets both halves of solidity, in ONE call.
    ///
    /// <para>
    /// One call and not two, because the two are one decision:
    /// <c>CStandaloneEntity::SetSolidity</c> writes the pair and then runs
    /// <c>UpdatePartition</c> once, which CREATES or DESTROYS the entity's
    /// spatial-partition handle from both values together. Set in pieces, an
    /// entity is briefly listed for a shape it does not have — the same reason
    /// <see cref="EntityTeleport"/> is one call rather than an origin and an
    /// angle.
    /// </para>
    /// </summary>
    public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _collisionServerArea.EntitySetSolidity(entity.Value, (int)solidType, (int)solidFlags);
    }

    //-------------------------------------------------------------------------
    // THE SIX MISSING DIRECTIONS, for the native half of a savegame.
    //
    // Each is the pair of a slot that already existed, and each exists because
    // SavedEntity's native store carries only fields the managed side can both
    // READ and WRITE -- which is what makes the field list and its apply order
    // testable against the engine double rather than only in a game. See
    // NativeField in SavedEntity.cs.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Sets the collision box, as offsets from the origin — the pair to
    /// <see cref="EntityGetBounds"/>.
    ///
    /// <para>
    /// The pair to THAT rather than to <see cref="EntityGetBoundsSize"/>, and
    /// the difference is not cosmetic: a size can only be centred on the
    /// origin, and most prop models put their origin on the base. A box
    /// rebuilt from a size sinks half through the floor.
    /// </para>
    /// </summary>
    public void SetBounds(EntityHandle entity, Vector mins, Vector maxs)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };

        _collisionServerArea.EntitySetBounds(entity.Value, lo, hi);
    }

    /// <summary>
    /// Sets the spawnflags — the pair to <see cref="EntityGetSpawnFlags"/>.
    ///
    /// <para>
    /// Replaces them wholesale rather than adding or removing bits, because
    /// what wants it is a restore putting back a value it read, and a
    /// read-modify-write against whatever the map lump just set would not be
    /// that value.
    /// </para>
    /// </summary>
    public void SetSpawnFlags(EntityHandle entity, int spawnFlags)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _entitiesServerArea.EntitySetSpawnFlags(entity.Value, spawnFlags);
    }

    /// <summary>
    /// Creates a map entity and gives the SHELL its map keys in the same
    /// crossing — plan_entity.md step 2d.
    ///
    /// <para>
    /// <b>What it replaces is 5709 crossings on dm_lockdown.</b> The map's text
    /// used to live only inside the native parse, so every key of every entity
    /// was handed up one at a time through the shell's <c>KeyValue</c>: 72% of
    /// the whole map-load entity budget, and the largest single item in it.
    /// <c>MapSpawner</c> parses the lump in managed memory and gives every key
    /// to <c>ScriptedEntityRegistry</c> with no crossing at all; this hands the
    /// same keys down to the shell's own arm, together.
    /// </para>
    ///
    /// <para>
    /// <paramref name="packedKeys"/> is <c>key\0value\0key\0value\0…</c>.
    /// EVERY key, in map order, filtered on neither side — a native filter would
    /// drop exactly the typo'd keys the near-miss report exists to catch, which
    /// is the route step 2a refuted.
    /// </para>
    /// </summary>
    private EntityHandle MapSpawnCreate(string className, ReadOnlySpan<byte> packedKeys)
    {
        // AND THE MAP'S ROUTE -- step 2e. The packed keys go to the same eight
        // arms CStandaloneEntity::KeyValueShellOnly claimed, in managed memory;
        // the caller then hands every key to the registry exactly as it does
        // for an entity that has a shell, so nothing about the managed half of
        // a map load changes.
        if (E.ShellLess.ClassIsShellLess(className))
            return E.ShellLess.Mine!.Create(className, packedKeys);

        if (!E.IsInitialized || !E.ShimsAttached) return EntityHandle.None;

        var name = Encoding.UTF8.GetBytes(className + "\0");

        fixed (byte* pName = name)
        fixed (byte* pKeys = packedKeys)
        {
            return new EntityHandle(_entityLifecycleArea.MapSpawnCreate(pName, pKeys, packedKeys.Length));
        }
    }

    private uint FindNextByClassname(uint startAfter, string className)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return EntityHandle.None.Value;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");
        fixed (byte* p = bytes) { return _entitiesServerArea.FindEntityByClassname(startAfter, p); }
    }

    /// <summary>
    /// THE OUTBOUND HALF OF THE OP TABLE (plan_entity.md §3.4; step 1b's second
    /// ABI request).
    ///
    /// <para>
    /// <c>Dispatch</c> is native asking managed; this is managed telling native,
    /// through the same <see cref="EntityOp"/> vocabulary. One op uses it,
    /// <see cref="EntityOp.PositionChanged"/> — see
    /// <see cref="EntityPositionChanged"/>, which is the shape every caller
    /// should take rather than building an <see cref="OpArgs"/> by hand.
    /// </para>
    /// </summary>
    /// <remarks>
    /// INTERNAL, and deliberately: the only shape a caller should take is
    /// <see cref="EntityPositionChanged"/> and the ops that follow it. A public
    /// raw-pointer entry point would be an <see cref="OpArgs"/> built by hand
    /// at every call site, which is exactly the per-op argument list this table
    /// exists to have one of.
    /// </remarks>
    private int EntityDispatch(EntityHandle h, EntityOp op, OpArgs* args)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return 0;

        return _entityLifecycleArea.EntityDispatch(h.Value, (int)op, args);
    }

    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public int UpdateVisibilityAll() => 0;

}

internal sealed unsafe partial class EngineEntityPhysics
{
    private readonly CollisionServerArea _collisionServerArea;
    private readonly PhysicsArea _physicsArea;
    private readonly TransformArea _transformArea;

    public EngineEntityPhysics(EngineSide side,
                               CollisionServerArea collisionServerArea,
                               PhysicsArea physicsArea,
                               TransformArea transformArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _collisionServerArea = collisionServerArea ?? throw new ArgumentNullException(nameof(collisionServerArea));
        _physicsArea = physicsArea ?? throw new ArgumentNullException(nameof(physicsArea));
        _transformArea = transformArea ?? throw new ArgumentNullException(nameof(transformArea));
    }

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
    /// <summary>
    /// How many times a projectile reflects off what it hits before its flight
    /// ends. Zero — the default — stops it at the first thing it touches.
    ///
    /// This has to be the engine's business: the reflection is about the
    /// surface NORMAL, and the normal exists only inside the step trace that
    /// moves the projectile. A touch handler is told what was hit, never which
    /// way it faced. What stays here is what a bounce MEANS, which is nothing
    /// at all for a bolt and the whole character of an energy ball.
    /// </summary>
    public void SetBounce(EntityHandle h, int bounces)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _transformArea.EntitySetBounce(h.Value, bounces);
    }

    /// <summary>Whether a brush can be walked through, walked into, or neither.</summary>
    public void SetBrushSolidity(EntityHandle entity, BrushSolidity solidity)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _collisionServerArea.EntitySetBrushSolidity(entity.Value, (int)solidity);
    }

    /// <summary>
    /// The same, read back. <see cref="CollisionGroup.None"/> for a handle that
    /// resolves to nothing, and for any engine group this mod has no name for.
    /// </summary>
    public CollisionGroup GetCollisionGroup(EntityHandle entity)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return CollisionGroup.None;

        return (CollisionGroup)_collisionServerArea.EntityGetCollisionGroup(entity.Value);
    }

    public SolidFlags GetSolidFlags(EntityHandle entity) => E.EntityGetSolidFlags(_collisionServerArea, entity);
    /// Which of the engine's collision groups this entity is in.
    ///
    /// Not the same question as solidity. Solidity is the shape an entity
    /// presents to a trace; a collision group is one half of a PAIR the game
    /// rules answer about, which is the only way to say "solid to vehicles and
    /// to nothing else".
    /// </summary>
    public void SetCollisionGroup(EntityHandle entity, CollisionGroup group)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _collisionServerArea.EntitySetCollisionGroup(entity.Value, (int)group);
    }

    public void SetGravity(EntityHandle h, float gravity) => E.EntitySetGravity(h, gravity);

    public void SetMass(EntityHandle entity, float mass) => E.EntitySetMass(entity, mass);
    /// <summary>
    /// Whether anything can bump into this entity.
    ///
    /// Not <see cref="EntitySetBrushSolidity"/>, which is a brush choosing what
    /// shape it presents. This is FSOLID_NOT_SOLID on an entity that already has
    /// one — a player being stepped through.
    /// </summary>
    public void SetNotSolid(EntityHandle entity, bool notSolid)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _collisionServerArea.EntitySetNotSolid(entity.Value, notSolid ? 1 : 0);
    }

    public nint PhysicsObject(EntityHandle entity) => E.EntityPhysicsObject(entity);

    public nint EnvironmentPointer() => E.PhysicsEnvironmentPointer();

    public Vector CollideExtent(nint physicsObject, Vector direction) => E.PhysicsCollideExtent(physicsObject, direction);

    public Vector Inertia(nint physicsObject) => E.PhysicsInertia(physicsObject);
    /// <summary>
    /// The level's world physics object (native's g_PhysWorldObject), which
    /// carries no game data, so the solver's answers know it by identity
    /// (plan_physics_bones_native_zero 2.1). 0 between levels.
    /// </summary>
    public nint WorldObject()
        => E.IsInitialized && E.ShimsAttached ? _physicsArea.PhysicsWorldObject() : 0;

    public void SetPhysicsStatic(EntityHandle entity, bool isStatic) => E.EntitySetPhysicsStatic(entity, isStatic);
}

internal sealed unsafe partial class EngineEntityRender
{
    private readonly EntitiesServerArea _entitiesServerArea;
    private readonly ModelsServerArea _modelsServerArea;
    private readonly NetworkServerArea _networkServerArea;

    /// <summary>
    /// The binding the container builds, on the mirror the pushes feed.
    ///
    /// <para>
    /// <see cref="EngineServices"/> constructs one of these per container —
    /// the defaults, the client's, the server's — so the mirror cannot be state
    /// on this object: the dormancy push would feed one instance and the render
    /// loop would read another, and nothing would say so. It is the side's
    /// composition's (plan_abi_hotreload D7): the module registers its mirror
    /// before the engine areas, <see cref="EngineServices"/> resolves this
    /// parameter from that container, and the module hands the same instance to
    /// the push handlers.
    /// </para>
    /// </summary>
    public EngineEntityRender(EngineSide side,
                              StandaloneMirror mirror,
                              ModelsArea modelsArea,
                              EntitiesServerArea entitiesServerArea,
                              ModelsServerArea modelsServerArea,
                              NetworkServerArea networkServerArea)
        : this(side,
               mirror,
               new StandaloneSkeletonRoute(),
               modelsArea,
               entitiesServerArea,
               modelsServerArea,
               networkServerArea)
    {
    }

    /// <summary>
    /// The server's: its skeletons are managed (plan physics_bones_native_zero
    /// step 1.2), so the bone members ask the route's
    /// <see cref="IStandaloneSkeletons"/> once the composition has filled it,
    /// and native only before that. EngineServices.Build picks this
    /// constructor when the container holds a route.
    /// </summary>
    public EngineEntityRender(EngineSide side,
                              StandaloneMirror mirror,
                              StandaloneSkeletonRoute skeletons,
                              ModelsArea modelsArea,
                              EntitiesServerArea entitiesServerArea,
                              ModelsServerArea modelsServerArea,
                              NetworkServerArea networkServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _mirror = mirror;
        _skeletons = skeletons ?? throw new ArgumentNullException(nameof(skeletons));
        _modelsArea = modelsArea ?? throw new ArgumentNullException(nameof(modelsArea));
        _entitiesServerArea = entitiesServerArea ?? throw new ArgumentNullException(nameof(entitiesServerArea));
        _modelsServerArea = modelsServerArea ?? throw new ArgumentNullException(nameof(modelsServerArea));
        _networkServerArea = networkServerArea ?? throw new ArgumentNullException(nameof(networkServerArea));
    }

    /// <summary>Precaches and applies a model.</summary>
    public void SetModel(EntityHandle entity, string model)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        var bytes = Encoding.UTF8.GetBytes(model + "\0");
        fixed (byte* p = bytes) { _modelsServerArea.EntitySetModel(entity.Value, p); }
    }

    /// <summary>
    /// Whether this entity is sent to every client regardless of where they
    /// are, rather than only when it is in somebody's PVS.
    ///
    /// Wanted by exactly one kind of entity: one a client is meant to be looking
    /// THROUGH from a room it is not in. A PVS check answers "not visible" for
    /// that entity, so it is never sent, so the client has nothing to take a
    /// view from.
    /// </summary>
    public void SetTransmitAlways(EntityHandle entity, bool always)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _networkServerArea.EntitySetTransmitAlways(entity.Value, always ? 1 : 0);
    }

    /// <summary>Whether a brush is drawn.</summary>
    public void SetVisible(EntityHandle entity, bool visible)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _entitiesServerArea.EntitySetVisible(entity.Value, visible ? 1 : 0);
    }

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

internal sealed unsafe partial class EngineGameEvents
{
    private readonly EventsServerArea _eventsServerArea;

    public EngineGameEvents(EngineSide side, EventsArea eventsArea, EventsServerArea eventsServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _eventsArea = eventsArea ?? throw new ArgumentNullException(nameof(eventsArea));
        _eventsServerArea = eventsServerArea ?? throw new ArgumentNullException(nameof(eventsServerArea));
    }

    /// <summary>Sets an int field of an event made by <see cref="EventCreate"/>.</summary>
    public void EventSetInt(IntPtr handle, string key, int value)
    {
        if (!E.IsInitialized || !E.ShimsAttached || handle == IntPtr.Zero) return;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { _eventsServerArea.EventSetInt(handle, p, value); }
    }

    /// <summary>Sets a float field.</summary>
    public void EventSetFloat(IntPtr handle, string key, float value)
    {
        if (!E.IsInitialized || !E.ShimsAttached || handle == IntPtr.Zero) return;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { _eventsServerArea.EventSetFloat(handle, p, value); }
    }

    /// <summary>Sets a bool field.</summary>
    public void EventSetBool(IntPtr handle, string key, bool value)
    {
        if (!E.IsInitialized || !E.ShimsAttached || handle == IntPtr.Zero) return;

        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { _eventsServerArea.EventSetBool(handle, p, value ? 1 : 0); }
    }

    /// <summary>Sets a string field.</summary>
    public void EventSetString(IntPtr handle, string key, string value)
    {
        if (!E.IsInitialized || !E.ShimsAttached || handle == IntPtr.Zero) return;

        var k = Encoding.UTF8.GetBytes(key + "\0");
        var v = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
        fixed (byte* pk = k)
        fixed (byte* pv = v) { _eventsServerArea.EventSetString(handle, pk, pv); }
    }
}

internal sealed unsafe partial class EngineGameRulesPolicy
{
    private readonly GameRulesArea _gameRulesArea;

    public EngineGameRulesPolicy(EngineSide side, GameRulesArea gameRulesArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _gameRulesArea = gameRulesArea ?? throw new ArgumentNullException(nameof(gameRulesArea));
    }

    /// <summary>
    /// The rules' constants. Silently does nothing on the client, where the
    /// slot is left NULL -- there is no rules object there to read the table.
    /// </summary>
    public void RegisterPolicy(GameRulesPolicyBlob policy)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        _gameRulesArea.RegisterGameRulesPolicy(&policy);
    }
}

internal sealed unsafe partial class EngineHost
{
    private readonly GameRulesArea _gameRulesArea;

    public EngineHost(EngineSide side, GameRulesArea gameRulesArea) : this(side, new NoSteamGameServer(), gameRulesArea) { }

    public EngineHost(EngineSide side, ISteamGameServer steam, GameRulesArea gameRulesArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _steam = steam ?? throw new ArgumentNullException(nameof(steam));
        _gameRulesArea = gameRulesArea ?? throw new ArgumentNullException(nameof(gameRulesArea));
    }

    /// <summary>
    /// Ends the round and sends everyone to intermission.
    ///
    /// Routed through the gamerules so the base class runs its own end-of-round
    /// bookkeeping — scores, timers, the map change that follows — rather than
    /// the shell reimplementing it.
    /// </summary>
    public void EndRound()
    {
        if (E.IsInitialized && E.ShimsAttached)
            _gameRulesArea.EndRound();
    }
}

internal sealed unsafe partial class EngineHud
{
    private readonly DebugOverlayArea _debugOverlayArea;
    private readonly EffectsServerArea _effectsServerArea;

    public EngineHud(EngineSide side, DebugOverlayArea debugOverlayArea, EffectsServerArea effectsServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _debugOverlayArea = debugOverlayArea ?? throw new ArgumentNullException(nameof(debugOverlayArea));
        _effectsServerArea = effectsServerArea ?? throw new ArgumentNullException(nameof(effectsServerArea));
    }

    /// <summary>
    /// A line of text drawn in the world at a position, through the engine's
    /// debug-overlay channel.
    ///
    /// <para>
    /// <paramref name="line"/> offsets it downward, so several calls at one
    /// position stack rather than overprint. <paramref name="duration"/> is a
    /// minimum in seconds — whatever is drawing has to keep drawing.
    /// </para>
    /// </summary>
    public void DrawWorldText(Vector origin, int line, string text, float duration, int r = 255, int g = 255, int b = 255, int a = 255)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        var bytes = Encoding.UTF8.GetBytes(text + "\0");

        fixed (byte* p = bytes)
        {
            _debugOverlayArea.DrawWorldText(o, line, p, duration, r, g, b, a);
        }
    }

    /// <summary>
    /// An entity's debug overlay, drawn FOR A HANDLE — <c>plan_entity.md</c>
    /// step 4c. Its collision box, and a line of text at its origin.
    ///
    /// <para>
    /// <b>A HANDLE AND NOT AN INDEX.</b> An index alone addresses whichever
    /// entity now occupies a reused slot; the handle carries the serial and
    /// native revalidates it, so a stale one draws nothing rather than drawing
    /// on a stranger.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="EntityHandle.None"/> IS THE EMPTY HANDLE</b> (and
    /// <c>default</c> is None since 2026-10-05). The raw handle 0 is entry 0
    /// serial 0, a REAL SLOT that can be worldspawn. This method refuses
    /// <c>None</c> and nothing else, because drawing on the world is a
    /// legitimate ask; the way to catch a wrong handle is to read the return
    /// value, which is what it is for.
    /// </para>
    ///
    /// <para>
    /// A null or empty <paramref name="text"/> makes the entity name itself:
    /// its targetname if it has one, else its classname.
    /// </para>
    /// </summary>
    /// <returns>True when something was drawn.</returns>
    public bool DrawEntityOverlay(EntityHandle entity, int line, string? text, float duration, int r = 255, int g = 255, int b = 255, int a = 255)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return false;
        if (entity == EntityHandle.None) return false;

        var bytes = Encoding.UTF8.GetBytes((text ?? string.Empty) + "\0");

        fixed (byte* p = bytes)
        {
            return _debugOverlayArea.DrawEntityOverlay(entity.Value, line, p, duration, r, g, b, a) != 0;
        }
    }

    /// <summary>
    /// The overlay readout — <c>plan_entity.md</c> step 4c. What the native
    /// overlay library has done since the last reset, and whether a viewer is
    /// established right now.
    ///
    /// <para>
    /// <b>WHY THE ZEROS ARE NOT ALL THE SAME ZERO.</b> Six of the native
    /// overlay entries were <c>return;</c> before 4c, and a build in that state
    /// is invisible to any assertion of the form "the call did not crash".
    /// <c>Drawn</c> is the work; <c>NoViewer</c>, <c>Clipped</c> and
    /// <c>NoOverlay</c> are the three honest reasons it could be zero. All four
    /// zero with <c>ViewerSet</c> false is "nobody asked"; <c>Drawn</c> zero
    /// with <c>NoViewer</c> climbing is the regression.
    /// </para>
    /// </summary>
    public (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet)
OverlayReadout()
    {
        if (!E.IsInitialized || !E.ShimsAttached) return (0, 0, 0, 0, false);

        uint drawn = 0, clipped = 0, noViewer = 0, noOverlay = 0;
        int viewerSet = 0;

        _debugOverlayArea.OverlayCounts(&drawn, &clipped, &noViewer, &noOverlay, &viewerSet);

        return (drawn, clipped, noViewer, noOverlay, viewerSet != 0);
    }

    /// <summary>
    /// Zeroes the four counters <see cref="OverlayReadout"/> reports, so a
    /// caller can read an interval rather than a running total.
    /// </summary>
    public void OverlayResetCounts()
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        _debugOverlayArea.OverlayResetCounts();
    }

    /// <summary>
    /// One line in the engine's debug overlay, between two world points.
    ///
    /// <para>
    /// <paramref name="duration"/> of 0 or less puts the line in the
    /// twenty-slot POOL — stock's <c>UTIL_AddDebugLine</c> lifetime, redrawn
    /// every frame until it is reused or <c>clear_debug_overlays</c> empties
    /// it. A positive duration is the engine's own timed overlay. Zero is not
    /// "forever" to the engine, it is one frame.
    /// </para>
    ///
    /// <para>
    /// The colour is the caller's: stock traced <c>MASK_BLOCKLOS</c> to pick
    /// white-or-red and that trace lives on this side now — see
    /// <see cref="TraceLine"/> with <see cref="TraceMask.BlockLos"/>.
    /// </para>
    ///
    /// <para>Server only. Does nothing on the client, where the slot is null.</para>
    /// </summary>
    public void AddLineOverlay(Vector start, Vector end, int r, int g, int b, bool noDepthTest = true, float duration = 0f)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };

        _debugOverlayArea.AddLineOverlay(s, e, r, g, b, noDepthTest ? 1 : 0, duration);
    }

    /// <summary>
    /// Kicks the view of every player in range.
    ///
    /// Not a shake: a shake rattles over a duration, this is one shove — what a
    /// mapper reaches for when something lands rather than when something
    /// rumbles. A radius of zero is everyone, as with the shake.
    /// </summary>
    public void ViewPunch(Vector origin, Vector punch, float radius, bool inAir = false)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* p = stackalloc float[3] { punch.X, punch.Y, punch.Z };
        _effectsServerArea.ViewPunch(o, p, radius, inAir ? 1 : 0);
    }

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

internal sealed unsafe partial class EngineNetworking
{
    private readonly EntitiesServerArea _entitiesServerArea;
    private readonly NetworkServerArea _networkServerArea;

    public EngineNetworking(EngineSide side,
                            EntitiesServerArea entitiesServerArea,
                            NetworkArea networkArea,
                            NetworkServerArea networkServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _entitiesServerArea = entitiesServerArea ?? throw new ArgumentNullException(nameof(entitiesServerArea));
        _networkArea = networkArea ?? throw new ArgumentNullException(nameof(networkArea));
        _networkServerArea = networkServerArea ?? throw new ArgumentNullException(nameof(networkServerArea));
    }

    /// <summary>
    /// Whether the generic payload props on this entity's SendTable read the
    /// same bytes its typed props do (plan_nettable_generic.md Part 14 Step 3).
    ///
    /// <para>
    /// Asked of native rather than worked out here, and that is the point. Both
    /// prop sets read one blob on the server, so their VALUES cannot differ —
    /// what can differ is where the payload props point, and only native can
    /// read the payload back through the offsets the engine will use. A managed
    /// check could only compare the blob with itself and would pass forever.
    /// </para>
    ///
    /// <para>
    /// <see cref="PayloadAgreement.Words"/> is 0 and <see cref="PayloadAgreement.State"/>
    /// is <c>NoPayload</c> for anything that is not a runtime-networked entity,
    /// which is most of them and is a real answer rather than a failure.
    /// </para>
    /// </summary>
    public PayloadAgreement CheckPayloadAgreement(EntityHandle h)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return PayloadAgreements.NotSupplied;

        int words = 0;
        int firstBad = -1;
        int result = _networkServerArea.EntityPayloadAgreement(h.Value, &words, &firstBad);

        return new PayloadAgreement(
            result switch
            {
                1 => PayloadAgreementState.Agrees,
                0 => PayloadAgreementState.Disagrees,
                _ => PayloadAgreementState.NoPayload,
            },
            words, firstBad);
    }

    /// <summary>What an entity is, in one crossing rather than five.</summary>
    public EntityState EntityGetState(EntityHandle entity, out MoveType moveType)
    {
        moveType = MoveType.None;

        if (!E.IsInitialized || !E.ShimsAttached) return EntityState.None;

        int move;
        var state = (EntityState)_entitiesServerArea.EntityGetState(entity.Value, &move);
        moveType = (MoveType)move;
        return state;
    }

    /// <summary>
    /// The entity's native data blob, as a bounds-checked span.
    ///
    /// Shared memory, not a copy — writes land directly in the entity. The
    /// pointer is re-fetched every call so a stale handle yields an empty span
    /// rather than addressing freed memory. Do not store the returned span
    /// across frames; the compiler enforces that it cannot escape, since
    /// Span&lt;T&gt; is a ref struct.
    /// </summary>
    public Span<byte> GetEntityBlob(EntityHandle h)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return Span<byte>.Empty;

        int size;
        void* p = _networkServerArea.EntityGetDataBlob(h.Value, &size);
        if (p == null || size <= 0)
            return Span<byte>.Empty;

        return new Span<byte>(p, size);
    }

    /// <summary>
    /// Announces a blob write that did not go through a bound state.
    ///
    /// <para>
    /// Fetching a blob used to mark the entity changed by itself — on reads as
    /// well as writes, because native could not tell them apart. Part 9 deletes
    /// that, so the writes with no state object behind them say so themselves.
    /// </para>
    ///
    /// <para>
    /// IT HAS NO PRODUCTION CALLER ANY MORE. The one it had was
    /// <c>EntityData.TryWrite</c>, the older layout model's write path, which
    /// step 8a deleted — every write goes through a generated blob-backed
    /// property (<c>NetBlobView</c>) now, and that path marks the entity itself.
    /// Left in place rather than removed: this is a general engine-area member
    /// with an ABI slot behind it, and reaping it is a separate decision from
    /// 8a's. Recorded in <c>.claude/entity-port/requests/8a.md</c> §4 so it is
    /// not re-derived.
    /// </para>
    ///
    /// <para>
    /// Deliberately not called by any read. That asymmetry is the whole
    /// behaviour change of the step.
    /// </para>
    /// </summary>
    public void MarkEntityStateChanged(EntityHandle h)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _networkServerArea.EntityMarkStateChanged(h.Value);
    }

    public void MarkEntityBlockChanged(EntityHandle h, int blockOffset) => E.MarkEntityBlockChanged(_networkServerArea, h, blockOffset);

    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public bool SendServerMessage(string key, ReadOnlySpan<byte> payload) => false;
}

internal sealed unsafe partial class EngineOverlaps
{
    private readonly CollisionServerArea _collisionServerArea;

    public EngineOverlaps(EngineSide side, CollisionServerArea collisionServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _collisionServerArea = collisionServerArea ?? throw new ArgumentNullException(nameof(collisionServerArea));
    }

    /// <summary>
    /// THE FRAME'S OVERLAPPING PAIRS — one call, a span of them
    /// (<c>plan_entity.md</c> §3.8 and step 2b).
    ///
    /// <para>
    /// Fills <paramref name="into"/> and answers how many pairs there ARE,
    /// which may be more than it could hold. The list is built once per frame
    /// and answered as many times as it is asked for, so the caller grows its
    /// buffer and asks again — see <c>OverlapReader</c>, which is the
    /// only caller and does exactly that. Taking the truncated answer would
    /// read to the touch diff as every pair past the end having left, and a
    /// departure is something the game acts on.
    /// </para>
    ///
    /// <para>
    /// <b>ZERO WITH NO ENGINE</b>, which is what a hermetic test and a
    /// client-side directory both get. Zero pairs is a legitimate frame — an
    /// empty map has one — so this cannot be distinguished from a real answer,
    /// and it must not be: the diff's response to it is to report every link
    /// as having ended, which for a directory that never had one is nothing.
    /// </para>
    /// </summary>
    public int OverlapReport(Span<OverlapPair> into)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return 0;

        // The empty span is a legal ASK: "how many are there". Native writes
        // nothing for it and answers the count, which is how the reader sizes
        // itself without a first call that throws away pairs.
        fixed (OverlapPair* p = into)
            return _collisionServerArea.EntityOverlapReport((uint*)p, into.Length);
    }
}

internal sealed unsafe partial class EnginePlayerMovement
{
    private readonly MovementServerArea _movementServerArea;
    private readonly PlayersArea _playersArea;

    public EnginePlayerMovement(EngineSide side,
                                MovementArea movementArea,
                                MovementServerArea movementServerArea,
                                PlayersArea playersArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _movementArea = movementArea ?? throw new ArgumentNullException(nameof(movementArea));
        _movementServerArea = movementServerArea ?? throw new ArgumentNullException(nameof(movementServerArea));
        _playersArea = playersArea ?? throw new ArgumentNullException(nameof(playersArea));
    }

    /// <summary>
    /// Whether a player wades rather than walks, together with the flag that
    /// stops their own movement fighting whatever is pushing them.
    ///
    /// Stock mispredicts this: the underlying variable has no send prop, so the
    /// client never learns it is set. Setting it here behaves exactly as stock
    /// does, misprediction included.
    /// </summary>
    public void SetSlowMovement(EntityHandle player, bool slow)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _movementServerArea.PlayerSetSlowMovement(player.Value, slow ? 1 : 0);
    }

    /// <summary>
    /// Sets the player's move type from outside a movement frame, and answers
    /// the one it had BEFORE the write.
    ///
    /// <para>
    /// The previous value comes back so that a toggle — which is what
    /// <c>noclip</c> is — is one crossing rather than a read and a write with a
    /// frame's worth of movement possible in between, on the very state that
    /// decides what that movement does.
    /// </para>
    ///
    /// <para>
    /// <see cref="MoveType.None"/> when the handle is not a hierarchy player,
    /// or when the slot is not bound. No real player is ever in that move type,
    /// so the refusal is distinguishable from an answer.
    /// </para>
    /// </summary>
    public MoveType SetMoveType(EntityHandle player, MoveType moveType)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return MoveType.None;

        return (MoveType)_movementServerArea.PlayerSetMoveType(player.Value, (int)moveType);
    }

    /// <summary>
    /// Snaps where the player is LOOKING, and tells the client to adopt it.
    ///
    /// <para>
    /// Not <see cref="EntitySetAngles"/>: an entity's angles are where its
    /// model points, and a player's view is <c>CPlayerState::v_angle</c>, which
    /// is separate state the engine networks for prediction. The native side
    /// sets <c>fixangle</c> with it, which is the half that makes the client
    /// adopt the value rather than keep predicting from its own — without it
    /// the next usercmd's stale viewangles overwrite the snap before anyone
    /// sees it.
    /// </para>
    ///
    /// <para>Pitch, yaw, roll, in that order. Roll is taken rather than
    /// assumed: <c>setang_exact</c>'s stock spelling accepts one.</para>
    /// </summary>
    public void SnapEyeAngles(EntityHandle player, Vector pitchYawRoll)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return;

        float* a = stackalloc float[3] { pitchYawRoll.X, pitchYawRoll.Y, pitchYawRoll.Z };
        _playersArea.PlayerSnapEyeAngles(player.Value, a);
    }
}

internal sealed unsafe partial class EnginePlayers
{
    private readonly ConsoleServerArea _consoleServerArea;
    private readonly EntitiesServerArea _entitiesServerArea;
    private readonly PlayersArea _playersArea;

    public EnginePlayers(EngineSide side,
                         ConsoleServerArea consoleServerArea,
                         EntitiesServerArea entitiesServerArea,
                         PlayersArea playersArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _consoleServerArea = consoleServerArea ?? throw new ArgumentNullException(nameof(consoleServerArea));
        _entitiesServerArea = entitiesServerArea ?? throw new ArgumentNullException(nameof(entitiesServerArea));
        _playersArea = playersArea ?? throw new ArgumentNullException(nameof(playersArea));
    }

    /// <summary>
    /// A handle for a "skip this entity" parameter, with a shell-less one
    /// turned into <see cref="EntityHandle.None"/> — <c>plan_entity.md</c>
    /// step 2e.
    ///
    /// <para>
    /// <b>The substitution changes nothing about the trace and that is the
    /// argument for it.</b> A shell-less entity is not in the partition and has
    /// no collision model, so it cannot be hit; "skip it" and "skip nothing"
    /// are the same trace. What differs is that the first spends a handle from
    /// the engine-invisible range on a native call, which is exactly what
    /// <see cref="InvisibleRangeReach"/> counts — 94 of them per
    /// <c>dm_lockdown</c> run before this, all from emitters excluding
    /// themselves.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="EntityHandle.None"/> (which <c>default</c> is) and never
    /// the raw handle 0.</b> The empty skip is <c>INVALID_EHANDLE_INDEX</c>;
    /// entry ZERO is worldspawn, and substituting it would ask the trace to
    /// ignore the world.
    /// </para>
    /// </summary>
    /// <summary>
    /// Puts a spawning player at <paramref name="origin"/> facing
    /// <paramref name="angles"/> — <c>plan_entity.md</c> step 2e.
    ///
    /// <para>
    /// <b>Only meaningful from inside <c>SelectSpawnPoint</c></b>, which is
    /// called from <c>CBasePlayer::Spawn</c> before the player is made solid.
    /// Called later it is the bug the ABI's own comment records: a player made
    /// solid at the world origin and teleported out of it afterwards arrives
    /// embedded in whatever was at the destination.
    /// </para>
    ///
    /// <para>
    /// <paramref name="origin"/> is final, stock's one-unit lift included;
    /// <paramref name="angles"/> is the spot's full facing, of which the entity
    /// takes yaw only and the player state takes all three.
    /// </para>
    /// </summary>
    public void PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return;

        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* pyr = stackalloc float[3] { angles.X, angles.Y, angles.Z };

        _playersArea.PlayerPlaceAtSpawn(player.Value, xyz, pyr);
    }

    public EntityHandle GetHandle(int playerIndex) => GetPlayerHandle(playerIndex);
    /// <summary>
    /// The player whose console is running the ConCommand being dispatched
    /// right now, or <see cref="EntityHandle.None"/> when nobody issued it —
    /// the server console with no player on it, or a slot that has not spawned.
    ///
    /// <para>
    /// Stock's <c>UTIL_GetCommandClient</c>, which is the line every one of the
    /// ent_* development commands opened with. Only meaningful DURING a
    /// dispatch; the engine clears the command client between them, so nothing
    /// caches this.
    /// </para>
    /// </summary>
    public EntityHandle GetCommandClient()
        => (E.IsInitialized && E.ShimsAttached)
            ? new EntityHandle(_consoleServerArea.GetCommandClientHandle())
            : EntityHandle.None;

    public ObserverMode GetObserverMode(EntityHandle player)
        => (E.IsInitialized && E.ShimsAttached)
            ? (ObserverMode)_playersArea.PlayerGetObserverMode(player.Value)
            : ObserverMode.None;

    /// <summary>
    /// Where a player's shots start — their eye, which is what the stock weapon
    /// code fires from. Separate from the aim trace because a weapon firing more
    /// than one pellet has to aim each one itself.
    /// </summary>
    public Vector GetShootOrigin(EntityHandle player)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return default;

        float* xyz = stackalloc float[3];
        _playersArea.GetPlayerShootOrigin(player.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>
    /// Team number for a player, or -1 if the handle is not a player.
    ///
    /// -1 rather than 0, because 0 is a real team a player can be on
    /// (unassigned) and conflating the two would make a rule unable to tell
    /// "not a player" from "no team yet".
    /// </summary>
    public int GetTeam(EntityHandle player)
        => (E.IsInitialized && E.ShimsAttached) ? _playersArea.GetPlayerTeam(player.Value) : -1;

    public bool IsAlive(EntityHandle player)
        => E.IsInitialized && E.ShimsAttached && _playersArea.PlayerIsAlive(player.Value) != 0;

    /// <summary>
    /// Turns the entity into a touchable pickup with the given half-extents.
    /// A trigger, not a solid: the player walks through it rather than into it.
    /// </summary>
    public void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _entitiesServerArea.EntityMakePickup(entity.Value, sizeX, sizeY, sizeZ);
    }

    /// <summary>
    /// Puts a dead player back in the world, through the engine's own respawn —
    /// so the spawn point choice, the loadout and the spawn event all still
    /// happen.
    /// </summary>
    public void Respawn(EntityHandle player)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _playersArea.PlayerRespawn(player.Value);
    }

    /// <summary>
    /// Puts a player into or out of spectating.
    /// <see cref="ObserverMode.None"/> stops them watching WITHOUT respawning
    /// them, which is a real state and not the same thing.
    /// </summary>
    public void SetObserverMode(EntityHandle player, ObserverMode mode)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _playersArea.PlayerSetObserverMode(player.Value, (int)mode);
    }

    /// <summary>
    /// Puts a player on a team, carrying out the store of stock's
    /// <c>CBaseEntity::ChangeTeam</c> — nothing more. The number is not
    /// validated here on purpose: which teams exist and which accept a
    /// joiner is the server's <c>ITeams</c> policy, and this call is how
    /// that decision reaches the native player. Client-side it does
    /// nothing: no area is attached.
    /// </summary>
    public void SetTeam(EntityHandle player, int team)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _playersArea.PlayerSetTeam(player.Value, team);
    }

    /// <summary>
    /// Shows or hides a pickup without destroying it, so an item that respawns
    /// keeps the entity a mapper may have wired outputs to.
    /// </summary>
    public void SetPickupEnabled(EntityHandle entity, bool enabled)
    {
        if (E.IsInitialized && E.ShimsAttached)
            _entitiesServerArea.EntitySetPickupEnabled(entity.Value, enabled ? 1 : 0);
    }

    /// <summary>
    /// Where one client's ears are, read off the standalone hierarchy only.
    ///
    /// <para>
    /// plan_server.md Phase 7, plan-audit C5. Half of a pair;
    /// <see cref="ClientsSdkPlayerEarPosition"/> is the other half, and the
    /// ORDER they are asked in is <c>ServerClients.EarPositionForClient</c>'s —
    /// which is the whole content of the crossing, because that order used to
    /// be an <c>#ifdef SOURCESHARP</c> block inside
    /// <c>CServerGameClients::ClientEarPosition</c>.
    /// </para>
    ///
    /// <para>
    /// <b><c>ears</c> is untouched on a miss, not zeroed.</b> The world origin
    /// is a real position, so "the listener is at the origin" and "no such
    /// client" cannot share a value — the caller asserts before writing
    /// <c>vec3_origin</c> and has to know which case it is in.
    /// </para>
    /// </summary>
    public bool TryStandalonePlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = default;
        if (!E.IsInitialized || !E.ShimsAttached) return false;

        float* xyz = stackalloc float[3];
        xyz[0] = 0.0f; xyz[1] = 0.0f; xyz[2] = 0.0f;
        int found = _playersArea.ClientsStandalonePlayerEarPosition(edictIndex, xyz);
        if (found == 0) return false;

        ears = new Vector(xyz[0], xyz[1], xyz[2]);
        return true;
    }
    /// <summary>
    /// The player a userid names, or None if nobody on the server has it.
    ///
    /// Built from the property rather than asked for as its own engine call —
    /// see GetPlayerUserId in sourcesharp.h. The walk is over player slots
    /// only, so it is bounded by the server's player count and not by the
    /// entity list; it runs on a death, not on a tick.
    ///
    /// Userid 0 is the engine's way of saying "the world", so it resolves to
    /// None rather than to whichever slot happens to be empty.
    /// </summary>
    public EntityHandle FindByUserId(int userId)
    {
        if (userId == 0)
            return EntityHandle.None;

        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            var player = GetPlayerHandle(slot);

            if (player != EntityHandle.None && EngineBound.Server(E).PlayerUserId(player.EntryIndex) == userId)
                return player;
        }

        return EntityHandle.None;
    }

    private partial Vector EyePosition(EntityHandle player) => E.EntityGetEyePosition(player);

    /// <summary>Handle for a player slot (1..maxplayers), or None if absent.</summary>
    private EntityHandle GetPlayerHandle(int playerIndex)
        => (E.IsInitialized && E.ShimsAttached)
            ? new EntityHandle(_playersArea.GetPlayerHandle(playerIndex))
            : EntityHandle.None;
}

internal sealed unsafe partial class EnginePrecache
{
    private readonly PrecacheArea _precacheArea;

    public EnginePrecache(EngineSide side, PrecacheArea precacheArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _precacheArea = precacheArea ?? throw new ArgumentNullException(nameof(precacheArea));
    }

    /// <summary>
    /// Claims a decal material during level load. Answers the index the engine
    /// will know it by, or -1 when the material would not load — which is a
    /// mapper's typo and the caller's to report, since the caller is the one
    /// that knows which entity wrote the key.
    /// </summary>
    public int PrecacheDecal(string name)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
        {
            return _precacheArea.PrecacheDecal(p);
        }
    }

    /// <summary>
    /// Precaches a material by name — "skybox/sky_day01_01rt", with no
    /// extension.
    ///
    /// <para>
    /// Not <see cref="PrecacheModel"/> with a .vmt, which is the neighbouring
    /// call and answers an INDEX. A model index is a number both sides of the
    /// wire agree on; a material is only ever a name the renderer resolves, and
    /// there is nothing to network.
    /// </para>
    /// </summary>
    public void PrecacheMaterial(string material)
    {
        if (!E.IsInitialized || !E.ShimsAttached || material.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(material + "\0");
        fixed (byte* p = bytes) { _precacheArea.PrecacheMaterial(p); }
    }

    /// <summary>
    /// A particle system, by name, so the client can build one.
    ///
    /// <para>
    /// The neighbour of <see cref="PrecacheModel"/> and
    /// <see cref="PrecacheMaterial"/> and interchangeable with neither: a
    /// particle system has its own string table, and a name that never reached
    /// it does not refuse at draw time — it is simply not there.
    /// </para>
    ///
    /// <para>
    /// Stock precaches the fire systems from <c>CFire::Precache</c>. That has
    /// not run since <c>env_fire</c> became a C# class, because
    /// <c>LINK_ENTITY_TO_CLASS( env_fire, CFire )</c> sits inside
    /// <c>#ifndef SOURCESHARP</c> and no <c>CFire</c> is ever constructed, and
    /// there was no other door to ask through.
    /// </para>
    /// </summary>
    public void PrecacheParticleSystem(string name)
    {
        if (!E.IsInitialized || !E.ShimsAttached || name.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { _precacheArea.PrecacheParticleSystem(p); }
    }

    public int PrecacheModel(string model) => E.PrecacheModel(model);
    /// <summary>
    /// Makes a sound available. Must happen during level load.
    ///
    /// A sound emitted without this produces a warning and silence, which is
    /// the worst shape of failure: the entity works, the map works, and one
    /// thing makes no noise for a reason nothing states.
    /// </summary>
    public void PrecacheSound(string soundName)
    {
        if (!E.IsInitialized || !E.ShimsAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes) _precacheArea.PrecacheSound(p);
    }
}

internal sealed unsafe partial class EngineScripted
{
    private readonly EntityLifecycleArea _entityLifecycleArea;

    public EngineScripted(EngineSide side, EntityLifecycleArea entityLifecycleArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _entityLifecycleArea = entityLifecycleArea ?? throw new ArgumentNullException(nameof(entityLifecycleArea));
    }

    //-------------------------------------------------------------------------
    // Weapons.
    //
    // The state machine that decides when a weapon fires and reloads is managed.
    // What is here is what IS the engine: assembling a bullet and tracing it,
    // playing a view-model animation, reading how long one lasts.
    //-------------------------------------------------------------------------

    //-------------------------------------------------------------------------
    // C#-declared entities.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Declares an entity class and the outputs mappers can connect to. The
    /// native side builds the entity description from this, which is why it has
    /// to happen before a map is parsed.
    /// </summary>
    public void RegisterEntityClass(string className, string[] outputNames, ScriptedEntityKind kind)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return;

        var nameBytes = Encoding.UTF8.GetBytes(className + "\0");

        // One pinned UTF-8 buffer per output name, plus the array of pointers
        // the native side reads. Both only need to survive the call — it copies
        // what it keeps.
        var outputBytes = new byte[outputNames.Length][];
        for (int i = 0; i < outputNames.Length; i++)
            outputBytes[i] = Encoding.UTF8.GetBytes(outputNames[i] + "\0");

        var handles = new System.Runtime.InteropServices.GCHandle[outputNames.Length];
        byte** pointers = stackalloc byte*[outputNames.Length == 0 ? 1 : outputNames.Length];

        try
        {
            for (int i = 0; i < outputNames.Length; i++)
            {
                handles[i] = System.Runtime.InteropServices.GCHandle.Alloc(
                    outputBytes[i], System.Runtime.InteropServices.GCHandleType.Pinned);
                pointers[i] = (byte*)handles[i].AddrOfPinnedObject();
            }

            fixed (byte* pName = nameBytes)
            {
                _entityLifecycleArea.RegisterScriptedEntity(pName, pointers, outputNames.Length, (int)kind);
            }
        }
        finally
        {
            for (int i = 0; i < handles.Length; i++)
                if (handles[i].IsAllocated)
                    handles[i].Free();
        }
    }

    /// <summary>
    /// WHICH OPS THIS CLASS OVERRIDES (plan_entity.md §3.4).
    ///
    /// <para>
    /// A bitmask over <see cref="EntityOp"/>: bit <c>1 &lt;&lt; op</c> set means
    /// "cross for this op". The shell stores it in the class descriptor and
    /// tests it before every dispatch, so an op a class does not override costs
    /// zero managed transitions.
    /// </para>
    ///
    /// <para>
    /// Called after <see cref="RegisterScriptedEntity"/> for the same name —
    /// that is what creates the class this is stored on. A mask for a name that
    /// was never registered is dropped by the far side, silently, the shape
    /// every other by-name slot has.
    /// </para>
    /// </summary>
    public void RegisterEntityClassOps(string className, uint opMask)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return;

        var nameBytes = Encoding.UTF8.GetBytes(className + "\0");

        fixed (byte* pName = nameBytes)
        {
            _entityLifecycleArea.RegisterClassOps(pName, opMask);
        }
    }

    // TemplateReconnectIO AND TemplateSchedulePrecache STOOD HERE, and they are
    // gone rather than moved (plan_server_all.md Wave 1, docs/plan-audit.md
    // D11). Both were "pure text manipulation on the stored map data … native
    // because the text is", and the text is not native any more: the store is
    // SourceSharp.Server's TemplateStore, so a template asking for its own group
    // to be rewritten is one managed object calling another and needs no slot at
    // all. Two crossings per point_template per map load, removed by moving the
    // thing they crossed in order to reach.

    /// <summary>
    /// Spawns one copy of each template entity, each at the place the caller
    /// worked out for it — from pairs that arrive PARSED.
    ///
    /// <para>
    /// Nothing here decides WHERE: every transform arrives resolved. And since
    /// plan_server_novalve.md P5 nothing here PARSES either: the caller read
    /// each stored block with <c>MapEntityBlock</c>, so what crosses is, per
    /// entity, a classname and its <c>key\0value\0…</c> pack — the payload
    /// <c>MapSpawnCreate</c> already uses. Native creates, keys, places and
    /// spawns. The text-parsing slot this replaced, <c>TemplateSpawnInstance</c>,
    /// is gone: nothing called it once P5 parsed in managed (the typed-call walk
    /// in ShimAreaTypedCallsTests enforces that).
    /// </para>
    /// </summary>
    /// <returns>
    /// The entities created. Empty when a class failed to create, which is
    /// stock's all-or-nothing answer.
    /// </returns>
    public List<EntityHandle> TemplateSpawnKeyed(IReadOnlyList<KeyedSpawn> spawns, int limit = 64)
    {
        var made = new List<EntityHandle>();

        if (!E.IsInitialized || !E.ShimsAttached || spawns.Count == 0)
            return made;

        int count = spawns.Count;
        float[] origins = new float[count * 3];
        float[] angles = new float[count * 3];
        int[] packBytes = new int[count];

        // ONE pinned buffer per stream rather than an array of pointers: an
        // array of managed byte[] cannot be pinned as a unit.
        var names = new List<byte>(count * 16);
        var packs = new List<byte>(count * 128);

        for (int i = 0; i < count; i++)
        {
            names.AddRange(System.Text.Encoding.UTF8.GetBytes(spawns[i].ClassName));
            names.Add(0);

            int before = packs.Count;
            foreach (var kv in spawns[i].Keys)
            {
                packs.AddRange(System.Text.Encoding.UTF8.GetBytes(kv.Key));
                packs.Add(0);
                packs.AddRange(System.Text.Encoding.UTF8.GetBytes(kv.Value));
                packs.Add(0);
            }
            packBytes[i] = packs.Count - before;

            origins[i * 3] = spawns[i].Origin.X;
            origins[i * 3 + 1] = spawns[i].Origin.Y;
            origins[i * 3 + 2] = spawns[i].Origin.Z;
            angles[i * 3] = spawns[i].Angles.Pitch;
            angles[i * 3 + 1] = spawns[i].Angles.Yaw;
            angles[i * 3 + 2] = spawns[i].Angles.Roll;
        }

        byte[] nameBytes = names.ToArray();
        byte[] packBuffer = packs.Count > 0 ? packs.ToArray() : new byte[1];

        uint* handles = stackalloc uint[limit];
        int total;

        fixed (byte* pNames = nameBytes)
        fixed (byte* pPacks = packBuffer)
        fixed (int* pBytes = packBytes)
        fixed (float* pOrigins = origins)
        fixed (float* pAngles = angles)
        {
            total = _entityLifecycleArea.TemplateSpawnKeyed(pNames, pPacks, pBytes, pOrigins, pAngles, count,
                                            handles, limit);
        }

        for (int i = 0; i < Math.Min(total, limit); i++)
        {
            if (handles[i] != 0)
                made.Add(new EntityHandle(handles[i]));
        }

        if (total > limit)
        {
            E.Warning($"[SourceSharp] a template made {total} entities and only {limit} were handed " +
                     "back; the rest exist but nothing here can address them\n");
        }

        return made;
    }
}

internal sealed unsafe partial class EngineSound
{
    private readonly SoundArea _soundArea;

    public EngineSound(EngineSide side, EngineArea engineBaseArea, SoundArea soundArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _engineBaseArea = engineBaseArea ?? throw new ArgumentNullException(nameof(engineBaseArea));
        _soundArea = soundArea ?? throw new ArgumentNullException(nameof(soundArea));
    }

    /// <summary>
    /// Retunes, restarts or stops an entity's AMBIENT channel.
    ///
    /// <para>
    /// Not another <see cref="EntityEmitSound"/>. The engine keys an ambient by
    /// (entity index, sound name), so a second call carrying
    /// <c>SND_CHANGE_PITCH</c> or <c>SND_CHANGE_VOL</c> retunes what is already
    /// playing instead of layering a second copy on top, and <c>SND_STOP</c>
    /// ends it. Every envelope, LFO and fade an ambient_generic has is built out
    /// of that one distinction.
    /// </para>
    ///
    /// <para>
    /// The index and the origin are handed over rather than an entity, because
    /// WHICH entity a sound comes from is the decision — a mapper's
    /// SourceEntityName key settles it — and because a stop must reach the same
    /// index the sound started on even after that entity has gone.
    /// </para>
    /// </summary>
    /// <param name="soundLevel">A soundlevel in dB, NOT an attenuation.</param>
    public void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch)
    {
        if (!E.IsInitialized || !E.ShimsAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        fixed (byte* p = bytes)
            _soundArea.EmitAmbientSound(entIndex, xyz, p, volume, soundLevel, flags, pitch);
    }

    /// <summary>
    /// A soundscript from the world at <paramref name="origin"/>, on a CHAN_*
    /// channel, at <paramref name="volume"/> times the script's own: its
    /// parameters, a PAS attenuation filter at their level, and the emit
    /// (vphysics_sound.h's Play* body; plan_physics_bones_native_zero 2.3).
    /// False when the script has no parameters.
    /// </summary>
    public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume)
    {
        if (!E.IsInitialized || !E.ShimsAttached || soundName.Length == 0) return false;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        fixed (byte* p = bytes) return _soundArea.EmitWorldSoundScript(p, xyz, channel, volume) != 0;
    }

    /// <summary>
    /// Stops a LOOPING sound. A one-shot ends by itself; a loop runs until the
    /// map changes.
    ///
    /// <para>
    /// <b>A shell-less entity's loop is stopped where its emit went</b>, which
    /// is <c>SOUND_FROM_WORLD</c>: the engine keys a playing sound by (entity
    /// index, name), so a stop that named a different index would leave the
    /// loop running forever and say nothing. The handle is still handed down —
    /// <c>SourceSharp_EntityStopSound</c> answers the world index for a handle
    /// it cannot resolve, and its own note says why that is this line's
    /// counterpart rather than a guess.
    /// </para>
    ///
    /// <para>
    /// No managed class of kind <c>Logical</c> loops a sound today: all eight
    /// callers of this method are brushes, an effect and a physics prop, every
    /// one of which has a shell. It is written down anyway because the emit
    /// above now works for a shell-less entity, and an emit that can start a
    /// loop nothing can stop is a worse defect than the one it closed.
    /// </para>
    /// </summary>
    public void EntityStopSound(EntityHandle entity, string soundName)
    {
        if (!E.IsInitialized || !E.ShimsAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes) _soundArea.EntityStopSound(entity.Value, p);
    }

    /// <summary>
    /// Plays a sound on an entity, so it comes from where the entity is.
    ///
    /// <para>
    /// <b>A SHELL-LESS ENTITY IS ANSWERED HERE AND NEVER CROSSES</b> —
    /// plan_entity.md step 2e's rule applied to step 8e-sound's first defect,
    /// and it is the same route <see cref="EntityGetOrigin"/> and
    /// <see cref="CreateEntity"/> already take.
    /// </para>
    ///
    /// <para>
    /// <b>The mechanism, because the failure it replaces was silent.</b>
    /// <c>SourceSharp_EntityEmitSound</c> resolves the handle through
    /// <c>g_pEntityList</c> so that native code can ask the entity where it is.
    /// A shell-less entity is never in that list by construction — no edict, no
    /// entity index, no C++ object of any kind — so the resolve failed for every
    /// one of them and the emit returned. Step 4b measured it on
    /// <c>dm_lockdown</c>, where 81 of 473 live entities are shell-less: three
    /// shelled <c>func_door</c>s gave three <c>EmitSound:</c> traces and one
    /// shell-less <c>info_target</c> gave zero, while step 2e's
    /// <c>logical.nativeReach</c> counter moved 24 → 25 for that one emit.
    /// </para>
    ///
    /// <para>
    /// <b>The position has to come from the side that owns it</b>, and this side
    /// owns it: <see cref="ShellLessEntity.Origin"/> is where a shell-less
    /// entity's position lives, and <see cref="EmitSoundAtPosition"/> already
    /// takes a point. Nothing new crosses and no ABI slot was added — which is
    /// also why <c>nativeReach</c> stays FLAT across the emit rather than
    /// counting one more reach, and the gate reads that number as the second,
    /// independent proof that this route was taken.
    /// </para>
    ///
    /// <para>
    /// <b>The divergence step 4b named does not exist, and it was checked in
    /// the code rather than reasoned about.</b> 4b's write-up
    /// said this routing was only equivalent once
    /// <c>SourceSharp_EmitSoundAtPosition</c> stopped writing
    /// <c>VOL_NORM</c>/<c>PITCH_NORM</c> over what the artist put in the
    /// soundscript. Two facts say it never did: <c>EmitSound_t</c>'s default
    /// constructor already sets <c>m_flVolume( VOL_NORM )</c> and
    /// <c>m_nPitch( PITCH_NORM )</c> (<c>game/shared/shareddefs.h:834</c>), so
    /// the two writes store the value that was already there; and
    /// <c>CSoundEmitterSystem::EmitSoundByHandle</c> reads neither field unless
    /// <c>SND_CHANGE_VOL</c> / <c>SND_CHANGE_PITCH</c> is set in
    /// <c>m_nFlags</c> (<c>sound_emitter.cpp:557-568</c>), and neither path sets
    /// any flag. The soundscript's own volume and pitch survive both routes.
    /// </para>
    ///
    /// <para>
    /// What DOES differ between the two routes is the recipient filter, and the
    /// difference is the right way round. The entity route builds a
    /// <c>CPASAttenuationFilter</c> around the source's CENTRE; the position
    /// route builds one around the point. For a shell-less entity those are the
    /// same point — it is of kind <c>Logical</c>, so it has no model, no OBB and
    /// therefore no centre distinct from its origin, which is what
    /// <c>shell_sound.cpp</c>'s own note about <c>SOUND_FROM_WORLD</c> already
    /// says from the native side.
    /// </para>
    /// </summary>
    public void EntityEmitSound(EntityHandle entity, string soundName)
    {
        if (soundName.Length == 0) return;

        if (E.ShellLess.State(entity) is { } shellLess)
        {
            // COUNTED BEFORE THE EMIT, and counted whether or not the engine
            // table is bound: this is the twin of logical.nativeReach, and what
            // it has to make observable is that the ROUTE was taken. Counting
            // after a call that returns early on an unbound table would report
            // zero for the very build a hermetic test runs in.
            E.ShellLess.NoteSoundPlacedHere();
            EmitSoundAtPosition(soundName, shellLess.Origin);
            return;
        }

        if (!E.IsInitialized || !E.ShimsAttached) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes) _soundArea.EntityEmitSound(entity.Value, p);
    }

    /// <summary>A noise with no entity to come from.</summary>
    public void EmitSoundAtPosition(string soundName, Vector origin)
    {
        if (!E.IsInitialized || !E.ShimsAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        fixed (byte* p = bytes) _soundArea.EmitSoundAtPosition(p, xyz);
    }

    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void ClientEmitSound(EntityHandle entity, string soundName, Vector? origin = null) { }

}

internal sealed unsafe partial class EngineVisibility
{
    private readonly EntitiesServerArea _entitiesServerArea;
    private readonly WorldServerArea _worldServerArea;

    public EngineVisibility(EngineSide side, EntitiesServerArea entitiesServerArea, WorldServerArea worldServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _entitiesServerArea = entitiesServerArea ?? throw new ArgumentNullException(nameof(entitiesServerArea));
        _worldServerArea = worldServerArea ?? throw new ArgumentNullException(nameof(worldServerArea));
    }

    /// <summary>
    /// Joins the engine's area-portal list at the number vbsp assigned, so the
    /// per-client visibility pass has something to ask about this portal.
    /// Returns 0 if the number is not a portal.
    /// </summary>
    public uint AreaPortalCreate(int portalNumber, int portalVersion)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return 0;

        return _worldServerArea.AreaPortalCreate(portalNumber, portalVersion);
    }

    /// <summary>Leaves the list. The hole is left open behind it.</summary>
    public void AreaPortalDestroy(uint portal)
    {
        if (E.IsInitialized && E.ShimsAttached && portal != 0)
            _worldServerArea.AreaPortalDestroy(portal);
    }

    /// <summary>Whether the hole is open.</summary>
    public void AreaPortalSetOpen(uint portal, bool open)
    {
        if (E.IsInitialized && E.ShimsAttached && portal != 0)
            _worldServerArea.AreaPortalSetOpen(portal, open ? 1 : 0);
    }

    /// <summary>
    /// The distance past which a window portal shuts, measured from the volume
    /// of the entity given.
    /// </summary>
    public void AreaPortalSetWindow(uint portal, EntityHandle entity, float fadeDistance)
    {
        if (E.IsInitialized && E.ShimsAttached && portal != 0)
            _worldServerArea.AreaPortalSetWindow(portal, entity.Value, fadeDistance);
    }

    /// <summary>
    /// Says there is no 3D skybox — a map with no sky_camera, or one that has
    /// just been unloaded. Players fall back to the engine's "nowhere" area.
    /// </summary>
    public void ClearSkyboxParams()
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;
        _worldServerArea.SetSkyboxParams(null);
    }

    /// <summary>
    /// States the 3D skybox every player is told about.
    ///
    /// <para>
    /// A push, not an answer to a question, and the rates are why: a map states
    /// its skybox once at level load, while the server reads it for every
    /// player every snapshot. The native side keeps the last thing pushed.
    /// </para>
    /// </summary>
    public void SetSkyboxParams(in SkyboxParams parameters)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return;

        fixed (SkyboxParams* p = &parameters) { _worldServerArea.SetSkyboxParams(p); }
    }

    /// <summary>
    /// The 3D-skybox area of one client, read off the standalone hierarchy —
    /// the other half of the pair above.
    /// </summary>
    public bool TryStandalonePlayerSkyboxArea(int edictIndex, out int area)
    {
        area = 0;
        if (!E.IsInitialized || !E.ShimsAttached) return false;

        int value = 0;
        int found = _entitiesServerArea.EntitiesStandalonePlayerSkyboxArea(edictIndex, &value);
        if (found == 0) return false;

        area = value;
        return true;
    }

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

internal sealed unsafe partial class EngineWeapons
{
    public EngineWeapons(EngineSide side)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
    }

    // The client's members, as a server engine answers them: what each one's
    // attach guard answered here, where the client's areas are never attached.
    public void RegisterAmmoType(string name, DamageType damageType, int maxCarry, int tracerType = 0, float damageForce = 0f, float damage = 0f) { }
}

internal sealed unsafe partial class EngineWorldQuery
{
    private readonly EntitiesServerArea _entitiesServerArea;
    private readonly WorldServerArea _worldServerArea;

    public EngineWorldQuery(EngineSide side,
                            EntitiesServerArea entitiesServerArea,
                            WorldArea worldArea,
                            WorldServerArea worldServerArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _entitiesServerArea = entitiesServerArea ?? throw new ArgumentNullException(nameof(entitiesServerArea));
        _worldArea = worldArea ?? throw new ArgumentNullException(nameof(worldArea));
        _worldServerArea = worldServerArea ?? throw new ArgumentNullException(nameof(worldServerArea));
    }

    /// <summary>
    /// The right and up vectors of a surface facing a direction.
    ///
    /// The ENGINE's VectorAngles/AngleVectors rather than a port of them: the
    /// client places the panes it draws with the same two vectors, in native
    /// code, from the same normal. Two implementations agreeing to the last bit
    /// is not something to assume.
    /// </summary>
    public (Vector Right, Vector Up) AngleVectorsFromForward(Vector forward)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return (default, default);

        float* f = stackalloc float[3] { forward.X, forward.Y, forward.Z };
        float* r = stackalloc float[3];
        float* u = stackalloc float[3];

        _worldServerArea.AngleVectorsFromForward(f, r, u);

        return (new Vector(r[0], r[1], r[2]), new Vector(u[0], u[1], u[2]));
    }

    //-------------------------------------------------------------------------
    // Asking the world what is there.
    //
    // The primitives behind the emitter entities. Everything else in the entity
    // API is about a subject — this entity's health, that entity's velocity. A
    // radius query has no subject: it is a question about the world, and the
    // entity that asked is only where the question was asked from.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Fills <paramref name="into"/> with everything within a radius and returns
    /// how many were written.
    ///
    /// A result equal to the buffer's length means the answer was TRUNCATED, not
    /// that it happened to fit. Callers that care should notice and say so
    /// rather than treating a full buffer as a complete answer.
    ///
    /// Unfiltered on purpose. Each emitter discards a different subset — a push
    /// field ignores what it cannot move, an explosion ignores what cannot be
    /// hurt — so what is worth acting on is decided here, not natively.
    /// </summary>
    public int EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into)
    {
        if (!E.IsInitialized || !E.ShimsAttached || into.IsEmpty) return 0;

        float* c = stackalloc float[3] { center.X, center.Y, center.Z };

        // Native writes RAW handles into the span's storage, and an
        // EntityHandle stores its raw handle inverted (Entity.cs): the elements
        // it wrote are decoded in place before anyone reads them.
        int written;
        fixed (uint* p = EntityHandle.RawView(into))
            written = _entitiesServerArea.EntitiesInSphere(c, radius, p, into.Length);

        written = Math.Clamp(written, 0, into.Length);
        EntityHandle.DecodeRawInPlace(into[..written]);
        return written;
    }

    /// <summary>
    /// A swept BOX, where <see cref="TraceLine"/> sweeps a ray.
    ///
    /// <para>
    /// The question a ray cannot answer is "would this fit here", which is what
    /// anything about to place an object in the world has to ask first.
    /// </para>
    /// </summary>
    /// <param name="mins">
    /// Corner offsets RELATIVE to the start point, which is how the engine wants
    /// a hull and how a caller holding an entity's bounds already has them.
    /// </param>
    public HullResult TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, EntityHandle ignore = default)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return new HullResult(false, false, EntityHandle.None);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };
        uint hit;
        int startSolid;

        int blocked = _worldServerArea.TraceHull(s, e, lo, hi, (int)mask, E.Skippable(ignore), &hit, &startSolid);

        return new HullResult(blocked != 0, startSolid != 0, new EntityHandle(hit));
    }

    public EntityHullTrace EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs, uint mask, int collisionGroup) => E.EntityTraceHull(_worldServerArea, entity, start, end, mins, maxs, mask, collisionGroup);
    /// <summary>Traces a line through the world.</summary>
    public TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return new TraceResult(false, 1f, end, Vector.Zero, EntityHandle.None);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        uint hit;

        int stopped = _worldServerArea.TraceLine(s, e, (int)mask, E.Skippable(ignore),
                                    &fraction, endPos, normal, &hit);

        return new TraceResult(
            stopped != 0,
            fraction,
            new Vector(endPos[0], endPos[1], endPos[2]),
            new Vector(normal[0], normal[1], normal[2]),
            new EntityHandle(hit));
    }

    /// <summary>
    /// Traces a line through the world and names the SURFACE that stopped it.
    ///
    /// <para>
    /// A slot of its own rather than a wider <see cref="TraceLine"/>. That one
    /// is the hottest in this API — 1.573 crossings per server frame at rest —
    /// and <c>ClientTraceLine</c>'s comment in <c>sourcesharp.h</c> records this
    /// ABI refusing to widen it once already, when the client wanted a hitbox.
    /// </para>
    ///
    /// <para>
    /// WHO WANTS IT: a bullet. <c>Arsenal.FireBullets</c> dispatches an impact
    /// effect per pellet, and the client chooses the decal and the impact sound
    /// from the surfaceprop the SERVER sent — that is what stock's
    /// <c>CBaseEntity::ImpactTrace</c> writes into the effect data. A server
    /// that names none leaves a wooden crate taking a concrete decal and cement
    /// flecks.
    /// </para>
    ///
    /// <para>
    /// A trace that hit nothing reports <see cref="SurfaceTraceResult.SurfaceProps"/>
    /// as -1, which is the ABI's answer and not a softening of one: index 0 is
    /// the real <c>default</c> surface.
    /// </para>
    /// </summary>
    public SurfaceTraceResult TraceLineSurface(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return new SurfaceTraceResult(false, 1f, end, Vector.Zero, EntityHandle.None, -1, 0);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        uint hit;
        int surfaceProps;
        int surfaceFlags;
        int hitGroup;

        int stopped = _worldServerArea.TraceLineSurface(s, e, (int)mask, E.Skippable(ignore),
                                           &fraction, endPos, normal, &hit,
                                           &surfaceProps, &surfaceFlags, &hitGroup);

        return new SurfaceTraceResult(
            stopped != 0,
            fraction,
            new Vector(endPos[0], endPos[1], endPos[2]),
            new Vector(normal[0], normal[1], normal[2]),
            new EntityHandle(hit),
            surfaceProps,
            surfaceFlags,
            (HitGroup)hitGroup);
    }

    /// <summary>
    /// Traces from a player's eye along their aim. Returns what was hit, or
    /// <see cref="EntityHandle.None"/> for a miss.
    /// </summary>
    public EntityHandle TracePlayerAim(EntityHandle player, float range, out Vector hitPoint)
    {
        hitPoint = default;

        if (!E.IsInitialized || !E.ShimsAttached)
            return EntityHandle.None;

        float* xyz = stackalloc float[3];
        uint hit = _worldServerArea.TracePlayerAim(player.Value, range, xyz);

        hitPoint = new Vector(xyz[0], xyz[1], xyz[2]);
        return new EntityHandle(hit);
    }

    /// <summary>
    /// Where the water beneath a point is, and what kind it is.
    ///
    /// <para>
    /// Three answers from one call, because the trace has to run to give any of
    /// them: asking "am I under water" and "where is the surface" separately
    /// would run it twice and let the two answers disagree about the same
    /// water.
    /// </para>
    /// </summary>
    public WaterBelow WaterSurfaceBelow(Vector start)
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return new WaterBelow(false, false, false, start);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* surface = stackalloc float[3];
        int slime;

        int answer = _worldServerArea.WaterSurfaceBelow(s, surface, &slime);

        return new WaterBelow(
            Submerged: answer < 0,
            Found: answer > 0,
            Slime: slime != 0,
            Surface: answer > 0 ? new Vector(surface[0], surface[1], surface[2]) : start);
    }

    /// <summary>
    /// worldspawn's chaptertitle key — the world's one game-logic key. Empty
    /// when the map set no title, on the client (the slot is server-only), or
    /// before the world exists.
    /// </summary>
    public string WorldChapterTitle()
    {
        if (!E.IsInitialized || !E.ShimsAttached)
            return string.Empty;

        return EngineSide.FromUtf8(_worldServerArea.WorldChapterTitle());
    }

    /// <summary>
    /// The surfaceprop an entity's own MODEL declares, or -1.
    ///
    /// <para>
    /// A TRACE answers with the surfaceprop of the face it struck, which is
    /// the right answer for a wall and the wrong one for a
    /// func_breakable_surf: vbsp refuses to compile that entity unless exactly
    /// ONE of its faces is drawn, so the other five carry nodraw's
    /// surfaceprop — 0, the default, concrete. Shot from behind, glass reports
    /// concrete.
    /// </para>
    ///
    /// <para>
    /// -1 IS THE COMMON ANSWER AND NOT A FAILURE. It means "nothing better
    /// than the face you struck", which is what a studio model (whose
    /// per-hitbox surfaceprop is finer than any whole-model answer), a brush
    /// drawing several materials (there is no single answer), and a side with
    /// no materials loaded all honestly are.
    /// </para>
    /// </summary>
    public int EntityModelSurfaceProp(EntityHandle entity)
    {
        if (!E.IsInitialized || !E.ShimsAttached) return -1;

        return _worldServerArea.EntityModelSurfaceProp(entity.Value);
    }

    /// <summary>
    /// A surfaceprop by name — "glass", "tile", "metal" — or -1. What a map
    /// key that names a KIND of surface rather than a material resolves
    /// through.
    /// </summary>
    public int SurfacePropIndex(string name)
    {
        if (!E.IsInitialized || !E.ShimsAttached || name.Length == 0) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");

        fixed (byte* p = bytes) { return _worldServerArea.SurfacePropIndex(p); }
    }

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
