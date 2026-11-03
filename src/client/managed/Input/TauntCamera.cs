//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

/// <summary>
/// The camera's forced third-person pair and its third-data pair, stock's
/// <c>TurnOnTauntCam</c>/<c>TurnOffTauntCam</c> reads on the mode and
/// <c>HandleTaunting</c>'s data calls (<c>c_tf_player.cpp</c>): the mode while
/// the taunt runs, and the mode it came from; the mod's <c>CameraThirdData_t</c>
/// set for the duration and handed back when it ends. Over
/// <see cref="ClientInput"/>: the toggle is <c>CAM_ToThirdPerson</c> and
/// <c>CAM_ToFirstPerson</c>, the read <c>CAM_IsThirdPerson</c>, the data pair
/// <c>CAM_SetCameraThirdData</c> (<c>in_camera.cpp:587</c>).
/// </summary>
public interface ITauntCamera
{
    /// <summary><c>CAM_IsThirdPerson</c>.</summary>
    bool IsThirdPerson { get; }

    /// <summary><c>CAM_ToThirdPerson</c>.</summary>
    void ToThirdPerson();

    /// <summary><c>CAM_ToFirstPerson</c>.</summary>
    void ToFirstPerson();

    /// <summary>
    /// <c>CAM_SetCameraThirdData</c> with the data: while it stands,
    /// <c>CAM_Think</c> answers the mod's chase instead of the per-frame
    /// third-person sync, so the cheats gate cannot pull the camera back mid
    /// taunt (<c>in_camera.cpp:242-245</c>).
    /// </summary>
    void SetCameraThirdData(CameraThirdData data);

    /// <summary>
    /// <c>CAM_SetCameraThirdData</c> with null, the way stock's
    /// <c>TurnOffTauntCam_Finish</c> hands the camera back (<c>:5646</c>).
    /// </summary>
    void ClearCameraThirdData();
}

/// <summary>
/// <see cref="ITauntCamera"/> over the client's input, whose camera it is.
/// The toggles go through <see cref="ClientInput.ThirdPersonCamera"/>, the
/// same manager the <c>thirdperson</c>/<c>firstperson</c> commands answer on:
/// the manager arm is what sets the override (stock's
/// <c>SetOverridingThirdPerson</c>) that keeps the mode against the per-frame
/// <c>CAM_Think</c> sync, and the bare input methods skip it.
/// </summary>
public sealed class InputTauntCamera(ClientInput input) : ITauntCamera
{
    public bool IsThirdPerson => input.CamIsThirdPerson();

    public void ToThirdPerson() => input.ThirdPersonCamera.CamToThirdPerson();

    public void ToFirstPerson() => input.ThirdPersonCamera.CamToFirstPerson();

    public void SetCameraThirdData(CameraThirdData data)
    {
        // Stock passes the manager's current offset as the pair's angle
        // argument, so the chase starts where the camera already sits
        // (c_tf_player.cpp:5778).
        input.CamSetCameraThirdData(data, CurrentOffset());
    }

    public void ClearCameraThirdData() => input.CamSetCameraThirdData(null, CurrentOffset());

    /// <summary>The manager's camera offset, stock's <c>GetCameraOffsetAngles</c>.</summary>
    private InputAngles CurrentOffset()
    {
        Vector3f offset = input.ThirdPersonCamera.CameraOffset;
        return new InputAngles(offset.X, offset.Y, offset.Z);
    }
}
