using System;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>Where an entity's engine block puts it: its origin, angles and collision box.</summary>
public readonly record struct EntityPlacement(Vector Origin, QAngle Angles, Vector Mins, Vector Maxs);

/// <summary>
/// An entity's place in the engine's collision, read and moved as one, for
/// code that moves an entity for a moment and back (the lag compensation's
/// rewind). Not named <c>IEngine*</c>: it is not an engine area, it is the
/// server's own read and write of an entity's engine block.
/// </summary>
public interface IServerEntityPlacement
{
    /// <summary>
    /// The entity's origin, angles and collision box as its engine block holds
    /// them; false, with nothing read, for an entity with no block.
    /// </summary>
    bool TryRead(EntityHandle entity, out EntityPlacement placement);

    /// <summary>
    /// <c>CBaseEntity::SetLocalOrigin</c> and <c>SetLocalAngles</c> on an
    /// unparented entity: the block's origin and angles written, the entity
    /// relinked in the engine's partition (so a trace finds it there) and its
    /// children moved with it. No trigger is touched, no teleport is announced
    /// to the client, the velocity is kept and no physics object is moved:
    /// stock's setters do none of those.
    /// </summary>
    void Relink(EntityHandle entity, Vector origin, QAngle angles);
}

/// <summary>
/// <see cref="IServerEntityPlacement"/> over the server's engine blocks
/// (<see cref="IEngineEntities.GetEngineBlock"/>) and its transform area: the
/// read is the block's head, the move is the area's
/// teleport with no velocity change (<c>CStandaloneEntity::TeleportTo</c>,
/// which writes the block and relinks it with no trigger sweep), and none of
/// <see cref="IEngineEntities.Teleport"/>'s managed half: no discontinuity
/// bump for the client and no physics object moved.
/// </summary>
public sealed unsafe class ServerEntityPlacement : IServerEntityPlacement
{
    private readonly IEngineEntities _entities;
    private readonly ITransformArea _transform;

    internal ServerEntityPlacement(IEngineEntities entities, ITransformArea transform)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));
    }

    /// <summary>Over <paramref name="entities"/> and <paramref name="engine"/>'s transform area, taken here once.</summary>
    public static ServerEntityPlacement For(ModuleEngine engine, IEngineEntities entities)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new ServerEntityPlacement(entities, engine.Attachment.Areas.Require<ITransformArea>());
    }

    /// <inheritdoc/>
    public bool TryRead(EntityHandle entity, out EntityPlacement placement)
    {
        Span<byte> block = _entities.GetEngineBlock(entity);

        if (block.Length < EngineBlock.SizeBytes)
        {
            placement = default;
            return false;
        }

        ref readonly EngineBlockHead head = ref MemoryMarshal.AsRef<EngineBlock>(block).Head;
        placement = new EntityPlacement(head.Origin, head.Angles, head.Mins, head.Maxs);
        return true;
    }

    /// <inheritdoc/>
    public void Relink(EntityHandle entity, Vector origin, QAngle angles)
    {
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        float* pyr = stackalloc float[3] { angles.Pitch, angles.Yaw, angles.Roll };
        _transform.EntityTeleport(entity.Value, xyz, pyr, 0);
    }
}
