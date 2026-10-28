using System.Diagnostics.CodeAnalysis;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// The server's half of <see cref="EngineSide"/> (plan_shim_areas_split phase 7).
///
/// <para>
/// Each side compiles its own <see cref="EngineSide"/> since phase 3, so what
/// differs by side is said once, here and in the client's file, rather than
/// chosen at run time: each side's half registers its own areas
/// (<see cref="ServerAreaRegistration"/> here), and a slot both sides serve is
/// on a shared base area both register, so the shared source compiles against
/// either. No <c>#if</c>: the side is the assembly.
/// </para>
///
/// <para>
/// And every member that reaches only the server's areas: they live here, in
/// the server's project, rather than in the shared file, where a client's
/// build compiled them only to answer a default (review of phase 7). The
/// bindings that call them are split the same way (EngineBinding.Server.cs).
/// </para>
/// </summary>
internal sealed unsafe partial class EngineSide
{
    // The areas the server's own services read (beside the shared four in
    // EngineSide.cs): the precache its break models use, the shell's
    // velocity, eye, origin and gravity members and the physics object its
    // motion host reads, and the player slots the movement loop's pass entity
    // is found in. Assigned with the shared four at each attach.
    private IPrecacheArea _precacheArea = null!;
    private ITransformArea _transformArea = null!;
    private IPhysicsArea _physicsArea = null!;
    private IPlayersArea _playersArea = null!;

    /// <summary>
    /// The server's own areas from <paramref name="areas"/>, at construction
    /// and at each attach. The motion host is made over the areas it is given,
    /// so an attach drops one made before it.
    /// </summary>
    [MemberNotNull(nameof(_precacheArea), nameof(_transformArea), nameof(_physicsArea), nameof(_playersArea))]
    private void OwnAreas(IServiceLocator areas)
    {
        _precacheArea = areas.Require<IPrecacheArea>();
        _transformArea = areas.Require<ITransformArea>();
        _physicsArea = areas.Require<IPhysicsArea>();
        _playersArea = areas.Require<IPlayersArea>();
        _motion = null;
    }

    /// <summary>
    /// The model precache the side's break models use: the server's own. A
    /// client engine built in this assembly (a cross-side fact) has none, as
    /// the client's slot was null.
    /// </summary>
    internal Func<string, int>? OwnModelPrecache => _attachment.Side == Side.Server ? PrecacheModel : null;

    /// <summary>
    /// The entity's one-object vphysics calls (plan_physics_bones_native_zero,
    /// the last thin forwards): made here, through the object's vtable, and
    /// the velocity home and the impulse decided here rather than in native.
    /// </summary>
    private EntityPhysicsMotion? _motion;

    internal EntityPhysicsMotion Motion => _motion ??= new EntityPhysicsMotion(new MotionHost(this), new VPhysicsMotionCalls(VPhysics));

    /// <summary>
    /// The entity half of <see cref="Motion"/>: the held object pointer, and
    /// the shell's own velocity members through the slots that keep only
    /// them. Callers have checked the side is attached.
    /// </summary>
    private sealed class MotionHost(EngineSide side) : IEntityPhysicsMotionHost
    {
        public nint PhysicsObject(EntityHandle entity) => side.EntityPhysicsObject(entity);

        public Vector EntityVelocity(EntityHandle entity)
        {
            float* xyz = stackalloc float[3];
            side._transformArea.EntityGetVelocity(entity.Value, xyz);
            return new Vector(xyz[0], xyz[1], xyz[2]);
        }

        public void SetEntityVelocity(EntityHandle entity, Vector velocity)
        {
            float* xyz = stackalloc float[3] { velocity.X, velocity.Y, velocity.Z };
            side._transformArea.EntitySetVelocity(entity.Value, xyz);
        }

        public Vector EntityAngularVelocity(EntityHandle entity)
        {
            float* pyr = stackalloc float[3];
            side._transformArea.EntityGetAngularVelocity(entity.Value, pyr);
            return new Vector(pyr[0], pyr[1], pyr[2]);
        }

        public void SetEntityAngularVelocity(EntityHandle entity, Vector degreesPerSecond)
        {
            float* pyr = stackalloc float[3] { degreesPerSecond.X, degreesPerSecond.Y, degreesPerSecond.Z };
            side._transformArea.EntitySetAngularVelocity(entity.Value, pyr);
        }

        public Vector Angles(EntityHandle entity) => side.EntityGetAngles(entity);

        public void Warning(string message) => side.Warning(message);
    }

    /// <summary>
    /// A new <see cref="EntityFactories"/> pointed at the native entity factory
    /// dictionary — the five slots the <c>sub-factory</c> lane requested and the
    /// <c>abi-tail</c> merge appended at ABI 242.
    ///
    /// <para>
    /// <b>ONLY WHEN THE SLOTS ARE REALLY THERE, and only on the server.</b> A
    /// null provider means "not asked"; providers wired to slots that answer
    /// zero would mean "the table is empty", and those are different answers
    /// with different consequences —
    /// <see cref="EntityFactories.OwnerOf"/> reads an unasked table as
    /// <c>Native</c>, which is the assumption this build already runs on, and an
    /// empty one as <c>Nothing</c>, which refuses <c>ent_create prop_physics</c>
    /// for a class that works. The client has no factory dictionary at all, so
    /// binding there would produce exactly that empty table.
    /// </para>
    ///
    /// <para>
    /// The refusal ledger is bound as a SET of three or not at all, because
    /// <see cref="EntityFactories.Refresh"/> skips the whole ledger without all
    /// three and a partial wiring would read as "nothing has been refused".
    /// </para>
    ///
    /// <para>
    /// A NEW INSTANCE PER CALL, which the server's composition asks for once
    /// (through <c>EngineServices.EntityFactories</c>) and registers in its own
    /// container — plan_abi_hotreload D7. The providers were five statics set
    /// from <c>Initialize</c>; two managed game instances would have shared them.
    /// </para>
    /// </summary>
    internal EntityFactories BindEntityFactories(IEntityLifecycleArea lifecycleArea)
    {
        // The slots are the EntityLifecycle area's (the struct lost them in C6c),
        // which only the server registers.
        if (_attachment.Side != Side.Server)
            return new EntityFactories();

        var factories = new EntityFactories
        {
            NativeCount = () => EntityFactoryCount(lifecycleArea),
            NativeNameAt = index => EntityFactoryNameAt(lifecycleArea, index),
            RefusalCount = () => EntityFactoryRefusalCount(lifecycleArea),
            RefusalNameAt = index => EntityFactoryRefusalNameAt(lifecycleArea, index),
            RefusalTimesAt = index => EntityFactoryRefusalTimesAt(lifecycleArea, index),
        };

        // PULLED ON THE WAY OUT. A cs_sv_reload builds a new composition, and so
        // a new table, with no map load after it to refresh one: the static
        // table this replaced simply survived. The dictionary is filled at
        // server.so's static init, so it is complete here; the map spawner
        // still pulls again per map, after SpawnWorld writes the refusals.
        factories.Refresh();
        return factories;
    }

    /// <summary>
    /// Binds a state object to an entity's blob, once, in place of resolving the
    /// entity on every access.
    ///
    /// <para>
    /// A null pointer with zero bytes is a real answer, not a failure: most
    /// entities carry no blob. <see cref="NetState.BindTo"/> turns it into a named
    /// exception, because a state whose entity has no storage is a state that
    /// would replicate nothing and say nothing about why.
    /// </para>
    /// </summary>
    internal EntityBlobBinding BindEntityBlob(INetworkServerArea networkArea, EntityHandle h)
    {
        int size = 0;
        uint serial = 0;
        EntitySlot* slot = null;

        void* p = networkArea.EntityBindBlob(h.Value, &size, &slot, &serial);

        if (p == null || size <= 0 || slot == null)
            return default;

        return new EntityBlobBinding((byte*)p, size, slot, serial);
    }

    /// <summary>
    /// A MANAGED WRITE TO ONE ENGINE-BLOCK FIELD, announced by offset
    /// (plan_entity.md step 1b; <c>.claude/entity-port/abi-requests/1b.md</c> §1).
    ///
    /// <para>
    /// <see cref="MarkEntityStateChanged"/> above sets
    /// <c>FL_FULL_EDICT_CHANGED</c>, which makes the local network backdoor
    /// copy EVERY prop in the entity's table. That is the honest answer for a
    /// generic payload word, which can only say "one of hundreds of them
    /// moved". A block field is different: Origin, Angles and ModelIndex each
    /// have a TYPED SendProp at exactly their own offset, so a write to one can
    /// name it and the engine copies that prop alone.
    /// </para>
    ///
    /// <para>
    /// <paramref name="blockOffset"/> is measured INSIDE THE BLOCK — take it
    /// from <see cref="EngineBlockMarks.OffsetInBlockOf"/>. Native adds the
    /// block base, which is the one number managed code cannot know: it is
    /// <c>ALIGN_VALUE(sizeof the largest shell, 16)</c> and it moves with any
    /// edit to a shell.
    /// </para>
    ///
    /// <para>
    /// NO PRODUCTION CALLER YET, and that is stated rather than hidden. Nothing
    /// managed writes the block directly today — every managed origin write
    /// still goes through <c>EntitySetOrigin</c>, whose native body writes the
    /// block and marks the two offsets itself. This is the slot the block
    /// writer will use when it lands; it exists now because the offsets are the
    /// op table's and the op table is step 1f's.
    /// </para>
    /// </summary>
    public void MarkEntityBlockChanged(INetworkServerArea networkArea, EntityHandle h, int blockOffset)
    {
        networkArea.EntityMarkBlockChanged(h.Value, blockOffset);
    }

    /// <summary>Whether a player is alive. False covers both dead and spectating.</summary>
    /// <summary>
    /// The player's facts and the shadow body native holds for them
    /// (plan_physics_bones_native_zero 2.5): what PlayerShadowBodies reads
    /// before it reads vphysics itself. False when the handle names no player.
    /// INTERNAL, like the three below and Teleported: they are the server
    /// interop's player-shadow half (EnginePlayerShadowNative), not an engine
    /// area any game code asks.
    /// </summary>
    internal bool PlayerShadowFacts(IMovementServerArea movementArea, EntityHandle player, out PlayerShadowFacts facts)
    {
        facts = default;

        facts.StructSize = (uint)sizeof(PlayerShadowFacts);
        fixed (PlayerShadowFacts* p = &facts)
            return movementArea.PlayerShadowFacts(player.Value, p) != 0;
    }

    /// <summary>Native holds the body managed built (stand, crouch, controller). False when the player is gone.</summary>
    internal bool PlayerShadowAdopt(IMovementServerArea movementArea, EntityHandle player, nint stand, nint crouch, nint controller)
        => movementArea.PlayerShadowAdopt(player.Value, stand, crouch, controller) != 0;

    /// <summary>Native tears the held body down (DestroyVCollision), or drops it if its environment has gone.</summary>
    internal void PlayerShadowRelease(IMovementServerArea movementArea, EntityHandle player)
    {
        movementArea.PlayerShadowRelease(player.Value);
    }

    /// <summary>The hull in play; after a command, m_oldOrigin and the move's step height consumed.</summary>
    internal void PlayerShadowCommit(IMovementServerArea movementArea, EntityHandle player, int state, bool afterCommand)
    {
        movementArea.PlayerShadowCommit(player.Value, state, afterCommand ? 1 : 0);
    }

    /// <summary>
    /// Whether the handle names a player. FALSE WITHOUT ASKING for a shell-less
    /// entity (step 2e): a player is kind Player, which is networked and has a
    /// shell, so a shell-less entity can never be one.
    /// </summary>
    internal bool EntityIsPlayer(IEntitiesServerArea entitiesServerArea, EntityHandle h)
        => !ShellLess.Claims(h) && entitiesServerArea.EntityIsPlayer(h.Value) != 0;

    /// <summary>Which way something is looking. Not its body angles.</summary>
    internal Vector EntityGetEyeAngles(ITransformArea transformArea, EntityHandle h)
    {
        float* pyr = stackalloc float[3];
        transformArea.EntityGetEyeAngles(h.Value, pyr);
        return new Vector(pyr[0], pyr[1], pyr[2]);
    }

    /// <summary>
    /// The entity's classname. A shell-less entity is answered here and never
    /// crosses (step 2e).
    /// </summary>
    internal string EntityGetClassName(IEntitiesServerArea entitiesServerArea, EntityHandle h)
    {
        if (ShellLess.State(h) is { } shellLess)
            return shellLess.ClassName;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        entitiesServerArea.EntityGetClassName(h.Value, buf, bufSize);
        return FromUtf8(buf);
    }

    /// <summary>
    /// Moves an entity. A shell-less entity's origin is its managed state's
    /// (step 2e).
    /// </summary>
    internal void EntitySetOrigin(EntityHandle h, Vector origin)
    {
        if (ShellLess.State(h) is { } shellLess)
        {
            shellLess.Origin = origin;
            return;
        }

        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        _transformArea.EntitySetOrigin(h.Value, xyz);
    }

    /// <summary>The player in slot <paramref name="playerIndex"/>, or <see cref="EntityHandle.None"/>.</summary>
    internal EntityHandle GetPlayerHandle(int playerIndex) => new(_playersArea.GetPlayerHandle(playerIndex));

    /// <summary>
    /// Spawns an entity. A shell-less entity spawns in managed code, in the
    /// same position of the same pass (step 2e); the crossing this replaces was
    /// outbound and is therefore not in any Crossings counter.
    /// </summary>
    internal void SpawnEntity(IEntityLifecycleArea entityLifecycleArea, EntityHandle entity)
    {
        if (ShellLess.Claims(entity))
        {
            ShellLess.Mine!.Spawn(entity);
            return;
        }

        entityLifecycleArea.EntitySpawn(entity.Value);
    }

    /// <summary>
    /// Puts a player into or out of spectating. The move type it implies is the
    /// managed record's (plan_server_native_v2 T4g); native keeps the solidity
    /// and its own readers' copy.
    /// </summary>
    internal void PlayerSetObserverMode(IPlayersArea playersArea, EntityHandle player, ObserverMode mode)
    {
        PlayerMoves.Target?.SetObserverMode(player, (int)mode);
        playersArea.PlayerSetObserverMode(player.Value, (int)mode);
    }

    /// <summary>
    /// THE SHELLS BEHIND ONE FRAME'S REMOVALS, FREED — one call, whatever the
    /// size of the batch (<c>plan_entity.md</c> §3.4, step 2c).
    ///
    /// <para>
    /// The only caller is <see cref="EntityDirectory.FreeRemoved"/>, which owns
    /// WHEN this happens; what a shell is and how it is freed is native's.
    /// An empty batch does not cross at all.
    /// </para>
    /// </summary>
    public void DestroyShells(IEntityLifecycleArea lifecycleArea, ReadOnlySpan<EntityHandle> batch)
    {
        if (batch.IsEmpty)
            return;

        // AN EntityHandle IS NOT ITS RAW HANDLE: it stores it inverted, so
        // default is the invalid handle (Entity.cs). The ABI asks for packed
        // uint32s, so they are copied out: on the stack for an ordinary
        // frame's removals, from the pool for a level's teardown.
        const int OnStack = 256;
        uint[]? rented = batch.Length > OnStack ? ArrayPool<uint>.Shared.Rent(batch.Length) : null;
        try
        {
            Span<uint> raw = rented is null ? stackalloc uint[batch.Length] : rented.AsSpan(0, batch.Length);
            EntityHandle.CopyRaw(batch, raw);

            fixed (uint* p = raw)
            {
                lifecycleArea.DestroyShell(p, batch.Length);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<uint>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// <b>THE SAFETY COUNTER FOR THE ENGINE-INVISIBLE RANGE</b> —
    /// <c>plan_entity.md</c> step 2e's third gate clause.
    ///
    /// <para>
    /// How many times a handle from entries 2048..8191 has arrived at
    /// <c>SourceSharp_ResolveEntity</c> and failed to resolve in the engine's
    /// own entity list — which a shell-less entity's handle always does,
    /// because it was never added to that list, and a live stock server-only
    /// <c>CBaseEntity</c>'s never does.
    /// </para>
    ///
    /// <para>
    /// <b>Read it beside <see cref="EntityDirectory.ShellLessCount"/>, never on
    /// its own.</b> A build that created no shell-less entities would leak none
    /// of their handles either, so the zero would be true and mean nothing.
    /// <c>status.logical</c> reports the pair.
    /// </para>
    ///
    /// <para>Zero with no engine, which is what a hermetic test sees.</para>
    /// </summary>
    /// <remarks>
    /// <b>INTERNAL, and not on an engine area.</b> It is instrumentation about
    /// the boundary rather than a capability game code asks for — its one
    /// caller is <see cref="ScriptedEntityRegistry.ShellLessNativeReach"/>, and
    /// putting it on <c>IEngineEntities</c> would publish a diagnostic counter
    /// to every consumer of the entity API.
    /// </remarks>
    internal int InvisibleRangeReach(IWorldServerArea worldArea)
        => worldArea.InvisibleRangeReach();

    public Vector EntityGetVelocity(EntityHandle h)
    {
        // ZERO, AND IT IS THE TRUE ANSWER -- step 2e. A shell-less entity is
        // not in any movement integrator and has nowhere to keep a velocity;
        // the shell's answer for a MOVETYPE_NONE server-only entity was zero
        // too.
        if (ShellLess.Claims(h))
            return default;

        return Motion.Velocity(h);
    }

    /// <summary>Where something looks from. A player's origin is at their feet.</summary>
    public Vector EntityGetEyePosition(EntityHandle h)
    {
        float* xyz = stackalloc float[3];
        _transformArea.EntityGetEyePosition(h.Value, xyz);
        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    public void EntitySetVelocity(EntityHandle h, Vector velocity)
    {
        Motion.SetVelocity(h, velocity);
    }

    /// <summary>
    /// Adds to velocity rather than replacing it. Clearing the ground matters
    /// more than it looks: something standing on the floor stays stuck to it, so
    /// an upward push does nothing unless the ground is let go of first.
    /// </summary>
    public void EntityApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround)
        => ApplyVelocityImpulse(PlayerMoves.Target, Motion, h, impulse, clearGround);

    /// <summary>
    /// The impulse's two homes. A player's velocity and ground are its movement
    /// record's (plan_server_native_v2 T4g), so <paramref name="players"/> takes
    /// a player's whole impulse; the player's physics object is its shadow,
    /// which the next move overwrites, so an impulse there was lost. Anything
    /// else is <paramref name="motion"/>'s: the solver's object, where
    /// clearGround has nothing to write (EntityPhysicsMotion's remarks).
    /// </summary>
    internal static void ApplyVelocityImpulse(IServerPlayerMovementRoute? players, EntityPhysicsMotion motion,
                                              EntityHandle h, Vector impulse, bool clearGround)
    {
        if (players?.ApplyVelocityImpulse(h, impulse, clearGround) == true)
            return;

        motion.ApplyVelocityImpulse(h, impulse);
    }

    public void EntitySetGravity(EntityHandle h, float gravity)
    {
        _transformArea.EntitySetGravity(h.Value, gravity);
    }

    //-------------------------------------------------------------------------
    // WHAT MANAGED PARENTING ASKS OF THE WORLD (step 2h, made live).
    //
    // Four calls, and the count is what says how much of parenting is managed
    // now: the registry, the composition, the cycle refusal, the deferred
    // parentname and the sweep are all in Hierarchy.cs, and these are the
    // questions it cannot answer for itself. Not one of them decides anything.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE ENGINE BLOCK, AND IT IS NOT <see cref="GetEntityBlob"/>.
    ///
    /// <para>
    /// <b>The two overlap and have different lengths, which is exactly the trap.</b>
    /// The block is the fixed 352-byte prefix of the entity's allocation and is
    /// always there — <c>CStandaloneEntity::operator new</c> floors every
    /// allocation at <c>SourceSharp_StandaloneMinBlobBytes()</c> for that reason.
    /// <see cref="GetEntityBlob"/> answers the NETWORKED payload, whose length is
    /// the entity's tier: eight words for a class that declared no state of its
    /// own, which is most of the map. Reading the block through that span sees
    /// thirty-two bytes of it and refuses everything past them — silently,
    /// because a short span is a legal span.
    /// </para>
    ///
    /// <para>
    /// So a composition asking "where is this parent" through the payload span
    /// gets a refusal for every ordinary prop in the map and leaves its children
    /// standing still. That is the bug this slot exists to remove, and it is the
    /// reason the block gets a getter of its own rather than a longer comment on
    /// the other one.
    /// </para>
    /// </summary>
    public Span<byte> GetEntityBlock(INetworkServerArea networkArea, EntityHandle h)
    {
        int size;
        void* p = networkArea.EntityGetEngineBlock(h.Value, &size);

        if (p == null || size <= 0)
            return Span<byte>.Empty;

        return new Span<byte>(p, size);
    }

    /// <summary>
    /// Stock's <c>VPhysicsSwapObject</c>: the shell holds <paramref name="physicsObject"/>
    /// (0 for none) and <paramref name="previous"/> is the object it held
    /// before, which is NOT destroyed. False when the handle names no shell.
    /// INTERNAL: the server interop's half of the managed physics init
    /// (EntityPhysicsCreation), not an area game code asks.
    /// </summary>
    internal bool EntitySwapPhysicsObject(EntityHandle entity, nint physicsObject, out nint previous)
    {
        previous = 0;

        void* held = null;
        int ok = _physicsArea.EntitySwapPhysicsObject(entity.Value, physicsObject, &held);
        previous = (nint)held;
        return ok != 0;
    }

    //-------------------------------------------------------------------------
    // Brush movement. Doors, lifts, platforms.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Spins an entity continuously. Not the same as rotating TO an angle: a fan
    /// never arrives anywhere, so it has no move-done and no destination.
    /// </summary>
    public void EntitySetAngularVelocity(EntityHandle entity, Vector degreesPerSecond)
    {
        Motion.SetAngularVelocity(entity, degreesPerSecond);
    }

    /// <summary>
    /// What a physics object is actually turning at, degrees per second.
    ///
    /// Read from the simulation rather than the entity: a prop's entity-level
    /// angular velocity is not refreshed every tick, so a sensor watching one
    /// through the entity would mostly read stale zeroes.
    /// </summary>
    public Vector EntityGetAngularVelocity(EntityHandle entity)
    {
        return Motion.AngularVelocity(entity);
    }

    /// <summary>
    /// Applies force at a point in the world rather than at the centre of mass.
    ///
    /// The only form that can spin something — the difference between pushing a
    /// crate and tipping it. <see cref="EntityApplyVelocityImpulse"/> always
    /// acts through the centre, so it cannot rotate anything at all.
    /// </summary>
    public void EntityApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint)
    {
        Motion.ApplyForceOffset(entity, force, worldPoint);
    }

    /// <summary>Spins a physics object about its own centre.</summary>
    public void EntityApplyTorque(EntityHandle entity, Vector torque)
    {
        Motion.ApplyTorque(entity, torque);
    }

    /// <summary>How heavy a physics object is. Zero if it has no physics at all.</summary>
    public float EntityGetMass(EntityHandle entity)
        => Motion.Mass(entity);

    public void EntitySetMass(EntityHandle entity, float mass)
    {
        Motion.SetMass(entity, mass);
    }

    /// <summary>
    /// Whether the engine moves this at all. A prop with motion disabled is
    /// still solid and still collides; it simply does not respond.
    /// </summary>
    public void EntityEnableMotion(EntityHandle entity, bool enable)
    {
        Motion.EnableMotion(entity, enable);
    }

    /// <summary>
    /// Puts a settled physics object back into the simulation —
    /// <c>IPhysicsObject::Wake()</c>, which is
    /// <c>ensure_in_simulation()</c> and nothing else.
    ///
    /// <para>
    /// NOT <see cref="EntityEnableMotion"/> with true. Waking does not unpin a
    /// prop whose motion a map deliberately disabled, and stock's
    /// <c>CPhysicsProp::InputWake</c> (<c>props.cpp:2707</c>) does not either:
    /// a crate pinned by the MotionDisabled spawnflag stays pinned through a
    /// Wake input.
    /// </para>
    /// </summary>
    public void EntityPhysicsWake(EntityHandle entity)
    {
        Motion.Wake(entity);
    }

    /// <summary>
    /// Lets a physics object settle out of the simulation —
    /// <c>IPhysicsObject::Sleep()</c> and nothing else.
    ///
    /// <para>
    /// NOT <see cref="EntityEnableMotion"/> with false, which turns
    /// <c>IsMoveable()</c> false. A SLEPT object stays moveable, so any
    /// contact revives it and the player's shadow controller still shoves it;
    /// a PINNED one revives for nothing and reads to
    /// the player's shadow controller as a wall that clamps the
    /// player's velocity to zero along the contact normal.
    /// </para>
    /// </summary>
    public void EntityPhysicsSleep(EntityHandle entity)
    {
        Motion.Sleep(entity);
    }

    /// <summary>
    /// ORs physics game flags (FVPHYSICS_*, and SourceSharp's own such as
    /// force-collide-moveable) into the entity's physics object, as stock's
    /// PhysSetGameFlags. The collision rules read them. No object, no effect.
    /// </summary>
    public void EntityAddPhysicsGameFlags(EntityHandle entity, uint flags)
    {
        Motion.AddGameFlags(entity, flags);
    }

    /// <summary>
    /// The entity's <c>IPhysicsObject *</c>, or 0 for none: what managed calls
    /// vphysics through directly, by the generated <c>PhysicsObjectFwd</c>
    /// (plan_physics_managed). Valid until the entity's object is destroyed.
    /// </summary>
    public nint EntityPhysicsObject(EntityHandle entity)
        => _physicsArea.EntityPhysicsObject(entity.Value);

    /// <summary>
    /// The level's <c>IPhysicsEnvironment *</c>, or 0 between levels: managed
    /// installs its own vphysics callback tables on it, by the generated
    /// <c>PhysicsEnvironmentFwd</c> (plan_physics_managed B2).
    /// </summary>
    public nint PhysicsEnvironmentPointer()
        => _physicsArea.PhysicsEnvironmentPointer();

    /// <summary>
    /// <c>IPhysicsCollision::CollideGetExtent</c> for a physics object at its
    /// current pose: the farthest point of its collide along
    /// <paramref name="direction"/>. Zero for no object, no collide, or no
    /// collision table. Called through the vtables: a Vector by-value return
    /// is spelled (tools/vtable-layout's BY_VALUE_RETURNS).
    /// </summary>
    public unsafe Vector PhysicsCollideExtent(nint physicsObject, Vector direction)
    {
        if (physicsObject == 0 || !PhysicsCollisionTable(out void* collision, out PhysicsCollisionFwd* vt))
            return Vector.Zero;

        PhysicsObjectFwd* obj = VPhysics.PhysicsObject;
        void* collide = obj->GetCollide((void*)physicsObject);
        if (collide == null)
            return Vector.Zero;

        Vector origin;
        QAngle angles;
        obj->GetPosition((void*)physicsObject, &origin, &angles);
        return vt->CollideGetExtent(collision, collide, &origin, &angles, &direction);
    }

    /// <summary>
    /// Each active object's owner told the solver moved it, carried out by
    /// native (PhysicsOwner_t::VPhysicsUpdate), and the entities each object
    /// is the own object of written to <paramref name="owners"/>, in order
    /// (plan_physics_bones_native_zero 2.3). The count written. INTERNAL:
    /// the server interop's physics frame half (EnginePhysicsFrameNative).
    /// </summary>
    internal unsafe int PhysicsUpdateOwners(ReadOnlySpan<nint> objects, ReadOnlySpan<float> velocities, Span<uint> owners)
    {
        if (objects.IsEmpty)
            return 0;

        int count = Math.Min(Math.Min(objects.Length, owners.Length), velocities.Length / 3);
        fixed (nint* pObjects = objects)
        fixed (float* pVelocities = velocities)
        fixed (uint* pOwners = owners)
            return _physicsArea.PhysicsUpdateOwners((void**)pObjects, pVelocities, count, pOwners);
    }

    /// <summary>
    /// <c>IPhysicsObject::GetInertia</c>, through the object's vtable (a Vector
    /// by-value return is spelled: tools/vtable-layout's BY_VALUE_RETURNS).
    /// Zero for no object.
    /// </summary>
    public unsafe Vector PhysicsInertia(nint physicsObject)
    {
        if (physicsObject == 0)
            return Vector.Zero;

        return (VPhysics.PhysicsObject)->GetInertia((void*)physicsObject);
    }

    /// <summary>
    /// Whether a prop is simulated or merely collided with. Must be set during
    /// spawn — afterwards the physics object already exists as the wrong kind.
    /// </summary>
    public void EntitySetPhysicsStatic(EntityHandle entity, bool isStatic)
    {
        PhysicsInit.Target?.SetStatic(entity, isStatic);
    }

    /// <summary>
    /// Whether things pass through this entity's physics object and are
    /// reported, rather than colliding with it. A physics trigger is a separate
    /// kind of thing to the solver, not a solid that happens to be intangible.
    /// </summary>
    public void EntityBecomePhysicsTrigger(EntityHandle entity, bool isTrigger)
    {
        Motion.BecomeTrigger(entity, isTrigger);
    }

    /// <summary>
    /// Precaches a model or sprite and answers its INDEX.
    ///
    /// <para>
    /// The index is the useful half: both sides of the wire agree on model
    /// indices, so an entity that networks what it is drawn as sends a number
    /// rather than a path.
    /// </para>
    /// </summary>
    /// <returns>-1 if the model would not load. Do not network that — it is a
    /// lookup that fails every frame on the far side.</returns>
    public int PrecacheModel(string model)
    {
        if (model.Length == 0) return -1;

        var bytes = Encoding.UTF8.GetBytes(model + "\0");
        fixed (byte* p = bytes) { return _precacheArea.PrecacheModel(p); }
    }

    /// <summary>
    /// Applies force through the centre of mass, so it moves without spinning.
    /// The counterpart to <see cref="EntityApplyForceOffset"/>.
    /// </summary>
    public void EntityApplyForceCenter(EntityHandle entity, Vector force)
    {
        Motion.ApplyForceCenter(entity, force);
    }

    /// <summary>
    /// Makes an entity physically simulated.
    ///
    /// Needs a model to read collision data out of, which is the same ordering
    /// trap as prop data: a brush has one during OnSpawn, a prop does not until
    /// the tick after, because SetModel from C# only records a pending name.
    /// Returns whether the entity is simulated afterwards — including when it
    /// already was, so calling twice is safe.
    /// </summary>
    public bool EntityInitPhysics(EntityHandle entity, bool asleep = false, bool debris = false)
        => PhysicsInit.Target?.Create(entity, isStatic: false, asleep, debris) == true;

    /// <summary>
    /// What shape this entity presents to a trace, as the engine's own FSOLID_
    /// bits. Zero for a handle that resolves to nothing.
    ///
    /// <para>
    /// Read off the collideable, so both hierarchies answer: the thing a
    /// projectile has run into may be a CBaseEntity trigger volume or a managed
    /// brush, and the projectile cannot know which it was handed.
    /// </para>
    /// </summary>
    public SolidFlags EntityGetSolidFlags(ICollisionServerArea collisionArea, EntityHandle entity)
    {
        return (SolidFlags)collisionArea.EntityGetSolidFlags(entity.Value);
    }

    //-------------------------------------------------------------------------
    // THE ENTITY FACTORY DICTIONARY'S FIVE QUESTIONS -- the `sub-factory` lane,
    // appended at ABI 242.
    //
    // SERVER ONLY. The slots are NULL on the client, so these answer 0 and null
    // there, which is what keeps EntityFactories.Available false on that side.
    //
    // ONCE PER LEVEL AND ONCE PER cs_sv_reload, from EntityFactories.Refresh --
    // 2 + N crossings with N = 156 on this build, plus 2 + M for the refusal
    // ledger with M = 3. Never per frame and never per entity.
    //
    // INTERNAL, AND DELIBERATELY NOT PART OF THE ENGINE SURFACE GAME CODE SEES.
    // Every other Engine member is forwarded by an EngineBinding area, and
    // EngineAreaTests holds that. These five are NOT, because a second public
    // route to this data is a route round the one distinction that matters:
    // EntityFactories.Available answers "was the dictionary asked", and a caller
    // reading Engine.EntityFactoryCount() straight would get 0 from an unwired
    // build and read it as "the table is empty" -- the exact misreading that
    // refuses `ent_create prop_physics` for a class that works. EntityFactories
    // is the only consumer, and it lives in this assembly.
    //-------------------------------------------------------------------------

    /// <summary>How many classnames the native dictionary holds a factory for.</summary>
    internal int EntityFactoryCount(IEntityLifecycleArea lifecycleArea)
        => lifecycleArea.EntityFactoryCount();

    /// <summary>
    /// The i'th installed classname in install order, or null past the end.
    ///
    /// <para>
    /// NULL AND NOT EMPTY-STRING, which <see cref="FromUtf8"/> would have given:
    /// the contract in <c>sourcesharp.h</c> is that indices are dense and this
    /// answers NULL outside the range, and
    /// <see cref="EntityFactories.Refresh"/> ENDS its walk on that answer rather
    /// than skipping it — a null in the middle means the far side is not the
    /// table this was written against, and walking on would read whatever came
    /// after it.
    /// </para>
    /// </summary>
    internal string? EntityFactoryNameAt(IEntityLifecycleArea lifecycleArea, int index)
    {
        return Utf8OrNull(lifecycleArea.EntityFactoryNameAt(index));
    }

    /// <summary>How many distinct classnames have been asked for and refused.</summary>
    internal int EntityFactoryRefusalCount(IEntityLifecycleArea lifecycleArea)
        => lifecycleArea.EntityFactoryRefusalCount();

    /// <summary>The i'th refused classname, or null past the end.</summary>
    internal string? EntityFactoryRefusalNameAt(IEntityLifecycleArea lifecycleArea, int index)
    {
        return Utf8OrNull(lifecycleArea.EntityFactoryRefusalNameAt(index));
    }

    /// <summary>How many times the i'th refused classname was asked for.</summary>
    internal int EntityFactoryRefusalTimesAt(IEntityLifecycleArea lifecycleArea, int index)
        => lifecycleArea.EntityFactoryRefusalTimesAt(index);

    // The server's late routes (plan_engineside_removal Phase 3): each made
    // empty here, registered by the side's container (EngineServices.Server)
    // before any binding is built, taken by constructor by the bindings that
    // read it, and filled by the composition once what answers it exists.

    // THE SERVER'S SOUNDS ARE MANAGED (plan_server_native_v2 T4a): the emit
    // and patch calls go to the managed SoundEmitter's route, which the
    // server's composition fills (ServerSoundRouting).
    internal SideRoute<IServerSoundRoute> ServerSounds { get; } = new();

    // What the server's traces cost (plan_server_native_v2 T4f): timed around
    // each trace member, read by cs_sv_trace_cost. Answers meters of its own
    // until the composition fills it with the instance it registers
    // (ServerTraceRouting.RouteCost).
    internal SideRoute<ServerTraceCost> TraceCost => _traceCost ??= new(new ServerTraceCost());

    private SideRoute<ServerTraceCost>? _traceCost;

    // THE SERVER'S TRACES ARE MANAGED (plan_server_native_v2 T4f): IEngineTrace::
    // TraceRay through its forwarder over libss_fwd_server's trace filter, which
    // the managed trace rule answers. The composition fills it
    // (ServerTraceRouting.Build); without one the side traces nothing.
    internal SideRoute<IServerTraceRoute> Tracer { get; } = new();

    // A PLAYER'S MOVEMENT STATE IS MANAGED (plan_server_native_v2 T4g): the
    // calls game code makes on it are answered from the command loop's
    // records, which the server's composition fills.
    internal SideRoute<IServerPlayerMovementRoute> PlayerMoves { get; } = new();

    /// <summary>
    /// The server's managed physics init (EntityPhysicsCreation), which
    /// <see cref="EntityInitPhysics"/> and <see cref="EntitySetPhysicsStatic"/>
    /// reach. Filled by the server composition once vphysics answers; empty
    /// makes nothing.
    /// </summary>
    internal SideRoute<IEntityPhysicsRoute> PhysicsInit { get; } = new();

    /// <summary>
    /// Told of every teleport after native has carried it out: the server
    /// composition hands a player's to its shadow (stock's TeleportEntity
    /// moves a player's physics object with them; plan_physics_bones_native_zero
    /// 2.5 made the shadow managed's). Empty tells nobody.
    /// </summary>
    internal SideRoute<Action<EntityHandle, Vector, bool>> Teleported { get; } = new();

    // COLLISION_GROUP_NONE: the group every trace of this side's but the
    // entity sweep asks with, as CTraceFilterSimple( ignore, COLLISION_GROUP_NONE ) did.
    private const int CollisionGroupNone = 0;

    // THE MOVEMENT LOOP'S HULL, THROUGH THE MANAGED FILTER (plan_server_native_v2
    // T4f, T4g): MoveTraceServer, without the two handles the command loop's
    // own world keeps. Without a filter the side refuses: blocked at the
    // start, never "the way is clear".
    private partial void SideMoveTrace(IMovementArea movement, float* start, float* end, float* mins, float* maxs, uint mask,
                                       int collisionGroup, EntityHandle player, MoveTraceBlob* blob)
        => MoveTraceServer((IMovementServerArea)movement, start, end, mins, maxs, mask, collisionGroup, player, blob,
                           out _, out _);

    private partial EntityHandle SideMovePass(int entIndex) => GetPlayerHandle(entIndex);

    /// <summary>
    /// The player's origin, CPlayerState angle words, death flag, health,
    /// scale and index, and the velocity of <see cref="PlayerCommandFacts.Ground"/>.
    /// False when the handle names no player.
    /// </summary>
    internal bool PlayerCommandBegin(IMovementServerArea movement, EntityHandle player, ref PlayerCommandFacts facts)
    {
        facts.StructSize = (uint)sizeof(PlayerCommandFacts);
        fixed (PlayerCommandFacts* p = &facts)
            return movement.PlayerCommandBegin(player.Value, p) != 0;
    }

    /// <summary>pl.v_angle and pl.fixangle, as the command's prepare decided them.</summary>
    internal void PlayerCommandView(IMovementServerArea movement, EntityHandle player, Vector angles, int fixAngle)
    {
        float* a = stackalloc float[3] { angles.X, angles.Y, angles.Z };
        movement.PlayerCommandView(player.Value, a, fixAngle);
    }

    /// <summary>The move's result: the origin, the body's yaw, the trigger sweep, and native's mirrors.</summary>
    internal void PlayerCommandFinish(IMovementServerArea movement, EntityHandle player, in PlayerCommandResult result)
    {
        PlayerCommandResult r = result;
        r.StructSize = (uint)sizeof(PlayerCommandResult);
        movement.PlayerCommandFinish(player.Value, &r);
    }

    /// <summary>The engine side's three words (origin, health, alive) into the player's networked state.</summary>
    internal void PlayerSyncEngineSide(IMovementServerArea movement, EntityHandle player)
    {
        movement.PlayerSyncEngineSide(player.Value);
    }

    /// <summary>A landing's damage through the shell's damage path; whether the player is still up.</summary>
    internal bool PlayerFallDamage(IMovementServerArea movement, EntityHandle player, float damage)
        => movement.PlayerFallDamage(player.Value, damage) != 0;

    /// <summary>
    /// The movement hull, through the managed filter for <paramref name="player"/>'s
    /// query, with the entity it hit and the ground it found as handles
    /// (<see cref="EntityHandle.None"/> for none). Refused, as native refuses,
    /// without a filter.
    /// </summary>
    internal void MoveTraceServer(IMovementServerArea movement, float* start, float* end, float* mins, float* maxs, uint mask, int collisionGroup,
                                  EntityHandle player, MoveTraceBlob* blob, out EntityHandle hit, out EntityHandle ground)
    {
        hit = EntityHandle.None;
        ground = EntityHandle.None;

        if (Tracer.Target is not { } tracer)
        {
            *blob = default;
            blob->StartSolid = 1;
            blob->AllSolid = 1;
            blob->EndPosX = start[0];
            blob->EndPosY = start[1];
            blob->EndPosZ = start[2];
            return;
        }

        uint* handles = stackalloc uint[2];
        nint filter = tracer.Enter(player, collisionGroup);
        try
        {
            movement.MoveTraceServer(start, end, mins, maxs, (int)mask, collisionGroup, blob, filter, handles);
        }
        finally
        {
            tracer.Exit();
        }

        hit = new EntityHandle(handles[0]);
        ground = new EntityHandle(handles[1]);
    }

    /// <summary>
    /// UTIL_TraceEntity: the entity's hull swept from start to end with the
    /// mask and collision group, ignoring the entity (physics stock parity B7).
    /// </summary>
    public EntityHullTrace EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs,
                                           uint mask, int collisionGroup)
    {
        if (Tracer.Target is not { } tracer) return new EntityHullTrace(start, true, true);

        long cost = CostMeter.Start();
        ServerTrace t = tracer.Hull(start, end, mins, maxs, mask, entity, collisionGroup);
        TraceCost.Target!.EntityHull.Stop(cost);
        return ServerTraceResults.EntityHull(t);
    }

    /// <summary>Traces a line through the world.</summary>
    public TraceResult TraceLine(Vector start, Vector end, TraceMask mask,
                                        EntityHandle ignore = default)
    {
        if (Tracer.Target is not { } tracer)
            return new TraceResult(false, 1f, end, Vector.Zero, EntityHandle.None);

        long cost = CostMeter.Start();
        ServerTrace t = tracer.Line(start, end, (uint)TraceMasks.Line(mask), new EntityHandle(Skippable(ignore)),
                                    CollisionGroupNone);
        TraceCost.Target!.Line.Stop(cost);
        return ServerTraceResults.Line(t);
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
    public SurfaceTraceResult TraceLineSurface(Vector start, Vector end, TraceMask mask,
                                                      EntityHandle ignore = default)
    {
        if (Tracer.Target is not { } tracer)
            return new SurfaceTraceResult(false, 1f, end, Vector.Zero, EntityHandle.None, -1, 0);

        long cost = CostMeter.Start();
        ServerTrace t = tracer.Line(start, end, (uint)TraceMasks.Line(mask), new EntityHandle(Skippable(ignore)),
                                    CollisionGroupNone);
        TraceCost.Target!.Line.Stop(cost);
        return ServerTraceResults.Surface(t);
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
    /// <summary>A box swept as a hull, answered as a surface line trace is: where, the face, what, and its surface.</summary>
    public SurfaceTraceResult TraceBox(Vector start, Vector end, Vector mins, Vector maxs,
                                       TraceMask mask, EntityHandle ignore = default)
    {
        if (Tracer.Target is not { } tracer)
            return new SurfaceTraceResult(false, 1f, end, Vector.Zero, EntityHandle.None, -1, 0);

        long cost = CostMeter.Start();
        ServerTrace t = tracer.Hull(start, end, mins, maxs, (uint)TraceMasks.Hull(mask),
                                    new EntityHandle(Skippable(ignore)), CollisionGroupNone);
        TraceCost.Target!.Hull.Stop(cost);
        return ServerTraceResults.Surface(t);
    }

    public HullResult TraceHull(Vector start, Vector end, Vector mins, Vector maxs,
                                       TraceMask mask, EntityHandle ignore = default)
    {
        if (Tracer.Target is not { } tracer)
            return new HullResult(false, false, EntityHandle.None);

        long cost = CostMeter.Start();
        ServerTrace t = tracer.Hull(start, end, mins, maxs, (uint)TraceMasks.Hull(mask),
                                    new EntityHandle(Skippable(ignore)), CollisionGroupNone);
        TraceCost.Target!.Hull.Stop(cost);
        return ServerTraceResults.Hull(t);
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
        if (Tracer.Target is not { } tracer)
            return new WaterBelow(false, false, false, start);

        long cost = CostMeter.Start();
        WaterBelow below = SourceSharp.Server.Interop.WaterQuery.Below(tracer, start);
        TraceCost.Target!.Water.Stop(cost);
        return below;
    }
}
