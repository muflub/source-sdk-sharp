using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// prop_sphere: a ball, for finding out how the physics behaves.
///
/// Stock's own comment calls it the debug sphere, and it is a <c>CPhysicsProp</c>
/// only by inheritance: <c>OverridePropdata</c> returns true, which stops the
/// model's prop data being read at all, and with that gone every part of
/// prop_physics that decides anything — the health, the damage modifiers, the
/// explosion, the gibs — has nothing to decide from. What is left is a model the
/// solver moves, so that is what this is, and it is why this does not derive from
/// <see cref="PropPhysics"/> despite stock's class doing exactly that.
///
/// The sphere itself is the part that cannot come over. Stock builds a real
/// twelve-unit sphere object rather than using the model's compiled hull, which
/// is the whole point of the entity — a sphere rolls where a hull tumbles.
/// See the divergence note.
/// </summary>
[ScriptedEntity(EntityNames.PropSphere, ScriptedEntityKind.Prop)]
public sealed class PropSphere : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="PropSphereVars"/>.</summary>
    [ScriptVars] public PropSphereVars Vars = new();

    public override void OnSpawn()
    {
        // The prop shell creates the physics object itself, after this returns
        // and after it has applied the pending model — which is the only order
        // that works, because the object is read out of the model.
        if (Vars.Model.Length > 0)
            SetModel(Vars.Model);
    }
}

/// <summary>
/// The map keys <see cref="PropSphere"/> declares.
/// </summary>
[ScriptVars]
public partial class PropSphereVars
{
    public string Model { get; set; } = string.Empty;
}
