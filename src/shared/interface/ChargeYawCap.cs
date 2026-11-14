namespace SourceSharp.Shared;

/// <summary>
/// One mod's cap on a yaw amount the client input is about to apply, asked
/// where stock's <c>CTFInput::CAM_CapYaw</c> sits
/// (<c>tf_input_main.cpp:48-65</c>): a side whose mod registers no rule, and
/// a side with no local player, answers its argument unchanged (:51-52, :64);
/// TF2 answers the argument clamped to ±<c>CalculateChargeCap</c> while the
/// local player holds <c>TF_COND_SHIELD_CHARGE</c> (:54-62), so a charging
/// shield turns at the cap's degrees per second instead of the mouse's full
/// delta.
///
/// <para>
/// Asked on the client by <c>ClientInput</c>'s yaw arms — the mouse
/// (<c>ApplyMouse</c>), the keyboard's look keys (<c>AdjustYaw</c>) and the
/// stick (<c>JoyStickAdjustYaw</c>) — which is why the fold, not each arm,
/// owns the clamp: stock's <c>AdjustYaw</c> clamps its two key terms
/// separately (:88-103) and <c>JoyStickAdjustYaw</c> its sign-checked speed
/// (:135-146), each on top of the shared clamp. The pitch arm
/// (<c>CAM_CapPitch</c>) is not here: stock's answers its argument unchanged
/// for every condition (:71-78).
/// </para>
/// </summary>
public interface IChargeYawCap
{
    /// <summary>The yaw amount <paramref name="value"/> (degrees this frame) becomes.</summary>
    float CapYaw(float value);
}
