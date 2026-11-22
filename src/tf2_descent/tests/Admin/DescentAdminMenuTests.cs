using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Admin;

using Xunit;

namespace TF2.Descent.Tests.Admin;

/// <summary>
/// TF2 Descent's admin menu (the user, 2026-11-15: "the admin menu should be updated for descent"):
/// the core's HL2MP and TF2 folders hidden but Players, a Map folder of the town and the installed
/// depths, and a Descent folder of the dev commands, each the existing console command for the
/// admin (or, in a player's folder, for that player), unavailable while sv_cheats is off.
/// </summary>
public sealed class DescentAdminMenuTests
{
    private const int Admin = 1;
    private const int Other = 3;

    private sealed class World : IDescentAdminWorld
    {
        public bool Cheats { get; set; } = true;
        public bool Hosted { get; set; }
        public bool OnLevel { get; set; } = true;
        public bool Horde { get; set; } = true;
        public bool BossArena { get; set; } = true;
        public Dictionary<int, string> Levels = new() { [1] = "tf2_descent-1-s1", [3] = "tf2_descent-3-s2" };
        public Dictionary<int, int> HeroLevels = new() { [Admin] = 4, [Other] = 12 };
        public HashSet<int> Gods = [];
        public readonly List<string> Lines = [];
        public readonly List<int> GodToggles = [];

        public IReadOnlyList<int> InstalledDepths() => [.. Levels.Keys.Order()];
        public string? InstalledLevel(int depth) => Levels.GetValueOrDefault(depth);
        public int? HeroLevel(int slot) => HeroLevels.TryGetValue(slot, out int l) ? l : null;
        public bool God(int slot) => Gods.Contains(slot);
        public int SlotOf(EntityHandle player) => (int)player.EntryIndex;
        public void Run(string line) => Lines.Add(line);
        public void ToggleGod(int slot) => GodToggles.Add(slot);
    }

    private static readonly Rules.TF2DescentRules SharedRules = new();

    private static (DescentAdminMenu Menu, World World) Make(Action<World>? setup = null)
    {
        var world = new World();
        setup?.Invoke(world);
        return (new DescentAdminMenu(world, SharedRules), world);
    }

    private static IReadOnlyList<AdminNode> Items(DescentAdminMenu menu) => [.. menu.AdminItems(Admin)];

    private static AdminNode Take(DescentAdminMenu menu, string id)
    {
        var node = AdminNode.Find(Items(menu), id);
        Assert.NotNull(node);
        Assert.Null(node!.Take());
        return node;
    }

    private static AdminNode Folder(IEnumerable<AdminNode> nodes, string label) => Assert.Single(nodes, n => n.Label == label && n.Children is not null);

    [Fact]
    public void Only_the_cores_players_folder_is_kept()
    {
        var (menu, _) = Make();

        Assert.Equal([AdminCoreFolders.Players], AdminCoreFolders.All.Where(menu.Offers));
    }

    [Fact]
    public void The_map_folder_is_the_town_then_each_installed_depth_by_name()
    {
        var (menu, _) = Make();

        var map = Folder(Items(menu), "#SS_UI_Admin_Map");

        Assert.Equal(["Town", "Depth 1", "Depth 3"], map.Children!.Select(n => n.Label));
    }

    [Fact]
    public void A_depth_changes_this_process_level_to_its_installed_level()
    {
        var (menu, world) = Make();

        Take(menu, "descent:map:3");
        Take(menu, "descent:map:town");

        Assert.Equal(["changelevel tf2_descent-3-s2", "changelevel tf2_descent_town"], world.Lines);
    }

    [Fact]
    public void With_a_host_the_levels_are_unavailable()
    {
        var (menu, world) = Make(w => w.Hosted = true);

        var map = Folder(Items(menu), "#SS_UI_Admin_Map");

        Assert.All(map.Children!, n => Assert.Equal(DescentAdminMenu.HostedLevels, n.Unavailable));
        Assert.Equal(DescentAdminMenu.HostedLevels, AdminNode.Find(Items(menu), "descent:map:1")!.Take());
        Assert.Empty(world.Lines);
    }

    [Fact]
    public void With_cheats_off_every_dev_command_is_unavailable_and_the_toggle_turns_them_on()
    {
        var (menu, world) = Make(w => w.Cheats = false);
        var descent = Folder(Items(menu), "Descent");

        var actions = AdminNode.Flatten(descent.Children!).Where(l => l.Id.Length > 0 && l.Id != "descent:cheats:on").ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, l => Assert.Equal(DescentAdminMenu.CheatsOff, l.Unavailable));

        Take(menu, "descent:cheats:on");
        Assert.Equal(["sv_cheats 1"], world.Lines);
    }

    [Fact]
    public void With_cheats_on_the_toggle_turns_them_off()
    {
        var (menu, world) = Make();

        Take(menu, "descent:cheats:off");

        Assert.Equal(["sv_cheats 0"], world.Lines);
    }

    [Fact]
    public void Refill_runs_the_refill_command_for_the_admin()
    {
        var (menu, world) = Make();

        Take(menu, "descent:refill:self");

        Assert.Equal(["tf2_descent_refill 1"], world.Lines);
    }

    [Fact]
    public void Level_up_raises_the_hero_one_level()
    {
        var (menu, world) = Make();

        Take(menu, "descent:levelup:self");

        Assert.Equal(["tf2_descent_setlevel 1 5"], world.Lines);
    }

    [Fact]
    public void Level_up_at_the_cap_is_unavailable()
    {
        var (menu, _) = Make(w => w.HeroLevels[Admin] = SharedRules.Heroes.LevelCap);

        Assert.StartsWith("already at the level cap", AdminNode.Find(Items(menu), "descent:levelup:self")!.Unavailable);
    }

    [Fact]
    public void Set_level_offers_only_levels_above_the_heros()
    {
        var (menu, world) = Make();
        var set = Folder(Folder(Items(menu), "Descent").Children!, "Set level");

        Assert.Equal(DescentAdminMenu.SetLevels.Where(l => l > 4 && l <= SharedRules.Heroes.LevelCap).Select(l => l.ToString()),
                     set.Children!.Select(n => n.Label));
        Take(menu, "descent:setlevel20:self");
        Assert.Equal(["tf2_descent_setlevel 1 20"], world.Lines);
    }

    [Fact]
    public void Australium_credits_the_admins_purse()
    {
        var (menu, world) = Make();

        Take(menu, "descent:australium10000:self");

        Assert.Equal(["tf2_descent_australium 1 10000"], world.Lines);
    }

    [Fact]
    public void Give_item_mints_the_blueprint_and_the_repair_kit_and_belts_the_potions()
    {
        var (menu, world) = Make();
        var give = Folder(Folder(Items(menu), "Descent").Children!, "Give item");

        foreach (var node in give.Children!)
            Assert.Null(node.Take());

        Assert.Equal(["Teleporter Blueprint", "Repair Kit", "Sandvich", "Bonk! Six-Pack"], give.Children!.Select(n => n.Label));
        Assert.Equal(["tf2_descent_give 1 town_portal", "tf2_descent_give 1 repair_kit",
                      "tf2_descent_belt_give 1 health_large", "tf2_descent_belt_give 1 mana_large"], world.Lines);
    }

    [Fact]
    public void Reveal_runs_the_automaps_reveal_and_is_unavailable_in_town()
    {
        var (menu, world) = Make();
        Take(menu, "descent:automap:self");
        Assert.Equal(["tf2_descent_automap 1 reveal"], world.Lines);

        var (town, _) = Make(w => w.OnLevel = false);
        Assert.Equal(DescentAdminMenu.NoLevel, AdminNode.Find(Items(town), "descent:automap:self")!.Unavailable);
    }

    [Fact]
    public void The_kill_entries_kill_the_room_or_the_level_for_the_admin()
    {
        var (menu, world) = Make();

        Take(menu, "descent:kill_room:self");
        Take(menu, "descent:kill_level:self");

        Assert.Equal(["tf2_descent_kill_robots 1 room", "tf2_descent_kill_robots 1 level"], world.Lines);
    }

    [Fact]
    public void With_no_horde_the_robot_entries_are_unavailable()
    {
        var (menu, _) = Make(w => w.Horde = false);
        var items = Items(menu);

        Assert.Equal(DescentAdminMenu.NoHorde, AdminNode.Find(items, "descent:kill_room:self")!.Unavailable);
        Assert.Equal(DescentAdminMenu.NoHorde, AdminNode.Find(items, "descent:kill_level:self")!.Unavailable);
        Assert.Equal(DescentAdminMenu.NoHorde, AdminNode.Find(items, "descent:spawn_soldier:self")!.Unavailable);
    }

    [Fact]
    public void Spawn_a_robot_is_a_folder_a_class_each_spawning_at_the_admins_crosshair()
    {
        var (menu, world) = Make();
        var spawn = Folder(Folder(Items(menu), "Descent").Children!, "Spawn a robot");

        Assert.Contains("Soldier", spawn.Children!.Select(n => n.Label));
        Assert.Equal(SharedRules.Content.Robots.Rows.Count, spawn.Children!.Sum(c => c.Children!.Count));
        Take(menu, "descent:spawn_soldier:self");
        Assert.Equal(["tf2_descent_spawn_robot soldier 0 - 0 1"], world.Lines);
    }

    [Fact]
    public void God_mode_toggles_the_cores_god_for_the_admin_and_says_which_way()
    {
        var (menu, world) = Make();
        Assert.Equal("God mode: off (turn on)", AdminNode.Find(Items(menu), "descent:god:self")!.Label);

        Take(menu, "descent:god:self");
        world.Gods.Add(Admin);

        Assert.Equal([Admin], world.GodToggles);
        Assert.Equal("God mode: on (turn off)", AdminNode.Find(Items(menu), "descent:god:self")!.Label);
    }

    [Fact]
    public void Go_to_the_boss_moves_the_admin_and_is_unavailable_with_no_arena()
    {
        var (menu, world) = Make();
        Take(menu, "descent:goto_boss:self");
        Assert.Equal(["tf2_descent_goto_boss 1"], world.Lines);

        var (none, _) = Make(w => w.BossArena = false);
        Assert.Equal(DescentAdminMenu.NoBoss, AdminNode.Find(Items(none), "descent:goto_boss:self")!.Unavailable);
    }

    [Fact]
    public void A_slot_with_no_hero_has_the_hero_entries_unavailable()
    {
        var (menu, _) = Make(w => w.HeroLevels.Remove(Admin));

        Assert.Equal(DescentAdminMenu.NoHero, AdminNode.Find(Items(menu), "descent:refill:self")!.Unavailable);
    }

    [Fact]
    public void A_players_folder_acts_for_that_player_by_his_user_id()
    {
        var (menu, world) = Make();
        var folder = Assert.Single(menu.ActionsFor(new EntityHandle((uint)((5 << 16) | Other)), userId: 42));

        Assert.Equal("Descent", folder.Label);
        Assert.Null(AdminNode.Find([folder], "descent:refill:42")!.Take());
        Assert.Null(AdminNode.Find([folder], "descent:levelup:42")!.Take());

        Assert.Equal(["tf2_descent_refill 3", "tf2_descent_setlevel 3 13"], world.Lines);
    }

    [Fact]
    public void Every_action_in_the_menu_has_an_id_of_its_own()
    {
        var (menu, _) = Make();
        var ids = AdminNode.Flatten([.. Items(menu), .. menu.ActionsFor(new EntityHandle((uint)((5 << 16) | Other)), 42)])
                           .Where(l => l.Id.Length > 0).Select(l => l.Id).ToList();

        Assert.Empty(ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key));
    }
}
