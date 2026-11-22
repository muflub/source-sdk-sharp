using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules.Automap;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Party;
using TF2.Descent.Shared;

using AutomapModel = TF2.Descent.Rules.Automap.Automap;
using AutomapRoomWire = TF2.Descent.Shared.AutomapRoom;
using RulesMarkerKind = TF2.Descent.Rules.Automap.LevelMarkerKind;

namespace TF2.Descent.Server.Automap;

/// <summary>
/// What reveals a hero's automap beside walking into a room (plan_descent_logic UI5): the
/// furniture's (WO3) shrine of sight reveals the whole level, and a shrine used shows used on
/// every hero's map. Nothing calls either yet: the furniture package does when it lands.
/// </summary>
public interface IAutomapReveal
{
    /// <summary>Every room of the level, revealed to the hero in <paramref name="playerIndex"/> and sent to him.</summary>
    void RevealAll(int playerIndex);

    /// <summary>The shrine on marker <paramref name="markerKey"/> (<see cref="LevelAdapters.MarkerKey"/>) was used.</summary>
    void ShrineUsed(string markerKey);
}

/// <summary>
/// THE AUTOMAP, REVEALED ROOM BY ROOM BY THE SERVER (plan_descent_logic UI5, mine plan E2;
/// plan_tf2_rpg 2m, R23). Each hero has a map of the level, and a party shares one (WO9): a room a
/// member reveals is revealed to every member here, a hero who joins a party is given the party's map
/// (and the party his), and one who leaves keeps every room he was shown. The map is
/// the rules' <see cref="AutomapRules"/> over the level's room graph
/// (<see cref="LevelAdapters.ToAutomap"/>). A room is revealed when the hero stands in it
/// (<see cref="LevelData.RoomAt"/>, looked at every <see cref="PollFrames"/> frames), and only
/// then does its outline reach his client (<see cref="AutomapRoomWire"/>): the bounds of the cells it
/// covers in map units, its storey and map label, a stub for each door it has, and the fixed
/// markers the rules' <see cref="AutomapRules.View"/> shows in it (the way down and the way in as
/// stairs, shrines). A room through a door of a revealed one is discovered
/// (<see cref="AutomapRules.Discovered"/>, user 2026-11-15) and goes as its outline, storey and
/// doors alone, flagged <see cref="AutomapRoomWire.Discovered"/>: no label, no marks; any other
/// unentered room is never sent. At each spawn the hero's whole map goes
/// again behind an <see cref="TF2.Descent.Shared.AutomapSync"/> (the client forgets its map when a level ends); a
/// level change starts every map empty.
/// </summary>
[SideService(Side.Server, As = [typeof(IAutomapReveal)])]
public sealed class AutomapService(ILevelData levels, Heroes heroes, HeroSheets sheets, IEngineEntities entities, IUserMessageSender sender,
                                   IPartyRoster roster, IParties parties, IPartySlots slots)
    : IAutomapReveal, IFrameLifecycle, IPlayerSpawnHook, IGameLifecycle, IPartyHook, IOrderedHook
{
    /// <summary>TF2 Descent's automap block (UI5), after the robot views (1200).</summary>
    public const int Order = ModStages.First + 1210;

    /// <summary>How often each hero's room is looked at.</summary>
    public const int PollFrames = 4;

    public int HookOrder => Order;

    public int FrameOrder => Order;

    public int LifecycleOrder => Order;

    private readonly Dictionary<string, AutomapModel> _maps = new(StringComparer.Ordinal);
    private LevelData? _level;
    private AutomapLevel? _graph;
    private int _frame;

    /// <summary>Rooms sent since the server started, for the dev print and the facts.</summary>
    public long RoomsSent { get; private set; }

    /// <summary>The room graph of the current level; null on the town and between levels.</summary>
    public AutomapLevel? Graph => Refresh() ? _graph : null;

    /// <summary>The hero in <paramref name="playerIndex"/>'s map; empty when he has none.</summary>
    public AutomapModel MapOf(int playerIndex) => _maps.GetValueOrDefault(KeyOf(playerIndex)) ?? AutomapModel.Empty;

    public void OnPostThink(float frameTime)
    {
        if (++_frame % PollFrames != 0)
            return;
        Poll();
    }

    /// <summary>One look at where every hero stands: a room he enters for the first time is revealed and sent to him.</summary>
    public void Poll()
    {
        if (!Refresh())
            return;
        foreach (var hero in heroes.All)
        {
            if (hero.PlayerIndex <= 0 || hero.Player == EntityHandle.None)
                continue;
            int room = _level!.RoomAt(entities.GetOrigin(hero.Player));
            if (room < 0)
                continue;
            Reveal(hero.PlayerIndex, LevelAdapters.RoomKey(room));
        }
    }

    /// <summary>
    /// Reveals <paramref name="roomKey"/> to the hero in <paramref name="playerIndex"/> and to every
    /// member of his party on this server (WO9: the party shares one map), each sent it as his own
    /// reveal; false when the hero had it.
    /// </summary>
    public bool Reveal(int playerIndex, string roomKey)
    {
        if (!Refresh() || !_graph!.HasRoom(roomKey))
            return false;
        bool his = false;
        foreach (int mate in roster.MatesOf(playerIndex))
        {
            bool added = Enter(mate, roomKey);
            if (mate == playerIndex)
                his = added;
        }
        return his;
    }

    /// <summary>The room on one hero's map, sent to him; false when he had it.</summary>
    private bool Enter(int playerIndex, string roomKey)
    {
        string key = KeyOf(playerIndex);
        var map = _maps.GetValueOrDefault(key) ?? AutomapModel.Empty;
        if (map.Seen(roomKey))
            return false;
        var before = map;
        _maps[key] = map = AutomapRules.Enter(map, _graph!, roomKey);
        Send(playerIndex, map, roomKey);
        SendDiscovered(playerIndex, before, map);
        return true;
    }

    /// <summary>Every room of the level, to the hero and the members of his party here (the party shares one map).</summary>
    public void RevealAll(int playerIndex)
    {
        if (!Refresh())
            return;
        foreach (int mate in roster.MatesOf(playerIndex))
        {
            string key = KeyOf(mate);
            var before = _maps.GetValueOrDefault(key) ?? AutomapModel.Empty;
            var after = AutomapRules.RevealAll(before, _graph!);
            _maps[key] = after;
            foreach (string room in after.RoomsSeen)
                if (!before.Seen(room))
                    Send(mate, after, room);
        }
    }

    /// <summary>
    /// The party's maps made one (WO9): each member of the hero's party here holds every room any
    /// of them has seen (<see cref="AutomapRules.Merge"/>), and is sent the ones he lacked. What a
    /// joiner gets (the party's map), and what the party gets of his.
    /// </summary>
    public void Share(int playerIndex)
    {
        if (!Refresh())
            return;
        var mates = roster.MatesOf(playerIndex);
        if (mates.Count < 2)
            return;
        var party = mates.Aggregate(AutomapModel.Empty, (m, slot) => AutomapRules.Merge(m, MapOf(slot)));
        foreach (int mate in mates)
        {
            string key = KeyOf(mate);
            var before = MapOf(mate);
            var after = AutomapRules.Merge(before, party);
            _maps[key] = after;
            foreach (string room in after.RoomsSeen)
                if (!before.Seen(room))
                    Send(mate, after, room);
            SendDiscovered(mate, before, after);
        }
    }

    /// <summary>A hero joined a party (or formed one): the party's maps are made one. One who leaves keeps every room he was shown.</summary>
    public void OnPartyChanged(in PartyChange change)
    {
        if (change.Kind is not (PartyChangeKind.Joined or PartyChangeKind.Formed))
            return;
        if (parties.HeroOf(change.Member) is { } hero && slots.SlotOf(hero) is > 0 and var slot)
            Share(slot);
    }

    public void ShrineUsed(string markerKey)
    {
        if (!Refresh() || _graph!.Markers.FirstOrDefault(m => m.Id == markerKey) is not { } shrine)
            return;
        foreach (var hero in heroes.All)
        {
            if (hero.PlayerIndex <= 0)
                continue;
            string key = KeyOf(hero.PlayerIndex);
            var map = AutomapRules.UseShrine(_maps.GetValueOrDefault(key) ?? AutomapModel.Empty, markerKey);
            _maps[key] = map;
            if (map.Seen(shrine.Room))
                Send(hero.PlayerIndex, map, shrine.Room);
        }
    }

    public void OnSpawning(EntityHandle player, float now) { }

    /// <summary>A spawn sends the hero's whole map again, behind a sync that has his client forget the one it held.</summary>
    public void OnSpawned(EntityHandle player, float now)
    {
        if (heroes.All.FirstOrDefault(h => h.Player == player) is not { PlayerIndex: > 0 } hero || !Refresh())
            return;
        var map = MapOf(hero.PlayerIndex);
        var discovered = AutomapRules.Discovered(map, _graph!);
        sender.Send(new AutomapSync { Rooms = (byte)Math.Min(map.RoomsSeen.Count + discovered.Count, 255) }, hero.PlayerIndex);
        foreach (string room in map.RoomsSeen)
            Send(hero.PlayerIndex, map, room);
        foreach (var room in discovered)
            SendDiscovered(hero.PlayerIndex, map, room.Id);
    }

    /// <summary>A level's maps are its own.</summary>
    public void OnLevelShutdown()
    {
        _maps.Clear();
        _level = null;
        _graph = null;
    }

    /// <summary>The key a hero's map is held under: his sheet's (so a reconnect keeps it), else his slot.</summary>
    private string KeyOf(int playerIndex) =>
        sheets.KeyOf(playerIndex) ?? "slot " + playerIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether there is a level with rooms; the graph follows the level (a new one when the map changes).</summary>
    private bool Refresh()
    {
        if (levels.Current is not { Rooms.Length: > 0 } level)
            return false;
        if (_level is null || _level.Map != level.Map)
        {
            _maps.Clear();
            _graph = LevelAdapters.ToAutomap(level);
        }
        _level = level;
        return true;
    }

    /// <summary>One revealed room to the hero, as the rules' view of his map shows it.</summary>
    private void Send(int playerIndex, AutomapModel map, string roomKey)
    {
        if (Wire(map, roomKey, KeyOf(playerIndex)) is { } wire)
        {
            sender.Send(wire, playerIndex);
            RoomsSent++;
        }
    }

    /// <summary>
    /// The room <paramref name="roomKey"/> of <paramref name="map"/> on the wire: null when the
    /// rules' view of the map does not hold it (an unentered room is never sent). Its door stubs
    /// are the view's doors that touch it; its markers the view's markers in it, placed where the
    /// level's marker of that kind stands.
    /// </summary>
    public AutomapRoomWire? Wire(AutomapModel map, string roomKey, string viewer)
    {
        if (!Refresh())
            return null;
        var view = AutomapRules.View(map, _graph!, viewer, []);
        if (view.Rooms.FirstOrDefault(r => r.Id == roomKey) is not { } shown
            || !int.TryParse(roomKey, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0 || id >= _level!.Rooms.Length || id > 254)
            return null;
        var room = _level.Rooms[id];

        var doors = new List<LevelDoor>();
        foreach (var d in view.Doors)
            if ((d.From == roomKey || d.To == roomKey) && DoorOf(d.Id) is { } door && doors.Count < AutomapRoomWire.MaxDoors)
                doors.Add(door);

        var marks = new List<(AutomapMarkKind Kind, Vector At)>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in view.Markers)
        {
            if (m.Room != roomKey || marks.Count >= AutomapRoomWire.MaxMarks)
                continue;
            RulesMarkerKind? from = m.Kind switch
            {
                MapMarkerKind.Arrival => RulesMarkerKind.Arrival,
                MapMarkerKind.Stairs => RulesMarkerKind.Stairs,
                MapMarkerKind.ShrineUnused or MapMarkerKind.ShrineUsed => RulesMarkerKind.Shrine,
                MapMarkerKind.LockedContainer => RulesMarkerKind.LockedContainer,
                _ => null,
            };
            AutomapMarkKind? kind = m.Kind switch
            {
                MapMarkerKind.Arrival or MapMarkerKind.Stairs => AutomapMarkKind.Stairs,
                MapMarkerKind.ShrineUnused => AutomapMarkKind.ShrineUnused,
                MapMarkerKind.ShrineUsed => AutomapMarkKind.ShrineUsed,
                MapMarkerKind.LockedContainer => AutomapMarkKind.ContainerLocked,
                MapMarkerKind.Portal => AutomapMarkKind.Portal,
                _ => null,
            };
            if (from is not { } level || kind is not { } k)
                continue;
            var marker = _graph!.Markers.FirstOrDefault(x => x.Room == roomKey && x.Kind == level && !used.Contains(x.Id));
            if (marker is null || PositionOf(marker.Id) is not { } at)
                continue;
            used.Add(marker.Id);
            marks.Add((k, at));
        }

        // HO13: the boss's arena carries the boss's mark where it stands.
        if (_level.BossArena == id && marks.Count < AutomapRoomWire.MaxMarks && LevelAdapters.BossSpot(_level) is { } boss)
            marks.Add((AutomapMarkKind.Boss, new Vector(boss.Position.X, boss.Position.Y, boss.Position.Z)));

        string label = room.Label.Length > AutomapRoomWire.MaxLabel ? room.Label[..AutomapRoomWire.MaxLabel] : room.Label;
        return new AutomapRoomWire
        {
            Room = (byte)id,
            Storey = (byte)Math.Clamp(shown.Storey, 0, 255),
            MinX = AutomapRoomWire.Short(room.Min.X),
            MinY = AutomapRoomWire.Short(room.Min.Y),
            MaxX = AutomapRoomWire.Short(room.Max.X),
            MaxY = AutomapRoomWire.Short(room.Max.Y),
            Label = label,
            DoorX = [.. doors.Select(d => AutomapRoomWire.Short(d.Position.X))],
            DoorY = [.. doors.Select(d => AutomapRoomWire.Short(d.Position.Y))],
            DoorWidth = [.. doors.Select(d => AutomapRoomWire.Short(d.Width))],
            DoorAlongY = [.. doors.Select(d => (byte)(MathF.Abs(d.Normal.X) > MathF.Abs(d.Normal.Y) ? 1 : 0))],
            MarkKind = [.. marks.Select(m => (byte)m.Kind)],
            MarkX = [.. marks.Select(m => AutomapRoomWire.Short(m.At.X))],
            MarkY = [.. marks.Select(m => AutomapRoomWire.Short(m.At.Y))],
        };
    }

    /// <summary>The rooms <paramref name="after"/> discovers that <paramref name="before"/> did not, each sent to the hero as discovered.</summary>
    private void SendDiscovered(int playerIndex, AutomapModel before, AutomapModel after)
    {
        var had = AutomapRules.Discovered(before, _graph!).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var room in AutomapRules.Discovered(after, _graph!))
            if (!had.Contains(room.Id))
                SendDiscovered(playerIndex, after, room.Id);
    }

    private void SendDiscovered(int playerIndex, AutomapModel map, string roomKey)
    {
        if (DiscoveredWire(map, roomKey) is { } wire)
        {
            sender.Send(wire, playerIndex);
            RoomsSent++;
        }
    }

    /// <summary>
    /// The discovered room <paramref name="roomKey"/> of <paramref name="map"/> on the wire: its
    /// outline, storey and a stub for each door it has, flagged discovered, with no label and no
    /// marks; null when the map has not discovered it (entered, or two doors from every room entered).
    /// </summary>
    public AutomapRoomWire? DiscoveredWire(AutomapModel map, string roomKey)
    {
        if (!Refresh() || AutomapRules.Discovered(map, _graph!).FirstOrDefault(r => r.Id == roomKey) is not { } shown
            || !int.TryParse(roomKey, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0 || id >= _level!.Rooms.Length || id > 254)
            return null;
        var room = _level.Rooms[id];
        var doors = new List<LevelDoor>();
        foreach (var d in _graph!.Doors)
            if (d.Touches(roomKey) && DoorOf(d.Id) is { } door && doors.Count < AutomapRoomWire.MaxDoors)
                doors.Add(door);
        return new AutomapRoomWire
        {
            Room = (byte)id,
            Storey = (byte)Math.Clamp(shown.Storey, 0, 255),
            MinX = AutomapRoomWire.Short(room.Min.X),
            MinY = AutomapRoomWire.Short(room.Min.Y),
            MaxX = AutomapRoomWire.Short(room.Max.X),
            MaxY = AutomapRoomWire.Short(room.Max.Y),
            Label = "",
            DoorX = [.. doors.Select(d => AutomapRoomWire.Short(d.Position.X))],
            DoorY = [.. doors.Select(d => AutomapRoomWire.Short(d.Position.Y))],
            DoorWidth = [.. doors.Select(d => AutomapRoomWire.Short(d.Width))],
            DoorAlongY = [.. doors.Select(d => (byte)(MathF.Abs(d.Normal.X) > MathF.Abs(d.Normal.Y) ? 1 : 0))],
            MarkKind = [],
            MarkX = [],
            MarkY = [],
            Discovered = 1,
        };
    }

    private LevelDoor? DoorOf(string doorKey) =>
        doorKey.Length > 1 && int.TryParse(doorKey.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int d)
        && d >= 0 && d < _level!.Doors.Length ? _level.Doors[d] : null;

    /// <summary>Where a graph marker stands: <c>m&lt;id&gt;</c> is the level's marker, <c>exit</c> the way down.</summary>
    private Vector? PositionOf(string markerKey)
    {
        if (markerKey == "exit")
            return _level!.Exit?.Position;
        return markerKey.Length > 1 && int.TryParse(markerKey.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int m)
               && m >= 0 && m < _level!.Markers.Length ? _level.Markers[m].Position : null;
    }
}
