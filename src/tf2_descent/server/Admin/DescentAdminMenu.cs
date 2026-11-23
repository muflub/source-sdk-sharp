using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Portal;

namespace TF2.Descent.Server.Admin;

/// <summary>The game as TF2 Descent's admin menu reads and drives it. The facts give a fake; <see cref="EngineDescentAdminWorld"/> is the game's.</summary>
public interface IDescentAdminWorld
{
    /// <summary>Whether <c>sv_cheats</c> is on: every dev command runs only under it.</summary>
    bool Cheats { get; }

    /// <summary>Whether a host picks this game's levels (it travels by the host, not by <c>changelevel</c>).</summary>
    bool Hosted { get; }

    /// <summary>Each depth with a level installed in the pool, shallowest first.</summary>
    IReadOnlyList<int> InstalledDepths();

    /// <summary>The installed level of <paramref name="depth"/>; null for none.</summary>
    string? InstalledLevel(int depth);

    /// <summary>Whether this level is a Descent level (its rooms read): the town is not.</summary>
    bool OnLevel { get; }

    /// <summary>Whether this level's horde stands (robots to kill or spawn).</summary>
    bool Horde { get; }

    /// <summary>Whether this level has a boss arena.</summary>
    bool BossArena { get; }

    /// <summary>The level of the hero in <paramref name="slot"/>; null for no hero with a sheet.</summary>
    int? HeroLevel(int slot);

    /// <summary>Whether the player in <paramref name="slot"/> is in god mode.</summary>
    bool God(int slot);

    /// <summary>The client slot of <paramref name="player"/>.</summary>
    int SlotOf(EntityHandle player);

    /// <summary>A line on the server's console (a dev command).</summary>
    void Run(string line);

    /// <summary>The core's <c>god</c> toggle for the player in <paramref name="slot"/> (it says so on the console).</summary>
    void ToggleGod(int slot);
}

/// <summary>The engine's <see cref="IDescentAdminWorld"/>: the convar, the host, the pool, the level, the heroes, the core's god toggle and the console.</summary>
[SideService(Side.Server, As = [typeof(IDescentAdminWorld)])]
public sealed class EngineDescentAdminWorld(SvCheatsConVar cheats, HostService host, LevelHop hop, ILevelData level, HordeService horde, Heroes heroes,
                                            IEnginePlayers players, IEngineEntities entities, IEntityDirectory directory, DamageCheats damage,
                                            IServerConsole console) : IDescentAdminWorld
{
    public bool Cheats => cheats.Value;

    public bool Hosted => host.Mode == HostingMode.Hosted;

    public IReadOnlyList<int> InstalledDepths() => hop.InstalledDepths();

    public string? InstalledLevel(int depth) => hop.InstalledLevel(depth);

    public bool OnLevel => level.Current is not null;

    public bool Horde => horde.Current is { Begun: true };

    public bool BossArena => level.Current?.BossArena is not null;

    public int? HeroLevel(int slot) => heroes.Of(slot)?.Sheet?.Level;

    public bool God(int slot) => directory.Get(players.GetHandle(slot)) is SsPlayer { GodMode: true };

    public int SlotOf(EntityHandle player) => entities.GetIndex(player);

    public void Run(string line) => console.Execute(line);

    public void ToggleGod(int slot)
    {
        EntityHandle player = players.GetHandle(slot);
        if (player != EntityHandle.None)
            damage.ToggleGod(player);
    }
}

/// <summary>
/// TF2 DESCENT'S ADMIN MENU (the user, 2026-11-15: "the admin menu should be updated for descent").
/// The core's Map, Mode, Settings, Votes and Restart were built for HL2MP and TF2 and are hidden
/// (<see cref="IAdminMenuScope"/>); the core's Players stays, each player's folder gaining a Descent
/// folder that acts for him. In their place:
/// <list type="bullet">
/// <item><b>Map</b>: the town and each installed depth, by name ("Town", "Depth 3"), each this
/// process's level change, as the game's own travel does with no host; with a host, unavailable.</item>
/// <item><b>Descent</b>: sv_cheats on or off, then the dev commands an admin wants while playing, each
/// the existing console command for the admin who opened the menu: refill, level up and set level,
/// Australium, an item (the Teleporter Blueprint, the Repair Kit, a Sandvich, a Bonk! Six-Pack), the
/// automap revealed, the robots of the room or the level killed, a robot spawned (by class), god
/// mode, and a step to the boss arena's door. Each is unavailable, saying why, while sv_cheats is off
/// (the commands refuse without it) or where it has nothing to act on.</item>
/// </list>
/// The menu is the core's: only an admin is sent it, and only an admin's choice runs.
/// </summary>
[SideService(Side.Server, As = [typeof(IAdminMenuProvider), typeof(IAdminPlayerActions), typeof(IAdminMenuScope)])]
public sealed class DescentAdminMenu(IDescentAdminWorld world, TF2DescentRules rules) : IAdminMenuProvider, IAdminPlayerActions, IAdminMenuScope
{
    /// <summary>The core folder Descent keeps: its players (kick, spectate, and the Descent folder below).</summary>
    public bool Offers(string folder) => folder == AdminCoreFolders.Players;

    /// <summary>Why a dev command cannot run while sv_cheats is off.</summary>
    public const string CheatsOff = "sv_cheats is off: turn cheats on first (Descent, Cheats)";

    public const string NoHero = "no hero with a sheet in that slot";
    public const string NoHorde = "no horde on this level";
    public const string NoLevel = "the town has no automap";
    public const string NoBoss = "this level has no boss arena";
    public const string HostedLevels = "a host picks this game's levels";

    /// <summary>The levels a Set level folder offers (those above the hero's, at most the cap).</summary>
    public static IReadOnlyList<int> SetLevels => [5, 10, 15, 20, 25, 30, 40, 50];

    /// <summary>The Australium amounts offered.</summary>
    public static IReadOnlyList<int> Australium => [1_000, 10_000, 100_000];

    public IEnumerable<AdminNode> AdminItems(int slot)
    {
        yield return AdminNode.Folder("#SS_UI_Admin_Map", Maps());
        yield return AdminNode.Folder("Descent", [Cheats(), .. Actions(slot, "self")]);
    }

    public IEnumerable<AdminNode> ActionsFor(EntityHandle player, int userId)
    {
        yield return AdminNode.Folder("Descent", Actions(world.SlotOf(player), userId.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The town, then each installed depth, each changing this process's level to it.</summary>
    public List<AdminNode> Maps()
    {
        string? hosted = world.Hosted ? HostedLevels : null;
        var maps = new List<AdminNode> { Level("Town", PortalRoute.TownMap, "town", hosted) };
        foreach (int depth in world.InstalledDepths())
        {
            if (world.InstalledLevel(depth) is { } map)
                maps.Add(Level(string.Create(CultureInfo.InvariantCulture, $"Depth {depth}"), map, depth.ToString(CultureInfo.InvariantCulture), hosted));
        }
        return maps;
    }

    private AdminNode Level(string label, string map, string key, string? unavailable) =>
        new(label, "descent:map:" + key, () => world.Run($"changelevel {map}"), Unavailable: unavailable);

    private AdminNode Cheats() => world.Cheats
        ? new AdminNode("Cheats: on (turn off)", "descent:cheats:off", () => world.Run("sv_cheats 0"))
        : new AdminNode("Cheats: off (turn on)", "descent:cheats:on", () => world.Run("sv_cheats 1"));

    /// <summary>The dev commands for the player in <paramref name="slot"/>, their ids ending in <paramref name="who"/>.</summary>
    public List<AdminNode> Actions(int slot, string who)
    {
        int? level = world.HeroLevel(slot);
        string? hero = level is null ? NoHero : null;
        string? horde = world.Horde ? null : NoHorde;
        string s = slot.ToString(CultureInfo.InvariantCulture);
        int cap = rules.Heroes.LevelCap;

        AdminNode Cmd(string label, string verb, string line, string? unavailable = null) =>
            new(label, $"descent:{verb}:{who}", () => world.Run(line), Unavailable: world.Cheats ? unavailable : CheatsOff);

        var levels = SetLevels.Where(l => l > (level ?? 0) && l <= cap)
                              .Select(l => Cmd(l.ToString(CultureInfo.InvariantCulture), $"setlevel{l}", $"tf2_descent_setlevel {s} {l}", hero))
                              .ToList();
        var robots = rules.Content.Robots.Rows
                          .GroupBy(r => r.Class)
                          .Select(g => AdminNode.Folder(Title(g.Key),
                              [.. g.Select(r => Cmd(r.Name, "spawn_" + r.Id, $"tf2_descent_spawn_robot {r.Id} 0 - 0 {s}", horde))]))
                          .ToList();

        return
        [
            Cmd("Refill life and mana", "refill", $"tf2_descent_refill {s}", hero),
            Cmd("Level up", "levelup", $"tf2_descent_setlevel {s} {(level ?? 0) + 1}", hero ?? (level >= cap ? $"already at the level cap ({cap})" : null)),
            AdminNode.Folder("Set level", levels),
            AdminNode.Folder("Australium", [.. Australium.Select(n => Cmd(n.ToString("N0", CultureInfo.InvariantCulture), $"australium{n}",
                                                                           $"tf2_descent_australium {s} {n}", hero))]),
            AdminNode.Folder("Give item",
            [
                Cmd(Name("town_portal"), "give_town_portal", $"tf2_descent_give {s} town_portal", hero),
                Cmd(Name("repair_kit"), "give_repair_kit", $"tf2_descent_give {s} repair_kit", hero),
                Cmd(Name("health_large"), "give_health_large", $"tf2_descent_belt_give {s} health_large", hero),
                Cmd(Name("mana_large"), "give_mana_large", $"tf2_descent_belt_give {s} mana_large", hero),
            ]),
            Cmd("Reveal the automap", "automap", $"tf2_descent_automap {s} reveal", hero ?? (world.OnLevel ? null : NoLevel)),
            Cmd("Kill the robots in this room", "kill_room", $"tf2_descent_kill_robots {s} room", hero ?? horde),
            Cmd("Kill every robot on the level", "kill_level", $"tf2_descent_kill_robots {s} level", horde),
            AdminNode.Folder("Spawn a robot", robots),
            new AdminNode(world.God(slot) ? "God mode: on (turn off)" : "God mode: off (turn on)", $"descent:god:{who}", () => world.ToggleGod(slot),
                          Unavailable: world.Cheats ? null : CheatsOff),
            Cmd("Go to the boss", "goto_boss", $"tf2_descent_goto_boss {s}", hero ?? (world.BossArena ? null : NoBoss)),
        ];
    }

    private string Name(string itemBase) => rules.Content.ItemBases.TryGet(itemBase, out var row) ? row.Name : itemBase;

    /// <summary>A robot class's folder: <c>heavy</c> is "Heavy".</summary>
    private static string Title(string robotClass) =>
        robotClass.Length == 0 ? "Other" : char.ToUpperInvariant(robotClass[0]) + robotClass[1..];
}
