using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Screens;

// The crafting bench from fakes (the ui-screen loop's sample step): the states
// a bench is ever in. Item/base/affix names are ui.Data (game copy, case kept);
// every #Descent_* token lives in game/mod_tf2_descent/resource/descent_english.txt.

public static class CraftBenchSamples
{
    /// <summary>The bench from fakes; windows placed from <paramref name="store"/> (empty: the default size).</summary>
    public static UiScreen Build(UiSampleServices s, FakeCraft craft, MemoryWindowStore? store = null,
                                 FakeCraftActions? actions = null, FakeCraftShortage? shortage = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add<IDescentCraft>(craft);
        if (actions is not null)
            s.Add<IDescentCraftActions>(actions);
        if (shortage is not null)
            s.Add<IDescentCraftShortage>(shortage);
        return s.Create<CraftBench>();
    }

    /// <summary>The five stances the interface names, with the scrolled bench under the reroll stance.</summary>
    public static FakeCraft Bench() => new()
    {
        Tabs = ["#Descent_Craft_Tab_Base", "#Descent_Craft_Tab_Reroll", "#Descent_Craft_Tab_Upgrade",
                "#Descent_Craft_Tab_Potions", "#Descent_Craft_Tab_Repair"],
        Selected = 1,
        InputsToken = "#Descent_Craft_Inputs_Reroll",
        PreviewToken = "#Descent_Craft_Preview_Scattergun",
        Item = new DescentItemView("Scattergun", DescentRarity.Vintage, 24, true, 80, 100, 3, false, false, false, false),
        Affixes = [new DescentAffixLine("Blast Damage", 14),
                   new DescentAffixLine("Health On Kill", 42),
                   new DescentAffixLine("Fire Damage", 9),
                   new DescentAffixLine("Move Speed", 7)],
        Locked = [false, false, true, true],
        LockedCount = 2,
        RerollCount = 2,
        RerollCost = 1_600,
        Scrap = 240,
        Reclaimed = 91,
        Refined = 12,
        CanCraft = true,
    };

    [UiSample("reroll: two locked, one open")]
    private static UiScreen RerollTwoLockedOneOpen(UiSampleServices s)
        => Build(s, Bench(), null, new FakeCraftActions());

    [UiSample("combine potions, no item under work")]
    private static UiScreen CombinePotions(UiSampleServices s)
        => Build(s, Bench() with { Selected = 3, Item = null, Locked = [false, false, true, true] },
                 null, new FakeCraftActions());

    /// <summary>Salvage stance — the repair-kit tab's other face: a doomed
    /// (broken) piece goes on the bench, nothing of it is kept (no lockable
    /// affix rows), and the preview names the metal stack the bench pays back
    /// (the server's Salvage rule: one Scrap per rung).</summary>
    [UiSample("salvage: broken piece, pays metal back")]
    private static UiScreen SalvageStance(UiSampleServices s)
        => Build(s, Bench() with
        {
            Selected = 4,
            InputsToken = "#Descent_Craft_Inputs_Salvage",
            PreviewToken = "#Descent_Craft_Preview_Salvage",
            Item = new DescentItemView("Worn Boots", DescentRarity.Stock, 12, true, 0, 14, 2, false, true, false, false),
            Affixes = [],
            Locked = [],
            LockedCount = 0,
            RerollCount = 0,
            RerollCost = 0,
            CanCraft = true,
        }, null, new FakeCraftActions());

    [UiSample("not enough metals: Craft disabled, stacks in Danger")]
    private static UiScreen InsufficientMetals(UiSampleServices s)
        => Build(s, Bench() with { Scrap = 10, Reclaimed = 4, Refined = 0, CanCraft = false },
                 null, new FakeCraftActions(),
                 new FakeCraftShortage { ScrapShort = 30, ReclaimedShort = 1, RefinedShort = 3 });

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Build(s, Bench(), new MemoryWindowStore().With("craftbench", new UiWindowPlacement(200, 120, 640, 420)));
}

/// <summary>The bench the samples choose: stances, the item under work, its affix lines, the three stacks.</summary>
public sealed record FakeCraft : IDescentCraft
{
    public string[] Tabs { get; init; } = [];
    public int Selected { get; init; }
    public string InputsToken { get; init; } = "#Descent_Craft_Inputs_Reroll";
    public string PreviewToken { get; init; } = "#Descent_Craft_Preview_Scattergun";
    public DescentItemView? Item { get; init; }
    public DescentAffixLine[] Affixes { get; init; } = [];
    public bool[] Locked { get; init; } = [];
    public int LockedCount { get; init; }
    public int RerollCount { get; init; }
    public long RerollCost { get; init; }
    public int Scrap { get; init; }
    public int Reclaimed { get; init; }
    public int Refined { get; init; }
    public bool CanCraft { get; init; }

    public int RecipeTabCount => Tabs.Length;

    public string RecipeTabToken(int i) => Tabs[i];

    public int RecipeTabIndex => Selected;

    public DescentItemView? ItemUnderTest => Item;

    public int AffixLineCount => Affixes.Length;

    public DescentAffixLine AffixLine(int i) => Affixes[i];

    public bool RerollAffixLocked(int i) => Locked[i];
}

/// <summary>Records which bench commands fired (the preview cannot click; the wiring test drives them).</summary>
public sealed class FakeCraftActions : IDescentCraftActions
{
    public int RecipeTabSelected { get; private set; } = -1;
    public int ToggledLock { get; private set; } = -1;
    public int Crafts { get; private set; }

    public void SelectRecipeTab(int i) => RecipeTabSelected = i;

    public void ToggleLock(int affixIndex) => ToggledLock = affixIndex;

    public void Craft() => Crafts++;
}

/// <summary>What the sample bench is short of, per stack.</summary>
public sealed record FakeCraftShortage : IDescentCraftShortage
{
    public int ScrapShort { get; init; }
    public int ReclaimedShort { get; init; }
    public int RefinedShort { get; init; }
}
