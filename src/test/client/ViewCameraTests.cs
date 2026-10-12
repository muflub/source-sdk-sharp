using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.ViewCamera;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The second camera (lane camproto): when it asks for a view, how often, and
/// the two placements, against a recording executor.
/// </summary>
public class ViewCameraTests
{
    private static readonly Vector Eye = new(100f, 200f, 64f);

    internal sealed class Targets : IViewTargets
    {
        public readonly List<(int Target, ViewTargetSetup Setup)> Requests = [];
        public bool Accept = true;
        public ViewTargetInfo Target = new(512, 512, 25, 0);
        public bool Exists = true;

        public bool Render(int target, in ViewTargetSetup setup)
        {
            Requests.Add((target, setup));
            return Accept;
        }

        public bool Info(int target, out ViewTargetInfo info)
        {
            info = Exists ? Target : default;
            return Exists;
        }
    }

    private sealed class Backend : IConVarBackend
    {
        public readonly Dictionary<string, int> Ids = new(StringComparer.OrdinalIgnoreCase);

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            int id = Ids.Count;
            Ids[name] = id;
            return id;
        }

        public void Set(int id, string value) { }
        public string Get(int id) => "";
        public string? ValueOf(string name) => null;
    }

    private sealed class Rig
    {
        public readonly Targets Targets = new();
        public readonly ViewReadout View = new();
        public readonly RecordingEngine Engine = new() { Strict = false };
        private readonly Backend _backend = new();
        private readonly ConVarRegistry _convars;
        public readonly ViewCameraService Camera;

        public Rig(bool eyeKnown = true, string? cheats = "1")
        {
            Engine.Returns("Console.FindConVarValue", cheats);
            _convars = new ConVarRegistry(_backend);
            _convars.RegisterTypes([typeof(ViewCameraModeConVar), typeof(ViewCameraEveryConVar)]);
            Camera = new ViewCameraService(Targets, View, Engine, Engine, _convars.Get<ViewCameraModeConVar>()!,
                                           _convars.Get<ViewCameraEveryConVar>()!);
            if (eyeKnown)
                View.PublishEye(Eye);
            View.PublishAngles(0f, 30f);
        }

        public ClientDebugViewCamera Route(SourceSharp.Client.Render.RenderPlanHost? plan = null)
            => new(Camera, Targets, Engine, _convars.Get<ViewCameraModeConVar>()!, _convars.Get<ViewCameraEveryConVar>()!, plan);

        public Rig Set(string name, string value)
        {
            _convars.NotifyChanged(_backend.Ids[name], value, "");
            return this;
        }

        public void Passes(int n)
        {
            for (int i = 0; i < n; i++)
                Camera.Pass();
        }
    }

    /// <summary>No fraction: the world lets every line through.</summary>
    private static float Open(Vector a, Vector b) => 1f;

    [Fact]
    public void Off_by_default_it_asks_for_no_view()
    {
        var rig = new Rig();
        rig.Passes(5);
        Assert.Empty(rig.Targets.Requests);
        Assert.Equal(default, rig.Camera.Counters);
    }

    [Fact]
    public void On_it_asks_for_one_view_a_pass_into_target_zero()
    {
        var rig = new Rig().Set("cs_cl_overview", "1").Set("cs_cl_overview_every", "1");
        rig.Passes(4);
        Assert.Equal(4, rig.Targets.Requests.Count);
        Assert.All(rig.Targets.Requests, r => Assert.Equal(ViewCameraService.TargetNumber, r.Target));
        Assert.Equal(new ViewCameraCounters(4, 4, 4, 0), rig.Camera.Counters);
    }

    [Fact]
    public void Every_third_frame_asks_on_one_pass_in_three()
    {
        var rig = new Rig().Set("cs_cl_overview", "2").Set("cs_cl_overview_every", "3");
        rig.Passes(7);
        Assert.Equal(3, rig.Targets.Requests.Count);
        Assert.Equal(7, rig.Camera.Counters.Passes);
    }

    [Fact]
    public void By_default_it_draws_on_every_other_pass()
    {
        // The second view roughly doubles the client's render crossings
        // (plan C7.6: 18.2 -> 26.2 a frame against the render row's ceiling);
        // one frame in two keeps it under (22.2/22.7).
        var rig = new Rig().Set("cs_cl_overview", "1");
        rig.Passes(6);
        Assert.Equal(3, rig.Targets.Requests.Count);
        Assert.Equal(new ViewCameraCounters(6, 3, 3, 0), rig.Camera.Counters);
    }

    [Theory]
    [InlineData(typeof(ViewCameraModeConVar))]
    [InlineData(typeof(ViewCameraEveryConVar))]
    public void The_cameras_convars_are_cheats(Type convar)
    {
        // A debug camera (user ruling 2026-10-11): the engine refuses a cheat
        // convar's change without sv_cheats 1, and reverts it when cheats go off.
        var attribute = (ConVarAttribute)Attribute.GetCustomAttribute(convar, typeof(ConVarAttribute))!;
        Assert.True(attribute.Flags.HasFlag(ConVarFlags.Cheat), $"{attribute.Name} is not FCVAR_CHEAT");
    }

    [Theory]
    [InlineData("0")]
    [InlineData(null)]
    public void Without_sv_cheats_the_camera_is_off_whatever_the_mode(string? cheats)
    {
        var rig = new Rig(cheats: cheats).Set("cs_cl_overview", "1").Set("cs_cl_overview_every", "1");
        rig.Passes(4);
        Assert.Empty(rig.Targets.Requests);
        Assert.Equal(ViewCameraMode.Off, ((IViewCameraSource)rig.Camera).Mode);
    }

    [Fact]
    public void Off_it_never_asks_whether_cheats_are_on()
    {
        // The default costs no crossing: sv_cheats is read only with a mode set.
        var rig = new Rig();
        rig.Passes(3);
        _ = ((IViewCameraSource)rig.Camera).Mode;
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Console.FindConVarValue");
    }

    [Fact]
    public void Before_a_view_is_known_it_asks_for_none()
    {
        var rig = new Rig(eyeKnown: false).Set("cs_cl_overview", "1");
        rig.Passes(3);
        Assert.Empty(rig.Targets.Requests);
    }

    [Fact]
    public void A_refused_view_is_counted_as_refused()
    {
        var rig = new Rig().Set("cs_cl_overview", "1").Set("cs_cl_overview_every", "1");
        rig.Targets.Accept = false;
        rig.Passes(2);
        Assert.Equal(new ViewCameraCounters(2, 2, 0, 2), rig.Camera.Counters);
    }

    [Fact]
    public void The_mode_picks_the_placement()
    {
        var rig = new Rig().Set("cs_cl_overview", "1").Set("cs_cl_overview_every", "1");
        rig.Passes(1);
        rig.Set("cs_cl_overview", "2");
        rig.Passes(1);
        Assert.Equal(ViewTargetFlags.DrawViewer, rig.Targets.Requests[0].Setup.Flags);
        Assert.Equal(ViewTargetFlags.Ortho | ViewTargetFlags.DrawViewer, rig.Targets.Requests[1].Setup.Flags);
    }

    [Fact]
    public void Third_person_sits_behind_and_above_the_eye_looking_along_its_yaw()
    {
        var s = ViewCameraService.ThirdPerson(Eye, 30f, Open);
        var origin = new Vector(s.OriginX, s.OriginY, s.OriginZ);
        var back = Eye - origin;

        Assert.Equal(ViewCameraService.ThirdPersonDistance, back.Length, 3);
        Assert.True(s.OriginZ > Eye.Z, "above the eye");
        // Behind along the yaw: the eye is ahead of the camera on the yaw's heading.
        Assert.True(back.X * MathF.Cos(30f * MathF.PI / 180f) + back.Y * MathF.Sin(30f * MathF.PI / 180f) > 0f);
        Assert.Equal((ViewCameraService.ThirdPersonPitch, 30f, 0f), (s.Pitch, s.Yaw, s.Roll));
        Assert.Equal((ViewCameraService.ThirdPersonWidth, ViewCameraService.ThirdPersonHeight), (s.Width, s.Height));
        Assert.Equal(ViewCameraService.ThirdPersonFov, s.Fov);
    }

    [Fact]
    public void A_wall_pulls_the_third_person_camera_in_short_of_it()
    {
        var s = ViewCameraService.ThirdPerson(Eye, 30f, (a, b) => 0.5f);
        float distance = (Eye - new Vector(s.OriginX, s.OriginY, s.OriginZ)).Length;
        Assert.Equal(ViewCameraService.ThirdPersonDistance * 0.5f - ViewCameraService.ThirdPersonWallGap, distance, 3);

        var pinned = ViewCameraService.ThirdPerson(Eye, 30f, (a, b) => 0f);
        float least = (Eye - new Vector(pinned.OriginX, pinned.OriginY, pinned.OriginZ)).Length;
        Assert.Equal(ViewCameraService.ThirdPersonMinDistance, least, 3);
    }

    [Fact]
    public void The_overview_is_orthographic_straight_down_and_centred_on_the_eye()
    {
        var s = ViewCameraService.Overview(Eye, Open);
        Assert.Equal((Eye.X, Eye.Y), (s.OriginX, s.OriginY));
        Assert.Equal((90f, 90f), (s.Pitch, s.Yaw));
        Assert.Equal(-s.OrthoLeft, s.OrthoRight);
        Assert.Equal(-s.OrthoTop, s.OrthoBottom);
        Assert.Equal(ViewCameraService.OverviewHalfExtent, s.OrthoRight);
        Assert.Equal((ViewCameraService.OverviewWidth, ViewCameraService.OverviewHeight), (s.Width, s.Height));
    }

    [Fact]
    public void The_overview_sits_in_the_ceiling_and_cuts_the_roof_away_above_the_head()
    {
        // A ceiling 10% of the rise up: the camera goes two units into it.
        var s = ViewCameraService.Overview(Eye, (a, b) => 0.1f);
        float rise = ViewCameraService.OverviewRise * 0.1f + 2f;
        Assert.Equal(Eye.Z + rise, s.OriginZ, 3);
        Assert.Equal(rise - ViewCameraService.OverviewHeadroom, s.ZNear, 3);
        Assert.Equal(s.ZNear + ViewCameraService.OverviewDepth, s.ZFar, 3);

        // No ceiling: the full rise, and the cut is still just above the head.
        var open = ViewCameraService.Overview(Eye, Open);
        Assert.Equal(Eye.Z + ViewCameraService.OverviewRise, open.OriginZ, 3);
        Assert.Equal(open.OriginZ - open.ZNear, Eye.Z + ViewCameraService.OverviewHeadroom, 3);
    }

    [Fact]
    public void The_panel_sees_the_mode_only_once_the_game_has_made_the_target()
    {
        var rig = new Rig().Set("cs_cl_overview", "1");
        rig.Targets.Exists = false;
        IViewCameraSource source = rig.Camera;
        Assert.Equal(ViewCameraMode.Off, source.Mode);
        Assert.Equal(0, source.Texture);

        rig.Targets.Exists = true;
        Assert.Equal(ViewCameraMode.ThirdPerson, source.Mode);
        // The picture is the target's vgui texture, which ui.Texture draws.
        Assert.Equal(25, source.Texture);
        Assert.Equal(new SourceSharp.Client.Ui.UiRect(0, 0, 1f, 288f / 512f), source.Uv);
        Assert.Equal(512f / 288f, source.Aspect, 4);

        rig.Set("cs_cl_overview", "2");
        Assert.Equal(ViewCameraMode.Overview, source.Mode);
        Assert.Equal(new SourceSharp.Client.Ui.UiRect(0, 0, 384f / 512f, 384f / 512f), source.Uv);
    }

    // ---- /client/{slot}/viewcam (user ruling 2026-10-11: tooling drives the debug camera) ----

    private static string Ask(Rig rig, string argument = "")
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(viewCamera: rig.Route()));
        return client.Answer(Debug.Api.DebugChannelQuery.ViewCam, argument);
    }

    [Fact]
    public void The_viewcam_answer_renders_exactly()
    {
        var rig = new Rig().Set("cs_cl_overview", "1").Set("cs_cl_overview_every", "1");
        rig.Targets.Target = new ViewTargetInfo(512, 512, 25, 4);
        rig.Passes(4);
        Assert.Equal(DebugApiClientGoldenTests.Golden("viewcam"), Ask(rig));
    }

    // The plan's monitor passes come in the same snapshot as native's drawn
    // count, so the live step compares them over one window (C7 item 6).
    [Fact]
    public void The_viewcam_answer_carries_the_plans_monitor_passes()
    {
        var plan = new SourceSharp.Client.Render.RenderPlanHost();
        var setup = new ViewTargetSetup { Width = 64, Height = 64, Fov = 90, ZNear = 4, ZFar = 100 };
        Assert.True(plan.QueueMonitor(0, setup));
        plan.BuildPlan(RenderPlannerTests.Inputs(), new RenderOp[512]);
        var rig = new Rig().Set("cs_cl_overview", "1");
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(viewCamera: rig.Route(plan)));
        Assert.Contains("\"monitorPasses\":1", client.Answer(Debug.Api.DebugChannelQuery.ViewCam), StringComparison.Ordinal);
    }

    [Fact]
    public void A_viewcam_write_without_sv_cheats_is_refused_and_sets_nothing()
    {
        var rig = new Rig(cheats: "0");
        string answer = Ask(rig, "1");
        Assert.Contains("\"write\":\"refused: needs sv_cheats 1\"", answer, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Console.SetConVarValueByName");
    }

    [Fact]
    public void A_viewcam_write_with_sv_cheats_sets_cs_cl_overview()
    {
        var rig = new Rig();
        string answer = Ask(rig, "2");
        Assert.Contains("\"write\":\"set\"", answer, StringComparison.Ordinal);
        var call = Assert.Single(rig.Engine.Calls, c => c.Member == "Console.SetConVarValueByName");
        Assert.Equal(["cs_cl_overview", "2"], call.Args);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("on")]
    public void A_viewcam_write_outside_0_to_2_is_refused(string mode)
    {
        var rig = new Rig();
        Assert.Contains("\"write\":\"refused: mode is 0, 1 or 2\"", Ask(rig, mode), StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Console.SetConVarValueByName");
    }
}
