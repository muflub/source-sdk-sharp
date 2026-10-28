using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The mods' half of the spawn kit (tf2 stage 0.7 part C): the
/// <see cref="ILoadoutProvider"/> chain <see cref="GameRules.OnPlayerSpawned"/>
/// consults. The core's HL2 three-and-two must survive every arm that names
/// nothing, and a provider that does name something must be able to own one
/// column and leave the other.
/// </summary>
public class LoadoutProviderTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>
    /// One provider: the two columns answered from the outside, the asks
    /// counted and the player recorded, so the no-op arm can tell "answered
    /// nothing" from "was never asked".
    /// </summary>
    private sealed class Provider(IEnumerable<string>? weapons, IEnumerable<(string, int)>? ammo) : ILoadoutProvider
    {
        public int Asks { get; private set; }
        public Action? Throws;
        public readonly List<string> Asked = [];

        public IEnumerable<string> Weapons(EntityHandle player)
        {
            Asked.Add(player.ToString());
            Asks++;
            Throws?.Invoke();
            return weapons ?? Array.Empty<string>();
        }

        public IEnumerable<(string, int)> Ammo(EntityHandle player)
        {
            return ammo ?? Array.Empty<(string, int)>();
        }
    }

    /// <summary>A spawn weapon that notes how far the grant had got when it was asked.</summary>
    private sealed class ChoiceProbe : ISpawnWeapon
    {
        public Func<int> Seen = () => 0;
        public int SeenAt = -1;
        public int Choices;

        public void Choose(EntityHandle player)
        {
            Choices++;
            SeenAt = Seen();
        }
    }

    /// <summary>GiveDefaultItems' last word, cl_defaultweapon's switch, comes after the whole kit is in hand.</summary>
    [Fact]
    public void TheSpawnWeaponIsChosenOnceTheKitIsInHand()
    {
        var rig = new Rig();
        var probe = new ChoiceProbe();

        int seen = rig.GrantsBeforeChoice(rig.Rules(spawnWeapon: probe), probe);

        Assert.Equal(1, probe.Choices);
        Assert.Equal(GameRules.Loadout.Length + GameRules.StartingAmmo.Length, seen);
    }

    /// <summary>The rules over a recording engine, as the other rules' facts build them.</summary>
    private sealed class Rig
    {
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        public readonly RecordingLog Log = new();
        private readonly RecordingEngine _host;
        private readonly RecordingEngine _players;

        public Rig()
        {
            ConVars.RegisterAll(typeof(GameRules).Assembly);
            _host = Fake().Returns("Host.ActiveSide", Side.Server);
            _players = Fake().Ignores("Players.FindByUserId", "Players.GetUserId");
            _players.Returns("Players.GiveItem", true);
            _players.Returns("Players.GiveAmmo", 1);
        }

        public GameRules Rules(IHooks<ILoadoutProvider>? chain = null, ISpawnWeapon? spawnWeapon = null)
            => new(Log, ConVars, _host, _players,
                   Fake().Returns("Entities.IsValid", false),
                   new Scoreboard(_host, new PlayerDisconnectEvent(), new RecordingLog()),
                   new PlayerDeathEvent(), loadout: chain, spawnWeapon: spawnWeapon);

        /// <summary>How many grant calls the engine had seen when the spawn weapon was chosen.</summary>
        public int GrantsBeforeChoice(GameRules rules, ChoiceProbe probe)
        {
            probe.Seen = () => _players.Calls.Count(c => c.Member is "Players.GiveItem" or "Players.GiveAmmo");
            rules.OnPlayerSpawned(Player, 0f);
            return probe.SeenAt;
        }

        /// <summary>
        /// The weapon class names one spawn's grant passed to the engine.
        /// Snapshotted by call count — Calls is read-only and the facts that
        /// spawn twice share one rig.
        /// </summary>
        public List<string> Gave(GameRules rules)
        {
            int at = _players.Calls.Count;
            rules.OnPlayerSpawned(Player, 0f);
            return _players.Calls.Skip(at)
                .Where(c => c.Member == "Players.GiveItem")
                .Select(c => (string)c.Args[1]!)
                .ToList();
        }

        /// <summary>The (ammo, count) pairs one spawn's grant passed to the engine.</summary>
        public List<(string, int)> GaveAmmo(GameRules rules)
        {
            int at = _players.Calls.Count;
            rules.OnPlayerSpawned(Player, 0f);
            return _players.Calls.Skip(at)
                .Where(c => c.Member == "Players.GiveAmmo")
                .Select(c => ((string)c.Args[2]!, (int)c.Args[1]!))
                .ToList();
        }
    }

    //-------------------------------------------------------------------------
    // The no-owner arm: the core's kit, differential form.
    //-------------------------------------------------------------------------

    [Fact]
    public void WithoutProvidersTheCoreKitIsGiven()
    {
        var rig = new Rig();

        Assert.True(rig.Gave(rig.Rules()).SequenceEqual(GameRules.Loadout),
            "no chain: the game as it was");
        Assert.True(rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>())).SequenceEqual(GameRules.Loadout),
            "an assembled hook that nobody registered costs the kit nothing");

        // A provider that names nothing on either column is the same game —
        // and it WAS asked, so this is "answered nothing", not "not reached".
        var quiet = new Provider(null, null);
        List<string> quietKit = rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(quiet)));
        Assert.True(quietKit.SequenceEqual(GameRules.Loadout),
            "a provider that names nothing must not change the kit");
        Assert.True(quiet.Asks > 0, "the provider was never asked: the stimulus did not reach it");
    }

    //-------------------------------------------------------------------------
    // The owner arms.
    //-------------------------------------------------------------------------

    /// <summary>
    /// With no provider a spawn gets stock's GiveDefaultItems ammo: 255
    /// pistol, 45 SMG1, a grenade, 6 buckshot and 6 .357. Found side by side
    /// with stock (tools/stock-ab --weapons, 2026-10-25): this mod gave 100
    /// pistol and 45 SMG1 and nothing else, so a frag fresh from a give had one
    /// grenade where stock's had two.
    /// </summary>
    [Fact]
    public void TheCoreAmmoIsStocksDefaultItems()
    {
        var rig = new Rig();

        Assert.Equal([("Pistol", 255), ("SMG1", 45), ("Grenade", 1), ("Buckshot", 6), ("357", 6)], rig.GaveAmmo(rig.Rules()));
    }

    [Fact]
    public void AProviderTakesTheWeaponColumnWhole()
    {
        var rig = new Rig();

        // BEFORE: the core's kit, stock's GiveDefaultItems.
        Assert.True(rig.Gave(rig.Rules()).SequenceEqual(["weapon_crowbar", "weapon_pistol", "weapon_smg1", "weapon_frag",
                                                         "weapon_physcannon"]));

        var kit = new Provider(["weapon_357"], [("Buckshot", 6)]);
        Assert.True(rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(kit)))
                        .SequenceEqual(["weapon_357"]),
            "the provider owns the weapons column whole — the crowbar the mod never named is gone");
    }

    [Fact]
    public void AProviderCanTakeOneColumnAndLeaveTheOther()
    {
        var rig = new Rig();
        var ammoOnly = new Provider(null, [("SniperRifle", 30)]);

        Assert.True(rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(ammoOnly)))
                        .SequenceEqual(GameRules.Loadout),
            "naming only ammo leaves the weapons alone");
        Assert.True(rig.GaveAmmo(rig.Rules(new ListHooks<ILoadoutProvider>(ammoOnly)))
                        .SequenceEqual([("SniperRifle", 30)]),
            "and the ammo column is its own");
    }

    [Fact]
    public void TheLastProviderThatNamedAColumnWins()
    {
        var rig = new Rig();
        var first = new Provider(["weapon_357"], null);
        var second = new Provider(["weapon_crossbow"], null);

        Assert.True(rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(first, second)))
                        .SequenceEqual(["weapon_crossbow"]),
            "last answer wins per slot");

        // The first keeps its column when the second has no opinion on it.
        var quiet = new Provider(null, null);
        Assert.True(rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(first, quiet)))
                        .SequenceEqual(["weapon_357"]),
            "a later provider with no opinion does not erase the earlier one");
    }

    [Fact]
    public void OneProviderDDupesByNameAndKeepsTheLastCount()
    {
        var rig = new Rig();
        var one = new Provider(["weapon_357", "weapon_pistol", "weapon_357"],
                               [("Pistol", 10), ("Pistol", 90), ("SMG1", 5)]);

        Assert.True(rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(one)))
                        .SequenceEqual(["weapon_357", "weapon_pistol"]),
            "one name, one grant — in the order it was first named");
        Assert.True(rig.GaveAmmo(rig.Rules(new ListHooks<ILoadoutProvider>(one)))
                        .SequenceEqual([("Pistol", 90), ("SMG1", 5)]),
            "and the last count for a name is the one the player gets");
    }

    [Fact]
    public void AThrowingProviderIsNamedAndTheKitBeforeItStands()
    {
        var rig = new Rig();
        var good = new Provider(["weapon_357"], null);
        var broken = new Provider(["weapon_crossbow"], null)
        {
            Throws = () => throw new InvalidOperationException("broken mod"),
        };

        var rules = rig.Rules(new ListHooks<ILoadoutProvider>(good, broken));
        Assert.True(rig.Gave(rules).SequenceEqual(["weapon_357"]),
            "the broken provider never becomes the last answer");

        string[] lines = rig.Log.Lines.Select(l => l.Text).ToArray();
        Assert.True(lines.Any(l => l.Contains("LoadoutProviderTests") && l.Contains("threw")),
            $"the throwing provider was not named: {string.Join(" | ", lines.Where(l => l.Contains("loadout")))}");

        // A broken provider alone is the core's kit, not no kit at all.
        Assert.True(rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(broken)))
                        .SequenceEqual(GameRules.Loadout),
            "a broken mod must not disarm the player");
    }

    [Fact]
    public void TheKitIsAskedPerPlayer()
    {
        var rig = new Rig();
        var provider = new Provider(["weapon_357"], null);

        rig.Gave(rig.Rules(new ListHooks<ILoadoutProvider>(provider)));

        Assert.True(provider.Asked.Count > 0 && provider.Asked.All(a => a == Player.ToString()),
            $"asked about someone else: {string.Join(" | ", provider.Asked)}");
    }
}
