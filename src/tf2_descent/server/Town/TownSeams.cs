using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Portal;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Town;

/// <summary>
/// The engine as the town's stalls use it (<see cref="TownService"/>): whether this map is the
/// town, its markers, the heroes and where they stand, the stalls' entities, the windows a
/// client is told to open and close, and a level change. The facts give a fake;
/// <see cref="EngineTownWorld"/> is the engine's.
/// </summary>
public interface ITownWorld
{
    /// <summary>Whether <paramref name="map"/> is the town: unhosted the town's map, hosted the hub instance.</summary>
    bool IsTown(string map);

    /// <summary>Where the <c>info_target</c> named <paramref name="name"/> stands; null when the map has none.</summary>
    Vector? Marker(string name);

    /// <summary>The floor under <paramref name="at"/> (a stall stands on it); <paramref name="at"/> itself when nothing is under it.</summary>
    Vector Floor(Vector at);

    /// <summary>The hero in client slot <paramref name="player"/> as the ledger names him; null for none.</summary>
    string? HeroOf(int player);

    /// <summary>Where the player's feet are; null for no player there.</summary>
    Vector? Feet(int player);

    /// <summary>Builds the stall at <paramref name="stall"/>'s index <paramref name="index"/> at <paramref name="at"/> facing <paramref name="yaw"/>; its entity index, 0 when the engine refused.</summary>
    int MakeStall(int index, TownStall stall, Vector at, float yaw);

    /// <summary>The stall's entity index of an entity a hero used; -1 for an entity that is no stall.</summary>
    int StallOf(int entity);

    /// <summary>Tells the player's client to open or close a window's panel (the stock VGUIMenu, to him alone).</summary>
    void Panel(int player, string panel, bool show);

    /// <summary>The game's clock, in seconds (a restock is counted from the town's start on it).</summary>
    double Now { get; }

    /// <summary>
    /// The depths the town may send the hero in <paramref name="player"/> to now: the rules'
    /// <c>TownDescents.Destinations</c> over the depth his character has reached and the bosses it
    /// has killed (unhosted: none reached, so depth 1).
    /// </summary>
    IReadOnlyList<int> Destinations(int player);

    /// <summary>
    /// Sends the hero down to <paramref name="depth"/>: hosted through the travel service (WO7),
    /// unhosted by changing this process's level to the installed level of that depth. Null when
    /// it started; why not otherwise (<see cref="TownRefusals"/>).
    /// </summary>
    string? Descend(int player, int depth);
}

/// <summary>The engine's <see cref="ITownWorld"/>: the host's mode, the entity list, a floor trace, the stall entities, VGUIMenu, the console.</summary>
[SideService(Side.Server, As = [typeof(ITownWorld)])]
public sealed class EngineTownWorld(HostService host, HeroKeys keys, Heroes heroes, IEngineEntities entities, IEngineWorldQuery world,
                                    IEntityDirectory directory, IUserMessageSender messages, IGameClock clock, IServerConsole console,
                                    LevelHop hop, PlayerService players, IBossKills bossKills, TravelService travel, ILog log) : ITownWorld
{
    private readonly Dictionary<int, (EntityHandle Handle, int Stall)> _stalls = [];

    public bool IsTown(string map) =>
        host.Mode == HostingMode.Hosted ? host.Boot?.InstanceKind == "hub" : string.Equals(map, PortalRoute.TownMap, StringComparison.Ordinal);

    public Vector? Marker(string name)
    {
        var marker = entities.FindAllByClassname("info_target").FirstOrDefault(e => entities.GetName(e) == name);
        return marker == EntityHandle.None ? null : entities.GetOrigin(marker);
    }

    public Vector Floor(Vector at)
    {
        TraceResult down = world.TraceLine(at + new Vector(0f, 0f, 16f), at - new Vector(0f, 0f, 512f), TraceMask.Brushes);
        return down.Hit ? down.End : at;
    }

    public string? HeroOf(int player) => keys.Of(player);

    public Vector? Feet(int player) =>
        heroes.Of(player)?.Player is { } p && p != EntityHandle.None && entities.IsValid(p) ? entities.GetOrigin(p) : null;

    public int MakeStall(int index, TownStall stall, Vector at, float yaw)
    {
        // A stall of an earlier map went with it: forget its handle.
        foreach (int gone in _stalls.Where(kv => !entities.IsValid(kv.Value.Handle)).Select(kv => kv.Key).ToList())
            _stalls.Remove(gone);

        EntityHandle h = entities.Create(VendorNetState.ClassName);
        if (h == EntityHandle.None)
            return 0;
        entities.Spawn(h);
        if (directory.Get(h) is not TownVendor vendor)
        {
            entities.Remove(h);
            return 0;
        }
        vendor.Stand(index, stall, at, yaw);
        int entity = entities.GetIndex(h);
        _stalls[entity] = (h, index);
        return entity;
    }

    public int StallOf(int entity) =>
        _stalls.TryGetValue(entity, out var stall) && entities.IsValid(stall.Handle) ? stall.Stall : -1;

    public void Panel(int player, string panel, bool show) =>
        messages.Send(new VguiMenu { Name = panel, Show = show, Keys = [], Values = [] }, player, reliable: true);

    public double Now => clock.Now;

    public IReadOnlyList<int> Destinations(int player)
    {
        var entry = host.Mode == HostingMode.Hosted ? players.ByEntity(player) : null;
        return TownDescents.Destinations(Math.Max(entry?.ReachedDepth ?? 0, 0), entry is null ? [] : bossKills.Depths(entry.CharacterId));
    }

    public string? Descend(int player, int depth)
    {
        if (host.Mode == HostingMode.Hosted)
        {
            _ = travel.Request(player, TravelDirection.Down, depth);
            return null;
        }
        if (LevelFor(depth) is not { } map)
            return TownRefusals.NoLevel;
        log.Msg($"[TF2Descent/Town] unhosted: player {player} goes down to depth {depth}: this process changes level to {map}, and every player in it comes along\n");
        console.Execute($"changelevel {map}");
        return null;
    }

    /// <summary>The installed level of <paramref name="depth"/> (<c>tf2_descent-&lt;depth&gt;-s&lt;seed&gt;</c> in the mod's pool, the lowest seed); null for none.</summary>
    private string? LevelFor(int depth)
    {
        string dir = hop.PoolMaps;
        if (!Directory.Exists(dir))
            return null;
        return Directory.EnumerateFiles(dir, $"tf2_descent-{depth}-s*.bsp")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => n.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
