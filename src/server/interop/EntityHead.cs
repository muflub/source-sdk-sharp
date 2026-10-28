namespace SourceSharp.Abi;

/// <summary>
/// What a standalone entity's collideable answers, read from its engine
/// block's head in place. The world has no block: it sits at the origin with
/// no bounds and is SOLID_BSP with no flags. A handle that resolves to nothing
/// answers zero.
/// </summary>
internal static unsafe class EntityHead
{
    private static readonly int TypeOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidType));
    private static readonly int FlagsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidFlags));
    private static readonly int OriginOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Origin));
    private static readonly int MinsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Mins));
    private static readonly int MaxsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Maxs));

    public static SolidType Shape(INetworkServerArea network, IEntitiesArea entities, EntityHandle entity)
        => (SolidType)Word(network, entities, entity, TypeOffset, world: (int)SolidType.Bsp);

    public static SolidFlags Flags(INetworkServerArea network, IEntitiesArea entities, EntityHandle entity)
        => (SolidFlags)Word(network, entities, entity, FlagsOffset, world: 0);

    /// <summary>The middle of the collision box, in world space (stock's WorldSpaceCenter).</summary>
    public static Vector WorldSpaceCenter(INetworkServerArea network, EntityHandle entity)
    {
        byte* block = Block(network, entity);

        if (block == null)
            return default;

        Vector origin = *(Vector*)(block + OriginOffset);
        Vector mins = *(Vector*)(block + MinsOffset);
        Vector maxs = *(Vector*)(block + MaxsOffset);
        return new Vector(origin.X + (mins.X + maxs.X) * 0.5f,
                          origin.Y + (mins.Y + maxs.Y) * 0.5f,
                          origin.Z + (mins.Z + maxs.Z) * 0.5f);
    }

    private static int Word(INetworkServerArea network, IEntitiesArea entities, EntityHandle entity, int offset, int world)
    {
        byte* block = Block(network, entity);

        if (block != null)
            return *(int*)(block + offset);

        // The world is index 0; a handle that resolves to nothing is -1.
        return entities.EntityGetIndex(entity.Value) == 0 ? world : 0;
    }

    private static byte* Block(INetworkServerArea network, EntityHandle entity)
    {
        int size;
        byte* block = (byte*)network.EntityGetEngineBlock(entity.Value, &size);
        return size >= EngineBlock.SizeBytes ? block : null;
    }
}
