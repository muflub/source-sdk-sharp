using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Server.Gear;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.OwnerViews;

/// <summary>
/// THE HERO'S OWN VERBS FOR THE SHEET AND THE INVENTORY (plan_descent_logic
/// UI3): what the character sheet's and the inventory's buttons send, each
/// acting for the player who sent it and no other, each decided by the rules
/// on the server:
/// <list type="bullet">
/// <item><c>tf2_descent_spend &lt;attribute&gt; &lt;count&gt;</c>: points into an
/// attribute, through <see cref="HeroRules.Spend"/> and the hero's audited
/// apply (<see cref="HeroComponent.Apply"/>): an audit refusal changes nothing.</item>
/// <item><c>tf2_descent_item_equip &lt;backpack index&gt; [equipment slot]</c>: the
/// item into the slot its base takes (the first free one of a trinket's two)
/// unless one is named, through <see cref="HeroEquipment.Equip"/>.</item>
/// <item><c>tf2_descent_item_unequip &lt;equipment slot&gt;</c>: back into the backpack.</item>
/// <item><c>tf2_descent_item_move &lt;from&gt; &lt;to&gt;</c>: a backpack item to
/// another backpack cell (an occupied one swaps).</item>
/// <item><c>tf2_descent_item_drop &lt;backpack index&gt;</c>: onto the floor at the hero's feet.</item>
/// </list>
/// A refusal is told to the hero as a notice (<c>#Descent_Refused_&lt;reason&gt;</c>,
/// the item's or attribute's name as its argument). Anything not a hero's passes on.
/// </summary>
[SideService(Side.Server)]
public sealed class OwnerClientCommands(
    Heroes heroes,
    HeroKeys keys,
    HeroEquipment equipment,
    IItemLedger ledger,
    IEngineEntities entities,
    ClientMessages messages,
    TF2DescentRules rules,
    ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string Spend = "tf2_descent_spend";
    public const string Equip = "tf2_descent_item_equip";
    public const string Unequip = "tf2_descent_item_unequip";
    public const string Move = "tf2_descent_item_move";
    public const string Drop = "tf2_descent_item_drop";

    /// <summary>In TF2 Descent's owner-views block (UI3).</summary>
    public const int Order = ModStages.First + 1130;

    public int HookOrder => Order;

    /// <summary>The refusals the language file words (<c>#Descent_Refused_&lt;code&gt;</c>); any other is told as <c>#Descent_Refused</c> with the code.</summary>
    public static IReadOnlyList<string> WordedRefusals =>
    [
        InventoryRefusals.BackpackFull, InventoryRefusals.StashFull, InventoryRefusals.WithdrawOnly, InventoryRefusals.NoItem,
        InventoryRefusals.NoSuchSlot, InventoryRefusals.WrongSlot, InventoryRefusals.WrongClass, InventoryRefusals.Level,
        InventoryRefusals.Requirement, InventoryRefusals.Unidentified, InventoryRefusals.Unusable, InventoryRefusals.Occupied,
        InventoryRefusals.NotStackable, LedgerRefusals.NoHero,
        nameof(SpendRefusal.NoPoints), nameof(SpendRefusal.AtCap), nameof(SpendRefusal.UnknownAttribute), nameof(SpendRefusal.BadCount),
    ];

    /// <summary>The notice's token for a refusal code.</summary>
    public static string TokenOf(string code) =>
        WordedRefusals.Contains(code) ? "#Descent_Refused_" + code : "#Descent_Refused";

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command is not (Spend or Equip or Unequip or Move or Drop))
            return false;
        int index = entities.GetIndex(player);
        if (heroes.Of(index) is not { } hero)
            return false;

        switch (command)
        {
            case Spend when args.Length >= 2 && Int(args[1]) is int count:
                SpendPoints(hero, args[0], count);
                break;
            case Equip when args.Length >= 1 && Int(args[0]) is int from:
                EquipItem(index, from, args.Length >= 2 ? args[1] : "");
                break;
            case Unequip when args.Length >= 1:
                _ = Answer(index, Unequip, args[0], equipment.Unequip(index, args[0]), NameIn(index, args[0]));
                break;
            case Move when args.Length >= 2 && Int(args[0]) is int from && Int(args[1]) is int to:
                _ = keys.Of(index) is { } key
                    ? Answer(index, Move, $"{from} {to}", ledger.Move(key, from, to), NameAt(index, from))
                    : Task.CompletedTask;
                break;
            case Drop when args.Length >= 1 && Int(args[0]) is int slot:
                _ = keys.Of(index) is { } dropper
                    ? Answer(index, Drop, args[0], ledger.Drop(dropper, slot, entities.GetOrigin(player)), NameAt(index, slot))
                    : Task.CompletedTask;
                break;
        }
        return true;
    }

    /// <summary>Points into an attribute through the rules and the audited apply; a refusal is told and changes nothing.</summary>
    public SpendResult? SpendPoints(HeroComponent hero, string attribute, int count)
    {
        if (hero.Sheet is not { } sheet)
            return null;
        var result = rules.Heroes.Spend(sheet, attribute, count);
        string name = rules.Content.Attributes.TryGet(attribute, out var row) ? row.Name : attribute;
        if (!result.Ok)
        {
            Tell(hero.PlayerIndex, result.Refusal!.Value.ToString(), name);
            log.Msg($"[TF2Descent/Sheet] {hero.PlayerIndex} spend {count} {attribute} refused: {result.Refusal}\n");
            return result;
        }

        var audit = hero.Apply(s => rules.Heroes.Spend(s, attribute, count).Sheet);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Sheet] {hero.PlayerIndex} spent {result.Spent} on {attribute}: base {hero.Sheet!.Base(attribute)}, " +
            $"points {hero.Sheet!.AttributePoints}, audit {(audit.Ok ? "ok" : audit.Rule)}\n"));
        if (!audit.Ok)
            Tell(hero.PlayerIndex, audit.Rule ?? "refused", name);
        return audit.Ok ? result : result with { Refusal = SpendRefusal.BadCount, Sheet = sheet, Spent = 0 };
    }

    private void EquipItem(int index, int from, string named)
    {
        if (equipment.HeroOf(index) is not { } key || ledger.Find(key) is not { } h)
        {
            Tell(index, LedgerRefusals.NoHero, "");
            return;
        }
        if (!h.Inventory.Backpack.TryGetValue(from, out var held))
        {
            Tell(index, InventoryRefusals.NoItem, "");
            return;
        }
        string slot = named.Length > 0 ? named : SlotFor(h, rules.Content.ItemBases[held.Item.BaseId].Slot);
        _ = Answer(index, Equip, $"{from} {slot}", equipment.Equip(index, from, slot), NameOf(held.Item));
    }

    /// <summary>The equipment slot that takes a base's slot: the first free one, else the first.</summary>
    private string SlotFor(LedgerHero h, string accepts)
    {
        var slots = rules.Content.InventorySettings.Equipment.Where(e => e.Accepts == accepts).ToList();
        if (slots.Count == 0)
            return accepts;
        return (slots.FirstOrDefault(e => !h.Inventory.Equipped.ContainsKey(e.Id)) ?? slots[0]).Id;
    }

    private async Task Answer(int index, string verb, string what, Task<LedgerResult> operation, string name)
    {
        var r = await operation;
        if (r.Ok)
        {
            log.Msg($"[TF2Descent/Items] {index} {verb} {what}: done\n");
            return;
        }
        log.Msg($"[TF2Descent/Items] {index} {verb} {what} refused: {r.Refusal}\n");
        Tell(index, r.Refusal ?? "refused", name);
    }

    private void Tell(int index, string code, string name)
    {
        string token = TokenOf(code);
        messages.Notice(index, token, token == "#Descent_Refused" ? code : name);
    }

    private string NameOf(TF2.Descent.Rules.Items.ItemInstance item) =>
        item.Unique is { } unique && rules.Content.Uniques.TryGet(unique, out var u) ? u.Name
        : rules.Content.ItemBases.TryGet(item.BaseId, out var b) ? b.Name : item.BaseId;

    private string NameAt(int index, int backpack) =>
        keys.Of(index) is { } key && ledger.Find(key)?.Inventory.Backpack.GetValueOrDefault(backpack) is { } held ? NameOf(held.Item) : "";

    private string NameIn(int index, string slot) =>
        keys.Of(index) is { } key && ledger.Find(key)?.Inventory.Equipped.GetValueOrDefault(slot) is { } held ? NameOf(held.Item) : slot;

    private static int? Int(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
}
