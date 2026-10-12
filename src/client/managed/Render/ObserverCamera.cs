using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.Render;

/// <summary>
/// The observer's camera, once a frame at FRAME_RENDER_START after the drawn
/// poses (plan_client_novalve C7 item 5): the local player's networked
/// ObserverMode and ObserverTarget, through <see cref="ObserverView"/>, into the
/// view the client mode's OverrideView draws. With no observer mode it hands
/// the view back, and the player's own eye draws as before.
/// </summary>
public sealed class ObserverCamera(IObserverCameraSlots slots, IClientStandalones standalones, IEngineHud hud,
                                   ViewReadout view, IGameClock clock, IEngineConsole console)
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
                                       console.FindConVarValue("mp_forcecamera") is null or "0", null);
        LastInputs = input;
        ObserverViewResult? result = _view.Calc(input, new World(slots, hud.LocalPlayerIndex));
        if (result is not { } v)
        {
            Release();
            return;
        }

        slots.SetView(true, v.Origin, v.Angles, v.SetViewAngles);
        _view.LastOrigin = v.Origin;
        _view.LastAngles = v.Angles;
        _published = true;
        Last = v;
        Frames++;
    }

    private void Release()
    {
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
            return new ObserverTarget(index, pose.Origin, pose.Angles, pose.Origin + s.ViewOffset, pose.HasModel,
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
