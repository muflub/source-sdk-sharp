using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>A plain solid brush. The simplest entity in the game.</summary>
[ScriptedEntity(EntityNames.FuncWall, ScriptedEntityKind.Brush)]
public sealed class FuncWall : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);
    }
}
