using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Autosave points. Deliberately inert: this is a multiplayer mod, where there
/// is no save game to write. Declared so maps that place them still load, and so
/// the class name does not fall through as unknown.
/// </summary>
[ScriptedEntity(EntityNames.LogicAutosave)]
public sealed class LogicAutosave : ScriptedEntity
{
    [ScriptedEvent] private void OnSave() { }
    [ScriptedEvent] private void OnSaveDangerous() { }
}
