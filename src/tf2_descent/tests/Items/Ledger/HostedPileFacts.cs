using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Host.Sdk;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// The hosted ledger's SDK 2.8 kill and break calls against <see cref="FakeHostItems"/> (the host's
/// DescentLedger, asking the rules' 1.2 members): a kill's Australium as one pile
/// (<c>KillAustralium</c>), picked up by any of the killer's party and paid to every member on the
/// level at once (<c>ClaimAustralium</c>, the rules' <c>AustraliumShares</c>), and a numbered
/// breakable broken by each hero for his own roll (<c>Break</c>). After each, the ledger's purses are
/// the host's.
/// </summary>
public sealed class HostedPileFacts
{
    private static readonly TF2DescentRules R = ItemFixtures.NewRules();
    private const string A = "76561190000000001";
    private const string B = "76561190000000002";
    private const string C = "76561190000000003";
    private const string Robot = "soldier";

    private readonly FakeHostItems _host = new(R);
    private readonly HostedItemLedger _ledger;

    public HostedPileFacts()
    {
        _host.AddCharacter(A, R.Heroes.New("soldier", "a"));
        _host.AddCharacter(B, R.Heroes.New("soldier", "b"));
        _host.AddCharacter(C, R.Heroes.New("soldier", "c"));
        _ledger = new HostedItemLedger(R, new NoHooks(), _host, new NoLog());
    }

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    private sealed class NoLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private long Purse(string hero) => _ledger.Find(hero)!.Inventory.Australium;

    /// <summary>The first kill whose seat-0 roll for <paramref name="partySize"/> heroes has Australium (or none, with <paramref name="with"/> false), as its kill sequence.</summary>
    private uint KillSeq(int partySize, bool with = true)
    {
        for (uint kill = 1; kill < 2000; kill++)
        {
            uint seq = R.Loot.KillSeq(kill, 0);
            if (R.KillRoll(_host.InstanceSeed, seq, Robot, _host.Depth, partySize).Australium > 0 == with)
                return seq;
        }
        throw new InvalidOperationException("no kill of 2000 rolls as asked");
    }

    private async Task<AustraliumPile> Pile(string killer, int partySize)
    {
        uint seq = KillSeq(partySize);
        var answer = await _ledger.KillAustralium(new KillPile(killer, seq, Robot, partySize, 1, new Vector(1, 2, 3)));
        Assert.True(answer.Ok, answer.Refusal);
        return answer.Pile!;
    }

    //-------------------------------------------------------------------------
    // The pile
    //-------------------------------------------------------------------------

    [Fact]
    public async Task A_kill_with_Australium_mints_one_pile_of_the_hosts_roll_on_the_floor()
    {
        await _ledger.Load(A);
        uint seq = KillSeq(1);

        var answer = await _ledger.KillAustralium(new KillPile(A, seq, Robot, 1, 999_999, default));

        Assert.True(answer.Ok, answer.Refusal);
        long roll = R.KillRoll(_host.InstanceSeed, seq, Robot, _host.Depth, 1).Australium;
        Assert.Equal(roll, answer.Pile!.Australium);
        Assert.Equal(answer.Pile, Assert.Single(_ledger.Piles));
        Assert.True(_host.Items[_ledger.HostIdOf(answer.Pile.Id)!].IsAustraliumPile);
        Assert.Empty(_ledger.Floor);
        Assert.Equal(0, Purse(A));
    }

    [Fact]
    public async Task A_second_pile_of_one_kill_is_refused()
    {
        await _ledger.Load(A);
        uint seq = KillSeq(1);
        Assert.True((await _ledger.KillAustralium(new KillPile(A, seq, Robot, 1, 1, default))).Ok);

        var again = await _ledger.KillAustralium(new KillPile(A, seq, Robot, 1, 1, default));

        Assert.EndsWith(":already_minted", again.Refusal);
        Assert.Single(_ledger.Piles);
    }

    [Fact]
    public async Task A_kill_with_no_Australium_makes_no_pile()
    {
        await _ledger.Load(A);

        var answer = await _ledger.KillAustralium(new KillPile(A, KillSeq(1, with: false), Robot, 1, 5, default));

        Assert.Equal(LedgerRefusals.NoAustralium, answer.Refusal);
        Assert.Empty(_ledger.Piles);
    }

    [Fact]
    public async Task A_pile_picked_up_by_a_party_member_pays_every_member_on_the_level_his_share()
    {
        _host.Party(A, B, C);
        foreach (var h in new[] { A, B, C })
            await _ledger.Load(h);
        var pile = await Pile(A, 3);

        var take = await _ledger.ClaimAustralium(B, pile.Id, [A, B, C]);

        Assert.True(take.Ok, take.Refusal);
        var split = LootRules.Split(pile.Australium, 3);
        Assert.Equal([(A, split[0]), (B, split[1]), (C, split[2])], take.Credits.Select(c => (c.Hero, c.Amount)));
        Assert.Equal(pile.Australium, take.Credits.Sum(c => c.Amount));
        Assert.All(new[] { A, B, C }, h => Assert.Equal(_host.Australium[h], Purse(h)));
        Assert.Empty(_ledger.Piles);
    }

    [Fact]
    public async Task A_pile_is_shared_among_the_members_on_the_level_only()
    {
        _host.Party(A, B, C);
        foreach (var h in new[] { A, B, C })
            await _ledger.Load(h);
        var pile = await Pile(A, 3);
        _host.Elsewhere.Add(C);

        var take = await _ledger.ClaimAustralium(A, pile.Id, [A, B]);

        Assert.True(take.Ok, take.Refusal);
        Assert.Equal([A, B], take.Credits.Select(c => c.Hero));
        Assert.Equal(LootRules.Split(pile.Australium, 2), take.Credits.Select(c => c.Amount));
        Assert.Equal((0L, 0L), (_host.Australium[C], Purse(C)));
    }

    [Fact]
    public async Task A_claimer_outside_the_killers_party_is_refused_and_nobody_is_paid()
    {
        _host.Party(A, B);
        await _ledger.Load(A);
        await _ledger.Load(C);
        var pile = await Pile(A, 1);

        var take = await _ledger.ClaimAustralium(C, pile.Id, [C]);

        Assert.Equal(LedgerRefusals.NotInParty, take.Refusal);
        Assert.Equal((0L, 0L), (_host.Australium[A], _host.Australium[C]));
        Assert.Single(_ledger.Piles);
    }

    [Fact]
    public async Task A_pile_is_taken_once()
    {
        await _ledger.Load(A);
        var pile = await Pile(A, 1);
        Assert.True((await _ledger.ClaimAustralium(A, pile.Id, [A])).Ok);
        _host.Calls.Clear();

        var again = await _ledger.ClaimAustralium(A, pile.Id, [A]);

        Assert.Equal(LedgerRefusals.NotOnFloor, again.Refusal);
        Assert.Empty(_host.Calls);
        Assert.Equal(pile.Australium, Purse(A));
    }

    [Fact]
    public async Task A_pile_credited_while_another_operation_of_the_hero_waits_at_the_host_is_kept()
    {
        await _ledger.Load(A);
        _host.Australium[A] = 1000;
        _ledger.Replace(_ledger.Find(A)! with { Inventory = _ledger.Find(A)!.Inventory with { Australium = 1000 } });
        var item = R.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 0, 1, 2, "base:rocket_launcher", "craft"), 3) with { Identified = false };
        _host.Reserve(A, 0, item);
        var floor = Assert.Single((await _ledger.KillDrop(new KillDrop(A, 0, 1, Robot, 1, default))).Items);
        Assert.True((await _ledger.Claim(A, floor.Id)).Ok);
        var pile = await Pile(A, 1);
        _host.InPlaceGate = new TaskCompletionSource();
        int slot = _ledger.Find(A)!.Inventory.Backpack.Single(p => p.Value.Id == floor.Id).Key;

        var identify = _ledger.Identify(A, slot);                 // waits at the host
        var take = _ledger.ClaimAustralium(A, pile.Id, [A]);     // the host pays the pile meanwhile
        _host.InPlaceGate.SetResult();
        Assert.True((await identify).Ok);
        Assert.True((await take).Ok);

        Assert.Equal(_host.Australium[A], Purse(A));
        Assert.True(Purse(A) > 1000 - R.Pricing.IdentifyFee(item), "the stimulus: the pile paid something");
    }

    //-------------------------------------------------------------------------
    // Breakables
    //-------------------------------------------------------------------------

    /// <summary>The first breakable number whose roll gives both heroes something (Australium or an item).</summary>
    private uint BreakableGiving(Func<BreakableRollView, bool> what)
    {
        for (uint seq = 0; seq < 500; seq++)
        {
            var a = R.BreakableDrops(_host.InstanceSeed, seq, _host.Depth, R.SheetCodec.Write(_host.Sheets[A]));
            var b = R.BreakableDrops(_host.InstanceSeed, seq, _host.Depth, R.SheetCodec.Write(_host.Sheets[B]));
            if (what(new(a.Australium, a.Items.Count)) && what(new(b.Australium, b.Items.Count)))
                return seq;
        }
        throw new InvalidOperationException("no breakable of 500 rolls as asked");
    }

    private sealed record BreakableRollView(long Australium, int Items);

    private BreakDrop Broken(string hero, uint seq) => new(hero, seq, _host.InstanceSeed, _host.Depth, new Vector(4, 5, 6));

    [Fact]
    public async Task Two_heroes_break_one_breakable_and_each_is_paid_his_own_australium()
    {
        await _ledger.Load(A);
        await _ledger.Load(B);
        uint seq = BreakableGiving(r => r.Australium > 0);

        var a = await _ledger.Break(Broken(A, seq));
        var b = await _ledger.Break(Broken(B, seq));

        Assert.True(a.Ok && b.Ok, a.Refusal ?? b.Refusal);
        Assert.True(a.Australium > 0 && b.Australium > 0);
        Assert.Equal((_host.Australium[A], _host.Australium[B]), (Purse(A), Purse(B)));
        Assert.Equal((a.Australium, b.Australium), (Purse(A), Purse(B)));
        Assert.Contains($"break {A}", _host.Calls);
        Assert.Contains($"break {B}", _host.Calls);
    }

    [Fact]
    public async Task A_breakables_items_land_on_the_floor_for_each_hero_and_he_can_claim_his_own()
    {
        await _ledger.Load(A);
        await _ledger.Load(B);
        uint seq = BreakableGiving(r => r.Items == 1);

        var a = await _ledger.Break(Broken(A, seq));
        var b = await _ledger.Break(Broken(B, seq));

        Assert.Equal(A, Assert.Single(a.Items).ForHero);
        Assert.Equal(B, Assert.Single(b.Items).ForHero);
        Assert.Equal(LedgerRefusals.WrongOwner, (await _ledger.Claim(A, b.Items[0].Id)).Refusal);
        Assert.True((await _ledger.Claim(A, a.Items[0].Id)).Ok);
    }

    [Fact]
    public async Task Each_heros_second_break_of_a_breakable_is_refused_even_when_the_first_gave_nothing()
    {
        await _ledger.Load(A);
        await _ledger.Load(B);
        uint seq = BreakableGiving(r => r.Australium == 0 && r.Items == 0);
        Assert.True((await _ledger.Break(Broken(A, seq))).Ok);
        Assert.True((await _ledger.Break(Broken(B, seq))).Ok);

        var againA = await _ledger.Break(Broken(A, seq));
        var againB = await _ledger.Break(Broken(B, seq));

        Assert.Equal((LedgerRefusals.AlreadyBroken, LedgerRefusals.AlreadyBroken), (againA.Refusal, againB.Refusal));
    }
}
