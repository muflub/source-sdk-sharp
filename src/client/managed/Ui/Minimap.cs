using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>What the minimap asks the game: who else is about, and whether the local player can see them.</summary>
public interface IMinimapWorld
{
    Vector LocalOrigin { get; }

    /// <summary>Which way the local player looks, in world yaw degrees (0 east, anticlockwise).</summary>
    float LocalYaw { get; }

    /// <summary>Fills <paramref name="into"/> with where the other living players this client knows of stand.</summary>
    void OtherPlayers(List<Vector> into);

    /// <summary>Whether a player standing at <paramref name="origin"/> is on the local player's screen, not behind a wall.</summary>
    bool OnScreen(Vector origin);
}

/// <summary><see cref="IMinimapWorld"/> over the client's own entities and the frame's view.</summary>
public sealed class EngineMinimapWorld(IClientStandalones standalones, IEngineHud screen, IEngineEntities entities,
                                       IEngineEntityRender render, IEngineWorldQuery world, IFxRender fx,
                                       IClientBeamExecutor frustum, ViewReadout view) : IMinimapWorld
{
    private const float PlayerHalfWidth = 16f, PlayerHeight = 72f, ChestHeight = 40f, EyeHeight = 64f;

    public Vector LocalOrigin => screen.LocalPlayerOrigin ?? view.Eye;

    public float LocalYaw => view.Yaw;

    public void OtherPlayers(List<Vector> into)
    {
        int local = screen.LocalPlayerIndex;
        foreach (ClientStandaloneEntity entity in standalones.Live())
        {
            if (entity is ClientSsPlayer { State.Alive: true } player
                && entities.GetIndex(player.Handle) != local
                && !render.IsDormant(player.Handle))
                into.Add(entities.GetOrigin(player.Handle));
        }
    }

    public bool OnScreen(Vector origin)
    {
        var mins = new Vector(origin.X - PlayerHalfWidth, origin.Y - PlayerHalfWidth, origin.Z);
        var maxs = new Vector(origin.X + PlayerHalfWidth, origin.Y + PlayerHalfWidth, origin.Z + PlayerHeight);
        frustum.BoxVisibility(in mins, in maxs, out _, out bool culled);
        if (culled)
            return false;

        // By the chest or the eyes: somebody looking over a low wall is in sight.
        Vector eye = fx.MainViewOrigin();
        return Clear(new Vector(origin.X, origin.Y, origin.Z + ChestHeight), eye)
            || Clear(new Vector(origin.X, origin.Y, origin.Z + EyeHeight), eye);
    }

    private bool Clear(Vector from, Vector to) => world.ClientTraceLine(from, to, TraceMask.Brushes).Fraction >= 1f;
}

/// <summary>Finds its world on first asking: the HUD composes before the view does.</summary>
public sealed class LazyMinimapWorld(IServiceLocator services) : IMinimapWorld
{
    private IMinimapWorld World => _world ??= services.Require<IMinimapWorld>();

    private IMinimapWorld? _world;

    public Vector LocalOrigin => World.LocalOrigin;

    public float LocalYaw => World.LocalYaw;

    public void OtherPlayers(List<Vector> into) => World.OtherPlayers(into);

    public bool OnScreen(Vector origin) => World.OnScreen(origin);
}

/// <summary>
/// The minimap's data. The outline is built on first asking and kept for the level, so a game
/// whose HUD never draws the map (TF2 takes hud.minimap) never reads the file. The dots are
/// worked out ten times a second, which is plenty for a mark this size.
/// </summary>
public sealed class Minimap(IEngineFiles files, IGameClock clock, IMinimapWorld world)
    : IMinimapSource, IClientLevelHook, IOrderedHook
{
    /// <summary>Where it stands among the level hooks: last, since it reads nothing of theirs.</summary>
    public const int Order = 9000;

    public int HookOrder => Order;

    /// <summary>How often the dots are worked out.</summary>
    public const float RefreshSeconds = 0.1f;

    private readonly List<Vector> _others = [];
    private readonly List<MinimapDot> _dots = [];
    private string? _map;
    private MapOutline? _outline;
    private bool _built;
    private float _refreshedAt = float.NegativeInfinity;

    public void OnLevelInit(string mapName)
    {
        _map = Path.GetFileNameWithoutExtension(mapName);
        _outline = null;
        _built = false;
        _dots.Clear();
        _refreshedAt = float.NegativeInfinity;
    }

    public void OnLevelShutdown()
    {
        _map = null;
        _outline = null;
        _built = false;
        _dots.Clear();
    }

    private MapOutline? Built
    {
        get
        {
            if (!_built && _map is not null)
            {
                _built = true;
                byte[]? file = files.ReadGameFileBytes($"maps/{_map}.bsp");
                _outline = file is null ? null : MapOutline.Build(file);
            }

            return _outline;
        }
    }

    public float[] Outline => Built?.Segments ?? [];

    public MinimapDot Self => Built is { } outline ? Dot(outline, world.LocalOrigin) : default;

    /// <summary>World yaw runs anticlockwise from east, the mark clockwise from north.</summary>
    public float SelfFacing => (90f - world.LocalYaw) * (MathF.PI / 180f);

    public int DotCount
    {
        get
        {
            Refresh();
            return _dots.Count;
        }
    }

    public MinimapDot Dot(int i) => _dots[i];

    private void Refresh()
    {
        float now = clock.Now;
        if (now >= _refreshedAt && now - _refreshedAt < RefreshSeconds)
            return;

        _refreshedAt = now;
        _dots.Clear();

        if (Built is not { } outline)
            return;

        _others.Clear();
        world.OtherPlayers(_others);
        foreach (Vector origin in _others)
            if (world.OnScreen(origin))
                _dots.Add(Dot(outline, origin));
    }

    /// <summary>Where a world position falls on the square, and how long a length is on it; false while the map has no outline.</summary>
    public bool TryProject(Vector world, float length, out float u, out float v, out float share)
    {
        if (Built is not { } outline)
        {
            u = v = share = 0f;
            return false;
        }

        (u, v) = outline.Project(world);
        share = outline.ProjectLength(length);
        return true;
    }

    private static MinimapDot Dot(MapOutline outline, Vector world)
    {
        (float u, float v) = outline.Project(world);
        return new MinimapDot(u, v);
    }
}
