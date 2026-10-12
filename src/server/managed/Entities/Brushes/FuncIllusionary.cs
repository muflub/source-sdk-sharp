using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>A brush you can see and walk through. Decoration.</summary>
[ScriptedEntity(EntityNames.FuncIllusionary, ScriptedEntityKind.Brush)]
public sealed class FuncIllusionary : ScriptedEntity
{
    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.None);
        SetVisible(true);
    }
}
