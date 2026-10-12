//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// phys_spring: a force pulling two entities towards a separation.
///
/// It sits in this file rather than with the emitters because it has the
/// constraint shape — two names, resolved after the map has loaded, joining
/// something that is not an entity — but it is deliberately NOT a
/// <see cref="PhysicsConstraintEntity"/>. That base assumes a joint that limits
/// where the ends may be, and offers a force and torque limit for breaking one.
/// A spring limits nothing and cannot break: it only pulls harder the further
/// it is stretched, and pulls with no force at all at its natural length.
///
/// So it repeats the deferred resolve rather than inheriting it. Four lines of
/// duplication is the honest price for not giving every constraint a
/// "breakable, unless it is a spring" exception.
/// </summary>
[ScriptedEntity(EntityNames.PhysSpring)]
public sealed class PhysSpring : ScriptedEntity
{
    public PhysSpring(IEngineSprings springs)
    {
        Springs = springs;
    }

    /// <summary>the solver's spring table, which the spring is made in.</summary>
    private readonly IEngineSprings Springs;

    /// <summary>What a map may set on this entity. See <see cref="PhysSpringVars"/>.</summary>
    [ScriptVars] public PhysSpringVars Vars = new();

    /// <summary>Pull but never push — a rope rather than a strut.</summary>
    private const int OnlyStretchFlag = 1;

    private Spring? _spring;

    /// <summary>
    /// The spring's raw native id, for the reload: the native spring outlives
    /// the assembly swap, and stock carries the pointer (DEFINE_PHYSPTR
    /// m_pSpring, physobj.cpp:91). <see cref="LoadState"/> re-adopts it.
    /// </summary>
    [SaveField] private uint _springId;
    private Vector _near;

    /// <summary>What the map asked for, for a test with no map.</summary>
    internal void Configure(Vector near, Vector far, float length)
    {
        _near = near;
        Vars.Far = far;
        Vars.Length = length;
    }

    internal float RestLength => Vars.Length;

    public override void OnSpawn()
    {
        _near = Origin;
        RestAtCurrentSeparation();

        // Deferred for the same reason the constraints are: both ends are named
        // and half the map does not exist yet while it is being parsed.
        SetNextThink(0f);
    }

    /// <summary>
    /// A spring with no length given rests where it was drawn.
    ///
    /// Separated out because it is the one piece of arithmetic here, and because
    /// getting it wrong is invisible: a spring that defaults to zero length
    /// yanks both ends together the instant the map loads, which reads as the
    /// two props being thrown at each other by something else.
    /// </summary>
    internal void RestAtCurrentSeparation()
    {
        if (Vars.Length <= 0f)
            Vars.Length = (Vars.Far - _near).Length;
    }

    public override void OnThink()
    {
        StopThinking();

        if (_spring is not null)
            return;

        EntityHandle a = Resolve(Vars.Attach1);
        EntityHandle b = Resolve(Vars.Attach2);

        if (!Entities.IsValid(a) && !Entities.IsValid(b))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' has neither end attached\n");
            return;
        }

        _spring = Spring.Make(Springs, a, b, _near, Vars.Far, Vars.Constant, Vars.Damping, Vars.RelativeDamping,
                              Vars.Length, (SpawnFlags & OnlyStretchFlag) != 0);

        _springId = _spring?.Id.Value ?? 0u;

        if (_spring is null)
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' could not be created\n");
    }

    /// <summary>
    /// Puts the wrapper back around the spring the reload left standing.
    /// Re-adopting rather than re-making is the point: a second Make would add
    /// a second force on the same bodies with nothing able to reach the first.
    /// </summary>
    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);

        if (_spring is null && _springId != 0u)
            _spring = Spring.Adopt(Springs, new PhysicsId(_springId));
    }

    [ScriptedEvent("SetSpringConstant")]
    private void OnSetSpringConstant(string value)
    {
        Vars.Constant = Parse(value, Vars.Constant);
        _spring?.SetConstant(Vars.Constant);
    }

    [ScriptedEvent("SetSpringDamping")]
    private void OnSetSpringDamping(string value)
    {
        Vars.Damping = Parse(value, Vars.Damping);
        _spring?.SetDamping(Vars.Damping);
    }

    [ScriptedEvent("SetSpringLength")]
    private void OnSetSpringLength(string value)
    {
        Vars.Length = Parse(value, Vars.Length);
        _spring?.SetLength(Vars.Length);
    }

    public override void OnRemoved()
    {
        _spring?.Destroy();
        _spring = null;
        _springId = 0u;
    }

    private static float Parse(string value, float fallback)
        => float.TryParse(value, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;

    /// <summary>
    /// An empty name is the world, not a lookup failure. A name that was given
    /// and does not resolve is a map error and says so.
    /// </summary>
    private EntityHandle Resolve(string name)
    {
        if (name.Length == 0)
            return EntityHandle.None;

        EntityHandle handle = Entities.FindByName(name);

        if (!Entities.IsValid(handle))
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': no entity named '{name}'\n");

        return handle;
    }
}

/// <summary>
/// The map keys <see cref="PhysSpring"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysSpringVars
{
    public string Attach1 { get; set; } = string.Empty;

    public string Attach2 { get; set; } = string.Empty;

    /// <summary>Stiffness. Stock's default, and high enough to hold a crate up.</summary>
    public float Constant { get; set; } = 150f;

    /// <summary>
    /// The separation at which the spring pulls with nothing. Left unset, it is
    /// however far apart the map drew the two ends — so a spring drawn at rest
    /// stays at rest, which is what a mapper expects from placing one.
    /// </summary>
    public float Length { get; set; }

    public float Damping { get; set; } = 2f;

    public float RelativeDamping { get; set; } = 0.01f;

    /// <summary>
    /// The far end, in world space. Named for the axis it describes rather than
    /// for the point it is, which is Hammer's name for the key and not one
    /// worth improving on here.
    /// </summary>
    [VarName("springaxis")]
    public Vector Far { get; set; }
}
