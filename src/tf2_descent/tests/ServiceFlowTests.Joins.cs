using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using TF2.Descent.Rules;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Tests.Hero;
using TF2.Descent.Tests.Items.Ledger;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// A hosted join told (<see cref="IHeroJoinHook"/>), and the item ledger loading the hero as it is
/// (SDK 2.7: the host holds his kit worn from his creation, so his TF kit follows at once).
/// </summary>
public sealed partial class ServiceFlowTests
{
    private const string Joiner = "76561197960287930";

    private sealed class JoinRecorder : IHeroJoinHook
    {
        public List<(int EntIndex, string SteamId)> Seen { get; } = [];
        public void OnHeroJoined(int entIndex, string steamId) => Seen.Add((entIndex, steamId));
    }

    /// <summary>A hosted hub whose player service tells <paramref name="joins"/>; client 3 is ready to join.</summary>
    private static (World W, PlayerService Players, FakeClients Clients) Joining(List<IHeroJoinHook> joins)
    {
        var w = Host(HostingMode.Hosted);
        w.Link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot("hub", 0)));
        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");
        var clients = new FakeClients();
        clients.Slots[3] = (76561197960287930UL, "203.0.113.7:27005");
        var sheets = new HeroSheets(new TF2DescentRules(), w.Host, clients, w.Log);
        var players = new PlayerService(w.Link, w.Host, clients, new ClientMessages(new RecordingEngine()), new NeverScheduler(), w.Log, sheets,
                                        new HeroRig.ListHooks<IHeroJoinHook>(joins));
        return (w, players, clients);
    }

    [Fact]
    public void A_join_tells_the_join_hooks_who_the_client_is_to_the_host()
    {
        var seen = new JoinRecorder();
        var (_, players, _) = Joining([seen]);

        players.OnClientActive(3);

        Assert.Equal([(3, Joiner)], seen.Seen);
    }

    [Fact]
    public void A_refused_join_tells_the_join_hooks_nothing()
    {
        var seen = new JoinRecorder();
        var (w, players, _) = Joining([seen]);
        w.Link.JoinAnswer = new JoinOutcome(false, "no character: create one in town first");

        players.OnClientActive(3);

        Assert.Empty(seen.Seen);
    }

    [Fact]
    public void A_hosted_hero_is_loaded_into_the_item_ledger_as_his_join_answers()
    {
        var rules = new TF2DescentRules();
        var host = new FakeHostItems(rules);
        host.AddCharacter(Joiner, rules.Heroes.New("soldier", "a"));
        foreach (var k in rules.StartingKit("soldier", rules.SheetCodec.Write(rules.Heroes.New("soldier", "a")), 1))
            host.Carry(Joiner, rules.ItemRules.Codec.Instance(k.Item), (HostContainer)(int)k.Container, k.Slot);
        var joins = new List<IHeroJoinHook>();
        var (w, players, clients) = Joining(joins);
        var hosted = new HostedItemLedger(rules, new HeroRig.ListHooks<IItemLedgerHook>([]), host, w.Log);
        var memory = new InMemoryItemLedger(rules, new HeroRig.ListHooks<IItemLedgerHook>([]), new StartingKit(rules));
        var ledger = new ItemLedger(w.Host, memory, hosted, new HeroKeys(w.Host, players, clients), w.Log);
        joins.Add(ledger);
        Assert.Null(ledger.Find(Joiner));

        players.OnClientActive(3);

        Assert.Equal(["melee", "primary", "secondary"], ledger.Find(Joiner)!.Inventory.Equipped.Keys.Order(StringComparer.Ordinal));
    }
}
