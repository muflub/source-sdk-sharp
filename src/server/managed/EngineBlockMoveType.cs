using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The engine block's <c>MoveType</c> word: managed writes a non-player
/// entity's movetype there when its physics object is made or handed over,
/// and native's StandaloneMoveType_Report reads it back for an entity holding
/// an object (2026-10-12, plan_physics_bones_perf C2/C6). A write, not a
/// decision: the callers decide.
/// </summary>
public static class EngineBlockMoveType
{
    private static readonly int Offset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.MoveType));

    /// <summary>Writes <paramref name="moveType"/> into the entity's block; nothing for a handle with none.</summary>
    public static void Write(IEngineEntities entities, EntityHandle entity, MoveType moveType)
    {
        Span<byte> block = entities.GetEngineBlock(entity);
        if (block.Length < EngineBlock.SizeBytes)
            return;

        MemoryMarshal.Write(block.Slice(Offset), (int)moveType);
    }

    /// <summary>The entity's movetype as its block holds it; <see cref="MoveType.None"/> for a handle with none.</summary>
    public static MoveType Read(IEngineEntities entities, EntityHandle entity)
    {
        Span<byte> block = entities.GetEngineBlock(entity);
        if (block.Length < EngineBlock.SizeBytes)
            return MoveType.None;

        return (MoveType)MemoryMarshal.Read<int>(block.Slice(Offset));
    }
}

/// <summary>
/// The engine block's <c>OwnerHandle</c> word: GetOwnerEntity, read where it
/// is kept. <see cref="IEngineEntities.SetOwner"/> is the write.
/// </summary>
public static class EngineBlockOwner
{
    private static readonly int Offset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.OwnerHandle));

    /// <summary>The entity's owner, or <see cref="EntityHandle.None"/>.</summary>
    public static EntityHandle Read(IEngineEntities entities, EntityHandle entity)
    {
        Span<byte> block = entities.GetEngineBlock(entity);
        if (block.Length < EngineBlock.SizeBytes)
            return EntityHandle.None;

        return new EntityHandle(MemoryMarshal.Read<uint>(block.Slice(Offset)));
    }
}
