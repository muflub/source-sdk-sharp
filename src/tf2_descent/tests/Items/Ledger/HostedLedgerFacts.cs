using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// The ledger facts against the hosted ledger over a host that keeps items as the hosting
/// service does, and what only it does: the reserve's kill path, the fallback mint, the lease on
/// every call, a failed host call, and the operations the host has no call for.
/// </summary>
public sealed class HostedLedgerFacts : LedgerFacts
{
    private readonly FakeHostItems _host;
    private readonly HostedItemLedger _ledger;
    private uint _kills;

    public HostedLedgerFacts()
    {
        _host = new FakeHostItems(R);
        _host.AddCharacter(A, R.Heroes.New("soldier", "a"));
        _host.AddCharacter(B, R.Heroes.New("soldier", "b"));
        _ledger = new HostedItemLedger(R, new Hooks(Changes), _host, new NullLog());
    }

    private sealed class NullLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    protected override ItemLedgerBase Ledger => _ledger;

    protected override async Task<FloorItem> DropFor(string hero, ItemInstance item)
    {
        _host.Reserve(hero, 0, item);
        var drop = await _ledger.KillDrop(new KillDrop(hero, 0, ++_kills, "soldier", 1, default));
        Assert.True(drop.Ok, drop.Refusal);
        return Assert.Single(drop.Items);
    }

    protected override Task SetAustralium(string hero, long amount)
    {
        _host.Australium[hero] = amount;
        var h = _ledger.Find(hero)!;
        _ledger.Replace(h with { Inventory = h.Inventory with { Australium = amount } });
        return Task.CompletedTask;
    }

    /// <summary>The ledger's backpack, by slot, in the host's ids: what the host holds for the hero, if the two agree.</summary>
    private IReadOnlyList<(int Slot, string Id)> Mirror(string hero) =>
        [.. _ledger.Find(hero)!.Inventory.Backpack.Select(p => (p.Key, _ledger.HostIdOf(p.Value.Id)!)).OrderBy(p => p.Key)];

    private IReadOnlyList<(int Slot, string Id)> Held(string hero) =>
        [.. _host.Owned(HostOwnerKind.Character, hero).Select(i => (i.Slot, i.Id)).OrderBy(p => p.Slot)];

    /// <summary>SDK 2.9 (plan L26): a hosted death leaves the Australium on the character, and the ledger says so.</summary>
    [Fact]
    public async Task A_hosted_death_leaves_the_australium_with_the_hero()
    {
        await _ledger.Load(A);
        await SetAustralium(A, 50);

        Assert.True(await _ledger.RecordDeath(A, default));

        Assert.Equal(50, _host.Australium[A]);   // the host's own shape
        Assert.Equal(50, _ledger.Find(A)!.Inventory.Australium);
    }

    /// <summary>
    /// SDK 2.9 (plan L26): the host mints the class's starting kit to the character in the death's
    /// write; the ledger reads that kit back, so the hero respawns with the host's kit and no other.
    /// </summary>
    [Fact]
    public async Task A_hosted_death_reads_back_the_kit_the_host_minted_and_gives_no_second()
    {
        await _ledger.Load(A);
        var shovel = await Put(A, Roll("shovel"));

        Assert.True(await _ledger.RecordDeath(A, default));

        var kit = _host.Owned(HostOwnerKind.Character, A).Select(i => i.Id).Order().ToList();
        var held = _ledger.Find(A)!.Inventory;
        Assert.Equal(6, kit.Count);   // the stimulus: the host minted the Soldier's kit
        Assert.Equal(kit, held.Items.Select(h => _ledger.HostIdOf(h.Id)).Order());
        Assert.Equal(["rocket_launcher", "shotgun", "shovel"], new[] { "primary", "secondary", "melee" }.Select(s => held.Equipped[s].Item.BaseId));
        Assert.DoesNotContain(held.Items, h => h.Id == shovel);
    }

    [Fact]
    public async Task A_reserve_drop_answers_from_the_reserve_without_waiting_for_the_reveal()
    {
        await _ledger.Load(A);
        _host.Reserve(A, 1, Roll("shovel", tier: 1));
        _host.HoldReveals = true;
        _host.Calls.Clear();

        var drop = _ledger.KillDrop(new KillDrop(A, 1, 3, "soldier", 1, default));

        Assert.True(drop.IsCompletedSuccessfully, "the kill drop waited on the reveal");
        Assert.Single((await drop).Items);
        Assert.Equal([$"next {A}", $"reveal {A}"], _host.Calls);
    }

    [Fact]
    public async Task An_empty_reserve_tier_falls_back_to_minting()
    {
        await _ledger.Load(A);
        _host.Calls.Clear();

        var drop = await _ledger.KillDrop(new KillDrop(A, 2, 4, "soldier", 1, default));

        Assert.True(drop.Ok, drop.Refusal);
        Assert.Equal(A, Assert.Single(drop.Items).ForHero);
        Assert.Equal([$"next {A}", $"mint {A}"], _host.Calls);
    }

    [Fact]
    public async Task Every_host_call_names_the_hero_whose_lease_it_carries()
    {
        await _ledger.Load(A);
        await _ledger.Load(B);
        await SetAustralium(A, 10_000);
        _host.Calls.Clear();

        long id = await Put(A, Roll("rocket_launcher", tier: 1, itemLevel: 5) with { Identified = false });
        await _ledger.Identify(A, SlotOf(A, id));
        await _ledger.Move(A, SlotOf(A, id), 9);
        await _ledger.Deposit(A, 9);
        await _ledger.Withdraw(A, 0);
        await _ledger.Sell(A, "saxton", SlotOf(A, id));

        Assert.True(_host.Calls.Count >= 7, string.Join(", ", _host.Calls));
        Assert.All(_host.Calls, c => Assert.EndsWith(" " + A, c));
    }

    [Fact]
    public async Task A_failed_host_call_changes_nothing_and_says_so()
    {
        await _ledger.Load(A);
        var floor = await DropFor(A, Roll("shovel"));
        var before = Snapshot(A);
        int seen = Changes.Seen.Count;
        _host.Failing.Add("claim");

        var claim = await _ledger.Claim(A, floor.Id);

        Assert.False(claim.Ok);
        Assert.StartsWith(LedgerRefusals.HostPrefix + "Unreachable", claim.Refusal);
        Assert.Contains(_ledger.Floor, f => f.Id == floor.Id);
        Assert.Equal(before, Snapshot(A));
        Assert.Equal(seen, Changes.Seen.Count);
    }

    [Fact]
    public async Task A_full_backpack_is_refused_without_asking_the_host()
    {
        await _ledger.Load(A);
        await Fill(A);
        var floor = await DropFor(A, Roll("shovel"));
        _host.Calls.Clear();

        await _ledger.Claim(A, floor.Id);

        Assert.DoesNotContain(_host.Calls, c => c.StartsWith("claim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Equipping_an_item_the_rules_allow_moves_it_into_its_equip_slot_on_the_host()
    {
        await _ledger.Load(A);
        long id = await Put(A, Roll("rocket_launcher"));

        var equip = await _ledger.Equip(A, SlotOf(A, id), "primary");

        var held = _host.Items[_ledger.HostIdOf(id)!];
        Assert.True(equip.Ok, equip.Refusal);
        Assert.Equal((HostContainer.Equipped, R.EquipSlots.ToList().IndexOf("primary")), (held.Container, held.Slot));
        Assert.Equal(id, _ledger.Find(A)!.Inventory.Equipped["primary"].Id);
    }

    [Fact]
    public async Task A_dev_give_is_refused_as_not_supported_by_the_host()
    {
        await _ledger.Load(A);

        var give = await _ledger.Give(A, "shovel", 0, 1);

        Assert.Equal(LedgerRefusals.HostUnsupported, give.Refusal);
    }

    [Fact]
    public async Task Collecting_Australium_is_refused_as_not_supported_by_the_host_and_asks_nothing()
    {
        await _ledger.Load(A);
        string before = Snapshot(A);
        _host.Calls.Clear();

        var collect = await _ledger.CollectAustralium(A, 37);

        Assert.Equal(LedgerRefusals.HostUnsupported, collect.Refusal);
        Assert.Equal(before, Snapshot(A));
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task A_potion_drop_is_refused_as_not_supported_by_the_host()
    {
        await _ledger.Load(A);
        _host.Calls.Clear();

        var drop = await _ledger.PotionDrop(A, "health_small", 9, 2, default);

        Assert.Equal(LedgerRefusals.HostUnsupported, drop.Refusal);
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task A_drop_with_no_container_number_is_refused_as_not_supported_by_the_host()
    {
        await _ledger.Load(A);
        _host.Calls.Clear();

        var drop = await _ledger.ContainerDrop(new ContainerDrop(A, "m7", 9, 1, null, 1, default));

        Assert.Equal(LedgerRefusals.HostUnsupported, drop.Refusal);
        Assert.Empty(_host.Calls);
        Assert.Empty(_ledger.Floor);
    }

    [Fact]
    public async Task A_salvage_reads_back_every_output_the_host_made()
    {
        await _ledger.Load(A);
        long id = await Put(A, Roll("rocket_launcher", tier: 2, itemLevel: 20));

        var salvage = await _ledger.Salvage(A, SlotOf(A, id));

        Assert.True(salvage.Ok, salvage.Refusal);
        Assert.Equal(Held(A), Mirror(A));
    }

    [Fact]
    public async Task After_a_random_sequence_the_ledger_holds_what_the_host_holds()
    {
        await No_sequence_of_ledger_operations_duplicates_or_loses_an_item();

        Assert.Equal(Held(A), Mirror(A));
        Assert.Equal(Held(B), Mirror(B));
        Assert.Equal(_host.Australium[A], _ledger.Find(A)!.Inventory.Australium);
    }

    [Fact]
    public async Task A_reveal_pays_no_Australium_the_kills_is_its_piles()
    {
        // SDK 2.8 sets australium_pile on every Reveal: the host books nothing there, and neither does the ledger.
        await _ledger.Load(A);
        await SetAustralium(A, 1000);
        _host.Calls.Clear();

        await DropFor(A, Roll("cap"));

        Assert.Contains($"reveal {A}", _host.Calls);
        Assert.Equal((1000L, 1000L), (_host.Australium[A], _ledger.Find(A)!.Inventory.Australium));
    }

    [Fact]
    public async Task A_hero_with_no_lease_here_is_not_loaded()
    {
        var load = await _ledger.Load("76561190000000099");

        Assert.Equal(LedgerRefusals.NoHero, load.Refusal);
    }

    [Fact]
    public async Task Hosted_a_buyers_price_stats_move_nothing_the_host_prices_itself()
    {
        await _ledger.Load(A);
        await SetAustralium(A, 100_000);
        _ledger.SetPrices(A, new TF2.Descent.Rules.Economy.BuyerPrices(-20, 20));

        var offer = (await _ledger.Stock(A, "saxton")).Items[0];
        var buy = await _ledger.Buy(A, "saxton", offer.Id);

        Assert.True(buy.Ok, buy.Refusal);
        Assert.Equal(R.Pricing.Price(offer.Item, SourceSharp.Host.Contracts.PriceKind.Buy), offer.Price);
        Assert.Equal(100_000 - offer.Price, _host.Australium[A]);
        Assert.Equal(_host.Australium[A], _ledger.Find(A)!.Inventory.Australium);
    }
}
