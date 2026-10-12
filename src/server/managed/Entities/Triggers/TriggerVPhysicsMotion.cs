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
/// A volume that everything physical inside it moves THROUGH rather than in:
/// heavier or lighter gravity, thicker air, a speed ceiling, a steady current.
/// Maps use it for the bottom of a shaft, for a wind tunnel, and for the
/// slow-falling debris in a set piece.
///
/// The nine numbers are read several times per physics tick for every object
/// inside, which is exactly the shape the plan reserves for native: there is no
/// point in that loop where a rule could have a view, because given the numbers
/// and one object's velocity the acceleration follows. So the loop is a
/// <see cref="MotionControllerKind.Medium"/> and this class owns the two things
/// that ARE decisions — what the numbers are, and which objects the medium is
/// currently allowed to act on.
///
/// One controller for the whole volume, not one per entrant, which is the
/// difference from <see cref="TriggerWind"/>: a wind blows on each object
/// differently and needs a force per object, while a medium is one medium and
/// everything in it feels the same thing.
/// </summary>
[ScriptedEntity(EntityNames.TriggerVphysicsMotion, ScriptedEntityKind.Brush)]
public sealed class TriggerVPhysicsMotion : TriggerVolume
{
    public TriggerVPhysicsMotion(IGameClock clock, IEngineNetworking net, IEngineEntityPhysics physics, IEnginePlayerMovement playerMovement,
                                 IEngineMotionControllers motion, IEntityDirectory directory)
        : base(clock)
    {
        Directory = directory;
        Net = net;
        Motion = motion;
        Physics = physics;
        PlayerMovement = playerMovement;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;

    /// <summary>how a player moves, and what may stop them.</summary>
    private readonly IEnginePlayerMovement PlayerMovement;

    /// <summary>the solver's motion controllers, which the medium is made in.</summary>
    private readonly IEngineMotionControllers Motion;

    /// <summary>the player's managed object, which holds m_afPhysicsFlags.</summary>
    private readonly IEntityDirectory Directory;
    // The key and the input share a name for each of these, because stock
    // declares them with DEFINE_INPUT — one line that is both. A mapper can set
    // the field in Hammer or wire the input at runtime and mean the same thing.

    /// <summary>What a map may set on this entity. See <see cref="TriggerVPhysicsMotionVars"/>.</summary>
    [ScriptVars] public TriggerVPhysicsMotionVars TriggerVPhysicsMotionVars = new();

    /// <summary>
    /// The medium itself. Created empty, because a volume's controller belongs
    /// to the volume and not to any one thing in it.
    /// </summary>
    private MotionController? _medium;

    /// <summary>
    /// The medium's raw native id, carried so a reload can re-adopt the wrapper
    /// above. The native controller and everything attached to it outlive the
    /// assembly swap (PhysicsMotionControllers carries its rows and clears them only at
    /// LevelShutdown), and stock saves the same pointer: m_pController is
    /// DEFINE_PHYSPTR in CTriggerVPhysicsMotion's DATADESC (triggers.cpp). With
    /// the wrapper null after a reload, a leaving object stayed in the medium
    /// and every retune input did nothing.
    /// </summary>
    [SaveField] private uint _mediumId;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Every entrant has to be attached, and a refire delay would quietly
        // skip the second of two crates falling in together — the same reason
        // the wind clears it.
        Vars.Wait = 0f;

        // The three calls that make this a volume the SOLVER knows about, which
        // is the only kind of volume a physics object can be reported entering.
        //
        // A brush trigger is normally described to the ENGINE — passable, and in
        // the engine's trigger partition list — and for a player that is enough,
        // because a player's touches come from the engine walking those lists.
        // It is worth nothing at all for a crate. Everything on this mod's
        // hierarchy is a CStandaloneEntity with no edict, so the engine's lists
        // never pair two of them: measured here, a crate falling the length of
        // this volume produced ZERO reports, with the trigger flag set and with
        // the players-only relay taken out of the way as well.
        //
        // Stock reaches the same place from the opposite direction and says so
        // out loud — CBaseVPhysicsTrigger::Spawn withholds FSOLID_TRIGGER on
        // purpose, "or you'll get game collisions AND vphysics collisions" — so
        // matching it means DROPPING the engine flag rather than keeping it as
        // well. Measured with it dropped: the player is still reported entering
        // and leaving, once each, because a player's shadow object is in the
        // solver too. One path, and it is the path stock uses.
        //
        //   Static, because a volume does not fall. The shell builds a simulated
        //   object for a Prop and none at all for a Brush, and this asks for the
        //   third thing — an object the solver tests against and never steps —
        //   which is what stock's VPhysicsInitStatic makes here.
        //
        //   Passable again, because making the object leaves the entity solid,
        //   which is right for scenery and wrong for a volume.
        //
        //   And a trigger, which is the point of the other two: an object in the
        //   solver's trigger list is tested for overlap and REPORTED rather than
        //   solved against.
        MakePhysicsStatic();
        SetSolidity(BrushSolidity.None);
        Physics.BecomePhysicsTrigger(Handle, true);

        _medium = MotionController.Unattached(Motion, MotionControllerKind.Medium);
        _mediumId = _medium?.Id.Value ?? 0u;
        Retune();
    }

    /// <summary>Re-adopts the medium the reload left standing; see <see cref="_mediumId"/>.</summary>
    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);

        if (_medium is null && _mediumId != 0u)
            _medium = MotionController.Adopt(Motion, new PhysicsId(_mediumId), MotionControllerKind.Medium);
    }

    public override void OnRemoved()
    {
        _medium?.Destroy();
        _medium = null;
        _mediumId = 0u;
    }

    /// <summary>
    /// Physics objects and players, and nothing else.
    ///
    /// The kind test is the medium's own rather than the base's: a force acts on
    /// a physics object, so something the solver does not move has nothing for
    /// this to reach. Stock asks the same question one line into
    /// CBaseVPhysicsTrigger::PassesTriggerFilters, before it looks at a single
    /// spawnflag.
    ///
    /// The spawnflags and the filter still decide among what is left, which is
    /// why this ends by asking the base rather than only the filter.
    /// </summary>
    protected override bool Passes(EntityHandle toucher)
    {
        var state = Net.EntityGetState(toucher, out MoveType moveType);

        if (moveType != MoveType.VPhysics && (state & EntityState.Player) == 0)
            return false;

        return base.Passes(toucher);
    }

    /// <summary>
    /// Every toucher, not only players.
    ///
    /// The registry's default rule — a touch is a player's unless the class says
    /// otherwise — is right for a pickup and exactly wrong here: a medium exists
    /// to act on the things the solver moves, and a crate is the case it was
    /// written for. Measured with everything else in this class fixed and this
    /// line missing: the solver reported the crate, the registry dropped it
    /// before this class could see it, and the crate fell at 245 units a second
    /// through a well whose ceiling is 70.
    /// </summary>
    public override bool TouchedByAnything => true;

    protected override void Fire(EntityHandle toucher) => Hold(toucher);

    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);
        Release(toucher);
    }

    /// <summary>
    /// Switching the volume off has to let go of what is already in it.
    ///
    /// Being disabled stops the base noticing arrivals, and that alone would
    /// leave every crate that was already inside falling slowly for the rest of
    /// the map. Stock reaches the same place from the other side: its Simulate
    /// answers SIM_NOTHING while disabled, so the objects stay attached and feel
    /// nothing.
    /// </summary>
    protected override void OnDisabledChanged()
    {
        foreach (var toucher in Inside)
        {
            if (Disabled)
                Release(toucher);
            else
                Hold(toucher);
        }
    }

    private void Hold(EntityHandle toucher)
    {
        // A player is moved by the movement code and not by the solver, so the
        // medium reaches their shadow object and almost nothing else. What makes
        // them wade is the pair of flags, set before stock looks for an object
        // or a controller (triggers.cpp:4686-4691): a refused medium still
        // flags the player.
        if ((Net.EntityGetState(toucher, out _) & EntityState.Player) != 0)
            SetPlayerFlags(toucher, true);

        _medium?.AttachObject(toucher);
    }

    private void Release(EntityHandle toucher)
    {
        if ((Net.EntityGetState(toucher, out _) & EntityState.Player) != 0)
            SetPlayerFlags(toucher, false);

        _medium?.DetachObject(toucher);
    }

    /// <summary>PFLAG_VPHYSICS_MOTIONCONTROLLER and m_bSlowMovement, together.</summary>
    private void SetPlayerFlags(EntityHandle player, bool inside)
    {
        PlayerMovement.SetSlowMovement(player, inside);

        if (Directory.Get(player) is SsPlayer ss)
        {
            ss.PhysicsFlags = inside
                ? ss.PhysicsFlags | SsPlayer.PhysicsFlagVPhysicsMotionController
                : ss.PhysicsFlags & ~SsPlayer.PhysicsFlagVPhysicsMotionController;
        }
    }

    [ScriptedEvent("SetGravityScale")]
    private void OnGravityScaleSet(float value)
    {
        TriggerVPhysicsMotionVars.GravityScale = value;
        Retune();
    }

    [ScriptedEvent("SetAdditionalAirDensity")]
    private void OnAirDensitySet(float value)
    {
        TriggerVPhysicsMotionVars.AirDensity = value;
        Retune();
    }

    [ScriptedEvent("SetVelocityLimit")]
    private void OnVelocityLimitSet(float value)
    {
        TriggerVPhysicsMotionVars.LinearLimit = value;
        Retune();
    }

    [ScriptedEvent("SetVelocityLimitDelta")]
    private void OnVelocityLimitDeltaSet(float value)
    {
        TriggerVPhysicsMotionVars.LinearLimitDelta = value;
        Retune();
    }

    [ScriptedEvent("SetVelocityScale")]
    private void OnVelocityScaleSet(float value)
    {
        TriggerVPhysicsMotionVars.LinearScale = value;
        Retune();
    }

    [ScriptedEvent("SetAngVelocityLimit")]
    private void OnAngVelocityLimitSet(float value)
    {
        TriggerVPhysicsMotionVars.AngularLimit = value;
        Retune();
    }

    [ScriptedEvent("SetAngVelocityScale")]
    private void OnAngVelocityScaleSet(float value)
    {
        TriggerVPhysicsMotionVars.AngularScale = value;
        Retune();
    }

    [ScriptedEvent("SetLinearForce")]
    private void OnLinearForceSet(float value)
    {
        TriggerVPhysicsMotionVars.LinearForce = value;
        Retune();
    }

    /// <summary>
    /// Three numbers, so the input carries a string rather than a float.
    ///
    /// Hammer comments this one out of the FGD while stock's datadesc still
    /// declares it, which means a map CAN send it even though the editor will
    /// not offer it. Refusing it here would be a regression nobody could see
    /// coming.
    /// </summary>
    [ScriptedEvent("SetLinearForceAngles")]
    private void OnLinearForceAnglesSet(string value)
    {
        TriggerVPhysicsMotionVars.LinearForceAngles = ParseVector(value);
        Retune();
    }

    /// <summary>
    /// Moves the speed ceiling to a new value over a period: "limit time".
    ///
    /// The one input that is not just a number, and the one that cannot be
    /// folded into <see cref="Retune"/>, because a ramp has to start from the
    /// value the medium has REACHED rather than from the one it was aiming at.
    /// That reading happens inside the physics tick, so the interpolation is
    /// native and this call is the decision: where to, and over how long.
    ///
    /// The destination is recorded here as well, so that a later SetGravityScale
    /// retunes with the limit the ramp is heading for rather than dragging it
    /// back to where it started.
    /// </summary>
    [ScriptedEvent("SetVelocityLimitTime")]
    private void OnVelocityLimitTimeSet(string value)
    {
        // "800 3" — limit first, seconds second. A malformed one reads as zero
        // and arrives at once, which is what UTIL_StringToFloatArray does with
        // it in stock.
        var args = ParseVector(value);

        TriggerVPhysicsMotionVars.LinearLimit = args.X;
        _medium?.RampLinearLimit(args.X, args.Y);
    }

    /// <summary>
    /// Hands the whole medium over at once.
    ///
    /// One call for nine numbers because they are one setting — they are read
    /// together every tick and no state exists in which changing one alone means
    /// anything. It costs nothing to send all nine when a map changed one, and a
    /// setter per number would be nine boundary crossings for the same effect.
    /// </summary>
    private void Retune()
        => _medium?.SetMedium(TriggerVPhysicsMotionVars.GravityScale, TriggerVPhysicsMotionVars.AirDensity, TriggerVPhysicsMotionVars.LinearLimit, TriggerVPhysicsMotionVars.LinearLimitDelta,
                              TriggerVPhysicsMotionVars.LinearScale, TriggerVPhysicsMotionVars.AngularLimit, TriggerVPhysicsMotionVars.AngularScale, TriggerVPhysicsMotionVars.LinearForce,
                              TriggerVPhysicsMotionVars.LinearForceAngles);

    /// <summary>The medium as it stands, for the tests, which have no physics to ask.</summary>
    internal (float Gravity, float Air, float Limit, float Delta, float Scale,
              float AngLimit, float AngScale, float Force, Vector Angles) Medium
        => (TriggerVPhysicsMotionVars.GravityScale, TriggerVPhysicsMotionVars.AirDensity, TriggerVPhysicsMotionVars.LinearLimit, TriggerVPhysicsMotionVars.LinearLimitDelta, TriggerVPhysicsMotionVars.LinearScale,
            TriggerVPhysicsMotionVars.AngularLimit, TriggerVPhysicsMotionVars.AngularScale, TriggerVPhysicsMotionVars.LinearForce, TriggerVPhysicsMotionVars.LinearForceAngles);
}

/// <summary>
/// The map keys <see cref="TriggerVPhysicsMotion"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerVPhysicsMotionVars
{
    /// <summary>Multiple of the world's gravity. 1 changes nothing.</summary>
    [VarName("SetGravityScale")]
    public float GravityScale { get; set; } = 1f;

    /// <summary>Drag added on top of whatever the object already has.</summary>
    [VarName("SetAdditionalAirDensity")]
    public float AirDensity { get; set; }

    /// <summary>Speed ceiling, units per second. 0 does not limit.</summary>
    [VarName("SetVelocityLimit")]
    public float LinearLimit { get; set; }

    /// <summary>
    /// Most speed one second of the ceiling may take away, which is what keeps a
    /// ceiling from reading as a wall. 0 takes as much as it needs.
    /// </summary>
    [VarName("SetVelocityLimitDelta")]
    public float LinearLimitDelta { get; set; }

    /// <summary>Speed multiplier. 1 changes nothing.</summary>
    [VarName("SetVelocityScale")]
    public float LinearScale { get; set; } = 1f;

    /// <summary>Spin ceiling, degrees per second. 0 does not limit.</summary>
    [VarName("SetAngVelocityLimit")]
    public float AngularLimit { get; set; }

    /// <summary>Spin multiplier. 1 changes nothing.</summary>
    [VarName("SetAngVelocityScale")]
    public float AngularScale { get; set; } = 1f;

    /// <summary>Strength of a steady current through the volume. 0 is still air.</summary>
    [VarName("SetLinearForce")]
    public float LinearForce { get; set; }

    /// <summary>Which way that current runs. Pitch yaw roll, as the map writes it.</summary>
    [VarName("SetLinearForceAngles")]
    public Vector LinearForceAngles { get; set; }
}
