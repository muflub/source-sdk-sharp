//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// A temp entity slot whose transform something other than the ballistic
/// step owns. <see cref="ClientTempEntFrame"/> asks before it integrates.
/// </summary>
public interface ITempEntDriver
{
    /// <summary>Writes the slot's place and facing, and answers true, when this driver owns the slot.</summary>
    bool Drive(int slot, ref TempEntTransform transform);

    /// <summary>The slot was freed: whatever the driver held for it goes.</summary>
    void Forget(int slot);
}

/// <summary>Where a clientside physics prop is drawn: a temp entity slot of its model.</summary>
public interface IPhysicsPropSlots
{
    /// <summary>A slot drawing <paramref name="modelIndex"/>, or -1.</summary>
    int Spawn(Vector origin, int modelIndex, in TempEntSpawn spawn);

    /// <summary>
    /// Takes back a slot a reload carried: its temp entity is still alive
    /// natively, and the pool must count it live again. False when the slot is
    /// not free to take.
    /// </summary>
    bool Adopt(int slot);
}

/// <summary>
/// What the client's physics callbacks ask about an object's owner when the
/// owner is a clientside physics prop: stock's game data was the
/// C_PhysPropClientside itself, so its collision group and its transform
/// answered for it.
/// </summary>
public interface IClientPhysicsPropOwners
{
    /// <summary>Whether <paramref name="owner"/> names a live clientside prop.</summary>
    bool IsProp(EntityHandle owner);

    /// <summary>The prop's collision group, when <paramref name="owner"/> is one.</summary>
    bool TryGroup(EntityHandle owner, out EngineCollisionGroup group);

    /// <summary>The prop's place and facing (its object's), when <paramref name="owner"/> is one.</summary>
    bool TryTransform(EntityHandle owner, out Vector origin, out QAngle angles);
}

/// <summary>
/// props_shared.cpp's two piece limits and the frame they count in:
/// <c>props_break_max_pieces</c>, <c>props_break_max_pieces_perframe</c>
/// (-1 is "the model's own count") and <c>gpGlobals-&gt;framecount</c>.
/// </summary>
public interface IPropBreakLimits
{
    int MaxPieces { get; }

    int MaxPiecesPerFrame { get; }

    int FrameCount { get; }
}

/// <summary>No limits: both -1, as the convars' defaults.</summary>
public sealed class NoPropBreakLimits : IPropBreakLimits
{
    public int MaxPieces => -1;

    public int MaxPiecesPerFrame => -1;

    public int FrameCount => 0;
}

/// <summary>The real one: the client's two convars and the engine's frame count.</summary>
public sealed class PropBreakLimits(PropsBreakMaxPiecesConVar max, PropsBreakMaxPiecesPerFrameConVar perFrame,
                                    IEngineEnvironment environment) : IPropBreakLimits
{
    public int MaxPieces => max.Value;

    public int MaxPiecesPerFrame => perFrame.Value;

    public int FrameCount => environment.Globals.IsAvailable ? environment.Globals.FrameCount : 0;
}

/// <summary>
/// C_PhysPropClientside::HitSurface's two questions: whether the prop's model
/// asks for a blood splat on a world impact (PROPINTER_WORLD_BLOODSPLAT), and
/// the splat itself, UTIL_BloodDecalTrace's red arm at the touch.
/// </summary>
public interface IClientPropHitSurfaces
{
    bool BloodSplats(string model);

    void BloodDecal(EntityHandle other, Vector start, Vector end);
}

/// <summary>The real one: a HIGH-priority slot in the pool, so a gib burst cannot evict a prop.</summary>
public sealed class PoolPhysicsPropSlots(ClientTempEntPool pool) : IPhysicsPropSlots
{
    public int Spawn(Vector origin, int modelIndex, in TempEntSpawn spawn)
        => pool.Spawn(origin, modelIndex, TempEntPriority.High, spawn);

    public bool Adopt(int slot) => pool.Adopt(slot);
}

/// <summary>
/// Stock's client physics props made by a temp entity: <c>CTempEnts::PhysicsProp</c>
/// (c_te_legacytempents.cpp) and the <c>C_PhysPropClientside</c> it builds
/// (physpropclientside.cpp): <c>CreateNew</c>'s cap, <c>Initialize</c>'s
/// object from the model's last solid, the velocity added to it, the skin,
/// <c>StartFadeOut</c>'s death time, and <c>Break</c> with the client half of
/// props_shared.cpp's <c>PropBreakableCreateAll</c> and
/// <c>BreakModelCreateSingle</c>.
///
/// <para>
/// The prop is drawn as a temp entity of its model (FTENT_PERSIST, so the
/// PVS cull does not free it) whose transform is read off its vphysics object
/// every frame, in place of the ballistic step. A prop that never fades never
/// dies, as stock's <c>m_fDeathTime = -1</c>; a fading one is the pool's
/// FTENT_FADEOUT ramp, which over FadeSpeed 1 is stock's FADEOUT_TIME second.
/// </para>
///
/// <para>
/// THE EFFECTS. CTempEnts::PhysicsProp's <c>SetEffects( physEffects )</c> is
/// kept on the prop, and a broken piece inherits its owner's, as
/// BreakModelCreateSingle copies them (which carries PropBreakableCreateAll's
/// EF_NOSHADOW with them). The slot that draws a prop is a temp entity, which
/// has no <c>m_fEffects</c>: its ShouldDraw's one off switch is its render
/// mode, so EF_NODRAW is drawn as kRenderNone. The rest have no subject on a
/// temp entity: it casts no shadow for EF_NOSHADOW to turn off, and nothing
/// adds its lights (EF_BRIGHTLIGHT / EF_DIMLIGHT, C_BaseEntity::AddEntity).
/// </para>
///
/// <para>
/// THE OWNER. Stock's object carried the C_PhysPropClientside as its game
/// data, an entity the client entity list held in its non-networkable range
/// (AddNonNetworkableEntity). Here the prop is no entity, so it names itself
/// with a handle of its own in that range (<see cref="ClientPhysicsPropHandles"/>)
/// and its object carries that handle's tagged word: the impact sounds, the
/// solver, the splash and the scrape see an owned object, and ask this class
/// (<see cref="IClientPhysicsPropOwners"/>) what the entity would have
/// answered. Its group is Initialize's COLLISION_GROUP_PUSHAWAY, and a piece
/// with no health BreakModelCreateSingle's COLLISION_GROUP_NONE.
/// </para>
///
/// <para>
/// Not carried, each with nothing in this build to act on it:
/// the respawn zones, a piece's
/// motion-disabled flag and the skin-family clamp.
/// </para>
///
/// <para>
/// THE TOUCH. Initialize's "we want touch calls when we hit the world":
/// the object's callback flags gain CALLBACK_GLOBAL_TOUCH_STATIC, so
/// vphysics reports its contacts with static objects.
/// </para>
///
/// <para>
/// StartTouch is stock's: at most one HitSurface every 0.1 s per prop
/// (m_flTouchDelta), and HitSurface's blood splat on what it touched when the
/// model's prop data asks for one (<see cref="HitSurfaces"/>).
/// </para>
///
/// <para>
/// THE RELOAD. A client hot reload keeps the level, its environment and the
/// native temp entities, so a prop's object and slot outlive the build that
/// made them: the props go across (an <see cref="IReloadParticipant"/>), each
/// slot taken back from the new pool, each handle from the new allocator.
/// </para>
///
/// <para>
/// THE PIECE LIMITS are PropBreakableCreateAll's: a break in a frame that has
/// already made <c>props_break_max_pieces_perframe</c> pieces makes none, the
/// walk stops once the frame's count passes it, and no piece at or past
/// <c>props_break_max_pieces</c> in the list is made. A temp entity's break
/// does not ignore the gib limit and precomputes no count.
/// </para>
/// </summary>
public sealed class ClientPhysicsProps(IRagdollVPhysics vphysics, IPhysicsPropSlots slots, IClientModelNames models,
                                       BreakModels breaks, IGameClock clock, Func<int> maxClients, IRandomSeeds seeds,
                                       ILog log, IPropBreakLimits? limits = null)
    : ITempEntDriver, IClientPhysicsPropOwners, IReloadParticipant, Physics.IClientTouchReceiver
{
    /// <summary>HitSurface's blood splat; none without it. Set by the composition.</summary>
    public IClientPropHitSurfaces? HitSurfaces { get; set; }

    /// <summary>StartTouch calls a prop has taken, and HitSurface calls past the 0.1 s limit.</summary>
    public long Touches { get; private set; }

    /// <inheritdoc cref="Touches"/>
    public long SurfaceHits { get; private set; }

    /// <summary>Of <see cref="Touches"/>, those whose toucher was the world.</summary>
    public long WorldTouches { get; private set; }

    /// <summary>Blood splats HitSurface made.</summary>
    public long BloodSplats { get; private set; }

    private readonly IPropBreakLimits _limits = limits ?? new NoPropBreakLimits();

    // PropBreakableCreateAll's nPropBreakablesPerFrameCount and nFrameNumber.
    private int _piecesThisFrame;
    private int _pieceFrame;

    /// <summary><c>cl_phys_props_max</c>'s default.</summary>
    public const int MaxProps = 300;

    /// <summary>physpropclientside.cpp's <c>FADEOUT_TIME</c>.</summary>
    public const float FadeOutTime = 1f;

    /// <summary><c>params.defBurstScale = 100</c>, C_PhysPropClientside::Break.</summary>
    public const float DefaultBurstScale = 100f;

    /// <summary><c>g_PhysDefaultObjectParams.rotInertiaLimit</c> (physics_shared.cpp).</summary>
    public const float DefaultRotInertiaLimit = 0.05f;

    /// <summary>EF_NODRAW (const.h).</summary>
    public const int EfNoDraw = 0x020;

    /// <summary>kRenderNone (const.h's RenderMode_t).</summary>
    public const int RenderNone = 10;

    /// <summary>CALLBACK_GLOBAL_TOUCH_STATIC (vphysics_interface.h).</summary>
    public const ushort CallbackGlobalTouchStatic = 0x0008;

    /// <summary>MULTIPLAYER_BREAK_SERVERSIDE (props_shared.h).</summary>
    public const int BreakServerSide = 1;

    private sealed record Prop(nint Object, nint Environment, EntityHandle Handle, EngineCollisionGroup Group, string Model)
    {
        /// <summary>m_flTouchDelta: the curtime before which a touch hits no surface.</summary>
        public float TouchDelta { get; set; }
    }

    private readonly Dictionary<int, Prop> _bySlot = new();
    private readonly Dictionary<EntityHandle, int> _slotByHandle = new();
    private readonly ClientPhysicsPropHandles _handles = new();
    private readonly TempEntRandom _random = new(seeds.Next());

    /// <summary>How many clientside props are alive (stock's <c>s_PhysPropList.Count()</c>).</summary>
    public int Count => _bySlot.Count;

    /// <summary>The slots alive, for a test or a probe.</summary>
    public IEnumerable<int> Slots => _bySlot.Keys;

    /// <summary>Each prop's slot, where its object is and whether it sleeps, for <c>cs_cl_physprops</c>.</summary>
    public IEnumerable<(int Slot, Vector Origin, bool Asleep)> Report()
    {
        foreach ((int slot, Prop prop) in _bySlot)
            yield return (slot, vphysics.PositionAngles(prop.Object).Position, vphysics.IsAsleep(prop.Object));
    }

    /// <summary>The physics object behind a slot, or 0.</summary>
    public nint ObjectOf(int slot) => _bySlot.TryGetValue(slot, out Prop? p) ? p.Object : 0;

    /// <summary>The handle a slot's prop names itself by, or <see cref="EntityHandle.None"/>.</summary>
    public EntityHandle HandleOf(int slot) => _bySlot.TryGetValue(slot, out Prop? p) ? p.Handle : EntityHandle.None;

    /// <inheritdoc/>
    public bool IsProp(EntityHandle owner) => owner != EntityHandle.None && _slotByHandle.ContainsKey(owner);

    /// <inheritdoc/>
    public bool TryGroup(EntityHandle owner, out EngineCollisionGroup group)
    {
        if (PropOf(owner) is { } prop)
        {
            group = prop.Group;
            return true;
        }

        group = EngineCollisionGroup.None;
        return false;
    }

    /// <inheritdoc/>
    public bool TryTransform(EntityHandle owner, out Vector origin, out QAngle angles)
    {
        if (PropOf(owner) is { } prop)
        {
            (origin, angles) = vphysics.PositionAngles(prop.Object);
            return true;
        }

        origin = Vector.Zero;
        angles = QAngle.Zero;
        return false;
    }

    /// <inheritdoc/>
    public bool Receives(EntityHandle entity) => IsProp(entity);

    /// <summary>
    /// C_PhysPropClientside::StartTouch: "Limit the amount of times we can
    /// bounce" -- HitSurface at most once every 0.1 s.
    /// </summary>
    public void StartTouch(EntityHandle entity, EntityHandle other, Vector point, Vector normal)
    {
        if (PropOf(entity) is not { } prop)
            return;

        Touches++;
        if (other == World)
            WorldTouches++;

        float now = clock.Now;
        if (prop.TouchDelta < now)
        {
            HitSurface(prop, other, point);
            prop.TouchDelta = now + 0.1f;
        }
    }

    /// <inheritdoc/>
    public void EndTouch(EntityHandle entity, EntityHandle other)
    {
    }

    // C_PhysPropClientside::HitSurface: the touch trace's entity is the other
    // side; its start is the memset trace's origin, its end the contact.
    private void HitSurface(Prop prop, EntityHandle other, Vector point)
    {
        SurfaceHits++;
        if (HitSurfaces is not { } hits || !hits.BloodSplats(prop.Model))
            return;

        hits.BloodDecal(other, Vector.Zero, point);
        BloodSplats++;
    }

    /// <summary>The world's handle: entry 0, serial 0.</summary>
    public static EntityHandle World => new(0);

    /// <summary>
    /// The entity a scrape's sound is made on (FrictionSounds' sound source):
    /// stock's client PhysFrictionSound -- "clientside created entites doesn't
    /// have a valid entindex, let 'world' play the sound for them" -- so a
    /// prop's is the world's, and anyone else's their own.
    /// </summary>
    public EntityHandle SoundSource(EntityHandle owner) => IsProp(owner) ? World : owner;

    private Prop? PropOf(EntityHandle owner)
        => owner != EntityHandle.None && _slotByHandle.TryGetValue(owner, out int slot) ? _bySlot[slot] : null;

    /// <summary>
    /// <c>CTempEnts::PhysicsProp( modelindex, skin, pos, angles, vel, physFlags, physEffects )</c>.
    /// Bit 0 of <paramref name="flags"/> is the break model: the prop is made
    /// and at once broken, so only its pieces are seen.
    /// </summary>
    public void PhysicsProp(int modelIndex, int skin, Vector origin, QAngle angles, Vector velocity, int flags, int effects)
    {
        string? model = models.NameOf(modelIndex);
        if (model is null)
        {
            log.Msg($"CTempEnts::PhysicsProp: model index {modelIndex} not found\n");
            return;
        }

        if (!MayCreate())
            return;

        if ((flags & 1) != 0)
        {
            // Spawn(), SetHealth( 0 ), Break(): Initialize was never called, so
            // the break has no physics object and reads the entity's own pose
            // and velocity, with no angular velocity.
            Break(model, skin, origin, angles, velocity, Vector.Zero, effects);
            return;
        }

        Create(modelIndex, model, skin, origin, angles, velocity, Vector.Zero, fadeTime: 0f, effects: effects);
    }

    /// <summary>
    /// The prop's collision group: Initialize's COLLISION_GROUP_PUSHAWAY ("player
    /// can push it away"), and for a broken piece BreakModelCreateSingle's
    /// COLLISION_GROUP_NONE when it has no health ("don't collide with player
    /// anymore").
    /// </summary>
    public static EngineCollisionGroup GroupFor(bool piece, float health)
        => piece && health == 0f ? EngineCollisionGroup.None : EngineCollisionGroup.PushAway;

    /// <summary>C_PhysPropClientside::CreateNew's cap.</summary>
    private bool MayCreate()
    {
        if (_bySlot.Count < MaxProps)
            return true;

        log.Msg($"Warning! Client physic props overflow *max {MaxProps}).\n");
        return false;
    }

    /// <summary>
    /// CreateNew, Initialize, the object's velocity and StartFadeOut: the slot,
    /// or -1 when any step refused (stock's Release on the way out).
    /// </summary>
    public int Create(int modelIndex, string model, int skin, Vector origin, QAngle angles, Vector velocity,
                      Vector angularVelocity, float fadeTime,
                      EngineCollisionGroup group = EngineCollisionGroup.PushAway, int effects = 0)
    {
        if (!MayCreate())
            return -1;

        nint environment = vphysics.Environment();
        if (environment == 0)
            return -1;

        if (!ParseSolid(modelIndex, out RagdollCollide collide, out RagdollSolid solid))
        {
            log.Msg($"C_PhysPropClientside::Initialize: PhysModelParseSolid failed for entity {modelIndex}.\n");
            return -1;
        }

        // The object's game data is the prop (VPhysicsInitNormal's `this`).
        EntityHandle handle = _handles.Take();
        int surface = string.IsNullOrEmpty(solid.SurfaceProp) ? -1 : vphysics.SurfaceIndex(solid.SurfaceProp);
        nint obj = vphysics.CreatePolyObject(environment, vphysics.CollideSolid(collide, solid.Index), surface, solid,
                                             new RagdollObjectOverrides(null, DefaultRotInertiaLimit,
                                                                        PhysicsGameData.FromHandle(handle), 0));
        vphysics.ReleaseSolid(solid);

        if (obj == 0)
        {
            _handles.Give(handle);
            log.Msg($" C_PhysPropClientside::Initialize: VPhysicsInitNormal() failed for {model}.\n");
            return -1;
        }

        // We want touch calls when we hit the world.
        vphysics.SetCallbackFlags(obj, (ushort)(vphysics.CallbackFlags(obj) | CallbackGlobalTouchStatic));

        // PhysModelParseSolid: collisions on; VPhysicsInitNormal: placed and woken.
        vphysics.EnableObjectCollisions(obj, true);
        vphysics.SetPositionMatrix(obj, Matrix(origin, angles), true);
        vphysics.Wake(obj);
        vphysics.AddVelocity(obj, velocity, angularVelocity);

        float now = clock.Now;
        TempEntSpawn spawn = TempEntSpawn.Prepared(now);
        spawn.Skin = skin;
        spawn.Angles = new Vector(angles.Pitch, angles.Yaw, angles.Roll);
        spawn.RenderAmt = 255;
        if ((effects & EfNoDraw) != 0)
            spawn.RenderMode = RenderNone;
        if (fadeTime > 0f)
        {
            // StartFadeOut: dead at now + delay + FADEOUT_TIME, fading over the last second.
            spawn.Flags = (int)(TempEntFlags.Persist | TempEntFlags.FadeOut);
            spawn.Die = now + fadeTime;
            spawn.FadeSpeed = 1f / FadeOutTime;
        }
        else
        {
            // m_fDeathTime = -1: never.
            spawn.Flags = (int)TempEntFlags.Persist;
            spawn.Die = float.MaxValue;
        }

        int slot = slots.Spawn(origin, modelIndex, spawn);
        if (slot < 0)
        {
            vphysics.DestroyObject(environment, obj);
            _handles.Give(handle);
            return -1;
        }

        _bySlot[slot] = new Prop(obj, environment, handle, group, model);
        _slotByHandle[handle] = slot;
        return slot;
    }

    /// <summary>
    /// PhysModelParseSolid: the LAST "solid" block of the model's collision
    /// text (PhysModelParseSolidByIndex with index -1 keeps overwriting).
    /// </summary>
    private bool ParseSolid(int modelIndex, out RagdollCollide collide, out RagdollSolid solid)
    {
        solid = default;
        collide = vphysics.Collide(modelIndex);
        if (collide.Pointer == 0 || collide.SolidCount <= 0)
            return false;

        nint parser = vphysics.KeyParserCreate(collide.KeyValues);
        bool parsed = false;
        try
        {
            while (!vphysics.Finished(parser))
            {
                if (string.Equals(vphysics.BlockName(parser), "solid", StringComparison.OrdinalIgnoreCase))
                {
                    RagdollSolid next = vphysics.ParseSolid(parser);
                    if (parsed)
                        vphysics.ReleaseSolid(solid);
                    solid = next;
                    parsed = true;
                }
                else
                {
                    vphysics.SkipBlock(parser);
                }
            }
        }
        finally
        {
            vphysics.KeyParserDestroy(parser);
        }

        if (parsed && (solid.Index < 0 || solid.Index >= collide.SolidCount))
        {
            vphysics.ReleaseSolid(solid);
            return false;
        }

        return parsed;
    }

    /// <summary>
    /// C_PhysPropClientside::Break, then PropBreakableCreateAll's client half
    /// over the model's break list, each piece through BreakModelCreateSingle.
    /// Answers how many pieces were made.
    /// </summary>
    public int Break(string model, int skin, Vector origin, QAngle angles, Vector velocity, Vector angularVelocity,
                     int effects = 0)
    {
        // params.defCollisionGroup lands in each breakmodel_t, which the
        // client's BreakModelCreateSingle never reads: the piece's group is
        // Initialize's, and NONE for a piece with no health (GroupFor).
        const CollisionGroup defaultGroup = CollisionGroup.Interactive;
        Transform frame = Transform.FromOriginAngles(origin, angles);

        // Check for prop breakable count reset.
        int perFrame = _limits.MaxPiecesPerFrame;
        if (perFrame != -1)
        {
            int frameCount = _limits.FrameCount;
            if (_pieceFrame != frameCount)
            {
                _piecesThisFrame = 0;
                _pieceFrame = frameCount;
            }

            // Check for max breakable count for the frame.
            if (_piecesThisFrame >= perFrame)
                return 0;
        }

        // The temp entity's break ignores no gib limit and precomputes no count.
        int precomputed = _limits.MaxPieces;

        int made = 0;
        int count = breaks.Count(model);
        for (int i = 0; i < count; i++)
        {
            if (!breaks.PieceAt(model, i, DefaultBurstScale, defaultGroup, out string pieceModel, out BreakPiece piece))
                continue;

            int pieceIndex = models.IndexOf(pieceModel);
            if (pieceIndex <= 0)
                continue;

            // Skip multiplayer pieces that should be spawning on the other dll.
            // (The client's defaultLocation is true here, so DEFAULT pieces stay.)
            if (maxClients() > 1 && piece.MultiplayerBreakMode == BreakServerSide)
                continue;

            if (perFrame != -1 && _piecesThisFrame > perFrame)
                break;

            if (precomputed != -1 && i >= precomputed)
                break;

            // Increment the number of breakable props this frame.
            ++_piecesThisFrame;

            Vector position = frame.TransformPoint(piece.Offset);

            // BreakModelCreateSingle: health, fade, then the velocity randomised by 5%.
            if (piece.IsRagdoll != 0)
                log.Msg("BreakModelCreateSingle: clientside doesn't support ragdoll breakmodels.\n");

            float spread = _random.Float(-0.025f, 0.025f);
            int slot = Create(pieceIndex, pieceModel, skin, position, angles, velocity + velocity * spread,
                              angularVelocity, piece.FadeTime, GroupFor(piece: true, piece.Health), effects);
            if (slot < 0)
                continue;

            made++;

            // The burst away from the break; from the piece's centre when its
            // origin is the break's own.
            if (piece.BurstScale != 0f)
            {
                Vector away = position - origin;
                if (away.Equals(Vector.Zero) && vphysics.ModelBounds(pieceIndex, out Vector mins, out Vector maxs))
                    away = Transform.FromOriginAngles(position, angles).TransformPoint((mins + maxs) * 0.5f) - origin;

                float length = away.Length;
                if (length > 0f)
                    vphysics.AddVelocity(_bySlot[slot].Object, away * (piece.BurstScale / length), Vector.Zero);
            }
        }

        return made;
    }

    /// <inheritdoc/>
    public bool Drive(int slot, ref TempEntTransform transform)
    {
        if (!_bySlot.TryGetValue(slot, out Prop? prop))
            return false;

        (Vector position, QAngle angles) = vphysics.PositionAngles(prop.Object);
        transform.Origin = position;
        transform.Angles = angles;
        transform.Velocity = Vector.Zero;
        return true;
    }

    /// <inheritdoc/>
    public void Forget(int slot)
    {
        if (!_bySlot.Remove(slot, out Prop? prop))
            return;

        _slotByHandle.Remove(prop.Handle);
        _handles.Give(prop.Handle);

        // The level's environment destroys its objects with itself; one freed
        // after it went is not destroyed twice.
        if (prop.Environment != 0 && vphysics.Environment() == prop.Environment)
            vphysics.DestroyObject(prop.Environment, prop.Object);
    }

    //-------------------------------------------------------------------------
    // The reload: the objects stay in the environment and the slots in the
    // native pool, so the table goes across, pointer for pointer.
    //-------------------------------------------------------------------------

    private static void Write(ReloadWriter w, nint pointer)
    {
        long value = pointer;
        w.Write(unchecked((int)value));
        w.Write(unchecked((int)(value >> 32)));
    }

    private static nint ReadPointer(ReloadReader r)
    {
        uint lo = unchecked((uint)r.ReadInt());
        long hi = r.ReadInt();
        return (nint)((hi << 32) | lo);
    }

    /// <inheritdoc/>
    public void Save(ReloadWriter writer)
    {
        writer.Write(_piecesThisFrame);
        writer.Write(_pieceFrame);
        writer.Write(_bySlot.Count);
        foreach ((int slot, Prop prop) in _bySlot)
        {
            writer.Write(slot);
            Write(writer, prop.Object);
            Write(writer, prop.Environment);
            writer.Write(unchecked((int)prop.Handle.Value));
            writer.Write((int)prop.Group);
            writer.Write(prop.Model);
            writer.Write(prop.TouchDelta);
        }
    }

    /// <inheritdoc/>
    public void Restore(ReloadReader reader)
    {
        _piecesThisFrame = reader.ReadInt();
        _pieceFrame = reader.ReadInt();
        int count = reader.ReadInt();
        var carried = new List<(int Slot, Prop Prop)>(count);
        for (int i = 0; i < count; i++)
        {
            int slot = reader.ReadInt();
            nint obj = ReadPointer(reader);
            nint environment = ReadPointer(reader);
            var handle = new EntityHandle(unchecked((uint)reader.ReadInt()));
            var group = (EngineCollisionGroup)reader.ReadInt();
            string model = reader.ReadString();
            float touchDelta = reader.ReadFloat();
            carried.Add((slot, new Prop(obj, environment, handle, group, model) { TouchDelta = touchDelta }));
        }

        foreach ((int slot, Prop prop) in carried)
        {
            // A prop whose slot the new pool cannot take back is dropped, its
            // object with it: nothing would draw or free it.
            if (!slots.Adopt(slot) || !_handles.Adopt(prop.Handle))
            {
                log.Msg($"[SourceSharp/Client] reload: clientside prop slot {slot} could not be taken back\n");
                if (prop.Environment != 0 && vphysics.Environment() == prop.Environment)
                    vphysics.DestroyObject(prop.Environment, prop.Object);
                continue;
            }

            _bySlot[slot] = prop;
            _slotByHandle[prop.Handle] = slot;
        }
    }

    private static Float3x4 Matrix(Vector origin, QAngle angles)
    {
        Transform t = Transform.FromOriginAngles(origin, angles);
        Float3x4 m = default;
        m[0, 0] = t.M00; m[0, 1] = t.M01; m[0, 2] = t.M02; m[0, 3] = t.M03;
        m[1, 0] = t.M10; m[1, 1] = t.M11; m[1, 2] = t.M12; m[1, 3] = t.M13;
        m[2, 0] = t.M20; m[2, 1] = t.M21; m[2, 2] = t.M22; m[2, 3] = t.M23;
        return m;
    }
}

/// <summary>
/// The handles clientside props name themselves by: stock's
/// AddNonNetworkableEntity range, entries from MAX_EDICTS up, each with a
/// serial that steps when the entry is handed out again, so a stale handle
/// names nobody. Nothing else on this client binds that range (the slot
/// table holds the networked entries only), so a native lookup of one of
/// these finds nothing and fails closed.
/// </summary>
public sealed class ClientPhysicsPropHandles
{
    /// <summary>NUM_SERIAL_NUM_SHIFT_BITS: the serial sits above the 16-bit entry.</summary>
    private const int SerialShift = 16;

    /// <summary>The serial's mask; the top serial bit is the static props'.</summary>
    private const uint SerialMask = 0x7FFF;

    private readonly Stack<int> _free = new();
    private readonly ushort[] _serial = new ushort[EntityHandle.EntryCount];
    private int _next = EntityHandle.FirstInvisibleEntry;

    /// <summary>A fresh handle, or <see cref="EntityHandle.None"/> when the range is spent.</summary>
    public EntityHandle Take()
    {
        int entry;
        if (_free.Count > 0)
            entry = _free.Pop();
        else if (_next < EntityHandle.EntryCount - 1)
            entry = _next++;
        else
            return EntityHandle.None;

        return new EntityHandle((uint)entry | ((uint)_serial[entry] << SerialShift));
    }

    /// <summary>
    /// Takes a carried handle's entry, with the serial it carries (a reload's
    /// restore, before anything new is handed out). False when the entry is
    /// already out.
    /// </summary>
    public bool Adopt(EntityHandle handle)
    {
        if (handle == EntityHandle.None || !handle.IsInvisibleRange)
            return false;

        int entry = handle.EntryIndex;
        if (entry >= _next)
        {
            for (int skipped = _next; skipped < entry; skipped++)
                _free.Push(skipped);
            _next = entry + 1;
        }
        else if (_free.Contains(entry))
        {
            int[] rest = _free.Where(e => e != entry).Reverse().ToArray();
            _free.Clear();
            foreach (int e in rest)
                _free.Push(e);
        }
        else
        {
            return false;
        }

        _serial[entry] = (ushort)((handle.Value >> SerialShift) & SerialMask);
        return true;
    }

    /// <summary>Gives a handle's entry back, its serial stepped.</summary>
    public void Give(EntityHandle handle)
    {
        if (handle == EntityHandle.None || !handle.IsInvisibleRange)
            return;

        int entry = handle.EntryIndex;
        _serial[entry] = (ushort)((_serial[entry] + 1) & SerialMask);
        _free.Push(entry);
    }
}
