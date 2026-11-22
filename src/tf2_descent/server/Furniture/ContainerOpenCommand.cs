using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Loot;

namespace TF2.Descent.Server.Furniture;

/// <summary>
/// <c>tf2_descent_container_open &lt;player slot&gt; &lt;container number&gt;</c>: the player opens
/// container <c>number</c> of this level as the item ledger knows it, with no furniture row
/// (dev; cheats). The ledger is asked for his share under that number
/// (<see cref="IItemLedger.ContainerDrop"/> with <see cref="ContainerDrop.Seq"/>): hosted, the host
/// replays the rules' container roll and mints it for him once (a second open is refused
/// <c>already_opened</c>, which the furniture's own once-only state never lets the game ask);
/// unhosted, the ledger rolls the rules' hosted container roll for him. What it gives lies at his
/// feet as a seat of his own, for <c>tf2_descent_take</c>.
/// </summary>
[ConCommand("tf2_descent_container_open", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_container_open <player slot> <container number>: open a numbered container for the player through the item ledger (hosted: the host's OpenContainer)")]
public sealed class ContainerOpenCommand(IItemLedger ledger, ILootParty party, LootFloor floor, FurnitureService furniture, TF2DescentRules rules, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(0, 1_000_000)] int number)
    {
        if (party.HeroOf(slot) is not { } hero || party.Origin(slot) is not { } at || ledger.Find(hero) is not { } held)
        {
            log.Warning($"[TF2Descent/Furniture] tf2_descent_container_open: no living hero with items in slot {slot}\n");
            return;
        }

        uint seq = (uint)number;
        int depth = Math.Max(1, furniture.Depth);
        var request = rules.Loot.HostedContainer(furniture.Seed, seq, held.Sheet.Name, held.Sheet.ClassId);
        var roll = rules.Loot.Container(request, depth);
        var drop = new ContainerDrop(hero, LootRules.ContainerName(seq), request.Seed, roll.Item ? roll.Tier : -1,
                                     roll.Outcome == LootOutcome.Potion ? roll.Potion : null, depth, at) { Seq = seq };
        _ = Open(slot, hero, seq, at, ledger.ContainerDrop(drop));
    }

    private async Task Open(int slot, string hero, uint seq, Vector at, Task<DropResult> asked)
    {
        var answer = await asked;
        if (!answer.Ok)
        {
            log.Warning($"[TF2Descent/Furniture] container {seq} opened by player {slot} ({ledger.Kind}): refused, {answer.Refusal}\n");
            return;
        }

        var placed = answer.Items.Count > 0 ? floor.Place(at, [new LootShare(hero, slot, answer.Items[0], 0)]) : null;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] container {seq} opened by player {slot} ({ledger.Kind}): " +
            $"{(answer.Items.Count == 0 ? "nothing" : string.Join(", ", answer.Items.Select(i => $"{i.Item.BaseId} rarity {i.Item.Rarity}")))}" +
            $"{(placed is { } d ? $", drop {d.Number} at ({at.X:0} {at.Y:0} {at.Z:0})" : "")}\n"));
    }
}
