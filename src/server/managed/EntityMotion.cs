//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The movement integrators, in managed code — <c>plan_entity.md</c> step 2f.
///
/// <para>
/// <b>WHAT MOVED.</b> <c>CStandaloneEntity::UpdateFlight</c> integrated a
/// world-space velocity, traced the step, and decided from the answer whether
/// the entity had arrived, bounced or stopped. Every one of those is a decision
/// about what the game does, which is the definition §6 gives of policy, so it
/// is here. What stays native is the trace itself and the origin write — the
/// two things that are not decisions.
/// </para>
///
/// <para>
/// <b>IT RUNS FROM THE FRAME, NOT FROM AN ENTITY.</b> Step 1g's rule and the
/// one this must not break: the pass is a single loop inside the one
/// <c>GameFrame</c> crossing the frame already pays for, so the number of
/// managed↔native TRANSITIONS it costs does not scale with the number of
/// entities. It is driven from <see cref="StandaloneHierarchy"/> at exactly the
/// point <c>StandaloneEntities_Update</c> used to call <c>UpdateFlight</c>:
/// after the touch links and the movers, before the thinks.
/// </para>
///
/// <para>
/// <b>What it costs, stated as the brief requires.</b> Per MOVING entity per
/// frame it makes native calls that the native integrator made for free,
/// because it was already inside native: one <c>GetParent</c>, one
/// <c>TraceLine</c>, one <c>SetOrigin</c>, and a <c>SetVelocity</c> only on the
/// frames the velocity actually changed. These are managed→native and so are
/// NOT what <see cref="Crossings"/> counts (it counts inbound thunks), which is
/// exactly why the number is written down here. Against that it REMOVES one
/// inbound crossing per impact: <c>OnManagedImpact</c> is gone, and the touch
/// is delivered by a managed virtual call. At rest the set is EMPTY and the
/// pass is one <c>List.Clear</c>, because nothing is flying.
/// </para>
///
/// <para>
/// <b>The owner skip is a stated divergence and it has an ABI request behind
/// it.</b> Native filtered the step trace with <c>CTraceFilterSkipTwoEntities</c>
/// — itself AND its owner. The ABI's <c>TraceLine</c> passes ONE entity
/// through, so this skips itself in the filter and discards a hit that names
/// the owner. The difference is what was BEHIND the owner within the same step:
/// native found it this frame, this finds it next frame, one tick and at most
/// one step's length later, and only while the projectile is still inside its
/// shooter. <c>.claude/entity-port/abi-requests/2f.md</c> asks for the second
/// skip so the difference goes away. A projectile is never let THROUGH a wall
/// by it: the wall is found on the following step.
/// </para>
/// </summary>
public sealed class EntityMotion
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public EntityMotion(ILog log, IEngineEntities entities, IEngineWorldQuery world,
                        IEngineEffects effects,
                        IEngineConsole console, EntityDirectory directory,
                        PhysicsPushedEntities pushed)
    {
        _log = log;
        _pushed = pushed;
        _entities = entities;
        _world = world;
        _effects = effects;
        _console = console;
        _directory = directory;
    }

    /// <summary>
    /// The parent links, for the push half — <c>plan_entity.md</c> step 2g.
    ///
    /// <para>
    /// <b>Handed over rather than injected, and it has to be.</b>
    /// <see cref="StandaloneHierarchy"/> is constructed WITH this object (it is
    /// what drives the pass), so asking the container for it here would be a
    /// cycle. It is set once, by that constructor, exactly as the directory's
    /// removal hook is.
    /// </para>
    ///
    /// <para>
    /// Null until then, and a null one means "nothing is parented as far as this
    /// pass knows" — which is the same answer the registry gives before a level
    /// has any links in it, so a pass that ran early would be right rather than
    /// broken.
    /// </para>
    /// </summary>
    public Hierarchy? Parenting { get; set; }

    /// <summary>For the one convar the blocked diagnostic reads. See <see cref="BlockedReportingOn"/>.</summary>
    private readonly IEngineConsole _console;

    private readonly IEngineEntities _entities;
    private readonly IEngineWorldQuery _world;

    /// <summary>
    /// For <see cref="IEngineEffects.CurrentGravity"/> and nothing else.
    ///
    /// <para>
    /// An integrator depending on the EFFECTS area reads wrong, and it is where
    /// the ABI actually puts <c>GetCurrentGravity()</c> (see
    /// <c>EngineAreas.World.cs</c>). Taken as it is rather than moved, because
    /// the areas are a shared file this step does not own; naming it here is
    /// the honest version of the compromise.
    /// </para>
    /// </summary>
    private readonly IEngineEffects _effects;

    private readonly EntityDirectory _directory;

    /// <summary>What a mover does to whatever is in its way: CPhysicsPushedEntities.</summary>
    private readonly PhysicsPushedEntities _pushed;

    /// <summary>One entity's motion, as this side holds it.</summary>
    private sealed class Mover
    {
        public MotionKind Kind;
        public Vector Velocity;
        public float GravityScale;
        public int BouncesLeft;
        public EntityHandle Owner;

        /// <summary>Degrees per second its angles turn by in flight: PhysicsToss's avelocity.</summary>
        public Vector Spin;

        /// <summary>The velocity it was flying at going into the last step: what a touch saw it arrive with.</summary>
        public Vector Incoming;
    }

    private readonly Dictionary<EntityHandle, Mover> _movers = new();

    /// <summary>
    /// Iteration order, and a snapshot of it, so that a class whose impact
    /// removes it — which is what nearly every projectile's does — cannot
    /// mutate the collection the pass is walking.
    /// </summary>
    private readonly List<EntityHandle> _order = new();
    private readonly List<EntityHandle> _pass = new();

    /// <summary>How many entities this pass integrated. Instrumentation; see the type's remarks.</summary>
    public int MovedLastFrame { get; private set; }

    /// <summary>How many entities are being integrated at all.</summary>
    public int Count => _movers.Count;

    //-------------------------------------------------------------------------
    // Being told what an entity is doing.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Aimed and let go: the entity flies from now on.
    ///
    /// <para>
    /// <paramref name="gravityScale"/> zero is <see cref="MotionKind.Fly"/> and
    /// anything else is <see cref="MotionKind.Toss"/>, which is stock's own
    /// <c>MOVETYPE_FLY</c> / <c>MOVETYPE_FLYGRAVITY</c> split expressed as the
    /// one number that separates them.
    /// </para>
    ///
    /// <para>
    /// <b>IT ASKS THE ENGINE NOTHING.</b> A launch is a statement, not a
    /// question: everything it needs is in its arguments, and the one thing
    /// that would have to be looked up — whether this entity is parented — is
    /// checked by <see cref="RunFrame"/> on every frame it flies, which is
    /// where <c>UpdateFlight</c> checked it too. Asking here as well would put
    /// a native round trip on the path a weapon takes to fire a shot, for an
    /// answer the very next frame re-reads.
    /// </para>
    /// </summary>
    public void Launch(EntityHandle entity, Vector velocity, float gravityScale,
                       int bounces, EntityHandle owner, Vector spin = default)
    {
        // COMPARED AGAINST None RATHER THAN ASKED IsValid, here and at every
        // other handle test in this file. EntityHandle.IsValid is a NATIVE CALL
        // (Entity.cs: it forwards to Engine.EntityIsValid), so using it inside
        // a per-frame pass would put an engine round trip behind what reads as
        // a field test -- two of them per flying entity per frame, for a
        // question this side can answer from the packed value.
        if (entity == EntityHandle.None)
            return;

        var mover = Track(entity);

        mover.Velocity = velocity;
        mover.GravityScale = gravityScale;
        mover.BouncesLeft = bounces;
        mover.Owner = owner;
        mover.Spin = spin;
        mover.Incoming = velocity;
        mover.Kind = gravityScale != 0f ? MotionKind.Toss : MotionKind.Fly;
    }

    /// <summary>
    /// Velocity applied and nothing asked about what is there — stock's
    /// <c>MOVETYPE_NOCLIP</c>.
    /// </summary>
    public void NoClip(EntityHandle entity, Vector velocity)
    {
        if (entity == EntityHandle.None)
            return;

        var mover = Track(entity);

        mover.Kind = MotionKind.NoClip;
        mover.Velocity = velocity;
        mover.GravityScale = 0f;
        mover.BouncesLeft = 0;
        mover.Spin = Vector.Zero;
    }

    /// <summary>
    /// Its position belongs to its parent from now on: this pass stops writing
    /// it. Stock's <c>MOVETYPE_NONE</c> with a move parent.
    /// </summary>
    public void Follow(EntityHandle entity)
    {
        if (entity != EntityHandle.None)
            Track(entity).Kind = MotionKind.Follow;
    }

    /// <summary>
    /// It stops where it is. The record is KEPT rather than dropped, so that a
    /// class asking what an entity is doing gets "nothing" instead of a lookup
    /// that fails the same way a handle nobody ever launched does.
    /// </summary>
    public void Stop(EntityHandle entity)
    {
        if (_movers.TryGetValue(entity, out var mover))
        {
            mover.Kind = MotionKind.None;
            mover.Velocity = Vector.Zero;
        }
    }

    /// <summary>It is gone. Drops the row.</summary>
    public bool Forget(EntityHandle entity)
    {
        // BOTH HALVES, and the answer is "either of them was there". An entity
        // that was pushing and never flew is still an entity this class was
        // holding a row for, and a Forget that reported false for it would leave
        // the row behind on every mover in the map.
        bool pushed = ForgetPush(entity);

        if (!_movers.Remove(entity))
            return pushed;

        _order.Remove(entity);
        return true;
    }

    /// <summary>A level change: nothing is moving any more.</summary>
    public void Clear()
    {
        _movers.Clear();
        _order.Clear();
        _pushers.Clear();
        _pushOrder.Clear();
        MovedLastFrame = 0;
        PushedLastFrame = 0;
        LastBlocker = EntityHandle.None;
    }

    /// <summary>What an entity is being integrated as. <see cref="MotionKind.None"/> if it is not.</summary>
    public MotionKind KindOf(EntityHandle entity)
        => _movers.TryGetValue(entity, out var mover) ? mover.Kind : MotionKind.None;

    /// <summary>The velocity this side is integrating, which is the store of record while it flies.</summary>
    public Vector VelocityOf(EntityHandle entity)
        => _movers.TryGetValue(entity, out var mover) ? mover.Velocity : Vector.Zero;

    /// <summary>
    /// The velocity it arrived with at its last step: stock's GetAbsVelocity
    /// inside a touch, which runs before the collision changes it. Zero for
    /// something not flying.
    /// </summary>
    public Vector IncomingVelocityOf(EntityHandle entity)
        => _movers.TryGetValue(entity, out var mover) ? mover.Incoming : Vector.Zero;

    /// <summary>How many bounces an entity has left.</summary>
    public int BouncesLeftOf(EntityHandle entity)
        => _movers.TryGetValue(entity, out var mover) ? mover.BouncesLeft : 0;

    //-------------------------------------------------------------------------
    // The frame.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One frame of motion for everything that is moving.
    ///
    /// <para>
    /// A frametime of zero or less does nothing at all, which is
    /// <c>UpdateFlight</c>'s own first guard: a paused server would otherwise
    /// integrate gravity into a velocity over no time and drift.
    /// </para>
    ///
    /// <para>Answers how many entities it integrated.</para>
    /// </summary>
    public int RunFrame(float frameTime)
    {
        MovedLastFrame = 0;
        PushedLastFrame = 0;

        if (frameTime <= 0f)
            return 0;

        // THE MOVERS BEFORE THE FLYERS, which is the order the native frame had:
        // StandaloneEntities_Update stepped every mover and UpdateFlight ran
        // after it. It is not arbitrary — a projectile's step trace should meet
        // the door where the door is THIS frame, not where it was last frame.
        RunPushFrame(frameTime);

        if (_movers.Count == 0)
            return PushedLastFrame;

        // The world's gravity ONCE per frame rather than once per entity: it is
        // a convar, every falling thing reads the same number, and reading it
        // per entity would be a native call per entity for a value that cannot
        // change between them.
        float gravity = _effects.CurrentGravity();

        _pass.Clear();
        _pass.AddRange(_order);

        for (int i = 0; i < _pass.Count; i++)
        {
            EntityHandle handle = _pass[i];

            if (!_movers.TryGetValue(handle, out var mover))
                continue;

            // AN ENTITY THAT HAS GONE SINCE THE SNAPSHOT WAS TAKEN, which is
            // the common case rather than a corner: a projectile's impact
            // removes it, and a rocket that killed the thing standing behind a
            // second rocket removes that one too.
            if (_directory.Resolve(handle) is null)
            {
                Forget(handle);
                continue;
            }

            switch (mover.Kind)
            {
                case MotionKind.Fly:
                case MotionKind.Toss:
                    Integrate(handle, mover, frameTime, gravity);
                    MovedLastFrame++;
                    break;

                case MotionKind.NoClip:
                    _entities.SetOrigin(handle, MoveMath.NoClipStep(
                        _entities.GetOrigin(handle), mover.Velocity, frameTime));
                    MovedLastFrame++;
                    break;

                // Follow and None both mean "this pass does not write its
                // origin". They are two states rather than one because a
                // follower's position IS being written, by the hierarchy, and a
                // stopped entity's is not — and telling them apart is what
                // stops a class asking "why did my parented thing not move"
                // from getting the same answer as one that was never launched.
                case MotionKind.Follow:
                case MotionKind.None:
                    break;
            }
        }

        // BOTH HALVES, and the number is the whole pass rather than the flight
        // half of it. Step 2f's callers all had nothing pushing, so every one of
        // their answers is unchanged; a caller that has movers as well is being
        // told how many entities this pass integrated, which is what the name
        // says and what a frame counter is for.
        return MovedLastFrame + PushedLastFrame;
    }

    /// <summary>
    /// One frame of flight: integrate, trace the step, and act on the answer.
    ///
    /// <para>
    /// The trace is what makes a projectile hit anything at all. A position
    /// test at the destination would miss every wall the step went through on
    /// the way — a bolt travels a thousand units a second and is two units
    /// across — and <c>MASK_SHOT</c> is what a shot is stopped by.
    /// </para>
    /// </summary>
    private void Integrate(EntityHandle handle, Mover mover, float frameTime, float gravity)
    {
        // THE PARENT IS CHECKED EVERY FRAME, which is where UpdateFlight
        // checked it and is the only place it CAN be checked: something can be
        // parented while it is in the air, and the frame that happens is the
        // frame two writers start on one origin.
        //
        // A parented projectile is not a case with a right answer. Flight
        // integrates a world-space velocity and traces the step; the parent's
        // composition writes the same origin from its own frame; and the two
        // would take turns, so the thing neither flies nor follows. Stopped and
        // said out loud, because a projectile that quietly went nowhere is the
        // least diagnosable of the three.
        if (_entities.GetParent(handle) != EntityHandle.None)
        {
            _log.Warning($"[SourceSharp] '{Name(handle)}' is parented and cannot fly; " +
                        "it follows its parent from now on\n");
            mover.Kind = MotionKind.Follow;
            return;
        }

        var input = new FlightInput(frameTime, gravity, mover.GravityScale,
                                    _entities.GetOrigin(handle), mover.Velocity,
                                    mover.BouncesLeft);

        FlightStep step = MoveMath.AdvanceFlight(input);
        mover.Incoming = mover.Velocity;

        // SKIPPING ITSELF, and that is not an optimisation — it is what makes a
        // projectile fly at all. A projectile is SOLID_BBOX and goes into the
        // spatial partition like anything else, so a trace with nothing passed
        // through starts inside its own two-unit box and hits IT, at fraction
        // zero, on the very first frame. A rocket exploded the instant it was
        // fired and a bolt was removed before it was ever drawn.
        TraceResult trace = _world.TraceLine(input.Origin, step.Destination,
                                             TraceMask.Shot, handle);

        // AND ITS OWNER, which the filter cannot be told about — see the type's
        // remarks and abi-requests/2f.md. A rocket fired at the floor spawns
        // inside the shooter's own hull, and the managed "was that me?" check
        // runs after the flight has already been stopped, so it cannot save it.
        if (mover.Owner != EntityHandle.None && trace.Entity == mover.Owner)
            trace = new TraceResult(false, 1f, step.Destination, Vector.Zero, EntityHandle.None);

        FlightResult result = MoveMath.ResolveFlight(input, step, trace.Fraction,
                                                     trace.End, trace.Normal);

        _entities.SetOrigin(handle, result.Origin);

        // PhysicsToss turns the angles by the angular velocity each frame: a
        // tumbling grenade. Nothing launched without a spin asks.
        if (mover.Spin.X != 0f || mover.Spin.Y != 0f || mover.Spin.Z != 0f)
            _entities.SetAngles(handle, _entities.GetAngles(handle) + mover.Spin * frameTime);

        // The velocity is written back to the shell only when it MOVED, and
        // that is not thrift for its own sake: EntitySetVelocity is also what
        // sets the shell's "is this thing flying" bit, which is the answer
        // shell_mover_velocity.h reads to decide whether a stop may clear the
        // velocity. Writing it every frame would be a native call per flyer per
        // frame for a value that a rocket never changes at all.
        if (!Same(result.Velocity, mover.Velocity))
        {
            mover.Velocity = result.Velocity;
            _entities.SetVelocity(handle, result.Velocity);
        }

        mover.BouncesLeft = result.BouncesLeft;

        if (!result.Flying)
            mover.Kind = MotionKind.None;

        if (!result.Struck)
            return;

        // AFTER the bounce is set up, so that a class removing the entity is
        // the last word rather than the first.
        //
        // A MANAGED CALL AND NOT A CROSSING. This was ENTITY_OP_IMPACT: the
        // native integrator called back into C# once per impact, which is a
        // crossing per rocket per wall. The entity is already resolved above,
        // so the same "you hit this" arrives as a virtual call. An impact that
        // named nothing hands over the invalid handle, which is what "no
        // entity" looks like everywhere else in this API and is what the world
        // reports as when a trace stopped on brushwork.
        _directory.Resolve(handle)?.OnTouch(trace.Entity);
    }

    private Mover Track(EntityHandle entity)
    {
        if (_movers.TryGetValue(entity, out var mover))
            return mover;

        mover = new Mover();
        _movers[entity] = mover;
        _order.Add(entity);
        return mover;
    }

    /// <summary>
    /// Exact equality, deliberately: this asks "did the integrator change the
    /// number", not "are these close". A tolerance here would swallow the first
    /// frames of a slow fall, where gravity moves the velocity by less than any
    /// epsilon worth writing down and the shell would then never be told.
    /// </summary>
    private static bool Same(in Vector a, in Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// The class name for a message, from the DIRECTORY. Not
    /// <c>EntityHandle.ToString()</c>, which asks the engine three questions.
    /// </summary>
    private string Name(EntityHandle entity)
        => _directory.Resolve(entity)?.ClassName ?? $"#{entity.Value}";

    //-------------------------------------------------------------------------
    // THE PUSH HALF — plan_entity.md step 2g.
    //
    // WHAT MOVED. CStandaloneEntity::UpdateMovement stepped a mover toward a
    // position or an orientation, asked BlockerAt whether anybody was standing
    // where it was about to be, committed the step or refused it, worked out how
    // fast it had actually gone, and reported arrival or obstruction to managed
    // code through two INBOUND crossings. All of that is here now; the
    // arithmetic is MoverMath, which is engine-free and gtested by fact.
    //
    // WHAT IT ADDS: the mover PUSHES what is in its way, riders included, as
    // stock's MOVETYPE_PUSH does — PhysicsPushedEntities, CPhysicsPushedEntities
    // in C#. Its Blocked is told only about something that could not be pushed
    // somewhere valid.
    //
    // WHAT IT COSTS, stated as the brief requires. Per MOVING mover per frame:
    // one GetOrigin, one GetAngles, one SetLocal (one block write and one
    // PositionChanged), a SetVelocity only on the frames the velocity actually
    // changed, and the push's own walk — one EntitiesInSphere over the swept
    // box, then for each candidate its state, origin and bounds, and a sweep and
    // a fit test for each one it pushes. A blocked frame writes the mover twice
    // (out and back), as stock's does. These are managed->native and so are NOT
    // what Crossings counts. Against that it REMOVES two INBOUND crossings —
    // OnManagedMoveDone and OnManagedBlocked are gone, and arrival and
    // obstruction are managed virtual calls.
    //
    // AND ONE COST THAT IS A REGRESSION, said out loud rather than left to be
    // found: SourceSharp_EntitiesInSphere walks EVERY standalone entity
    // (shell_worldquery.cpp:167), where the native stepper walked the engine's
    // spatial partition over the destination box. So the question went from
    // O(log n) to O(n) in the number of entities on this hierarchy, once per
    // moving mover per frame. .claude/entity-port/abi-requests/2g.md asks for
    // the box query that removes it.
    //-------------------------------------------------------------------------

    /// <summary>One mover's push state — <c>CStandaloneEntity</c>'s seven members, in C#.</summary>
    private sealed class Push
    {
        /// <summary>
        /// The target, IN THE MOVER'S OWN FRAME: the parent's for a child, the
        /// world's for everything else. Converted once, when the movement is
        /// asked for, so that from then on there is one arithmetic and the only
        /// difference is how the result is committed.
        /// </summary>
        public Vector MoveTarget;
        public float MoveSpeed;
        public bool MovingLinear;

        public QAngle AngleTarget;
        public float AngleSpeed;
        public bool MovingAngular;

        /// <summary>A continuous spin, which never arrives and so never reports.</summary>
        public QAngle Spin;

        /// <summary>What was last written to the shell, so an unchanged velocity costs no call.</summary>
        public Vector Velocity;
    }

    private readonly Dictionary<EntityHandle, Push> _pushers = new();
    private readonly List<EntityHandle> _pushOrder = new();
    private readonly List<EntityHandle> _pushPass = new();

    /// <summary>How many movers this pass stepped. Instrumentation; see the type's remarks.</summary>
    public int PushedLastFrame { get; private set; }

    /// <summary>How many entities are being pushed at all.</summary>
    public int PushCount => _pushers.Count;

    /// <summary>Whoever a mover last refused a step to, for <c>cs_sv_movers_blocked</c>.</summary>
    public EntityHandle LastBlocker { get; private set; } = EntityHandle.None;

    //-------------------------------------------------------------------------
    // SURVIVING A HOT RELOAD -- ISaveCustom's third tier.
    //
    // Both tables above are module-scoped managed objects, so the unload
    // destroys them, and NOTHING rebuilds them: the only entry points are
    // Launch and MoveToPosition/MoveToAngles/Spin, which are commands game code
    // issues at the moment of the event. Nothing re-issues one.
    //
    // AND IT CANNOT BE RE-DERIVED FROM NATIVE, which is the part that makes
    // this a save rather than a rebuild. shell_standalone.h:1173-1192 records
    // that m_vecMoveTarget, m_angMoveTarget, m_flMoveSpeed, m_flAngleSpeed,
    // m_bMovingLinear and m_bMovingAngular were DELETED from the shell when
    // this class took them. What native still holds are mirrors -- velocity,
    // gravity scale, bounces -- and a mirror of a velocity does not say where
    // the door was going.
    //
    // What a player sees without it: every rocket and grenade in the air stops
    // dead and hangs there forever, never impacting because nothing traces it;
    // every door, lift and plat mid-travel freezes half-open, never fires
    // OnMoveDone, and leaves its riders the floor velocity it was last handed.
    //
    // ONE ROW PER ENTITY PER TABLE, and the two are separate rows because an
    // entity can be in both (a projectile is never pushed, but the shape does
    // not forbid it and a merged row would have to say which half was absent).
    //-------------------------------------------------------------------------

    /// <summary>The row kind, so one list can carry both tables.</summary>
    public const string RowKind = "kind";

    /// <summary><inheritdoc cref="RowKind" path="/summary"/></summary>
    public const string RowFly = "fly";

    /// <summary><inheritdoc cref="RowKind" path="/summary"/></summary>
    public const string RowPush = "push";

    /// <summary>Writes every mover and every pusher into the record.</summary>
    public void SaveInto(ISaveWriter writer)
    {
        foreach (var (handle, mover) in _movers)
        {
            if (mover.Kind == MotionKind.None)
                continue;

            writer.AddRow()
                  .Set(RowKind, RowFly)
                  .Set("e", handle)
                  .Set("k", (int)mover.Kind)
                  .Set("v", mover.Velocity)
                  .Set("g", mover.GravityScale)
                  .Set("b", mover.BouncesLeft)
                  .Set("o", mover.Owner);
        }

        foreach (var (handle, push) in _pushers)
        {
            if (!push.MovingLinear && !push.MovingAngular
                && push.Spin.Pitch == 0f && push.Spin.Yaw == 0f && push.Spin.Roll == 0f)
            {
                continue;
            }

            writer.AddRow()
                  .Set(RowKind, RowPush)
                  .Set("e", handle)
                  .Set("mt", push.MoveTarget)
                  .Set("ms", push.MoveSpeed)
                  .Set("ml", push.MovingLinear)
                  .Set("at", new Vector(push.AngleTarget.Pitch, push.AngleTarget.Yaw,
                                        push.AngleTarget.Roll))
                  .Set("as", push.AngleSpeed)
                  .Set("ma", push.MovingAngular)
                  .Set("sp", new Vector(push.Spin.Pitch, push.Spin.Yaw, push.Spin.Roll))
                  .Set("v", push.Velocity);
        }
    }

    /// <summary>
    /// Puts the two tables back. Answers how many rows landed.
    ///
    /// <para>
    /// <b>STRAIGHT INTO THE ROW, NEVER BACK THROUGH <c>MoveToPosition</c>.</b>
    /// That entry point converts a WORLD destination into the mover's own frame,
    /// and the saved target is already in that frame — putting it through again
    /// would convert it twice. Worse, it tests "am I already there" against the
    /// converted value and fires <c>OnMoveDone</c> when it is, so a door that
    /// was half way would announce an arrival it never made.
    /// </para>
    ///
    /// <para>
    /// A row whose entity no longer exists is DROPPED. A reload keeps the native
    /// world standing so that should not happen, and it is checked because
    /// "cannot happen" is what the entity list said about half of
    /// <c>docs/divergences.md</c>. The spelling of a handle that names nothing
    /// is <see cref="EntityHandle.None"/> (which <c>default</c> is), never the
    /// raw handle 0 — entry zero is worldspawn.
    /// </para>
    /// </summary>
    public int RestoreFrom(ISaveReader reader)
    {
        int landed = 0;

        foreach (ReloadRow row in reader.Rows)
        {
            string kind = row.GetString(RowKind);
            EntityHandle entity = reader.Resolve(row.GetHandle("e"));

            if (entity == EntityHandle.None)
                continue;

            if (kind == RowFly)
            {
                var mover = new Mover
                {
                    Kind = (MotionKind)row.GetInt("k"),
                    Velocity = row.GetVector("v"),
                    GravityScale = row.GetFloat("g"),
                    BouncesLeft = row.GetInt("b"),
                    Owner = reader.Resolve(row.GetHandle("o")),
                };

                if (mover.Kind == MotionKind.None)
                    continue;

                if (!_movers.ContainsKey(entity))
                    _order.Add(entity);

                _movers[entity] = mover;
                landed++;
                continue;
            }

            if (kind != RowPush)
                continue;

            Vector angles = row.GetVector("at");
            Vector spin = row.GetVector("sp");

            var push = new Push
            {
                MoveTarget = row.GetVector("mt"),
                MoveSpeed = row.GetFloat("ms"),
                MovingLinear = row.GetBool("ml"),
                AngleTarget = new QAngle(angles.X, angles.Y, angles.Z),
                AngleSpeed = row.GetFloat("as"),
                MovingAngular = row.GetBool("ma"),
                Spin = new QAngle(spin.X, spin.Y, spin.Z),
                Velocity = row.GetVector("v"),
            };

            if (!_pushers.ContainsKey(entity))
                _pushOrder.Add(entity);

            _pushers[entity] = push;
            landed++;
        }

        return landed;
    }

    //-- being told what a mover is doing --------------------------------------

    /// <summary>
    /// Travel to a world position at a constant speed, reporting arrival through
    /// <c>OnMoveDone</c>. <c>CStandaloneEntity::MoveToPosition</c>.
    ///
    /// <para>
    /// <b>The destination is converted into the mover's own frame ONCE, here.</b>
    /// A mapper and a managed class both name a destination in world
    /// coordinates, and for an unparented entity that is the end of it. For a
    /// child it is not: integrating in world space has the child's own step and
    /// its parent's composition writing one origin from two different frames,
    /// each undoing half of the other's — which is jitter, and reads as a solver
    /// problem rather than as a frame mismatch.
    /// </para>
    ///
    /// <para>
    /// <b>Already there is not a movement, but it IS an arrival.</b> A door told
    /// to open when it is open must still report that it finished, or the
    /// managed class waits forever for a MoveDone that will never come.
    /// </para>
    /// </summary>
    public void MoveToPosition(EntityHandle entity, Vector destination, float speed)
    {
        if (entity == EntityHandle.None)
            return;

        Push push = TrackPush(entity);

        Vector target = destination;
        Vector current = _entities.GetOrigin(entity);

        if (ToLocal(entity, ref target))
            ToLocal(entity, ref current);

        push.MoveTarget = target;
        push.MoveSpeed = speed;
        push.MovingLinear = speed > 0f && !Same(target, current);

        if (!push.MovingLinear)
            MoveDone(entity);
    }

    /// <summary>
    /// Turn to a world orientation at a constant speed.
    /// <c>CStandaloneEntity::MoveToAngles</c>.
    ///
    /// <para>
    /// Into the parent's space for <see cref="MoveToPosition"/>'s reason: a
    /// hatch on a turning platform opens through its own arc, not the world's.
    /// </para>
    ///
    /// <para>
    /// <b>It cancels a continuous spin.</b> Spinning and turning to a target are
    /// the same machinery pointed at different things, and doing both at once
    /// means neither arrives.
    /// </para>
    /// </summary>
    public void MoveToAngles(EntityHandle entity, QAngle destination, float speed)
    {
        if (entity == EntityHandle.None)
            return;

        Push push = TrackPush(entity);

        QAngle target = destination;
        QAngle current = QAngle.FromVector(_entities.GetAngles(entity));

        if (ToLocal(entity, ref target))
            ToLocal(entity, ref current);

        push.AngleTarget = target;
        push.AngleSpeed = speed;
        push.MovingAngular = speed > 0f && target != current;
        push.Spin = QAngle.Zero;

        if (!push.MovingAngular)
            MoveDone(entity);
    }

    /// <summary>
    /// Spin for ever at so many degrees a second about each axis.
    /// <c>CStandaloneEntity::SetAngularVelocity</c>.
    ///
    /// <para>
    /// <b>The shell is told as well</b>, and that is not a duplicate store: a
    /// sensor entity exists to watch something turn and reads the answer back
    /// through <c>EntityGetAngularVelocity</c>, which prefers the solver's
    /// number when there is one and the shell's otherwise. This side holds the
    /// number it INTEGRATES; the shell holds the number anything else READS. The
    /// same split step 2f made for gravity scale and bounces.
    /// </para>
    /// </summary>
    public void Spin(EntityHandle entity, QAngle degreesPerSecond)
    {
        if (entity == EntityHandle.None)
            return;

        Push push = TrackPush(entity);

        push.Spin = degreesPerSecond;
        push.MovingAngular = false;

        _entities.SetAngularVelocity(entity, degreesPerSecond.AsVector());
    }

    /// <summary>
    /// A mover is told to stop. <c>CStandaloneEntity::StopMoving</c>.
    ///
    /// <para>
    /// <b>The velocity is cleared here rather than left to the next frame</b>,
    /// which would say the same thing one tick later: a stop is answered by
    /// whoever asks first, and a rider sampled between the input and the next
    /// tick would otherwise be handed the floor speed of a mover that had
    /// already been told to stop.
    /// </para>
    ///
    /// <para>
    /// <b>A FLYER'S VELOCITY IS NOT THIS METHOD'S TO CLEAR.</b> Native asked
    /// <c>StandaloneMover_OwnsVelocity(m_bFlying)</c> — "was this velocity set as
    /// a mover's or as a flyer's" — because two owners wrote one member and only
    /// a bit on the shell could tell them apart. Both owners are this class now,
    /// so the question is answered by which set an entity is in, and the bit is
    /// deleted rather than ported.
    /// </para>
    /// </summary>
    public void StopMoving(EntityHandle entity)
    {
        if (!_pushers.TryGetValue(entity, out Push? push))
            return;

        push.MovingLinear = false;
        push.MovingAngular = false;
        push.Spin = QAngle.Zero;

        if (!Same(push.Velocity, Vector.Zero))
        {
            push.Velocity = Vector.Zero;
            _entities.SetVelocity(entity, Vector.Zero);
        }
    }

    /// <summary>Whether this entity is being pushed toward anything at all.</summary>
    public bool IsPushing(EntityHandle entity)
        => _pushers.TryGetValue(entity, out Push? push)
           && (push.MovingLinear || push.MovingAngular || push.Spin != QAngle.Zero);

    /// <summary>The velocity this side last gave a mover. The store of record while it moves.</summary>
    public Vector PushVelocityOf(EntityHandle entity)
        => _pushers.TryGetValue(entity, out Push? push) ? push.Velocity : Vector.Zero;

    //-- the frame -------------------------------------------------------------

    /// <summary>
    /// One frame of push for every mover that is moving.
    ///
    /// <para>
    /// A frametime of zero or less does nothing at all, which is
    /// <c>UpdateMovement</c>'s own guard: a paused server would otherwise
    /// integrate a step over no time and hand every rider a floor speed of
    /// infinity.
    /// </para>
    /// </summary>
    private int RunPushFrame(float frameTime)
    {
        PushedLastFrame = 0;

        if (_pushers.Count == 0)
            return 0;

        // One convar read per frame there are movers, for the push trace.
        _pushed.Trace = ConVarOn(MoversPushedConVar.ConVarName);

        _pushPass.Clear();
        _pushPass.AddRange(_pushOrder);

        for (int i = 0; i < _pushPass.Count; i++)
        {
            EntityHandle handle = _pushPass[i];

            if (!_pushers.TryGetValue(handle, out Push? push))
                continue;

            // An entity that has gone since the snapshot was taken. A door
            // removed by the class its own OnMoveDone woke is the ordinary case,
            // not a corner.
            if (_directory.Resolve(handle) is null)
            {
                ForgetPush(handle);
                continue;
            }

            if (Step(handle, push, frameTime))
                PushedLastFrame++;
        }

        return PushedLastFrame;
    }

    /// <summary>
    /// One mover, one frame. Answers whether it committed anything.
    /// <c>CStandaloneEntity::UpdateMovement</c>, line for line.
    /// </summary>
    private bool Step(EntityHandle handle, Push push, float frameTime)
    {
        // WHERE THE WORLD SAYS IT IS BEFORE ANYTHING IS DECIDED, because the
        // velocity written at the bottom is the difference between this and
        // where it ends up. Read here rather than after the early return so
        // that a mover which stops is measured as having stopped.
        Vector worldBefore = _entities.GetOrigin(handle);

        if (!push.MovingLinear && !push.MovingAngular && push.Spin == QAngle.Zero)
        {
            // STOPPED, and that has to be said out loud. Falling out with the
            // last step's velocity still stored leaves a lift that reached the
            // top handing its passenger fifty units a second of floor for the
            // rest of the map.
            if (!Same(push.Velocity, Vector.Zero))
            {
                push.Velocity = Vector.Zero;
                _entities.SetVelocity(handle, Vector.Zero);
            }

            return false;
        }

        // The frame this movement is measured in: the parent's for a child, the
        // world's for everything else. Both targets were converted into it when
        // the movement was asked for, so from here on there is one arithmetic
        // and the only difference is how the result is committed.
        Vector current = worldBefore;
        QAngle angCurrent = QAngle.FromVector(_entities.GetAngles(handle));
        bool local = ToLocal(handle, ref current, ref angCurrent);

        bool arrived = false;
        bool moved = false;

        // Both halves' destinations first; whether either happens is the push's
        // answer below.
        QAngle angNext = angCurrent;
        bool turnArrives = false;

        if (push.MovingAngular)
        {
            AngularStep turned = MoverMath.AdvanceAngular(angCurrent, push.AngleTarget,
                                                          push.AngleSpeed, frameTime);
            angNext = turned.Angles;
            turnArrives = turned.Arrived;
        }
        else if (push.Spin != QAngle.Zero)
        {
            angNext = MoverMath.AdvanceSpin(angCurrent, push.Spin, frameTime);
        }

        bool unblockable = _directory.Resolve(handle)?.UnblockableByPlayer ?? false;
        EntityHandle blocker = EntityHandle.None;
        Vector blockedAt = worldBefore;

        // CBaseEntity::PerformPush: moving AND rotating rotates first, then
        // moves, and a blocked rotation does not move at all. Each half is
        // CPhysicsPushedEntities' — begin before the commit, finish after it
        // (PhysicsPushedEntities says why the commit splits it).
        //
        // ONE WRITE FOR BOTH HALVES when nothing is in the way, as there was
        // before the push: a rotation with nobody to push needs no position of
        // its own, so it rides along with the move's commit.
        bool rotationPending = false;

        if (angNext != angCurrent)
        {
            Vector wo = current;
            QAngle from = angCurrent;
            QAngle to = angNext;

            if (local)
            {
                Vector ignored = current;
                ToWorld(handle, ref wo, ref from);
                ToWorld(handle, ref ignored, ref to);
            }

            if (_pushed.BeginRotate(handle, Parenting, wo, from, to, unblockable) == 0)
            {
                rotationPending = true;
            }
            else
            {
                Commit(handle, current, angNext);
                blocker = _pushed.Finish();

                if (blocker != EntityHandle.None)
                    Commit(handle, current, angCurrent);
            }

            if (blocker == EntityHandle.None)
            {
                moved = true;

                if (turnArrives)
                {
                    push.MovingAngular = false;
                    arrived = true;
                }
            }
            else
            {
                angNext = angCurrent;
            }
        }

        if (blocker == EntityHandle.None && push.MovingLinear)
        {
            LinearStep stepped = MoverMath.AdvanceLinear(current, push.MoveTarget,
                                                         push.MoveSpeed, frameTime);

            // The push is a WORLD question — who is standing where this is about
            // to be — so for a child it is asked of the composed positions
            // rather than of the offsets just computed.
            Vector from = current;
            Vector to = stepped.Origin;
            QAngle worldAngles = angNext;

            if (local)
            {
                QAngle ignored = angNext;
                ToWorld(handle, ref from, ref worldAngles);
                ToWorld(handle, ref to, ref ignored);
            }

            if (_pushed.Trace)
            {
                _log.Msg($"[cs_sv_movers_pushed] #{handle.Value}: world origin {worldBefore}, " +
                         $"{(local ? "parented, local " + current : "unparented")}; " +
                         $"target {push.MoveTarget}; push {from} -> {to}\n");
            }

            int pushing = _pushed.BeginLinear(handle, Parenting, from, to, worldAngles, unblockable);

            Commit(handle, stepped.Origin, angNext);
            rotationPending = false;

            if (pushing > 0)
            {
                blocker = _pushed.Finish();

                if (blocker != EntityHandle.None)
                {
                    blockedAt = to;
                    Commit(handle, current, angNext);
                }
            }

            if (blocker == EntityHandle.None)
            {
                moved = true;

                if (stepped.Arrived)
                {
                    push.MovingLinear = false;
                    arrived = true;
                }
            }
        }

        if (rotationPending)
            Commit(handle, current, angNext);

        if (blocker != EntityHandle.None)
        {
            // Somebody in the way who could not be pushed. This does not decide
            // what that means — a door reverses, a crusher kills, a platform
            // waits — so the mover is back where it was and asks.
            LastBlocker = blocker;
            ReportBlocked(handle, blocker, blockedAt);
            _directory.Resolve(handle)?.OnBlocked(blocker);
        }

        Vector worldAfter = _entities.GetOrigin(handle);

        // HOW FAST IT IS ACTUALLY GOING, from where it was and where it now is.
        // AFTER the commit, so a blocked step measures as zero — which is the
        // truth: a lift waiting for somebody to move out of its way is not
        // moving. Written only when it CHANGED, because an unchanged velocity is
        // a native call for a value the shell already holds.
        Vector velocity = MoverMath.Velocity(worldBefore, worldAfter, frameTime);

        if (!Same(velocity, push.Velocity))
        {
            push.Velocity = velocity;
            _entities.SetVelocity(handle, velocity);
        }

        // THE RIDERS ARE PUSHED, not carried after the fact: stock's push list
        // takes whoever stands on the pusher (CPushBlockerEnum::IsStandingOnPusher)
        // and moves them with it, which is what PhysicsPushedEntities does above.
        //
        // AFTER the commit and BEFORE the arrival is reported: a class whose
        // OnMoveDone removes the mover must not remove it out from under the
        // people standing on it.
        if (arrived)
            MoveDone(handle);

        return moved;
    }

    /// <summary>
    /// The mover's position, through the hierarchy: SetLocal writes an
    /// unparented entity in world space, which is the same operation
    /// SetAbsTransform was.
    /// </summary>
    private void Commit(EntityHandle handle, Vector origin, QAngle angles)
        => Parenting?.SetLocal(handle, origin, angles);

    /// <summary>
    /// WHO stopped a mover, said out loud on request.
    ///
    /// <para>
    /// A brush that refuses to move is the most visible failure there is and the
    /// least explicable one: from outside the server a door that will not shut, a
    /// door told to shut by nothing, and a door blocked by somebody standing in
    /// it are three identical silences. Off by default and printing only on a
    /// frame that is actually blocked, so carrying it costs one convar read per
    /// refused step.
    /// </para>
    /// </summary>
    private void ReportBlocked(EntityHandle mover, EntityHandle blocker, in Vector destination)
    {
        if (!BlockedReportingOn())
            return;

        var (mins, maxs) = _entities.GetBounds(blocker);
        Vector origin = _entities.GetOrigin(blocker);
        var (_, moverMaxs) = _entities.GetBounds(mover);

        _log.Msg($"[cs_sv_movers_blocked] '{Name(mover)}' refused a step to {destination}; " +
                $"its top is {_entities.GetOrigin(mover).Z + moverMaxs.Z:F1}. " +
                $"Blocked by '{Name(blocker)}' box {origin + mins} .. {origin + maxs}, " +
                $"feet {origin.Z + mins.Z:F1}\n");
    }

    /// <summary>
    /// <c>cs_sv_movers_blocked</c>, read through the console BY NAME rather than
    /// through an injected <c>ConVarRegistry</c>.
    ///
    /// <para>
    /// Not a preference: <see cref="EntityMotion"/> is constructed before
    /// <c>ServerModule</c> registers its ConVars and publishes them to the
    /// container, so an injected instance would be null for the life of the
    /// object. The lookup costs one native call and one small string, and it is
    /// only made on a frame that is actually blocked.
    /// </para>
    /// </summary>
    private bool BlockedReportingOn() => ConVarOn(MoversBlockedConVar.ConVarName);

    private bool ConVarOn(string name)
    {
        string? value = _console.FindConVarValue(name);

        return value is not null && value.Length != 0 && value != "0";
    }

    private void MoveDone(EntityHandle entity) => _directory.Resolve(entity)?.OnMoveDone();

    private Push TrackPush(EntityHandle entity)
    {
        if (_pushers.TryGetValue(entity, out Push? push))
            return push;

        push = new Push();
        _pushers[entity] = push;
        _pushOrder.Add(entity);
        return push;
    }

    /// <summary>It is gone. Drops the row.</summary>
    public bool ForgetPush(EntityHandle entity)
    {
        if (!_pushers.Remove(entity))
            return false;

        _pushOrder.Remove(entity);
        return true;
    }

    //-- the frame this pass is measured in ------------------------------------

    /// <summary>
    /// A world place, and optionally a facing, in the mover's own frame. False,
    /// and the arguments untouched, when nothing is carrying it.
    /// </summary>
    private bool ToLocal(EntityHandle entity, ref Vector origin)
    {
        QAngle ignored = QAngle.Zero;
        return ToLocal(entity, ref origin, ref ignored);
    }

    private bool ToLocal(EntityHandle entity, ref QAngle angles)
    {
        Vector ignored = Vector.Zero;
        return ToLocal(entity, ref ignored, ref angles);
    }

    private bool ToLocal(EntityHandle entity, ref Vector origin, ref QAngle angles)
        => Parenting is not null && Parenting.WorldToLocal(entity, ref origin, ref angles);

    private bool ToWorld(EntityHandle entity, ref Vector origin, ref QAngle angles)
        => Parenting is not null && Parenting.LocalToWorld(entity, ref origin, ref angles);
}

/// <summary>
/// Name whoever stops a managed mover, once per refused step.
///
/// <para>
/// A brush that refuses to move is the most visible failure in a map and the
/// least explicable one, and both of the blocker bugs this tree has had cost a
/// measurement session apiece for exactly that reason — the first because a
/// player was invisible to the test and nothing said so, the second because a
/// platrot reversed off something and would not name it.
/// </para>
///
/// <para>
/// Declared here rather than in <c>ConVars.cs</c> because the thing it reports
/// on is here; <c>ConVarRegistry.RegisterAll</c> scans the whole assembly, so
/// where the declaration sits changes nothing about whether it registers.
/// </para>
/// </summary>
[ConVar(MoversBlockedConVar.ConVarName, "0", Help = "Name whoever stops a managed mover, once per refused step.",
        Flags = ConVarFlags.Cheat)]
public sealed class MoversBlockedConVar : BoolConVar
{
    /// <summary>Its console name. <c>Name</c> itself belongs to <c>ConVarBase</c>.</summary>
    public const string ConVarName = "cs_sv_movers_blocked";
}

/// <summary>
/// Say what every managed mover's push found and decided: each candidate and
/// why it was or was not pushed, each sweep, and who blocked. For the case a
/// door goes through somebody and nothing says why.
/// </summary>
[ConVar(MoversPushedConVar.ConVarName, "0", Help = "Log every managed mover's push: candidates, verdicts, sweeps.",
        Flags = ConVarFlags.Cheat)]
public sealed class MoversPushedConVar : BoolConVar
{
    /// <summary>Its console name.</summary>
    public const string ConVarName = "cs_sv_movers_pushed";
}

/// <summary>
/// <see cref="IEngineBrushMovement"/> answered by <see cref="EntityMotion"/>
/// instead of by the engine — <c>plan_entity.md</c> step 2g.
///
/// <para>
/// This is the seam the whole step turns on. Every mover in the game reaches the
/// stepper through <c>ScriptedEntity.MoveTo</c>, <c>RotateTo</c>, <c>Spin</c> and
/// <c>StopMoving</c>, which resolve this contract; registering this in the server
/// module's container puts the managed integrator behind all four without one of
/// the two dozen brush classes changing a line.
/// </para>
///
/// </summary>
internal sealed class ManagedBrushMovement : IEngineBrushMovement
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public ManagedBrushMovement(ILog log, EntityMotion motion) 
    {
        _log = log;
        _motion = motion;
    }

    private readonly EntityMotion _motion;

    public void MoveToPosition(EntityHandle entity, Vector destination, float speed)
        => _motion.MoveToPosition(entity, destination, speed);

    public void MoveToAngles(EntityHandle entity, Vector destination, float speed)
        => _motion.MoveToAngles(entity, QAngle.FromVector(destination), speed);

    public void Spin(EntityHandle entity, Vector degreesPerSecond)
        => _motion.Spin(entity, QAngle.FromVector(degreesPerSecond));

    public void StopMoving(EntityHandle entity) => _motion.StopMoving(entity);
}
