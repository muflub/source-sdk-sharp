using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// What the solver's answers read of an entity: its engine block's collision
/// fields, whether it is a player, what moves it, and the contents it
/// collides with (stock's PhysicsSolidMaskForEntity).
/// </summary>
public readonly record struct SolverEntity(int CollisionGroup, int Solid, int SolidFlags, int MoveType,
                                           bool IsPlayer, EntityHandle Owner, EntityHandle Parent,
                                           uint SolidMask = EnginePushWorld.MaskSolid);

/// <summary>
/// The engine facts managed's <c>IPhysicsCollisionSolver</c> answers from
/// (plan_physics_bones_native_zero 2.1). Reads only: it decides nothing.
/// </summary>
public interface IPhysicsSolverWorld
{
    /// <summary>The level's world physics object (native's g_PhysWorldObject), or 0 between levels.</summary>
    nint WorldObject();

    /// <summary>The entity's facts, or false when the handle names nothing live.</summary>
    bool TryEntity(EntityHandle entity, out SolverEntity facts);

    /// <summary>The entity's own physics object, or 0.</summary>
    nint PhysicsObjectOf(EntityHandle entity);
}

/// <summary>
/// <see cref="IPhysicsSolverWorld"/> answered by the engine: the entity's
/// engine block read in place, IsPlayer, and EntityGetState's move type (the
/// player's own, or MOVETYPE_VPHYSICS for an entity with a physics object, as
/// native's StandaloneMoveType_Report has it), except that a class which
/// moves as a pusher answers MOVETYPE_PUSH, as stock's movers are: an "AI
/// mover" whose shadow passes through the world (ShouldCollide).
/// </summary>
public sealed class EnginePhysicsSolverWorld(IEngineEntities entities, IEngineNetworking net,
                                             IEngineEntityPhysics physics, Func<EntityHandle, bool>? movesAsPusher = null,
                                             Func<EntityHandle, uint?>? solidMask = null)
    : IPhysicsSolverWorld
{
    private static readonly int SolidOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidType));
    private static readonly int SolidFlagsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidFlags));
    private static readonly int GroupOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.CollisionGroup));
    private static readonly int ParentOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.ParentHandle));
    private static readonly int OwnerOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.OwnerHandle));

    public nint WorldObject() => physics.WorldObject();

    public bool TryEntity(EntityHandle entity, out SolverEntity facts)
    {
        Span<byte> block = entities.GetEngineBlock(entity);
        if (block.Length < EngineBlock.SizeBytes)
        {
            facts = default;
            return false;
        }

        net.EntityGetState(entity, out MoveType moveType);
        if (movesAsPusher?.Invoke(entity) == true)
            moveType = MoveType.Push;
        facts = new SolverEntity(
            MemoryMarshal.Read<int>(block.Slice(GroupOffset)),
            MemoryMarshal.Read<int>(block.Slice(SolidOffset)),
            MemoryMarshal.Read<int>(block.Slice(SolidFlagsOffset)),
            (int)moveType,
            entities.IsPlayer(entity),
            new EntityHandle(MemoryMarshal.Read<uint>(block.Slice(OwnerOffset))),
            new EntityHandle(MemoryMarshal.Read<uint>(block.Slice(ParentOffset))),
            solidMask?.Invoke(entity) ?? EnginePushWorld.MaskSolid);
        return true;
    }

    public nint PhysicsObjectOf(EntityHandle entity) => physics.PhysicsObject(entity);
}

/// <summary>
/// The pairs of entities the game has told not to collide (stock's
/// g_EntityCollisionHash, keyed by entity: PhysDisableEntityCollisions /
/// PhysEnableEntityCollisions / PhysEntityCollisionsAreDisabled), owned by
/// managed. Keyed by packed handle, the world by <see cref="WorldKey"/>, so a
/// recycled entity never inherits a dead one's pair. Emptied with the level,
/// as stock destroys the hash with the environment.
/// </summary>
public sealed class EntityPairCollisions(IPhysicsSolverWorld world, IVPhysicsSolverObjects objects)
{
    /// <summary>The world's key: it is on neither entity arm (native's kPenetrationWorldKey).</summary>
    public const uint WorldKey = 0xFFFFFFFEu;

    private readonly HashSet<ulong> _pairs = new();

    /// <summary>Pairs currently disabled.</summary>
    public int Count => _pairs.Count;

    public static ulong PairKey(uint a, uint b) => a < b ? ((ulong)a << 32) | b : ((ulong)b << 32) | a;

    /// <summary>Whether the pair with these keys is disabled. Symmetric.</summary>
    public bool IsDisabled(uint key0, uint key1) => _pairs.Count != 0 && _pairs.Contains(PairKey(key0, key1));

    /// <summary>
    /// Stock's PhysEntityCollisionsAreDisabled, by entity. A handle that names
    /// nothing is the world; one object with itself is no pair.
    /// </summary>
    public bool AreDisabled(EntityHandle a, EntityHandle b)
        => Resolve(a, b, out uint ka, out uint kb, out _, out _) && IsDisabled(ka, kb);

    /// <summary>
    /// Stock's PhysEnableEntityCollisions / PhysDisableEntityCollisions: the
    /// pair is recorded, and both objects re-ask the filter (vphysics caches
    /// each pair's answer until RecheckCollisionFilter). False when there is no
    /// pair to form.
    /// </summary>
    public bool SetEnabled(EntityHandle a, EntityHandle b, bool enabled)
    {
        if (!Resolve(a, b, out uint ka, out uint kb, out nint oa, out nint ob))
            return false;

        if (enabled)
            _pairs.Remove(PairKey(ka, kb));
        else
            _pairs.Add(PairKey(ka, kb));

        // PhysRecheckObjectPair: a static object has no cached pairs of its own.
        if (!objects.IsStatic(oa))
            objects.RecheckCollisionFilter(oa);
        if (!objects.IsStatic(ob))
            objects.RecheckCollisionFilter(ob);
        return true;
    }

    public void Clear() => _pairs.Clear();

    // Each end's object, the world's for a handle that names nothing live (as
    // native's SetPairCollisions resolved them), and its owner's key.
    private bool Resolve(EntityHandle a, EntityHandle b, out uint ka, out uint kb, out nint oa, out nint ob)
    {
        oa = End(a, out ka);
        ob = End(b, out kb);
        return oa != 0 && ob != 0 && oa != ob;
    }

    private nint End(EntityHandle entity, out uint key)
    {
        if (world.TryEntity(entity, out _))
        {
            key = entity.Value;
            return world.PhysicsObjectOf(entity);
        }

        key = WorldKey;
        return world.WorldObject();
    }
}
