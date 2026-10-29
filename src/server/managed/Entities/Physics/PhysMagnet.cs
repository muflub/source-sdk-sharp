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
/// phys_magnet: something that keeps what it hits.
///
/// The first entity here that reacts to a physics COLLISION rather than to a
/// touch, and that is the whole reason it needed anything new. A touch is the
/// engine noticing two volumes overlap; a magnet needs to know where the two
/// actually met, because that point is where the joint goes. Weld a crate to a
/// magnet at the magnet's origin and the crate jumps to the middle of it.
///
/// It is also the only entity that builds constraints it did not describe in
/// the map: one per object caught, made on impact and kept until it is turned
/// off. The list is the entity's own state, not a table somewhere, because a
/// magnet that forgets what it is holding cannot let go.
/// </summary>
[ScriptedEntity(EntityNames.PhysMagnet, ScriptedEntityKind.Prop)]
public sealed class PhysMagnet : AnimatingEntity, ISaveCustom
{
    public PhysMagnet(IGameClock clock, IEngineNetworking net, IEngineEntityPhysics physics, IEngineWorldQuery world,
                      IEngineConstraints constraints)
    {
        Clock = clock;
        Constraints = constraints;
        Net = net;
        Physics = physics;
        World = world;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the solver's joint table, which every stuck object is held by.</summary>
    private readonly IEngineConstraints Constraints;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysMagnetVars"/>.</summary>
    [ScriptVars] public PhysMagnetVars Vars = new();

    /// <summary>
    /// phys_magnet's own spawnflags, from physobj.cpp's SF_MAGNET_*.
    ///
    /// <para>
    /// Bits 1 and 2 describe the magnet's own body, bits 4 and 8 describe what it
    /// does to what it catches. Worth naming together because the numbers are
    /// reused wholesale elsewhere -- 1 and 2 mean asleep and debris on
    /// phys_convert, and asleep and motion-disabled here, and only the type says
    /// which word is being read.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        Asleep = 0b0000_0001,
        MotionDisabled = 0b0000_0010,
        Suck = 0b0000_0100,
        AllowRotation = 0b0000_1000,
    }

    /// <summary>Source's kilograms-per-pound, so a mapper's number means what Hammer says.</summary>
    private const float KilogramsPerPound = 1f / 2.2f;

    public readonly Output OnAttach = new();
    public readonly Output OnDetach = new();

    /// <summary>
    /// What it is holding. A plain list of tuples crosses no save tier, so
    /// the class carries it through <see cref="ISaveCustom"/> and re-adopts
    /// each joint by its saved native id on restore — stock's
    /// m_MagnettedEntities FIELD_EMBEDDED with the OnRestore pointer re-wire
    /// (physobj.cpp:1494-1512). The native joints survive a reload; without
    /// this the rebuilt magnet's DetachAll destroyed nothing and every held
    /// object stayed welded with nothing managing it.
    /// </summary>
    private readonly List<(EntityHandle Entity, Constraint Joint)> _held = [];

    /// <summary>Set by TurnOn/TurnOff; stock saves m_bActive (physobj.cpp:1498).</summary>
    [SaveField] private bool _active = true;

    /// <summary>The suck rate limit; stock saves m_flNextSuckTime (physobj.cpp:1506).</summary>
    [SaveField] private float _nextSuckTime;

    /// <summary>What it is holding. Exposed for tests and for debug-api.</summary>
    internal int HeldCount => _held.Count;

    public override void OnSpawn()
    {
        if (Vars.Model.Length > 0)
            SetModel(Vars.Model);

        _active = true;

        // The Prop shell simulates it already, so all that is left is what the
        // map asked for on top — and that has to wait a tick, because the
        // physics object does not exist until after this returns.
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        StopThinking();

        if (Vars.MassScale > 0f)
        {
            float mass = Physics.GetMass(Handle);

            if (mass > 0f)
                Physics.SetMass(Handle, mass * Vars.MassScale);
        }

        // A magnet on a crane is held by the crane, not by the solver.
        if (Flags.HasFlag(SpawnFlag.MotionDisabled))
            EnableMotion(false);
    }

    [ScriptedEvent("TurnOn")] private void OnTurnOn() => _active = true;
    [ScriptedEvent("TurnOff")] private void OnTurnOff() => DetachAll();
    [ScriptedEvent("Toggle")] private void OnToggle()
    {
        if (_active)
            DetachAll();
        else
            _active = true;
    }

    /// <summary>
    /// Whether the solver moves the other side of the collision being handled:
    /// the record's own flag, set for the length of one OnVPhysicsCollision.
    /// Stock welds only when pPhysics->IsMoveable() (physobj.cpp:1705); a
    /// motion-disabled prop still has MOVETYPE_VPHYSICS and the physics state,
    /// so the movetype gate alone welded the magnet to something pinned.
    /// True outside a record (the narrow hook carries no flag).
    /// </summary>
    internal bool OtherMoveable { get; set; } = true;

    public override void OnVPhysicsCollision(in PhysicsEvent ev, int index)
    {
        OtherMoveable = ev.IsMoveable(index == 0 ? 1 : 0);
        try
        {
            base.OnVPhysicsCollision(in ev, index);
        }
        finally
        {
            OtherMoveable = true;
        }
    }

    public override void OnPhysicsCollision(EntityHandle other, Vector contact, float speed,
                                            SurfaceMaterial surfaceMaterial,
                                            SurfaceMaterial modelMaterial)
    {
        if (!Entities.IsValid(other))
            return;

        // Before every pickup gate, as stock's VPhysicsCollision does
        // (DoMagnetSuck ahead of m_bActive/max/material, physobj.cpp:1641-1644):
        // a suck magnet resting on something it cannot hold still drags loose
        // metal up. Suck keeps its own active/flag/rate gates.
        Suck(other);

        if (!_active)
            return;

        if (Vars.MaxObjects > 0 && _held.Count >= Vars.MaxObjects)
            return;

        if (!Sticks(surfaceMaterial, modelMaterial))
            return;

        var state = Net.EntityGetState(other, out MoveType moveType);

        // Only something the solver is actually moving. Welding to a brush or to
        // a motion-disabled prop would pin the MAGNET instead, which looks like
        // the magnet having jammed.
        if (moveType != MoveType.VPhysics || (state & EntityState.Physics) == 0 || !OtherMoveable)
            return;

        if (Holds(other))
            return;

        Constraint? joint = Flags.HasFlag(SpawnFlag.AllowRotation)
            ? Constraint.BallSocket(Constraints, Handle, other, contact,
                                    ForceLimitKg, TorqueLimitKg)
            : Constraint.Fixed(Constraints, Handle, other, ForceLimitKg, TorqueLimitKg);

        if (joint is null)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': could not hold onto {Entities.GetClassName(other)}\n");
            return;
        }

        _held.Add((other, joint));
        OnAttach.Fire();
    }

    /// <summary>
    /// Drags loose metal underneath the magnet up towards it.
    ///
    /// Only ever from a collision, which is stock's design and worth keeping
    /// straight: the magnet does not reach out for things, it grabs more once
    /// something has already landed on it. A crane magnet dropped into a pile
    /// of scrap comes up with an armful rather than with one piece.
    ///
    /// The sphere hangs 96 units BELOW the magnet along its own down axis, so a
    /// magnet mounted sideways sucks sideways.
    /// </summary>
    private void Suck(EntityHandle justCaught)
    {
        if (!Flags.HasFlag(SpawnFlag.Suck) || !_active)
            return;

        // Rate-limited rather than per-collision: a magnet resting in scrap
        // collides constantly, and every one of those would be a radius query.
        if (Clock.Now < _nextSuckTime)
            return;

        _nextSuckTime = Clock.Now + SuckInterval;

        Vector down = Angles.Vectors(Entities.GetAngles(Handle)).Up * -1f;
        Vector origin = Origin;
        Vector centre = origin + down * 96f;
        Vector towards = origin + down * 64f;

        Span<EntityHandle> found = stackalloc EntityHandle[20];
        int count = World.EntitiesInSphere(centre, SuckRadius, found);

        for (int i = 0; i < count; i++)
        {
            EntityHandle target = found[i];

            if (target == justCaught || target == Handle || !Entities.IsValid(target))
                continue;

            Net.EntityGetState(target, out MoveType moveType);

            if (moveType != MoveType.VPhysics)
                continue;

            // Too heavy to drag is not the same as too heavy to hold: something
            // this big would tow the magnet instead.
            float mass = Physics.GetMass(target);

            if (mass <= 0f || mass >= MaxSuckMass)
                continue;

            Vector at = Entities.GetOrigin(target);

            // Through a floor is not through a magnet.
            var trace = World.TraceLine(origin, at, TraceMask.Shot, Handle);

            if (trace.Hit && trace.Entity != target)
                continue;

            // Scaled by mass, which reads backwards and is stock's: a heavier
            // object is pulled FASTER. Kept because a mapper's magnet behaves
            // the way their magnet behaved.
            Entities.ApplyVelocityImpulse(target, (towards - at).Normalized * (5f * mass), false);
        }
    }

    /// <summary>Seconds between sucks. Stock's, and it is a rate limit rather than a rhythm.</summary>
    private const float SuckInterval = 2f;

    private const float SuckRadius = 80f;
    private const float MaxSuckMass = 5000f;

    private float ForceLimitKg => Vars.ForceLimit * KilogramsPerPound;
    private float TorqueLimitKg => Vars.TorqueLimit * KilogramsPerPound;

    /// <summary>
    /// Whether the magnet holds on to this.
    ///
    /// Metal, by either account. The two can disagree — a metal model with a
    /// painted face reports paint for the surface struck and metal for the
    /// model's own default — and stock accepts either, which is what makes a
    /// magnet feel like it grabs the OBJECT rather than the texture.
    /// </summary>
    internal static bool Sticks(SurfaceMaterial surfaceMaterial, SurfaceMaterial modelMaterial)
        => IsMetal(surfaceMaterial) || IsMetal(modelMaterial);

    private static bool IsMetal(SurfaceMaterial material)
        => material is SurfaceMaterial.Metal or SurfaceMaterial.Computer;

    private bool Holds(EntityHandle entity)
    {
        foreach (var (held, _) in _held)
        {
            if (held == entity)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Lets go of everything, which is what turning a magnet off means.
    ///
    /// Turning off does not merely stop it CATCHING things — a magnet that kept
    /// its load after being switched off would be a magnet nothing could ever
    /// put down.
    /// </summary>
    private void DetachAll()
    {
        _active = false;

        if (_held.Count == 0)
            return;

        foreach (var (entity, joint) in _held)
        {
            joint.Destroy();

            // Woken deliberately, which stock does not do. Destroying a joint
            // does not wake what it held, so a magnet that let go of something
            // already asleep left it hanging in mid-air -- measured, not
            // guessed. It never showed in stock because a magnet is usually on
            // a crane, and a moving magnet keeps its load awake.
            if (Entities.IsValid(entity))
                Physics.EnableMotion(entity, true);
        }

        _held.Clear();
        OnDetach.Fire();
    }

    /// <summary>
    /// The solver broke one of the joints this magnet is holding.
    ///
    /// Stock's CPhysMagnet::ConstraintBroken (physobj.cpp:1849-1872) finds the
    /// entry whose constraint is the broken one, removes it, fires
    /// m_OnMagnetDetach and destroys the constraint. Without this the entry
    /// stays in <c>_held</c> forever: Holds() keeps answering true so the
    /// object can never be re-caught, every break permanently consumes one of
    /// maxobjects, OnDetach never fires for a break, and the dead native
    /// entry leaks.
    /// </summary>
    public override bool OnPhysicsConstraintBroken(PhysicsId constraint)
    {
        for (int i = 0; i < _held.Count; i++)
        {
            if (_held[i].Joint.Id != constraint)
                continue;

            var (entity, joint) = _held[i];
            _held.RemoveAt(i);

            joint.Destroy();

            // Woken like DetachAll does: the joint is gone and the object may
            // have fallen asleep welded, so it would hang where the break
            // left it.
            if (Entities.IsValid(entity))
                Physics.EnableMotion(entity, true);

            OnDetach.Fire();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        foreach (var (entity, joint) in _held)
        {
            writer.AddRow()
                  .Set("e", entity)
                  .Set("j", joint.Id.Value.ToString(ReloadRecord.Culture))
                  .Set("k", (int)joint.Kind);
        }
    }

    /// <inheritdoc/>
    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        // The reload keeps the native world standing, so each saved joint is
        // still holding — stock re-wires the same pointers in OnRestore
        // (physobj.cpp:1494-1512). Re-adopting rather than re-making is what
        // keeps TurnOff working: a re-Create would weld at the CURRENT pose
        // and strand the old joint where no managed code could destroy it.
        // A joint the level change already dropped needs no holder either:
        // Constraint.IsValid asks native, and DetachAll's Destroy on a dead
        // id is the ordinary already-destroyed case.
        _held.Clear();

        foreach (ReloadRow row in reader.Rows)
        {
            EntityHandle entity = reader.Resolve(row.GetHandle("e"));

            // The id is uint text, like ReloadRow's handles: an id's serial
            // reaches bit 31, which an int reading would lose.
            var id = new PhysicsId(uint.TryParse(row.GetString("j"),
                                                 System.Globalization.NumberStyles.Integer,
                                                 ReloadRecord.Culture, out uint raw)
                                       ? raw : 0u);

            if (id.IsNone || entity == EntityHandle.None)
                continue;

            _held.Add((entity, Constraint.Adopt(Constraints, id, (ConstraintKind)row.GetInt("k"))));
        }
    }

    public override void OnRemoved() => DetachAll();
}

/// <summary>
/// The map keys <see cref="PhysMagnet"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysMagnetVars
{
    public string Model { get; set; } = string.Empty;

    /// <summary>Multiplies the mass the model was compiled with.</summary>
    public float MassScale { get; set; }

    /// <summary>How many objects it will hold. Zero is unlimited.</summary>
    public int MaxObjects { get; set; }

    /// <summary>
    /// What each joint takes before it lets go, in POUNDS — which is what
    /// Hammer asks a mapper for, and why they are converted before use.
    /// </summary>
    public float ForceLimit { get; set; }

    public float TorqueLimit { get; set; }
}
