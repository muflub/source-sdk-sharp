using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A place, and nothing else.
///
/// <para>
/// Stock's CEnableMotionFixup is a two-line class with an empty body: it
/// declares itself and stops. Everything about it lives in the entities that
/// look it up by name — physics_cannister and the motion-enable inputs read its
/// ORIGIN to decide where a prop should be re-placed when its motion is turned
/// on, and never ask the entity anything else. A marker whose whole content is
/// its position is the plainest possible entity, and it is the only reason
/// this file exists rather than the classname simply being dropped.
/// </para>
///
/// <para>
/// Kept as <see cref="ScriptedEntityKind.Logical"/>: no model, no collision,
/// nothing networked. A mapper pointing at it gets a position, which is all
/// stock ever gave them.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointEnableMotionFixup)]
public sealed class PointEnableMotionFixup : ScriptedEntity
{
}
