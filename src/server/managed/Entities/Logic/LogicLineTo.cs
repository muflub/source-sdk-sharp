using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Names two points and reports the line between them. Declared for map
/// compatibility; nothing subscribes to the measurement in this mod.
/// </summary>
[ScriptedEntity(EntityNames.LogicLineto)]
public sealed class LogicLineTo : ScriptedEntity
{
    public readonly Output Line = new();

    /// <summary>What a map may set on this entity. See <see cref="LogicLineToVars"/>.</summary>
    [ScriptVars] public LogicLineToVars Vars = new();
}

/// <summary>
/// The map keys <see cref="LogicLineTo"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicLineToVars
{
    public string Source { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;
}
