//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Stock's PhysGunPickup_t.</summary>
public enum PhysGunPickup
{
    PickedUpByCannon,
    PuntedByCannon,
    PickedUpByPlayer,
}

/// <summary>Stock's PhysGunDrop_t.</summary>
public enum PhysGunDrop
{
    DroppedByPlayer,
    ThrownByPlayer,
    DroppedByCannon,
    LaunchedByCannon,
}

/// <summary>
/// What an entity answers the gravity gun: stock's IPlayerPickupVPhysics, the
/// part of it HL2MP's cannon asks (player_pickup.h). Every member has the
/// default stock's CDefaultPlayerPickupVPhysics gives, so an entity that
/// implements nothing behaves as an entity with no interface at all.
/// </summary>
public interface IPhysGunTarget
{
    /// <summary>
    /// FVPHYSICS_NO_PLAYER_PICKUP from the entity's own rules
    /// (SF_PHYSPROP_PREVENT_PICKUP sets it at spawn in stock).
    /// </summary>
    bool PreventsPickup => false;

    /// <summary>A motion-disabled object the cannon may still take (SF_PHYSPROP_ENABLE_ON_PHYSCANNON).</summary>
    bool EnablesOnPhyscannon => false;

    /// <summary>ForcePhysgunOpen: the claws open at this even when it cannot be picked up.</summary>
    bool ForcePhysgunOpen(EntityHandle player) => false;

    /// <summary>OnAttemptPhysGunPickup: false while it must first be torn loose.</summary>
    bool OnAttemptPhysGunPickup(EntityHandle player, PhysGunPickup reason) => true;

    /// <summary>OnPhysGunPickup: picked up or punted.</summary>
    void OnPhysGunPickup(EntityHandle player, PhysGunPickup reason) { }

    /// <summary>OnPhysGunDrop: let go, or launched.</summary>
    void OnPhysGunDrop(EntityHandle player, PhysGunDrop reason) { }
}

/// <summary>
/// The server's physcannon world (<see cref="IPhysCannonWorld"/>): the halves
/// of weapon_physcannon.cpp under <c>#ifndef CLIENT_DLL</c> and the grab
/// controller they drive, one per carrier.
///
/// <para>
/// Traces are stock's: <c>MASK_SHOT | CONTENTS_GRATE</c> from the carrier's
/// shoot position along its eyes. Stock's CTraceFilterNoOwnerTest (which lets
/// a player grab what he owns, his own grenades) is a simple filter passing
/// the carrier here, so an object the carrier owns is not found; the held
/// object is owned by its holder, which is what lets the hold trace skip it.
/// </para>
/// </summary>
public sealed class PhysCannonWorld : IPhysCannonWorld, IGameLifecycle
{
    // MASK_SHOT | CONTENTS_GRATE.
    private const uint MaskShotGrate = 0x1 | 0x4000 | 0x2000000 | 0x2 | 0x4000000 | 0x40000000 | 0x8;

    // hl2_normspeed's default: a dropped object leaves no faster than 1.5x a run.
    private const float NormSpeed = 190f;

    // FVPHYSICS_NO_PLAYER_PICKUP.
    private const ushort NoPlayerPickup = 0x0080;

    // CHAR_TEX_ANTLION, CHAR_TEX_FLESH, CHAR_TEX_BLOODYFLESH, CHAR_TEX_ALIENFLESH.
    private static bool IsFleshMaterial(int material) => material is 'A' or 'F' or 'B' or 'H';

    private readonly ILog _log;
    private readonly IEngineEntities _entities;
    private readonly IEnginePlayers _players;
    private readonly IEngineEntityPhysics _physics;
    private readonly IVPhysicsGrabCalls _v;
    private readonly ITraceQueries _traces;
    private readonly IEngineWorldQuery _world;
    private readonly IEngineDamage _damage;
    private readonly IScreenEffects _screen;
    private readonly ITempEnts _tempEnts;
    private readonly IEntityDirectory _directory;
    private readonly IEnginePrecache _precache;
    private readonly ICustomMotionControllers _controllers;
    private readonly IEngineEnvironment _environment;
    private readonly Random _random = new();

    private readonly Dictionary<int, Carrier> _carriers = new();

    public PhysCannonWorld(ILog log, IEngineEntities entities, IEnginePlayers players,
                           IEngineEntityPhysics physics, IVPhysicsGrabCalls v, ITraceQueries traces,
                           IEngineWorldQuery world, IEngineDamage damage, IScreenEffects screen,
                           ITempEnts tempEnts, IEntityDirectory directory, IEnginePrecache precache,
                           ICustomMotionControllers controllers, IEngineEnvironment environment)
    {
        _log = log;
        _entities = entities;
        _players = players;
        _physics = physics;
        _v = v;
        _traces = traces;
        _world = world;
        _damage = damage;
        _screen = screen;
        _tempEnts = tempEnts;
        _directory = directory;
        _precache = precache;
        _controllers = controllers;
        _environment = environment;
    }

    /// <summary>One carrier's cannon: its grab controller and what its last punt trace found.</summary>
    private sealed class Carrier(GrabController grab)
    {
        public readonly GrabController Grab = grab;
        public EntityHandle Player;
        public bool ResetOwner;
        public EntityHandle PuntTarget;
        public Vector PuntEnd;
        public EntityHandle LastLetGo;
    }

    public bool Authoritative => true;

    //-------------------------------------------------------------------------
    // The level.
    //-------------------------------------------------------------------------

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysCannon;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        _carriers.Clear();

        // CWeaponPhysCannon::Precache: the two beam sprites the blast draws
        // with, the hold loop and the script's sounds.
        _precache.PrecacheModel(BeamSprite);
        _precache.PrecacheModel(BeamSpriteNoZ);

        foreach (string sound in PhysCannonSounds.All)
            _precache.PrecacheSound(sound);
    }

    /// <summary>PHYSCANNON_BEAM_SPRITE.</summary>
    public const string BeamSprite = "sprites/orangelight1.vmt";

    /// <summary>PHYSCANNON_BEAM_SPRITE_NOZ.</summary>
    public const string BeamSpriteNoZ = "sprites/orangelight1_noz.vmt";

    // The controllers die with the environment (PhysicsMotionControllers
    // frees its table at level shutdown); what held them goes with them.
    void IGameLifecycle.OnLevelShutdown() => _carriers.Clear();

    private Carrier? For(int owner)
    {
        EntityHandle player = _players.GetHandle(owner);

        if (player == EntityHandle.None)
            return null;

        if (!_carriers.TryGetValue(owner, out var carrier))
        {
            carrier = new Carrier(new GrabController(_v, _physics, _controllers));
            _carriers[owner] = carrier;
        }

        carrier.Player = player;
        return carrier;
    }

    /// <summary>The grab controller of a carrier, for the reports and the facts.</summary>
    public GrabController? GrabOf(int owner) => _carriers.TryGetValue(owner, out var c) ? c.Grab : null;

    //-------------------------------------------------------------------------
    // Holding.
    //-------------------------------------------------------------------------

    public int Attached(int owner)
        => _carriers.TryGetValue(owner, out var c) && c.Grab.Attached != EntityHandle.None
            ? _entities.GetIndex(c.Grab.Attached)
            : 0;

    public bool UpdateHeld(int owner)
    {
        if (For(owner) is not { } c || c.Grab.Attached == EntityHandle.None)
            return false;

        EntityHandle held = c.Grab.Attached;

        if (!_entities.IsValid(held) || _physics.PhysicsObject(held) == 0)
        {
            // Removed or broken while held: nothing to give its mass back to.
            c.Grab.Forget();
            return false;
        }

        Vector eyes = _entities.GetEyeAngles(c.Player);
        (Vector mins, Vector maxs) = _entities.GetBounds(c.Player);
        Vector origin = _entities.GetOrigin(c.Player);
        bool standingOn = _directory.Get(c.Player) is SsPlayer p && p.GroundIndex == _entities.GetIndex(held);

        var view = new GrabView(eyes, _players.GetShootOrigin(c.Player), maxs,
                                origin + mins, origin + maxs, _entities.GetWorldSpaceCenter(c.Player), standingOn);

        Globals globals = _environment.Globals;
        int frame = globals.IsAvailable ? globals.FrameCount : -1;
        float frameTime = globals.IsAvailable ? globals.FrameTime : 0f;

        EntityHandle player = c.Player;
        return c.Grab.UpdateObject(in view, PlayerToWorld(eyes), PhysCannonTuning.HoldError, frame, frameTime,
                                   (from, to, mask) => _traces.Line(from, to, mask, player).Fraction);
    }

    /// <summary>
    /// The carrier's EntityToWorldTransform as the gun reads it. Stock's
    /// FinishMove sets the player's angles to the view, pitch and all, and
    /// CHL2MP_Player::PostThink zeroes the pitch only after the base
    /// PostThink's ItemPostFrame: so the frame ItemPostFrame sees (the
    /// pickup's) is the whole view, and the one ItemPreFrame sees (every
    /// hold update) is its yaw alone. An object picked up looking down is
    /// therefore turned by that pitch as it comes up, as it is in stock.
    /// </summary>
    public static GrabMatrix CarrierTransform(Vector eyes, bool inPostFrame)
        => GrabMath.AngleMatrix(inPostFrame ? new QAngle(eyes.X, eyes.Y, eyes.Z) : new QAngle(0f, eyes.Y, 0f));

    private static GrabMatrix PlayerToWorld(Vector eyes) => CarrierTransform(eyes, inPostFrame: false);

    public void DetachHeld(int owner, bool launched)
    {
        if (!_carriers.TryGetValue(owner, out var c))
            return;

        EntityHandle held = c.Grab.Attached;

        c.Grab.DetachEntity(launched, NormSpeed * 1.5f);
        c.LastLetGo = held;

        if (held == EntityHandle.None || !_entities.IsValid(held))
            return;

        Target(held)?.OnPhysGunDrop(c.Player, launched ? PhysGunDrop.LaunchedByCannon : PhysGunDrop.DroppedByCannon);

        if (c.ResetOwner)
            _entities.SetOwner(held, EntityHandle.None);

        c.ResetOwner = false;
    }

    public float HeldDistance(int owner, int held)
        => For(owner) is { } c && c.Grab.Attached != EntityHandle.None
            ? (_entities.GetWorldSpaceCenter(c.Grab.Attached) - _entities.GetWorldSpaceCenter(c.Player)).Length
            : -1f;

    public void Launch(int owner, int entity)
    {
        if (For(owner) is not { } c)
            return;

        // The object just let go of: DetachHeld has given it back its mass.
        EntityHandle launched = c.LastLetGo;
        if (launched == EntityHandle.None || !_entities.IsValid(launched) || _entities.GetIndex(launched) != entity)
            return;

        ApplyVelocityBasedForce(launched, Forward(c.Player));

        // Do repulse effect
        LaunchEffect(c.Player, _entities.GetWorldSpaceCenter(launched));
    }

    //-------------------------------------------------------------------------
    // Finding.
    //-------------------------------------------------------------------------

    public bool TargetAhead(int owner)
    {
        if (For(owner) is not { } c)
            return false;

        Vector start = _players.GetShootOrigin(c.Player);
        Vector end = start + Forward(c.Player) * PhysCannonTuning.TraceLength;

        TraceAnswer tr = _traces.Hull(start, end, new Vector(-4, -4, -4), new Vector(4, 4, 4), MaskShotGrate, c.Player);

        if (tr.Fraction != 1f && tr.Entity != EntityHandle.None)
        {
            // FIXME (stock's): Try just having the elements always open when pointed at a physics object
            return CanPickupObject(c, tr.Entity) || Target(tr.Entity)?.ForcePhysgunOpen(c.Player) == true;
        }

        return false;
    }

    public PhysCannonFind FindObject(int owner, out int attached)
    {
        attached = 0;

        if (For(owner) is not { } c)
            return PhysCannonFind.NotFound;

        Vector forward = Forward(c.Player);

        // Setup our positions
        Vector start = _players.GetShootOrigin(c.Player);
        float testLength = PhysCannonTuning.TraceLength * 4.0f;
        Vector end = start + forward * testLength;

        // Try to find an object by looking straight ahead
        TraceAnswer tr = _traces.Line(start, end, MaskShotGrate, c.Player);

        // Try again with a hull trace
        if (tr.Fraction == 1.0f || tr.Entity == EntityHandle.None || IsWorld(tr.Entity))
            tr = _traces.Hull(start, end, new Vector(-4, -4, -4), new Vector(4, 4, 4), MaskShotGrate, c.Player);

        EntityHandle entity = tr.Entity != EntityHandle.None ? RootMoveParent(tr.Entity) : EntityHandle.None;
        bool attach = false;
        bool pull = false;

        // If we hit something, pick it up or pull it
        if (tr.Fraction != 1.0f && tr.Entity != EntityHandle.None && !IsWorld(tr.Entity))
        {
            // Attempt to attach if within range
            if (tr.Fraction <= 0.25f)
                attach = true;
            else
                pull = true;
        }

        // Find anything within a general cone in front
        EntityHandle cone = EntityHandle.None;

        if (!attach && !pull)
            cone = FindObjectInCone(c, start, forward, PhysCannonTuning.Cone);

        if (cone != EntityHandle.None)
        {
            entity = cone;

            // If the object is near, grab it. Else, pull it a bit.
            if ((_entities.GetWorldSpaceCenter(entity) - start).LengthSquared <= testLength * testLength)
                attach = true;
            else
                pull = true;
        }

        if (!CanPickupObject(c, entity))
            return PhysCannonFind.Denied;

        // Check to see if the object is constrained + needs to be ripped off...
        if (Target(entity) is { } target && !target.OnAttemptPhysGunPickup(c.Player, PhysGunPickup.PickedUpByCannon))
            return PhysCannonFind.BeingDetached;

        if (attach)
        {
            if (!AttachObject(c, entity))
                return PhysCannonFind.NotFound;

            attached = _entities.GetIndex(entity);
            return PhysCannonFind.Found;
        }

        if (!pull)
            return PhysCannonFind.NotFound;

        nint obj = _physics.PhysicsObject(entity);
        if (obj == 0)
            return PhysCannonFind.NotFound;

        // If we're too far, simply start to pull the object towards us
        Vector pullDir = start - _entities.GetWorldSpaceCenter(entity);
        GrabMath.Normalize(ref pullDir);
        pullDir *= PhysCannonTuning.PullForce;

        float mass = _v.Mass(obj);
        if (mass < 50.0f)
            pullDir *= (mass + 0.5f) * (1 / 50.0f);

        // Nudge it towards us
        _v.ApplyForceCenter(obj, pullDir);
        return PhysCannonFind.NotFound;
    }

    private EntityHandle FindObjectInCone(Carrier c, Vector origin, Vector dir, float coneCosine)
    {
        // Find the nearest physics-based item in a cone in front of me.
        float nearestDist = PhysCannonTuning.TraceLength + 1.0f;
        Span<EntityHandle> list = stackalloc EntityHandle[256];
        int count = _world.EntitiesInSphere(origin, nearestDist * MathF.Sqrt(3f), list);

        EntityHandle nearest = EntityHandle.None;

        for (int i = 0; i < count; i++)
        {
            EntityHandle candidate = list[i];

            if (candidate == EntityHandle.None || _physics.PhysicsObject(candidate) == 0)
                continue;

            // Closer than other objects
            Vector los = _entities.GetWorldSpaceCenter(candidate) - origin;
            float dist = GrabMath.Normalize(ref los);
            if (dist >= nearestDist)
                continue;

            // Cull to the cone
            if (los.Dot(dir) <= coneCosine)
                continue;

            // Make sure it isn't occluded!
            TraceAnswer tr = _traces.Line(origin, _entities.GetWorldSpaceCenter(candidate), MaskShotGrate, c.Player);
            if (tr.Entity == candidate)
            {
                nearestDist = dist;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private bool AttachObject(Carrier c, EntityHandle entity)
    {
        if (c.Grab.Attached != EntityHandle.None)
            return false;

        if (!CanPickupObject(c, entity))
            return false;

        if (_physics.PhysicsObject(entity) == 0)
            return false;

        // NOTE: This can change the mass; so it must be done before max speed setting
        OnPhysGunPickup(c, entity, PhysGunPickup.PickedUpByCannon);

        Globals globals = _environment.Globals;
        Vector eyes = _entities.GetEyeAngles(c.Player);

        // NOTE: This must happen after OnPhysGunPickup because that can change the mass
        if (!c.Grab.AttachEntity(entity, CarrierTransform(eyes, inPostFrame: true),
                                 GrabMath.AngleMatrix(QAngle.FromVector(_entities.GetAngles(entity))),
                                 _entities.GetOrigin(entity), _entities.GetWorldSpaceCenter(entity),
                                 globals.IsAvailable ? globals.FrameTime : 0f))
        {
            _log.Warning($"[SourceSharp/PhysCannon] could not take hold of {entity}\n");
            return false;
        }

        c.ResetOwner = false;

        if (OwnerOf(entity) == EntityHandle.None)
        {
            _entities.SetOwner(entity, c.Player);
            c.ResetOwner = true;
        }

        return true;
    }

    /// <summary>
    /// CWeaponPhysCannon::CanPickupObject over CBasePlayer::CanPickupObject
    /// (player.cpp, HL2_DLL) with the cannon's mass limit.
    /// </summary>
    private bool CanPickupObject(Carrier c, EntityHandle target)
    {
        if (target == EntityHandle.None || IsWorld(target))
            return false;

        if (_directory.Get(c.Player) is SsPlayer p && p.GroundIndex == _entities.GetIndex(target))
            return false;

        nint obj = _physics.PhysicsObject(target);

        if (obj != 0 && IsFleshMaterial((int)_world.SurfacePropGameMaterial(_v.MaterialIndex(obj))))
            return false;

        if (obj != 0 && (_v.GameFlags(obj) & GrabController.PlayerHeld) != 0)
            return false;

        // CBasePlayer::CanPickupObject: must move with physics, and have an object.
        if (MoveTypeOf(target) != MoveType.VPhysics || obj == 0)
            return false;

        IPhysGunTarget? rules = Target(target);

        if ((_v.GameFlags(obj) & NoPlayerPickup) != 0 || rules?.PreventsPickup == true)
            return false;

        // Must be under our threshold weight
        if (_v.Mass(obj) > PhysCannonTuning.MaxMass)
            return false;

        // Allow pickup of phys props that are motion enabled on player pickup
        if (!_v.IsMoveable(obj) && rules?.EnablesOnPhyscannon != true)
            return false;

        return true;
    }

    //-------------------------------------------------------------------------
    // Punting.
    //-------------------------------------------------------------------------

    public PhysCannonPunt AimPunt(int owner, out int entity)
    {
        entity = 0;

        if (For(owner) is not { } c)
            return PhysCannonPunt.DryFire;

        Vector forward = Forward(c.Player);

        // NOTE (stock's): punting has shorter range than the pull.
        Vector start = _players.GetShootOrigin(c.Player);
        Vector end = start + forward * PhysCannonTuning.TraceLength;

        TraceAnswer tr = _traces.Hull(start, end, new Vector(-8, -8, -8), new Vector(8, 8, 8), MaskShotGrate, c.Player);
        bool valid = !(tr.Fraction == 1 || tr.Entity == EntityHandle.None);

        if (valid && MoveTypeOf(tr.Entity) != MoveType.VPhysics && !TakesDamage(tr.Entity))
            valid = false;

        // If the entity we've hit is invalid, try a traceline instead
        if (!valid)
        {
            tr = _traces.Line(start, end, MaskShotGrate, c.Player);
            if (tr.Fraction == 1 || tr.Entity == EntityHandle.None)
                return PhysCannonPunt.DryFire;
        }

        EntityHandle hit = tr.Entity;
        c.PuntTarget = hit;
        c.PuntEnd = tr.End;
        entity = _entities.GetIndex(hit);

        // See if we hit something
        if (MoveTypeOf(hit) != MoveType.VPhysics)
        {
            // Don't let the player zap any NPC's except regular antlions and headcrabs.
            if (!TakesDamage(hit) || _entities.IsPlayer(hit))
                return PhysCannonPunt.DryFire;

            return PhysCannonPunt.NonVPhysics;
        }

        nint obj = _physics.PhysicsObject(hit);
        if (obj != 0 && IsFleshMaterial((int)_world.SurfacePropGameMaterial(_v.MaterialIndex(obj))))
            return PhysCannonPunt.DryFire;

        return PhysCannonPunt.VPhysics;
    }

    public void PuntNonVPhysics(int owner, int entity)
    {
        if (For(owner) is not { } c || c.PuntTarget == EntityHandle.None)
            return;

        Vector forward = Forward(c.Player);
        _damage.ApplyAt(c.PuntTarget, c.Player, 1.0f, DamageType.Crush | DamageType.PhysGun, forward, c.PuntEnd);

        // Explosion effect
        LaunchEffect(c.Player, c.PuntEnd);
    }

    public bool PuntVPhysics(int owner, int entity)
    {
        if (For(owner) is not { } c || c.PuntTarget == EntityHandle.None)
            return false;

        EntityHandle target = c.PuntTarget;
        Vector vecForward = Forward(c.Player);
        Vector forward = vecForward;

        // The info's zero damage, DMG_PHYSGUN, dispatched as a trace attack.
        _damage.ApplyAt(target, c.Player, 0f, DamageType.PhysGun, Vector.Zero, c.PuntEnd);

        IPhysGunTarget? rules = Target(target);

        if (rules?.OnAttemptPhysGunPickup(c.Player, PhysGunPickup.PuntedByCannon) != false)
        {
            nint obj = _physics.PhysicsObject(target);

            if (obj == 0)
            {
                // FIXME (stock's): Do we want to do this if there's no physics object?
                OnPhysGunPickup(c, target, PhysGunPickup.PuntedByCannon);
                return false;
            }

            // reflect, but flatten the trajectory out a bit so it's easier to hit standing targets
            if (forward.Z < 0)
                forward.Z *= -0.65f;

            // NOTE: Do this first to enable motion (if disabled) - so forces will work.
            // Tell the object it's been punted.
            OnPhysGunPickup(c, target, PhysGunPickup.PuntedByCannon);

            // Not Pickup_ShouldPuntUseLaunchForces: no entity here asks for the
            // launch forces (stock's only taker is the cannister).
            float totalMass = _v.Mass(obj);

            // limit mass to avoid punting REALLY huge things
            float mass = MathF.Min(totalMass, 250f);

            // Put some spin on the object: the hit object's share, plus half.
            const float hitObjectFactor = 0.5f;
            float ratio = totalMass > 0f ? _v.Mass(obj) / totalMass : 1f;
            ratio += hitObjectFactor;
            ratio = MathF.Min(ratio, 1.0f);

            _v.ApplyForceCenter(obj, forward * 15000.0f * ratio);
            _v.ApplyForceOffset(obj, forward * mass * 600.0f * ratio, c.PuntEnd);
        }

        // Explosion effect
        LaunchEffect(c.Player, c.PuntEnd);
        return true;
    }

    /// <summary>ApplyVelocityBasedForce: the launch speed for the mass, along <paramref name="forward"/>, and a random spin.</summary>
    private void ApplyVelocityBasedForce(EntityHandle entity, Vector forward)
    {
        nint obj = _physics.PhysicsObject(entity);
        if (obj == 0)
            return;

        Vector vVel = forward * PhysCannonTuning.LaunchSpeed(_v.Mass(obj));

        // Pickup_PhysGunLaunchAngularImpulse's default: RandomAngularImpulse( -600, 600 ).
        Vector aVel = new(RandomFloat(-600f, 600f), RandomFloat(-600f, 600f), RandomFloat(-600f, 600f));

        _v.AddVelocity(obj, vVel, aVel);
    }

    /// <summary>
    /// DoEffectLaunch's dispatch: "PhyscannonImpact" where the blast landed,
    /// naming the gun's carrier (stock names the weapon, whose attachment 1
    /// the client's callback reads).
    /// </summary>
    private void LaunchEffect(EntityHandle source, Vector at)
    {
        var data = new TeEffectData
        {
            Origin = at,
            EntIndex = _entities.GetIndex(source),
        };

        _tempEnts.DispatchEffect(TeFilters.Pvs(at), 0f, "PhyscannonImpact", in data);
    }

    //-------------------------------------------------------------------------
    // The carrier.
    //-------------------------------------------------------------------------

    public void ViewPunch(int owner, QAngle punch)
    {
        // CBasePlayer::ViewPunch: the punch's velocity, twenty times the kick.
        if (_directory.Get(_players.GetHandle(owner)) is SsPlayer player)
            player.State.PunchAngleVel = QAngle.FromVector(player.State.PunchAngleVel.AsVector() + punch.AsVector() * 20f);
    }

    public void FireFlash(int owner)
    {
        EntityHandle player = _players.GetHandle(owner);

        if (player != EntityHandle.None)
            _screen.Fade(player, 245, 245, 255, 32, 0.1f, 0f, FadeFlags.In);
    }

    public float RandomFloat(float low, float high) => low + (float)_random.NextDouble() * (high - low);

    public int RandomInt(int low, int high) => _random.Next(low, high + 1);

    //-------------------------------------------------------------------------

    private Vector Forward(EntityHandle player) => Angles.Vectors(_entities.GetEyeAngles(player)).Forward;

    private bool IsWorld(EntityHandle entity) => _entities.GetIndex(entity) == 0;

    private IPhysGunTarget? Target(EntityHandle entity) => _directory.Get(entity) as IPhysGunTarget;

    /// <summary>
    /// Physgun_OnPhysGunPickup. Stock first turns debris into interactive
    /// debris, which converts back when it comes to rest; the engine area's
    /// collision groups have no interactive debris to say that with, so a
    /// debris object stays debris while held.
    /// </summary>
    private void OnPhysGunPickup(Carrier c, EntityHandle entity, PhysGunPickup reason)
        => Target(entity)?.OnPhysGunPickup(c.Player, reason);

    /// <summary>GetRootMoveParent: the top of the entity's parent chain.</summary>
    private EntityHandle RootMoveParent(EntityHandle entity)
    {
        for (int depth = 0; depth < 32; depth++)
        {
            EntityHandle parent = _entities.GetParent(entity);
            if (parent == EntityHandle.None)
                break;
            entity = parent;
        }

        return entity;
    }

    private MoveType MoveTypeOf(EntityHandle entity) => EngineBlockMoveType.Read(_entities, entity);

    private EntityHandle OwnerOf(EntityHandle entity) => EngineBlockOwner.Read(_entities, entity);

    /// <summary>
    /// Whether m_takedamage is anything but DAMAGE_NO. The engine area
    /// answers that only by setting it, so this reads health instead: what a
    /// map makes breakable has some, and the world and the brushes have none.
    /// </summary>
    private bool TakesDamage(EntityHandle entity) => !IsWorld(entity) && _entities.GetHealth(entity) > 0;
}
