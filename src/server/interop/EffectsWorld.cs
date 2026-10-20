using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// What the two traced effects (SourceSharp.Server's <c>EffectsOverTempEnts.PaintDecal</c>,
/// <c>BubbleEffect</c>) ask of the engine beyond a
/// temp entity (plan_server_native_v2 T4f: SourceSharp_PaintDecal and
/// SourceSharp_BubbleEffect, which asked the same things natively).
/// </summary>
public interface IEffectsWorld
{
    /// <summary>
    /// <c>UTIL_TraceLine( start, end, mask, CTraceFilterSimple( NULL,
    /// COLLISION_GROUP_NONE ) )</c> with the engine's own <c>MASK_*</c>; null
    /// when the side traces nothing (no trace filter was built).
    /// </summary>
    ServerTrace? Trace(Vector start, Vector end, uint mask);

    /// <summary><c>engine->StaticDecal( position, decal, entityIndex, modelIndex, lowPriority )</c>.</summary>
    void StaticDecal(Vector originInEntitySpace, int decalIndex, int entityIndex, int modelIndex, bool lowPriority);

    /// <summary>The game's <c>PrecacheModel( name )</c>: the index, or 0 / -1 for none.</summary>
    int PrecacheModel(string name);

    /// <summary>The entity's edict index: 0 for the world, -1 for one with no edict or none at all.</summary>
    int EntityIndex(EntityHandle entity);

    /// <summary>The entity's engine block, empty for anything without one.</summary>
    Span<byte> Block(EntityHandle entity);

    /// <summary>The entity's classname and name, for a diagnostic.</summary>
    (string ClassName, string Name) Names(EntityHandle entity);
}

/// <summary>
/// <see cref="IEffectsWorld"/> over the side's tracer (ServerTraceRouting) and
/// the engine server's forwarders. The engine server is acquired on first use:
/// this is composed with the engine areas, before the side's interfaces are
/// asked for. The sprite's precache is the game's (<see cref="IEnginePrecache"/>,
/// Precache_Model): the world precached it at level load (W_Precache), so it
/// answers the index and warns of nothing, as the engine's own did.
/// </summary>
public sealed class EngineEffectsWorld(ModuleEngine engine, IEngineEntities entities, IEnginePrecache precache)
    : IEffectsWorld
{
    // COLLISION_GROUP_NONE.
    private const int CollisionGroupNone = 0;

    private IFwdEngineServer? _server;

    /// <inheritdoc/>
    public ServerTrace? Trace(Vector start, Vector end, uint mask)
        => ServerTraceRouting.TracerOf(engine) is { } tracer
               ? tracer.Line(start, end, mask, EntityHandle.None, CollisionGroupNone)
               : null;

    /// <inheritdoc/>
    public void StaticDecal(Vector originInEntitySpace, int decalIndex, int entityIndex, int modelIndex, bool lowPriority)
        => Server().StaticDecal(originInEntitySpace, decalIndex, entityIndex, modelIndex, lowPriority);

    /// <inheritdoc/>
    public int PrecacheModel(string name) => precache.PrecacheModel(name);

    /// <inheritdoc/>
    public int EntityIndex(EntityHandle entity) => entities.GetIndex(entity);

    /// <inheritdoc/>
    public Span<byte> Block(EntityHandle entity) => entities.GetEngineBlock(entity);

    /// <inheritdoc/>
    public (string ClassName, string Name) Names(EntityHandle entity)
        => (entities.GetClassName(entity), entities.GetName(entity));

    private IFwdEngineServer Server()
        => _server ??= EngineServer.Acquire(engine, out string why)
                       ?? throw new InvalidOperationException("the effects world has no engine server: " + why);
}
