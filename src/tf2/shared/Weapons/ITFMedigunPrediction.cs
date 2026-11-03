using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The medigun's PREDICTED half: the target pick and the clocks it keeps, run
/// by <see cref="TFMedigunBehaviour"/> beside the effects arm.
///
/// <para>
/// Stock compiles one <c>CWeaponMedigun::ItemPostFrame</c> for both sides, so
/// the client's prediction replays the target search the server's answer came
/// from — <c>FindAndHealTargets</c>' maintain-or-find over the same traces and
/// <c>AllowedToHealTarget</c> checks (shared/tf/tf_weapon_medigun.cpp:1214).
/// Ours splits the frame: the effects are the server's <see cref="ITFWeaponEffects.Heal"/>
/// arm, and this is the state half the client's replay runs on every pass —
/// the first prediction's as much as any re-run's, because a pass that wrote
/// nothing would stand judged against the server's word. It reads and writes
/// the declared states alone: the carrier's beam index and release flag, the
/// weapon's clocks. The server answers it with null and runs the whole frame
/// in its own arm.
/// </para>
/// </summary>
public interface ITFMedigunPrediction
{
    /// <summary>
    /// The medigun's bit in <see cref="WeaponNetState.BehaviourMode"/>: the
    /// attack1 state the press edge reads back, the client-side image of the
    /// server's <c>m_bCanChangeTarget</c> bookkeeping (tf_weapon_medigun.cpp:1555-1573).
    /// The kind's own bits start at the base's <c>ModeKindShift</c> — bits 0
    /// and 1 are the shell's single-shell reload and the held alt-fire.
    /// </summary>
    const int Attack1Down = 1 << 8;

    /// <summary>One command's frame: the pick, over the held attack and the script's primary range.</summary>
    void Frame(in WeaponAttackContext attack, TFWeaponScript script, bool held);
}
