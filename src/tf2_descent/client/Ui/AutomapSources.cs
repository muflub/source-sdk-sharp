using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Descent.Client.Automap;
using TF2.Descent.Client.Party;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// THE AUTOMAP'S SOURCES OVER WHAT THE SERVER REVEALED (plan_descent_logic UI5).
// The rooms are the client's cache of the server's automap messages and
// nothing more; the hero's place and facing are the local player's; the party
// marks are the other heroes standing in a revealed room. The canvas draws on
// a 1024 grid: the source fits the revealed rooms into it (a square around
// them, never smaller than MinExtentUnits, padded), so the map zooms out as
// more is revealed; tf2_descent_automap_zoom above 1 zooms in on the hero.
//=============================================================================

/// <summary>Room labels start on (the automap screen's toggle starts from it); a client's own, kept in its config.</summary>
[ConVar("tf2_descent_automap_labels", "1", Flags = ConVarFlags.Archive, Help = "Room names on the TF2 Descent automap when it opens: 1 on, 0 off")]
public sealed class AutomapLabelsConVar : BoolConVar { }

/// <summary>The automap's zoom: 1 fits every revealed room; more zooms in on the hero (to 4).</summary>
[ConVar("tf2_descent_automap_zoom", "1", Flags = ConVarFlags.Archive, Help = "TF2 Descent automap zoom: 1 fits the revealed rooms, up to 4 zooms in on the hero")]
public sealed class AutomapZoomConVar : FloatConVar { }

/// <summary>What the automap asks of the game: where the local hero stands and faces, and where the other heroes are.</summary>
public interface IAutomapWorld
{
    /// <summary>The local player's origin, or null before there is one.</summary>
    Vector? LocalOrigin { get; }

    /// <summary>Which way the local player looks, world yaw degrees (0 east, anticlockwise).</summary>
    float LocalYaw { get; }

    /// <summary>Fills <paramref name="into"/> (cleared first) with where every other member of the local hero's party stands (WO9).</summary>
    void PartyPositions(List<Vector> into);
}

/// <summary><see cref="IAutomapWorld"/> over the HUD's local player, the minimap's view yaw and the client's hero entities.</summary>
[SideService(Side.Client, As = [typeof(IAutomapWorld)])]
public sealed class EngineAutomapWorld(IEngineHud hud, IMinimapWorld view, IHeroStates heroes, IClientStandalones standalones,
                                       IEngineEntities entities, IPartyState party) : IAutomapWorld
{
    public Vector? LocalOrigin => hud.LocalPlayerOrigin;

    public float LocalYaw => view.LocalYaw;

    public void PartyPositions(List<Vector> into)
    {
        into.Clear();
        int local = hud.LocalPlayerIndex;
        foreach (var hero in heroes.All())
        {
            if (hero.PlayerIndex == local || !party.IsMember(hero.PlayerIndex) || standalones.GetByIndex(hero.PlayerIndex) is not { } player)
                continue;
            into.Add(entities.GetOrigin(player.Handle));
        }
    }
}

/// <summary>
/// The automap and the HUD's corner minimap (<see cref="IDescentAutomap"/>) over the revealed rooms
/// (<see cref="IAutomapRooms"/>). Laid out once a client frame (<see cref="IGameClock"/>): the fit,
/// each room's rectangle, the door stubs (a door both its rooms carry drawn once), the server's marks,
/// the party marks, and the hero's place. Storeys are 1-based (the map screen's chips); the hero's is
/// the storey of the revealed room he stands in.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentAutomap)])]
public sealed class AutomapSource(IAutomapRooms rooms, IAutomapWorld world, IGameClock clock, AutomapZoomConVar zoom) : IDescentAutomap
{
    /// <summary>The canvas's grid (AutomapCanvas): a source's coordinates run 0 to this.</summary>
    public const float Grid = 1024f;

    /// <summary>The fit is never smaller than four cells, so one revealed room does not fill the map.</summary>
    public const float MinExtentUnits = 4096f;

    /// <summary>Map units kept round the revealed rooms.</summary>
    public const float PadUnits = 512f;

    /// <summary>A door stub's thickness across its wall, map units.</summary>
    public const float StubUnits = 32f;

    private float[] _rx = [], _ry = [], _rw = [], _rh = [];
    private int[] _storey = [];
    private readonly List<(float X, float Y, float W, float H)> _stubs = new();
    private readonly List<DescentMarker> _marks = new();
    private readonly List<Vector> _party = new();
    private readonly HashSet<(int, int)> _seenDoors = new();
    private int _version = -1;
    private float _builtAt = float.NaN;
    private float _left, _top, _extent = MinExtentUnits;
    private float _heroX, _heroY, _heroHeading;
    private int _heroStorey = 1, _storeys;
    private float _zoom = 1f;

    /// <summary>The frame's layout, worked out once a clock tick when first asked.</summary>
    private void Build()
    {
        float now = clock.Now;
        if (now == _builtAt && _version == rooms.Version)
            return;
        _builtAt = now;
        var list = rooms.Rooms;
        var at = world.LocalOrigin;
        float z = Math.Clamp(float.IsFinite(zoom.Value) ? zoom.Value : 1f, 1f, 4f);
        bool relaid = _version != rooms.Version || z != _zoom || z > 1f;
        _version = rooms.Version;
        _zoom = z;

        // The hero's storey: the revealed room his feet are in, else the one he had.
        _storeys = 0;
        foreach (var r in list)
        {
            _storeys = Math.Max(_storeys, r.Storey + 1);
            if (at is { } p && p.X >= r.MinX && p.X <= r.MaxX && p.Y >= r.MinY && p.Y <= r.MaxY)
                _heroStorey = r.Storey + 1;
        }

        if (relaid)
            Fit(list, at);

        if (at is { } h)
        {
            _heroX = GridX(h.X);
            _heroY = GridY(h.Y);
        }
        _heroHeading = 90f - world.LocalYaw;

        if (relaid)
            Lay(list);
        Marks(list);
    }

    /// <summary>The square of map units the grid shows: round every revealed room, or round the hero when zoomed in.</summary>
    private void Fit(IReadOnlyList<AutomapRoomView> list, Vector? hero)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var r in list)
        {
            minX = Math.Min(minX, r.MinX);
            minY = Math.Min(minY, r.MinY);
            maxX = Math.Max(maxX, r.MaxX);
            maxY = Math.Max(maxY, r.MaxY);
        }
        if (list.Count == 0)
        {
            minX = minY = -MinExtentUnits / 2;
            maxX = maxY = MinExtentUnits / 2;
        }
        float extent = Math.Max(Math.Max(maxX - minX, maxY - minY) + 2 * PadUnits, MinExtentUnits);
        float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        if (_zoom > 1f)
        {
            extent /= _zoom;
            if (hero is { } h)
            {
                cx = h.X;
                cy = h.Y;
            }
        }
        _extent = extent;
        _left = cx - extent / 2;
        _top = cy + extent / 2;
    }

    private float GridX(float x) => (x - _left) / _extent * Grid;

    private float GridY(float y) => (_top - y) / _extent * Grid;

    private float GridLength(float units) => units / _extent * Grid;

    private void Lay(IReadOnlyList<AutomapRoomView> list)
    {
        if (_rx.Length != list.Count)
        {
            _rx = new float[list.Count];
            _ry = new float[list.Count];
            _rw = new float[list.Count];
            _rh = new float[list.Count];
            _storey = new int[list.Count];
        }
        _stubs.Clear();
        _seenDoors.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            _rx[i] = GridX(r.MinX);
            _ry[i] = GridY(r.MaxY);
            _rw[i] = GridLength(r.MaxX - r.MinX);
            _rh[i] = GridLength(r.MaxY - r.MinY);
            _storey[i] = r.Storey + 1;
            foreach (var d in r.Doors)
            {
                if (!_seenDoors.Add((d.X, d.Y)))
                    continue;
                float along = GridLength(d.Width), across = GridLength(StubUnits);
                _stubs.Add((GridX(d.X), GridY(d.Y), d.AlongY ? across : along, d.AlongY ? along : across));
            }
        }
    }

    private void Marks(IReadOnlyList<AutomapRoomView> list)
    {
        _marks.Clear();
        for (int i = 0; i < list.Count; i++)
            foreach (var m in list[i].Marks)
                _marks.Add(new DescentMarker(KindOf(m.Kind), i, GridX(m.X), GridY(m.Y), list[i].Storey + 1));

        // The party: another hero standing in a revealed room.
        world.PartyPositions(_party);
        foreach (var p in _party)
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (p.X < r.MinX || p.X > r.MaxX || p.Y < r.MinY || p.Y > r.MaxY)
                    continue;
                _marks.Add(new DescentMarker(DescentMapMarker.Party, i, GridX(p.X), GridY(p.Y), r.Storey + 1));
                break;
            }
    }

    /// <summary>The canvas's marker kind for a server's mark.</summary>
    public static DescentMapMarker KindOf(AutomapMarkKind kind) => kind switch
    {
        AutomapMarkKind.Stairs => DescentMapMarker.Stairs,
        AutomapMarkKind.ShrineUnused => DescentMapMarker.ShrineUnused,
        AutomapMarkKind.ShrineUsed => DescentMapMarker.ShrineUsed,
        AutomapMarkKind.ContainerLocked => DescentMapMarker.ContainerLocked,
        AutomapMarkKind.Portal => DescentMapMarker.Portal,
        AutomapMarkKind.Corpse => DescentMapMarker.Corpse,
        AutomapMarkKind.Boss => DescentMapMarker.Boss,
        _ => DescentMapMarker.Stairs,
    };

    public int RoomCount
    {
        get
        {
            Build();
            return _rx.Length;
        }
    }

    public float RoomX(int room)
    {
        Build();
        return _rx[room];
    }

    public float RoomY(int room)
    {
        Build();
        return _ry[room];
    }

    public float RoomW(int room)
    {
        Build();
        return _rw[room];
    }

    public float RoomH(int room)
    {
        Build();
        return _rh[room];
    }

    public int RoomStorey(int room)
    {
        Build();
        return _storey[room];
    }

    public bool RoomCurrentStorey(int room)
    {
        Build();
        return _storey[room] == _heroStorey;
    }

    public int DoorStubCount
    {
        get
        {
            Build();
            return _stubs.Count;
        }
    }

    public float DoorStubX(int i) => _stubs[i].X;

    public float DoorStubY(int i) => _stubs[i].Y;

    public float DoorStubW(int i) => _stubs[i].W;

    public float DoorStubH(int i) => _stubs[i].H;

    public int MarkerCount
    {
        get
        {
            Build();
            return _marks.Count;
        }
    }

    public DescentMarker Marker(int i)
    {
        Build();
        return _marks[i];
    }

    public int StoreyCount
    {
        get
        {
            Build();
            return _storeys;
        }
    }

    public int HeroStorey
    {
        get
        {
            Build();
            return _heroStorey;
        }
    }

    public float HeroX
    {
        get
        {
            Build();
            return _heroX;
        }
    }

    public float HeroY
    {
        get
        {
            Build();
            return _heroY;
        }
    }

    public float HeroHeadingDeg
    {
        get
        {
            Build();
            return _heroHeading;
        }
    }

    public int FocusStorey => HeroStorey;
}

/// <summary>The automap's viewer prefs (<see cref="IAutomapPrefs"/>): room labels start as <c>tf2_descent_automap_labels</c> says.</summary>
[SideService(Side.Client, As = [typeof(IAutomapPrefs)])]
public sealed class AutomapPrefsSource(AutomapLabelsConVar labels) : IAutomapPrefs
{
    public bool ShowLabelsByDefault => labels.Value;
}

/// <summary>Room names on the automap (<see cref="IAutomapRoomLabels"/>): each revealed room's map label as the server sent it.</summary>
[SideService(Side.Client, As = [typeof(IAutomapRoomLabels)])]
public sealed class AutomapRoomLabelsSource(IAutomapRooms rooms) : IAutomapRoomLabels
{
    public string RoomLabel(int room) => room >= 0 && room < rooms.Rooms.Count ? rooms.Rooms[room].Label : "";
}

/// <summary><c>tf2_descent_automap_cl</c>: the rooms this client holds, as the server revealed them (UI5's live proof).</summary>
[ConCommand("tf2_descent_automap_cl", Help = "tf2_descent_automap_cl: the automap rooms this client holds")]
public sealed class AutomapClientCommand(IAutomapRooms rooms, IDescentAutomap map, ILog log)
{
    public void Execute()
    {
        var list = rooms.Rooms;
        var shown = string.Join(", ", list.Select(r => r.Label.Length > 0 ? $"{r.Room} ({r.Label})" : r.Room.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        log.Msg(FormattableString.Invariant(
            $"[TF2Descent/Automap] client holds {list.Count} rooms: {shown}; {map.DoorStubCount} door stubs, {map.MarkerCount} marks, hero at ({map.HeroX:0}, {map.HeroY:0}) storey {map.HeroStorey} of {map.StoreyCount}\n"));
    }
}
