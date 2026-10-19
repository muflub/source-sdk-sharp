using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The engine block's <c>Effects</c> word, the EF_* bits <c>m_fEffects</c>
/// networks (standalone_nettable.h). Managed sets and clears bits in it and
/// announces the write, so the next snapshot carries it to the client. A
/// write, not a decision: the callers decide. Native's SetVisible writes the
/// same word read-modify-write, so a bit set here survives EF_NODRAW changes.
/// </summary>
public static class EngineBlockEffects
{
    /// <summary>The word's offset inside the block, as MarkEntityBlockChanged takes it.</summary>
    public static readonly int Offset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Effects));

    /// <summary>The block's EF_* bits; 0 for a span too short to be a block.</summary>
    public static int Read(ReadOnlySpan<byte> block)
        => block.Length < EngineBlock.SizeBytes ? 0 : MemoryMarshal.Read<int>(block.Slice(Offset));

    /// <summary>
    /// Sets (<paramref name="on"/>) or clears <paramref name="bits"/> in the block;
    /// true when the word changed (and so needs announcing).
    /// </summary>
    public static bool Apply(Span<byte> block, EntityEffect bits, bool on)
    {
        if (block.Length < EngineBlock.SizeBytes)
            return false;

        int old = MemoryMarshal.Read<int>(block.Slice(Offset));
        int value = on ? old | (int)bits : old & ~(int)bits;
        if (value == old)
            return false;

        MemoryMarshal.Write(block.Slice(Offset), in value);
        return true;
    }
}

/// <summary>An entity's EF_* bits, as the code that switches them asks.</summary>
public interface IEntityEffectBits
{
    /// <summary>The entity's EF_* bits; 0 for an entity with no block.</summary>
    int Read(EntityHandle entity);

    /// <summary>Sets or clears <paramref name="bits"/>; the change reaches the client.</summary>
    void Set(EntityHandle entity, EntityEffect bits, bool on);
}

/// <summary><see cref="IEntityEffectBits"/> over the entity's engine block: one crossing a read, two a change.</summary>
public sealed class EngineEntityEffectBits(IEngineEntities entities, IEngineNetworking networking) : IEntityEffectBits
{
    public int Read(EntityHandle entity) => EngineBlockEffects.Read(entities.GetEngineBlock(entity));

    public void Set(EntityHandle entity, EntityEffect bits, bool on)
    {
        if (EngineBlockEffects.Apply(entities.GetEngineBlock(entity), bits, on))
            networking.MarkEntityBlockChanged(entity, EngineBlockEffects.Offset);
    }
}
