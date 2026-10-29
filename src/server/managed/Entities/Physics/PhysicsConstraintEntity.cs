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
/// The phys_* constraint entities, in C#.
///
/// These are the first entities here that describe a relationship rather than a
/// thing. Everything ported so far has been a single entity acting on itself —
/// a door that moves, a trigger that fires. A constraint joins two OTHER
/// entities, owns neither, and does nothing itself once it has been made.
///
/// That shape is the reason this file exists rather than more classes in
/// BrushEntities.cs, and it is why they share a base: resolving two names,
/// waiting until both exist, and tearing the joint down afterwards is the same
/// problem every time, and the only part that differs is which joint to build.
/// </summary>
/// <summary>
/// The spawnflags every phys_* constraint shares, from physconstraint.cpp.
///
/// <para>
/// A constraint's spawnflag word is half family and half private, and nothing
/// in the number says which half a given bit is in. Bits 1, 4, 8 and 0x10 mean
/// the same thing on every constraint in the family; bit 2 means something
/// DIFFERENT on each one -- "limit ends" on a slide, "rigid" on a pulley and on
/// a length, "free movement" on a ragdoll. That is why bit 2 is deliberately
/// absent here and declared on each entity instead: a shared member for it
/// would let a slide ask a length constraint's question and get an answer.
/// </para>
///
/// <para>
/// Only the two the port honours are named. Stock also defines
/// SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY (0x0008) and
/// SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED (0x0010); naming those here would
/// imply they are read, and they are not.
/// </para>
/// </summary>
[Flags]
internal enum ConstraintSpawnFlag
{
    /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
    None = 0b0000_0000,

    /// <summary>
    /// The mapper's "do not collide" checkbox. Almost every hinge wants it,
    /// because a hinge usually joins two things that overlap.
    /// </summary>
    DisableCollision = 0b0000_0001,

    /// <summary>
    /// SF_CONSTRAINT_START_INACTIVE: built but not holding until TurnOn
    /// (GetBreakParams' isActive, for every kind).
    /// </summary>
    StartInactive = 0b0000_0100,

    /// <summary>
    /// SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY: a hinge to the world makes its
    /// object itself hinged, fixed in place and turning about one axis.
    /// </summary>
    AssumeWorldGeometry = 0b0000_1000,

    /// <summary>
    /// SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED: not built at all until
    /// TurnOn, which looks its two ends up then.
    /// </summary>
    NoConnectUntilActivated = 0b0001_0000,
}

public abstract class PhysicsConstraintEntity : ScriptedEntity
{
    protected PhysicsConstraintEntity(IEngineConstraints constraints, IEngineSound sound, EntityPairCollisions pairs)
    {
        Constraints = constraints;
        Sound = sound;
        Pairs = pairs;
    }

    /// <summary>Where a "do not collide" pair is recorded (stock's PhysDisableEntityCollisions).</summary>
    private EntityPairCollisions Pairs { get; }

    private ConstraintSpawnFlag ConstraintFlags => SpawnFlagsAs<ConstraintSpawnFlag>();

    /// <summary>Where the breaksound is played.</summary>
    private IEngineSound Sound { get; }

    /// <summary>
    /// The side's constraint table, which every joint this entity builds is
    /// made in. A constructor parameter, as every area but
    /// <see cref="ScriptedEntity.Entities"/> is: the joint acts on two other
    /// entities, not on this one.
    /// </summary>
    protected IEngineConstraints Constraints { get; }

    /// <summary>What a map may set on this entity. See <see cref="PhysicsConstraintEntityVars"/>.</summary>
    [ScriptVars] public PhysicsConstraintEntityVars Vars = new();

    /// <summary>Fired when the joint breaks or is broken.</summary>
    public readonly Output OnBreak = new();

    /// <summary>
    /// The joint, once built. Setting it also records the raw native id and
    /// kind in <c>[SaveField]</c> leaves: the native joint outlives a reload,
    /// and stock saves the pointer (DEFINE_PHYSPTR m_pConstraint, rebuilt in
    /// Activate, physconstraint.cpp:388). Without them the rebuilt entity's Break
    /// destroyed nothing while still announcing OnBreak.
    /// </summary>
    protected Constraint? Joint
    {
        get => _joint;
        private set
        {
            _joint = value;
            _jointId = value?.Id.Value ?? 0u;
            _jointKind = (int)(value?.Kind ?? default);
        }
    }

    private Constraint? _joint;
    [SaveField] private uint _jointId;
    [SaveField] private int _jointKind;

    /// <summary>The kilograms per pound stock's lbs2kg multiplies by (mathlib.h KG_PER_POUND).</summary>
    public const float KilogramsPerPound = 0.45359237f;

    /// <summary>
    /// The breaking limits as the solver takes them: the map writes pounds and
    /// CPhysConstraint::GetBreakParams converts, params.forceLimit =
    /// lbs2kg( m_forceLimit ) (physconstraint.cpp:379-380; plan_physics_managed
    /// A24). Unconverted, every breakable joint held 2.2 times its limit.
    /// </summary>
    protected float ForceLimitKg => Vars.ForceLimit * KilogramsPerPound;

    /// <inheritdoc cref="ForceLimitKg"/>
    protected float TorqueLimitKg => Vars.TorqueLimit * KilogramsPerPound;

    // The two ends the joint was built between, for the break sound.
    [SaveField] private EntityHandle _a = EntityHandle.None;
    [SaveField] private EntityHandle _b = EntityHandle.None;

    /// <summary>
    /// Builds the joint. Called once both ends have been resolved; either may be
    /// <see cref="EntityHandle.None"/>, meaning the world.
    /// </summary>
    protected abstract Constraint? Build(EntityHandle a, EntityHandle b);

    /// <summary>
    /// CPhysConstraint::Activate, deferred to the first think: the two ends are
    /// named, and a name cannot be resolved while the map is still being
    /// parsed. A constraint that cannot be made is removed, as stock removes
    /// it -- unless it waits for TurnOn (SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED).
    /// </summary>
    public override void OnSpawn() => SetNextThink(0f);

    public override void OnThink()
    {
        StopThinking();

        if (ConstraintFlags.HasFlag(ConstraintSpawnFlag.NoConnectUntilActivated))
            return;

        if (!ActivateConstraint())
            Remove();
    }

    /// <summary>
    /// CPhysConstraint::ActivateConstraint: a joint already made is only set
    /// up again; otherwise both ends are looked up and the joint built.
    /// </summary>
    private bool ActivateConstraint()
    {
        if (Joint is not null)
        {
            OnConstraintSetup();
            return true;
        }

        EntityHandle a = Resolve(Vars.Attach1);
        EntityHandle b = Resolve(Vars.Attach2);

        // Both ends unnamed would constrain the world to itself.
        if (!Entities.IsValid(a) && !Entities.IsValid(b))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' has neither end attached\n");
            return false;
        }

        Joint = Build(a, b);
        if (Joint is null)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' could not be created\n");
            return false;
        }

        (_a, _b) = (a, b);

        // GetBreakParams' isActive.
        if (ConstraintFlags.HasFlag(ConstraintSpawnFlag.StartInactive))
            Joint.Deactivate();

        OnConstraintSetup();
        return true;
    }

    /// <summary>CPhysConstraint::OnConstraintSetup: the pair stops colliding when the map says so.</summary>
    private void OnConstraintSetup()
    {
        if (ConstraintFlags.HasFlag(ConstraintSpawnFlag.DisableCollision))
            Pairs.SetEnabled(_a, _b, false);
    }

    /// <summary>
    /// CPhysConstraint::Deactivate: the joint stops holding, and a pair the map
    /// kept apart collides again.
    /// </summary>
    private void Deactivate()
    {
        if (Joint is null)
            return;

        Joint.Deactivate();

        if (ConstraintFlags.HasFlag(ConstraintSpawnFlag.DisableCollision))
            Pairs.SetEnabled(_a, _b, true);
    }

    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);

        if (_joint is null && _jointId != 0u)
            _joint = Constraint.Adopt(Constraints, new PhysicsId(_jointId), (ConstraintKind)_jointKind);
    }

    private EntityHandle Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
            return EntityHandle.None;

        EntityHandle found = Entities.FindByName(name);
        if (!Entities.IsValid(found))
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': no entity named '{name}'\n");

        return found;
    }

    /// <summary>CPhysConstraint::InputBreak.</summary>
    [ScriptedEvent("Break")]
    protected void OnBreakInput() => OnBroken();

    /// <summary>The joint broke under load (IPhysicsConstraintEvent::ConstraintBroken).</summary>
    public override bool OnPhysicsConstraintBroken(PhysicsId constraint)
    {
        if (Joint is null || Joint.Id != constraint)
            return false;

        OnBroken();
        return true;
    }

    /// <summary>CPhysConstraint::OnBreak: deactivated, the sound between its ends, OnBreak, and gone.</summary>
    private void OnBroken()
    {
        Deactivate();

        if (Vars.BreakSound.Length > 0)
            Sound.EmitSoundAtPosition(Vars.BreakSound, BreakSoundOrigin());

        OnBreak.Fire();
        Remove();
    }

    /// <summary>
    /// CPhysConstraint::OnBreak's origin: halfway between the two ends' objects,
    /// either one alone when the other is the world, the constraint's own
    /// origin when neither is.
    /// </summary>
    private Vector BreakSoundOrigin()
    {
        bool hasA = _a != EntityHandle.None && Entities.IsValid(_a);
        bool hasB = _b != EntityHandle.None && Entities.IsValid(_b);

        if (hasA && hasB)
            return (Entities.GetOrigin(_a) + Entities.GetOrigin(_b)) * 0.5f;
        if (hasA)
            return Entities.GetOrigin(_a);
        if (hasB)
            return Entities.GetOrigin(_b);

        return Origin;
    }

    /// <summary>CPhysConstraint::InputTurnOff: Deactivate, the joint kept.</summary>
    [ScriptedEvent("TurnOff")]
    protected void OnTurnOff() => Deactivate();

    /// <summary>
    /// CPhysConstraint::InputTurnOn: built now if it waited for this, then
    /// holding again, both ends woken.
    /// </summary>
    [ScriptedEvent("TurnOn")]
    protected void OnTurnOn()
    {
        if (ConstraintFlags.HasFlag(ConstraintSpawnFlag.NoConnectUntilActivated))
            ActivateConstraint();

        Joint?.Activate();
    }

    /// <summary>~CPhysConstraint: Deactivate, then the joint goes.</summary>
    public override void OnRemoved()
    {
        Deactivate();
        Joint?.Destroy();
        Joint = null;
    }
}

/// <summary>
/// The map keys <see cref="PhysicsConstraintEntity"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysicsConstraintEntityVars
{
    /// <summary>
    /// Names of the two entities to join. Either may be left empty, which means
    /// the world — that is what a mapper means by a hinge with a single target,
    /// and it is what makes a door swing rather than drift.
    /// </summary>
    public string Attach1 { get; set; } = string.Empty;

    public string Attach2 { get; set; } = string.Empty;

    /// <summary>
    /// What the joint will take before it breaks. Zero is unbreakable, which is
    /// the default and what most maps rely on.
    /// </summary>
    public float ForceLimit { get; set; }

    public float TorqueLimit { get; set; }

    /// <summary>Played between the two objects when the joint breaks.</summary>
    [VarName("breaksound")]
    public string BreakSound { get; set; } = string.Empty;
}
