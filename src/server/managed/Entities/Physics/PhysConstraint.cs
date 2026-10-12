using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Welds two objects together where they stand. Attached to the world at one
/// end, it pins the other in place.
/// </summary>
[ScriptedEntity(EntityNames.PhysConstraint)]
public sealed class PhysConstraint : PhysicsConstraintEntity
{
    public PhysConstraint(IEngineConstraints constraints, IEngineSound sound)
        : base(constraints, sound)
    {
    }

    protected override Constraint? Build(EntityHandle a, EntityHandle b)
        => Constraint.Fixed(Constraints, a, b, ForceLimitKg, TorqueLimitKg);
}
