using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The mods' arms around a spawn's kit (tf2 stage 2.3,
/// <see cref="IPlayerSpawnHook"/>): <see cref="IPlayerSpawnHook.OnSpawning"/>
/// before the loadout providers are asked, <see cref="IPlayerSpawnHook.OnSpawned"/>
/// after the weapons and the ammo, and a hook that throws costs neither the
/// kit nor the hooks after it — and the <see cref="IKitGate"/> that can
/// refuse the grant between them, the stock TF2 observer's unarmed spawn.
/// </summary>
public class PlayerSpawnHookTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>Writes every arm it hears, and the provider's asks, into one shared order.</summary>
    private sealed class Hook(List<string> order, string name, bool throws = false) : IPlayerSpawnHook
    {
        public void OnSpawning(EntityHandle player, float now)
        {
            order.Add(name + ".spawning");
            if (throws)
                throw new InvalidOperationException("broken mod");
        }

        public void OnSpawned(EntityHandle player, float now) => order.Add(name + ".spawned");
    }

    private sealed class Provider(List<string> order) : ILoadoutProvider
    {
        public IEnumerable<string> Weapons(EntityHandle player)
        {
            order.Add("kit");
            return ["weapon_crowbar"];
        }

        public IEnumerable<(string, int)> Ammo(EntityHandle player) => [];
    }

    /// <summary>Writes its ask into the shared order (or a fresh one) and answers <paramref name="allow"/>.</summary>
    private sealed class Gate(List<string>? order, bool allow) : IKitGate
    {
        public bool ShouldGiveKit(EntityHandle player)
        {
            order?.Add("gate");
            return allow;
        }
    }

    private static (GameRules Rules, RecordingEngine Players, RecordingLog Log) Rules(
        IHooks<ILoadoutProvider>? loadout, IHooks<IPlayerSpawnHook>? hooks,
        IHooks<IKitGate>? kitGates = null, ISpawnWeapon? spawnWeapon = null)
    {
        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var host = Fake().Returns("Host.ActiveSide", Side.Server);
        var players = Fake().Ignores("Players.FindByUserId", "Players.GetUserId");
        players.Returns("Players.GiveItem", true);
        players.Returns("Players.GiveAmmo", 1);
        var log = new RecordingLog();
        var rules = new GameRules(log, convars.Holder<GameRulesConVars>(), host, players, Fake().Returns("Entities.IsValid", false),
                                  new Scoreboard(host, new PlayerDisconnectEvent(), new RecordingLog()),
                                  new PlayerDeathEvent(), loadout: loadout, spawnHooks: hooks,
                                  kitGates: kitGates, spawnWeapon: spawnWeapon);
        return (rules, players, log);
    }

    [Fact]
    public void Without_hooks_the_spawn_is_the_kit_alone()
    {
        var (rules, players, _) = Rules(null, null);

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(GameRules.Loadout.Length, players.CountOf("Players.GiveItem"));
    }

    [Fact]
    public void Spawning_comes_before_the_kit_and_spawned_after_it()
    {
        var order = new List<string>();
        var (rules, _, _) = Rules(new ListHooks<ILoadoutProvider>(new Provider(order)),
                                  new ListHooks<IPlayerSpawnHook>(new Hook(order, "a")));

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(["a.spawning", "kit", "a.spawned"], order);
    }

    [Fact]
    public void Spawned_hears_the_kit_already_given()
    {
        RecordingEngine? players = null;
        int givenAtSpawned = -1;
        var hook = new Probe(() => givenAtSpawned = players!.CountOf("Players.GiveItem"));
        (var rules, players, _) = Rules(null, new ListHooks<IPlayerSpawnHook>(hook));

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(GameRules.Loadout.Length, givenAtSpawned);
    }

    [Fact]
    public void Every_hook_is_asked_in_the_chains_order()
    {
        var order = new List<string>();
        var (rules, _, _) = Rules(null, new ListHooks<IPlayerSpawnHook>(new Hook(order, "a"), new Hook(order, "b")));

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(["a.spawning", "b.spawning", "a.spawned", "b.spawned"], order);
    }

    [Fact]
    public void A_throwing_hook_is_named_and_the_kit_and_the_next_hook_stand()
    {
        var order = new List<string>();
        var (rules, players, log) = Rules(null,
            new ListHooks<IPlayerSpawnHook>(new Hook(order, "broken", throws: true), new Hook(order, "b")));

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(["broken.spawning", "b.spawning", "broken.spawned", "b.spawned"], order);
        Assert.Equal(GameRules.Loadout.Length, players.CountOf("Players.GiveItem"));
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("spawn hook") && l.Text.Contains("broken mod"));
    }

    private sealed class Chooser(List<string> order) : ISpawnWeapon
    {
        public void Choose(EntityHandle player) => order.Add("choose");
    }

    [Fact]
    public void An_allowing_gate_leaves_the_spawn_as_it_was()
    {
        var (rules, players, _) = Rules(null, null, new ListHooks<IKitGate>(new Gate(null, allow: true)));

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(GameRules.Loadout.Length, players.CountOf("Players.GiveItem"));
    }

    [Fact]
    public void A_refusing_gate_gives_nothing_and_starts_no_spawn_weapon()
    {
        var order = new List<string>();
        var (rules, players, _) = Rules(new ListHooks<ILoadoutProvider>(new Provider(order)),
                                        null, new ListHooks<IKitGate>(new Gate(null, allow: false)),
                                        new Chooser(order));

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(0, players.CountOf("Players.GiveItem"));
        Assert.Equal(0, players.CountOf("Players.GiveAmmo"));
        // The providers are not asked, and the default-weapon choice — GiveDefaultItems
        // never ran — is not made either.
        Assert.Equal([], order);
    }

    [Fact]
    public void A_refused_spawn_still_runs_the_hooks_around_the_missing_kit()
    {
        var order = new List<string>();
        var (rules, _, _) = Rules(new ListHooks<ILoadoutProvider>(new Provider(order)),
                                  new ListHooks<IPlayerSpawnHook>(new Hook(order, "a")),
                                  new ListHooks<IKitGate>(new Gate(order, allow: false)));

        rules.OnPlayerSpawned(Player, 0f);

        // The gate is asked after the class-taking OnSpawning arms and before the
        // providers: a class chosen by the hooks is the class the gate judges.
        Assert.Equal(["a.spawning", "gate", "a.spawned"], order);
    }

    [Fact]
    public void A_throwing_gate_is_named_and_the_kit_stands()
    {
        var (rules, players, log) = Rules(null, null,
            new ListHooks<IKitGate>(new ThrowingGate(), new Gate(null, allow: true)));

        rules.OnPlayerSpawned(Player, 0f);

        Assert.Equal(GameRules.Loadout.Length, players.CountOf("Players.GiveItem"));
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("kit gate") && l.Text.Contains("broken mod"));
    }

    private sealed class ThrowingGate : IKitGate
    {
        public bool ShouldGiveKit(EntityHandle player) => throw new InvalidOperationException("broken mod");
    }

    private sealed class Probe(Action atSpawned) : IPlayerSpawnHook
    {
        public void OnSpawning(EntityHandle player, float now) { }
        public void OnSpawned(EntityHandle player, float now) => atSpawned();
    }
}
