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
/// The player's animation state machine — the port of Valve's
/// <c>CMultiPlayerAnimState</c> (game/shared/Multiplayer/
/// multiplayer_animstate.cpp), the generic half of the multi-layer animation
/// ruling (D10) that every mod's own animation policy layers on top of.
///
/// <para>
/// <b>Pure, and pure in a specific sense.</b> The machine holds state
/// (<see cref="CurrentMainActivity"/>, the feet yaws, <see cref="Jumping"/>,
/// the gesture slots) and mutates it one drive at a time — but it holds a
/// reference to exactly one thing, an <see cref="IAnimTarget"/>, and reaches
/// the world through nothing else. No <c>CBasePlayer</c>: stock's every line
/// went through the player pointer, which is why evicting the class hierarchy
/// under it took the whole file with it. No <c>gpGlobals</c>: the clock
/// arrives as an argument of <see cref="Update"/> and every timestamp the
/// machine keeps is kept in that same unit, which is how one class runs the
/// server's authoritative pose and the client's predicted one without either
/// side's clock leaking into the other.
/// </para>
///
/// <para>
/// <b>Same code on both sides.</b> The server's arms are the player entity's
/// engine reads; the client's are <c>ClientPoseParameters</c>/
/// <c>ClientAnimationLayers</c>' plumbing over the predicted and interpolated
/// net state. Where the two sides genuinely differ — the server has no layer
/// or ground-speed arm — the TARGET answers honestly (NaN, or its own memory)
/// and this class does not branch on side at all. A machine that behaved
/// differently per side would be two machines, and the client's whole claim
/// to be predicting rests on it being the same rule.
/// </para>
///
/// <para>
/// <b>The chain replaces stock's subclass hooks</b> (<see
/// cref="IAnimStateLayer"/>): the three consultation points are exactly
/// where stock's derived games put their overrides — the activity inside
/// <see cref="ComputeMainSequence"/> (where stock called the virtual
/// <c>CalcMainActivity</c>), the gesture slots in <see
/// cref="ComputeGestureSequence"/>, and the event switch in <see
/// cref="DoAnimationEvent"/>. Stock's own base answers remain, and every one
/// of them is reachable: with an empty chain this class IS
/// <c>CMultiPlayerAnimState</c>, which is what the no-owner facts check.
/// </para>
///
/// <para>
/// <b>The seven <c>TF_DLL</c> arms stay out</b> (Stage 3.5, phase 0.10's
/// exclusion): the three reload arms' attribute-hook blocks
/// (<c>mult_reload_time</c>, <c>fast_reload</c>) are the TF half of the event
/// switch, and each reload arm keeps its stock tail of
/// <c>m_flPlaybackRate = 1.0f</c>, which is what remains when the TF clamp
/// is not compiled. A TF2 stage adds them back as a chain layer, not a
/// <c>#if</c>.
/// </para>
///
/// <para>
/// Stock's debug surfaces (<c>anim_showstate</c>, <c>ShowDebugInfo</c>,
/// <c>DebugGestureInfo</c>, <c>DoMovementTest</c>'s duration warnings) are
/// not ported: they printed to a cheat console the facts here replace, and
/// <c>DoMovementTest</c>'s active body was two pose writes whose results its
/// own checks were commented out. Where stock carried two near-duplicate
/// state fields and used only one (<c>m_flLastAimTurnTime</c> versus the
/// pose-data one), BOTH are carried, because <see
/// cref="ComputePoseParam_AimYaw"/>'s first-frame test reads one and its
/// converge arm writes the other, and "correcting" that quirk would be this
/// file deciding instead of recording.
/// </para>
/// </summary>
public sealed class MultiplayerAnimState
{
    /// <summary>Stock's <c>MOVING_MINIMUM_SPEED</c> (multiplayer_animstate.cpp:29).</summary>
    public const float MovingMinimumSpeed = 0.5f;

    /// <summary>Stock's <c>WL_Waist</c> — the water level at which the swim state and its cancel-on-water take hold.</summary>
    public const int WaterLevelWaist = 3;

    /// <summary>Stock's jump grace: the on-ground flag is not believed until this much time has passed since <see cref="DoAnimationEvent"/> said jump (:822).</summary>
    public const float JumpGroundDelay = 0.2f;

    private readonly IAnimTarget _target;
    private readonly AnimStateParams _params;
    private readonly AnimStateLayerChain _chain;

    // The chain's scratch, allocated once: the plan's zero-allocation fact
    // makes "once" a checked claim. A layer must not retain these past the
    // call it was handed them; the machine reuses them on the next drive.
    private readonly AnimMainSequence _mainScratch = new();
    private readonly AnimGesture[] _gestureScratch = new AnimGesture[GestureSlot.Count];
    private readonly GestureSlotState[] _slots = new GestureSlotState[GestureSlot.Count];

    // ---- pose-parameter data (stock's MultiPlayerPoseData_t) --------------
    private int _moveX = -1;
    private int _moveY = -1;
    private int _aimYaw = -1;
    private int _aimPitch = -1;
    private int _moveYaw = -1;
    private int _moveScale = -1;
    private bool _poseParameterInit;
    private float _estimateYaw;
    private float _lastAimTurnTime;   // the POSE-DATA one; AimYaw's init test reads THIS

    // ---- eye / feet (stock's m_flEyeYaw … m_flLastAimTurnTime) ------------
    private float _eyeYaw;
    private float _eyePitch;
    private float _goalFeetYaw;
    private float _currentFeetYaw;
    private float _lastAimTurnTime2;  // the OTHER one; AimYaw's converge arm writes this

    // ---- jump / swim / dying state -----------------------------------------
    private bool _jumping;
    private float _jumpStartTime;
    private bool _firstJumpFrame;
    private bool _inSwim;
    private bool _firstSwimFrame = true;
    private bool _dying;
    private bool _firstDyingFrame = true;

    private string _currentMainActivity = MpActivities.StandIdle;
    private int _specificMainSequence = -1;
    private float _lastAnimationStateClearTime;
    private float _maxGroundSpeed;
    private bool _restartPending;

    // The leg-blend cache stock's GetMovementFlags keyed on the sequence.
    private int _movementSequence = -1;
    private LegAnimType _legAnimType = LegAnimType.NineWay;

    // The clock of the last drive; DoAnimationEvent/RestartMainSequence
    // timestamp with it (stock read gpGlobals->curtime; the clock arrives
    // with Update and this is the same value between drives).
    private float _now;
    private float _frameTime;

    /// <summary>
    /// <see cref="SetupPoseParameters"/>'s re-ask flag, public so the host
    /// can force a fresh resolve; <see cref="OnNewModel"/> is the normal way
    /// it is set.
    /// </summary>
    public bool ForceAimYaw { get; set; }

    public MultiplayerAnimState(IAnimTarget target, AnimStateParams? stateParams = null,
                                AnimStateLayerChain? chain = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _params = stateParams ?? AnimStateParams.Hl2Defaults();
        _chain = chain ?? new AnimStateLayerChain(Array.Empty<IAnimStateLayer>());

        for (int i = 0; i < GestureSlot.Count; i++)
        {
            _gestureScratch[i] = new AnimGesture { Slot = i };
        }

        ResetGestureSlots();
    }

    /// <summary>The player this state belongs to.</summary>
    public IAnimTarget Target => _target;

    /// <summary>The configured speeds — stock's <c>m_MovementData</c>, via <see cref="AnimStateParams"/>.</summary>
    public AnimStateParams Parameters => _params;

    /// <summary>The layers this machine asks, in the order it asks them (the side's composition, <see cref="AnimStateLayers"/>).</summary>
    public IReadOnlyList<IAnimStateLayer> Layers => _chain.Layers;

    //-------------------------------------------------------------------------
    // Stock's Update (multiplayer_animstate.cpp:1327).
    //
    // Same order, same early-outs. The clock arguments are the port's shape:
    // stock's Update took (eyeYaw, eyePitch) and read curtime/frametime out
    // of gpGlobals at six places; the host passes them once instead, and the
    // machine holds them for the calls between drives (DoAnimationEvent,
    // RestartMainSequence) that also want them.
    //
    // Stock's `ShouldDrawLocalPlayer → SetPlaybackRate(1)` client arm is not
    // ported: it exists for the third-person demo camera, and this build has
    // no ShouldDrawLocalPlayer arm and no managed demo camera (divergence
    // row; when a third-person view lands it is one line here and one target
    // push).
    //-------------------------------------------------------------------------

    /// <summary>
    /// One drive: store the eyes, compute the sequences, pose the body.
    /// Stock's <c>Update(eyeYaw, eyePitch)</c>, with the clock it read out of
    /// <c>gpGlobals</c> now passed in — <paramref name="now"/> is the side's
    /// <c>curtime</c>, <paramref name="frameTime"/> its
    /// <c>frametime</c>/decode-delta.
    /// </summary>
    public void Update(float eyeYaw, float eyePitch, float now, float frameTime)
    {
        _now = now;
        _frameTime = frameTime;

        // Stock: `if (!pStudioHdr) return;` — no model, nothing to pose or
        // pose against. The target is the one who knows whether it is
        // wearing anything.
        if (!_target.HasModel)
            return;

        // Stock's ShouldUpdateAnimState: not visible → clear and bail; and
        // (its second half) dead-with-no-death-animation → same. The
        // visibility half is the target's (it is the read that differs per
        // side); the alive half mixes in this class's own _dying.
        if (!_target.ShouldUpdateAnimState || (!(_target.Alive || _dying)))
        {
            ClearAnimationState();
            return;
        }

        _eyeYaw = NormalizeAngle(eyeYaw);
        _eyePitch = NormalizeAngle(eyePitch);

        ComputeSequences();

        if (SetupPoseParameters())
        {
            ComputePoseParam_MoveYaw();
            ComputePoseParam_AimPitch();
            ComputePoseParam_AimYaw();
        }
    }

    /// <summary>Stock's <c>ClearAnimationState</c> (:136): every one-shot flag down, every gesture slot emptied.</summary>
    public void ClearAnimationState()
    {
        _jumping = false;
        _dying = false;
        _lastAnimationStateClearTime = _now;
        _specificMainSequence = -1;

        ResetGestureSlots();
    }

    /// <summary>Stock's <c>OnNewModel</c> (:2146): forget the dials, clear the state — the model that just arrived is a different instrument.</summary>
    public void OnNewModel()
    {
        _poseParameterInit = false;
        _moveX = _moveY = _aimYaw = _aimPitch = _moveYaw = _moveScale = -1;
        _estimateYaw = 0f;
        _lastAimTurnTime = 0f;
        _movementSequence = -1;
        ClearAnimationState();
    }

    //-------------------------------------------------------------------------
    // Read-back facts (what the debug overlay and the facts assert).
    //-------------------------------------------------------------------------

    /// <summary>Stock's <c>GetCurrentMainActivity</c>: the activity NAME the body is playing (or would, before the chain's resolve).</summary>
    public string CurrentMainActivity => _currentMainActivity;

    /// <summary>The sequence <see cref="ComputeMainSequence"/> last handed over, or -1 before the first.</summary>
    public int CurrentMainSequence { get; private set; } = -1;

    /// <summary>Stock's <c>m_bJumping</c>.</summary>
    public bool Jumping => _jumping;

    /// <summary>Stock's <c>m_bInSwim</c>.</summary>
    public bool InSwim => _inSwim;

    /// <summary>Stock's <c>m_bDying</c>.</summary>
    public bool Dying => _dying;

    /// <summary>Stock's <c>m_angRender[YAW]</c>: the body's rendered facing, the feet yaw the aim layer converged.</summary>
    public float RenderYaw { get; private set; }

    /// <summary>Stock's <c>m_flCurrentFeetYaw</c>.</summary>
    public float CurrentFeetYaw => _currentFeetYaw;

    /// <summary>Stock's <c>m_PoseParameterData.m_flEstimateYaw</c>: the estimated travel direction.</summary>
    public float EstimateYawValue => _estimateYaw;

    /// <summary>Stock's <c>m_flMaxGroundSpeed</c>: what the current sequence was last measured to travel at (0 until measured; the server's targets never measure — their ground-speed arm says NaN).</summary>
    public float MaxGroundSpeed => _maxGroundSpeed;

    /// <summary>Stock's <c>m_nSpecificMainSequence</c>: the forced full-body sequence, -1 for none.</summary>
    public int SpecificMainSequence => _specificMainSequence;

    /// <summary>The eyes <see cref="Update"/> last stored, normalised.</summary>
    public float EyeYaw => _eyeYaw;

    /// <summary>As <see cref="EyeYaw"/>, pitch.</summary>
    public float EyePitch => _eyePitch;

    /// <summary>Stock's <c>m_flLastAnimationStateClearTime</c>.</summary>
    public float LastAnimationStateClearTime => _lastAnimationStateClearTime;

    //-------------------------------------------------------------------------
    // Sequences.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Stock's <c>ComputeSequences</c> (:1107), same order: the main
    /// sequence first, then the ground-speed bookkeeping that reads it, then
    /// the gestures.
    /// </summary>
    public void ComputeSequences()
    {
        ComputeMainSequence();
        UpdateInterpolators();
        ComputeGestureSequence();
    }

    /// <summary>
    /// Stock's <c>CalcMainActivity</c> (:951): jump, then duck, then swim,
    /// then die — and only if none of them claimed, move. Returns stock's
    /// activity NAME. Running it every drive (even when a layer will
    /// override the answer) is what advances the jump/swim/dying state —
    /// stock's derived games chain-called <c>BaseClass::CalcMainActivity</c>
    /// for exactly this reason, and the chain here asks the same question in
    /// the opposite direction.
    /// </summary>
    public string CalcMainActivity()
    {
        string idealActivity = MpActivities.StandIdle;

        if (HandleJumping() || HandleDucking() || HandleSwimming() || HandleDying())
        {
            // intentionally blank
        }
        else
        {
            idealActivity = HandleMoving();
        }

        return idealActivity;
    }

    /// <summary>Stock's <c>HandleJumping</c> (:802). True claims ACT_MP_JUMP.</summary>
    private bool HandleJumping()
    {
        if (_jumping)
        {
            if (_firstJumpFrame)
            {
                _firstJumpFrame = false;
                RestartMainSequence();   // Reset the animation.
            }

            // Hit water and the jump animation stops.
            if (_target.WaterLevel >= WaterLevelWaist)
            {
                _jumping = false;
                RestartMainSequence();
            }
            // Don't check the on-ground flag for a sec — sometimes it is
            // still set right when the event comes in (stock's comment).
            else if (_now - _jumpStartTime > JumpGroundDelay)
            {
                if (_target.OnGround)
                {
                    _jumping = false;
                    RestartMainSequence();
                }
            }
        }

        if (!_jumping)
            return false;

        _currentMainActivity = MpActivities.Jump;
        return true;
    }

    /// <summary>Stock's <c>HandleDucking</c> (:845): crouched and moving is a crouchwalk, crouched and still a crouch idle.</summary>
    private bool HandleDucking()
    {
        if (!_target.Ducked)
            return false;

        _currentMainActivity = GetOuterXYSpeed() > MovingMinimumSpeed
            ? MpActivities.CrouchWalk
            : MpActivities.CrouchIdle;

        return true;
    }
    /// <summary>
    /// Whether a sequence has run out: stock's <c>IsSequenceFinished</c>,
    /// the forced-sequence arm's question. The cycle comes from the target
    /// (the client reads the shell's clock; the server answers NaN, which
    /// counts as finished — the server never animates a forced sequence it
    /// cannot time anyway, and the specific sequence clears rather than
    /// sticking).
    /// </summary>
    private bool SequenceFinished(int sequence)
    {
        if (_target.CurrentSequence != sequence || sequence < 0)
            return true;   // not playing it at all is "done with it"

        if (_target.SequenceLooping(sequence))
            return false;

        float cycle = _target.SequenceCycle(sequence);
        return float.IsNaN(cycle) || cycle >= 0.999f;
    }

    /// <summary>Stock's <c>HandleSwimming</c> (:869): waist-deep is the swim state, and its first frame restarts.</summary>
    private bool HandleSwimming()
    {
        if (_target.WaterLevel >= WaterLevelWaist)
        {
            if (_firstSwimFrame)
            {
                RestartMainSequence();
                _firstSwimFrame = false;
            }

            _inSwim = true;
            _currentMainActivity = MpActivities.Swim;
            return true;
        }

        _inSwim = false;
        _firstSwimFrame = true;
        return false;
    }

    /// <summary>Stock's <c>HandleDying</c> (:902): once <see cref="Dying"/> is set the death animation is the answer.</summary>
    private bool HandleDying()
    {
        if (_dying)
        {
            if (_firstDyingFrame)
            {
                RestartMainSequence();
                _firstDyingFrame = false;
            }

            _currentMainActivity = MpActivities.DieSimple;
            return true;
        }

        _firstDyingFrame = true;
        return false;
    }

    /// <summary>Stock's <c>HandleMoving</c> (:932) — and its comment, verbatim: "In TF we run all the time now." Moving is always a run; the walk, when a game has one, is a layer's business (HL2's is <see cref="Hl2PostureLayer"/>).</summary>
    private string HandleMoving() =>
        GetOuterXYSpeed() > MovingMinimumSpeed ? MpActivities.Run : MpActivities.StandIdle;

    /// <summary>Stock's <c>TranslateActivity</c> (:987): in the swim, the stand attack/reload activities become their swim variants.</summary>
    public string TranslateActivity(string actDesired)
    {
        if (!_inSwim)
            return actDesired;

        return actDesired switch
        {
            MpActivities.AttackStandPrimaryFire => MpActivities.AttackSwimPrimaryFire,
            MpActivities.AttackStandSecondaryFire => MpActivities.AttackSwimSecondaryFire,
            MpActivities.AttackStandGrenade => MpActivities.AttackSwimGrenade,
            MpActivities.ReloadStand => MpActivities.ReloadSwim,
            _ => actDesired,
        };
    }

    /// <summary>
    /// Stock's <c>ComputeMainSequence</c> (:1125) — <b>the chain's first
    /// consultation point</b>: stock asked its virtual <c>CalcMainActivity</c>
    /// here, which is precisely where every derived game put itself; the
    /// chain is asked at the same point by the same rule (stock's base
    /// answer remains as the no-layer arm, and both are facts).
    /// </summary>
    public void ComputeMainSequence()
    {
        // The state advance (jump/swim/dying bookkeeping) runs whatever a
        // layer may decide — see CalcMainActivity's comment.
        string stockActivity = CalcMainActivity();

        _mainScratch.Activity = stockActivity;
        _mainScratch.Sequence = _target.SelectWeightedSequence(stockActivity);

        var context = new AnimStateContext(this, _target, _now, _frameTime);
        if (!_chain.TryMainSequence(context, _mainScratch))
        {
            // Nobody overrode: stock's own answer stands, including its
            // fallback — animDesired < 0 becomes sequence 0
            // (:1189-1192). The HL2 layer answers instead, on both sides,
            // and answers -1 only when the model has NOTHING to play —
            // which the layer-side rule below leaves as "leave the model
            // alone" rather than forcing the reference pose.
            if (_mainScratch.Sequence < 0)
                _mainScratch.Sequence = 0;
        }

        // Store the current activity so the aim and fire layers know what to
        // do (stock's comment) — the ACTIVITY, whatever sequence it resolved.
        _currentMainActivity = _mainScratch.Activity;

        // Hook to force playback of a specific requested full-body sequence
        // (stock's m_nSpecificMainSequence arm, :1140).
        if (_specificMainSequence >= 0)
        {
            if (_target.CurrentSequence != _specificMainSequence)
            {
                CurrentMainSequence = _specificMainSequence;
                _target.PlaySequence(_specificMainSequence, 1f, restart: true);
                ResetGroundSpeed();
                return;
            }

            if (!SequenceFinished(_specificMainSequence))
                return;

            _specificMainSequence = -1;
            RestartMainSequence();
            ResetGroundSpeed();
        }

        int animDesired = _mainScratch.Sequence;

        // Stock compares ACTIVITIES (:1180): two sequences tagged the same
        // activity are the same gait and the walk must not stumble when the
        // model's table reshuffles under a fold change.
        bool sameActivity = _target.SequenceActivity(_target.CurrentSequence) ==
                            _target.SequenceActivity(animDesired) &&
                            animDesired >= 0;

        // A layer resolved to nothing on this model: leave it alone — the
        // same honesty the client's animator has always had about a model
        // with no animation at all (forcing sequence 0 draws whatever the
        // file happens to begin with, which on a player can be a death
        // animation).
        if (animDesired < 0)
            return;

        // The rate is measured against the sequence ABOUT TO PLAY — the
        // client's animator always did (Render.SequenceGroundSpeed of the
        // freshly resolved sequence, not of the one still on the body).
        // Stock's own CalcMovementPlaybackRate measured the sequence
        // CURRENTLY set; in this hierarchy the hand-over is the moment the
        // rate is chosen, so measuring the stale sequence would play the
        // new gait at the old gait's feet-per-second for one frame per
        // change — visible as a limp on every start.
        float rate = CalcMovementPlaybackRate(out _, animDesired);

        // The hand-over runs every drive — the rate is the machine's and the
        // shell's clock follows it — but a RESTART is stock's ResetSequence,
        // which fires exactly when the activity changes or something asked
        // for one (:1206), never on a mere rate change while the same gait
        // plays.
        bool restart = !sameActivity || _restartPending;
        _restartPending = false;

        if (!sameActivity || CurrentMainSequence != animDesired)
            CurrentMainSequence = animDesired;

        _target.PlaySequence(animDesired, rate, restart);

        if (!sameActivity)
        {
            // Stock's client-only idle→walk ground-speed interpolation reset
            // (:1196) fed m_flMaxGroundSpeed, which feeds only
            // GetInterpolatedGroundSpeed — and its one consumer, the
            // commented-out line in CalcMovementPlaybackRate, means this
            // side has no interpolation to reset (see UpdateInterpolators).
            // The reset is kept for the value itself.
            ResetGroundSpeed();
        }
    }

    /// <summary>Stock's <c>RestartMainSequence</c> (:787): <c>m_flAnimTime = curtime; SetCycle(0)</c> — in this hierarchy, restart the hand-over's clock.</summary>
    public void RestartMainSequence()
    {
        _restartPending = true;
    }

    /// <summary>Stock's <c>ResetGroundSpeed</c> (client arm): re-measure the current sequence's authored speed.</summary>
    private void ResetGroundSpeed()
    {
        _maxGroundSpeed = Measured(GetCurrentMaxGroundSpeed());
    }

    /// <summary>
    /// Stock's <c>UpdateInterpolators</c> (:1221). The client's
    /// <c>CInterpolatedVar</c> (10 Hz notes + interpolate) is NOT ported:
    /// its only reader, <c>GetInterpolatedGroundSpeed</c>, appears in
    /// <c>CalcMovementPlaybackRate</c> on a commented-out line and nowhere
    /// else in the file — an interpolator for a value nobody reads would be
    /// this file inventing state. The server arm — the plain assignment — is
    /// the whole port.
    /// </summary>
    private void UpdateInterpolators()
    {
        _maxGroundSpeed = Measured(GetCurrentMaxGroundSpeed());
    }

    private static float Measured(float speed) =>
        float.IsNaN(speed) ? 0f : speed;

    /// <summary>
    /// Stock's <c>GetCurrentMaxGroundSpeed</c> (:1009), the pose-parameter
    /// save/restore included: shove the blend to its extreme, ask the model
    /// how fast THAT travels, put the dials back. Returns the target's
    /// honest NaN when the side has no ground-speed arm.
    /// </summary>
    public float GetCurrentMaxGroundSpeed(int sequence = -1)
    {
        if (_moveX < 0 || _moveY < 0)
            return float.NaN;

        if (sequence < 0)
            sequence = _target.CurrentSequence;

        float prevX = _target.GetPoseParameter(_moveX);
        float prevY = _target.GetPoseParameter(_moveY);

        float d = MathF.Max(MathF.Abs(prevX), MathF.Abs(prevY));
        float newX = d == 0f ? 1f : prevX / d;
        float newY = d == 0f ? 0f : prevY / d;

        _target.SetPoseParameter(_moveX, newX);
        _target.SetPoseParameter(_moveY, newY);

        float speed = _target.SequenceGroundSpeed(sequence);

        _target.SetPoseParameter(_moveX, prevX);
        _target.SetPoseParameter(_moveY, prevY);

        return speed;
    }
    /// <summary>Stock's <c>GetOuterXYSpeed</c> (:1859).</summary>
    public float GetOuterXYSpeed()
    {
        Vector vel = GetOuterAbsVelocity();
        return MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
    }

    /// <summary>Stock's <c>GetOuterAbsVelocity</c>: server absolute, client estimated — the TARGET's decision, which is where the side difference belongs.</summary>
    public Vector GetOuterAbsVelocity() => _target.Velocity;

    /// <summary>Stock's <c>CalcMovementSpeed</c> (:1048): the ground speed, or zero and false below the moving threshold.</summary>
    public float CalcMovementSpeed(out bool isMoving)
    {
        Vector vel = GetOuterAbsVelocity();
        float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);

        if (speed > MovingMinimumSpeed)
        {
            isMoving = true;
            return speed;
        }

        isMoving = false;
        return 0f;
    }

    /// <summary>
    /// Stock's <c>CalcMovementPlaybackRate</c> (:1070): speed over the
    /// sequence's authored ground speed, clamped to 0.01..10 — and NOT the
    /// <c>GetInterpolatedGroundSpeed</c> the commented-out line offered, the
    /// live line is the direct measure.
    ///
    /// <para>
    /// A sequence that does not travel answers 0, and the rate for it is 1
    /// — NOT this file's own <c>flGroundSpeed &lt; 0.001f → 0.01f</c> arm:
    /// stock had that arm too (:1080), and nothing in the HL2MP build ever
    /// called it — the rate the deathmatch player actually plays at is
    /// <c>CBasePlayerAnimState::CalcMovementPlaybackRate</c>
    /// (base_playeranimstate.cpp:504, via CSDKPlayerAnimState), whose zero
    /// test returns 1. This hierarchy's client animator followed the live
    /// rule; the phase's gate is that the players animate AS BEFORE, so the
    /// live rule is the one ported and the uncalled arm is the divergence
    /// row that says so. A side that cannot measure at all (the server's
    /// NaN) also plays at 1.
    /// </para>
    /// <para>
    /// <paramref name="sequence"/> -1 measures the sequence currently on the
    /// body (stock's question); the hand-over passes the one about to play
    /// (see its call site).
    /// </summary>
    public float CalcMovementPlaybackRate(out bool isMoving, int sequence = -1)
    {
        float speed = CalcMovementSpeed(out isMoving);
        float result = 1f;

        if (isMoving)
        {
            float groundSpeed = GetCurrentMaxGroundSpeed(sequence);

            if (!float.IsNaN(groundSpeed) && groundSpeed >= 0.001f)
            {
                // Stock's note survives: the sequence-change hand-over resets
                // this to 1 anyway (ResetSequenceInfo's equivalent is our
                // restart argument).
                result = Math.Clamp(speed / groundSpeed, 0.01f, 10f);
            }
        }

        return result;
    }

    //-------------------------------------------------------------------------
    // Pose parameters.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Stock's <c>SetupPoseParameters</c> (:1400): look each dial up ONCE
    /// and remember, including the -1s. Stock's commented-out early-returns
    /// are its own record that a model without the dials is legal; the
    /// lookups below that read -1 make every later write through that index
    /// a no-op, which is the same decision from the write side.
    /// (The pose-data struct also carries <c>m_iBodyHeight</c>; its
    /// <c>ComputePoseParam_BodyHeight</c> is declared and never defined or
    /// called — not ported, which the parity fact can then check.)
    /// </summary>
    public bool SetupPoseParameters()
    {
        if (_poseParameterInit)
            return true;

        if (!_target.HasModel)
            return false;

        _poseParameterInit = true;

        _moveX = _target.LookupPoseParameter("move_x");
        _moveY = _target.LookupPoseParameter("move_y");
        _aimPitch = _target.LookupPoseParameter("body_pitch");
        _aimYaw = _target.LookupPoseParameter("body_yaw");
        _moveYaw = _target.LookupPoseParameter("move_yaw");
        _moveScale = _target.LookupPoseParameter("move_scale");

        return true;
    }

    /// <summary>
    /// Stock's <c>ComputePoseParam_MoveYaw</c> (:1566), both blend arms
    /// verbatim: the 9-way unit-box blend on <c>move_x</c>/<c>move_y</c>,
    /// the 8-way polar one on <c>move_yaw</c>/<c>move_scale</c>, the
    /// <see cref="SlamMoveYaw"/> snap in the middle, and the authored-speed
    /// scale at the end. Which arm a model gets is <see
    /// cref="IAnimTarget.LegAnimType"/>'s answer, re-asked when the sequence
    /// changes exactly as stock's <c>GetMovementFlags</c> did (its
    /// <c>DoMovementTest</c> duration warnings — whose checks were commented
    /// out even in stock — are not ported).
    /// </summary>
    public void ComputePoseParam_MoveYaw()
    {
        EstimateYaw();

        float flAngle = NormalizeAngle(_eyeYaw);

        // Calc side to side turning — the view vs. movement yaw (stock's
        // comment): flYaw = -(eyeYaw - estimateYaw).
        float flYaw = NormalizeAngle(-(flAngle - _estimateYaw));

        float flSpeed = CalcMovementSpeed(out bool isMoving);

        if (isMoving)
        {
            ReaskLegAnimType();

            if (SlamMoveYaw)
                flYaw = SnapYawTo(flYaw);

            if (_legAnimType == LegAnimType.NineWay)
            {
                // Convert yaw back into a vector, push the edges out to the
                // -1..1 box, measure, then scale the vector down to the
                // speed actually walked.
                float x = MathF.Cos(flYaw * (MathF.PI / 180f));
                float y = -MathF.Sin(flYaw * (MathF.PI / 180f));

                float invScale = MathF.Max(MathF.Abs(x), MathF.Abs(y));
                if (invScale != 0f)
                {
                    x /= invScale;
                    y /= invScale;
                }

                _target.SetPoseParameter(_moveX, x);
                _target.SetPoseParameter(_moveY, y);
                float maxSpeed = Measured(_target.SequenceGroundSpeed(_target.CurrentSequence));

                if (!float.IsNaN(maxSpeed) && maxSpeed > flSpeed)
                {
                    x *= flSpeed / maxSpeed;
                    y *= flSpeed / maxSpeed;
                }

                _target.SetPoseParameter(_moveX, x);
                _target.SetPoseParameter(_moveY, y);
            }
            else
            {
                _target.SetPoseParameter(_moveYaw, flYaw);
                _target.SetPoseParameter(_moveScale, 1f);

                float maxSpeed = Measured(_target.SequenceGroundSpeed(_target.CurrentSequence));
                if (!float.IsNaN(maxSpeed) && maxSpeed > flSpeed)
                {
                    _target.SetPoseParameter(_moveScale, flSpeed / maxSpeed);
                }
            }
        }
        else
        {
            // Stock's still-arm zeroes the 9-way dials (the 8-way dials it
            // FREEZES — EstimateYaw's own stale-direction quirk, kept, and
            // kept visible by the posture layer computing the strafe
            // independently from velocity).
            _target.SetPoseParameter(_moveX, 0f);
            _target.SetPoseParameter(_moveY, 0f);
        }
    }

    /// <summary>
    /// <c>mp_slammoveyaw</c> (the convar's port as an injected knob — the
    /// shared machine cannot register a convar on either side without the
    /// host, so the side that has one wires it here): snap the strafe to the
    /// nearest of the authored eight directions.
    /// </summary>
    public bool SlamMoveYaw { get; set; }

    private void ReaskLegAnimType()
    {
        int seq = _target.CurrentSequence;
        if (_movementSequence == seq)
            return;

        _movementSequence = seq;
        _legAnimType = _target.LegAnimType;
    }

    /// <summary>Stock's file-local <c>SnapYawTo</c> (:1486), the 45-degree quantiser, boundaries verbatim.</summary>
    public static float SnapYawTo(float flValue)
    {
        float sign = flValue < 0f ? -1f : 1f;
        if (flValue < 0f)
            flValue = -flValue;

        flValue = flValue switch
        {
            < 23f => 0f,
            < 67f => 45f,
            < 113f => 90f,
            < 157f => 135f,
            _ => 180f,
        };

        return flValue * sign;
    }

    /// <summary>
    /// Stock's <c>EstimateYaw</c> (:1649): moving, the feet point where the
    /// velocity points; still, they drift toward the body's own facing at a
    /// quarter-rate. The exact-zero velocity test (not the moving threshold)
    /// and the un-normalised on-drift value are stock's — the
    /// <c>AngleNormalize</c> whose result stock discards on the drift branch
    /// is why the field can sit outside (-180,180] here and is folded again
    /// at each use.
    /// </summary>
    public void EstimateYaw()
    {
        if (_frameTime == 0f)
            return;

        Vector vel = GetOuterAbsVelocity();
        float bodyYaw = _target.BodyYaw;

        if (vel.X == 0f && vel.Y == 0f)
        {
            float flYawDelta = NormalizeAngle(bodyYaw - _estimateYaw);

            if (_frameTime < 0.25f)
                flYawDelta *= _frameTime * 4f;
            else
                flYawDelta *= _frameTime;

            _estimateYaw += flYawDelta;
        }
        else
        {
            _estimateYaw = Math.Clamp(
                MathF.Atan2(vel.Y, vel.X) * (180f / MathF.PI), -180f, 180f);
        }
    }

    /// <summary>Stock's <c>ComputePoseParam_AimPitch</c> (:1689): the torso reads the eye pitch, negated.</summary>
    public void ComputePoseParam_AimPitch()
    {
        _target.SetPoseParameter(_aimPitch, -_eyePitch);
    }

    /// <summary>
    /// Stock's <c>ComputePoseParam_AimYaw</c> (:1702): feet follow eyes
    /// while moving; standing, the goal yaw is capped 45 degrees from where
    /// the last turn left off and the feet converge at the configured
    /// <see cref="AnimStateParams.BodyYawRate"/>. The two <c>LastAimTurnTime</c>
    /// fields, the 3D velocity length, the consumed-<see cref="ForceAimYaw"/>
    /// flag: all stock's, all kept.
    ///
    /// <para>
    /// What is NOT done here: the server's <c>SetAbsAngles</c> (stock's last
    /// lines — this build's movement code is authoritative for the body's
    /// angles and an animation state that wrote them would be the animation
    /// making a physics decision; <see cref="RenderYaw"/> keeps the value
    /// for the read-back and for the day a render-angle arm exists) and the
    /// client's render-angle store, which is the same field either way and
    /// likewise reaches no shell arm at this base. Both halves are the
    /// divergence rows they name.
    /// </summary>
    public void ComputePoseParam_AimYaw()
    {
        Vector vel = GetOuterAbsVelocity();
        bool bMoving = vel.Length > 1f;   // stock's LENGTH is 3D; kept, quirk and all

        if (bMoving || ForceAimYaw)
        {
            _goalFeetYaw = _eyeYaw;
        }
        else
        {
            if (_lastAimTurnTime <= 0f)
            {
                _goalFeetYaw = _eyeYaw;
                _currentFeetYaw = _eyeYaw;
                _lastAimTurnTime = _now;
            }
            else
            {
                float flYawDelta = NormalizeAngle(_goalFeetYaw - _eyeYaw);

                if (MathF.Abs(flYawDelta) > 45f)
                {
                    float flSide = flYawDelta > 0f ? -1f : 1f;
                    _goalFeetYaw += 45f * flSide;
                }
            }
        }

        _goalFeetYaw = NormalizeAngle(_goalFeetYaw);

        if (_goalFeetYaw != _currentFeetYaw)
        {
            if (ForceAimYaw)
            {
                _currentFeetYaw = _goalFeetYaw;
            }
            else
            {
                ConvergeYawAngles(_goalFeetYaw, _params.BodyYawRate, _frameTime, ref _currentFeetYaw);
                _lastAimTurnTime2 = _now;
            }
        }

        RenderYaw = _currentFeetYaw;

        // The torso dial turns the torso FROM THE ROOT to the eyes. Stock's
        // root is the feet (RenderYaw above); a side that cannot draw the
        // root there draws it at the body's own facing, and posing against
        // the feet on that root would turn the torso twice — eyes plus
        // (eyes − feet). So the dial is measured from whichever root is drawn.
        float root = _target.RootFollowsFeet ? _currentFeetYaw : _target.BodyYaw;
        float flAimYaw = NormalizeAngle(_eyeYaw - root);
        _target.SetPoseParameter(_aimYaw, -flAimYaw);

        ForceAimYaw = false;
    }

    /// <summary>Stock's <c>ConvergeYawAngles</c> (:1793), <c>FADE_TURN_DEGREES</c> 60 included: the turn starts slow (the scale floors at 1%) and snaps home when the remaining angle is inside one frame's turn.</summary>
    public static void ConvergeYawAngles(float goalYaw, float yawRate, float deltaTime, ref float currentYaw)
    {
        const float FadeTurnDegrees = 60f;

        float flDeltaYaw = goalYaw - currentYaw;
        float flDeltaYawAbs = MathF.Abs(flDeltaYaw);
        flDeltaYaw = NormalizeAngle(flDeltaYaw);

        float scale = Math.Clamp(flDeltaYawAbs / FadeTurnDegrees, 0.01f, 1f);
        float flYaw = yawRate * deltaTime * scale;

        if (flDeltaYawAbs < flYaw)
        {
            currentYaw = goalYaw;
        }
        else
        {
            float side = flDeltaYaw < 0f ? -1f : 1f;
            currentYaw += flYaw * side;
        }

        currentYaw = NormalizeAngle(currentYaw);
    }

    /// <summary>Stock's <c>AngleNormalize</c> into (-180, 180]; the non-finite answer is 0 (a pose value mid-load arrives as NaN and must not spin the loop).</summary>
    public static float NormalizeAngle(float degrees)
    {
        if (!float.IsFinite(degrees))
            return 0f;

        while (degrees > 180f)
            degrees -= 360f;

        while (degrees <= -180f)
            degrees += 360f;

        return degrees;
    }

    //-------------------------------------------------------------------------
    // Gestures — stock's "Multiplayer gesture code" section.
    //
    // The slots are a fixed array of mutable structs and the chain sees them
    // through the reusable scratch, so a drive allocates nothing (checked by
    // the allocation fact). Stock's VerifyAnimLayerInSlot tracked a POINTER
    // that had gone stale under a crash; there is no pointer here, and the
    // range half of the check is kept in full.
    //-------------------------------------------------------------------------

    /// <summary>Stock's <c>ResetGestureSlots</c> (:436).</summary>
    public void ResetGestureSlots()
    {
        for (int i = 0; i < GestureSlot.Count; i++)
            ResetGestureSlot(i);
    }

    /// <summary>Stock's <c>ResetGestureSlot</c> (:448): the slot forgets everything and the layer behind it is cleared.</summary>
    public void ResetGestureSlot(int gestureSlot)
    {
        if (!VerifyAnimLayerInSlot(gestureSlot))
            return;

        ref var slot = ref _slots[gestureSlot];
        slot.Slot = GestureSlot.Invalid;
        slot.Activity = string.Empty;
        slot.Sequence = -1;
        slot.AutoKill = false;
        slot.Active = false;
        slot.Weight = 0f;
        slot.Cycle = 0f;
        slot.PrevCycle = 0f;
        slot.PlaybackRate = 1f;

        _target.ClearGestureLayer(gestureSlot);
    }

    /// <summary>Stock's <c>VerifyAnimLayerInSlot</c> (:537), the range half (see the section comment for the pointer half).</summary>
    public bool VerifyAnimLayerInSlot(int gestureSlot) =>
        gestureSlot >= 0 && gestureSlot < GestureSlot.Count;

    /// <summary>Stock's <c>IsGestureSlotActive</c> (:526).</summary>
    public bool IsGestureSlotActive(int gestureSlot) =>
        gestureSlot >= 0 && gestureSlot < GestureSlot.Count && _slots[gestureSlot].Active;

    /// <summary>Stock's <c>IsGestureSlotPlaying</c> (:566): active AND this same activity.</summary>
    public bool IsGestureSlotPlaying(int gestureSlot, string gestureActivity)
    {
        if (!IsGestureSlotActive(gestureSlot))
            return false;

        return _slots[gestureSlot].Activity == gestureActivity;
    }

    /// <summary>What sequence a slot holds, -1 for none.</summary>
    public int GestureSequence(int gestureSlot) => _slots[gestureSlot].Sequence;

    /// <summary>A slot's current cycle (0..1; stock's <c>m_flCycle</c>).</summary>
    public float GestureCycle(int gestureSlot) => _slots[gestureSlot].Cycle;

    /// <summary>A slot's blend weight.</summary>
    public float GestureWeight(int gestureSlot) => _slots[gestureSlot].Weight;

    /// <summary>A slot's activity name.</summary>
    public string GestureActivity(int gestureSlot) => _slots[gestureSlot].Activity;

    /// <summary>A slot's <c>m_bAutoKill</c>.</summary>
    public bool GestureAutoKill(int gestureSlot) => _slots[gestureSlot].AutoKill;

    /// <summary>Stock's <c>GetGesturePlaybackRate</c>: the virtual was a mod hook; the chain is this build's, and the value is stock's base one.</summary>
    public float GesturePlaybackRate => 1f;

    /// <summary>Stock's <c>RestartGesture</c> (:581).</summary>
    public void RestartGesture(int gestureSlot, string gestureActivity, bool autoKill = true)
    {
        if (!VerifyAnimLayerInSlot(gestureSlot))
            return;

        if (!IsGestureSlotPlaying(gestureSlot, gestureActivity))
        {
            // Stock's client arm ran the outgoing gesture's animation
            // events to completion before replacing it (cycle=1 + the
            // event pump). The pump this hierarchy has watches the SHELL's
            // clock, and a replaced slot's tail window is gone before the
            // pump's next frame: interrupted gestures' trailing events are
            // not fired. Divergence row; the bookkeeping cycle is still
            // walked to 1 so the auto-kill timing matches stock's.
            ref var outgoing = ref _slots[gestureSlot];
            if (outgoing.Active)
                outgoing.Cycle = 1f;

            AddToGestureSlot(gestureSlot, TranslateActivity(gestureActivity), autoKill);
            return;
        }

        // Reset the cycle = restart the gesture.
        ref var slot = ref _slots[gestureSlot];
        slot.Cycle = 0f;
        slot.PrevCycle = 0f;
        PushGestureLayer(gestureSlot, restart: true);
    }

    /// <summary>Stock's <c>AddToGestureSlot</c> (:616), both #ifdefs' field list at once. Stock refuses sequence 0 (`<= 0`, :631) — the model's first sequence is not a gesture, and that is kept as stock wrote it.</summary>
    public void AddToGestureSlot(int gestureSlot, string gestureActivity, bool autoKill)
    {
        if (!VerifyAnimLayerInSlot(gestureSlot))
            return;

        int gestureSequence = _target.SelectWeightedSequence(gestureActivity);
        if (gestureSequence <= 0)
            return;

        ref var slot = ref _slots[gestureSlot];
        slot.Slot = gestureSlot;
        slot.Activity = gestureActivity;
        slot.Sequence = gestureSequence;
        slot.AutoKill = autoKill;
        slot.Active = true;
        slot.Weight = 1f;
        slot.PlaybackRate = 1f;
        slot.Cycle = 0f;
        slot.PrevCycle = 0f;

        PushGestureLayer(gestureSlot, restart: true);
    }

    /// <summary>Stock's <c>AddVCDSequenceToGestureSlot</c> (:693): a named-by-index sequence (the VCD's), at a starting cycle, on a slot.</summary>
    public void AddVCDSequenceToGestureSlot(int gestureSlot, int gestureSequence,
                                            float cycle = 0f, bool autoKill = true)
    {
        if (!VerifyAnimLayerInSlot(gestureSlot))
            return;

        if (gestureSequence <= 0)
            return;

        ref var slot = ref _slots[gestureSlot];
        slot.Slot = gestureSlot;
        slot.Activity = MpActivities.Vcd;
        slot.Sequence = gestureSequence;
        slot.AutoKill = autoKill;
        slot.Active = true;
        slot.Weight = 1f;
        slot.PlaybackRate = 1f;
        slot.Cycle = cycle;
        slot.PrevCycle = 0f;

        PushGestureLayer(gestureSlot, restart: true);
    }

    private void PushGestureLayer(int gestureSlot, bool restart)
    {
        ref var slot = ref _slots[gestureSlot];
        _target.SetGestureLayer(gestureSlot, slot.Sequence, slot.Weight,
                                slot.PlaybackRate, slot.Cycle, gestureSlot, restart);
    }

    /// <summary>
    /// Stock's <c>ComputeGestureSequence</c> (:1257) — <b>the chain's second
    /// consultation point</b>: every ACTIVE slot, in slot order, has its
    /// cycle advanced and is then offered to the chain, which may rewrite it
    /// or take it down; the stock autokill/wait-at-1.0 arms run on whatever
    /// the chain left standing.
    /// </summary>
    public void ComputeGestureSequence()
    {
        for (int i = 0; i < GestureSlot.Count; i++)
        {
            if (!_slots[i].Active)
                continue;

            if (!VerifyAnimLayerInSlot(i))
                continue;

            UpdateGestureLayer(i);
        }
    }

    private void UpdateGestureLayer(int gestureSlot)
    {
        ref var slot = ref _slots[gestureSlot];

        if (slot.Sequence <= 0)
        {
            // Stock's server arm: a slot whose activity the shell does not
            // know any more (a model swap under it) is reset
            // (:1311-1314). With the bookkeeping here rather than in a shell
            // pointer, the same net check is "the sequence is gone from the
            // model" — which the read of its duration answers directly.
            if (_target.SequenceDuration(slot.Sequence) <= 0f)
            {
                ResetGestureSlot(gestureSlot);
            }

            return;
        }

        // Stock's client advance (:1289): one cycle = frametime / duration,
        // scaled by the virtual and the slot's own playback rate. A sequence
        // with no duration does not advance (BoneLibraryMath.Cycle's own
        // rule for the same case).
        float duration = _target.SequenceDuration(slot.Sequence);
        if (duration > 0f)
        {
            slot.PrevCycle = slot.Cycle;
            slot.Cycle += (_frameTime / duration) * GesturePlaybackRate * slot.PlaybackRate;
        }

        int preSequence = slot.Sequence;

        var context = new AnimStateContext(this, _target, _now, _frameTime);
        var scratch = _gestureScratch[gestureSlot];
        scratch.Slot = gestureSlot;
        scratch.Activity = slot.Activity;
        scratch.Sequence = slot.Sequence;
        scratch.Weight = slot.Weight;
        scratch.Cycle = slot.Cycle;
        scratch.PlaybackRate = slot.PlaybackRate;
        scratch.AutoKill = slot.AutoKill;
        scratch.Decision = AnimGestureDecision.Keep;

        _chain.TryGesture(context, scratch);

        slot.Activity = scratch.Activity;
        slot.Sequence = scratch.Sequence;
        slot.Weight = scratch.Weight;
        slot.Cycle = scratch.Cycle;
        slot.PlaybackRate = scratch.PlaybackRate;
        slot.AutoKill = scratch.AutoKill;

        if (scratch.Decision == AnimGestureDecision.Kill)
        {
            ResetGestureSlot(gestureSlot);
            return;
        }

        if (slot.Cycle > 1f)
        {
            if (slot.AutoKill)
            {
                ResetGestureSlot(gestureSlot);
                return;
            }

            slot.Cycle = 1f;
        }

        // The shell's mirror follows the bookkeeping: a slot that merely
        // advanced keeps its own running clock (the shell advances that
        // clock itself; the machine's cycle here is the AUTOKILL timer, and
        // pushing it as a clock change would double-run the gesture); one
        // whose CONTENT a layer rewrote is re-pushed as new, which is
        // stock's AddToGestureSlot reaching the same state from the other
        // side.
        PushGestureLayer(gestureSlot, restart: slot.Sequence != preSequence);
    }

    //-------------------------------------------------------------------------
    // Events.
    //-------------------------------------------------------------------------

    /// <summary>Stock's <c>PlayFlinchGesture</c> (:371): an inactive flinch slot takes the requested flinch if the model has it, and the chest one if not.</summary>
    public void PlayFlinchGesture(string activity)
    {
        if (IsGestureSlotActive(GestureSlot.Flinch))
            return;

        if (activity != MpActivities.FlinchChest &&
            _target.SelectWeightedSequence(activity) < 0)
        {
            RestartGesture(GestureSlot.Flinch, MpActivities.FlinchChest);
        }
        else
        {
            RestartGesture(GestureSlot.Flinch, activity);
        }
    }

    /// <summary>
    /// Stock's <c>DoAnimationEvent</c> (:152) — <b>the chain's third
    /// consultation point</b> and stock's whole event switch minus the TF
    /// reload attributes. The stock arms are stock's: which slot, which
    /// activity, which flag, including its dead ones
    /// (<c>CUSTOM_GESTURE_SEQUENCE</c>'s body is commented out in the file
    /// and so is here; <c>SWIM</c>, <c>DOUBLEJUMP</c> and <c>CANCEL</c> have
    /// no arms at all and fall through exactly as they do upstream).
    ///
    /// <para>
    /// The stock signature took an <c>int nData</c> that meant an Activity
    /// index for the three CUSTOM arms and a sequence index for one; without
    /// an Activity enum here, the custom arms take the activity by NAME in
    /// <paramref name="activity"/> and <c>CustomSequence</c> keeps the
    /// int-as-sequence. The clock stock read from gpGlobals is the one the
    /// last <see cref="Update"/> passed.
    /// </summary>
    /// <returns>Whether the chain (true) or the stock switch handled it (still true) — kept simple: the chain consumed, or the core did.</returns>
    public bool DoAnimationEvent(PlayerAnimEvent animEvent, int data = 0, string? activity = null)
    {
        var context = new AnimStateContext(this, _target, _now, _frameTime);
        if (_chain.TryAnimEvent(context, animEvent, data))
            return true;

        switch (animEvent)
        {
            case PlayerAnimEvent.AttackPrimary:
                RestartGesture(GestureSlot.AttackAndReload, MpActivities.AttackStandPrimaryFire);
                break;

            case PlayerAnimEvent.AttackSecondary:
                RestartGesture(GestureSlot.AttackAndReload, MpActivities.AttackStandSecondaryFire);
                break;

            case PlayerAnimEvent.AttackGrenade:
                RestartGesture(GestureSlot.Grenade, MpActivities.AttackStandGrenade);
                break;

            case PlayerAnimEvent.Reload:
                RestartGesture(GestureSlot.AttackAndReload, ReloadActivity(ReloadPhase.Start));
                // The TF attribute block ends with the rate clamped to a
                // float; without TF the only value it could hold is stock's
                // initial one.
                _slots[GestureSlot.AttackAndReload].PlaybackRate = 1f;
                PushGestureLayer(GestureSlot.AttackAndReload, restart: false);
                break;

            case PlayerAnimEvent.ReloadLoop:
                RestartGesture(GestureSlot.AttackAndReload, ReloadActivity(ReloadPhase.Loop));
                _slots[GestureSlot.AttackAndReload].PlaybackRate = 1f;
                PushGestureLayer(GestureSlot.AttackAndReload, restart: false);
                break;

            case PlayerAnimEvent.ReloadEnd:
                RestartGesture(GestureSlot.AttackAndReload, ReloadActivity(ReloadPhase.End));
                _slots[GestureSlot.AttackAndReload].PlaybackRate = 1f;
                PushGestureLayer(GestureSlot.AttackAndReload, restart: false);
                break;

            case PlayerAnimEvent.Jump:
                _jumping = true;
                _firstJumpFrame = true;
                _jumpStartTime = _now;
                RestartMainSequence();
                break;

            case PlayerAnimEvent.Die:
                // Stock Assert(0)s here and plays the death animation
                // anyway ("not supporting this yet" is its comment); the
                // assert's content is carried by HandleDying's existence.
                _dying = true;
                RestartMainSequence();
                break;

            case PlayerAnimEvent.Spawn:
                ClearAnimationState();
                break;

            case PlayerAnimEvent.SnapYaw:
                _lastAimTurnTime = 0f;
                break;

            case PlayerAnimEvent.Custom:
            {
                string ideal = TranslateActivity(activity ?? string.Empty);
                _specificMainSequence = _target.SelectWeightedSequence(ideal);
                RestartMainSequence();
                break;
            }

            case PlayerAnimEvent.CustomGesture:
                RestartGesture(GestureSlot.Custom, activity ?? string.Empty);
                break;

            case PlayerAnimEvent.CustomSequence:
                _specificMainSequence = data;
                RestartMainSequence();
                break;

            case PlayerAnimEvent.CustomGestureSequence:
                // Stock's body is a commented-out RestartGestureSequence —
                // ported as stock's nothing.
                break;

            case PlayerAnimEvent.FlinchChest:
                PlayFlinchGesture(MpActivities.FlinchChest);
                break;
            case PlayerAnimEvent.FlinchHead:
                PlayFlinchGesture(MpActivities.FlinchHead);
                break;
            case PlayerAnimEvent.FlinchLeftArm:
                PlayFlinchGesture(MpActivities.FlinchLeftArm);
                break;
            case PlayerAnimEvent.FlinchRightArm:
                PlayFlinchGesture(MpActivities.FlinchRightArm);
                break;
            case PlayerAnimEvent.FlinchLeftLeg:
                PlayFlinchGesture(MpActivities.FlinchLeftLeg);
                break;
            case PlayerAnimEvent.FlinchRightLeg:
                PlayFlinchGesture(MpActivities.FlinchRightLeg);
                break;

            default:
                break;
        }

        return true;
    }

    private enum ReloadPhase { Start, Loop, End }

    private string ReloadActivity(ReloadPhase phase)
    {
        // Stock picks by the SAME two tests in all three reload arms:
        // FL_DUCKING, then its own swim flag.
        if (_target.Ducked)
            return phase switch
            {
                ReloadPhase.Start => MpActivities.ReloadCrouch,
                ReloadPhase.Loop => MpActivities.ReloadCrouchLoop,
                _ => MpActivities.ReloadCrouchEnd,
            };

        if (_inSwim)
            return phase switch
            {
                ReloadPhase.Start => MpActivities.ReloadSwim,
                ReloadPhase.Loop => MpActivities.ReloadSwimLoop,
                _ => MpActivities.ReloadSwimEnd,
            };

        return phase switch
        {
            ReloadPhase.Start => MpActivities.ReloadStand,
            ReloadPhase.Loop => MpActivities.ReloadStandLoop,
            _ => MpActivities.ReloadStandEnd,
        };
    }

    /// <summary>One gesture slot's bookkeeping (stock's <c>GestureSlot_t</c> minus the layer pointer, whose fields the port owns).</summary>
    private struct GestureSlotState
    {
        public int Slot;
        public string Activity;
        public int Sequence;
        public bool AutoKill;
        public bool Active;
        public float Weight;
        public float Cycle;
        public float PrevCycle;
        public float PlaybackRate;
    }
}

/// <summary>
/// The activity names stock's <c>CMultiPlayerAnimState</c> itself reaches
/// for, spelled as the stock file spells them. These are the
/// <c>ACT_MP_*</c> family — the generic multiplayer set the state machine
/// names; HL2MP's content answers to the <c>ACT_HL2MP_*</c> set instead,
/// which is why the core's posture layer (<see cref="Hl2PostureLayer"/>)
/// resolves its main sequence through <see
/// cref="Hl2PostureLayer.Activities(PlayerPosture)"/> and leaves these
/// names as the stock-arm answer an empty chain exercises.
/// </summary>
public static class MpActivities
{
    // CalcMainActivity's set.
    public const string StandIdle = "ACT_MP_STAND_IDLE";
    public const string CrouchWalk = "ACT_MP_CROUCHWALK";
    public const string CrouchIdle = "ACT_MP_CROUCH_IDLE";
    public const string Swim = "ACT_MP_SWIM";
    public const string DieSimple = "ACT_DIESIMPLE";
    public const string Run = "ACT_MP_RUN";
    public const string Jump = "ACT_MP_JUMP";

    // The event switch's gestures.
    public const string AttackStandPrimaryFire = "ACT_MP_ATTACK_STAND_PRIMARYFIRE";
    public const string AttackStandSecondaryFire = "ACT_MP_ATTACK_STAND_SECONDARYFIRE";
    public const string AttackStandGrenade = "ACT_MP_ATTACK_STAND_GRENADE";
    public const string AttackSwimPrimaryFire = "ACT_MP_ATTACK_SWIM_PRIMARYFIRE";
    public const string AttackSwimSecondaryFire = "ACT_MP_ATTACK_SWIM_SECONDARYFIRE";
    public const string AttackSwimGrenade = "ACT_MP_ATTACK_SWIM_GRENADE";

    public const string ReloadStand = "ACT_MP_RELOAD_STAND";
    public const string ReloadStandLoop = "ACT_MP_RELOAD_STAND_LOOP";
    public const string ReloadStandEnd = "ACT_MP_RELOAD_STAND_END";
    public const string ReloadCrouch = "ACT_MP_RELOAD_CROUCH";
    public const string ReloadCrouchLoop = "ACT_MP_RELOAD_CROUCH_LOOP";
    public const string ReloadCrouchEnd = "ACT_MP_RELOAD_CROUCH_END";
    public const string ReloadSwim = "ACT_MP_RELOAD_SWIM";
    public const string ReloadSwimLoop = "ACT_MP_RELOAD_SWIM_LOOP";
    public const string ReloadSwimEnd = "ACT_MP_RELOAD_SWIM_END";

    public const string FlinchChest = "ACT_MP_GESTURE_FLINCH_CHEST";
    public const string FlinchHead = "ACT_MP_GESTURE_FLINCH_HEAD";
    public const string FlinchLeftArm = "ACT_MP_GESTURE_FLINCH_LEFTARM";
    public const string FlinchRightArm = "ACT_MP_GESTURE_FLINCH_RIGHTARM";
    public const string FlinchLeftLeg = "ACT_MP_GESTURE_FLINCH_LEFTLEG";
    public const string FlinchRightLeg = "ACT_MP_GESTURE_FLINCH_RIGHTLEG";

    /// <summary>What <see cref="MultiplayerAnimState.AddVCDSequenceToGestureSlot"/> tags the slot with.</summary>
    public const string Vcd = "ACT_MP_VCD";
}
