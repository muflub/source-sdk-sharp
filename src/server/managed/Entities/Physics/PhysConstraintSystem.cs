using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Groups constraints so the solver treats them as one system.
///
/// Deliberately does nothing. In stock Source this creates a VPhysics
/// constraint group, which improves the solution when several joints share
/// objects — a chain, a ragdoll. Constraints built here are ungrouped, so a map
/// naming a system gets independent joints rather than an error.
///
/// This is a stop, not an omission: grouping needs the group to exist before any
/// member is created, which is an ordering constraint the deferred build above
/// does not currently express. Worth revisiting if a map ever shows it.
/// </summary>
[ScriptedEntity(EntityNames.PhysConstraintsystem)]
public sealed class PhysConstraintSystem : ScriptedEntity
{
}
