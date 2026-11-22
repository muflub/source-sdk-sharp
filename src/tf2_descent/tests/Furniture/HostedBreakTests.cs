using SourceSharp.Abi;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Server.Items;
using TF2.Descent.Tests.Items.Ledger;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// A crate or barrel broken on a hosted level (SDK 2.8 <c>Break</c>): numbered by its slot in the
/// breakables' entity, its outcome keyed on (instance seed, slot, depth) as the host's
/// <c>BreakableDrops</c> keys it, and asked of the host once for each hero in its room, the breaker
/// first (R4), each paid his own roll.
/// </summary>
public sealed class HostedBreakTests
{
    private readonly FakeHostItems _host = new(FurnitureRig.Rules);
    private readonly HostedItemLedger _ledger;
    private readonly FurnitureRig _rig;

    public HostedBreakTests()
    {
        _host.AddCharacter(FurnitureRig.A, FurnitureRig.Rules.Heroes.New("soldier", "a"));
        _host.AddCharacter(FurnitureRig.B, FurnitureRig.Rules.Heroes.New("medic", "b"));
        _ledger = new HostedItemLedger(FurnitureRig.Rules, new TF2.Descent.Tests.Floor.NoLedgerHooks(), _host, new NullLog());
        _rig = new FurnitureRig(level: FurnitureRig.D1S1Breakables, ledger: _ => _ledger);
        _host.InstanceSeed = _rig.Service.Seed;
        _host.Depth = _rig.Service.Depth;
        int slot = 0;
        foreach (var row in _rig.Service.Rows.Where(r => r.Kind == FurnitureKind.Breakable))
            row.BreakableSlot = slot++;   // as the breakables' entity numbers them
    }

    private sealed class NullLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private BreakOutcome Hosted(FurnitureRow row) => FurnitureRig.Rules.Furniture.Breaks.Hosted(_rig.Service.Seed, (uint)row.BreakableSlot!.Value, _rig.Service.Depth);

    /// <summary>The first crate or barrel whose hosted outcome is <paramref name="outcome"/>, both heroes standing at it.</summary>
    private (FurnitureRow Row, FurnitureHero Breaker) At(BreakOutcome outcome)
    {
        var row = _rig.Service.Rows.FirstOrDefault(r => r.Kind == FurnitureKind.Breakable && Hosted(r) == outcome)
                  ?? throw new InvalidOperationException($"no breakable of the level rolls {outcome} hosted");
        var breaker = _rig.Stand(FurnitureRig.SlotA, row);
        _rig.Stand(FurnitureRig.SlotB, row);
        return (row, breaker);
    }

    private async Task<BreakOutcome?> Break(FurnitureRow row, FurnitureHero by)
    {
        Assert.True(_rig.Service.Strike(row, by.Player, 10));
        return await _rig.Service.Break(row);
    }

    [Fact]
    public async Task A_hosted_break_rolls_the_outcome_of_the_instance_its_slot_and_the_depth()
    {
        var rows = _rig.Service.Rows.Where(r => r.Kind == FurnitureKind.Breakable).ToList();
        int differ = 0;
        foreach (var row in rows)
        {
            var outcome = await Break(row, _rig.Stand(FurnitureRig.SlotA, row));
            Assert.Equal(Hosted(row), outcome);
            if (outcome != FurnitureRig.Rules.Furniture.Breaks.Roll(_rig.Service.Seed, row.Placed.Marker).Outcome)
                differ++;
        }
        Assert.True(differ > 0, "the stimulus: some breakable's hosted outcome is not its unhosted one");
    }

    [Fact]
    public async Task A_hosted_break_is_asked_of_the_host_once_for_each_hero_in_its_room_the_breaker_first()
    {
        var (row, breaker) = At(BreakOutcome.Nothing);
        _host.Calls.Clear();

        await Break(row, breaker);

        Assert.Equal([$"break {FurnitureRig.A}", $"break {FurnitureRig.B}"], _host.Calls.Where(c => c.StartsWith("break ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Each_hero_is_paid_his_own_australium_by_the_host()
    {
        var ledger = _ledger;
        await ledger.Load(FurnitureRig.A);
        await ledger.Load(FurnitureRig.B);
        var (row, breaker) = At(BreakOutcome.Australium);

        await Break(row, breaker);

        long a = Roll(row, FurnitureRig.A).Australium, b = Roll(row, FurnitureRig.B).Australium;
        Assert.True(a > 0 && b > 0, "the stimulus: both heroes' rolls hold Australium");
        Assert.Equal((a, b), (_host.Australium[FurnitureRig.A], _host.Australium[FurnitureRig.B]));
        Assert.Equal((a, b), (ledger.Find(FurnitureRig.A)!.Inventory.Australium, ledger.Find(FurnitureRig.B)!.Inventory.Australium));
        Assert.Empty(_rig.Floor.Drops);   // credited at once: no Australium lies on the floor
    }

    [Fact]
    public async Task Each_heros_items_lie_on_his_own_seat()
    {
        var (row, breaker) = At(BreakOutcome.Item);

        await Break(row, breaker);

        var seats = _rig.Floor.Drops.SelectMany(d => d.Seats).ToList();
        Assert.Equal([FurnitureRig.A, FurnitureRig.B], seats.Select(s => s.Hero).Order());
        Assert.All(seats, s => Assert.Equal(s.Hero, s.Item!.ForHero));
        Assert.All(seats, s => Assert.Equal(Roll(row, s.Hero).Items[0].Seed, s.Item!.Item.Seed));
    }

    [Fact]
    public async Task A_bad_outcome_is_broken_on_the_host_too_and_pays_nothing()
    {
        var (row, breaker) = At(BreakOutcome.Blast);
        _host.Calls.Clear();

        await Break(row, breaker);

        Assert.Equal(2, _host.Calls.Count(c => c.StartsWith("break ", StringComparison.Ordinal)));
        Assert.NotEmpty(_rig.World.Explosions);
        Assert.Equal((0L, 0L), (_host.Australium[FurnitureRig.A], _host.Australium[FurnitureRig.B]));
    }

    private SourceSharp.Host.Contracts.BreakableRoll Roll(FurnitureRow row, string hero) =>
        FurnitureRig.Rules.BreakableDrops(_host.InstanceSeed, (uint)row.BreakableSlot!.Value, _host.Depth,
            FurnitureRig.Rules.SheetCodec.Write(_host.Sheets[hero]));
}
