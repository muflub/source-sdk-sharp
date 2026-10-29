using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IEntityPhysicsHost"/> over the server's engine: the entity's
/// engine block read in place, the shell's solidity and teardown through the
/// entity area, the held pointer through native's swap, the managed class told
/// through the registry. Decides nothing.
/// </summary>
public sealed class EngineEntityPhysicsHost(IEngineEntities entities, IEngineEntityPhysics physics,
                                            IEngineEntityRender render, IEntityPhysicsNative native,
                                            ScriptedEntityRegistry registry, Func<bool> solidityBeforePhysics,
                                            Action<string> warning) : IEntityPhysicsHost
{
    private static readonly int HandleOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Handle));
    private static readonly int ModelOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.CollisionModelIndex));
    private static readonly int OriginOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Origin));
    private static readonly int AnglesOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Angles));
    private static readonly int SolidFlagsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidFlags));

    public bool TryFacts(EntityHandle entity, out EntityPhysicsFacts facts)
    {
        Span<byte> block = entities.GetEngineBlock(entity);
        if (block.Length < EngineBlock.SizeBytes)
        {
            facts = default;
            return false;
        }

        facts = new EntityPhysicsFacts(
            new EntityHandle(MemoryMarshal.Read<uint>(block.Slice(HandleOffset))),
            MemoryMarshal.Read<int>(block.Slice(ModelOffset)),
            MemoryMarshal.Read<Vector>(block.Slice(OriginOffset)),
            MemoryMarshal.Read<QAngle>(block.Slice(AnglesOffset)),
            (SolidFlags)MemoryMarshal.Read<int>(block.Slice(SolidFlagsOffset)),
            entities.GetClassName(entity));
        return true;
    }

    public string ModelName(EntityHandle entity) => render.GetModelName(entity);

    public nint PhysicsObject(EntityHandle entity) => physics.PhysicsObject(entity);

    public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags)
        => entities.SetSolidity(entity, solidType, solidFlags);

    public bool Swap(EntityHandle entity, nint physicsObject, out nint previous)
        => native.Swap(entity, physicsObject, out previous);

    public void DestroyPhysics(EntityHandle entity) => entities.DestroyPhysics(entity);

    public void SetMoveType(EntityHandle entity, MoveType moveType) => EngineBlockMoveType.Write(entities, entity, moveType);

    public void Created(EntityHandle entity) => registry.PhysicsCreated(entity);

    public string? MassCenterOverrideName(EntityHandle entity)
        => registry.Get(entity) is ITakesMassCenterOverride ? entities.GetName(entity) : null;

    public bool SolidityBeforePhysics => solidityBeforePhysics();

    public void Warning(string text) => warning(text);
}
