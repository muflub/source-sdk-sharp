using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// VScript host. This mod's scripting is C#, so the class exists only so maps
/// referencing it load; the scripts it names are not run.
/// </summary>
[ScriptedEntity(EntityNames.LogicScript)]
[ClaimsKeys("Group", 0, 16)]
public sealed class LogicScript : ScriptedEntity
{
    public override bool OnKeyValue(string key, string value)
        => key.StartsWith("Group", StringComparison.OrdinalIgnoreCase);
}
