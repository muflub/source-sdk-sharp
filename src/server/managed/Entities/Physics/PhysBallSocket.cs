using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Free rotation about a point. The entity's origin is the point.</summary>
[ScriptedEntity(EntityNames.PhysBallsocket)]
public sealed class PhysBallSocket : PhysicsConstraintEntity
{
    public PhysBallSocket(IEngineConstraints constraints, IEngineSound sound)
        : base(constraints, sound)
    {
    }

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
        => Constraint.BallSocket(Constraints, a, b, Origin, ForceLimitKg, TorqueLimitKg);
}
