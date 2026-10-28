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
/// A gas cannister that becomes a rocket when something breaks it open.
///
/// The entity is three things bolted together and it is worth naming them
/// separately, because only the middle one is really physics: it is a breakable
/// with health, it is a thruster on a motion controller, and it is a bang. What
/// makes it a cannister rather than a barrel is that DAMAGE SETS IT OFF instead
/// of merely wearing it down — a hit that does not kill it lights it, and a hit
/// on something already lit finishes it.
///
/// <para>
/// The thrust is a <see cref="MotionControllerKind.ConstantForce"/> told to read
/// its numbers as an ACCELERATION. Stock's own controller returns
/// SIM_LOCAL_ACCELERATION and the shell's returns a force by default, which
/// differs by a factor of the mass; the flag is the fix rather than multiplying
/// the magnitude here, because a magnitude scaled once is wrong the moment the
/// mass changes. See <see cref="MotionController.SetForceIsAcceleration"/>.
/// </para>
///
/// <para>
/// The jet is a real <c>env_steam</c>, still a native entity, created and driven
/// through inputs. That is deliberate: a steam jet is an appearance, drawn by
/// the client from networked numbers, and nothing about it is a decision this
/// mod would want to change.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PhysicsCannister, ScriptedEntityKind.Prop)]
public sealed class PhysicsCannister : AnimatingEntity
{
    public PhysicsCannister(IGameClock clock, IEngineDamage damage, IEngineHost host, IEngineSound sound,
                            IEngineWorldQuery world, IEngineEffects effects, IEnginePrecache precache, IBlastSweep blast, IServerDecals decals,
                            IEngineEntityPhysics physics, IEngineMotionControllers motion, IEngineEnvironment environment,
                            ConVarRegistry? convars = null)
    {
        Environment = environment;
        ConVars = convars;
        Physics = physics;
        Motion = motion;
        Clock = clock;
        Damage = damage;
        Host = host;
        Sound = sound;
        World = world;
        Effects = effects;
        Precache = precache;
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

    /// <summary>tracing, and what is at a point. For the blast.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>the fireball, the scorch mark and the sparks.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>getting the blast's sprite and decal onto the client.</summary>
    private readonly IEnginePrecache Precache;


    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>making sounds, and what is heard.</summary>
    private readonly IEngineSound Sound;

    /// <summary>
    /// this cannister as the solver sees it — for the Wake input, which is a
    /// simulation call and not a motion-enable one.
    /// </summary>
    private readonly IEngineEntityPhysics Physics;

    /// <summary>the solver's motion controllers, which the thruster is made in.</summary>
    private readonly IEngineMotionControllers Motion;

    /// <summary>gpGlobals->maxClients, and breakable_multiplayer: whose its client-marked pieces are.</summary>
    private readonly IEngineEnvironment Environment;

    private readonly ConVarRegistry? ConVars;
    /// <summary>What a map may set on this entity. See <see cref="PhysicsCannisterVars"/>.</summary>
    [ScriptVars] public PhysicsCannisterVars Vars = new();

    /// <summary>Fired when something lights the cannister, whatever lit it.</summary>
    public readonly Output OnActivate = new();

    /// <summary>
    /// Fired the first time a cannister that spawned asleep is disturbed.
    ///
    /// DECLARED AND NEVER FIRED, and both halves are deliberate. Stock's
    /// spawnflag 1 asks for a physics object created asleep and stock hangs this
    /// output off VPhysicsUpdate noticing it has woken; the Prop shell builds
    /// its object awake, and neither the sleep state nor that callback crosses
    /// the ABI, so there is nothing here to ask. It is still declared so that a
    /// map which wires it keeps loading rather than failing on a name nothing
    /// answers to. See docs/divergences.md.
    /// </summary>
    public readonly Output OnAwakened = new();

    private const int DefaultHealth = 25;

    /// <summary>The native jet's classname. See <see cref="StartJet"/> for why it is spelt here.</summary>
    private const string SteamJetClass = "env_steam";

    /// <summary>
    /// How long a stopped jet is left standing before it is removed.
    ///
    /// Stock's five seconds, and it is the difference between a plume that thins
    /// and one that blinks out: the gas already in the air belongs to the jet
    /// entity, so removing it the moment the cannister stops venting takes the
    /// visible plume with it.
    /// </summary>
    private const float JetLinger = 5f;

    /// <summary>
    /// How long after lighting the cannister ignores further damage.
    ///
    /// Stock's window, and it exists because the shot that lights a cannister
    /// usually lands the same tick: without it the blow that set it off would
    /// immediately be re-read as the blow that finishes it, and no cannister
    /// would ever get off the ground.
    /// </summary>
    private const float SelfHitGrace = 0.1f;

    /// <summary>
    /// The controller's raw native id. The wrapper is rebuilt from it on
    /// restore; the native controller itself outlives the reload (the shell
    /// clears the table only at LevelShutdown), so without the id the
    /// rebuilt cannister could neither re-drive it nor destroy it. Stock
    /// saves the controller and re-wires it in OnRestore.
    /// </summary>
    [SaveField] private uint _controllerId;

    private MotionController? _thruster;

    /// <summary>The env_steam; a handle leaf, so the tier re-checks it on the way in.</summary>
    [SaveField] private EntityHandle _jet = EntityHandle.None;

    /// <summary>Who lit it, and therefore who is answerable for what it kills.</summary>
    [SaveField] private EntityHandle _launcher = EntityHandle.None;

    // The burn's whole story, and stock saves every line of it
    // (physics_cannister.cpp:66-89). Uncarried, the rebuilt instance came back
    // with none of it: no douse clock, thrust along the zeroed vector, the
    // plume and the gas sound orphaned to native, and a spent cannister
    // relightable on top of the first controller.
    [SaveField] private bool _active;
    [SaveField] private bool _spent;
    [SaveField] private float _activateTime;
    [SaveField] private float _shutdownAt;

    /// <summary>Thrust direction in the cannister's own frame, worked out once when it lights.</summary>
    [SaveField] private Vector _localThrust;

    [SaveField] private bool _exploded;

    public override void OnSpawn()
    {
        if (Vars.Model.Length > 0)
            SetModel(Vars.Model);

        // The shell builds the physics object after this returns, so nothing
        // here may ask about mass. Health is the one thing that can be set now,
        // and stock's default is 25 rather than the engine's zero.
        Health = Vars.Health > 0 ? Vars.Health : DefaultHealth;
    }

    /// <summary>
    /// Puts the wrapper back around the controller the reload left standing.
    ///
    /// The native controller outlives the assembly swap — the shell clears
    /// the controller table only at LevelShutdown — so a lit cannister
    /// restored without this drives nothing (the zeroed <c>_localThrust</c>
    /// and a null wrapper), cannot douse cleanly (nothing to Destroy), and a
    /// spent one could even be relit onto a second controller. Stock re-wires
    /// the same pointer on restore (physics_cannister.cpp:66-89).
    /// Re-adopting rather than re-attaching is the point: the old controller
    /// is still attached to the body, and a new one would be a second force
    /// on the same body with nothing able to reach the first.
    /// </summary>
    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);

        if (_thruster is not null || _controllerId == 0u)
            return;

        _thruster = MotionController.Adopt(Motion, new PhysicsId(_controllerId),
                                           MotionControllerKind.ConstantForce);
    }

    //-------------------------------------------------------------------------
    // Damage.
    //
    // Two entirely different rules, and which applies is decided by whether the
    // cannister is already lit — not by how hard it was hit. An unlit cannister
    // is a container being broken open; a lit one is a rocket with a hole in it.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A blow with a place, which is how a bullet arrives.
    ///
    /// Stock splits this across two virtuals: OnTakeDamage does the health, and
    /// TraceAttack lights the cannister when the trace hit a HITBOX rather than
    /// the bounding box. The hitbox half cannot be reproduced — the standalone
    /// prop is traced against the model's compiled hull, which has no hitgroups,
    /// and no hitgroup crosses the ABI. So a located hit stands in for it: a
    /// bullet that struck the cannister somewhere lights it, which is the
    /// behaviour the rule exists to produce. See docs/divergences.md.
    ///
    /// The direction is taken from the blow, so a cannister shot from the front
    /// veers away from the shooter exactly as stock's does.
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        // An explosion is NOT a located hit, however good a position it comes
        // with. Stock reaches TraceAttack only down the bullet and melee path,
        // and the ordering is what makes that matter: lighting the cannister
        // here would put it inside its own grace window, and the health check
        // below would then be skipped — so a blast big enough to destroy it
        // would set it off instead. Blast is answered in OnDamaged, after the
        // health, exactly where stock answers it.
        if (!_active && !_spent && (damageType & DamageType.Blast) == 0)
        {
            // Stock's TraceAttack tilts the thrust DOWN by 5 before normalising,
            // on top of reversing the incoming direction. That is what stops a
            // cannister shot level from skating along the floor.
            Vector away = force.Normalized * -1f;
            away.Z -= 5f;

            Light(attacker, away.Normalized);
        }

        return OnDamaged(attacker, damage, damageType);
    }

    /// <summary>
    /// The health, and the two ways damage sets a cannister off without a place
    /// to have landed.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_exploded)
            return 0;

        if (!_active)
        {
            // Stock: m_iHealth -= info.GetDamage(); if ( m_iHealth < 0 )
            // (physics_cannister.cpp:167-168) -- an int minus a float, truncated
            // back, so a fractional blow costs a whole point (25 - 0.5 -> 24)
            // and the test is strictly below zero: a blow to exactly 0 leaves
            // it standing. The shell applies what this returns, so the amount
            // taken is the old health less stock's new one.
            int health = Health;
            int remaining = (int)(health - damage);
            int taken = health - remaining;

            if (remaining < 0)
            {
                Explode(attacker);
                return taken;
            }

            LightFromBlast(attacker, damageType);
            return taken;
        }

        // Already lit: it ignores everything for a moment, then anything that
        // burns or punctures finishes it.
        if (Clock.Now - _activateTime <= SelfHitGrace)
            return 0;

        const DamageType Finishes = DamageType.Bullet | DamageType.Buckshot
                             | DamageType.Burn | DamageType.Blast;

        if ((damageType & Finishes) != 0)
            Explode(attacker);

        // Zero either way. A lit cannister does not lose health — it either
        // goes off or shrugs the blow off entirely.
        return 0;
    }

    /// <summary>
    /// The damage kinds that light a cannister without breaking it.
    ///
    /// An explosion always does. A blunt blow does about half the time, which is
    /// stock's number and is what makes hitting one with a crowbar a gamble
    /// rather than a procedure.
    ///
    /// The draw comes from the ENGINE's stream rather than a managed one, for
    /// the same reason env_beverage's does: server randomness is replayed by
    /// demo playback and has to come out the same.
    /// </summary>
    private void LightFromBlast(EntityHandle attacker, DamageType damageType)
    {
        if (damageType.HasFlag(DamageType.Blast))
        {
            Light(attacker, Vector.Zero);
            return;
        }

        const DamageType Blunt = DamageType.Club | DamageType.Slash | DamageType.Crush;

        // Stock draws an integer in [1,100] and takes it under 50, which is 49
        // outcomes out of 100 rather than the half it reads as. Said as the
        // fraction it actually is, because there is no integer draw across the
        // ABI to copy literally.
        // ANY of the three, not all: HasFlag(Blunt) would want a blow that is
        // club AND slash AND crush at once.
        if ((damageType & Blunt) != 0 && Host.RandomFloat(0f, 1f) < 0.49f)
            Light(attacker, Vector.Zero);
    }

    //-------------------------------------------------------------------------
    // Inputs.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The map lighting it deliberately.
    ///
    /// The offset is stock's literal, and it is not arbitrary: it tips the
    /// thrust slightly down and to one side so a cannister lit by a trigger
    /// tumbles off somewhere instead of standing on its jet.
    /// </summary>
    [ScriptedEvent("Activate")]
    private void OnActivateInput(InputData input)
        => Light(input.Activator, new Vector(0f, 0.1f, -0.25f));

    [ScriptedEvent("Deactivate")]
    private void OnDeactivate() => Douse();

    [ScriptedEvent("Explode")]
    private void OnExplodeInput(InputData input) => Explode(input.Activator);

    /// <summary>
    /// Shakes a settled cannister back into simulation.
    ///
    /// Stock is one call — <c>pPhys->Wake()</c> in
    /// <c>CPhysicsCannister::InputWake</c>
    /// (<c>physics_cannister.cpp:319</c>) — and it crosses the ABI directly.
    ///
    /// <para>
    /// This used to be <c>EnableMotion(true)</c>, on the reasoning that
    /// re-enabling motion also wakes and is "harmless on one that had not".
    /// It is not harmless: motion enable undoes a pin, not a sleep, so a
    /// cannister a map had deliberately pinned came unpinned on a Wake input
    /// that stock leaves it pinned through. prop_physics answers its own Wake
    /// input the same way this one now does.
    /// </para>
    /// </summary>
    [ScriptedEvent("Wake")]
    private void OnWake() => Physics.Wake(Handle);

    //-------------------------------------------------------------------------
    // Lighting, running and going out.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Lights the cannister: thrust on, jet out, hiss going, fuel clock running.
    ///
    /// <paramref name="offset"/> perturbs the direction the gas leaves in, which
    /// is what makes a cannister lit by a shot fly differently from one lit by a
    /// trigger.
    /// </summary>
    private void Light(EntityHandle activator, Vector offset)
    {
        // Already going, or the fuel is gone. Stock's second half of this test
        // is what makes Deactivate permanent — it zeroes the fuel — so a map
        // cannot switch a spent cannister back on.
        if (_active || _spent || Vars.ThrustTime <= 0f)
            return;

        _launcher = activator;

        // The offset is a WORLD direction — a shot comes in along one and the
        // Activate literal is stock's, added to a world-space nozzle vector — so
        // it is brought into the cannister's frame before the two are combined.
        // Stock does the whole sum in world space and converts the result once;
        // the same rotation applied to one term instead of the sum is the same
        // vector, because normalising does not care which frame it is done in.
        _localThrust = ThrustDirection(ToLocal(offset, Angles.Vectors(Entities.GetAngles(Handle))));

        // Before the physics, because a map's OnActivate connection may well be
        // what opens the door the cannister is about to fly through.
        OnActivate.Fire();

        _thruster = MotionController.Attach(Motion, Handle, MotionControllerKind.ConstantForce);

        // The raw id rides the record so the reload can re-adopt the wrapper;
        // the controller itself outlives the reload and keeps driving.
        _controllerId = _thruster?.Id.Value ?? 0u;

        if (_thruster is null)
        {
            Log.Warning($"[SourceSharp] physics_cannister '{Name}': no physics object to thrust\n");
            return;
        }

        _thruster.SetForceIsAcceleration(true);
        CalcThrust();
        _thruster.SetActive(true);

        _active = true;
        _activateTime = Clock.Now;
        _shutdownAt = _activateTime + Vars.ThrustTime;

        StartJet();

        if (Vars.GasSound.Length > 0)
            Sound.EntityEmitSound(Handle, Vars.GasSound);

        // Every tick while it burns: the jet has to be moved because it cannot
        // be parented to a standalone entity. The thrust needs no think: it was
        // computed once above, in the cannister's own frame. It stops the
        // moment the fuel does, so the cost is bounded by the fuel key rather
        // than by the map's lifetime.
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        // The one think a doused cannister still owes: taking away the jet that
        // was left standing to let its plume thin out.
        if (!_active)
        {
            if (Entities.IsValid(_jet))
                Entities.Remove(_jet);

            _jet = EntityHandle.None;
            StopThinking();
            return;
        }

        if (Clock.Now >= _shutdownAt)
        {
            Douse();
            return;
        }

        MoveJet();
        SetNextThink(0f);
    }

    /// <summary>
    /// CThrustController::CalcThrust, once, when the cannister lights: the
    /// force (direction * thrust * mass) at the nozzle decomposed into push and
    /// twist by vphysics (CalculateVelocityOffset), with the push turned into
    /// the object's frame (WorldToLocalVector), and answered every tick as
    /// SIM_LOCAL_ACCELERATION, so it turns as the cannister tumbles. Before
    /// 2026-10-12 this re-issued a world force every think; the setter's local
    /// conversion (plan_physics_bones_native_zero 2.4) made stock's shape
    /// possible.
    ///
    /// <para>
    /// The mass appears here for the same reason it appears in stock's
    /// CalcThrust, and it is NOT the pre-multiplying the controller warns
    /// against. Decomposing a force at a point divides by the mass on the way
    /// through, so a "thrust" key that is meant to be an ACCELERATION has to
    /// go in as thrust * mass to come out as thrust. The acceleration flag then
    /// stops the solver putting the mass back a second time. Stock reads the
    /// mass once, at activation, as this does.
    /// </para>
    /// </summary>
    private void CalcThrust()
    {
        Vector direction = Rotate(_localThrust, Angles.Vectors(Entities.GetAngles(Handle)));

        // Through the nozzle rather than the centre of mass, which is what makes
        // a cannister spin as it flies. The nozzle is the entity's origin: see
        // ThrustDirection for why that is stock's answer for this model.
        _thruster?.SetForceAtPoint(direction * (Vars.Thrust * Mass), Origin,
                                   ForceComponents.Both, localSpace: true);
    }

    /// <summary>
    /// The jet, to where the cannister's nozzle is NOW and pointing the way
    /// the gas goes. Stock parents it (m_pJet->SetParent( this )); a
    /// standalone entity cannot be parented, so the burn's think moves it.
    /// </summary>
    private void MoveJet()
    {
        if (!Entities.IsValid(_jet))
            return;

        Vector direction = Rotate(_localThrust, Angles.Vectors(Entities.GetAngles(Handle)));
        Entities.SetOrigin(_jet, Origin);
        Entities.SetAngles(_jet, Angles.FromVector(direction * -1f));
    }

    /// <summary>
    /// Which way the gas pushes, in the cannister's own frame.
    ///
    /// DOWN THE MODEL'S OWN UP AXIS, and that is stock's answer rather than an
    /// approximation of it. Stock asks for the model's "nozzle" attachment and
    /// pushes along the reverse of its up axis — but CBaseAnimating::GetAttachment
    /// answers a model that has no such attachment by copying the ENTITY's world
    /// transform and returning false, which its caller does not check. So for
    /// every model without a nozzle, and props_junk/propanecanister001a is one —
    /// it has no attachments at all — stock's thrust is along the entity's own
    /// -Z and its origin is the entity's origin. That is exactly what this
    /// returns, so such a cannister is not merely close to stock, it is stock.
    ///
    /// It was previously +X, the cannister's nose, on the reasoning that a
    /// nozzle sits at one end and points back down the body. The body of that
    /// model runs along Z — it is 22 units tall and 14 across — so +X was across
    /// the barrel rather than along it, and a cannister lying down pushed itself
    /// into the floor. See docs/divergences.md for what that measured as.
    ///
    /// <paramref name="offset"/> is in the same frame, so a caller with a world
    /// direction converts it first — see <see cref="ToLocal"/>. The 5:1
    /// weighting is stock's, and it is what keeps the offset a nudge rather than
    /// a steer.
    /// </summary>
    internal static Vector ThrustDirection(Vector offset)
        => (new Vector(0f, 0f, -5f) + offset).Normalized;

    /// <summary>
    /// A direction in the cannister's frame, in world space.
    ///
    /// Static and taking the basis rather than reading the entity, so the
    /// arithmetic can be checked without an engine.
    /// </summary>
    internal static Vector Rotate(Vector local, Angles.Basis basis)
        => basis.Forward * local.X + basis.Right * local.Y + basis.Up * local.Z;

    /// <summary>
    /// A direction in world space, in the cannister's frame.
    ///
    /// The inverse of <see cref="Rotate"/>, and a transpose rather than an
    /// inverse because a basis of unit axes is orthonormal: the world vector's
    /// component along each of the cannister's own axes is what it projects onto
    /// that axis.
    /// </summary>
    internal static Vector ToLocal(Vector world, Angles.Basis basis)
        => new(world.Dot(basis.Forward), world.Dot(basis.Right), world.Dot(basis.Up));

    /// <summary>
    /// The visible half: an env_steam at the nozzle, pointing the way the gas
    /// goes.
    ///
    /// Its four live numbers are set through inputs, which is all that is
    /// needed — JetLength, SpreadSpeed, Speed and Rate are DEFINE_INPUT on
    /// CSteamJet, so each is a networked field a map could already drive. The
    /// two that are keyfields only, StartSize and EndSize, go in as keyvalues
    /// before the spawn: stock writes m_StartSize = 10, m_EndSize = 25 straight
    /// onto the jet (physics_cannister.cpp:247-248), and EnvSteamVars' own
    /// defaults are 0, a plume of invisible puffs.
    /// </summary>
    private void StartJet()
    {
        // The literal rather than an EntityNames constant, because that file
        // lists what this mod IMPLEMENTS and env_steam is still Valve's: it is
        // registered in steamjet.cpp and stays there.
        _jet = Entities.Create(SteamJetClass);

        if (!Entities.IsValid(_jet))
        {
            Log.Warning($"[SourceSharp] physics_cannister '{Name}': could not create env_steam\n");
            return;
        }

        Entities.SetOrigin(_jet, Origin);

        // No owner is set, and stock sets one. It is not expressible rather than
        // forgotten: an owner is a CBaseEntity and this cannister is not one, so
        // the call would quietly store a null. Nothing is lost by it — an owner
        // keeps two solid things from colliding, and a steam jet is neither.
        Entities.SetKeyValue(_jet, "StartSize", "10");
        Entities.SetKeyValue(_jet, "EndSize", "25");
        Entities.Spawn(_jet);

        // After spawn, because an input on an entity the engine has not built
        // yet reaches nothing.
        var jet = JetSettings(Vars.Thrust);

        Fire(_jet, "SpreadSpeed", jet.SpreadSpeed);
        Fire(_jet, "Speed", jet.Speed);
        Fire(_jet, "Rate", jet.Rate);
        Fire(_jet, "JetLength", jet.Length);

        Entities.FireInput(_jet, "TurnOn", "", Handle);

        // Placed and aimed straight away, so the first frame it is drawn it is
        // already at the nozzle rather than at the map origin.
        MoveJet();
    }

    /// <summary>
    /// How the plume looks for a given thrust.
    ///
    /// Stock's arithmetic, kept whole because the shape of it is the point: the
    /// spread scales with the thrust without limit, while the speed and rate ride
    /// a fraction that saturates at 5000. So a hard cannister vents WIDER but not
    /// endlessly faster, which is what stops a strong one looking like a hose.
    ///
    /// Static and taking the thrust, so it can be checked without an engine.
    /// </summary>
    internal static (float SpreadSpeed, float Speed, float Rate, float Length) JetSettings(float thrust)
    {
        float extra = Math.Clamp(thrust * (1f / 5000f), 0f, 1f);

        // Stock casts the fraction to int BEFORE multiplying, so the term is 20
        // only at the very top of the range and 0 everywhere below it. That reads
        // like a mistake and is kept anyway: it is the rate every cannister in
        // every shipped map has had, and "fixing" it would change all of them.
        return (SpreadSpeed: 15f * thrust * 0.001f,
                Speed: 128f + 100f * extra,
                Rate: 52f + (int)extra * 20,
                Length: 64f);
    }

    private void Fire(EntityHandle target, string input, float value)
        => Entities.FireInput(target, input,
                                  value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                  Handle);

    /// <summary>
    /// Puts it out for good.
    ///
    /// Spent rather than merely off, which is stock's behaviour said plainly:
    /// stock zeroes the fuel here, so nothing can light the cannister again. A
    /// flag says that better than a zeroed number, because the number is the
    /// map's and this is not the map changing its mind.
    /// </summary>
    private void Douse()
    {
        if (!_active)
            return;

        _active = false;
        _spent = true;

        _thruster?.Destroy();
        _thruster = null;
        _controllerId = 0u;

        StopJet();

        if (Vars.GasSound.Length > 0)
            Sound.EntityStopSound(Handle, Vars.GasSound);
    }

    /// <summary>
    /// Stops the plume and leaves the jet standing for a moment.
    ///
    /// Stock queues the jet's own "kill" five seconds out; there is no delay on
    /// EntityFireInput, so the wait is a think of this entity's instead. That is
    /// the better place for it anyway: the removal is now something the
    /// cannister does rather than something it asked the event queue to do on
    /// its behalf, so a cannister that explodes in the meantime takes its jet
    /// with it instead of leaving an entry pointing at a dead entity.
    /// </summary>
    private void StopJet()
    {
        if (!Entities.IsValid(_jet))
        {
            _jet = EntityHandle.None;
            StopThinking();
            return;
        }

        Entities.FireInput(_jet, "TurnOff", "", Handle);
        Fire(_jet, "Rate", 0f);

        SetNextThink(JetLinger);
    }

    /// <summary>
    /// The end of it.
    ///
    /// The thrust is stopped first, and not only for tidiness: the explosion
    /// removes this entity, and a motion controller left attached to a physics
    /// object that is about to go away is exactly the kind of dangling the
    /// handle table exists to prevent.
    /// </summary>
    private void Explode(EntityHandle attacker)
    {
        if (_exploded)
            return;

        _exploded = true;
        Douse();

        // Douse leaves a think pending to take the jet away in five seconds, and
        // this entity will not be here in five seconds. OnRemoved takes the jet
        // instead, so the timer is cancelled rather than left to fire against an
        // entity that has gone.
        StopThinking();

        // The launcher is credited where there is one, because a cannister lit
        // by a player and finished by the floor is still that player's kill.
        var owner = Entities.IsValid(attacker) ? attacker : _launcher;

        // The gibs first, as stock does: PropBreakableCreateAll( ..., 1.0, 20,
        // COLLISION_GROUP_DEBRIS ) before ExplosionCreate (physics_cannister.cpp:
        // 366-367), placed from where the cannister still is and thrown along
        // what it was doing.
        new PropBreak(Log, Entities, Physics, Precache).CreateAll(Vars.Model, Handle, new BreakContext(
            Origin: Entities.GetOrigin(Handle),
            Angles: Entities.GetAngles(Handle),
            Velocity: Entities.GetVelocity(Handle),
            AngularVelocity: Entities.GetAngularVelocity(Handle),
            DefaultBurstScale: 20f,
            DefaultCollisionGroup: CollisionGroup.Debris,
            SpawnFlags: SpawnFlags),
            side: BreakPieceSides.InMultiplayer(Environment, ConVars) ? BreakPieceSide.DefaultLocation : BreakPieceSide.All);

        // ignored: Handle -- an entity that explodes where it stands must say so,
        // or its own hull stops the very first trace of the traversal and every
        // victim in the radius is scored at distance zero. See Explosions.Fire.
        Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                        Origin, Vars.Damage, Vars.DamageRadius, owner,
                        ignored: Handle, inflictor: Handle);
        Remove();
    }

    /// <summary>
    /// Everything this entity made, given back.
    ///
    /// The jet especially: it is a separate entity that this one owns and
    /// nothing else knows about, so a cannister removed by a map cleanup would
    /// otherwise leave a steam plume hanging in the air forever.
    /// </summary>
    public override void OnRemoved()
    {
        _thruster?.Destroy();
        _thruster = null;
        _controllerId = 0u;

        if (Entities.IsValid(_jet))
            Entities.Remove(_jet);

        _jet = EntityHandle.None;
    }
}

/// <summary>
/// The map keys <see cref="PhysicsCannister"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysicsCannisterVars
{
    /// <summary>The model, which also supplies the collision shape and the mass.</summary>
    public string Model { get; set; } = string.Empty;

    public int Health { get; set; }

    /// <summary>
    /// How hard it pushes, as an acceleration.
    ///
    /// Stock leaves this at zero when a map does not say, and so does this: a
    /// cannister with no thrust key is a barrel that explodes, which is a thing
    /// a map may legitimately want, and inventing a default would make every
    /// such cannister take off.
    /// </summary>
    public float Thrust { get; set; }

    /// <summary>Seconds of thrust. Zero means it will never light at all.</summary>
    [VarName("fuel")]
    public float ThrustTime { get; set; }

    [VarName("expdamage")]
    public float Damage { get; set; }

    /// <summary>
    /// How far the explosion reaches.
    ///
    /// Stock reads this key and then passes a literal 0 to ExplosionCreate, so
    /// no map has ever had it do anything. It is honoured here, which is a
    /// divergence in only one direction: 0 already means "work the radius out
    /// from the magnitude", so a map that never wrote the key is unchanged and
    /// a map that did now gets what it asked for. See docs/divergences.md.
    /// </summary>
    [VarName("expradius")]
    public float DamageRadius { get; set; }

    /// <summary>The hiss while it is venting. Stopped when it stops.</summary>
    public string GasSound { get; set; } = string.Empty;
}
