using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Mode;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Portal;

/// <summary>What using a portal came to: a trip started, or why not (a <c>#Descent_Portal_*</c> token or the host's words).</summary>
public readonly record struct PortalUse(bool Started, string? Why);

/// <summary>
/// THE TOWN PORTAL (plan_descent_logic WO4, ruling L10: an Engineer teleporter; the death plan's
/// M3). A Teleporter Blueprint scroll (<see cref="ITownPortal.Open"/>) builds the hero an
/// entrance pad on the floor in front of him, on a level; the town shows its exit pad. Standing on
/// a pad for <see cref="ChargeSeconds"/> uses it: the entrance goes up to town, the exit comes back
/// down to the entrance's place, once. After the return the portal is spent: both pads are gone,
/// and a second return is refused.
/// <list type="bullet">
/// <item><b>One a hero</b> (the death plan's M3: placing again moves it): a second scroll takes
/// the first portal down and builds the new one. In town no portal is built.</item>
/// <item><b>Who uses it</b>: its owner, and whoever <see cref="IPortalParty"/> allows (the
/// owner alone until parties).</item>
/// <item><b>Its life</b>: the record lives as long as the server, across every map change and a
/// hero's disconnect; a pad is made from it on each map it belongs to at level ready, and its
/// entity is forgotten when the map goes, so no record holds an entity across maps. A death keeps
/// it (the death plan's M3). It is Gone when its level is left for another level, when a new
/// scroll moves it, when the host no longer has it, or through <see cref="IPortalOwnerGone"/>.</item>
/// <item><b>Unhosted</b> (<see cref="PortalRoute"/>): going up changes this process's level to
/// the town, so the level is unloaded; coming back loads the same map and seed again, and the
/// level starts over from them (robots, loot and furniture as new). The hero is told so. A hosted
/// return is to the same live instance while it runs (the host's contract).</item>
/// <item><b>Edicts</b>: one pad a portal on each map, so at most one entity a portal in any
/// process (plan section 2's "hero bodies, teleporter pads, boss props": 16).</item>
/// </list>
/// </summary>
[SideService(Side.Server, As = [typeof(ITownPortal), typeof(IPortalOwnerGone), typeof(IPortalRecords)])]
public sealed class TownPortals(IDescentModeState mode, IPortalWorld world, IPortalRoute route, IHostAnchors anchors,
                                IPortalParty party, ILog log)
    : ITownPortal, IPortalOwnerGone, IPortalRecords, IGameLifecycle, IFrameLifecycle
{
    /// <summary>After the level's data (+50) and the loot floor's pool, so a pad is made after the level's own pools.</summary>
    public const int Order = ModStages.First + 1400;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    /// <summary>How long a hero stands on a pad before it sends him: TF2's entrance sends a player who stands still on it; about a second.</summary>
    public const float ChargeSeconds = 1f;

    /// <summary>How often the pads look for heroes standing on them.</summary>
    public const float TickSeconds = 0.1f;

    /// <summary>Standing on a pad: the feet within its half width of its middle, and on its deck (from the floor to a step above the deck).</summary>
    public const float StandRadius = PortalPlacement.Half, StandHeight = PortalPlacement.Height + 18f;

    /// <summary>How high above a pad's floor a hero is set on it: on the deck (TF2's teleporter arrival height, 13).</summary>
    public const float DeckHeight = 13f;

    public const string NotHere = "#Descent_Portal_NotHere";
    public const string InTown = "#Descent_Portal_InTown";
    public const string NoHero = "#Descent_Portal_NoHero";
    public const string Dead = "#Descent_Portal_Dead";
    public const string Refused = "#Descent_Portal_Refused";
    public const string NotBuilt = "#Descent_Portal_NotBuilt";
    public const string Opened = "#Descent_Portal_Opened";
    public const string Moved = "#Descent_Portal_Moved";
    public const string Spent = "#Descent_Portal_Spent";
    public const string Gone = "#Descent_Portal_Gone";
    public const string NotYours = "#Descent_Portal_NotYours";
    public const string Busy = "#Descent_Portal_Busy";
    public const string NoPad = "#Descent_Portal_NoPad";
    public const string Back = "#Descent_Portal_Back";
    public const string Arrived = "#Descent_Portal_Arrived";
    public const string Anew = "#Descent_Portal_Anew";

    private readonly List<PortalRecord> _records = [];
    private int _nextId = 1;

    // This map's: the pads made from records (by record id), the charge of each (pad, player), the
    // pairs that must step off before a pad charges again, and the hosted asks already made. All
    // cleared when the map goes: no entity outlives its map here.
    private readonly Dictionary<int, Pad> _pads = [];
    private readonly Dictionary<(int Pad, int Player), float> _charge = [];
    private readonly HashSet<(int Pad, int Player)> _mustStepOff = [];
    private readonly HashSet<int> _travelling = [];
    private readonly HashSet<string> _askedHere = new(StringComparer.Ordinal);
    private string _map = "";
    private int _mapSerial;
    private bool _levelAnchorsAsked;
    private float _sinceTick;

    private readonly record struct Pad(int Entity, PortalEnd End, Vector At, float Yaw);

    public IReadOnlyList<PortalRecord> All => _records;

    public PortalRecord? OpenOf(string hero) => _records.FirstOrDefault(r => r.Owner == hero && r.State == PortalState.Open);

    /// <summary>The pad of <paramref name="portal"/> on this map, its entity index; 0 for none.</summary>
    public int PadOf(PortalRecord portal) => _pads.TryGetValue(portal.Id, out var p) ? p.Entity : 0;

    /// <summary>The map this service last saw start.</summary>
    public string Map => _map;

    //-------------------------------------------------------------------------
    // Opening (the scroll)
    //-------------------------------------------------------------------------

    public PortalAnswer Open(int playerIndex)
    {
        if (!mode.Active)
            return Refuse(playerIndex, NotHere, "not a Descent game");
        PortalHere here = world.Here(_map);
        if (!here.IsLevel)
            return Refuse(playerIndex, InTown, $"{_map} is not a level");
        if (world.HeroOf(playerIndex) is not { } hero)
            return Refuse(playerIndex, NoHero, "no hero");
        if (!world.IsAlive(playerIndex))
            return Refuse(playerIndex, Dead, "dead");

        PortalPlace place = world.Place(playerIndex);
        if (!place.Ok)
            return Refuse(playerIndex, place.Why, "no place for the pad");

        var portal = new PortalRecord
        {
            Id = _nextId,
            Owner = hero,
            Character = world.CharacterOf(playerIndex) ?? "",
            Map = here.Map,
            Depth = here.Depth,
            Seed = here.Seed,
            LevelId = here.LevelId,
            Position = place.Position,
            Yaw = place.Yaw,
            Team = world.Team(playerIndex),
        };

        int pad = world.MakePad(portal, PortalEnd.Entrance, place.Position, place.Yaw);
        if (pad == 0)
            return Refuse(playerIndex, NotBuilt, "the engine refused the pad");

        // One a hero: the new one moves the old (the death plan's M3).
        foreach (PortalRecord old in _records.Where(r => r.Owner == hero && r.State == PortalState.Open).ToList())
        {
            Close(old, PortalState.Gone, $"moved by portal {portal.Id}");
            world.Notice(playerIndex, Moved);
        }

        _nextId++;
        _records.Add(portal);
        _pads[portal.Id] = new Pad(pad, PortalEnd.Entrance, place.Position, place.Yaw);
        world.Show(PortalShow.Built, pad, playerIndex);
        world.Notice(playerIndex, Opened);
        log.Msg($"[TF2Descent/Portal] {playerIndex} ({hero}) opened {portal.Line()}; entrance pad #{pad}\n");

        if (route.Hosted)
            _ = Anchor(hero, portal);
        return new PortalAnswer(true, null);
    }

    private async Task Anchor(string hero, PortalRecord portal)
    {
        try
        {
            bool placed = await anchors.Place(hero, portal.Position, portal.Yaw);
            log.Msg($"[TF2Descent/Portal] portal {portal.Id}: the host {(placed ? "keeps" : "refused")} its anchor\n");
        }
        catch (Exception e)
        {
            log.Warning($"[TF2Descent/Portal] portal {portal.Id}: placing its anchor threw: {e.Message}\n");
        }
    }

    /// <summary>A refused scroll: logged here; the scroll's user tells the hero <paramref name="why"/> and keeps the scroll.</summary>
    private PortalAnswer Refuse(int playerIndex, string why, string words)
    {
        log.Msg($"[TF2Descent/Portal] {playerIndex} could not open a portal: {words} ({why})\n");
        return new PortalAnswer(false, why);
    }

    //-------------------------------------------------------------------------
    // Using a pad
    //-------------------------------------------------------------------------

    /// <summary>
    /// The player uses the pad he may use on this map, as standing on it for the charge time does
    /// (the dev command <c>tf2_descent_portal_use</c>): the nearest of the pads he may use, else
    /// why none (his own portal spent or gone, or none here).
    /// </summary>
    public Task<PortalUse> Use(int player)
    {
        if (world.HeroOf(player) is not { } hero)
            return Task.FromResult(Told(player, NoHero));

        Vector feet = world.Feet(player) ?? default;
        var usable = _pads
            .Select(kv => (Portal: Find(kv.Key), Pad: kv.Value))
            .Where(p => p.Portal is { State: PortalState.Open } r && (r.Owner == hero || party.MayUse(r.Owner, hero)))
            .OrderBy(p => Distance2(p.Pad.At, feet))
            .FirstOrDefault();
        if (usable.Portal is { } portal)
            return UsePad(portal, usable.Pad, player);

        // Nothing here he may use: say why, by his own latest portal.
        PortalRecord? own = _records.LastOrDefault(r => r.Owner == hero);
        string why = own?.State switch
        {
            PortalState.Spent => Spent,
            PortalState.Gone => Gone,
            _ when _pads.Count > 0 => NotYours,
            _ => NoPad,
        };
        return Task.FromResult(Told(player, why));
    }

    /// <summary>
    /// The player uses portal <paramref name="id"/>'s pad on this map, as standing on it does: a
    /// spent or gone portal is refused as such, and one with no pad on this map says so.
    /// </summary>
    public Task<PortalUse> Use(int player, int id)
    {
        if (Find(id) is not { } portal)
            return Task.FromResult(Told(player, NoPad));
        if (portal.State != PortalState.Open)
            return Task.FromResult(Told(player, portal.State == PortalState.Spent ? Spent : Gone));
        return _pads.TryGetValue(id, out Pad pad) ? UsePad(portal, pad, player) : Task.FromResult(Told(player, NoPad));
    }

    private Task<PortalUse> UsePad(PortalRecord portal, Pad pad, int player)
    {
        if (world.HeroOf(player) is not { } hero)
            return Task.FromResult(Told(player, NoHero));
        if (!world.IsAlive(player))
            return Task.FromResult(Told(player, Dead));
        if (portal.State != PortalState.Open)
            return Task.FromResult(Told(player, portal.State == PortalState.Spent ? Spent : Gone));
        if (portal.Owner != hero && !party.MayUse(portal.Owner, hero))
            return Task.FromResult(Told(player, NotYours));
        if (!_travelling.Add(portal.Id))
            return Task.FromResult(Told(player, Busy));
        return pad.End == PortalEnd.Entrance ? GoUp(portal, pad, player, hero) : GoBack(portal, pad, player, hero);
    }

    private async Task<PortalUse> GoUp(PortalRecord portal, Pad pad, int player, string hero)
    {
        try
        {
            PortalTrip trip = await route.Up(player, hero, portal);
            if (!trip.Started)
            {
                log.Msg($"[TF2Descent/Portal] {player} ({hero}) could not go up through portal {portal.Id}: {trip.Why}\n");
                return Told(player, Refused, trip.Why);
            }

            portal.Trips++;
            portal.Arriving = hero;
            portal.ArrivalEnd = PortalEnd.Exit;
            world.Show(PortalShow.Departed, pad.Entity, player);
            log.Msg($"[TF2Descent/Portal] {player} ({hero}) goes up through portal {portal.Id} from depth {portal.Depth}\n");
            return new PortalUse(true, null);
        }
        finally
        {
            _travelling.Remove(portal.Id);
        }
    }

    private async Task<PortalUse> GoBack(PortalRecord portal, Pad pad, int player, string hero)
    {
        try
        {
            PortalTrip trip = await route.Back(player, hero, portal);
            if (!trip.Started)
            {
                if (trip.Gone)
                {
                    Close(portal, PortalState.Gone, $"the host has no such teleporter ({trip.Why})");
                    log.Msg($"[TF2Descent/Portal] {player} ({hero}) could not go back through portal {portal.Id}: it is gone\n");
                    return Told(player, Gone);
                }
                log.Msg($"[TF2Descent/Portal] {player} ({hero}) could not go back through portal {portal.Id}: {trip.Why}\n");
                return Told(player, Refused, trip.Why);
            }

            portal.Trips++;
            world.Show(PortalShow.Departed, pad.Entity, player);
            Close(portal, PortalState.Spent, $"{hero} came back through it");
            portal.Arriving = hero;
            portal.ArrivalEnd = PortalEnd.Entrance;
            world.Notice(player, Back);
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Portal] {player} ({hero}) goes back through portal {portal.Id} to depth {portal.Depth} seed {portal.Seed?.ToString(CultureInfo.InvariantCulture) ?? "none"}{(portal.LevelId.Length > 0 ? " level " + portal.LevelId : "")}" +
                $" at ({portal.Position.X:0} {portal.Position.Y:0} {portal.Position.Z:0}): the portal is spent\n"));
            return new PortalUse(true, null);
        }
        finally
        {
            _travelling.Remove(portal.Id);
        }
    }

    private PortalUse Told(int player, string why, string arg = "")
    {
        world.Notice(player, why, arg);
        return new PortalUse(false, why.StartsWith('#') ? why : arg);
    }

    //-------------------------------------------------------------------------
    // Closing
    //-------------------------------------------------------------------------

    public int OwnerGone(string hero, string why)
    {
        var open = _records.Where(r => r.Owner == hero && r.State == PortalState.Open).ToList();
        foreach (PortalRecord portal in open)
        {
            Close(portal, PortalState.Gone, why);
            if (route.Hosted)
                _ = anchors.Clear(hero);
        }
        return open.Count;
    }

    /// <summary>Ends a portal: its pad on this map goes (the other map's went with that map).</summary>
    private void Close(PortalRecord portal, PortalState state, string note)
    {
        portal.State = state;
        portal.Note = note;
        portal.Arriving = null;
        if (_pads.Remove(portal.Id, out Pad pad))
            world.RemovePad(pad.Entity);
        log.Msg($"[TF2Descent/Portal] portal {portal.Id} {state.ToString().ToLowerInvariant()}: {note}\n");
    }

    //-------------------------------------------------------------------------
    // Maps
    //-------------------------------------------------------------------------

    public void OnLevelInit(string mapName)
    {
        ForgetMap();
        _map = mapName;
        _mapSerial++;
    }

    /// <summary>
    /// The pads this map holds: on a level, the entrance of each open portal standing on it;
    /// in town, the exit of each open portal (unhosted: the records this process keeps). A portal
    /// left open on another level is Gone: unhosted, its level was unloaded and another loaded in
    /// its place, so the party moved on.
    /// </summary>
    public void OnLevelReady(string mapName)
    {
        if (mapName != _map)
            OnLevelInit(mapName);
        if (!mode.Active)
            return;

        PortalHere here = world.Here(_map);
        if (here.IsLevel)
        {
            foreach (PortalRecord portal in _records.Where(r => r.State == PortalState.Open).ToList())
            {
                if (here.Holds(portal))
                    Build(portal, PortalEnd.Entrance, portal.Position, portal.Yaw);
                else if (!route.Hosted)
                    Close(portal, PortalState.Gone, $"its level was left for {_map}");
            }

            foreach (PortalRecord back in _records.Where(r => r.State == PortalState.Spent && r.Arriving is not null && here.Holds(r)))
                log.Msg(Fresh(back));
            return;
        }

        int index = 0;
        foreach (PortalRecord portal in _records.Where(r => r.State == PortalState.Open).ToList())
            BuildExit(portal, index++);
    }

    private void BuildExit(PortalRecord portal, int index)
    {
        if (world.TownSpot(index) is not { } spot)
        {
            log.Warning($"[TF2Descent/Portal] {_map} has no place for portal {portal.Id}'s exit pad\n");
            return;
        }
        Build(portal, PortalEnd.Exit, spot.At, spot.Yaw);
    }

    private void Build(PortalRecord portal, PortalEnd end, Vector at, float yaw)
    {
        int pad = world.MakePad(portal, end, at, yaw);
        if (pad == 0)
        {
            log.Warning($"[TF2Descent/Portal] the engine refused portal {portal.Id}'s {end.ToString().ToLowerInvariant()} pad on {_map}\n");
            return;
        }
        _pads[portal.Id] = new Pad(pad, end, at, yaw);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Portal] {_map}: portal {portal.Id}'s {end.ToString().ToLowerInvariant()} pad #{pad} at ({at.X:0} {at.Y:0} {at.Z:0}) yaw {yaw:0}\n"));
    }

    /// <summary>What an unhosted return finds: the level loaded anew from its map and seed. Hosted: the same live instance.</summary>
    private string Fresh(PortalRecord portal) => route.Hosted
        ? $"[TF2Descent/Portal] portal {portal.Id}: back on level {portal.LevelId}, the host's instance\n"
        : string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Portal] unhosted: depth {portal.Depth} seed {portal.Seed?.ToString(CultureInfo.InvariantCulture) ?? "none"} was loaded anew from {portal.Map} " +
            $"and its seed: its robots, loot and furniture start over (one process holds one map, so the level was unloaded while the party was in town; a hosted level is the same live instance)\n");

    public void OnLevelShutdown() => ForgetMap();

    private void ForgetMap()
    {
        _pads.Clear();
        _charge.Clear();
        _mustStepOff.Clear();
        _travelling.Clear();
        _askedHere.Clear();
        _levelAnchorsAsked = false;
        _sinceTick = 0f;
    }

    //-------------------------------------------------------------------------
    // The tick: arrivals, standing on pads, the host's anchors
    //-------------------------------------------------------------------------

    public void OnThink(float frameTime)
    {
        if (!mode.Active)
            return;
        _sinceTick += frameTime;
        if (_sinceTick < TickSeconds)
            return;
        Tick(_sinceTick);
        _sinceTick = 0f;
    }

    /// <summary>One look, <paramref name="elapsed"/> seconds after the last: arrivals set on their pads, then the charge of each hero on each pad.</summary>
    public void Tick(float elapsed)
    {
        IReadOnlyList<int> heroes = world.Heroes();
        Arrivals(heroes);
        if (route.Hosted)
            AskTheHost(heroes);
        Charge(heroes, elapsed);
    }

    private void Arrivals(IReadOnlyList<int> heroes)
    {
        PortalHere? here = null;
        foreach (PortalRecord portal in _records)
        {
            if (portal.Arriving is not { } who)
                continue;
            int player = heroes.FirstOrDefault(p => world.HeroOf(p) == who);
            if (player == 0 || !world.IsAlive(player))
                continue;

            if (portal.ArrivalEnd == PortalEnd.Exit && _pads.TryGetValue(portal.Id, out Pad exit) && exit.End == PortalEnd.Exit)
            {
                SetOn(portal, player, exit, $"arrived in {_map} on portal {portal.Id}'s exit pad");
                world.Notice(player, Arrived);
            }
            else if (portal.ArrivalEnd == PortalEnd.Entrance && portal.State == PortalState.Spent && (here ??= world.Here(_map)).Holds(portal))
            {
                SetOn(portal, player, new Pad(0, PortalEnd.Entrance, portal.Position, portal.Yaw),
                      $"is back on depth {portal.Depth} at portal {portal.Id}'s entrance");
                if (!route.Hosted)
                    world.Notice(player, Anew);
            }
        }
    }

    private void SetOn(PortalRecord portal, int player, Pad pad, string what)
    {
        Vector deck = pad.At + new Vector(0f, 0f, DeckHeight);
        world.SetOn(player, deck, pad.Yaw);
        world.Show(PortalShow.Arrived, pad.Entity, player);
        if (pad.Entity != 0)
            _mustStepOff.Add((pad.Entity, player));
        portal.Arriving = null;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Portal] {player} {what}, set at ({deck.X:0} {deck.Y:0} {deck.Z:0})\n"));
    }

    private void Charge(IReadOnlyList<int> heroes, float elapsed)
    {
        foreach (var (id, pad) in _pads.ToList())
        {
            if (Find(id) is not { State: PortalState.Open } portal)
                continue;
            foreach (int player in heroes)
            {
                var key = (pad.Entity, player);
                if (!world.IsAlive(player) || world.Feet(player) is not { } feet || !StandsOn(feet, pad.At))
                {
                    _charge.Remove(key);
                    _mustStepOff.Remove(key);
                    continue;
                }
                if (_mustStepOff.Contains(key))
                    continue;

                // A thousandth of slack: ten ticks of a tenth are a second whatever the float sum says.
                float held = _charge.GetValueOrDefault(key) + elapsed;
                if (held < ChargeSeconds - 1e-3f)
                {
                    _charge[key] = held;
                    continue;
                }

                _charge.Remove(key);
                _mustStepOff.Add(key);
                _ = UsePad(portal, pad, player);
            }
        }
    }

    /// <summary>Whether feet at <paramref name="feet"/> stand on a pad whose floor is at <paramref name="pad"/>.</summary>
    public static bool StandsOn(Vector feet, Vector pad)
    {
        float dx = feet.X - pad.X, dy = feet.Y - pad.Y, dz = feet.Z - pad.Z;
        return dx * dx + dy * dy <= StandRadius * StandRadius && dz >= -1f && dz <= StandHeight;
    }

    //-------------------------------------------------------------------------
    // Hosted: the town's pads from the host, a return's arrival, a fresh instance's teleporters
    //-------------------------------------------------------------------------

    private void AskTheHost(IReadOnlyList<int> heroes)
    {
        PortalHere here = world.Here(_map);
        if (here.IsLevel && here.Live && !_levelAnchorsAsked)
        {
            _levelAnchorsAsked = true;
            _ = ReadLevelAnchors(_mapSerial);
        }

        foreach (int player in heroes)
        {
            if (world.HeroOf(player) is not { } hero || !_askedHere.Add(hero))
                continue;
            _ = here.IsLevel ? TakeArrival(player, hero, _mapSerial) : ReadPartyAnchors(player, hero, _mapSerial);
        }
    }

    /// <summary>A fresh instance of a level re-creates the teleporters the host lists for it (the death plan's M3).</summary>
    private async Task ReadLevelAnchors(int serial)
    {
        IReadOnlyList<PortalAnchor> listed = await anchors.OnThisLevel();
        if (serial != _mapSerial)
            return;
        PortalHere here = world.Here(_map);
        foreach (PortalAnchor a in listed)
        {
            if (_records.Any(r => r.State == PortalState.Open && r.Character == a.Owner && r.LevelId == a.LevelId))
                continue;
            var portal = Remember(a, here.Map);
            Build(portal, PortalEnd.Entrance, portal.Position, portal.Yaw);
        }
    }

    /// <summary>In town: one exit pad for each teleporter of the hero's party the host lists.</summary>
    private async Task ReadPartyAnchors(int player, string hero, int serial)
    {
        IReadOnlyList<PortalAnchor> listed = await anchors.Party(hero);
        if (serial != _mapSerial)
            return;
        string mine = world.CharacterOf(player) ?? "";
        foreach (PortalAnchor a in listed)
        {
            if (_records.Any(r => r.State == PortalState.Open && r.Character == a.Owner && r.LevelId == a.LevelId))
                continue;
            var portal = Remember(a, "", a.Owner == mine ? hero : null);
            BuildExit(portal, _pads.Values.Count(p => p.End == PortalEnd.Exit));
        }
    }

    /// <summary>On a level: a hero the town sent through a teleporter stands at its entrance, and that portal is spent.</summary>
    private async Task TakeArrival(int player, string hero, int serial)
    {
        string? through = await anchors.TakeArrival(hero);
        if (through is null || serial != _mapSerial)
            return;
        PortalHere here = world.Here(_map);
        if (_records.FirstOrDefault(r => r.State == PortalState.Open && r.Character == through && here.Holds(r)) is not { } portal)
        {
            log.Msg($"[TF2Descent/Portal] {player} ({hero}) came through {through}'s teleporter, which this level does not hold\n");
            return;
        }

        portal.Trips++;
        Close(portal, PortalState.Spent, $"{hero} came back through it");
        portal.Arriving = hero;
        portal.ArrivalEnd = PortalEnd.Entrance;
        if (portal.Owner == hero)
            _ = anchors.Clear(hero);
        log.Msg(Fresh(portal));
    }

    private PortalRecord Remember(PortalAnchor a, string map, string? owner = null)
    {
        var portal = new PortalRecord
        {
            Id = _nextId++,
            Owner = owner ?? "character:" + a.Owner,
            Character = a.Owner,
            Map = map,
            Depth = a.Depth,
            LevelId = a.LevelId,
            Position = a.Position,
            Yaw = a.Yaw,
        };
        _records.Add(portal);
        log.Msg($"[TF2Descent/Portal] the host lists {portal.Line()}\n");
        return portal;
    }

    //-------------------------------------------------------------------------

    private PortalRecord? Find(int id) => _records.FirstOrDefault(r => r.Id == id);

    private static float Distance2(Vector a, Vector b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Every portal's record and state, and this map's pads (<c>tf2_descent_portals</c>).</summary>
    public string Report()
    {
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"[TF2Descent/Portal] {_records.Count} portal(s) on record; {_map}: {_pads.Count} pad(s)\n");
        foreach (PortalRecord portal in _records)
        {
            b.Append("[TF2Descent/Portal]   ").Append(portal.Line());
            if (_pads.TryGetValue(portal.Id, out Pad pad))
                b.Append(CultureInfo.InvariantCulture, $"; here: {pad.End.ToString().ToLowerInvariant()} pad #{pad.Entity} at ({pad.At.X:0} {pad.At.Y:0} {pad.At.Z:0})");
            b.Append('\n');
        }
        return b.ToString();
    }
}
