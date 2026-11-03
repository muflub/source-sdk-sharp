extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared.Anim;

using Xunit;

using Camera = client::SourceSharp.Client.Input.ITauntCamera;
using CameraData = client::SourceSharp.Client.Input.CameraThirdData;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Standalone = client::SourceSharp.Client.Standalone;
using Standalones = client::SourceSharp.Client.IClientStandalones;
using Hook = client::TF2.Client.Player.TFClientTauntCamera;
using CamDist = client::TF2.Client.TFTauntcamDistConVar;
using Cond = client::TF2.Shared.TFCond;

using TF2.Client;

namespace TF2.Tests.Client;

/// <summary>
/// The local player's taunt pair, stock's <c>HandleTaunting</c> arms
/// (<c>c_tf_player.cpp:5677-5739</c>): the camera forced out of the head while
/// the taunt holds (<c>TurnOnTauntCam</c> :5540-5577) and back when it ends,
/// only when the taunt itself moved it (<c>TurnOffTauntCam</c> :5597-5615),
/// with the input's camera-third data set for the duration and handed back
/// null (<c>:5779</c>, <c>TurnOffTauntCam_Finish</c> :5646); and the gesture —
/// one cycle of the held pose onto the gesture's own slot when the condition
/// starts (:4342-4358), the slot reset on the removal (:5729). Only the local
/// player's condition drives either arm; other conditions and other players
/// leave the camera alone.
/// </summary>
public sealed class TFClientTauntCameraTests
{
    /// <summary>The local player: entry index 4, hud's answer below.</summary>
    private static readonly EntityHandle Player = new(0x0002_0004u);

    /// <summary>Somebody else on the same hud: entry index 5.</summary>
    private static readonly EntityHandle Other = new(0x0002_0005u);

    [Fact]
    public void The_locals_taunt_puts_the_camera_in_third_person()
    {
        var rig = Local();

        rig.Hook.OnAdded(Player, Cond.Taunting);

        Assert.Equal(["to-third", "set-data"], rig.Camera.Calls);
        Assert.True(rig.Camera.IsThirdPerson);
    }

    [Fact]
    public void A_second_taunt_add_re_toggles_nothing()
    {
        var rig = Local();

        rig.Hook.OnAdded(Player, Cond.Taunting);
        rig.Hook.OnAdded(Player, Cond.Taunting);

        Assert.Equal(["to-third", "set-data"], rig.Camera.Calls);
    }

    [Fact]
    public void The_taunts_removal_puts_the_camera_back_in_the_head()
    {
        var rig = Local();

        rig.Hook.OnAdded(Player, Cond.Taunting);
        rig.Hook.OnRemoved(Player, Cond.Taunting);

        Assert.Equal(["to-third", "set-data", "to-first", "clear-data"], rig.Camera.Calls);
        Assert.False(rig.Camera.IsThirdPerson);

        // Stock's finish clears the data after the mode (:5646): the null is
        // what hands the chase back to the per-frame sync.
        Assert.True(rig.Camera.Data is null, "the removal handed the camera data back");
    }

    [Fact]
    public void The_data_carries_stocks_fill_and_the_convars_distance()
    {
        // Stock's m_TauntCameraData fill (c_tf_player.cpp:5558-5563): no orbit,
        // lag one, the distance tf_tauntcam_dist answers; stock's convar is
        // declared 150 (:5533), and an operator's value goes straight into the
        // data — the second arm is the non-zero stimulus proving the read.
        var rig = Local(dist: "77");

        rig.Hook.OnAdded(Player, Cond.Taunting);

        CameraData? data = rig.Camera.Data;
        Assert.True(data is not null, "the add set the input's camera data");
        Assert.Equal((0f, 0f, 77f, 1f), (data!.Value.Pitch, data.Value.Yaw, data.Value.Dist, data.Value.Lag));
    }

    [Fact]
    public void The_declared_distance_is_fiftys_when_nothing_was_written()
    {
        var rig = Local();

        rig.Hook.OnAdded(Player, Cond.Taunting);

        CameraData? data = rig.Camera.Data;
        Assert.True(data is not null);
        Assert.Equal(150f, data!.Value.Dist);
    }

    [Fact]
    public void A_removal_with_no_taunt_before_it_leaves_the_camera_alone()
    {
        var rig = Local();

        rig.Hook.OnRemoved(Player, Cond.Taunting);

        Assert.Empty(rig.Camera.Calls);
    }

    [Fact]
    public void Another_players_taunt_moves_no_camera()
    {
        var rig = Local();

        rig.Hook.OnAdded(Other, Cond.Taunting);
        rig.Hook.OnRemoved(Other, Cond.Taunting);

        Assert.Empty(rig.Camera.Calls);
    }

    [Fact]
    public void A_condition_that_is_not_the_taunt_moves_no_camera()
    {
        var rig = Local();

        rig.Hook.OnAdded(Player, Cond.Stealthed);
        rig.Hook.OnRemoved(Player, Cond.Stealthed);

        Assert.Empty(rig.Camera.Calls);
    }

    [Fact]
    public void The_taunt_lands_one_cycle_of_the_held_pose_on_the_gesture_slot()
    {
        // Stock plays the taunt's sequence into the gesture slot at the start
        // (:4356-4357); ours is one cycle of the held pose the weapon table
        // answers for the carried primary (tf_weaponbase.cpp:3662), a fresh
        // push into the Custom slot beside the gait's own answer.
        var rig = Local();

        rig.Hook.OnAdded(Player, Cond.Taunting);

        var push = rig.Engine.Last("EntityRender.SetAnimLayer");
        Assert.True(push is not null, "the gesture reached the render");
        Assert.Equal(GestureSlot.Custom, push!.Value.ArgAs<int>(1));
        Assert.Equal(7, push!.Value.ArgAs<int>(2));     // the model's answer
    }

    [Fact]
    public void The_removal_clears_the_gesture_slot_like_stocks_reset()
    {
        // Stock's removal resets the gesture slot before releasing the camera
        // (:5729, then :5734): the cleared push carries the slot's sequence
        // gone (-1), and it comes after the add's push and before nothing.
        var rig = Local();

        rig.Hook.OnAdded(Player, Cond.Taunting);
        rig.Hook.OnRemoved(Player, Cond.Taunting);

        var clear = rig.Engine.Last("EntityRender.SetAnimLayer");
        Assert.True(clear is not null);
        Assert.Equal(GestureSlot.Custom, clear!.Value.ArgAs<int>(1));
        Assert.Equal(-1, clear!.Value.ArgAs<int>(2));
    }

    [Fact]
    public void A_body_without_a_machine_yet_still_gets_the_camera()
    {
        // AnimStateOrNull is null until the body's first drive: the camera
        // toggle must not wait on the gesture.
        var rig = Local(bodies: new Bodies(player: null));

        rig.Hook.OnAdded(Player, Cond.Taunting);

        Assert.Equal(["to-third", "set-data"], rig.Camera.Calls);
        Assert.Empty(rig.Engine.AllOf("EntityRender.SetAnimLayer"));
    }

    /// <summary>The arm over a hud whose local player is entry index 4.</summary>
    private static Rig Local(Bodies? bodies = null, string dist = "150")
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 4);

        // The drives the body's machine asks on its first Animate(): the
        // model's answers, the clock, and the eyes' own reads — the same
        // stubs the client's animator facts hand a hand-built player
        // (PlayerAnimatorTests.Engine at src/test/client).
        engine.Returns("EntityRender.GetModelName", "models/player/soldier_hm.mdl");
        engine.Returns("EntityRender.LookupSequenceByActivity", 7);
        engine.Returns("EntityRender.SequenceGroundSpeed", 0f);
        engine.Returns("EntityRender.LookupPoseParameter", 2);
        engine.Returns("EntityRender.SetPoseParameter", 0f);
        engine.Returns("EntityRender.GetPoseParameter", 0f);
        engine.Returns("EntityRender.IsDormant", false);
        engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));
        engine.ReturnsEachCall("Clock.Now", () => 5f);

        // A supplied registry owns the seam entirely: with no body in it,
        // nothing else may touch the engine's animation writes either.
        var player = bodies is null ? DrivenBody(engine) : null;
        var camera = new TauntCamera();
        var tauntCamDist = new CamDist();
        tauntCamDist.Bind(-1, "tf_tauntcam_dist", dist);
        var hook = new Hook(camera, engine, bodies ?? new Bodies(player), tauntCamDist);
        return new Rig(hook, camera, engine);
    }

    /// <summary>A body driven once, so its machine exists for the gesture arm.</summary>
    private static Standalone.ClientSsPlayer DrivenBody(Engine engine)
    {
        var player = new Standalone.ClientSsPlayer(engine, clock: engine, screen: engine)
        {
            Handle = new EntityHandle(0x0002_0004u),
        };

        player.BindEngine(engine, engine);
        player.BindIndex(4);
        player.State.BindLocal();
        player.State.MaxSpeed = 320f;
        player.State.Alive = true;
        player.Animate();

        return player;
    }

    private sealed record Rig(Hook Hook, TauntCamera Camera, Engine Engine);

    /// <summary>The registry seam: the body at index 4, or nobody at all.</summary>
    private sealed class Bodies(Standalone.ClientSsPlayer? player) : Standalones
    {
        public Standalone.ClientSsPlayer? For(int index) => index == 4 ? player : null;

        public System.Collections.Generic.IReadOnlyList<Standalone.ClientStandaloneEntity> Live() =>
            player is null ? [] : [player];

        public Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => For(entityIndex);
    }

    /// <summary>The camera seam by hand: the toggles and the data pair, counted in order.</summary>
    private sealed class TauntCamera : Camera
    {
        public List<string> Calls { get; } = [];

        public bool IsThirdPerson { get; private set; }

        public CameraData? Data { get; private set; }

        public void ToThirdPerson()
        {
            Calls.Add("to-third");
            IsThirdPerson = true;
        }

        public void ToFirstPerson()
        {
            Calls.Add("to-first");
            IsThirdPerson = false;
        }

        public void SetCameraThirdData(CameraData data)
        {
            Calls.Add("set-data");
            Data = data;
        }

        public void ClearCameraThirdData()
        {
            Calls.Add("clear-data");
            Data = null;
        }
    }
}
