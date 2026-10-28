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
/// Brushes and models the map asks to be simulated.
///
/// The shells decide what an entity IS — a brush is carved from the map, a prop
/// is a model the engine collides with — and neither is simulated. Being
/// simulated is a separate thing the map asks for, and these are the classes
/// that ask.
///
/// <para>
/// They share one ordering problem and solve it two different ways, which is
/// the reason they sit in one file. Creating a physics object needs a model to
/// read collision data out of. A brush has one during OnSpawn, because the
/// engine resolves the map's "*23" while parsing keyvalues. A prop does not:
/// SetModel from C# records a pending name that the shell applies after OnSpawn
/// returns. So a brush initialises physics immediately and a prop waits a tick.
/// </para>
///
/// <para>
/// Not sealed, because func_physbox_multiplayer is this class and a classname —
/// see FuncPhysboxMultiplayer for what stock added under that name and why none
/// of it survives.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncPhysbox, ScriptedEntityKind.Brush)]
public class FuncPhysbox : ScriptedEntity, IPhysGunTarget, ITakesMassCenterOverride
{
    /// <summary>CPhysBox::Spawn: DAMAGE_YES, or DAMAGE_EVENTS_ONLY -- never DAMAGE_NO.</summary>
    public override bool TakesDamage => true;

    public FuncPhysbox(IEngineEntityPhysics physics, PlayerPickupRules pickups)
    {
        Physics = physics;
        Pickups = pickups;
    }

    /// <summary>What a player's +USE may do with this box (its caps and its Use).</summary>
    private readonly PlayerPickupRules Pickups;

    /// <summary>this entity as the solver sees it.</summary>
    protected readonly IEngineEntityPhysics Physics;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncPhysboxVars"/>.</summary>
    [ScriptVars] public FuncPhysboxVars Vars = new();

    // Stock's SF_PHYSBOX_* values. High bits, because func_physbox inherits
    // func_breakable's spawnflags and these start above them — the low bits
    // mean "break on touch", "break under pressure" and so on.

    /// <summary>
    /// func_physbox's own spawnflags, stock's SF_PHYSBOX_*.
    ///
    /// <para>
    /// High bits, because func_physbox inherits func_breakable's spawnflags and
    /// these start above them -- the low bits mean "break on touch", "break under
    /// pressure" and so on, and are not handled here. That shared low half is
    /// exactly why the type matters: a bare 0x1000 read off this word says nothing
    /// about which half of the inheritance it belongs to.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        StartAsleep = 0b0001_0000_0000_0000,  // 0x1000
        Debris = 0b0100_0000_0000_0000,  // 0x4000
        MotionDisabled = 0b1000_0000_0000_0000,  // 0x8000
        IgnoreUse = 0x2000,                      // SF_PHYSBOX_IGNOREUSE
        EnableOnPhyscannon = 0x20000,            // SF_PHYSBOX_ENABLE_ON_PHYSCANNON
        EnablePickupOutput = 0x80000,            // SF_PHYSBOX_ENABLE_PICKUP_OUTPUT
        NeverPickUp = 0x200000,                  // SF_PHYSBOX_NEVER_PICK_UP
    }

    public readonly Output OnBreak = new("OnBreak");
    /// <summary>
    /// Named without the "On" because the class also OVERRIDES OnDamaged — the
    /// virtual that decides how much damage is taken. A field and a method
    /// cannot share a name, and of the two it is the output that can be renamed
    /// without changing what a map writes.
    /// </summary>
    public readonly Output Damaged = new("OnDamaged");
    public readonly Output OnMotionEnabled = new("OnMotionEnabled");
    public readonly Output OnHealthChanged = new("OnHealthChanged");
    public readonly Output OnAwakened = new("OnAwakened");
    public readonly Output OnPlayerUse = new("OnPlayerUse");

    // The gravity gun's (CPhysBox::OnPhysGunPickup and OnPhysGunDrop,
    // physobj.cpp:398-401), each with the player as activator.
    public readonly Output OnPhysGunPickup = new("OnPhysGunPickup");
    public readonly Output OnPhysGunPunt = new("OnPhysGunPunt");
    public readonly Output OnPhysGunOnlyPickup = new("OnPhysGunOnlyPickup");
    public readonly Output OnPhysGunDrop = new("OnPhysGunDrop");

    // Stock keeps all three on the entity across a save (CBreakable's
    // m_iMaxHealth; the motion flag lives on the physics object; a broken box
    // is gone). Uncarried, a reloaded box read as indestructible, could break
    // twice, and a motion-disabled one never announced being enabled.
    [SaveField] private bool _broken;
    [SaveField] private bool _motionEnabled = true;
    [SaveField] private int _startingHealth;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        _startingHealth = Vars.Health;

        if (Vars.Health > 0)
            Health = Vars.Health;

        // CPhysBox::Spawn (physobj.cpp:448-450; A32): debris is COLLISION_GROUP_DEBRIS,
        // set before the object is made.
        if (Flags.HasFlag(SpawnFlag.Debris))
            Physics.SetCollisionGroup(Handle, CollisionGroup.Debris);

        // A brush already has its model here, so this can happen now — see the
        // file comment for why a prop cannot.
        if (!Physics.InitPhysics(Handle,
                                      asleep: Flags.HasFlag(SpawnFlag.StartAsleep),
                                      debris: Flags.HasFlag(SpawnFlag.Debris)))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' could not be simulated\n");
            return;
        }

        if (Vars.MassScale > 0f)
        {
            float mass = Physics.GetMass(Handle);

            if (mass > 0f)
                Physics.SetMass(Handle, mass * Vars.MassScale);
        }

        if (Vars.NotSolidToWorld)
            Physics.SetNotSolid(Handle, true);

        if (Flags.HasFlag(SpawnFlag.MotionDisabled))
            SetMotion(false);
    }

    // Simulation state, not motion enable — and so NOT SetMotion, which also
    // fires OnMotionEnabled. Stock is CPhysBox::InputWake/InputSleep
    // (physobj.cpp:639, :648), each exactly one call on the physics object:
    //
    //     VPhysicsGetObject()->Wake();   VPhysicsGetObject()->Sleep();
    //
    // A slept physbox stays moveable, so a shove revives it and the player is
    // not stopped dead by it; a motion-disabled one is pinned until something
    // enables motion again.
    [ScriptedEvent("Wake")] private void OnWake() => Physics.Wake(Handle);

    /// <summary>
    /// CPhysBox::ObjectCaps (physobj.cpp:550): usable when the map asked for
    /// the use output, or, unless it ignores use, when a player could pick it up.
    /// </summary>
    public override UseCaps ObjectCaps
    {
        get
        {
            UseCaps caps = base.ObjectCaps;

            if (Flags.HasFlag(SpawnFlag.EnablePickupOutput))
                caps |= UseCaps.ImpulseUse;
            else if (!Flags.HasFlag(SpawnFlag.IgnoreUse) && Pickups.CanPickupObject(Handle))
                caps |= UseCaps.ImpulseUse;

            return caps;
        }
    }

    /// <summary>
    /// CPhysBox::Use (physobj.cpp:571-580; A28): a player's use fires
    /// OnPlayerUse when the map asked for it, then, unless the box ignores
    /// use, asks to pick it up (which stock HL2MP refuses unless
    /// hl2mp_allow_pickup is on).
    /// </summary>
    [ScriptedEvent("Use")]
    private void OnUseInput(InputData input)
    {
        if (!Entities.IsPlayer(input.Activator))
            return;

        if (Flags.HasFlag(SpawnFlag.EnablePickupOutput))
            OnPlayerUse.Fire(input.Activator);

        if (!Flags.HasFlag(SpawnFlag.IgnoreUse))
            Pickups.PickupObject(input.Activator, Handle);
    }

    /// <summary>
    /// CPhysBox::VPhysicsUpdate (physobj.cpp:716-722; A28): the first time a
    /// start-asleep box moves it fires OnAwakened and stops being start-asleep.
    /// </summary>
    public override void OnPhysicsWake()
    {
        int flags = SpawnFlags;
        if ((flags & (int)SpawnFlag.StartAsleep) == 0)
            return;

        OnAwakened.Fire(Handle);
        Entities.SetSpawnFlags(Handle, flags & ~(int)SpawnFlag.StartAsleep);
    }
    [ScriptedEvent("Sleep")] private void OnSleep() => Physics.Sleep(Handle);

    [ScriptedEvent("EnableMotion")] private void OnEnableMotionInput() => SetMotion(true);
    [ScriptedEvent("DisableMotion")] private void OnDisableMotionInput() => SetMotion(false);

    /// <summary>
    /// Turning motion on fires an output; turning it off does not.
    ///
    /// That asymmetry is stock's and it is the useful way round: a map wires
    /// OnMotionEnabled to start something happening, and there is nothing to
    /// react to in a brush going still. Guarded so the output fires on the
    /// TRANSITION rather than on every input, which is what a map wiring a
    /// one-shot to it expects.
    /// </summary>
    private void SetMotion(bool enabled)
    {
        Physics.EnableMotion(Handle, enabled);

        if (enabled && !_motionEnabled)
            OnMotionEnabled.Fire();

        _motionEnabled = enabled;
    }

    /// <summary>
    /// CPhysBox -> CBreakable::OnTakeDamage reaches the base, and its physics
    /// push, unless the box is gone or the blow is under minhealthdmg
    /// (func_break.cpp:823-845; plan_physics_managed A30).
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        if (!_broken && (int)damage >= Vars.MinimumDamage)
            VPhysicsTakeDamage(damageType, position, force);
        return OnDamaged(attacker, damage, damageType);
    }

    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_broken)
            return 0;

        int taken = (int)damage;

        // Under the threshold is IGNORED, not reduced: a physbox with
        // minhealthdmg set is meant to shrug off small arms entirely.
        if (taken < Vars.MinimumDamage)
            return 0;

        // CPhysBox::OnTakeDamage: m_OnDamaged.FireOutput( info.GetAttacker() )
        // (physobj.cpp:811); UpdateHealth and Break carry the same attacker.
        Damaged.Fire(attacker);

        // No health means it cannot break. It still reports the hit, because a
        // map may well wire OnDamaged on something indestructible.
        if (_startingHealth <= 0)
            return 0;

        // Health is NOT subtracted here. The shell applies whatever this
        // returns and calls OnKilled when it runs out — so doing it here as
        // well takes the damage twice, which is exactly what happened: 60
        // health hit for 25 came out at 10.
        int remaining = Health - taken;

        // CBreakable::UpdateHealth outputs the clamped ratio of max health
        // (func_break.cpp:685-698), as PropPhysics and FuncPushable do.
        OnHealthChanged.Fire((Math.Max(0, remaining) / (float)_startingHealth)
                                 .ToString(System.Globalization.CultureInfo.InvariantCulture), attacker);

        // Damage can also be what unpins it, and that happens before it breaks:
        // a crate meant to be shot loose should come loose on the shot that
        // would have broken it too.
        if (Vars.DamageToEnableMotion > 0 && remaining < Vars.DamageToEnableMotion)
            SetMotion(true);

        return taken;
    }

    public override void OnKilled(EntityHandle attacker) => Break(attacker);

    //-------------------------------------------------------------------------
    // The gravity gun (CPhysBox's IPlayerPickupVPhysics, physobj.cpp).
    //-------------------------------------------------------------------------

    /// <summary>CPhysBox::CanBePickedUpByPhyscannon's spawnflag: never.</summary>
    bool IPhysGunTarget.PreventsPickup => Flags.HasFlag(SpawnFlag.NeverPickUp);

    /// <summary>A pinned box the gun may take: SF_PHYSBOX_ENABLE_ON_PHYSCANNON.</summary>
    bool IPhysGunTarget.EnablesOnPhyscannon => Flags.HasFlag(SpawnFlag.EnableOnPhyscannon);

    /// <summary>
    /// CPhysBox::OnPhysGunPickup (physobj.cpp:731): a punt says so first; a
    /// pinned box comes loose only if its map said the gun may free it (and
    /// says nothing more if not); then the pickup, and the cannon's own.
    /// </summary>
    void IPhysGunTarget.OnPhysGunPickup(EntityHandle player, PhysGunPickup reason)
    {
        if (reason == PhysGunPickup.PuntedByCannon)
            OnPhysGunPunt.Fire(player);

        if (!_motionEnabled)
        {
            if (!Flags.HasFlag(SpawnFlag.EnableOnPhyscannon))
                return;

            SetMotion(true);
        }

        OnPhysGunPickup.Fire(player);

        if (reason == PhysGunPickup.PuntedByCannon)
            return;

        if (reason == PhysGunPickup.PickedUpByCannon)
            OnPhysGunOnlyPickup.Fire(player);
    }

    /// <summary>CPhysBox::OnPhysGunDrop: every drop says so.</summary>
    void IPhysGunTarget.OnPhysGunDrop(EntityHandle player, PhysGunDrop reason) => OnPhysGunDrop.Fire(player);

    private void Break(EntityHandle breaker)
    {
        if (_broken)
            return;

        _broken = true;
        OnBreak.Fire(breaker); // m_OnBreak.FireOutput( m_hBreaker ) (func_break.cpp:1112)
        Remove();
    }
}

/// <summary>
/// The map keys <see cref="FuncPhysbox"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncPhysboxVars
{
    /// <summary>Zero health means it cannot be broken, only shoved.</summary>
    public int Health { get; set; }

    /// <summary>Damage below this is ignored entirely, not reduced.</summary>
    [VarName("minhealthdmg")]
    public int MinimumDamage { get; set; }

    /// <summary>Multiplies the mass derived from the brush's volume.</summary>
    public float MassScale { get; set; }

    /// <summary>
    /// Still collides with players and props; simply does not wedge against the
    /// map. What a mapper reaches for when something should be knocked about
    /// inside a space it would otherwise jam in.
    /// </summary>
    [VarName("notsolid")]
    public bool NotSolidToWorld { get; set; }

    /// <summary>
    /// Below this health, motion turns on. The map's way of saying "pinned
    /// until something hits it hard enough".
    /// </summary>
    public int DamageToEnableMotion { get; set; }
}
