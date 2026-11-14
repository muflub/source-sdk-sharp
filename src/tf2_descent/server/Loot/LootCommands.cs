using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Inventory;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Loot;

/// <summary><c>tf2_descent_loot</c>: the floor (each drop, its room, entity or data, its seats) and the pool's free count (dev).</summary>
[ConCommand("tf2_descent_loot", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_loot: print the loot on the floor (each drop, its seats, its room, entity or data) and the pool")]
public sealed class LootFloorCommand(LootFloor floor, ILog log)
{
    public void Execute() => log.Msg(floor.Report());
}

/// <summary>
/// <c>tf2_descent_loot_drop &lt;slot&gt; [tier]</c>: a drop at the player's
/// feet as if he had killed a soldier robot there (<see cref="LootRewards.Reward"/>,
/// the whole path: the party near him, the rules' rolls, the ledger, the
/// Australium split, the floor), under a kill number of the dev drops' own.
/// With a tier, every seat's roll is an item of that tier. Named
/// <c>_loot_drop</c>: <c>tf2_descent_drop</c> is the item commands' backpack
/// drop.
/// </summary>
[ConCommand("tf2_descent_loot_drop", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_loot_drop <player slot> [tier 0-4]: roll a kill's loot at the player's feet, as if he had killed a robot there")]
public sealed class LootDropCommand(LootRewards rewards, ILootParty party, ILog log)
{
    /// <summary>The robot a dev drop is rolled as.</summary>
    public const string Robot = "soldier";

    /// <summary>The first dev drop's kill number: far above any level's real kills.</summary>
    public const uint FirstSequence = 900_000;

    private uint _next = FirstSequence;

    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(-1, 4)] int tier = -1)
    {
        if (party.Origin(slot) is not { } at)
        {
            log.Warning($"[TF2Descent/Loot] tf2_descent_loot_drop: no living hero in slot {slot}\n");
            return;
        }

        uint sequence = _next++;
        log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Loot] dev drop {sequence} for slot {slot} at ({at.X:0} {at.Y:0} {at.Z:0}){(tier >= 0 ? " tier " + tier : "")}\n"));
        _ = Report(sequence, rewards.Reward(sequence, Robot, at, slot, ownTable: false, forceTier: tier));
    }

    private async Task Report(uint sequence, Task<LootReward?> reward)
    {
        if (await reward is not { } r)
            log.Msg($"[TF2Descent/Loot] dev drop {sequence}: nothing (no hero near, or the level ended)\n");
        else if (r.Drops.Count == 0)
            log.Msg($"[TF2Descent/Loot] dev drop {sequence}: the rolls dropped nothing\n");
    }
}

/// <summary>
/// <c>tf2_descent_take &lt;slot&gt;</c>: the player takes his seat of the
/// nearest drop within use reach, through the same path a +USE takes
/// (<see cref="LootFloor.Take"/>) (dev).
/// </summary>
[ConCommand("tf2_descent_take", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_take <player slot>: take the player's seat of the nearest drop within reach, as a use does")]
public sealed class LootTakeCommand(LootFloor floor, ILog log)
{
    /// <summary>How far from the player a drop may lie: the use's reach and a little.</summary>
    public const float Reach = 128f;

    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (floor.Nearest(slot, Reach) is not { } drop)
        {
            log.Warning($"[TF2Descent/Loot] tf2_descent_take: no drop of slot {slot}'s within {Reach:0} units\n");
            return;
        }

        _ = Report(slot, drop, floor.Take(drop, slot, touch: false));
    }

    private async Task Report(int slot, FloorDrop drop, Task<TakeOutcome> take) =>
        log.Msg($"[TF2Descent/Loot] tf2_descent_take: slot {slot}, drop {drop.Number}: {await take}\n");
}

/// <summary>
/// <c>tf2_descent_fill_backpack &lt;slot&gt;</c>: mints Stock shovels into the
/// player's backpack through the item ledger until it is full (dev: the
/// refusal of a full backpack, live). Unhosted only, as the dev mint is.
/// </summary>
[ConCommand("tf2_descent_fill_backpack", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_fill_backpack <player slot>: fill the player's backpack with Stock shovels through the item ledger")]
public sealed class FillBackpackCommand(IItemLedger ledger, HeroKeys keys, ILog log)
{
    /// <summary>More than any backpack holds: the fill stops at the first refusal before this.</summary>
    public const int Most = 200;

    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (keys.Of(slot) is not { } hero)
        {
            log.Warning($"[TF2Descent/Loot] tf2_descent_fill_backpack: no hero in slot {slot}\n");
            return;
        }

        _ = Fill(hero);
    }

    private async Task Fill(string hero)
    {
        int given = 0;
        string? refusal = null;
        while (given < Most)
        {
            var r = await ledger.Give(hero, "shovel", 0, 1);
            if (!r.Ok)
            {
                refusal = r.Refusal;
                break;
            }

            given++;
        }

        int count = ledger.Find(hero)?.Inventory.Backpack.Count ?? 0;
        log.Msg($"[TF2Descent/Loot] tf2_descent_fill_backpack {hero}: gave {given}, backpack holds {count}, stopped: {refusal ?? "the cap"}" +
                $"{(refusal == InventoryRefusals.BackpackFull ? " (full)" : "")}\n");
    }
}
