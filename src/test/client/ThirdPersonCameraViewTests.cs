using SourceSharp.Abi;
using SourceSharp.Client.Input;
using SourceSharp.Client.WeaponEffects;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The <c>thirdperson</c>/<c>firstperson</c> chain end to end as the live TF2
/// game answers it (<c>in_camera.cpp</c>'s <c>thirdperson</c> command,
/// <c>cam_thirdperson.cpp</c>'s <c>CAM_ToThirdPerson</c>, and the shell's
/// placement over the <c>ThirdPersonCamera</c> crossing): the command puts the
/// camera behind the viewer at the minimum distance and lets the ideal chase
/// pull it out, the other command hands the eye back, and the behind view
/// draws the viewer's own body and drops the viewmodel to the eye. Proved
/// live on ctf_2fort at main e5b07961c: the third-person shots show the body
/// seen from behind with no viewmodel, the first-person shots return the eye
/// to the viewmodel, and <c>cam_idealdist 200</c> moves the body back with it.
/// </summary>
public class ThirdPersonCameraViewTests
{
    [Fact]
    public void The_thirdperson_command_moves_the_camera_behind_the_player()
    {
        var rig = new InputRig();

        rig.Run("thirdperson");

        Assert.True(rig.Input.CamIsThirdPerson());

        // The decision the viewmodel-hide and body-draw arms both read the same way.
        Assert.True(new InputThirdPersonCamera(rig.Input).IsThirdPerson);

        // The view sits behind the eye from the start: the offset seeds at CAM_MIN_DIST.
        Assert.Equal(30f, rig.Input.ThirdPersonCamera.CameraOffset.Z);

        // And toward the ideal distance the convars carry (cam_idealdist, default 150;
        // the manager's desired offset holds distance in X and up-distances in Z).
        Assert.Equal(150f, rig.Input.ThirdPersonCamera.DesiredCameraOffset.X);
    }

    [Fact]
    public void Cam_ideal_dist_pulls_the_behind_camera_out_to_the_written_distance()
    {
        var rig = new InputRig();
        rig.Host.Engine["sv_cheats"] = "1";
        rig.Run("thirdperson");
        rig.Cvars.CamIdealDist.Write(200f);

        rig.Input.CamThink();

        // One think of the chase: cam_ideallag's default of 4 makes the lag 5,
        // so the distance closes 30 + (200 - 30) / 5 — past the seed, heading
        // for the written distance.
        Assert.Equal(64f, rig.Input.ThirdPersonCamera.CameraOffset.Z);
    }

    [Fact]
    public void The_firstperson_command_returns_the_camera_to_the_eye()
    {
        var rig = new InputRig();
        rig.Run("thirdperson");

        rig.Run("firstperson");

        Assert.False(rig.Input.CamIsThirdPerson());
        Assert.False(new InputThirdPersonCamera(rig.Input).IsThirdPerson);
        Assert.Equal(0f, rig.Input.ThirdPersonCamera.DesiredCameraOffset.Z);
    }

    [Fact]
    public void The_behind_view_draws_the_body_and_the_eye_takes_the_viewmodel_back()
    {
        // The camera behind the player sees them: the body joins the frame.
        Assert.True(ThirdPersonCamera.SeesTheOwner(1f, 150f));

        // A camera collapsed onto the eye is a first-person view whatever the
        // mode says, and the inside of the head is not a picture: the model
        // leaves and the viewmodel returns.
        Assert.False(ThirdPersonCamera.SeesTheOwner(0.05f, 150f));
    }

    [Fact]
    public void A_camera_swept_in_below_the_minimum_gives_the_view_back_to_the_eye()
    {
        // A sweep that clears keeps its fraction of the wish.
        Assert.Equal(0.5f, ThirdPersonCamera.SurvivingFraction(0.5f, 400f));

        // Pulled inside CAM_MIN_DIST, keeping any distance is worse than the
        // eye: the answer is zero and the view goes home.
        Assert.Equal(0f, ThirdPersonCamera.SurvivingFraction(0.05f, 400f));
    }
}
