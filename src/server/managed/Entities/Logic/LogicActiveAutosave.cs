using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <inheritdoc cref="LogicAutosave"/>
[ScriptedEntity(EntityNames.LogicActiveAutosave)]
public sealed class LogicActiveAutosave : ScriptedEntity
{
    [ScriptedEvent] private void OnEnable() { }
    [ScriptedEvent] private void OnDisable() { }
}
