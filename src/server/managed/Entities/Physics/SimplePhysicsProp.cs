using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// simple_physics_prop: the same for a model.
///
/// It needs no InitPhysics call, unlike its brush counterpart, and the asymmetry
/// is the shells' rather than these classes': the Prop shell creates a physics
/// object during its own spawn because every scripted prop is simulated, while
/// the Brush shell deliberately does not — most brushes are doors and triggers.
///
/// So all this has to do is make sure the model arrives. phys_convert sets it as
/// a keyvalue before spawning, which reaches here like any other.
/// </summary>
[ScriptedEntity(EntityNames.SimplePhysicsProp, ScriptedEntityKind.Prop)]
public sealed class SimplePhysicsProp : AnimatingEntity
{
    /// <summary>What a map may set on this entity. See <see cref="SimplePhysicsPropVars"/>.</summary>
    [ScriptVars] public SimplePhysicsPropVars Vars = new();

    public override void OnSpawn()
    {
        if (Vars.Model.Length > 0)
            SetModel(Vars.Model);
    }
}

/// <summary>
/// The map keys <see cref="SimplePhysicsProp"/> declares.
/// </summary>
[ScriptVars]
public partial class SimplePhysicsPropVars
{
    public string Model { get; set; } = string.Empty;
}
