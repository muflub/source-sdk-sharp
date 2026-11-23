using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Furniture;
using TF2.Descent.Server.Loot;

namespace TF2.Descent.Server.Items;

/// <summary>
/// <c>tf2_descent_breakable_open &lt;player slot&gt; &lt;breakable number&gt;</c>: the player breaks
/// breakable <c>number</c> of this level as the item ledger knows it, with no crate standing (dev;
/// cheats). The ledger's <see cref="IItemLedger.Break"/> under that number: hosted, the host
/// replays the rules' <c>BreakableDrops</c> for him once (a second break is refused
/// <c>already_broken</c>, which the furniture's own once-only state never lets the game ask), credits
/// its Australium to him and mints its items for him; unhosted, the rules' roll. What it gives lies
/// at his feet as a seat of his own, for <c>tf2_descent_take</c>.
/// </summary>
[ConCommand("tf2_descent_breakable_open", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_breakable_open <player slot> <breakable number>: break a numbered breakable for the player through the item ledger (hosted: the host's Break)")]
public sealed class BreakableOpenCommand(IItemLedger ledger, ILootParty party, LootFloor floor, FurnitureService furniture, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(0, 1_000_000)] int number)
    {
        if (party.HeroOf(slot) is not { } hero || party.Origin(slot) is not { } at || ledger.Find(hero) is null)
        {
            log.Warning($"[TF2Descent/Items] tf2_descent_breakable_open: no living hero with items in slot {slot}\n");
            return;
        }

        uint seq = (uint)number;
        _ = Break(slot, hero, seq, at, ledger.Break(new BreakDrop(hero, seq, furniture.Seed, Math.Max(1, furniture.Depth), at)));
    }

    private async Task Break(int slot, string hero, uint seq, Vector at, Task<BreakAnswer> asked)
    {
        var answer = await asked;
        if (!answer.Ok)
        {
            log.Warning($"[TF2Descent/Items] breakable {seq} broken by player {slot} ({ledger.Kind}): refused, {answer.Refusal}\n");
            return;
        }

        var placed = answer.Items.Count > 0 ? floor.Place(at, [new LootShare(hero, slot, answer.Items[0], 0)]) : null;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Items] breakable {seq} broken by player {slot} ({ledger.Kind}): {answer.Australium} Australium credited, " +
            $"{(answer.Items.Count == 0 ? "no item" : string.Join(", ", answer.Items.Select(i => $"{i.Item.BaseId} rarity {i.Item.Rarity}")))}" +
            $"{(placed is { } d ? $", drop {d.Number} at ({at.X:0} {at.Y:0} {at.Z:0})" : "")}; purse {ledger.Find(hero)?.Inventory.Australium}\n"));
    }
}
