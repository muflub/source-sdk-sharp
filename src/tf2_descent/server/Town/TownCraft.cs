using SourceSharp.Abi;
using SourceSharp.Host.Contracts;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Town;

/// <summary>The bench's recipes, in the order its window lists them (a recipe's number is its place here).</summary>
public enum BenchRecipe
{
    /// <summary><c>craft_item:&lt;base&gt;</c>: a random item of the base of the item under work, at the hero's level, for metal.</summary>
    CraftItem,
    /// <summary><c>reroll:&lt;affix&gt;</c>: one affix of the item under work rolled again, for metal.</summary>
    Reroll,
    /// <summary><c>upgrade</c>: the item under work one rarity up, for metal.</summary>
    Upgrade,
    /// <summary><c>combine_potions</c>: three single potions of one size make one of the next.</summary>
    CombinePotions,
    /// <summary><c>repair_kit</c>: a Repair Kit, for metal.</summary>
    RepairKit,
    /// <summary>Salvage: the item under work into metal.</summary>
    Salvage,
    /// <summary><c>metal_up</c>: three of the lowest rung held into one of the next.</summary>
    MetalUp,
    /// <summary><c>metal_down</c>: one of the highest rung held into three of the one below.</summary>
    MetalDown,
}

/// <summary>A bench recipe as the server will run it: the rules' recipe name, the backpack slots it is given, in order, and its metal cost.</summary>
public sealed record CraftPlan(BenchRecipe Recipe, string Name, IReadOnlyList<int> Slots, int Cost);

/// <summary>
/// The Engineer's bench over the hero's backpack (plan_tf2_rpg 2k, D24; plan_descent_logic WO8):
/// which backpack slots each recipe is given and what it costs, so the window and the verb name
/// the same inputs and the rules decide. The item under work is the hero's first gear in the
/// backpack (the lowest slot); a recipe that costs metal takes every metal stack he carries and
/// gives the change back (the rules' <c>Crafting.Pay</c>).
/// </summary>
[SideService(Side.Server)]
public sealed class TownCraft(TF2DescentRules rules)
{
    /// <summary>How many recipes the bench lists.</summary>
    public const int RecipeCount = 8;

    private DescentContent Content => rules.Content;

    private bool IsMetal(ItemInstance item) => Content.ItemBases[item.BaseId].Kind == ItemKinds.Metal;

    private bool IsGear(ItemInstance item) => Content.ItemBases[item.BaseId].IsGear;

    /// <summary>The backpack slot of the item under work (the first gear); -1 for none.</summary>
    public int Work(LedgerHero h) =>
        h.Inventory.Backpack.Where(p => IsGear(p.Value.Item)).Select(p => p.Key).DefaultIfEmpty(-1).First();

    /// <summary>The metal the hero carries, in scrap units.</summary>
    public int Metal(LedgerHero h) => h.Inventory.Backpack.Values.Sum(v => rules.Crafting.MetalUnits(v.Item));

    private List<int> MetalSlots(LedgerHero h) => [.. h.Inventory.Backpack.Where(p => IsMetal(p.Value.Item)).Select(p => p.Key)];

    /// <summary>
    /// What <paramref name="recipe"/> takes from the hero's backpack now (<paramref name="affix"/>:
    /// a re-roll's affix), or null when he holds nothing it can run on.
    /// </summary>
    public CraftPlan? Plan(LedgerHero h, BenchRecipe recipe, int affix = 0)
    {
        int work = Work(h);
        var e = Content.EconomySettings;
        ItemInstance? item = work >= 0 ? h.Inventory.Backpack[work].Item : null;
        switch (recipe)
        {
            case BenchRecipe.CraftItem when item is not null:
                return new(recipe, $"{e.CraftItemRecipe}:{item.BaseId}", MetalSlots(h), e.CraftItemCost);
            case BenchRecipe.Reroll when item is not null && affix >= 0 && affix < item.Affixes.Length:
                return new(recipe, $"{e.RerollRecipe}:{affix}", [work, .. MetalSlots(h)], rules.Crafting.RerollCost(item, affix));
            case BenchRecipe.Upgrade when item is not null:
                return new(recipe, e.UpgradeRecipe, [work, .. MetalSlots(h)], Content.Rarities.Rows[item.Rarity].UpgradeCost);
            case BenchRecipe.CombinePotions:
            {
                var three = h.Inventory.Backpack
                    .Where(p => p.Value.Item.Count == 1 && Content.ItemBases[p.Value.Item.BaseId].Kind == ItemKinds.Potion)
                    .GroupBy(p => p.Value.Item.BaseId).FirstOrDefault(g => g.Count() >= e.PotionCombine);
                return three is null ? null : new(recipe, e.PotionRecipe, [.. three.Take(e.PotionCombine).Select(p => p.Key)], 0);
            }
            case BenchRecipe.RepairKit:
                return new(recipe, e.RepairKitRecipe, MetalSlots(h), e.RepairKitCost);
            case BenchRecipe.Salvage when item is not null:
                return new(recipe, "salvage", [work], 0);
            case BenchRecipe.MetalUp:
            {
                var low = h.Inventory.Backpack.Where(p => IsMetal(p.Value.Item) && p.Value.Item.Count >= e.MetalRatio)
                    .OrderBy(p => Rung(p.Value.Item)).ThenBy(p => p.Key).Select(p => p.Key).DefaultIfEmpty(-1).First();
                return low < 0 ? null : new(recipe, e.MetalUpRecipe, [low], 0);
            }
            case BenchRecipe.MetalDown:
            {
                var high = h.Inventory.Backpack.Where(p => IsMetal(p.Value.Item) && Rung(p.Value.Item) > 0)
                    .OrderByDescending(p => Rung(p.Value.Item)).ThenBy(p => p.Key).Select(p => p.Key).DefaultIfEmpty(-1).First();
                return high < 0 ? null : new(recipe, e.MetalDownRecipe, [high], 0);
            }
            default:
                return null;
        }
    }

    private int Rung(ItemInstance item)
    {
        var ladder = Content.EconomySettings.MetalLadder;
        for (int i = 0; i < ladder.Count; i++)
            if (ladder[i] == item.BaseId)
                return i;
        return -1;
    }

    /// <summary>
    /// Whether the rules would run <paramref name="plan"/> for the hero now: the recipe (or the
    /// salvage) answers, and its outputs fit his backpack. Nothing changes.
    /// </summary>
    public bool Ready(LedgerHero h, CraftPlan plan)
    {
        var inputs = plan.Slots.Select(s => rules.ItemRules.Codec.ToRolled(h.Inventory.Backpack[s].Item)).ToList();
        var row = rules.LevelTable(1);
        var ctx = new ItemRollContext(h.Sheet.ClassId, h.Sheet.Level, 0, row.ItemLevel, 0, row.LootTable, "craft");
        var change = plan.Recipe == BenchRecipe.Salvage ? rules.Salvage(inputs[0], ctx, 0) : rules.Craft(plan.Name, inputs, ctx, 0);
        if (!change.Ok)
            return false;
        var ids = change.Produced.Select((_, i) => -1L - i).ToList();
        return rules.Inventory.Apply(h.Inventory, h.Stash, h.Sheet, plan.Slots, change, ids).Ok;
    }
}
