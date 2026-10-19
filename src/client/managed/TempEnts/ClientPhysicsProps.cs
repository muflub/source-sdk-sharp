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
}

/// <summary>The real one: a HIGH-priority slot in the pool, so a gib burst cannot evict a prop.</summary>
public sealed class PoolPhysicsPropSlots(ClientTempEntPool pool) : IPhysicsPropSlots
{
    public int Spawn(Vector origin, int modelIndex, in TempEntSpawn spawn)
        => pool.Spawn(origin, modelIndex, TempEntPriority.High, spawn);
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
/// Not carried, each with nothing in this build to act on it: the object's
/// game data and collision group (it collides as an ownerless object, as the
/// client solver answers; its impact sounds play through
/// <see cref="Owns"/>, and it makes no scrape sound, which FrictionSounds keys
/// by entity),
/// CALLBACK_GLOBAL_TOUCH_STATIC and the respawn zones (the client routes no
/// contact touch), <c>m_fEffects</c> (a temp entity carries none), a piece's
/// motion-disabled flag, the skin-family clamp and the per-frame and total
/// piece limits (props_break_max_pieces*, -1 by default).
/// </para>
/// </summary>
public sealed class ClientPhysicsProps(IRagdollVPhysics vphysics, IPhysicsPropSlots slots, IClientModelNames models,
                                       BreakModels breaks, IGameClock clock, Func<int> maxClients, IRandomSeeds seeds,
                                       ILog log) : ITempEntDriver
{
    /// <summary><c>cl_phys_props_max</c>'s default.</summary>
    public const int MaxProps = 300;

    /// <summary>physpropclientside.cpp's <c>FADEOUT_TIME</c>.</summary>
    public const float FadeOutTime = 1f;

    /// <summary><c>params.defBurstScale = 100</c>, C_PhysPropClientside::Break.</summary>
    public const float DefaultBurstScale = 100f;

    /// <summary><c>g_PhysDefaultObjectParams.rotInertiaLimit</c> (physics_shared.cpp).</summary>
    public const float DefaultRotInertiaLimit = 0.05f;

    /// <summary>MULTIPLAYER_BREAK_SERVERSIDE (props_shared.h).</summary>
    public const int BreakServerSide = 1;

    private sealed record Prop(nint Object, nint Environment);

    private readonly Dictionary<int, Prop> _bySlot = new();
    private readonly HashSet<nint> _objects = new();
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

    /// <summary>Whether <paramref name="physicsObject"/> is a clientside prop's (for its impact sounds).</summary>
    public bool Owns(nint physicsObject) => physicsObject != 0 && _objects.Contains(physicsObject);

    /// <summary>The physics object behind a slot, or 0.</summary>
    public nint ObjectOf(int slot) => _bySlot.TryGetValue(slot, out Prop? p) ? p.Object : 0;

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
            Break(model, skin, origin, angles, velocity, Vector.Zero);
            return;
        }

        Create(modelIndex, model, skin, origin, angles, velocity, Vector.Zero, fadeTime: 0f);
    }

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
                      Vector angularVelocity, float fadeTime)
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

        int surface = string.IsNullOrEmpty(solid.SurfaceProp) ? -1 : vphysics.SurfaceIndex(solid.SurfaceProp);
        nint obj = vphysics.CreatePolyObject(environment, vphysics.CollideSolid(collide, solid.Index), surface, solid,
                                             new RagdollObjectOverrides(null, DefaultRotInertiaLimit, 0, 0));
        vphysics.ReleaseSolid(solid);

        if (obj == 0)
        {
            log.Msg($" C_PhysPropClientside::Initialize: VPhysicsInitNormal() failed for {model}.\n");
            return -1;
        }

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
            return -1;
        }

        _bySlot[slot] = new Prop(obj, environment);
        _objects.Add(obj);
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
    public int Break(string model, int skin, Vector origin, QAngle angles, Vector velocity, Vector angularVelocity)
    {
        // The pieces' collision group is read by nothing here (see the class
        // notes); stock's default would be the prop's PUSHAWAY.
        const CollisionGroup defaultGroup = CollisionGroup.Interactive;
        Transform frame = Transform.FromOriginAngles(origin, angles);

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

            Vector position = frame.TransformPoint(piece.Offset);

            // BreakModelCreateSingle: health, fade, then the velocity randomised by 5%.
            if (piece.IsRagdoll != 0)
                log.Msg("BreakModelCreateSingle: clientside doesn't support ragdoll breakmodels.\n");

            float spread = _random.Float(-0.025f, 0.025f);
            int slot = Create(pieceIndex, pieceModel, skin, position, angles, velocity + velocity * spread,
                              angularVelocity, piece.FadeTime);
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

        _objects.Remove(prop.Object);

        // The level's environment destroys its objects with itself; one freed
        // after it went is not destroyed twice.
        if (prop.Environment != 0 && vphysics.Environment() == prop.Environment)
            vphysics.DestroyObject(prop.Environment, prop.Object);
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
