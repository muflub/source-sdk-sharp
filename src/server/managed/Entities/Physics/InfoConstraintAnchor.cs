using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A point a constraint can attach to instead of an entity.
///
/// It does nothing itself; it exists to be found by name and to have a
/// position. That is the whole entity in stock Source too.
/// </summary>
[ScriptedEntity(EntityNames.InfoConstraintAnchor)]
public sealed class InfoConstraintAnchor : ScriptedEntity
{
}
