using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A name with nothing behind it, faithfully.
///
/// Stock binds logic_proximity to a bare CPointEntity — no keys, no inputs, no
/// outputs, no think. Whatever it was meant to become was never written, and a
/// map placing one gets a point in space that does nothing. This is that, in C#,
/// so the name still resolves and still does nothing.
///
/// Giving it the behaviour the name suggests would be inventing an entity rather
/// than porting one, and the mod already has the entity that measures distance
/// to a point — point_proximity_sensor — with trigger_proximity for the volume
/// case. A map wanting either should say so.
/// </summary>
[ScriptedEntity(EntityNames.LogicProximity)]
public sealed class LogicProximity : ScriptedEntity
{
}
