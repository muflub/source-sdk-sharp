//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using Vector = SourceSharp.Abi.Vector;

namespace SourceSharp.Shared.Anim;

/// <summary>
/// Everything <see cref="MultiplayerAnimState"/> asks the world, and the only
/// way it touches the world.
///
/// <para>
/// Stock's <c>CMultiPlayerAnimState</c> was welded to <c>CBasePlayer</c>:
/// every pose-parameter write, every sequence query and every layer push went
/// straight through the player pointer, which is why this build's eviction of
/// the class hierarchy under it took the whole animation state with it — the
/// file could not compile without the class. The port keeps stock's DECISIONS
/// (which activity, which sequence, which pose value —
/// <see cref="MultiplayerAnimState"/>) and pushes every QUESTION and every
/// WRITE out through this interface, which is also the shape the plan
/// (<c>docs/plans/tf2/stage0_core_seams.md</c> phase 0.10) asks for: the
/// server implements it with the player entity's engine arms and the client
/// with <c>ClientPoseParameters</c>/<c>ClientAnimationLayers</c>' plumbing,
/// and neither side has its own copy of the rule any more.
/// </para>
///
/// <para>
/// <b>No new ABI surface.</b> Both implementations are built entirely from
/// members that already exist on the engine areas
/// (<c>IEngineEntities.GetVelocity/GetAngles/GetEyeAngles</c>,
/// <c>IEngineEntityRender.LookupSequenceByActivity/SetPoseParameter/
/// SequenceGroundSpeed/SequenceDuration/PlaySequence/SetAnimLayer/AnimLayer/
/// LookupPoseParameter</c>, and <c>ModelDescriptors</c> for the cached
/// per-model table). Where one side genuinely has no arm for something —
/// sequence ground speed on the server, animation layers on the server — the
/// target answers honestly (<see cref="SequenceGroundSpeed"/> says NaN,
/// meaning "this side cannot know") and the machine keeps the bookkeeping in
/// its own memory. That is a divergence row, not a fork edit.
/// </para>
///
/// <para>
/// <b>The target is per player and it is not required to be thread-safe</b> —
/// the machine that owns it is only ever driven from its side's game thread,
/// and every member here is called from that drive.
/// </para>
/// </summary>
public interface IAnimTarget
{
    /// <summary>
    /// How the player is moving right now: stock's
    /// <c>GetOuterAbsVelocity</c> (multiplayer_animstate.cpp:1837) — the
    /// absolute velocity on the server, the ESTIMATED one on the client
    /// (<c>EstimateAbsVelocity</c>, which for this hierarchy is the predicted
    /// or interpolated velocity the net state already carries).
    /// </summary>
    Vector Velocity { get; }

    /// <summary>
    /// Which way the BODY faces, degrees. Stock reads
    /// <c>GetBasePlayer()->GetLocalAngles().y</c> in
    /// <c>EstimateYaw</c> (:1657); the eye yaw is the argument of
    /// <see cref="MultiplayerAnimState.Update"/>, never this.
    /// </summary>
    float BodyYaw { get; }

    /// <summary>
    /// Whether the side draws the model's ROOT at the machine's
    /// <see cref="MultiplayerAnimState.RenderYaw"/> (stock's
    /// <c>m_angRender[YAW]</c>, the converged feet yaw). Stock always does, and
    /// the torso dial <c>body_yaw</c> is then <c>−(eye − feet)</c>. A side with
    /// no render-angle arm draws the root at <see cref="BodyYaw"/> instead, and
    /// the machine poses the dial against THAT so the torso still lands on the
    /// eyes: <c>−(eye − BodyYaw)</c> (the divergence row names it).
    /// </summary>
    bool RootFollowsFeet { get; }

    /// <summary>
    /// The player's own speed cap (<c>sv_maxspeed</c> after every scale):
    /// HL2's walk/run split is a fraction OF THIS, not a constant — see
    /// <see cref="Hl2PostureLayer.RunFraction"/>.
    /// </summary>
    float MaxSpeed { get; }

    /// <summary><c>FL_ONGROUND</c> (const.h).</summary>
    bool OnGround { get; }

    /// <summary><c>FL_DUCKING</c>: fully crouched, not merely crouching.</summary>
    bool Ducked { get; }

    /// <summary>
    /// <c>m_nWaterLevel</c> (0 above, 1 feet, 2 eyes-minus, 3 waist, 4 full):
    /// stock's swim test is <c>&gt;= WL_Waist</c>, i.e. 3
    /// (multiplayer_animstate.cpp:871).
    /// </summary>
    int WaterLevel { get; }

    /// <summary>Whether the player is alive: stock's <c>IsAlive()</c>.</summary>
    bool Alive { get; }

    /// <summary>
    /// Whether the anim state should run at all: stock's
    /// <c>ShouldUpdateAnimState</c> (:1381) minus the two reads no arm here
    /// has (<c>EF_NODRAW</c> and, on the server, <c>IsDormant</c>), each
    /// named by that method's own comment. The client answers from
    /// <c>IEngineEntityRender.IsDormant</c>; the server answers true.
    /// (Alive is NOT folded in — stock tests it next to this, against
    /// <see cref="Alive"/>.)
    /// </summary>
    bool ShouldUpdateAnimState { get; }

    /// <summary>
    /// Which way this side thinks the legs should blend: stock's
    /// <c>GetMovementFlags</c> (:1527) reads the sequence's
    /// <c>$keyvalues movement</c> and picks 8-way for <c>robot2</c>, 9-way
    /// otherwise. No arm exposes per-sequence <c>$keyvalues</c> in this
    /// build, so the targets answer from what their content actually is
    /// (both sides: 8-way — <see cref="LegAnimType.EightWay"/>) and the
    /// machine still runs stock's re-ask-on-sequence-change around the
    /// answer. A later arm narrows this; it is a divergence row, not a
    /// guess.
    /// </summary>
    LegAnimType LegAnimType { get; }

    /// <summary>
    /// Whether the player is wearing a model at all — stock's
    /// <c>GetModelPtr()</c> null test at the top of <c>Update</c>
    /// (multiplayer_animstate.cpp:1333): with no studio header there is
    /// nothing to pose, nothing to resolve and nothing to blend, and the
    /// drive must not half-run.
    /// </summary>
    bool HasModel { get; }

    /// <summary>
    /// The body's base sequence, current cycle (0..1) — stock's
    /// <c>IsSequenceFinished</c> input, which the forced-sequence arm of
    /// <see cref="MultiplayerAnimState.ComputeMainSequence"/> asks. The
    /// client reads the shell's clock
    /// (<c>IEngineEntityRender.SequenceState</c>); the server has no arm
    /// for it and answers NaN, which the machine treats as finished
    /// (named in that arm's divergence row).
    /// </summary>
    float SequenceCycle(int sequence);

    /// <summary>
    /// <c>GetSequenceActivity(sequence)</c>, as the activity NAME
    /// (<c>ACT_MP_RUN</c>), empty for a sequence with no activity tag.
    /// <see cref="MultiplayerAnimState.ComputeMainSequence"/> compares
    /// activities — not indices — before changing the body, so two
    /// sequences tagged alike are the same gait and the walk does not
    /// stumble when the model's table reshuffles. Both sides answer from
    /// the model table's per-sequence activity.
    /// </summary>
    string SequenceActivity(int sequence);

    /// <summary>
    /// What the body is doing, as the HL2 layer read it last drive. The
    /// machine never reads this; the layer writes it, and the host —
    /// <c>ClientSsPlayer</c>'s hand-over, a test, a report — reads it back
    /// out of the thing whose body it is. <c>null</c> until a layer that
    /// speaks posture has run.
    /// </summary>
    PlayerPosture? Posture { get; set; }

    /// <summary>
    /// <c>LookupPoseParameter(pStudioHdr, name)</c>: the index of a named
    /// pose parameter on the model worn now, or -1 for a model that has no
    /// such blend. Called once per parameter per model
    /// (<see cref="MultiplayerAnimState.SetupPoseParameters"/>), not per
    /// frame; -1 is a legal answer that is remembered as readily as an
    /// index.
    /// </summary>
    int LookupPoseParameter(string name);

    /// <summary>
    /// <c>SetPoseParameter(pStudioHdr, index, value)</c>: turn one dial.
    /// Returns what actually took, as the engine clamped it — stock's own
    /// answer, which the machine feeds back into
    /// <see cref="GetPoseParameter"/> for the save/restore that
    /// <see cref="GetCurrentMaxGroundSpeed"/> needs. A negative index is a
    /// no-op returning 0 (a model without that blend is not an error).
    /// </summary>
    float SetPoseParameter(int index, float value);

    /// <summary>
    /// <c>GetPoseParameter(index)</c>: what a dial reads now. A negative
    /// index answers 0, the same non-answer as
    /// <see cref="SetPoseParameter"/>'s.
    /// </summary>
    float GetPoseParameter(int index);

    /// <summary>
    /// <c>SelectWeightedSequence(activity)</c>: the sequence to play for a
    /// named activity, or -1. Stock weighted it by the activity's
    /// per-sequence weight; this build's arm is the model table's FIRST
    /// sequence tagged with the activity
    /// (<c>IEngineEntityRender.LookupSequenceByActivity</c>,
    /// <c>ModelDescriptor.LookupSequenceByActivity</c>) — the weighting has
    /// no managed surface, which the parity facts pin either way.
    /// </summary>
    int SelectWeightedSequence(string activity);

    /// <summary>
    /// <c>GetSequenceGroundSpeed(sequence)</c>: the speed the ANIMATOR built
    /// the sequence to travel at, units a second.
    ///
    /// <para>
    /// Two different no-answers, and the distinction is the whole point:
    /// <b>0</b> means the sequence genuinely does not travel (every idle,
    /// the jump) and the honest rate for it is 1; <b>NaN</b> means THIS
    /// SIDE cannot know — the server has no sequence-ground-speed arm —
    /// and the machine then leaves its playback rate at 1 rather than
    /// running stock's divide-by-nothing. The client answers from
    /// <c>IEngineEntityRender.SequenceGroundSpeed</c>, the server from NaN.
    /// </para>
    /// </summary>
    float SequenceGroundSpeed(int sequence);

    /// <summary>
    /// How long a sequence runs at the model's own default pose: stock's
    /// <c>SequenceDuration</c>, the divisor of a layer's cycle rate and the
    /// clock a gesture is timed against. Both sides can answer — the client
    /// through <c>IEngineEntityRender.SequenceDuration</c>, the server out
    /// of the <c>ModelDescriptor</c> table, which carries it.
    /// </summary>
    float SequenceDuration(int sequence);

    /// <summary>
    /// Whether a sequence loops (<c>STUDIO_LOOPING</c>): stock asks through
    /// <c>IsSequenceFinished</c> when deciding whether a specific main
    /// sequence has run out. Both sides answer from the sequence's flags.
    /// </summary>
    bool SequenceLooping(int sequence);

    /// <summary>
    /// <c>GetSequence()</c>: what the body's base sequence is right now.
    /// </summary>
    int CurrentSequence { get; }

    /// <summary>
    /// <c>ResetSequence(sequence)</c> plus the playback rate — this
    /// hierarchy's hand-over, which keeps the running clock when only the
    /// rate moved and only rewinds it on <paramref name="restart"/>. The
    /// machine hands over the main sequence on every drive (stock sets
    /// <c>m_flPlaybackRate</c> every frame through
    /// <c>CalcMovementPlaybackRate</c>) and sets <paramref name="restart"/>
    /// only when the sequence itself changed or something called
    /// <see cref="MultiplayerAnimState.RestartMainSequence"/>.
    /// </summary>
    void PlaySequence(int sequence, float playbackRate, bool restart);

    /// <summary>
    /// Fill one gesture layer: stock's <c>AddToGestureSlot</c> field writes
    /// (<c>m_nSequence</c>, weight 1, rate 1, cycle 0, order = the slot)
    /// become one push of the slot's whole state. The client writes it
    /// through <c>IEngineEntityRender.SetAnimLayer</c> and the shell runs
    /// the clock; the server has no layer arm, so its target keeps the
    /// table in its own memory for the read-back and the pose driver that
    /// will one day want it. A <paramref name="restart"/> of false means
    /// "same slot, same content, keep the clock" — the machine knows which
    /// it means and the target must not guess.
    /// </summary>
    void SetGestureLayer(int slot, int sequence, float weight, float playbackRate,
                         float cycle, int order, bool restart);

    /// <summary>Empty one gesture slot: <c>ResetGestureSlot</c>'s visible half.</summary>
    void ClearGestureLayer(int slot);
}

/// <summary>Stock's <c>LegAnimType_t</c> (multiplayer_animstate.h): which blend the legs run.</summary>
public enum LegAnimType
{
    /// <summary>The 9-way <c>move_x</c>/<c>move_y</c> box blend.</summary>
    NineWay,

    /// <summary>The 8-way <c>move_yaw</c>/<c>move_scale</c> polar blend.</summary>
    EightWay,
}
