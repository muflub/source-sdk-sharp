//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared.Anim;

/// <summary>
/// One mod's layer of a player's animation policy: the shape ruling D10
/// (<c>docs/plans/plan_tf2_port.md</c> §0) gives to animation, and the hook
/// the plan's phase 0.10 names.
///
/// <para>
/// Stock layered a mod's animation by SUBCLASSING — <c>CMPAnimState</c>
/// became <c>CTFRulesPlayerAnimState</c>, overriding
/// <c>CalcMainActivity</c>, <c>DoAnimationEvent</c> and
/// <c>RestartGesture</c>, which is fine for one game and fatal for a mod
/// chain: HL2, then TF2, then a server's own rules cannot each own a
/// subclass of one class. So the machine
/// (<see cref="MultiplayerAnimState"/>) is written once, and at the three
/// stock decision points — <c>CalcMainActivity</c> inside
/// <see cref="MultiplayerAnimState.ComputeMainSequence"/>,
/// <c>ComputeGestureSequence</c>, and <see
/// cref="MultiplayerAnimState.DoAnimationEvent"/> — it asks a CHAIN of
/// layers, in registration order, each of which may answer or stand aside.
/// A mod adds a class or weapon rule by registering one more link; nobody
/// recompiles anything and nothing subclasses anything.
/// </para>
///
/// <para>
/// <b>The chain is first-answer-wins, consulted in a fixed order.</b> That
/// is the stock subclass's `if (handled) return` pattern read bottom-up:
/// the most specific layer answers and the rest never run for that decision
/// — but unlike a subclass chain, "the rest" are siblings, not an inherited
/// base, so the HL2 layer underneath a TF2 layer can still be asked (and
/// IS, because it stands aside exactly where TF2 would have overridden).
/// Consulted in registration order, deterministic: an animation that
/// differs by resolve order between two frames is the same bug as a hook
/// list that shuffles (see <c>Hooks&lt;T&gt;</c>' unique-order rule — a
/// chain whose order is a dictionary's mood is one bad merge away from
/// flapping poses).
/// </para>
///
/// <para>
/// Every method's contract: <b>true means "I decided this; the answer is in
/// the ref/out; nobody after me is asked." false means "I have nothing to
/// say."</b> A layer that peeks without deciding must not return true.
/// </para>
/// </summary>
public interface IAnimStateLayer
{
    /// <summary>
    /// Where this layer sits in the chain: lower numbers are asked first,
    /// and the CORE's own HL2 layer (<see cref="Hl2PostureLayer"/>) sits
    /// LAST — it is the fallback every more specific layer stands on, which
    /// is exactly where HL2MP sat under TF2 in stock's inheritance tree.
    /// Two layers with the same order at the same position are refused at
    /// chain build (<see cref="AnimStateLayerChain"/>), for the same reason
    /// frame participants' ties are.
    /// </summary>
    int LayerOrder { get; }

    /// <summary>
    /// At <see cref="MultiplayerAnimState.ComputeMainSequence"/>,
    /// replacing stock's <c>CalcMainActivity</c>: name the activity the
    /// body should play, and the sequence to play it (stock's
    /// <c>SelectWeightedSequence</c> result).
    ///
    /// <para>
    /// The activity is stock's name for the gait — <c>ACT_MP_STAND_IDLE</c>,
    /// <c>ACT_MP_RUN</c> — and a layer that knows the gait but not this
    /// model's sequences still must resolve the sequence itself, because a
    /// sequence chosen from the activity list is a per-model fact and the
    /// layer that names the gait is the one that should say how IT maps it
    /// (the HL2 layer maps through <see cref="Hl2PostureLayer.Activities"/>).
    /// </para>
    /// </summary>
    bool TryOverrideMainSequence(AnimStateContext context, AnimMainSequence main);

    /// <summary>
    /// At <see cref="MultiplayerAnimState.ComputeGestureSequence"/>, after
    /// a gesture slot's cycle has advanced: decide something about the
    /// still-active gesture in <paramref name="gesture"/> — rewrite its
    /// sequence, its weight, or kill it (<see
    /// cref="AnimGestureDecision.Kill"/>). Consulted for every ACTIVE slot,
    /// slot by slot, the way stock's <c>UpdateGestureLayer</c> loops
    /// (:1275).
    /// </summary>
    bool TryOverrideGesture(AnimStateContext context, AnimGesture gesture);

    /// <summary>
    /// At <see cref="MultiplayerAnimState.DoAnimationEvent"/>, BEFORE the
    /// stock switch: decide what the event means instead of the core's
    /// answer — a TF2 layer turns <c>AttackPre</c> into a crit-ready
    /// gesture, a mod's layer turns <c>Reload</c> into something with its
    /// own slot rule. Returning true consumes the event: the stock arms and
    /// the layers after this one do not see it. Returning false lets it
    /// fall through — to the next layer and then to the core switch, which
    /// is stock's.
    /// </summary>
    bool TryHandleAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data);
}

/// <summary>
/// What a layer is allowed to look at, and only this: the state machine
/// asking the question (read its <c>Current*</c> facts), the target it is
/// driving, and the frame's clock. Deliberately a small struct rather than
/// the machine itself — a layer that can call <c>RestartMainSequence</c>
/// from inside <see cref="IAnimStateLayer.TryOverrideMainSequence"/> is a
/// layer that re-enters the decision it is answering.
/// </summary>
public readonly struct AnimStateContext
{
    public AnimStateContext(MultiplayerAnimState state, IAnimTarget target,
                            float now, float frameTime)
    {
        State = state;
        Target = target;
        Now = now;
        FrameTime = frameTime;
    }

    /// <summary>The machine asking. Read its facts; drive it from the Update, not from here.</summary>
    public MultiplayerAnimState State { get; }

    /// <summary>The player being posed.</summary>
    public IAnimTarget Target { get; }

    /// <summary><c>gpGlobals->curtime</c>, the caller's clock.</summary>
    public float Now { get; }

    /// <summary><c>gpGlobals->frametime</c>, the caller's clock delta.</summary>
    public float FrameTime { get; }
}

/// <summary>
/// The answer to <see cref="IAnimStateLayer.TryOverrideMainSequence"/>: the
/// activity as stock's name, and the sequence to play it (a resolved
/// <c>SelectWeightedSequence</c>).
/// </summary>
public sealed class AnimMainSequence
{
    /// <summary>Stock's activity NAME (<c>ACT_MP_STAND_IDLE</c>, …): why the body plays this, kept for the read-back.</summary>
    public string Activity { get; set; } = MpActivities.StandIdle;

    /// <summary>The sequence, already weighted/resolved against this model. Negative means none — stock then falls back to 0.</summary>
    public int Sequence { get; set; } = -1;
}

/// <summary>
/// One active gesture, as handed to <see
/// cref="IAnimStateLayer.TryOverrideGesture"/>: the slot's bookkeeping.
/// Everything is mutable — a layer that decides to change the gesture
/// changes these, and the machine writes the result back to the target
/// exactly once per slot per drive.
/// </summary>
public sealed class AnimGesture
{
    /// <summary>Which named slot (<see cref="GestureSlot"/>).</summary>
    public int Slot { get; set; } = GestureSlot.Invalid;

    /// <summary>The stock activity name the gesture was started with, or empty.</summary>
    public string Activity { get; set; } = string.Empty;

    /// <summary>The sequence playing in the slot.</summary>
    public int Sequence { get; set; } = -1;

    /// <summary>Blend weight, 0..1.</summary>
    public float Weight { get; set; }

    /// <summary>Cycle position 0..1 (stock's <c>m_flCycle</c>).</summary>
    public float Cycle { get; set; }

    /// <summary>How fast the slot's own clock runs.</summary>
    public float PlaybackRate { get; set; } = 1f;

    /// <summary>Stock's <c>m_bAutoKill</c>: reset the slot when the cycle passes 1.</summary>
    public bool AutoKill { get; set; } = true;

    /// <summary>
    /// The decision for this drive: keep, or take the slot down (the
    /// machine then calls <see cref="IAnimTarget.ClearGestureLayer"/> —
    /// stock's <c>ResetGestureSlot</c> from the autokill arm at
    /// multiplayer_animstate.cpp:1307).
    /// </summary>
    public AnimGestureDecision Decision { get; set; } = AnimGestureDecision.Keep;
}

/// <summary>What a layer decided about an active gesture.</summary>
public enum AnimGestureDecision
{
    /// <summary>Leave it as it stands.</summary>
    Keep,

    /// <summary>Reset the slot — stock's autokill and interrupt paths.</summary>
    Kill,
}
