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
}
