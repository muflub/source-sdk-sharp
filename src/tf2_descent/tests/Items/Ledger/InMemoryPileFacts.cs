using SourceSharp.Abi;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// The unhosted ledger's kill pile and numbered breakable, the same rule as the host's (L24): one
/// pile a kill, any of the killer's party may take it, every member credited his share at once by
/// the rules' <c>AustraliumShares</c>; a breakable rolled for each hero once.
/// </summary>
public sealed class InMemoryPileFacts
{
    private static readonly TF2DescentRules R = ItemFixtures.NewRules();
    private const string A = "ann", B = "bob", C = "cat";

    private readonly InMemoryItemLedger _ledger = new(R, new NoHooks(), new StartingKit(R));

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    private long Purse(string hero) => _ledger.Find(hero)!.Inventory.Australium;

    private async Task<AustraliumPile> Pile(long australium)
    {
        var answer = await _ledger.KillAustralium(new KillPile(A, 0, "soldier", 3, australium, default));
        Assert.True(answer.Ok, answer.Refusal);
        return answer.Pile!;
    }

    [Fact]
    public async Task A_kills_pile_is_the_games_roll_on_the_floor()
    {
        var pile = await Pile(31);

        Assert.Equal((31L, A), (pile.Australium, pile.Killer));
        Assert.Equal(pile, Assert.Single(_ledger.Piles));
    }

    [Fact]
    public async Task A_roll_of_no_Australium_makes_no_pile()
    {
        var answer = await _ledger.KillAustralium(new KillPile(A, 0, "soldier", 1, 0, default));

        Assert.Equal(LedgerRefusals.NoAustralium, answer.Refusal);
        Assert.Empty(_ledger.Piles);
    }

    [Fact]
    public async Task Any_member_picks_the_pile_up_and_every_member_is_credited_his_share_at_once()
    {
        var pile = await Pile(31);

        var take = await _ledger.ClaimAustralium(C, pile.Id, [A, B, C]);

        Assert.True(take.Ok, take.Refusal);
        Assert.Equal(LootRules.Split(31, 3), [Purse(A), Purse(B), Purse(C)]);
        Assert.Equal(31, take.Credits.Sum(c => c.Amount));
        Assert.Empty(_ledger.Piles);
    }

    [Fact]
    public async Task A_scavenger_finds_his_share_raised()
    {
        await _ledger.Load(B);
        var bob = _ledger.Find(B)!;
        _ledger.SetSheet(B, bob.Sheet with { SkillRanks = bob.Sheet.SkillRanks.SetItem("scavenger", 5), Level = 15 });
        var pile = await Pile(100);

        await _ledger.ClaimAustralium(A, pile.Id, [A, B]);

        Assert.Equal((50L, LootRules.FoundShare(50, 20)), (Purse(A), Purse(B)));
    }

    [Fact]
    public async Task A_claimer_outside_the_party_is_refused_and_the_pile_stays()
    {
        var pile = await Pile(31);
        await _ledger.Load(C);

        var take = await _ledger.ClaimAustralium(C, pile.Id, [A, B]);

        Assert.Equal(LedgerRefusals.NotInParty, take.Refusal);
        Assert.Equal(0, Purse(C));
        Assert.Single(_ledger.Piles);
    }

    [Fact]
    public async Task A_pile_is_taken_once()
    {
        var pile = await Pile(9);
        Assert.True((await _ledger.ClaimAustralium(A, pile.Id, [A])).Ok);

        var again = await _ledger.ClaimAustralium(A, pile.Id, [A]);

        Assert.Equal(LedgerRefusals.NotOnFloor, again.Refusal);
        Assert.Equal(9, Purse(A));
    }

    [Fact]
    public async Task A_new_level_leaves_the_last_levels_piles_behind()
    {
        var pile = await Pile(9);

        _ledger.NewLevel();

        Assert.Empty(_ledger.Piles);
        Assert.Equal(LedgerRefusals.NotOnFloor, (await _ledger.ClaimAustralium(A, pile.Id, [A])).Refusal);
    }

    [Fact]
    public async Task A_numbered_breakable_is_the_rules_roll_for_each_hero_once()
    {
        await _ledger.Load(A);
        uint seq = Enumerable.Range(0, 500).Select(s => (uint)s)
            .First(s => R.BreakableDrops(42, s, 2, R.SheetCodec.Write(_ledger.Find(A)!.Sheet)).Australium > 0);
        long roll = R.BreakableDrops(42, seq, 2, R.SheetCodec.Write(_ledger.Find(A)!.Sheet)).Australium;

        var first = await _ledger.Break(new BreakDrop(A, seq, 42, 2, default));
        var again = await _ledger.Break(new BreakDrop(A, seq, 42, 2, default));

        Assert.Equal((roll, roll), (first.Australium, Purse(A)));
        Assert.Equal(LedgerRefusals.AlreadyBroken, again.Refusal);
        Assert.Equal(roll, Purse(A));
    }
}
