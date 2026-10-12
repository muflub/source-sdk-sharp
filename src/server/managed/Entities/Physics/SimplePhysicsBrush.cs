using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// simple_physics_brush: what a brush BECOMES.
///
/// Not an entity a mapper places. phys_convert makes one when it turns a static
/// piece of scenery into something that falls over, and it exists because a
/// converted brush needs somewhere to put the physics object it inherits.
///
/// It takes damage only in the sense of reporting it, because whatever it was
/// converted from has already had its say about breaking.
/// </summary>
[ScriptedEntity(EntityNames.SimplePhysicsBrush, ScriptedEntityKind.Brush)]
public sealed class SimplePhysicsBrush : ScriptedEntity
{
    public SimplePhysicsBrush(IEngineEntityPhysics physics)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        // Safe if the conversion has already handed one over: native answers
        // yes without building a second, which would orphan the first.
        if (!Physics.InitPhysics(Handle))
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' could not be simulated\n");
    }
}
