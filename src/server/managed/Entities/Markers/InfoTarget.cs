using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A named point in the world. Maps use it as somewhere to aim, somewhere to
/// play a sound from, and somewhere to point an entity at.
/// </summary>
[ScriptedEntity(EntityNames.InfoTarget)]
[ScriptedEntity(EntityNames.InfoTeleportDestination)]
[ScriptedEntity(EntityNames.InfoLandmark)]
[ScriptedEntity(EntityNames.InfoMassCenter)]
public sealed class InfoTarget : ScriptedEntity
{
}
