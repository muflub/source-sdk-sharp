//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The player's physics-shadow state machine — plan_entity.md §3.1 and §3.10,
/// the policy-player lane.
///
/// <para>
/// <b>WHAT MOVED.</b> Step 3f gave a standalone player a body in the solver and
/// left the STATE MACHINE that drives it in C++ —
/// <c>CStandalonePlayer::UpdateVCollision</c> and <c>SetVCollisionState</c>,
/// 479 raw / 236 comment-stripped lines of decisions with no §6 row and no
/// engine asking for any of them: which hull is in the solver, how far ahead
/// the controller is aimed, whether the shove is softened, when a step is a
/// step and when it is a teleport, and when the shadow has fallen so far behind
/// that it must be snapped rather than dragged. That is here now, and native's
/// two remaining halves READ and WRITE and decide nothing.
/// </para>
///
/// <para>
/// <b>IT IS A PORT AND NOT AN INVENTION.</b> Every branch below is stock's, at
/// the line named beside it: <c>CBasePlayer::PostThinkVPhysics</c>
/// (player.cpp:4798), <c>UpdateVPhysicsPosition</c> (:4873), the arrival ramp
/// from <c>PhysicsSimulate</c> (:3368, :3399) and <c>SetVCollisionState</c>
/// (:8620). Where this build differs from stock the divergence is stated at the
/// site, as step 3f stated its four.
/// </para>
///
/// <para>
/// <b>ZERO COUNTED CROSSINGS.</b> The counters count native→managed thunk
/// entries (<c>Crossings.cs</c>). <see cref="Step"/> runs from the managed
/// command loop and reaches native through two OUTBOUND slots, so the boundary
/// count does not move at all. It is not free — two more managed→native calls
/// per command per player, on top of the three
/// <c>PlayerCommandQueue.Run</c> already makes — and that cost is recorded in
/// the lane's handover rather than hidden behind a counter that cannot see it.
/// </para>
///
/// <para>
/// <b>WHY <see cref="Plan"/> IS PURE.</b> It takes a state and answers a plan
/// and touches nothing else, so every arm of it is a <c>[Fact]</c> without a
/// physics environment, a map or a game. The old body could only be checked by
/// walking a player into a crate.
/// </para>
///
/// <para>
/// In <c>SourceSharp.Abi</c> for <see cref="MoveMath"/>'s reason: it is the one
/// assembly resolved to the default load context, so there is exactly one copy
/// of it. The server is its only caller today — the shadow is a body in the
/// server's physics environment — but a class that decides player physics is
/// one that must not be able to differ per side.
/// </para>
///
/// <para>
/// <b>STATELESS ARITHMETIC, STATEFUL RUN</b> (statics to zero): the plan stays
/// static and pure; the step counter belongs to one instance, which the server
/// composition's <see cref="PlayerCommandQueue"/> owns and its commands are
/// handed.
/// </para>
/// </summary>
public sealed unsafe class PlayerShadow
{
    // ---- the hull states, stock's VPHYS_* (player.h) ------------------------

    /// <summary>Standing: the tall hull is in the solver.</summary>
    public const int StateWalk = 0;

    /// <summary>Crouched: the short hull is in the solver.</summary>
    public const int StateCrouch = 1;

    /// <summary>Neither: noclip and observers have no body to collide with.</summary>
    public const int StateNoClip = 2;

    // ---- what PlayerShadowApply is asked to do about a step ----------------

    public const int StepNone = 0;
    public const int StepClimb = 1;
    public const int StepTeleport = 2;

    // ---- the contact bits the probe reports --------------------------------

    /// <summary><c>IPhysicsPlayerController::IsInContact()</c>.</summary>
    public const int ContactController = 0x1;

    /// <summary>
    /// Stock's <c>m_touchedPhysObject</c>, from the move helper's touch list.
    /// </summary>
    public const int ContactTouched = 0x2;

    /// <summary>
    /// The largest frame time the controller is ever told about — stock's
    /// clamp at <c>CBasePlayer::PostThinkVPhysics</c> (player.cpp:4806).
    ///
    /// <para>
    /// A frame that took longer than this, or a paused server reporting zero,
    /// is treated as a tenth of a second. Without the clamp a hitch tells the
    /// controller to cover a second of travel in one step, which is a crate
    /// launched by a player who was standing still while the level loaded.
    /// </para>
    /// </summary>
    public const float MaxShadowFrameTime = 0.1f;

    /// <summary>
    /// A step this small is not a step. Stock's <c>m_outStepHeight &gt; 0.1f</c>
    /// (player.cpp:4855) — below it the controller keeps up on its own and
    /// telling it to climb would jitter the shadow every tick on a flat floor.
    /// </summary>
    public const float StepClimbThreshold = 0.1f;

    /// <summary>
    /// A step this big is a teleport, not a climb. Stock's
    /// <c>m_outStepHeight &gt; 4.0f</c> (player.cpp:4857): above four units the
    /// controller's own <c>StepUp</c> cannot cover it in a tick, so the hull is
    /// placed instead.
    /// </summary>
    public const float StepTeleportThreshold = 4.0f;

    /// <summary>
    /// The halfway aim's weight. Stock's
    /// <c>(GetAbsOrigin() * 0.5f) + (vecWished * 0.5f)</c> (player.cpp:4816) —
    /// aim the shadow between where the move put the player and where their
    /// wish would carry them, so it does not arrive at full speed inside
    /// whatever it is leaning on.
    /// </summary>
    public const float LeaningAimWeight = 0.5f;

    /// <summary>
    /// Whether the shadow is LEANING on something — touching, on the ground,
    /// not mid-step, and not standing on a moveable physics object (stock's
    /// <c>!pPhysGround</c>: a player on a crate is riding it, not leaning on it).
    ///
    /// <para>
    /// It gates the softened aim in <see cref="Plan"/> (stock's
    /// <c>!pPhysGround &amp;&amp; m_touchedPhysObject &amp;&amp;
    /// m_outStepHeight &lt;= 0 &amp;&amp; FL_ONGROUND</c>, player.cpp:4811). The
    /// return half of that aim -- bringing the player up to where the shadow
    /// got -- is stock's VPhysicsShadowUpdate, PlayerShadowRecovery (B7), which
    /// replaced this side's per-command Advance.
    /// </para>
    /// </summary>
    public static bool IsLeaning(in PlayerShadowState s)
        => s.GroundPhysicsMass <= 0f
           && (s.ContactBits & ContactTouched) != 0
           && s.OutStepHeight <= 0.0f
           && (s.Flags & GameMovement.FlOnGround) != 0;

    /// <summary>
    /// THE STATE MACHINE, as a pure function.
    ///
    /// <para>
    /// Answers what native should do to the shadow for one command. Every
    /// branch is named against the stock line it came from. The plan is
    /// complete: native reads nothing else and decides nothing.
    /// </para>
    /// </summary>
    public static PlayerShadowPlan Plan(in PlayerShadowState s)
    {
        var plan = default(PlayerShadowPlan);
        plan.StructSize = (uint)sizeof(PlayerShadowPlan);

        if (s.HasShadow == 0)
            return plan;   // Valid stays 0: there is nothing to steer.

        // THE SWITCH IS HONOURED LIVE, which is what makes an A/B one process.
        // Turning it off takes the body out of the solver at the next COMMAND
        // rather than at the next spawn.
        if (s.Enabled == 0)
        {
            plan.Valid = 1;
            plan.Destroy = 1;
            return plan;
        }

        plan.Valid = 1;

        // STOCK'S ARRIVAL RAMP, player.cpp:3368 and :3399. The first command of
        // a batch asks the shadow to be in place one tick from now, the second
        // two ticks, and so on — "if simulating at 2 * TICK_INTERVAL, add an
        // extra TICK_INTERVAL to position arrival computation", in stock's own
        // words. A constant tick would tell the controller to cover a whole
        // packet's worth of travel in the time one command takes, which is how
        // a shove becomes a kick.
        plan.Arrival = (s.CommandIndex + 1) * s.TickInterval;

        float frameTime = s.FrameTime;
        if (frameTime <= 0.0f || frameTime > MaxShadowFrameTime)
            frameTime = MaxShadowFrameTime;

        // WHICH HULL, from what the move left behind. Stock's
        // SetVCollisionState call in PostThinkVPhysics (player.cpp:4801).
        int state = StateWalk;
        if (s.MoveType == GameMovement.MoveTypeNoClip || s.MoveType == GameMovement.MoveTypeObserver)
            state = StateNoClip;
        else if ((s.Flags & GameMovement.FlDucking) != 0)
            state = StateCrouch;

        plan.State = state;

        if (state != s.State)
        {
            plan.StateChanged = 1;
            plan.StateOrigin = s.Origin;
            plan.StateVelocity = s.Velocity;
        }

        // WHETHER THE SHADOW IS LEANING ON SOMETHING, and it is the whole of
        // the difference between a shove and a launch. Both arms below that
        // SOFTEN the shove are gated on it, and step 3f measured what happens
        // without it: the gate's crate went 218 units at ~350 units a second
        // through a push speed limit of 50.
        //
        // THE MOVE'S TOUCH ONLY (plan_physics_managed A16). Stock's
        // PlayerRunCommand clears m_touchedPhysObject (player.cpp:3782) before
        // the move's Touch sets it (:4774) and this reads it (:4793, :4814).
        // The controller's IsInContact is VPhysicsShadowUpdate's (:8328), after
        // the commands, and is cleared before the next command reads it: it is
        // PlayerShadowRecovery's signal, not the plan's. Counting it here gave
        // a player merely rubbing a prop the softened, slow shove.
        bool touching = (s.ContactBits & ContactTouched) != 0;

        bool onGround = (s.Flags & GameMovement.FlOnGround) != 0;

        Vector newPosition = s.Origin;
        Vector wishVel = s.OutWishVel;

        if (IsLeaning(in s))
        {
            // player.cpp:4816. Aim the shadow HALFWAY between where the move
            // put the player and where their wish velocity would carry them,
            // which is what stops it arriving at full speed inside whatever it
            // is leaning on. OldOrigin is the previous command's origin, as
            // stock's m_oldOrigin is.
            Vector wished = s.OldOrigin + (s.OutWishVel * frameTime);
            newPosition = (s.Origin * LeaningAimWeight) + (wished * LeaningAimWeight);
        }

        // STOCK'S `!(TouchedPhysics() || pPhysGround)`, player.cpp:4813: riding
        // a moveable physics object counts as touching one.
        bool physGround = s.GroundPhysicsMass > 0f;

        if (!(touching || physGround))
        {
            // player.cpp:4834: nothing is in the way, so the shadow may travel
            // as fast as it likes to keep up with a player who is not being
            // resisted.
            //
            // STOCK'S sv_maxspeed FALLBACK IS DROPPED, deliberately, and step
            // 3f's reason is carried across with the move: stock reads
            // `m_flMaxspeed > 0 ? m_flMaxspeed : sv_maxspeed.GetFloat()` and the
            // second arm is for a player whose own number was never set. This
            // player's is set at construction and re-asserted from Movement.cs's
            // registered parameters before any command runs, so the arm is
            // unreachable — and reaching for the convar added a shim→stock
            // cross-edge into movevars_shared.cpp, measured at
            // docs/entity-census.txt 377 → 378 distinct symbols.
            wishVel = new Vector(s.MaxSpeed, s.MaxSpeed, s.MaxSpeed);
        }

        // THE STEP-UP. Game code climbs a ledge in one move; the shadow has to
        // be told, or it stays at the bottom and drags the controller through
        // the step every tick.
        if (s.OutStepHeight > StepClimbThreshold && s.HasShadowPosition != 0)
        {
            plan.StepUp = s.OutStepHeight > StepTeleportThreshold ? StepTeleport : StepClimb;
            plan.StepHeight = s.OutStepHeight;
        }

        // NO SNAP (plan_physics_managed A18): stock has none, and a shadow left
        // far behind by a teleport is teleported by vphysics itself (more than
        // 24 units from its target), measured live with no sweep. Plan.Snap and
        // its native arm stay unused until the shadow apply moves (B4).
        plan.Position = newPosition;
        plan.WishVelocity = wishVel;
        plan.OnGround = onGround ? 1 : 0;

        // UpdateVPhysicsPosition (player.cpp:4854-4867): "if the object is much
        // heavier than the player, treat it as a local coordinate system".
        // IsRideablePhysics is the decision; native looks the object up.
        plan.RideableGround = IsRideable(in s) ? 1 : 0;

        return plan;
    }

    /// <summary>
    /// Stock's <c>IsRideablePhysics( GetGroundVPhysics() )</c>
    /// (player.cpp:4603-4612): ground physics heavier than twice the shadow.
    /// </summary>
    public static bool IsRideable(in PlayerShadowState s)
        => s.GroundPhysicsMass > 0f && s.GroundPhysicsMass > s.ShadowMass * 2f;

    /// <summary>
    /// One command's shadow step: probe, plan, apply.
    ///
    /// <para>
    /// Both calls are OUTBOUND, so this costs no counted crossing. It runs
    /// where stock runs <c>UpdateVPhysicsPosition</c> — immediately after the
    /// command's own teardown (<c>CBasePlayer::PhysicsSimulate</c>,
    /// player.cpp:3395).
    /// </para>
    ///
    /// <para>
    /// A side whose slots are null does nothing at all, which is what the
    /// client is: the shadow is a body in the SERVER's physics environment.
    /// </para>
    /// </summary>
    /// <returns>Whether a plan was applied.</returns>
    /// <param name="stages">The stages of the side the command loop runs on,
    /// over its <see cref="MovementRunner"/> (plan_abi_hotreload D8: it was
    /// <c>Engine.Current</c>), which trace the climb on that side; null, a loop
    /// with no engine, steps nothing.</param>
    /// <param name="player">The player whose shadow steps.</param>
    /// <param name="commandIndex">The loop's command index (the arrival ramp).</param>
    /// <param name="enabled"><c>sourcesharp_player_shadow</c>, as this side's composition reads it.</param>
    internal bool Step(PlayerRunMoveStages? stages, EntityHandle player, int commandIndex, bool enabled)
    {
        if (stages is null || !stages.Api.ShimsAttached || Bodies is not { } bodies)
            return false;

        // The probe is managed's since plan_physics_bones_native_zero 2.5:
        // native's facts and vphysics' own reads (PlayerShadowBodies.Probe).
        if (bodies.Probe(player, out PlayerShadowFacts facts, afterCommand: true) is not { } state)
            return false;

        // THE INDEX IS THE LOOP'S, NOT THE PROBE'S. It is stock's loop counter
        // and the arrival ramp is measured in it.
        state.CommandIndex = commandIndex;

        // AND THE SWITCH IS THIS SIDE'S: "may a player push things" is a game
        // decision and its declaration is managed.
        state.Enabled = enabled ? 1 : 0;

        PlayerShadowPlan plan = Plan(in state);

        if (plan.Valid == 0)
            return false;

        // A climb the controller does itself is first traced up from the
        // shadow, so it never teleports into solid (A19). The hull is the
        // shadow's current one, as stock's UTIL_TraceEntity( this, ... ) is,
        // but from MoveConstantDefaults.Stock rather than the player's scaled hulls
        // (player.cpp:4834-4835): coupled to SsPlayer.ShadowHulls, which reads
        // the same constants, and both change together if a composition ever
        // registers others.
        if (plan.StepUp == StepClimb)
        {
            MoveConstants hulls = MoveConstantDefaults.Stock;
            bool crouched = state.State == StateCrouch;
            Vector mins = crouched ? hulls.DuckHullMin : hulls.HullMin;
            Vector maxs = crouched ? hulls.DuckHullMax : hulls.HullMax;
            Vector from = state.ShadowOrigin;
            Vector to = new(from.X, from.Y, from.Z + plan.StepHeight);
            EntityHullTrace trace = stages.TraceHull(player, from, to, mins, maxs,
                                                        GameMovement.MaskPlayerSolid,
                                                        GameMovement.CollisionGroupPlayerMovement);
            plan.StepHeight = ClampStep(plan.StepHeight, from.Z, in trace);
        }

        if (Trace is { On: true } recorder)
            recorder.Planned(player, in state, in plan);

        bodies.Apply(player, in facts, in plan);
        _steps++;

        return true;
    }

    /// <summary>
    /// The shadows this loop steps: the server composition's
    /// <see cref="PlayerShadowBodies"/> (plan_physics_bones_native_zero 2.5).
    /// Null steps nothing, which is what a side with no physics is.
    /// </summary>
    public PlayerShadowBodies? Bodies { get; set; }

    /// <summary>
    /// Stock's "don't ever teleport into solid" for a climb of four units or
    /// less (player.cpp:4828-4838): the hull is traced up from the SHADOW's
    /// position by the step, and if it is stopped the step becomes the height it
    /// reached (trace.endpos.z - position.z). A start in solid reaches nothing,
    /// so the step is 0 (plan_physics_managed A19).
    /// </summary>
    internal static float ClampStep(float stepHeight, float shadowZ, in EntityHullTrace trace)
    {
        float reached = trace.EndPos.Z - shadowZ;
        return reached < stepHeight ? reached : stepHeight;
    }

    /// <summary>
    /// How many shadow steps have been applied. A readout, so a gate can tell
    /// "the shadow is being driven" from "the slot is wired and answers zero" —
    /// which look the same from a crossing count and are opposite bugs.
    /// </summary>
    public long Steps => _steps;

    /// <summary>Zeroes the readout, so a gate can measure one interval.</summary>
    public void ResetSteps() => _steps = 0;

    /// <summary>
    /// Who is told each command's state and plan, when it is on: the server
    /// composition's <c>cs_sv_playershadow_trace</c>. Null steps silently.
    /// </summary>
    public IPlayerShadowTrace? Trace { get; set; }

    private long _steps;
}

/// <summary>
/// A per-command record of the shadow step, for the push-jam diagnosis: what
/// the probe answered and what the plan made of it, just before native applies
/// it. Off, it costs one property read per command.
/// </summary>
public interface IPlayerShadowTrace
{
    /// <summary>Whether to record at all.</summary>
    bool On { get; }

    /// <summary>One command's probe and the plan that will be applied.</summary>
    void Planned(EntityHandle player, in PlayerShadowState state, in PlayerShadowPlan plan);
}
