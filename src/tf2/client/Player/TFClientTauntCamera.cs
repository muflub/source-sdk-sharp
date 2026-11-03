//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared.Anim;

using TF2.Shared;
using TF2.Shared.Anim;

namespace TF2.Client.Player;

/// <summary>
/// The local player's third-person camera while taunting, the camera half of
/// stock's <c>CTFPlayer::HandleTaunting</c> (<c>c_tf_player.cpp:5677-5739</c>):
/// the taunt condition turning on calls <c>TurnOnTauntCam</c>, which forces the
/// camera out of the head (<c>CAM_ToThirdPerson</c>) after remembering whether
/// the head had it (<c>m_bWasInThirdPerson</c>); the condition turning off calls
/// <c>TurnOffTauntCam</c>, which puts the camera back only when the taunt
/// itself moved it. Only the local player's condition drives it — stock's arm
/// sits behind the same local test, and other players' taunts never move this
/// client's camera.
///
/// <para>
/// The third-data pair is what holds the camera out of the head: stock's
/// <c>HandleTaunting</c> sets the input's camera data every frame while the
/// taunt runs (:5779, the data filled at :5558-5563 — pitch and yaw zero, the
/// distance <c>tf_tauntcam_dist</c>, lag one), and the removal hands it back
/// with a null (<c>TurnOffTauntCam_Finish</c>, :5646). While the data stands,
/// <c>CAM_Think</c> answers the mod's chase and returns before the third-person
/// sync (<c>in_camera.cpp:242-245</c>), so the cheats gate cannot pull the
/// camera back into the head mid taunt — the taunt holds its camera even with
/// cheats off, and the gate resumes its forcing once the data is gone.
/// </para>
///
/// <para>
/// The second arm is the gesture itself: stock plays the taunt's sequence into
/// the gesture slot when the condition starts (:4342-4358) and resets the slot
/// on the removal (:5729). Without stock's scene system the play is one cycle
/// of the held pose the layer answers for the carried primary — the gesture
/// slot's own <c>Custom</c> lane, restarted fresh each taunt. The per-class and
/// per-item taunt anims, the looped pair-taunt scene, and the VCD naming of the
/// sequence wait in <c>docs/plans/tf2/deferred.tsv</c>. Stock's own distance
/// ramp (<c>TauntCamInterpolation</c>, :5745 onward) is not here either: the
/// lag chase of <c>CAM_CameraThirdThink</c> answers the pull-out.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFClientTauntCamera(ITauntCamera camera, IEngineHud hud, IClientStandalones standalones,
                                        TFTauntcamDistConVar tauntCamDist)
    : ITFClientConditionHook, IOrderedHook
{
    /// <summary>
    /// The players whose taunt took the camera out of a first-person head:
    /// stock's <c>m_bWasInThirdPerson</c>, the mode the removal restores.
    /// </summary>
    private readonly HashSet<EntityHandle> _own = [];

    public int HookOrder => TFClientConditionOrders.TauntCamera;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.Taunting || player.EntryIndex != hud.LocalPlayerIndex || !_own.Add(player))
            return;

        camera.ToThirdPerson();

        // The data itself, stock's m_TauntCameraData fill (:5558-5563): no
        // orbit, the distance the convar answers (default 150, :5533), lag
        // one — the input's chase moves the camera out on it.
        camera.SetCameraThirdData(new CameraThirdData(0f, 0f, tauntCamDist.Value, 1f));

        // The gesture, like stock's AddVCDSequenceToGestureSlot of the taunt's
        // sequence into the gesture slot (:4356-4357): one cycle of the held
        // pose, landing on the gesture's own slot so the gait keeps its answer.
        var machine = Machine(player);
        machine?.DoAnimationEvent(PlayerAnimEvent.CustomGesture, activity: TFAnimActivities.StandPrimary);
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond != TFCond.Taunting)
            return;

        if (!_own.Remove(player))
            return;

        // Stock's removal resets the gesture slot before releasing the camera
        // (:5729, then :5734): the pose stops at the door, not on the timer.
        Machine(player)?.ResetGestureSlot(GestureSlot.Custom);
        camera.ToFirstPerson();

        // Stock's finish hands the camera back to the convars with a null
        // after the mode is restored (:5640-5646): the chase ends where the
        // camera already sits, and the per-frame sync resumes its answer.
        camera.ClearCameraThirdData();
    }

    /// <summary>The player's animation machine, once their body has been driven at least one frame.</summary>
    private MultiplayerAnimState? Machine(EntityHandle player) =>
        (standalones.GetByIndex(player.EntryIndex) as ClientSsPlayer)?.AnimStateOrNull;
}
