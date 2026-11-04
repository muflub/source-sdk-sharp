using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>What the markers ask of the game: where the player stands, and where a place falls on the screen.</summary>
public interface IMarkerView
{
    Vector LocalOrigin { get; }

    /// <summary>A world place as fractions of the screen; false when it is behind the view, the fractions then the way to turn.</summary>
    bool ToScreen(Vector world, out float x, out float y);
}

/// <summary><see cref="IMarkerView"/> over the engine's own projection and the frame's view.</summary>
public sealed class EngineMarkerView(IEngineHud screen, IClientBeamExecutor project, IFxRender fx, ViewReadout view, Input.IClientInputEngine size) : IMarkerView
{
    public Vector LocalOrigin => screen.LocalPlayerOrigin ?? view.Eye;

    public bool ToScreen(Vector world, out float x, out float y)
    {
        Vector on = project.ScreenPosition(world);
        size.GetScreenSize(out int width, out int height);
        x = width > 0 ? on.X / width : 0.5f;
        y = height > 0 ? on.Y / height : 0.5f;
        FxView at = fx.View();
        float ahead = (world.X - at.Origin.X) * at.Forward.X + (world.Y - at.Origin.Y) * at.Forward.Y + (world.Z - at.Origin.Z) * at.Forward.Z;
        return ahead > 0f;
    }
}

/// <summary>Finds its view on first asking: the HUD composes before the view does.</summary>
public sealed class LazyMarkerView(IServiceLocator services) : IMarkerView
{
    private IMarkerView? _view;

    private IMarkerView View => _view ??= services.Require<IMarkerView>();

    public Vector LocalOrigin => View.LocalOrigin;

    public bool ToScreen(Vector world, out float x, out float y) => View.ToScreen(world, out x, out y);
}

/// <summary>
/// The markers a server has sent this player (<see cref="PlayerMarker"/>; the server sends them to
/// one slot, so a spectator's or another player's screen never has them). Each lives its seconds and
/// goes. One place shows once: the same place named again starts over rather than doubling up. A
/// few at most, the oldest giving way. A place shows on the minimap and in the world, a rough
/// area on the minimap alone.
/// </summary>
public sealed class PlayerMarkerBoard(IGameClock clock, Minimap map, IMarkerView view)
    : IPlayerMarkerHook, IMinimapPingSource, IWorldMarkerSource, IClientLevelHook, IOrderedHook
{
    /// <summary>How many show at once.</summary>
    public const int MaxMarkers = 4;

    /// <summary>A place's marker keeps this far inside the screen's edge.</summary>
    public const float EdgeInset = 0.04f;

    /// <summary>World units in a metre, the game's player scale.</summary>
    public const float UnitsPerMetre = 52.5f;

    /// <summary>Two markers this close, of one name, are the same place.</summary>
    public const float SamePlace = 1f;

    private readonly PlayerMarker[] _markers = new PlayerMarker[MaxMarkers];
    private readonly float[] _at = new float[MaxMarkers];
    private readonly WorldMarker[] _world = new WorldMarker[MaxMarkers];
    private readonly MinimapPing[] _pings = new MinimapPing[MaxMarkers];
    private int _count;
    private int _worldCount;
    private int _pingCount;
    private float _builtAt = float.NaN;

    public int HookOrder => Minimap.Order + 1;

    public void OnLevelInit(string mapName) => Clear();

    public void OnLevelShutdown() => Clear();

    private void Clear()
    {
        _count = 0;
        _worldCount = 0;
        _pingCount = 0;
        _builtAt = float.NaN;
    }

    public void OnPlayerMarker(PlayerMarker marker)
    {
        if (marker.Seconds <= 0f)
            return;
        Expire();
        for (int i = 0; i < _count; i++)
        {
            if (_markers[i].Label == marker.Label && Close(_markers[i].Position, marker.Position))
            {
                _markers[i] = marker;
                _at[i] = clock.Now;
                _builtAt = float.NaN;
                return;
            }
        }

        if (_count == MaxMarkers)
            RemoveAt(0);
        _markers[_count] = marker;
        _at[_count++] = clock.Now;
        _builtAt = float.NaN;
    }

    private static bool Close(Vector a, Vector b)
        => MathF.Abs(a.X - b.X) < SamePlace && MathF.Abs(a.Y - b.Y) < SamePlace && MathF.Abs(a.Z - b.Z) < SamePlace;

    private void RemoveAt(int i)
    {
        Array.Copy(_markers, i + 1, _markers, i, _count - i - 1);
        Array.Copy(_at, i + 1, _at, i, _count - i - 1);
        _count--;
    }

    private void Expire()
    {
        float now = clock.Now;
        for (int i = _count - 1; i >= 0; i--)
        {
            // A level change takes the clock back, and a marker from before it goes too.
            if (now - _at[i] >= _markers[i].Seconds || now < _at[i])
                RemoveAt(i);
        }
    }

    /// <summary>The frame's view of them, worked out once a clock tick, when the HUD first asks.</summary>
    private void Build()
    {
        float now = clock.Now;
        if (now == _builtAt)
            return;
        _builtAt = now;
        Expire();
        _worldCount = 0;
        _pingCount = 0;
        for (int i = 0; i < _count; i++)
        {
            ref readonly PlayerMarker m = ref _markers[i];
            float age = (now - _at[i]) / m.Seconds;
            bool area = m.Kind == (byte)PlayerMarkerKind.Area;
            if (map.TryProject(m.Position, m.Radius, out float u, out float v, out float share))
                _pings[_pingCount++] = new MinimapPing(u, v, area ? share : 0f, age);
            if (!area)
                _world[_worldCount++] = World(m, age);
        }
    }

    private WorldMarker World(in PlayerMarker m, float age)
    {
        bool ahead = view.ToScreen(m.Position, out float x, out float y);
        if (!ahead)
        {
            // Behind the view the projection mirrors and its height means nothing (seen live: over
            // the minimap): at the middle of the edge on the side to turn to.
            x = x < 0.5f ? 2f : -1f;
            y = 0.5f;
        }

        Vector o = view.LocalOrigin;
        float dx = m.Position.X - o.X, dy = m.Position.Y - o.Y, dz = m.Position.Z - o.Z;
        int metres = (int)MathF.Round(MathF.Sqrt(dx * dx + dy * dy + dz * dz) / UnitsPerMetre);
        return new WorldMarker(Math.Clamp(x, EdgeInset, 1f - EdgeInset), Math.Clamp(y, EdgeInset, 1f - EdgeInset), m.Label, metres, age);
    }

    public int PingCount
    {
        get
        {
            Build();
            return _pingCount;
        }
    }

    public MinimapPing Ping(int i) => _pings[i];

    public int MarkerCount
    {
        get
        {
            Build();
            return _worldCount;
        }
    }

    public WorldMarker Marker(int i) => _world[i];
}
