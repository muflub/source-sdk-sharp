//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

using Vector = SourceSharp.Abi.Vector;

namespace SourceSharp.Shared.Anim;

/// <summary>
/// The core's animation policy for an HL2 player: the posture mapping that
/// used to live in the client's <c>PlayerAnimation.cs</c>, re-cast as the
/// <b>last link of the <see cref="IAnimStateLayer"/> chain</b>.
///
/// <para>
/// <b>Managed, shared, and those are two separate decisions.</b>
/// </para>
///
/// <para>
/// MANAGED because it is a rule about a game's players and not a service the
/// engine provides. Stock kept it in <c>CBasePlayerAnimState</c>
/// (base_playeranimstate.cpp), which this build evicted as unreachable —
/// every one of its entry points wanted a <c>CBasePlayer</c>. The arithmetic
/// it did is quoted here, in the language the rest of this port's policy is
/// written in.
/// </para>
///
/// <para>
/// SHARED (it used to be client-only) because phase 0.10 put the whole
/// animation state on the server too — stock's server poses the same
/// skeleton for hitboxes, and with the generic machine now shared, a
/// posture rule that stayed on the client would have left the server's copy
/// of the state machine answering with stock's ACT_MP activities no HL2
/// model in this content carries. Its INPUTS are the same movement state
/// that already travels (velocity, flags, ducked), so both sides
/// independently compute the same pose from the same numbers, which is what
/// agreement means for something that is never sent. The rule does not
/// change where a player is or what they can hit, so nothing here needs
/// agreeing on the wire.
/// </para>
///
/// <para>
/// The same policy runs for the viewer's own player and for everyone else's,
/// deliberately. What differs between them is not the rule but WHEN the
/// state it reads was computed: the local player's is predicted forward to
/// now, a remote player's is interpolated from the recent past. Writing two
/// rules for that would be writing the same rule twice and letting them
/// drift.
/// </para>
///
/// <para>
/// <b>Where it sits in the chain, and what "HL2 layer" means for its
/// answers.</b> Stock's generic <c>CMultiPlayerAnimState</c> answers with
/// the <c>ACT_MP_*</c> set; HL2MP's mounted content has those sequences in
/// its ACT tables but the deathmatch models this hierarchy actually wears
/// resolve through the <c>ACT_HL2MP_*</c> list
/// (<see cref="Activities(PlayerPosture)"/>), and the walk/run split is a
/// fraction of the player's own cap rather than stock's run-always rule.
/// That is exactly the shape of a stock subclass's <c>CalcMainActivity</c>
/// override, reached as a chain link instead: with this layer alone the
/// player animates as the client's animator always has, and a TF2 layer
/// sits <i>under</i> it (<see cref="LayerOrderDefault"/> lower = asked
/// earlier) and overrides both answers.
/// </para>
///
/// <para>
/// Pure and static for the rules themselves: everything here is a function
/// of its arguments, which is what lets them be checked without a model, a
/// level or an engine — and the rules are exactly where the bugs are,
/// because every one of them fails as a picture that is nearly right. The
/// instance half (the <see cref="IAnimStateLayer"/> methods) is pure too in
/// the same sense: it reads its <see cref="IAnimTarget"/> and decides, and
/// keeps no state between consultations — the STATE belongs to the machine,
/// which is the whole point of the split.
/// </para>
/// </summary>
public sealed class Hl2PostureLayer : IAnimStateLayer
{
    /// <summary>
    /// The HL2 layer's place in the chain: LAST (asked after every mod's
    /// own layer), because it is the fallback every more specific layer
    /// stands on — the same position HL2MP's own policy held at the root of
    /// stock's inheritance tree. A mod picks anything below it; the spacing
    /// leaves the whole range under it to them.
    /// </summary>
    public const int LayerOrderDefault = 1000;

    private readonly IAnimTarget _target;

    public Hl2PostureLayer(IAnimTarget target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <inheritdoc/>
    public int LayerOrder => LayerOrderDefault;

    /// <summary>
    /// The posture decision, replacing the stock gait names: this is the
    /// answer the client's animator has always given (the posture from
    /// <see cref="Choose"/>, the sequence from <see
    /// cref="Resolve(IAnimTarget, PlayerPosture)"/>, the strafe from the
    /// pose), written once and asked by both sides now. The chosen posture
    /// is left on the target (<see cref="IAnimTarget.Posture"/>) for the
    /// host's read-back.
    /// </summary>
    public bool TryOverrideMainSequence(AnimStateContext context, AnimMainSequence main)
    {
        var target = context.Target;

        // The posture is decided from the movement state, exactly as the
        // client's animator decided it from the decoded net state. The jump
        // here is the FL_ONGROUND-free posture the old path produced; the
        // stock JUMP EVENT's flag (m_bJumping) is a different state the
        // machine reads first (HandleJumping) and this layer must not
        // second-guess it — when the machine says jump, that answer stands
        // and we are only asked when it did not claim.
        PlayerPose pose = Choose(target.Velocity, target.BodyYaw,
                                 target.OnGround, target.Ducked, target.MaxSpeed);

        target.Posture = pose.Posture;

        main.Activity = PostureActivityName(pose.Posture);
        main.Sequence = Resolve(target, pose.Posture);

        return true;
    }

    /// <summary>
    /// The HL2 layer has no gesture rules: reloads, flinches and attacks
    /// are the stock switch's, which is what HL2 played before this phase
    /// too (the client never had gesture code at all). Stands aside — the
    /// no-owner arm of this hook.
    /// </summary>
    public bool TryOverrideGesture(AnimStateContext context, AnimGesture gesture) => false;

    /// <summary>
    /// Likewise the event switch: HL2 has no event rules of its own beyond
    /// the stock ones (the jump goes through the machine's flag, not a
    /// gesture — stock's own choice, kept). Stands aside.
    /// </summary>
    public bool TryHandleAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data) => false;

    /// <summary>
    /// The posture's stock-family name, so <see
    /// cref="MultiplayerAnimState.CurrentMainActivity"/> reads like stock
    /// even when this layer decided it: the ACT_MP activity the posture
    /// means. (Not the ACT_HL2MP name the SEQUENCE is resolved by — the
    /// activity is the gait's identity, the sequence list is this content's
    /// answer to it.)
    /// </summary>
    public static string PostureActivityName(PlayerPosture posture) => posture switch
    {
        PlayerPosture.Idle => MpActivities.StandIdle,
        PlayerPosture.CrouchIdle => MpActivities.CrouchIdle,
        PlayerPosture.Walk => MpActivities.Run,       // HL2DM has no walk; see Activities
        PlayerPosture.Run => MpActivities.Run,
        PlayerPosture.CrouchWalk => MpActivities.CrouchWalk,
        PlayerPosture.Jump => MpActivities.Jump,
        _ => MpActivities.StandIdle,
    };

    /// <summary>
    /// The posture, as a sequence on the target's model: asks for each of
    /// the posture's activities in turn and takes the first the model
    /// answers to (moved with the rest of the animator's resolve from
    /// <c>ClientSsPlayer.Resolve</c> — the resolve is part of the policy
    /// now that both sides run it, and both sides' targets read the same
    /// model table).
    ///
    /// <para>
    /// Answers -1 when the model answers to none of them, and the caller
    /// then leaves the model alone — the honest thing to do with something
    /// that has no animation at all, rather than forcing it to sequence 0
    /// and drawing whatever that happens to be.
    /// </para>
    /// </summary>
    public static int Resolve(IAnimTarget target, PlayerPosture posture)
    {
        foreach (string activity in Activities(posture))
        {
            int sequence = target.SelectWeightedSequence(activity);

            if (sequence >= 0)
                return sequence;
        }

        return -1;
    }

    /// <summary>
    /// Below this speed a player is standing still. Stock's
    /// <c>MOVING_MINIMUM_SPEED</c> (base_playeranimstate.h), quoted rather
    /// than chosen: a player who has just let go of the key still carries a
    /// few hundredths of a unit a second of friction residue, and a
    /// threshold of exactly zero leaves them walking on the spot forever.
    /// </summary>
    public const float MovingMinimumSpeed = 0.5f;

    /// <summary>
    /// The fraction of a player's own speed cap above which they are running
    /// rather than walking.
    ///
    /// <para>
    /// A FRACTION of their cap and not a fixed speed, because the cap is not
    /// a constant: <c>sv_maxspeed</c>, a speed powerup and
    /// <c>IClientModule.GetMaxSpeedScale</c> all move it, and a player
    /// slowed to a third of normal is still running as hard as they can. A
    /// fixed threshold would put them in a walk cycle for the whole of it.
    /// </para>
    ///
    /// <para>
    /// One half, because that is where stock's own two speeds sit: HL2's
    /// walk (<c>+speed</c>, 150) against its run (320).
    /// </para>
    /// </summary>
    public const float RunFraction = 0.5f;

    /// <summary>
    /// What the playback rate may not go outside. Stock's clamp from
    /// <c>CalcMovementPlaybackRate</c>, quoted: 0.01 to 10.
    /// </summary>
    public const float MinRate = 0.01f;

    /// <summary>See <see cref="MinRate"/>.</summary>
    public const float MaxRate = 10f;

    /// <summary>
    /// How fast a player is travelling ACROSS THE GROUND.
    ///
    /// <para>
    /// Two dimensions and not three, which is stock's <c>Length2D</c> and is
    /// the difference between a walk cycle and a walk cycle that speeds up
    /// when the player falls. Vertical speed is what jumping and lifts are
    /// made of, and neither is a gait.
    /// </para>
    /// </summary>
    public static float GroundSpeed(Vector velocity)
        => MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);

    /// <summary>
    /// Which gait, from the movement state.
    ///
    /// <para>
    /// The order of the tests is the rule. Being in the air beats everything
    /// — a player who jumps while sprinting is jumping, not sprinting — and
    /// being still beats being crouched, because a crouched player standing
    /// still has a crouched IDLE and not a crouched walk played at rate
    /// zero.
    /// </para>
    /// </summary>
    /// <param name="velocity">How fast, and which way.</param>
    /// <param name="onGround">Whether <c>FL_ONGROUND</c> is set.</param>
    /// <param name="ducked">Whether they are fully crouched.</param>
    /// <param name="maxSpeed">Their current speed cap; see <see cref="RunFraction"/>.</param>
    public static PlayerPosture Posture(Vector velocity, bool onGround, bool ducked, float maxSpeed)
    {
        if (!onGround)
            return PlayerPosture.Jump;

        float speed = GroundSpeed(velocity);

        if (speed <= MovingMinimumSpeed)
            return ducked ? PlayerPosture.CrouchIdle : PlayerPosture.Idle;

        if (ducked)
            return PlayerPosture.CrouchWalk;

        // A cap of zero is a player who cannot move at all, and the division
        // below would be by zero. They are moving, so this is a push or a
        // conveyor carrying them: a walk is the honest gait for it.
        if (maxSpeed <= 0f)
            return PlayerPosture.Walk;

        return speed >= maxSpeed * RunFraction ? PlayerPosture.Run : PlayerPosture.Walk;
    }

    /// <summary>
    /// How fast to play the gait so the feet keep up with the ground.
    ///
    /// <para>
    /// Stock's <c>CalcMovementPlaybackRate</c>: the player's speed divided
    /// by the speed the ANIMATOR built the sequence to travel at, clamped.
    /// Ask the model for the divisor
    /// (<see cref="IAnimTarget.SequenceGroundSpeed"/>) rather than writing
    /// one down — a number written down here is true of male_01 and of no
    /// other model, no build would catch it going stale, and the only
    /// symptom is feet skating across the floor, which reads as a physics
    /// bug and not an animation one.
    /// </para>
    ///
    /// <para>
    /// A sequence that does not travel — every idle, and the jump — answers
    /// a ground speed of ZERO, and that is not an error to be guarded
    /// against: it is the ordinary answer, and the right rate for it is 1.
    /// Dividing by it is the bug.
    /// </para>
    /// </summary>
    /// <param name="speed">The player's ground speed.</param>
    /// <param name="sequenceGroundSpeed">What the sequence was built to travel at.</param>
    public static float Rate(float speed, float sequenceGroundSpeed)
    {
        if (sequenceGroundSpeed <= 0.001f)
            return 1f;

        return Math.Clamp(speed / sequenceGroundSpeed, MinRate, MaxRate);
    }

    /// <summary>
    /// Which way the player is travelling, relative to the way their body
    /// faces — the <c>move_yaw</c> pose parameter.
    ///
    /// <para>
    /// Stock's <c>ComputePoseParam_MoveYaw</c> arithmetic, which is
    /// <c>gaitYaw - bodyYaw</c> folded into (-180, 180] — its
    /// <c>flYaw = -(eyeYaw - gaitYaw)</c> with the negation already applied.
    /// The direction of travel comes from the VELOCITY and the facing from
    /// the body, and keeping them apart is the whole feature: a player
    /// strafing left while looking forward has a body pointing one way and
    /// feet crossing the other, and the 8-way blend is what draws that.
    /// </para>
    ///
    /// <para>
    /// A player who is not moving has no direction of travel, and this
    /// answers 0 for them. Stock instead FREEZES the last one (its
    /// <c>EstimateYaw</c> simply does not update below the threshold) —
    /// which is invisible for stock because the idle sequences do not read
    /// move_yaw at all, and would be a stale strafe leaking into the next
    /// step here. Zero is the honest answer to a question about a direction
    /// that does not exist.
    /// </para>
    /// </summary>
    /// <param name="velocity">How fast, and which way, in world space.</param>
    /// <param name="bodyYaw">Which way the body faces, in degrees.</param>
    public static float MoveYaw(Vector velocity, float bodyYaw)
    {
        if (GroundSpeed(velocity) <= MovingMinimumSpeed)
            return 0f;

        float travelYaw = MathF.Atan2(velocity.Y, velocity.X) * (180f / MathF.PI);

        return Fold(travelYaw - bodyYaw);
    }

    /// <summary>
    /// An angle brought back into (-180, 180].
    ///
    /// <para>
    /// A LOOP and not one subtraction, because the two angles it works on
    /// come from different places: a body yaw arrives off the wire already
    /// normalised, but a travel yaw is an <c>atan2</c> and their difference
    /// is only ever within one turn — while a caller handing this an
    /// unnormalised angle from somewhere else deserves an answer rather than
    /// a number 340 degrees out.
    /// </para>
    /// </summary>
    public static float Fold(float degrees)
    {
        // Bounded: an infinity or a NaN would spin here forever, and a pose
        // parameter is exactly the sort of value that arrives as one when a
        // model is mid-load.
        if (!float.IsFinite(degrees))
            return 0f;

        while (degrees > 180f)
            degrees -= 360f;

        while (degrees <= -180f)
            degrees += 360f;

        return degrees;
    }

    /// <summary>
    /// The whole decision, for callers that want all three answers at once.
    ///
    /// <para>
    /// The rate is left at 1 here and refined by the caller once it has
    /// resolved the posture to a sequence and asked the model what that
    /// sequence travels at — the one number this class cannot compute from
    /// its arguments. See <see cref="Rate"/>.
    /// </para>
    /// </summary>
    public static PlayerPose Choose(Vector velocity, float bodyYaw, bool onGround,
                                    bool ducked, float maxSpeed)
        => new(Posture(velocity, onGround, ducked, maxSpeed),
               1f,
               MoveYaw(velocity, bodyYaw));

    /// <summary>
    /// The activities a posture may be drawn by, best first.
    ///
    /// <para>
    /// A LIST rather than one name, and the reason is a measurement rather
    /// than a hypothetical. The player wears
    /// <c>models/humans/group03/male_01.mdl</c>, which has ONE sequence of
    /// its own and takes its animations from four <c>$includemodel</c> files
    /// — and which four is decided by the search path, not by the model.
    /// This build runs on Source SDK Base 2013 <b>Multiplayer</b>, whose
    /// content mount overrides HL2's <c>models/humans/male_shared.mdl</c>
    /// with HL2DM's. So the merged table on THIS installation is the
    /// deathmatch set: <c>ACT_HL2MP_IDLE_*</c>, <c>ACT_HL2MP_RUN_*</c>,
    /// <c>ACT_HL2MP_WALK_CROUCH_*</c>, <c>ACT_HL2MP_JUMP_*</c> — 106
    /// sequences, and <b>not one of them is ACT_IDLE, ACT_WALK or
    /// ACT_RUN</b>. A policy naming only the single-player activities
    /// resolves nothing, plays nothing, and leaves the model standing in its
    /// reference pose with its arms out, which is indistinguishable from an
    /// animation system that was never written. <c>cs_cl_sequences</c> is
    /// the probe that says which set a live model actually has.
    /// </para>
    ///
    /// <para>
    /// <b>THE WEAPON SUFFIX IS FIXED AT MELEE, and that is a scope
    /// decision.</b> HL2DM has no gait activity that is not per-weapon —
    /// there is an <c>ACT_HL2MP_RUN_PISTOL</c> and an
    /// <c>ACT_HL2MP_RUN_AR2</c> and no <c>ACT_HL2MP_RUN</c>. Choosing
    /// between them means the animation policy knowing which weapon a
    /// player holds, which the client knows for the VIEWER (ClientArsenal)
    /// and does not know for anybody else, so doing it properly is a
    /// networked weapon slot and not a table. The melee poses are the
    /// neutral ones — arms down, nothing raised — which is the right thing
    /// to wear while that is undecided. When it is decided, it is this one
    /// function that changes.
    /// </para>
    ///
    /// <para>
    /// The single-player names are kept BELOW the deathmatch ones in every
    /// list, so a model carrying the HL2 citizen set — or an installation
    /// mounting it — animates from the same policy with no change. The
    /// fallback for anything unmatched is <c>ACT_IDLE</c>, last in every
    /// list: a player standing in an idle is wrong, and a player standing in
    /// the bind pose is broken.
    /// </para>
    ///
    /// <para>
    /// HL2DM HAS NO WALK. Its gaits are <c>run_*</c> and <c>cwalk_*</c> and
    /// nothing between, so a walking player gets the run sequence slowed
    /// down by <see cref="Rate"/> — which is what the deathmatch player
    /// does too, and is why the rate is measured against the sequence's own
    /// ground speed rather than assumed.
    /// </para>
    /// </summary>
    /// <summary>
    /// The six cached tables — <b>cached</b> because the machine asks this
    /// on every main-sequence drive and the phase's fact is that a steady
    /// drive allocates nothing; a per-drive <c>new[]</c> would fail it. Six
    /// <c>ImmutableArray</c> constant tables (the RagdollPolicy shape the
    /// static-state rule allows), not an array OF arrays, whose element
    /// cells the analyzer is right to call mutable.
    /// </summary>
    private static readonly System.Collections.Immutable.ImmutableArray<string> s_Idle =
        System.Collections.Immutable.ImmutableArray.Create(
            "ACT_HL2MP_IDLE_MELEE", "ACT_HL2MP_IDLE_PISTOL", "ACT_IDLE");

    private static readonly System.Collections.Immutable.ImmutableArray<string> s_CrouchIdle =
        System.Collections.Immutable.ImmutableArray.Create(
            "ACT_HL2MP_IDLE_CROUCH_MELEE", "ACT_HL2MP_IDLE_CROUCH_PISTOL",
            "ACT_CROUCHIDLE", "ACT_COVER_LOW", "ACT_IDLE");

    private static readonly System.Collections.Immutable.ImmutableArray<string> s_Walk =
        System.Collections.Immutable.ImmutableArray.Create(
            "ACT_HL2MP_RUN_MELEE", "ACT_HL2MP_RUN_PISTOL", "ACT_WALK", "ACT_RUN", "ACT_IDLE");

    private static readonly System.Collections.Immutable.ImmutableArray<string> s_Run =
        System.Collections.Immutable.ImmutableArray.Create(
            "ACT_HL2MP_RUN_MELEE", "ACT_HL2MP_RUN_PISTOL", "ACT_RUN", "ACT_WALK", "ACT_IDLE");

    private static readonly System.Collections.Immutable.ImmutableArray<string> s_CrouchWalk =
        System.Collections.Immutable.ImmutableArray.Create(
            "ACT_HL2MP_WALK_CROUCH_MELEE", "ACT_HL2MP_WALK_CROUCH_PISTOL",
            "ACT_WALK_CROUCH", "ACT_RUN_CROUCH", "ACT_HL2MP_IDLE_CROUCH_MELEE",
            "ACT_CROUCHIDLE", "ACT_COVER_LOW", "ACT_IDLE");

    private static readonly System.Collections.Immutable.ImmutableArray<string> s_Jump =
        System.Collections.Immutable.ImmutableArray.Create(
            "ACT_HL2MP_JUMP_MELEE", "ACT_HL2MP_JUMP_PISTOL", "ACT_GLIDE", "ACT_JUMP", "ACT_IDLE");

    public static System.Collections.Immutable.ImmutableArray<string> Activities(PlayerPosture posture) => posture switch
    {
        PlayerPosture.Idle => s_Idle,
        PlayerPosture.CrouchIdle => s_CrouchIdle,
        PlayerPosture.Walk => s_Walk,
        PlayerPosture.Run => s_Run,
        PlayerPosture.CrouchWalk => s_CrouchWalk,
        PlayerPosture.Jump => s_Jump,
        _ => s_Idle,
    };

    /// <summary>
    /// Whether a posture's sequence should restart from the beginning when
    /// it is handed over.
    ///
    /// <para>
    /// The gaits must NOT: a walk that restarted whenever the posture was
    /// re-decided would take one step forever. A JUMP must, and it is the
    /// only one: it is a one-shot, and a second jump has to play the
    /// animation again rather than resume a cycle that already ran out. This
    /// is the same distinction the viewmodel's AnimationParity draws,
    /// arrived at from the other side — there the server bumps a counter to
    /// say RESTART, here the posture itself says whether restarting is
    /// meaningful.
    /// </para>
    /// </summary>
    public static bool Restarts(PlayerPosture posture) => posture == PlayerPosture.Jump;
}
