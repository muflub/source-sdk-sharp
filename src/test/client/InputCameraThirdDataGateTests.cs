using SourceSharp.Client.Input;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The mod's camera-third data against the per-frame camera sync of
/// <c>CAM_Think</c> (<c>in_camera.cpp:238-245</c>): while a mod's
/// <c>CameraThirdData_t</c> stands, the think answers the mod's chase and
/// returns BEFORE <c>g_ThirdPersonManager.Update</c>, so the cheats gate
/// (<c>cam_thirdperson.cpp:82</c>) cannot pull the camera back into the head —
/// the taunt camera of a local player holds through its duration with cheats
/// off and the game refusing third person. Once the data is handed back the
/// gate resumes its forcing. Stock's arms: <c>CAM_SetCameraThirdData</c>
/// (:587), the chase of <c>CAM_CameraThirdThink</c> (:604), and the null the
/// TF player passes on the way out (<c>c_tf_player.cpp:5646</c>).
/// </summary>
public class InputCameraThirdDataGateTests
{
    [Fact]
    public void While_the_taunt_camera_data_stands_the_cheats_gate_leaves_the_camera_out()
    {
        // sv_cheats answers 0 to the fake host (InputFakes), HL2MP's gamerules
        // refuse third person, and no data-armed toggle survives a per-frame
        // sync — but the data puts CAM_Think on the mod's chase first, so the
        // gate line is never reached.
        var rig = new InputRig();
        var camera = new InputTauntCamera(rig.Input);

        camera.ToThirdPerson();
        camera.SetCameraThirdData(new CameraThirdData(0f, 0f, 150f, 1f));
        Assert.True(rig.Input.CamIsThirdPerson());

        // Three thinks of the held taunt: the mode holds, and the distance
        // chases the data's 150 with the lag's half-close per frame — the
        // chase CAM_CameraThirdThink answers (in_camera.cpp:632-639).
        float[] expected = [90f, 120f, 135f];
        for (int frame = 0; frame < expected.Length; frame++)
        {
            rig.Input.CamThink();
            Assert.True(rig.Input.CamIsThirdPerson());
            Assert.Equal(expected[frame], rig.Input.ThirdPersonCamera.CameraOffset.Z);
        }
    }

    [Fact]
    public void Without_the_data_the_cheats_gate_forces_the_camera_into_the_head()
    {
        // The other arm of the same gate, proved the plain way: the same
        // toggle without the mod's data is reverted the very next think, so
        // the hold above really is the data's doing.
        var rig = new InputRig();
        var camera = new InputTauntCamera(rig.Input);

        camera.ToThirdPerson();
        Assert.True(rig.Input.CamIsThirdPerson());

        rig.Input.CamThink();

        Assert.False(rig.Input.CamIsThirdPerson());
    }

    [Fact]
    public void Handing_the_data_back_leaves_the_player_in_first_person()
    {
        // Stock's removal chain: CAM_ToFirstPerson, then CAM_SetCameraThirdData
        // with null (c_tf_player.cpp:5640-5646). The chase is gone, the gate
        // resumes, and the head keeps the camera — the +8 s frame of the live
        // proof.
        var rig = new InputRig();
        var camera = new InputTauntCamera(rig.Input);

        camera.ToThirdPerson();
        camera.SetCameraThirdData(new CameraThirdData(0f, 0f, 150f, 1f));
        rig.Input.CamThink();
        Assert.True(rig.Input.CamIsThirdPerson());

        camera.ToFirstPerson();
        camera.ClearCameraThirdData();
        rig.Input.CamThink();

        Assert.False(rig.Input.CamIsThirdPerson());
    }
}
