using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IPhysicsConversionWorld"/> over the server's engine areas, the
/// level's parenting registry and native's hold. One call each; decides nothing.
/// </summary>
public sealed class EnginePhysicsConversionWorld(IEngineEntities entities, IEngineEntityRender render,
                                                 IEngineEntityPhysics physics, IEngineNetworking networking,
                                                 IEnginePrecache precache, IEntityPhysicsNative native,
                                                 Func<Hierarchy?> parenting, Action<string> message)
    : IPhysicsConversionWorld
{
    private static readonly int ModelIndexOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.ModelIndex));

    public MoveType MoveTypeOf(EntityHandle entity)
    {
        networking.EntityGetState(entity, out MoveType moveType);
        return moveType;
    }

    public string ClassName(EntityHandle entity) => entities.GetClassName(entity);

    public string Name(EntityHandle entity) => entities.GetName(entity);

    public string ModelName(EntityHandle entity) => render.GetModelName(entity);

    public bool IsBrushModel(EntityHandle entity)
    {
        Span<byte> block = entities.GetEngineBlock(entity);
        return block.Length >= EngineBlock.SizeBytes
               && precache.ModelTypeOf(MemoryMarshal.Read<int>(block.Slice(ModelIndexOffset))) == ModelType.Brush;
    }

    public Vector AbsOrigin(EntityHandle entity) => entities.GetOrigin(entity);

    public Vector AbsAngles(EntityHandle entity) => entities.GetAngles(entity);

    public void Unparent(EntityHandle entity)
    {
        if (entities.GetParent(entity) != EntityHandle.None)
            entities.SetParent(entity, EntityHandle.None);
    }

    public IReadOnlyList<(EntityHandle Child, int Attachment)> Children(EntityHandle parent)
    {
        var children = new List<(EntityHandle, int)>();
        if (parenting() is { } hierarchy)
        {
            foreach (ParentLink link in hierarchy.Links)
            {
                if (link.Parent == parent)
                    children.Add((link.Child, entities.GetParentAttachment(link.Child)));
            }
        }

        return children;
    }

    public void SetParent(EntityHandle child, EntityHandle parent, int attachment)
        => entities.SetParent(child, parent, attachment);

    public void SetModel(EntityHandle entity, string model) => render.SetModel(entity, model);

    public EntityHandle Create(string className) => entities.Create(className);

    public void SetKeyValue(EntityHandle entity, string key, string value) => entities.SetKeyValue(entity, key, value);

    public void SetOrigin(EntityHandle entity, Vector origin) => entities.SetOrigin(entity, origin);

    public void SetAngles(EntityHandle entity, Vector angles) => entities.SetAngles(entity, angles);

    public void Spawn(EntityHandle entity) => entities.Spawn(entity);

    public nint PhysicsObject(EntityHandle entity) => physics.PhysicsObject(entity);

    public bool Swap(EntityHandle entity, nint physicsObject, out nint previous)
        => native.Swap(entity, physicsObject, out previous);

    public void DestroyPhysics(EntityHandle entity) => entities.DestroyPhysics(entity);

    public void SetMoveType(EntityHandle entity, MoveType moveType) => EngineBlockMoveType.Write(entities, entity, moveType);

    public void SetMass(EntityHandle entity, float mass) => physics.SetMass(entity, mass);

    public void SetCollisionGroup(EntityHandle entity, CollisionGroup group) => physics.SetCollisionGroup(entity, group);

    public int PoseParameterCount(EntityHandle entity) => render.PoseParameterCount(entity);

    public float GetPoseParameter(EntityHandle entity, int parameter) => render.GetPoseParameter(entity, parameter);

    public void SetPoseParameter(EntityHandle entity, int parameter, float value)
        => render.SetPoseParameter(entity, parameter, value);

    public void SetNotSolid(EntityHandle entity) => physics.SetNotSolid(entity, true);

    public void SetNoDraw(EntityHandle entity) => render.SetVisible(entity, false);

    public void Remove(EntityHandle entity) => entities.Remove(entity);

    public void Message(string text) => message(text);
}
