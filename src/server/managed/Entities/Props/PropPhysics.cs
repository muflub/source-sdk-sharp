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
/// The physics props, in C#.
///
/// A prop is the first entity here that the engine MOVES: it falls, it is
/// pushed, it has mass and momentum. None of that simulation is managed — C#
/// never integrates anything — and the division is worth stating because it is
/// the one that keeps this affordable. VPhysics owns motion; C# owns everything
/// around it: how much health the thing has, what damage it accepts, whether it
/// responds at all, and what happens when it breaks.
///
/// This is also the most-placed entity in a real map. dm_lockdown has a hundred
/// of them, which makes it the first port where per-entity cost is worth
/// thinking about — hence nothing here runs per frame.
/// </summary>
[ScriptedEntity(EntityNames.PropPhysics, ScriptedEntityKind.Prop)]
// The SDK collapses these onto one class at spawn; they are the same entity
// under names that differ for the editor's benefit.
[ScriptedEntity(EntityNames.PhysicsProp, ScriptedEntityKind.Prop)]
[ScriptedEntity(EntityNames.PropPhysicsOverride, ScriptedEntityKind.Prop)]
public class PropPhysics : ScriptedEntity, IPhysGunTarget
{
    public PropPhysics(IEngineDamage damage, IEngineEntityPhysics physics, IEngineWorldQuery world,
                       IEngineEffects effects, IEnginePrecache precache, IEngineHost host, IBlastSweep blast, IServerDecals decals,
                       IEngineEntityRender render, IServerPropData propData, PhysicsDamageQueue impacts,
                       FluidTouches fluid, PhysicsSoundBatch sounds)
    {
        Sounds = sounds;
        Impacts = impacts;
        Fluid = fluid;
        Render = render;
        PropDataTable = propData;
        Damage = damage;
        Physics = physics;
        World = world;
        Effects = effects;
        Precache = precache;
        Host = host;
        Blast = blast;
        Decals = decals;
    }

    /// <summary>
    /// The managed blast traversal — the falloff, the line of sight and the
    /// blocking rule that used to run inside <c>CGameRules::RadiusDamage</c>.
    /// See <see cref="BlastSweep"/>.
    /// </summary>
    private readonly IBlastSweep Blast;

    /// <summary>the decal system, whose "Scorch" group the blast paints from.</summary>
    private readonly IServerDecals Decals;

    /// <summary>The model's $keyvalues text, for its prop data.</summary>
    private readonly IEngineEntityRender Render;

    /// <summary>propdata.txt, which a model's prop_data names as its base.</summary>
    private readonly IServerPropData PropDataTable;

    /// <summary>tracing, and what is at a point. For the blast.</summary>
    protected readonly IEngineWorldQuery World;

    /// <summary>the fireball, the scorch mark and the sparks.</summary>
    protected readonly IEngineEffects Effects;

    /// <summary>Getting the blast's sprite and decal onto the client, and
    /// asking a model what it is made of before the level runs. Two lanes
    /// added this field independently -- explosions and break pieces -- and
    /// the compiler caught the duplicate at merge.</summary>
    protected readonly IEnginePrecache Precache;

    /// <summary>the engine's random stream, which the blast's sparks draw from.</summary>
    protected readonly IEngineHost Host;

    /// <summary>hurting things.</summary>
    protected readonly IEngineDamage Damage;

    /// <summary>The step's impact damage, delivered after it (PhysCallbackDamage).</summary>
    protected readonly PhysicsDamageQueue Impacts;

    /// <summary>Whether this prop is in fluid and floats (IsFloating, I2).</summary>
    protected readonly FluidTouches Fluid;

    /// <summary>this entity as the solver sees it.</summary>
    protected readonly IEngineEntityPhysics Physics;

    /// <summary>The queued impact and break sounds (CPhysicsHook's batch, managed).</summary>
    protected readonly PhysicsSoundBatch Sounds;

    private PropBreak? _breaker;

    /// <summary>
    /// What this prop breaks into, and how to make it.
    ///
    /// <para>
    /// Built on first use rather than in the constructor, and that is not
    /// laziness for its own sake: <see cref="ScriptedEntity.Entities"/> is
    /// BOUND after construction, so a breaker made in the constructor would
    /// hold the default binding — the real engine — however the entity was
    /// wired. In a test that means an engine that answers zero to everything,
    /// silently, which is the one thing the double exists to prevent.
    /// </para>
    /// </summary>
    private PropBreak Breaker => _breaker ??= new PropBreak(Log, Entities, Physics, Precache);
    public readonly Output OnBreak = new();
    public readonly Output OnHealthChanged = new();
    public readonly Output OnMotionEnabled = new();

    /// <summary>
    /// A prop that started asleep woke for the first time. Stock fires it from
    /// CPhysicsProp::VPhysicsUpdate (props.cpp:2915) and clears the flag.
    /// </summary>
    public readonly Output OnAwakened = new();

    /// <summary>
    /// The prop is outside the world or moving faster than stock allows:
    /// CBaseEntity::IsInWorld, checked on every VPhysicsUpdate (props.cpp:2932).
    /// </summary>
    public readonly Output OnOutOfWorld = new();

    /// <summary>What a map may set on this entity. See <see cref="PropPhysicsVars"/>.</summary>
    [ScriptVars] public PropPhysicsVars Vars = new();

    /// <summary>
    /// Motion disabled: the map pins this prop in place. Still solid, still
    /// collided with, simply never simulated — how a mapper bolts a crate down
    /// without making it part of the world.
    /// </summary>
    private const int MotionDisabled = 8;

    /// <summary>
    /// SF_PHYSPROP_PREVENT_PLAYER_TOUCH_ENABLE: a player bumping the prop does
    /// not knock a forcetoenablemotion prop loose.
    /// </summary>
    private const int PreventPlayerTouchEnable = 0x400;

    /// <summary>SF_PHYSPROP_START_ASLEEP: the prop is put to sleep when it spawns.</summary>
    private const int StartAsleep = 1;

    /// <summary>SF_PHYSPROP_ENABLE_ON_PHYSCANNON: pinned, until the gravity gun takes it.</summary>
    private const int EnableOnPhyscannon = 0x40;

    /// <summary>SF_PHYSPROP_PREVENT_PICKUP: neither +use nor the gravity gun may pick it up.</summary>
    private const int PreventPickup = 0x200;

    /// <summary>FVPHYSICS_WAS_THROWN: what a launch from the gun marks the object with.</summary>
    private const uint WasThrown = 0x0100;

    /// <summary>SF_PHYSPROP_DEBRIS (props_shared.h).</summary>
    private const int DebrisFlag = 4;

    /// <summary>
    /// SF_PHYSPROP_DONT_TAKE_PHYSICS_DAMAGE. Prop data that gives the prop
    /// health clears it (props_shared.cpp:357).
    /// </summary>
    private const int DontTakePhysicsDamage = 2;

    /// <summary>m_impactEnergyScale's default when the map gives none (props.cpp:903).</summary>
    private const float DefaultImpactEnergyScale = 0.1f;

    private bool _broken;
    private bool _settled;
    private int _startingHealth;

    /// <summary>How big a bang this model makes when it breaks, if any.</summary>
    private float _explosiveDamage;
    private float _explosiveRadius;
    private bool _propDataRead;

    /// <summary>
    /// How many pieces this model breaks into — stock's
    /// <c>m_iNumBreakableChunks</c>, filled at the same moment stock fills it.
    ///
    /// <para>
    /// It is not only the loop count. It is also what tells a GIB whether it may
    /// break again: stock refuses damage to a fading piece that has no pieces of
    /// its own, so a splinter cannot be shot into smaller splinters forever.
    /// </para>
    /// </summary>
    private int _breakPieces;

    /// <summary>What the piece count says, for tests, which have no models.</summary>
    internal int BreakPieceCount => _breakPieces;

    /// <summary>
    /// What the settle armed the next think for, or -1 if it stopped thinking.
    ///
    /// <para>
    /// Exposed for tests for the same reason <see cref="SettleCount"/> is:
    /// SetNextThink is a <see cref="ScriptedEntity"/> convenience that goes
    /// through the STATIC engine, so a test's double never sees it and a prop
    /// that stopped thinking when it should have armed its end looks exactly
    /// like one that armed it.
    /// </para>
    /// </summary>
    internal float ThinkArmed { get; private set; } = -1f;

    /// <summary>Whether this prop has taken itself out of the world. Same reason.</summary>
    internal bool Gone { get; private set; }

    /// <summary>
    /// Out of the world, and said so where a test can see it.
    /// </summary>
    private void Vanish()
    {
        Gone = true;
        Remove();
    }

    public override void OnSpawn()
    {
        if (Vars.Model.Length > 0)
            SetModel(Vars.Model);

        // CPhysicsProp::Spawn (props.cpp:2497-2500; A32): the debris flag puts the
        // prop in COLLISION_GROUP_DEBRIS before its object is made (the shell
        // makes it after this returns). The force-touch-triggers variant,
        // COLLISION_GROUP_DEBRIS_TRIGGER, is not a group this side names yet.
        if ((SpawnFlags & DebrisFlag) != 0)
            Physics.SetCollisionGroup(Handle, CollisionGroup.Debris);

        // What this prop breaks into, precached HERE because here is inside the
        // level-load window. Stock says the same thing in the same place —
        // CBreakableProp::Precache is where m_iNumBreakableChunks comes from —
        // and it has to be here: a piece is created when something breaks, by
        // which time the map is long since built and a precache is a console
        // line and nothing drawn.
        //
        // By NAME, not through this entity, which is what makes it possible at
        // all: the pieces are read out of the model file, and a piece's own
        // pieces are read by recursing on a name with no entity behind it.
        if (Vars.Model.Length > 0)
            _breakPieces = Breaker.Precache(Vars.Model);

        // Health is NOT resolved here, and the reason is worth stating: SetModel
        // above only records a pending name, which the shell applies after this
        // returns. For the whole of OnSpawn this prop has no model, so there is
        // nothing to read prop data out of yet.
        if (Vars.Health > 0)
            Health = Vars.Health;

        // Mass and motion cannot be set here for the same reason health cannot
        // be read here: the shell creates the physics object AFTER this returns,
        // so during OnSpawn there is nothing to set them on.
        SetNextThink(0f);
    }

    /// <summary>
    /// Whether this prop is one of the pieces something else broke into.
    ///
    /// <para>
    /// The map key is SourceSharp's own and is how a piece is told what it is.
    /// It has to arrive as a KEY rather than as a call after the spawn, because
    /// what it decides — how long the piece lasts, and whether it can be broken
    /// again — is decided during the spawn.
    /// </para>
    /// </summary>
    protected bool IsGib => Vars.GibFadeTime > 0f;

    /// <summary>
    /// The health this prop starts with, resolved on first use.
    ///
    /// The model, not the map, is where a prop's health usually comes from: no
    /// map gives an explosive barrel a "health" key, and its health, damage
    /// modifiers and explosion are all compiled into the model and resolved
    /// through scripts/propdata.txt. Reading only map keys made every barrel in
    /// dm_lockdown scenery, and scenery ignores damage entirely.
    ///
    /// Lazy rather than eager because the model is not set until after OnSpawn.
    /// </summary>
    private int StartingHealth
    {
        get
        {
            if (_propDataRead)
                return _startingHealth;

            _propDataRead = true;

            // The model's own prop_data over propdata.txt's base chain, parsed in
            // C# (plan_server_novalve.md P2.4; SourceSharp_EntityGetPropData
            // parsed it with tier1's KeyValues).
            int fromModel = PropDataTable.ModelPropData(Render.GetModelKeyValueText(Render.GetModelName(Handle)),
                                                        out _explosiveDamage, out _explosiveRadius);

            // A map key still wins where there is one — that is the point of
            // being able to write it.
            _startingHealth = Vars.Health > 0 ? Vars.Health : fromModel;
            return _startingHealth;
        }
    }

    /// <summary>
    /// Mass and motion, a tick after spawn.
    ///
    /// Split from <see cref="OnTimer"/> because a subclass wanting a timer of
    /// its own must not lose this: prop_physics_respawnable schedules a think
    /// to come back, and if that were the same method the first tick of every
    /// respawnable prop in the map would count as a respawn.
    /// </summary>
    public override void OnThink()
    {
        if (!_settled)
        {
            _settled = true;
            SettleCount++;

            // A gib keeps the think it already had rather than stopping: the
            // settle is what was scheduled, and stopping here would leave
            // nothing to bring its end round. Every other prop stops, because
            // a prop that thinks forever is fifty per-frame callbacks in a map
            // full of crates.
            if (IsGib)
            {
                ThinkArmed = Vars.GibFadeTime;
                SetNextThink(Vars.GibFadeTime);
            }
            else
            {
                ThinkArmed = -1f;
                StopThinking();
            }

            return;
        }

        if (IsGib)
        {
            Vanish();
            return;
        }

        OnTimer();
    }

    /// <summary>Whether the one-off settle has run. For tests, which have no engine.</summary>
    internal bool HasSettled => _settled;

    /// <summary>How many times it ran. Should never exceed one.</summary>
    internal int SettleCount { get; private set; }

    /// <summary>A think that is not the one-off settle. Empty here by design.</summary>
    protected virtual void OnTimer() { }

    /// <summary>
    /// What the map asked for that only a physics object can answer.
    ///
    /// Both of these were read into fields that nothing looked at, so every
    /// prop_physics in every map was fully simulated at its compiled mass
    /// whatever the map said. It looked like nothing: a pinned crate simply
    /// fell twenty units and settled, which reads as a crate on the floor.
    /// </summary>
    private void ApplySpawnPhysics()
    {
        if (Vars.MassScale > 0f)
        {
            float mass = Physics.GetMass(Handle);

            if (mass > 0f)
                Physics.SetMass(Handle, mass * Vars.MassScale);
        }

        // CreateVPhysics: a prop that needs a force to move starts pinned too.
        // SF_PHYSPROP_MOTIONDISABLED, forcetoenablemotion or damagetoenablemotion
        // (props.cpp:2585).
        if ((SpawnFlags & MotionDisabled) != 0 || Vars.ForceToEnableMotion > 0f || Vars.DamageToEnableMotion > 0)
        {
            Physics.EnableMotion(Handle, false);
            _motionDisabled = true;
        }
    }

    /// <summary>
    /// CPhysicsProp::EnableMotion: let the object move, wake it, and say so.
    /// </summary>
    // Pinned (motion disabled) since the object was made and not yet let go:
    // what stock asks as !VPhysicsGetObject()->IsMoveable().
    private bool _motionDisabled;

    // The last blow taken, for Event_Killed's push of a pinned prop.
    private (DamageType Type, Vector Position, Vector Force) _lastBlow;

    private void EnableMotion()
    {
        _motionDisabled = false;
        Physics.EnableMotion(Handle, true);
        Physics.Wake(Handle);
        OnMotionEnabled.Fire();
    }

    // Through the INJECTED physics area rather than ScriptedEntity's protected
    // helper. It is the same engine call either way, and the reason to say it
    // this way is that the helper is a static crossing nothing can observe: a
    // test asserting "this input did NOT enable motion" against the helper is
    // a check that cannot fail, which is what the Wake/Sleep pair below was
    // mis-spelled as in the first place.
    [ScriptedEvent] private void OnEnableMotion() => EnableMotion();

    [ScriptedEvent] private void OnDisableMotion() => Physics.EnableMotion(Handle, false);

    //-------------------------------------------------------------------------
    // The gravity gun (CPhysicsProp's IPlayerPickupVPhysics, props.cpp).
    //-------------------------------------------------------------------------

    /// <summary>SF_PHYSPROP_PREVENT_PICKUP: stock sets FVPHYSICS_NO_PLAYER_PICKUP from it at creation.</summary>
    bool IPhysGunTarget.PreventsPickup => (SpawnFlags & PreventPickup) != 0;

    /// <summary>A pinned prop the gun may take: SF_PHYSPROP_ENABLE_ON_PHYSCANNON.</summary>
    bool IPhysGunTarget.EnablesOnPhyscannon => (SpawnFlags & EnableOnPhyscannon) != 0;

    /// <summary>
    /// CPhysicsProp::OnPhysGunPickup: a pinned prop comes loose only if its
    /// map said the gun may free it.
    /// </summary>
    void IPhysGunTarget.OnPhysGunPickup(EntityHandle player, PhysGunPickup reason)
    {
        if (!_motionDisabled)
            return;

        if ((SpawnFlags & EnableOnPhyscannon) == 0)
            return;

        EnableMotion();
    }

    /// <summary>CPhysicsProp::OnPhysGunDrop: a launch marks the object thrown.</summary>
    void IPhysGunTarget.OnPhysGunDrop(EntityHandle player, PhysGunDrop reason)
    {
        if (reason == PhysGunDrop.LaunchedByCannon)
            Physics.AddGameFlags(Handle, WasThrown);
    }

    [ScriptedEvent] private void OnSetHealth(float health)
    {
        _startingHealth = Math.Max(1, (int)health);
        Health = _startingHealth;
    }

    [ScriptedEvent("Break")] private void OnBreakInput() => Break();

    /// <summary>
    /// Stock's CPhysicsProp::CreateVPhysics creates a SF_PHYSPROP_START_ASLEEP
    /// prop's object asleep (props.cpp:2547). The shell makes the object after
    /// OnSpawn and tells the prop before the solver has run, so sleeping it here
    /// is the same thing.
    /// </summary>
    public override void OnPhysicsCreated()
    {
        // CPhysicsProp::CreateVPhysics (props.cpp:2544-2622; A33): mass scale and
        // the pin are applied as the object is made, before it is ever
        // simulated. Once: a recreated object must not compound the scale.
        if (!_spawnPhysicsApplied)
        {
            _spawnPhysicsApplied = true;

            // CBreakableProp spawns with its health in m_iHealth: the map's key,
            // or else its model's prop_data. Only the key reached the engine
            // before (OnSpawn cannot read the model, which the shell applies
            // after it returns), so a crate whose health came from its model sat
            // at 0 and broke on the first point of damage. Here the model is in
            // place and nothing has been able to hurt the prop yet.
            int health = StartingHealth;
            if (health > 0)
                Health = health;

            ApplySpawnPhysics();
        }

        if ((SpawnFlags & StartAsleep) != 0)
            Physics.Sleep(Handle);
    }

    private bool _spawnPhysicsApplied;

    /// <summary>
    /// CPhysicsProp::VPhysicsUpdate (props.cpp:2917): the first time a
    /// start-asleep prop is awake it fires OnAwakened and drops the flag, so it
    /// fires once.
    /// </summary>
    /// <summary>
    /// CPhysicsProp::VPhysicsUpdate's last check (props.cpp:2932): outside the
    /// world, or too fast to be in it, says so every frame it is.
    /// </summary>
    public override void OnPhysicsUpdate()
    {
        if (!IsInWorld(Entities.GetOrigin(Handle), Entities.GetVelocity(Handle)))
            OnOutOfWorld.Fire(Handle);
    }

    /// <summary>
    /// CBaseEntity::IsInWorld (baseentity.cpp): every origin axis strictly
    /// inside +/-MAX_COORD_INTEGER and every velocity axis strictly under 2000.
    /// </summary>
    public static bool IsInWorld(Vector origin, Vector velocity)
    {
        const float MaxCoord = 16384f;
        const float MaxSpeed = 2000f;

        return origin.X < MaxCoord && origin.Y < MaxCoord && origin.Z < MaxCoord
            && origin.X > -MaxCoord && origin.Y > -MaxCoord && origin.Z > -MaxCoord
            && velocity.X < MaxSpeed && velocity.Y < MaxSpeed && velocity.Z < MaxSpeed
            && velocity.X > -MaxSpeed && velocity.Y > -MaxSpeed && velocity.Z > -MaxSpeed;
    }

    /// <summary>
    /// CPhysicsProp::VPhysicsCollision's damage half (props.cpp:3026; physics
    /// stock parity C6): CalculateDefaultPhysicsDamage with this prop's energy
    /// scale and static damage allowed, from the entity that hit it, through
    /// the damage pipeline. Delivered after the step (PhysicsEventRouter).
    ///
    /// Stated: no damage force is applied, because the solver already moved
    /// both objects for this impact and TakeDamage's force is a blast
    /// direction; and the world as the attacker is None, not worldspawn. The
    /// prop data's damage_table (glass) is not read yet, so every prop uses
    /// the NPC table, stock's default.
    /// </summary>
    public override void OnVPhysicsCollision(in PhysicsEvent ev, int index)
    {
        base.OnVPhysicsCollision(in ev, index);

        if (Vars.ForceToEnableMotion > 0f)
            KnockLoose(in ev, index);

        if ((SpawnFlags & DontTakePhysicsDamage) != 0 && StartingHealth <= 0)
            return;

        float energyScale = Vars.PhysDamageScale > 0f ? Vars.PhysDamageScale : DefaultImpactEnergyScale;
        uint otherRaw = index == 0 ? ev.Entity1 : ev.Entity0;
        var other = otherRaw == PhysicsEvent.NoEntity ? EntityHandle.None : new EntityHandle(otherRaw);
        var parties = new ImpactParties(
            SelfIsPlayer: false,
            OtherIsPlayer: other != EntityHandle.None && Entities.IsPlayer(other),
            OtherIsWorld: other == EntityHandle.None,
            SelfIsFloating: Fluid.IsFloating(Handle));

        float damage = PhysicsImpactDamage.Calculate(in ev, index, ImpactDamageTables.Npc, energyScale,
                                                     allowStaticDamage: true, damageFromHeldObjects: false,
                                                     parties, out DamageType damageType);
        if (damage > 0f)
            Impacts.Add(Handle, other, damage, damageType, in ev, index);
    }

    public override void OnPhysicsWake()
    {
        int flags = SpawnFlags;
        if ((flags & StartAsleep) == 0)
            return;

        OnAwakened.Fire(Handle);
        Entities.SetSpawnFlags(Handle, flags & ~StartAsleep);
    }

    //-------------------------------------------------------------------------
    // Awake and asleep, WHICH IS NOT MOTION ENABLE.
    //
    // Stock is two lines and neither touches motion enable:
    //
    //     void CPhysicsProp::InputWake( inputdata_t & )   // props.cpp:2707
    //     { ... pPhysicsObject->Wake(); }
    //     void CPhysicsProp::InputSleep( inputdata_t & )  // props.cpp:2719
    //     { ... pPhysicsObject->Sleep(); }
    //
    // These used to be EnableMotion(true) and EnableMotion(false), which is a
    // different thing in both directions. EnableMotion(false) pins the object:
    // IsMoveable() goes false, so nothing can ever shove the prop awake again
    // AND the player's shadow controller clamps the player's own
    // shadow velocity to zero against it — a crate sent to Sleep became a wall.
    // EnableMotion(true) went the other way and unpinned a prop the map had
    // deliberately disabled.
    //-------------------------------------------------------------------------
    // Through the INJECTED physics area rather than the ScriptedEntity helper,
    // so a test with a recording engine can see which of the two calls this
    // actually makes. That is the whole defect: the two are indistinguishable
    // from the outside until one of them pins the prop.
    [ScriptedEvent] private void OnWake() => Physics.Wake(Handle);
    [ScriptedEvent] private void OnSleep() => Physics.Sleep(Handle);

    //-------------------------------------------------------------------------
    // Damage.
    //
    // A prop with no health key is scenery: it ignores damage entirely rather
    // than accumulating it invisibly. Saying so here beats letting health go
    // negative on something that can never break.
    //-------------------------------------------------------------------------
    /// <summary>
    /// forcetoenablemotion's collision arm (physics stock parity C8): once the
    /// other object's mass x collision speed reaches the key, the prop is let
    /// go, pushed with the other's momentum at the contact
    /// (ComputeEnablingImpulse), and the key is spent. A player does not count
    /// when the map sets SF_PHYSPROP_PREVENT_PLAYER_TOUCH_ENABLE.
    ///
    /// Stated: the other's mass centre is its world-space centre, the only one
    /// the entity area answers; stock uses vphysics' own. And the key's damage
    /// arm (OnTakeDamage: a damage FORCE over the key) is not here, because
    /// OnDamaged is not handed the force.
    /// </summary>
    private void KnockLoose(in PhysicsEvent ev, int index)
    {
        int otherSide = 1 - index;
        uint otherRaw = otherSide == 0 ? ev.Entity0 : ev.Entity1;
        var other = otherRaw == PhysicsEvent.NoEntity ? EntityHandle.None : new EntityHandle(otherRaw);

        if (other != EntityHandle.None && Entities.IsPlayer(other)
            && (SpawnFlags & PreventPlayerTouchEnable) != 0)
            return;

        float otherMass = otherSide == 0 ? ev.Mass0 : ev.Mass1;
        if (ev.Speed * otherMass < Vars.ForceToEnableMotion)
            return;

        // The surface speed of the object that hit us at the contact: its
        // velocity plus w x r, w turned from its own frame into the world's.
        Vector contactVelocity = ev.PreVelocity(otherSide);
        if (other != EntityHandle.None)
        {
            Vector angles = Entities.GetAngles(other);
            Vector spin = Transform.FromOriginAngles(Vector.Zero, new QAngle(angles.X, angles.Y, angles.Z))
                                   .TransformPoint(ev.PreAngularVelocity(otherSide) * (MathF.PI / 180f));
            Vector r = ev.Point - Entities.GetWorldSpaceCenter(other);
            contactVelocity += new Vector(spin.Y * r.Z - spin.Z * r.Y,
                                          spin.Z * r.X - spin.X * r.Z,
                                          spin.X * r.Y - spin.Y * r.X);
        }

        Physics.ApplyForceOffset(Handle, contactVelocity * otherMass, ev.Point);
        EnableMotion();
        Vars.ForceToEnableMotion = 0f;
    }

    /// <summary>
    /// CBreakableProp::OnTakeDamage reaches CBaseEntity::OnTakeDamage, and so
    /// its physics push, for every prop that takes the blow at all -- a gib or
    /// an unbreakable prop (DAMAGE_EVENTS_ONLY) included (plan_physics_managed
    /// A30). Its refusals before the base (minhealthdmg, the damage filter)
    /// are not ported yet (A38).
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        // CBreakableProp::OnTakeDamage: under minhealthdmg the blow is refused
        // before the base, so it neither hurts nor pushes (props.cpp:1045; A38).
        if (damage < Vars.MinHealthDamage)
            return 0;

        VPhysicsTakeDamage(damageType, position, force);
        _lastBlow = (damageType, position, force);
        int taken = OnDamaged(attacker, damage, damageType);

        // CPhysicsProp::OnTakeDamage (props.cpp:3122-3150). A damage FORCE past
        // forcetoenablemotion lets the prop go, once.
        if (Vars.ForceToEnableMotion > 0f && force.Length >= Vars.ForceToEnableMotion)
        {
            EnableMotion();
            Vars.ForceToEnableMotion = 0f;
        }

        // A blow that leaves health under damagetoenablemotion lets it go, once,
        // and pushes it with the blow now that it can move.
        if (Vars.DamageToEnableMotion > 0 && Health - taken < Vars.DamageToEnableMotion)
        {
            Vars.DamageToEnableMotion = 0;
            EnableMotion();
            VPhysicsTakeDamage(damageType, position, force);
        }

        return taken;
    }

    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        // A gib that breaks into nothing takes no damage. Stock's
        // BreakModelCreateSingle says this as
        //
        //     if ( pProp && !pProp->GetNumBreakableChunks() && m_takedamage == DAMAGE_YES )
        //         pProp->m_takedamage = DAMAGE_EVENTS_ONLY;
        //
        // and without it a splinter is a breakable prop with one health that
        // vanishes on the first pellet — fifty entities' worth of debris that
        // evaporates the moment anyone shoots near it.
        if ((IsGib && _breakPieces == 0) || _broken || StartingHealth <= 0)
        {
            // Frozen debris crushed under a heavier object (ShouldFreezeObject,
            // physics stock parity G). Stock: a prop gib that cannot be damaged
            // is removed -- "it's always safe to delete gibs, so kill this one to
            // avoid simulation problems". Anything else would get an entity
            // solver, which is deferred.
            if (IsGib && (damageType & DamageType.Crush) != 0)
                Entities.Remove(Handle);

            return 0;
        }

        int taken = (int)damage;
        if (taken <= 0)
            return 0;

        OnHealthChanged.Fire(Math.Max(0, Health - taken) / (float)StartingHealth);
        return taken;
    }

    /// <summary>
    /// CBreakableProp's physdamagescale input: m_impactEnergyScale, which the
    /// next impact reads (props.cpp:775; A38).
    /// </summary>
    [ScriptedEvent("physdamagescale")]
    internal void OnPhysDamageScaleInput(float scale) => Vars.PhysDamageScale = scale;

    public override void OnKilled(EntityHandle attacker)
    {
        // CBreakableProp::Event_Killed (props.cpp:1170-1180): a pinned prop is
        // let go and pushed by the killing blow before it breaks (A38).
        if (_motionDisabled)
        {
            EnableMotion();
            VPhysicsTakeDamage(_lastBlow.Type, _lastBlow.Position, _lastBlow.Force);
        }

        Break();
    }

    /// <summary>
    /// Breaking, which is the same for every physics prop and therefore not
    /// virtual.
    ///
    /// It was virtual once, and prop_physics_respawnable overrode it to hide
    /// itself instead — which silently dropped the explosion, because the bang
    /// lived in the half that was replaced. Every explosive barrel in
    /// dm_lockdown is a respawnable one, so the map's twenty-one barrels all
    /// died quietly. A subclass now chooses only what becomes of the prop
    /// afterwards; it cannot skip the parts that are common to breaking.
    /// </summary>
    protected void Break()
    {
        // Once. A prop hit twice in a tick would otherwise fire its outputs
        // twice and be removed twice.
        if (_broken)
            return;

        _broken = true;
        OnBreak.Fire();

        // CBreakableProp::Break's PhysBreakSound( this, VPhysicsGetObject(),
        // GetAbsOrigin() ), before the explosion and the pieces (props.cpp:1673;
        // physics stock parity L).
        Sounds.AddBreakSound(Handle);

        // The bang is the model's, and it happens before the prop goes: the
        // explosion needs somewhere to be, and the owner is credited with
        // whatever it kills. Touching StartingHealth first is what guarantees
        // the prop data has been read, for a prop broken by an input rather
        // than by damage.
        _ = StartingHealth;

        // ignored: Handle -- a barrel that explodes where it stands must say so,
        // or its own hull stops the very first trace of the traversal and every
        // victim in the radius is scored at distance zero. See Explosions.Fire.
        if (_explosiveDamage > 0f)
            Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                            Origin, _explosiveDamage, _explosiveRadius, Handle,
                            ignored: Handle, inflictor: Handle);

        CreatePieces();

        Broken();
    }

    /// <summary>
    /// The debris, which is what a player actually sees a prop do when it
    /// breaks.
    ///
    /// <para>
    /// BEFORE <see cref="Broken"/>, and that order is the whole of it: the
    /// pieces are placed relative to where the prop is and thrown along what it
    /// was doing, and the default <see cref="Broken"/> removes it. Reading a
    /// removed entity's origin and velocity gives a heap of debris at the world
    /// origin, motionless.
    /// </para>
    ///
    /// <para>
    /// The prop is handed on as each piece's owner. Stock does the same, through
    /// <c>CreateNoSpawn( "prop_physics", position, angles, pOwner )</c>, and it
    /// is what keeps a piece from colliding with the thing it came out of.
    /// </para>
    /// </summary>
    private void CreatePieces()
    {
        if (_breakPieces == 0 || Vars.Model.Length == 0)
            return;

        var context = new BreakContext(
            // Through the injected area rather than the base class's Origin
            // property, so the whole context comes from one place — the property
            // is a static-engine convenience and would answer from a different
            // engine than the four lines below it.
            Origin: Entities.GetOrigin(Handle),
            Angles: Entities.GetAngles(Handle),
            Velocity: Entities.GetVelocity(Handle),
            AngularVelocity: Entities.GetAngularVelocity(Handle),

            // Stock sets this unconditionally in CBreakableProp::Break: "no
            // damage/damage force? set a burst of 100 for some movement".
            DefaultBurstScale: PropBreakPlan.DefaultBurstScale,

            DefaultCollisionGroup: PropBreakPlan.GroupForPieces(
                Physics.GetCollisionGroup(Handle)),

            SpawnFlags: SpawnFlags);

        PiecesMade = Breaker.CreateAll(Vars.Model, Handle, context);
    }

    /// <summary>How many pieces the last break made. For tests, and for a report.</summary>
    internal int PiecesMade { get; private set; }

    /// <summary>
    /// What becomes of the prop once it has broken. Gone, unless a subclass
    /// wants it back.
    /// </summary>
    protected virtual void Broken() => Vanish();

    /// <summary>
    /// Puts a broken prop back into play, so it can break again.
    ///
    /// Only a prop that comes back needs this, and it exists rather than a
    /// settable flag so the one-shot guard stays private to breaking.
    /// </summary>
    protected void Unbreak() => _broken = false;
}

/// <summary>
/// The map keys <see cref="PropPhysics"/> declares.
/// </summary>
[ScriptVars]
public partial class PropPhysicsVars
{
    /// <summary>The model, which also supplies the collision shape.</summary>
    public string Model { get; set; } = string.Empty;

    public int Health { get; set; }

    /// <summary>Multiplies the mass the model was compiled with.</summary>
    public float MassScale { get; set; }

    /// <summary>
    /// Seconds this prop lasts before it goes. Zero, on everything a mapper
    /// places — it is how a BREAK PIECE is told what it is.
    ///
    /// <para>
    /// SourceSharp's own key rather than one of stock's, because stock has
    /// nowhere to put it: there, the fade time travels inside a
    /// <c>breakmodel_t</c> straight into <c>SUB_StartFadeOut</c> on an entity
    /// the same function just made. Here the piece is a separate managed entity
    /// created through the ordinary route, and a key before the spawn is the
    /// only channel that reaches it in time — which it must, because this also
    /// decides whether the piece may be broken again.
    /// </para>
    ///
    /// <para>
    /// A mapper may write it, and it does what it says: the prop lasts that long
    /// and then disappears.
    /// </para>
    /// </summary>
    [VarName(GibFadeTimeKey)]
    public float GibFadeTime { get; set; }

    /// <summary>
    /// m_impactEnergyScale: how much of a physics impact's energy this prop
    /// feels. 0 means stock's default, 0.1.
    /// </summary>
    [VarName("physdamagescale")]
    public float PhysDamageScale { get; set; }

    /// <summary>
    /// m_flForceToEnableMotion: the prop starts pinned and a collision whose
    /// mass x speed reaches this lets it go, once (it is zeroed then).
    /// </summary>
    [VarName("forcetoenablemotion")]
    public float ForceToEnableMotion { get; set; }

    /// <summary>
    /// m_iMinHealthDmg: a blow under this is refused outright, neither hurting
    /// nor pushing the prop.
    /// </summary>
    [VarName("minhealthdmg")]
    public int MinHealthDamage { get; set; }

    /// <summary>
    /// m_damageToEnableMotion: the prop starts pinned, and the blow that leaves
    /// its health under this lets it go (once).
    /// </summary>
    [VarName("damagetoenablemotion")]
    public int DamageToEnableMotion { get; set; }

    /// <summary>The key's spelling, so the breaker and the prop cannot disagree.</summary>
    public const string GibFadeTimeKey = "gibfadetime";
}
