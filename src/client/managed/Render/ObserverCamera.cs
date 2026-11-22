using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.Render;

/// <summary>
/// A mod's word on where another player looks: stock's
/// <c>C_TFPlayer::EyeAngles</c> answers the networked, interpolated
/// <c>m_angEyeAngles</c> for any player but the local one
/// (c_tf_player.cpp:7281). Without one, the observer's in-eye view takes the
/// drawn body's angles.
/// </summary>
public interface IObserverEyeAngles
{
    /// <summary>The eye angles of the player at <paramref name="playerIndex"/> this frame, or false.</summary>
    bool TryEyeAngles(int playerIndex, out QAngle angles);
}

/// <summary>
/// When the freeze cam's frozen picture began (stock's client-side
/// <c>freezecam_started</c>), or null outside it: what a mod's freeze panel
/// times its screenshot hint from.
/// </summary>
public interface IObserverFreezeFrame
{
    float? FreezeStartedAt { get; }
}

/// <summary>
/// The observer's camera, once a frame at FRAME_RENDER_START after the drawn
/// poses (plan_client_novalve C7 item 5): the local player's networked
/// ObserverMode and ObserverTarget, through <see cref="ObserverView"/>, into the
/// view the client mode's OverrideView draws. With no observer mode it hands
/// the view back, and the player's own eye draws as before.
/// </summary>
public sealed class ObserverCamera(IObserverCameraSlots slots, IClientStandalones standalones, IEngineHud hud,
                                   ViewReadout view, IGameClock clock, ClForceCameraConVar forceCamera,
                                   IRenderPolicyWishes policy, IHooks<IObserverEyeAngles> eyes,
                                   MovementRulesResolver movement, IUniformRandom random, IViewFreezeFrame freezeFrame,
                                   ClSpecFreezeTimeConVar freezeTime, ClSpecFreezeTravelTimeConVar travelTime,
                                   SpecFreezeDistanceMinConVar distanceMin, SpecFreezeDistanceMaxConVar distanceMax)
    : IObserverFreezeFrame
{
    // WALL_OFFSET (c_baseplayer.h): the camera's hull.
    public const float WallOffset = 6.0f;

    private const uint EntryMask = (1u << 16) - 1;

    private readonly ObserverView _view = new();
    private float _lastTime = -1f;
    private float _deathTime;
    private bool _wasAlive = true;
    private bool _published;

    /// <summary>The last view handed to native, or null when the player's own eye draws.</summary>
    public ObserverViewResult? Last { get; private set; }

    /// <summary>Why the last frame's target is what it is, for the dev API.</summary>
    public string TargetNote { get; private set; } = "";

    /// <summary>The last frame's inputs, for the dev API.</summary>
    public ObserverInputs? LastInputs { get; private set; }

    /// <summary>Frames an observer view was drawn.</summary>
    public long Frames { get; private set; }

    /// <summary>
    /// When the freeze cam's flight arrived and froze the picture (stock's
    /// client-side <c>freezecam_started</c>), or null outside it: the freeze
    /// panel's screenshot hint counts from it.
    /// </summary>
    public float? FreezeStartedAt { get; private set; }

    private bool _inFreeze;

    public void Pass()
    {
        float now = clock.Now;
        float frameTime = _lastTime < 0 ? 0f : Math.Max(0f, now - _lastTime);
        _lastTime = now;

        ClientSsPlayer? local = LocalPlayer();
        if (local is null)
        {
            Release();
            return;
        }

        PlayerNetState state = local.State;
        if (_wasAlive && !state.Alive)
            _deathTime = now;
        _wasAlive = state.Alive;

        var mode = (ObserverMode)state.ObserverMode;
        if (mode == ObserverMode.None)
        {
            Release();
            return;
        }

        // The mod's view vectors: the chase cam's height and the freeze cam's aim.
        MovementParams hull = movement.Constants;
        _view.View = hull.View;
        _view.DuckView = hull.DuckView;
        _view.HullHeight = hull.HullMax.Z;

        Vector eye = view.Known ? view.Eye : state.Origin + state.ViewOffset;
        var viewAngles = new QAngle(view.Pitch, view.Yaw, 0f);
        if (!_published)
        {
            // The first observer frame starts from where the player looked.
            _view.LastOrigin = eye;
            _view.LastAngles = viewAngles;
        }

        var input = new ObserverInputs(mode, Target(state.ObserverTarget), eye, viewAngles, viewAngles, state.ViewOffset,
                                       state.PunchAngle, frameTime, now, _deathTime,
                                       forceCamera.Value == 0, null);
        LastInputs = input;
        Freeze(mode == ObserverMode.FreezeCam, eye, now);
        WatchInEye(mode == ObserverMode.InEye && input.Target is { IsPlayer: true } watched ? watched.Index : 0);
        ObserverViewResult? result = _view.Calc(input, new World(slots, hud.LocalPlayerIndex));
        if (result is not { } v)
        {
            Release();
            return;
        }

        // The flight arrived: freeze the picture for spec_freeze_time
        // (CalcFreezeCamView's view->FreezeFrame and freezecam_started).
        if (_inFreeze && _view.SentFreezeFrame && FreezeStartedAt is null)
        {
            FreezeStartedAt = now;
            freezeFrame.ViewFreezeFrame(freezeTime.Value, now);
        }

        slots.SetView(true, v.Origin, v.Angles, v.SetViewAngles);
        _view.LastOrigin = v.Origin;
        _view.LastAngles = v.Angles;
        _published = true;
        Last = v;
        Frames++;
    }

    /// <summary>
    /// The player watched in-eye is in first person (stock's
    /// C_BasePlayer::InFirstPersonView): its body, and what it carries and
    /// wears, are not drawn from inside its eyes. Told to the render policy on
    /// a change, and the carried entities asked again
    /// (<see cref="IFirstPersonGated"/>), since nothing of theirs moved.
    /// </summary>
    private void WatchInEye(int index)
    {
        if (index == policy.InEyeTarget)
            return;

        policy.WatchInEye(index);
        foreach (var entity in standalones.Live())
        {
            if (entity is IFirstPersonGated gated)
                gated.ReconsiderFirstPerson();
        }
    }

    /// <summary>
    /// The freeze cam's edges (C_BasePlayer::PostDataUpdate, c_baseplayer.cpp:885-930):
    /// entering it starts the flight from the view as it is, at a random
    /// distance and height; leaving it ends the frozen picture.
    /// </summary>
    private void Freeze(bool inFreeze, Vector eye, float now)
    {
        if (inFreeze == _inFreeze)
            return;

        _inFreeze = inFreeze;
        if (inFreeze)
        {
            Vector start = _published ? _view.LastOrigin : eye;
            _view.Freeze = new FreezeFrame(start, now, random.RandomFloat(distanceMin.Value, distanceMax.Value),
                                           random.RandomFloat(-30f, 20f), travelTime.Value);
            _view.SentFreezeFrame = false;
            FreezeStartedAt = null;
            return;
        }

        _view.Freeze = null;
        _view.SentFreezeFrame = false;
        FreezeStartedAt = null;
        freezeFrame.ViewFreezeFrame(0f, now);
    }

    private void Release()
    {
        Freeze(false, Vector.Zero, clock.Now);
        WatchInEye(0);
        if (_published)
            slots.SetView(false, Vector.Zero, QAngle.Zero, false);
        _published = false;
        Last = null;
        _view.ChaseDistance = 0f;
    }

    private ClientSsPlayer? LocalPlayer()
    {
        int local = hud.LocalPlayerIndex;
        if (local <= 0)
            return null;
        return standalones.GetByIndex(local) as ClientSsPlayer;
    }

    private ObserverTarget? Target(uint raw)
    {
        if (raw == EntityHandle.None.Value || raw == 0xFFFFFFFFu)
            return null;
        int index = (int)(raw & EntryMask);
        if (slots.RenderPose(index) is not { } pose)
        {
            TargetNote = $"raw 0x{raw:x8} index {index}: no drawn pose (entity {(standalones.GetByIndex(index) is null ? "absent" : "present")})";
            return null;
        }
        TargetNote = $"raw 0x{raw:x8} index {index}";

        if (standalones.GetByIndex(index) is ClientSsPlayer player)
        {
            PlayerNetState s = player.State;
            bool ducking = (s.Flags & DuckingFlag) != 0;
            QAngle eyeAngles = pose.Angles;
            foreach (IObserverEyeAngles source in eyes.All)
            {
                if (source.TryEyeAngles(index, out QAngle networked))
                {
                    eyeAngles = networked;
                    break;
                }
            }

            return new ObserverTarget(index, pose.Origin, eyeAngles, pose.Origin + s.ViewOffset, pose.HasModel,
                                      true, s.Alive, ducking, s.ViewOffset);
        }
        return new ObserverTarget(index, pose.Origin, pose.Angles, pose.Origin, pose.HasModel, false, true, false,
                                  Vector.Zero);
    }

    // FL_DUCKING (const.h).
    private const uint DuckingFlag = 1u << 1;

    private sealed class World(IObserverCameraSlots slots, int localIndex) : IObserverWorld
    {
        public ObserverTrace TraceHull(Vector start, Vector end, ObserverTraceFilter filter, int passIndex)
        {
            var min = new Vector(-WallOffset, -WallOffset, -WallOffset);
            var max = new Vector(WallOffset, WallOffset, WallOffset);
            CameraTrace t = slots.TraceHull(start, end, min, max, TraceMask.Solid,
                                            filter == ObserverTraceFilter.NoPlayersPassing ? TraceFilter.NoNpcsOrPlayer : TraceFilter.Simple,
                                            filter == ObserverTraceFilter.SimplePassingSelf ? localIndex : passIndex);
            return new ObserverTrace(t.Fraction, t.End);
        }
    }
}
