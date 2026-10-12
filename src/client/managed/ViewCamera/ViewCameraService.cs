using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.ViewCamera;

/// <summary>What the second camera has done since load.</summary>
/// <param name="Passes">Render-start passes with the camera on and a local view known.</param>
/// <param name="Requested">Views asked of the executor (one in every Nth pass).</param>
/// <param name="Queued">Of those, the ones it took.</param>
/// <param name="Refused">And the ones it refused.</param>
public readonly record struct ViewCameraCounters(long Passes, long Requested, long Queued, long Refused);

/// <summary>
/// The second camera (lane camproto, ahead of plan_client_novalve C7): decides
/// each frame whether a second view is drawn, from where, and how, and queues
/// it for the frame's render. The picture lands in a view render target that a
/// HUD panel shows through <see cref="IViewCameraSource"/>.
/// </summary>
public interface IViewCameraService
{
    /// <summary>Once a frame, at FRAME_RENDER_START: the drawn poses are this frame's.</summary>
    void Pass();

    ViewCameraCounters Counters { get; }
}

/// <summary>
/// The camera's decisions, all of them: the mode (cs_cl_overview), the rate
/// (cs_cl_overview_every), the place and the projection. Native only draws the
/// view it is handed (shell_view_targets.cpp).
/// </summary>
public sealed class ViewCameraService(
    IViewTargets targets, ViewReadout view, IEngineWorldQuery world, IEngineConsole console,
    ViewCameraModeConVar mode, ViewCameraEveryConVar every) : IViewCameraService, IViewCameraSource
{
    /// <summary>The one view target the prototype has.</summary>
    public const int TargetNumber = 0;

    // THIRD PERSON: behind and above the eye, looking down at it along the
    // view's yaw. The wall pulls the camera in, never through it.
    public const int ThirdPersonWidth = 512, ThirdPersonHeight = 288;
    public const float ThirdPersonPitch = 15f, ThirdPersonDistance = 140f, ThirdPersonFov = 75f;
    public const float ThirdPersonWallGap = 8f, ThirdPersonMinDistance = 16f;

    // OVERVIEW: straight down, north up (stock cl_leveloverview's angles),
    // orthographic, centred on the eye. The camera sits in the ceiling over the
    // player, or OverviewHeight up when there is none, and the near plane is
    // put OverviewHeadroom above the eye so the roof above is cut away.
    public const int OverviewWidth = 384, OverviewHeight = 384;
    public const float OverviewHalfExtent = 512f, OverviewRise = 2048f, OverviewHeadroom = 72f, OverviewDepth = 4096f;

    public const float ZNear = 4f, ZFar = 16384f;

    private ViewCameraCounters _counters;
    private long _frame;
    private ViewTargetInfo _target;

    public ViewCameraCounters Counters => _counters;

    /// <summary>
    /// The render plan's monitor queue (RenderPlanHost.QueueMonitor), handed
    /// over once the plan host can be built; false hands the view to native.
    /// </summary>
    public Func<int, ViewTargetSetup, bool>? PlannedQueue { get; set; }

    public void Pass()
    {
        var m = Requested;
        if (m == ViewCameraMode.Off || !view.Known)
            return;

        _counters = _counters with { Passes = _counters.Passes + 1 };
        if (_frame++ % Math.Max(1, every.Value) != 0)
            return;

        var setup = m == ViewCameraMode.ThirdPerson
            ? ThirdPerson(view.Eye, view.Yaw, Fraction)
            : Overview(view.Eye, Fraction);

        // The render plan draws the view as a monitor pass when the frame is
        // the plan's (C7 item 6); native's own queue draws it otherwise.
        bool queued = (PlannedQueue?.Invoke(TargetNumber, setup) ?? false) || targets.Render(TargetNumber, setup);
        _counters = _counters with
        {
            Requested = _counters.Requested + 1,
            Queued = _counters.Queued + (queued ? 1 : 0),
            Refused = _counters.Refused + (queued ? 0 : 1),
        };
    }

    /// <summary>How far along start → end the world's brushes let a line go.</summary>
    private float Fraction(Vector start, Vector end)
    {
        var r = world.ClientTraceLine(start, end, TraceMask.Brushes);
        return r.Hit ? Math.Clamp(r.Fraction, 0f, 1f) : 1f;
    }

    /// <summary>The third-person view of an eye at <paramref name="eye"/> facing <paramref name="yaw"/>.</summary>
    public static ViewTargetSetup ThirdPerson(Vector eye, float yaw, Func<Vector, Vector, float> fraction)
    {
        var angles = new QAngle(ThirdPersonPitch, yaw, 0f);
        Vector forward = Angles.Forward(angles);
        Vector wish = eye - forward * ThirdPersonDistance;
        float f = fraction(eye, wish);
        float reach = f < 1f ? ThirdPersonDistance * f - ThirdPersonWallGap : ThirdPersonDistance;
        float distance = MathF.Max(ThirdPersonMinDistance, reach);
        Vector origin = eye - forward * distance;

        return new ViewTargetSetup
        {
            OriginX = origin.X, OriginY = origin.Y, OriginZ = origin.Z,
            Pitch = angles.Pitch, Yaw = angles.Yaw, Roll = 0f,
            Fov = ThirdPersonFov, ZNear = ZNear, ZFar = ZFar,
            Width = ThirdPersonWidth, Height = ThirdPersonHeight,
            Flags = ViewTargetFlags.DrawViewer,
        };
    }

    /// <summary>The top-down orthographic view centred on <paramref name="eye"/>.</summary>
    public static ViewTargetSetup Overview(Vector eye, Func<Vector, Vector, float> fraction)
    {
        // Into the first brush above the eye: inside a solid the camera's leaf
        // sees every leaf, and the ceiling it sits in faces away from it.
        Vector up = new(eye.X, eye.Y, eye.Z + OverviewRise);
        float rise = OverviewRise * fraction(eye, up);
        if (rise < OverviewRise)
            rise += 2f;
        Vector origin = new(eye.X, eye.Y, eye.Z + rise);
        float near = MathF.Max(ZNear, rise - OverviewHeadroom);
        float half = OverviewHalfExtent;
        float halfY = half * OverviewHeight / OverviewWidth;

        return new ViewTargetSetup
        {
            OriginX = origin.X, OriginY = origin.Y, OriginZ = origin.Z,
            Pitch = 90f, Yaw = 90f, Roll = 0f,
            Fov = 90f, ZNear = near, ZFar = near + OverviewDepth,
            OrthoLeft = -half, OrthoTop = -halfY, OrthoRight = half, OrthoBottom = halfY,
            Width = OverviewWidth, Height = OverviewHeight,
            Flags = ViewTargetFlags.Ortho | ViewTargetFlags.DrawViewer,
        };
    }

    // ---- the panel's data (IViewCameraSource) ----

    /// <summary>Off until the game has made the target the panel would show.</summary>
    public ViewCameraMode Mode
    {
        get
        {
            var m = Requested;
            return m != ViewCameraMode.Off && Target().Width > 0 ? m : ViewCameraMode.Off;
        }
    }

    /// <summary>
    /// cs_cl_overview, or Off without sv_cheats 1: a debug camera (user ruling
    /// 2026-10-11). The engine already refuses the cheat convar's change; this
    /// holds the camera off should the value get set some other way. sv_cheats
    /// is asked only with a mode set, so the default costs no crossing.
    /// </summary>
    public ViewCameraMode Requested
    {
        get
        {
            var m = (ViewCameraMode)Math.Clamp(mode.Value, 0, 2);
            return m != ViewCameraMode.Off && console.FindConVarValue("sv_cheats") is "1" ? m : ViewCameraMode.Off;
        }
    }

    public int Texture => Target().Texture;

    public UiRect Uv
    {
        get
        {
            var t = Target();
            if (t.Width <= 0 || t.Height <= 0)
                return new UiRect(0, 0, 1, 1);
            var (w, h) = Size();
            return new UiRect(0, 0, w / (float)t.Width, h / (float)t.Height);
        }
    }

    public float Aspect
    {
        get
        {
            var (w, h) = Size();
            return w / (float)h;
        }
    }

    private (int W, int H) Size()
        => (ViewCameraMode)Math.Clamp(mode.Value, 0, 2) == ViewCameraMode.Overview
            ? (OverviewWidth, OverviewHeight)
            : (ThirdPersonWidth, ThirdPersonHeight);

    /// <summary>The target's size, asked once it exists (it never changes size).</summary>
    private ViewTargetInfo Target()
    {
        if (_target.Width == 0 && targets.Info(TargetNumber, out var info))
            _target = info;
        return _target;
    }
}
