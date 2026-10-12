using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Entities that drive something continuously — a motor spinning a fan, a
/// torque twisting a prop.
///
/// They share a shape with the constraints: two named things, resolved after
/// the map has finished loading, joined by something that is not itself an
/// entity. What they do NOT share is where the work happens. A constraint is
/// built once and then belongs entirely to the solver; a force has to be
/// supplied every physics tick, so it lives in a native controller whose
/// parameters C# sets. See <see cref="MotionController"/> for why.
/// </summary>
public abstract class PhysicsForceEntity : ScriptedEntity
{
    protected PhysicsForceEntity(IEngineMotionControllers motion)
    {
        _motion = motion;
    }

    /// <summary>
    /// The side's motion-controller table, which <see cref="Controller"/> is
    /// made in. A constructor parameter, as every area but
    /// <see cref="ScriptedEntity.Entities"/> is.
    /// </summary>
    private readonly IEngineMotionControllers _motion;

    /// <summary>What a map may set on this entity. See <see cref="PhysicsForceEntityVars"/>.</summary>
    [ScriptVars] public PhysicsForceEntityVars Vars = new();

    /// <summary>
    /// The controller's raw native id, carried so a reload can re-adopt the
    /// wrapper below. The native controller outlives the assembly swap
    /// (PhysicsMotionControllers carries its rows across it and clears them only at LevelShutdown),
    /// and stock saves the same pointer — m_motor is DEFINE_PHYSPTR, rebuilt
    /// on restore (phys_controller.cpp's DATADESCs).
    /// </summary>
    [SaveField] private uint _controllerId;

    protected MotionController? Controller { get; private set; }

    /// <summary>The entity the force acts on; a handle leaf, re-checked on the way in.</summary>
    [SaveField] protected EntityHandle Attached { get; private set; } = EntityHandle.None;

    protected abstract MotionControllerKind Kind { get; }

    /// <summary>Called once the controller exists, to set it up.</summary>
    protected abstract void OnAttached();

    /// <summary>
    /// Deferred to the first think, as the constraints are and for the same
    /// reason: the target is named, and while the map is being parsed half of
    /// it does not exist yet.
    /// </summary>
    public override void OnSpawn() => SetNextThink(0f);

    public override void OnThink()
    {
        if (Controller is not null)
            return;

        Attached = Entities.FindByName(Vars.Attach1);

        if (!Entities.IsValid(Attached))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': no entity named '{Vars.Attach1}'\n");
            return;
        }

        Controller = MotionController.Attach(_motion, Attached, Kind);

        if (Controller is null)
        {
            // Almost always the same cause: the map pointed this at a brush or a
            // static prop, which has nothing for a force to act on.
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': '{Vars.Attach1}' has no physics object\n");
            return;
        }

        _controllerId = Controller.Id.Value;

        OnAttached();
    }

    /// <summary>
    /// Re-adopts the controller the reload left standing. Without this the
    /// rebuilt entity sees a null wrapper and <see cref="OnThink"/> would
    /// happily ATTACH A SECOND controller to a body the first is still
    /// driving — two forces, and neither reachable to destroy.
    /// </summary>
    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);

        if (Controller is null && _controllerId != 0u)
            Controller = MotionController.Adopt(_motion, new PhysicsId(_controllerId), Kind);
    }

    public override void OnRemoved()
    {
        Controller?.Destroy();
        Controller = null;
        _controllerId = 0u;
    }
}

/// <summary>
/// The map keys <see cref="PhysicsForceEntity"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysicsForceEntityVars
{
    /// <summary>Name of the entity to act on. It needs a physics object.</summary>
    public string Attach1 { get; set; } = string.Empty;
}
