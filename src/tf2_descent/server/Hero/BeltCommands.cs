using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// The belt's verbs a hero's client sends (plan_descent_logic HE8):
/// <list type="bullet">
/// <item><c>tf2_descent_belt &lt;slot 1-4&gt;</c>: drink from a belt slot (the belt keys).</item>
/// <item><c>tf2_descent_use &lt;backpack slot&gt; [target backpack slot]</c>: use a scroll or a
/// tome (an identify scroll names the item it identifies).</item>
/// </list>
/// Each is claimed for a hero only; anything else passes on.
/// </summary>
[SideService(Side.Server)]
public sealed class BeltClientCommands(HeroBelt belt, HeroConsumables consumables, Heroes heroes, IEngineEntities entities)
    : IClientCommandHook, IOrderedHook
{
    public const string Drink = "tf2_descent_belt";
    public const string UseVerb = "tf2_descent_use";

    /// <summary>TF2 Descent's belt block.</summary>
    public const int Order = ModStages.First + 812;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command is not (Drink or UseVerb))
            return false;
        int index = entities.GetIndex(player);
        if (heroes.Of(index) is null)
            return false;

        switch (command)
        {
            case Drink when args.Length >= 1 && Int(args[0]) is int slot:
                _ = belt.Drink(index, slot);
                break;
            case UseVerb when args.Length >= 1 && Int(args[0]) is int item:
                _ = consumables.Use(index, item, args.Length >= 2 && Int(args[1]) is int target ? target : -1);
                break;
        }

        return true;
    }

    private static int? Int(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
}

/// <summary><c>tf2_descent_belt_as &lt;player&gt; &lt;slot&gt;</c>: the client's <c>tf2_descent_belt</c> for that player, from the server console (sv_cheats).</summary>
[ConCommand("tf2_descent_belt_as", Help = "tf2_descent_belt_as <player slot> <belt slot 1-4>: drink from the belt for the player (sv_cheats).")]
public sealed class BeltDrinkAsCommand(SvCheatsConVar cheats, Heroes heroes, HeroBelt belt, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(1, 4)] int beltSlot)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_belt_as", slot) is null)
            return;
        _ = Report(slot, beltSlot, belt.Drink(slot, beltSlot));
    }

    private async Task Report(int slot, int beltSlot, Task<BeltDrink> drink)
    {
        var r = await drink;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Belt] {slot} tf2_descent_belt_as {beltSlot}: {(r.Ok ? $"drank {r.Potion} +{r.Restored:0.##} {r.Restores}" : "refused " + r.Refusal)}\n"));
    }
}

/// <summary><c>tf2_descent_use_as &lt;player&gt; &lt;backpack slot&gt; [target]</c>: the client's <c>tf2_descent_use</c> for that player (sv_cheats).</summary>
[ConCommand("tf2_descent_use_as", Help = "tf2_descent_use_as <player slot> <backpack slot> [target backpack slot]: use a scroll or tome for the player (sv_cheats).")]
public sealed class UseAsCommand(SvCheatsConVar cheats, Heroes heroes, HeroConsumables consumables, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(0, int.MaxValue)] int backpack, int target = -1)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_use_as", slot) is null)
            return;
        _ = Report(slot, backpack, consumables.Use(slot, backpack, target));
    }

    private async Task Report(int slot, int backpack, Task<ItemUse> use)
    {
        var r = await use;
        log.Msg($"[TF2Descent/Belt] {slot} tf2_descent_use_as {backpack}: {(r.Ok ? "used " + r.Base + ": " + r.Did : "refused " + r.Refusal)}\n");
    }
}

/// <summary>
/// <c>tf2_descent_belt_give &lt;player&gt; &lt;potion&gt; [count]</c>: mints potions through the
/// ledger into the backpack and puts each on the belt (onto the same potion's slot while it
/// has room, else the first empty slot; one that finds no place stays in the backpack) (sv_cheats).
/// </summary>
[ConCommand("tf2_descent_belt_give", Help = "tf2_descent_belt_give <player slot> <potion base> [count]: mint potions onto the belt through the item ledger (sv_cheats).")]
public sealed class BeltGiveCommand(SvCheatsConVar cheats, Heroes heroes, HeroKeys keys, IItemLedger ledger, TF2DescentRules rules, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string potion, [ArgRange(1, 40)] int count = 1)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_belt_give", slot) is null || keys.Of(slot) is not { } key)
            return;
        if (rules.Content.Consumables.Potion(potion) is null)
        {
            log.Warning($"[TF2Descent/Belt] tf2_descent_belt_give: '{potion}' is no potion\n");
            return;
        }
        _ = Give(slot, key, potion, count);
    }

    private async Task Give(int slot, string key, string potion, int count)
    {
        int belted = 0;
        for (int n = 0; n < count; n++)
        {
            var given = await ledger.Give(key, potion, 0, 1);
            if (!given.Ok || given.Change?.Added is not [{ } added, ..])
            {
                log.Warning($"[TF2Descent/Belt] tf2_descent_belt_give {potion}: {given.Refusal}\n");
                break;
            }
            var belt = ledger.Find(key)!.Inventory.Belt;
            int stack = rules.Content.ItemBases[potion].Stack;
            int to = Enumerable.Range(0, rules.Content.InventorySettings.BeltSlots)
                .Where(i => belt.TryGetValue(i, out var h) && h.Item.BaseId == potion && h.Item.Count < stack)
                .Concat(Enumerable.Range(0, rules.Content.InventorySettings.BeltSlots).Where(i => !belt.ContainsKey(i)))
                .DefaultIfEmpty(-1).First();
            if (to >= 0 && (await ledger.ToBelt(key, added.Slot, to)).Ok)
                belted++;
        }
        log.Msg($"[TF2Descent/Belt] {slot} given {count} {potion}, {belted} on the belt\n");
    }
}

/// <summary><c>tf2_descent_belt_print &lt;player&gt;</c>: the hero's belt slot by slot, its cooldown and what was last sent to his client (sv_cheats).</summary>
[ConCommand("tf2_descent_belt_print", Help = "tf2_descent_belt_print <player slot>: print the hero's belt, cooldown and the belt message (sv_cheats).")]
public sealed class BeltPrintCommand(SvCheatsConVar cheats, Heroes heroes, HeroKeys keys, IItemLedger ledger, HeroBelt belt,
                                     BeltSync sync, IBeltWorld world, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_belt_print", slot) is null)
            return;
        if (keys.Of(slot) is not { } key || ledger.Find(key) is not { } held)
        {
            log.Warning($"[TF2Descent/Belt] tf2_descent_belt_print: player {slot} has no items in the ledger\n");
            return;
        }
        log.Msg(Describe(slot, held, belt, sync, world));
    }

    public static string Describe(int slot, LedgerHero held, HeroBelt belt, BeltSync sync, IBeltWorld world)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var (ready, length) = belt.CooldownOf(slot);
        sb.Append(c, $"[TF2Descent/Belt] {slot} belt: cooldown {(ready > 0 ? $"to tick {ready} ({ready - world.Tick} ticks left of {length})" : "ready")}, tick {world.Tick}\n");
        for (int i = 0; i < belt.Slots; i++)
        {
            if (held.Inventory.Belt.TryGetValue(i, out var h))
                sb.Append(c, $"  belt {i + 1}: #{h.Id} {h.Item.BaseId} x{h.Item.Count}\n");
            else
                sb.Append(c, $"  belt {i + 1}: empty\n");
        }
        if (sync.BeltOf(slot) is { } wire)
            sb.Append(c, $"  wire: bases [{wire.Bases}] counts [{string.Join(',', wire.Counts)}] ready {wire.ReadyTick} length {wire.CooldownTicks}\n");
        return sb.ToString();
    }
}
