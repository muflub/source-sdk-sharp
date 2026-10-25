//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary><see cref="IPlayerTouchWorld"/> over one side's engine: the entity's block read in place, and its held object.</summary>
internal sealed class EnginePlayerTouchWorld(EngineSide side, INetworkServerArea network) : IPlayerTouchWorld
{
    private static readonly int MoveTypeOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.MoveType));
    private static readonly int SolidOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidType));
    private static readonly int SolidFlagsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.SolidFlags));

    public bool TryEntity(EntityHandle entity, out int moveType, out int solidType, out int solidFlags, out nint physicsObject)
    {
        Span<byte> block = side.GetEntityBlock(network, entity);
        if (block.Length < EngineBlock.SizeBytes)
        {
            moveType = solidType = solidFlags = 0;
            physicsObject = 0;
            return false;
        }

        moveType = MemoryMarshal.Read<int>(block.Slice(MoveTypeOffset));
        solidType = MemoryMarshal.Read<int>(block.Slice(SolidOffset));
        solidFlags = MemoryMarshal.Read<int>(block.Slice(SolidFlagsOffset));
        physicsObject = side.EntityPhysicsObject(entity);
        return true;
    }
}

/// <summary>A player's shadow as the solver left it, for VPhysicsShadowUpdate (PlayerShadowRecovery).</summary>
public interface IPlayerShadowProbe
{
    /// <summary>Null with no shadow, or no such player.</summary>
    PlayerShadowState? Probe(EntityHandle player);
}

/// <summary>The player shadow as the game asks for it: a body at spawn, a probe, and the teleport.</summary>
public interface IPlayerShadows : IPlayerShadowProbe
{
    /// <summary>A body for the player, or the one it has teleported to where it is (a respawn). True when it has one after.</summary>
    bool Init(EntityHandle player, Vector standMins, Vector standMaxs, Vector crouchMins, Vector crouchMaxs,
              float mass, float pushMassLimit, float pushSpeedLimit);

    /// <summary>The player was teleported: its body goes with it.</summary>
    void Teleport(EntityHandle player, Vector origin, bool zeroVelocity);
}

/// <summary><see cref="IPlayerShadowNative"/> over one side's engine slots.</summary>
internal sealed class EnginePlayerShadowNative(EngineSide side, IMovementServerArea movement) : IPlayerShadowNative
{
    public bool Facts(EntityHandle player, out PlayerShadowFacts facts) => side.PlayerShadowFacts(movement, player, out facts);

    public bool Adopt(EntityHandle player, nint stand, nint crouch, nint controller)
        => side.PlayerShadowAdopt(movement, player, stand, crouch, controller);

    public void Release(EntityHandle player) => side.PlayerShadowRelease(movement, player);

    public void Commit(EntityHandle player, int state, bool afterCommand) => side.PlayerShadowCommit(movement, player, state, afterCommand);

    public nint Environment() => side.PhysicsEnvironmentPointer();
}

/// <summary>
/// The player's physics shadow, managed: the body's creation, its hull state,
/// the per-command apply, the teleport, and the probe's vphysics reads
/// (plan_physics_bones_native_zero 2.5; native's CStandalonePlayer::
/// InitVCollision, SetVCollisionState, ShadowProbe, ShadowApply and
/// TeleportShadow at bc21e4d46, which are stock's CBasePlayer::InitVCollision /
/// SetupVPhysicsShadow / SetVCollisionState / UpdateVPhysicsPosition, player.cpp).
///
/// <para>
/// Every vphysics call is made here, through vphysics' own tables
/// (<see cref="IVPhysicsShadowCalls"/>). Native holds the pointers on the
/// player, because the player's destructor is the body's one teardown owner
/// (SsPlayer.OnRemoved's note: a managed second owner was tried and reverted).
/// <see cref="PlayerShadow.Plan"/> stays the per-command decision.
/// </para>
/// </summary>
public sealed class PlayerShadowBodies : IPlayerShadows
{
    /// <summary>CALLBACK_GLOBAL_COLLISION | CALLBACK_SHADOW_COLLISION: both hulls'.</summary>
    public const ushort HullCallbackFlags = 0x0001 | 0x0010;

    /// <summary>Stock's inertia: not a large number so much as an infinite one -- the shadow must not tip.</summary>
    public const float HullInertia = 1e24f;

    /// <summary>g_PhysDefaultObjectParams' damping, rotdamping and rotInertiaLimit, which stock leaves alone.</summary>
    public const float HullDamping = 0.1f, HullRotDamping = 0.1f, HullRotInertiaLimit = 0.05f;

    /// <summary>The surfaceprop both hulls are made of.</summary>
    public const string HullSurfaceProp = "player";

    /// <summary>MOVETYPE_VPHYSICS: the only ground a shadow can stand on as physics.</summary>
    public const int MoveTypeVPhysics = 6;

    /// <summary>FL_ONGROUND and FL_DUCKING.</summary>
    public const int FlOnGround = 1 << 0, FlDucking = 1 << 1;

    private readonly IPlayerShadowNative _native;
    private readonly IVPhysicsShadowCalls _v;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _warning;
    private readonly IPlayerTouchWorld? _touches;

    // THE TWO NAMES VPHYSICS KEEPS. objectparams_t::pName is stored, not
    // copied, and read back by GetName for as long as the object lives -- the
    // SS-PHYSWORLD report prints it. So they are unmanaged, made once per
    // instance and never freed: an object can outlive this instance across a
    // hot reload, and two short strings are the price of that never dangling.
    // The two hulls' names, made on the first body built rather than in the
    // constructor, and kept for the bodies' lifetime.
    private nint _standName;
    private nint _crouchName;

    private nint StandName => _standName != 0 ? _standName : _standName = Marshal.StringToCoTaskMemUTF8("player_stand");

    private nint CrouchName => _crouchName != 0 ? _crouchName : _crouchName = Marshal.StringToCoTaskMemUTF8("player_crouch");

    /// <param name="enabled"><c>sourcesharp_player_shadow</c>: whether a body may be built at all.</param>
    /// <param name="warning">Where a refusal is said (native's Warning).</param>
    /// <param name="touches">What the touched-physics decision reads of a touched entity; null touches nothing.</param>
    /// <param name="memory">The players' m_oldOrigin and m_touchedPhysObject; null makes this instance's own.</param>
    public PlayerShadowBodies(IPlayerShadowNative native, IVPhysicsShadowCalls v, Func<bool> enabled,
                              Action<string> warning, IPlayerTouchWorld? touches = null, PlayerMoveMemory? memory = null)
    {
        _native = native;
        _v = v;
        _enabled = enabled;
        _warning = warning;
        _touches = touches;
        Memory = memory ?? new PlayerMoveMemory();
    }

    /// <summary>The players' stock m_oldOrigin and m_touchedPhysObject, shared with PlayerShadowRecovery.</summary>
    public PlayerMoveMemory Memory { get; }

    /// <summary>
    /// The server composition's bodies, over <paramref name="engine"/>'s own
    /// side: native's half through its slots, and every teleport that side
    /// carries out handed to <see cref="Teleport"/> afterwards.
    /// </summary>
    public static PlayerShadowBodies ForEngine(ModuleEngine engine, IVPhysicsShadowCalls v, Func<bool> enabled,
                                               Action<string> warning,
                                               SourceSharp.Server.Interop.ServerPlayerMoves? moves = null,
                                               SourceSharp.Server.Interop.ServerMoveData? data = null)
    {
        ArgumentNullException.ThrowIfNull(engine);

        // The movement words are the managed command loop's when it runs
        // (plan_server_native_v2 T4g).
        var areas = engine.Attachment.Areas;
        IPlayerShadowNative native = new EnginePlayerShadowNative(engine.Api, areas.Require<IMovementServerArea>());
        if (moves is not null && data is not null)
            native = new SourceSharp.Server.Interop.MovedPlayerShadowNative(native, moves, data);

        var bodies = new PlayerShadowBodies(native, v, enabled, warning,
                                            new EnginePlayerTouchWorld(engine.Api, areas.Require<INetworkServerArea>()));
        engine.Api.Teleported = bodies.Teleport;
        return bodies;
    }

    /// <summary>The refusals of a non-finite value at the four places the game hands one over.</summary>
    public PhysicsNanGuard Nan { get; } = new();

    /// <summary>Shadows built since the module started.</summary>
    public int Built { get; private set; }

    /// <summary>Probes: one per command step and one per VPhysicsShadowUpdate.</summary>
    public int Updates { get; private set; }

    /// <summary>Probes with either contact bit set.</summary>
    public int Contacts { get; private set; }

    /// <summary>Probes with the move's touch set.</summary>
    public int Touches { get; private set; }

    /// <summary>Snaps applied (the plan never asks for one today; stock has none).</summary>
    public int Snaps { get; private set; }

    /// <summary>Whether a body may be built: <c>sourcesharp_player_shadow</c>.</summary>
    public bool Enabled => _enabled();

    // m_pShadowActive, derived: the hull the state names.
    private static nint Active(in PlayerShadowFacts f)
        => f.State == PlayerShadow.StateWalk ? f.Stand : f.State == PlayerShadow.StateCrouch ? f.Crouch : 0;

    // GetGroundVPhysics (player.cpp:4614-4626): the ground's own object when it
    // is a moveable MOVETYPE_VPHYSICS one, else nothing.
    private nint GroundObject(in PlayerShadowFacts f)
        => f.GroundMoveType == MoveTypeVPhysics && f.GroundObject != 0 && _v.IsMoveable(f.GroundObject)
            ? f.GroundObject
            : 0;

    /// <summary>
    /// CBasePlayer::InitVCollision and SetupVPhysicsShadow: two box hulls and
    /// a player controller, or -- for a player who already has a body, which is
    /// a respawn -- that body teleported to where the player is now. True when
    /// the player has a shadow afterwards.
    /// </summary>
    public bool Init(EntityHandle player, Vector standMins, Vector standMaxs, Vector crouchMins, Vector crouchMaxs,
                     float mass, float pushMassLimit, float pushSpeedLimit)
    {
        if (!_native.Facts(player, out PlayerShadowFacts f))
            return false;

        // A body built for the previous level is three pointers into freed
        // memory: dropped (native frees nothing for it), and a new one built.
        if (f.HasShadow != 0 && f.BodyCurrent == 0)
        {
            _native.Release(player);
            f.HasShadow = 0;
        }

        int state = (f.Flags & FlDucking) != 0 ? PlayerShadow.StateCrouch : PlayerShadow.StateWalk;

        // ASKING TWICE IS A RESPAWN: the existing body is teleported rather than
        // a second controller built beside it.
        if (f.HasShadow != 0)
        {
            _native.Commit(player, -1, afterCommand: false);
            if (SetState(f.Stand, f.Crouch, f.Controller, f.Origin, f.Velocity, state))
                _native.Commit(player, state, afterCommand: false);
            return true;
        }

        if (!_enabled())
            return false;

        nint env = _native.Environment();
        if (env == 0)
            return false;

        nint standCollide = _v.BoxCollide(standMins, standMaxs);
        nint crouchCollide = _v.BoxCollide(crouchMins, crouchMaxs);
        if (standCollide == 0 || crouchCollide == 0)
            return false;

        int surface = _v.SurfaceIndex(HullSurfaceProp);

        // A shadow BORN at NaN is the same octree.
        if (!Nan.Accept(f.Origin, PhysicsNanGuard.SiteShadowCreate))
        {
            _warning($"[SourceSharp] refusing to build a player shadow at a non-finite origin ({f.Origin.X} {f.Origin.Y} {f.Origin.Z})\n");
            return false;
        }

        // STOCK'S PARAMETERS: g_PhysDefaultObjectParams with mass, inertia,
        // drag and enableCollisions overridden; the game data the tagged handle.
        var p = new VPhysicsObjectParams
        {
            Mass = mass,
            Inertia = HullInertia,
            Damping = HullDamping,
            RotDamping = HullRotDamping,
            RotInertiaLimit = HullRotInertiaLimit,
            Name = StandName,
            GameData = PhysicsGameData.FromHandle(player),
            Volume = 0f,
            DragCoefficient = 0f,
            EnableCollisions = 0,
        };

        nint stand = _v.CreatePolyObject(env, standCollide, surface, f.Origin, in p);
        p.Name = CrouchName;
        nint crouch = _v.CreatePolyObject(env, crouchCollide, surface, f.Origin, in p);

        if (stand == 0 || crouch == 0)
        {
            _v.DestroyObject(env, stand);
            _v.DestroyObject(env, crouch);
            return false;
        }

        _v.SetCallbackFlags(stand, HullCallbackFlags);
        _v.SetCallbackFlags(crouch, HullCallbackFlags);

        nint controller = _v.CreatePlayerController(env, stand);
        if (controller == 0)
        {
            _v.DestroyObject(env, stand);
            _v.DestroyObject(env, crouch);
            return false;
        }

        // The two numbers the shove is about: how heavy, how fast.
        _v.SetPushLimits(controller, pushMassLimit, pushSpeedLimit);

        // "Give the controller a valid position so it doesn't do anything rash"
        // -- stock's UpdatePhysicsShadowToPosition. A fresh CPlayerController's
        // m_targetPosition is the world origin, and its m_enable and clip
        // limits start unset; the ZERO velocity is what makes Update clear
        // m_enable, so the controller does nothing until a command aims it.
        _v.Update(controller, f.Origin, Vector.Zero, f.FrameTime, (f.Flags & FlOnGround) != 0, 0);

        if (!_native.Adopt(player, stand, crouch, controller))
        {
            _v.DestroyPlayerController(env, controller);
            _v.DestroyObject(env, stand);
            _v.DestroyObject(env, crouch);
            return false;
        }

        if (SetState(stand, crouch, controller, f.Origin, f.Velocity, state))
            _native.Commit(player, state, afterCommand: false);

        Built++;
        return true;
    }

    /// <summary>
    /// Stock's SetVCollisionState: one hull in the solver, the other out. A
    /// non-finite origin or velocity is refused and nothing changes. True when
    /// it was applied.
    /// </summary>
    private bool SetState(nint stand, nint crouch, nint controller, Vector origin, Vector velocity, int state)
    {
        if (controller == 0 || stand == 0 || crouch == 0)
            return false;

        if (!Nan.Accept(origin, PhysicsNanGuard.SiteShadowState) || !Nan.Accept(velocity, PhysicsNanGuard.SiteShadowState))
        {
            _warning("[SourceSharp] refusing a non-finite player shadow state: " +
                     $"origin ({origin.X} {origin.Y} {origin.Z}) velocity ({velocity.X} {velocity.Y} {velocity.Z})\n");
            return false;
        }

        switch (state)
        {
            case PlayerShadow.StateWalk:
                _v.SetPosition(stand, origin, teleport: true);
                _v.SetVelocity(stand, velocity, null);
                _v.EnableCollisions(crouch, false);
                _v.SetObject(controller, stand);
                _v.EnableCollisions(stand, true);
                break;

            case PlayerShadow.StateCrouch:
                _v.SetPosition(crouch, origin, teleport: true);
                _v.SetVelocity(crouch, velocity, null);
                _v.EnableCollisions(stand, false);
                _v.SetObject(controller, crouch);
                _v.EnableCollisions(crouch, true);
                break;

            case PlayerShadow.StateNoClip:
                _v.EnableCollisions(crouch, false);
                _v.EnableCollisions(stand, false);
                break;
        }

        return true;
    }

    /// <summary>
    /// The player's active hull when it is awake after the step, else 0 --
    /// stock's VPhysicsUpdate and VPhysicsShadowUpdate reach a player only
    /// then (native's AwakeShadow; plan_physics_bones_native_zero 2.3 moved
    /// PhysFrame's two player walks to managed). The facts it read come back
    /// for the stress the frame computes next.
    /// </summary>
    public nint AwakeShadow(EntityHandle player, out PlayerShadowFacts facts)
    {
        if (!_native.Facts(player, out facts) || facts.HasShadow == 0 || facts.Controller == 0)
            return 0;

        nint active = Active(in facts);
        return active != 0 && !_v.IsAsleep(active) ? active : 0;
    }

    /// <summary>The probe: native's facts and vphysics' own reads, as the plan and the recovery read them.</summary>
    public PlayerShadowState? Probe(EntityHandle player) => Probe(player, out _, afterCommand: false);

    /// <summary>
    /// <see cref="Probe(EntityHandle)"/>, and the facts it was made from (the
    /// apply needs the body). <paramref name="afterCommand"/>: the command loop's
    /// probe, which decides the command's <c>m_touchedPhysObject</c> from its
    /// touch list first; the recovery's reads what that left.
    /// </summary>
    public unsafe PlayerShadowState? Probe(EntityHandle player, out PlayerShadowFacts facts, bool afterCommand)
    {
        if (!_native.Facts(player, out facts) || facts.HasShadow == 0 || facts.Controller == 0)
            return null;

        if (afterCommand)
            Memory.SetTouchedPhysics(player, TouchedMoveablePhysics(in facts));

        var s = default(PlayerShadowState);
        s.StructSize = (uint)sizeof(PlayerShadowState);
        s.HasShadow = 1;
        s.Origin = facts.Origin;
        s.Velocity = facts.Velocity;
        s.OldOrigin = Memory.OldOrigin(player);
        s.OutWishVel = facts.OutWishVel;
        s.OutStepHeight = facts.OutStepHeight;
        s.MaxSpeed = facts.MaxSpeed;
        s.Flags = facts.Flags;
        s.MoveType = facts.MoveType;
        s.State = facts.State;

        nint active = Active(in facts);
        if (active != 0)
        {
            s.HasShadowPosition = 1;
            s.ShadowOrigin = _v.Position(active);
        }

        // VPhysicsShadowUpdate's facts (B7), read and not acted on.
        (bool updated, _) = _v.ShadowPosition(facts.Controller);
        s.PhysicsUpdated = updated ? 1 : 0;
        s.ShadowVelocity = _v.ShadowVelocity(facts.Controller);
        s.WasFrozen = _v.WasFrozen(facts.Controller) ? 1 : 0;
        // The penetration ledger is managed's (2.1), which answers it there.
        s.PenetratingMover = 0;
        s.ShadowMass = active != 0 ? _v.Mass(active) : 0f;

        if (_v.IsInContact(facts.Controller))
            s.ContactBits |= PlayerShadow.ContactController;
        bool touched = Memory.TouchedPhysics(player);
        if (touched)
            s.ContactBits |= PlayerShadow.ContactTouched;

        nint ground = GroundObject(in facts);
        s.GroundPhysicsMass = ground != 0 ? _v.Mass(ground) : 0f;

        // RAW: the clamp stock applies to a frame time is the plan's.
        s.FrameTime = facts.FrameTime;
        s.TickInterval = facts.TickInterval;

        Updates++;
        if (s.ContactBits != 0)
            Contacts++;
        if (touched)
            Touches++;

        return s;
    }

    /// <summary>MOVETYPE_VPHYSICS and SOLID_VPHYSICS, and FSOLID_TRIGGER.</summary>
    private const int SolidVPhysics = 6, SolidFlagTrigger = 0x0008;

    /// <summary>Touch lists longer than the facts list, whose tail was not seen.</summary>
    public int TouchOverflows { get; private set; }

    /// <summary>
    /// Stock's <c>CBasePlayer::Touch</c> (player.cpp:4761-4775) over the
    /// command's touch list: true when the move ran into anything that is not
    /// the ground it stood on at the time, is MOVETYPE_VPHYSICS and
    /// SOLID_VPHYSICS, is not a trigger, and holds a moveable physics object.
    /// Native recorded the list (RecordMoveTouch) and decided nothing; it used
    /// to apply two of these four tests itself (NoteTouchedPhysics).
    /// </summary>
    public bool TouchedMoveablePhysics(in PlayerShadowFacts facts)
    {
        if (_touches is null || facts.Touched <= 0)
            return false;

        int listed = Math.Min(facts.Touched, PlayerShadowFacts.TouchMax);
        for (int i = 0; i < listed; i++)
        {
            // if ( pOther == GetGroundEntity() ) return;
            if ((facts.TouchedGround & (1u << i)) != 0)
                continue;

            if (!_touches.TryEntity(new EntityHandle(facts.TouchedEntities[i]), out int moveType, out int solid,
                                    out int solidFlags, out nint physicsObject))
                continue;

            if (moveType != MoveTypeVPhysics || solid != SolidVPhysics || (solidFlags & SolidFlagTrigger) != 0)
                continue;

            if (physicsObject != 0 && _v.IsMoveable(physicsObject))
                return true;
        }

        // More entities than the list holds and none of the listed was it:
        // counted, so a gate can see the tail it could not read.
        if (facts.Touched > listed)
            TouchOverflows++;

        return false;
    }

    /// <summary>
    /// One command's plan, carried out: stock's SetVCollisionState, the
    /// step-up, the snap and the controller's Update (UpdateVPhysicsPosition,
    /// player.cpp:4854-4895), with the NaN refusals before anything vphysics
    /// is handed.
    /// </summary>
    public void Apply(EntityHandle player, in PlayerShadowFacts facts, in PlayerShadowPlan plan)
    {
        long start = CostMeter.Start();
        ApplyPlan(player, in facts, in plan);
        ApplyCost.Stop(start);
    }

    /// <summary>
    /// <see cref="Apply"/>'s wall time, one call a command, every vphysics
    /// and native crossing it makes included (cs_sv_physics_cost).
    /// </summary>
    public CostMeter ApplyCost { get; } = new();

    private void ApplyPlan(EntityHandle player, in PlayerShadowFacts facts, in PlayerShadowPlan plan)
    {
        if (plan.Valid == 0 || facts.Controller == 0)
            return;

        // The switch was turned off live: the body leaves at this command.
        if (plan.Destroy != 0)
        {
            _native.Release(player);
            return;
        }

        int state = facts.State;
        if (plan.StateChanged != 0
            && SetState(facts.Stand, facts.Crouch, facts.Controller, plan.StateOrigin, plan.StateVelocity, plan.State))
            state = plan.State;

        var current = facts with { State = state };
        nint active = Active(in current);

        // The step-up: the shadow is told, or it stays at the bottom and drags
        // the controller through the step.
        if (plan.StepUp != PlayerShadow.StepNone && active != 0)
        {
            if (plan.StepUp == PlayerShadow.StepTeleport)
                _v.SetPosition(active, facts.Origin, teleport: true);
            else
                _v.StepUp(facts.Controller, plan.StepHeight);

            _v.Jump(facts.Controller);
        }

        // The move data's step height consumed, and the hull in play; and
        // PostThinkVPhysics' last line, m_oldOrigin = GetAbsOrigin(), into the
        // one m_oldOrigin the shadow update reads too.
        _native.Commit(player, state, afterCommand: true);
        Memory.SetOldOrigin(player, facts.Origin);

        if (plan.Snap != 0 && active != 0
            && Nan.Accept(plan.SnapOrigin, PhysicsNanGuard.SiteShadowSnap)
            && Nan.Accept(plan.SnapVelocity, PhysicsNanGuard.SiteShadowSnap))
        {
            _v.SetPosition(active, plan.SnapOrigin, teleport: true);
            _v.SetVelocity(active, plan.SnapVelocity, null);
            Snaps++;
        }

        // THE LAST GATE, and the one every command passes: the controller
        // steers the shadow at Position, so a NaN here is a NaN shadow.
        if (!Nan.Accept(plan.Position, PhysicsNanGuard.SiteControllerUpdate)
            || !Nan.Accept(plan.WishVelocity, PhysicsNanGuard.SiteControllerUpdate)
            || !PhysicsNanGuard.IsFiniteCoord(plan.Arrival))
        {
            _warning("[SourceSharp] refusing a non-finite player controller update: " +
                     $"position ({plan.Position.X} {plan.Position.Y} {plan.Position.Z}) " +
                     $"wishvel ({plan.WishVelocity.X} {plan.WishVelocity.Y} {plan.WishVelocity.Z}) arrival {plan.Arrival}\n");
            return;
        }

        nint ground = plan.RideableGround != 0 ? GroundObject(in facts) : 0;
        _v.Update(facts.Controller, plan.Position, plan.WishVelocity, plan.Arrival, plan.OnGround != 0, ground);
    }

    /// <summary>
    /// Stock's TeleportEntity for a player's physics object: the active hull
    /// goes where the player was put, teleported, and the controller is
    /// re-aimed there with a zero velocity (UpdatePhysicsShadowToCurrentPosition),
    /// or vphysics snaps a hull more than 24 units from its target back to it.
    /// </summary>
    public void Teleport(EntityHandle player, Vector origin, bool zeroVelocity)
    {
        if (!_native.Facts(player, out PlayerShadowFacts f) || f.HasShadow == 0 || f.Controller == 0)
            return;

        nint active = Active(in f);
        if (active == 0)
            return;

        _v.SetPosition(active, origin, teleport: true);
        _v.Update(f.Controller, origin, Vector.Zero, f.FrameTime, (f.Flags & FlOnGround) != 0, 0);
        if (zeroVelocity)
            _v.SetVelocity(active, Vector.Zero, Vector.Zero);
    }
}
