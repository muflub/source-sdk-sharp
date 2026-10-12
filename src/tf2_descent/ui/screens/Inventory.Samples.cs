using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

// The inventory window from fakes (the ui-screen loop's sample step): the
// states plan §2.4 names, built through the same container the game uses.
// Item names are ui.Data (game copy, case kept); every #Descent_* token lives
// in game/mod_tf2_descent/resource/descent_english.txt.

public static class InventorySamples
{
    public static UiScreen Build(UiSampleServices s, FakeInvEquip equip, FakeInvBackpack bag,
        FakeInvHover hover, FakeInvPurse purse, FakeInvBelt belt, FakeInvActions actions)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IDescentEquipment>(equip);
        s.Add<IDescentItemGridSource>(bag);
        s.Add<IDescentItemHover>(hover);
        s.Add<IDescentInvPurse>(purse);
        s.Add<IDescentBelt>(belt);
        s.Add<IDescentItemActions>(actions);
        return s.Create<Inventory>();
    }

    /// <summary>Everything the class may wear, worn: all nine doll slots
    /// earned and filled (the amulet is R29's), a full kit in the bag.</summary>
    [UiSample("full-kit")]
    private static UiScreen FullKit(UiSampleServices s) => Build(s,
        new FakeInvEquip { Earned = [true, true, true, true, true, true, true, true, true], Have = [true, true, true, true, true, true, true, true, true] },
        new FakeInvBackpack(), new FakeInvHover(), new FakeInvPurse(), new FakeInvBelt(), new FakeInvActions());

    /// <summary>A bag full of one unidentified stack: D22 - the slot shows "?"
    /// and the tooltip never shows its affixes.</summary>
    [UiSample("unidentified-stack")]
    private static UiScreen UnidentifiedStack(UiSampleServices s) => Build(s,
        new FakeInvEquip { Have = [true, true, true, true, true, true, true, false, false] },
        new FakeInvBackpack
        {
            Cells =
            [
                Item("Shotgun", DescentRarity.Stock, 4, false, 22, 40),
                Item("Shotgun", DescentRarity.Vintage, 7, false, 30, 40),
                Item("Shotgun", DescentRarity.Stock, 3, false, 12, 40),
                Item("Shotgun", DescentRarity.Vintage, 9, false, 40, 40),
                Item("Shotgun", DescentRarity.Stock, 2, false, 8, 40),
                Item("Shotgun", DescentRarity.Stock, 5, false, 27, 40),
            ],
        },
        new FakeInvHover(), new FakeInvPurse(), new FakeInvBelt(), new FakeInvActions());

    /// <summary>The rare air of the ladder: an Unusual in the bag, and the
    /// amulet wears one too (purple edge, Star chip).</summary>
    [UiSample("unusual")]
    private static UiScreen Unusual(UiSampleServices s) => Build(s,
        new FakeInvEquip
        {
            Earned = [true, true, true, true, true, true, true, true, true],
            Have = [true, true, true, true, true, true, true, false, true],
            Amulet = Item("Amulet", DescentRarity.Unusual, 21, true, 0, 0, 2, true),
        },
        new FakeInvBackpack
        {
            Cells =
            [
                Item("Rocket Launcher", DescentRarity.Unusual, 15, true, 40, 40, 3, true),
                Item("Medigun", DescentRarity.Vintage, 9, true, 24, 40, 1),
                Item("Bottle", DescentRarity.Stock, 1, true, 9, 12),
            ],
        },
        new FakeInvHover(), new FakeInvPurse(), new FakeInvBelt(), new FakeInvActions());

    /// <summary>2d: loot past the rolled capacity - the tail cells dim with a
    /// lock chip, the caption flags the state in the warning tone.</summary>
    [UiSample("overflow")]
    private static UiScreen Overflow(UiSampleServices s) => Build(s,
        new FakeInvEquip { Have = [true, true, true, true, true, true, true, false, false] },
        new FakeInvBackpack
        {
            Cells =
            [
                .. Loose(),
                Item("Medigun", DescentRarity.Vintage, 9, true, 24, 40, 1),
                Item("Fire Axe", DescentRarity.Vintage, 8, true, 20, 40, 1),
                Item("Medigun", DescentRarity.Vintage, 9, true, 24, 40, 1),
                Item("Fire Axe", DescentRarity.Vintage, 8, true, 20, 40, 1),
                Item("Medigun", DescentRarity.Vintage, 9, true, 24, 40, 1),
                Item("Fire Axe", DescentRarity.Vintage, 8, true, 20, 40, 1),
            ],
            Capacity = 8,
            Overflow = true,
        },
        new FakeInvHover(), new FakeInvPurse(), new FakeInvBelt(), new FakeInvActions());

    /// <summary>D21: a weapon worn to nothing - Cross45 chip, the durability
    /// meter at Danger, and the doll wears it so the row reads broken.</summary>
    [UiSample("broken-weapon")]
    private static UiScreen BrokenWeapon(UiSampleServices s) => Build(s,
        new FakeInvEquip
        {
            Have = [true, true, true, true, true, true, true, false, false],
            Primary = Item("Minigun", DescentRarity.Vintage, 12, true, 0, 40, 2, broken: true),
        },
        new FakeInvBackpack(), new FakeInvHover(), new FakeInvPurse(), new FakeInvBelt(), new FakeInvActions());

    /// <summary>The hover projection forces the tooltip open - the ItemTooltip
    /// part over the busy backdrop, with the compare block against the
    /// equipped weapon of its family (R17): deltas green/red.</summary>
    [UiSample("tooltip-open")]
    private static UiScreen TooltipOpen(UiSampleServices s) => Build(s,
        new FakeInvEquip { Have = [true, true, true, true, true, true, true, false, false] },
        new FakeInvBackpack(),
        new FakeInvHover
        {
            Hovering = true,
            Item = Item("Back Scatter", DescentRarity.Vintage, 11, true, 38, 40, 3),
            Family = "scattergun",
            CompareHas = true,
            CompareItem = Item("Back Scatter", DescentRarity.Stock, 5, true, 21, 40, 1),
        },
        new FakeInvPurse(), new FakeInvBelt(), new FakeInvActions());

    private static DescentItemView Item(string name, DescentRarity rarity, int ilvl, bool id,
        int dur, int maxDur, int affixes = 0, bool unusual = false, bool broken = false)
        => new(name, rarity, ilvl, id, dur, maxDur, affixes, unusual, broken, false, false);

    private static DescentItemView?[] Loose() =>
    [
        Item("Shotgun", DescentRarity.Stock, 4, true, 22, 40),
        Item("Medigun", DescentRarity.Vintage, 9, true, 24, 40, 1),
        Item("Bottle", DescentRarity.Stock, 1, true, 9, 12),
        Item("Fire Axe", DescentRarity.Vintage, 8, true, 20, 40, 1),
        Item("Wrench", DescentRarity.Stock, 3, true, 14, 20),
        Item("Revolver", DescentRarity.Vintage, 6, true, 31, 40, 1),
        Item("Sniper Rifle", DescentRarity.Stock, 7, true, 18, 40),
        Item("Knife", DescentRarity.Stock, 2, true, 6, 8),
    ];
}

/// <summary>The doll's nine slots, in the enum's fixed order: weapon, weapon,
/// weapon, hat, body, feet, trinket, trinket, amulet.</summary>
public sealed class FakeInvEquip : IDescentEquipment
{
    public bool[] Earned = [true, true, true, true, true, true, true, false, false];
    public bool[] Have = [true, true, true, true, true, true, false, false, false];
    public DescentItemView? Primary;
    public DescentItemView? Amulet;

    public int SlotCount => 9;
    public DescentEquipSlot Slot(int i) => (DescentEquipSlot)i;
    public bool SlotEarned(int i) => Earned[i];

    public DescentItemView? SlotItem(int i)
    {
        if (i == 0 && Primary is { } primary)
            return primary;
        if (!Have[i])
            return null;
        return i switch
        {
            0 => Item("Minigun", DescentRarity.Vintage, 12, true, 33, 40, 2),
            1 => Item("Shotgun", DescentRarity.Stock, 5, true, 22, 40, 0),
            2 => Item("Bottle", DescentRarity.Stock, 1, true, 7, 12, 0),
            3 => Item("Hat", DescentRarity.Vintage, 6, true, 25, 30, 1),
            4 => Item("Body", DescentRarity.Stock, 4, true, 30, 45, 0),
            5 => Item("Boots", DescentRarity.Stock, 3, true, 12, 20, 0),
            6 => Item("Trinket", DescentRarity.Vintage, 9, true, 0, 0, 1),
            7 => Item("Trinket", DescentRarity.Stock, 2, true, 0, 0, 0),
            _ => Amulet ?? Item("Amulet", DescentRarity.Strange, 18, true, 0, 0, 2),
        };
    }

    private static DescentItemView Item(string name, DescentRarity rarity, int ilvl, bool id,
        int dur, int maxDur, int affixes, bool unusual = false, bool broken = false)
        => new(name, rarity, ilvl, id, dur, maxDur, affixes, unusual, broken, false, false);
}

/// <summary>The backpack's cells.</summary>
public sealed class FakeInvBackpack : IDescentItemGridSource
{
    public DescentItemView?[] Cells =
    [
        new("Shotgun", DescentRarity.Stock, 4, true, 22, 40, 0, false, false, false, false),
        new("Medigun", DescentRarity.Vintage, 9, true, 24, 40, 1, false, false, false, false),
        new("Bottle", DescentRarity.Stock, 1, true, 9, 12, 0, false, false, false, false),
        new("Fire Axe", DescentRarity.Vintage, 8, true, 20, 40, 1, false, false, false, false),
        new("Wrench", DescentRarity.Stock, 3, true, 14, 20, 0, false, false, false, false),
        new("Revolver", DescentRarity.Vintage, 6, true, 31, 40, 1, false, false, false, false),
    ];

    public int Capacity { get; init; } = 12;
    public bool Overflow { get; init; }

    public int CellCount => Cells.Length;
    public DescentItemView? Cell(int i) => i < Cells.Length ? Cells[i] : null;
}

/// <summary>The hover projection; a sample forces it open.</summary>
public sealed class FakeInvHover : IDescentItemHover
{
    public bool Hovering { get; init; }
    public DescentItemView Item { get; init; } = new("Shotgun", DescentRarity.Stock, 5, true, 22, 40, 0, false, false, false, false);
    public string Family { get; init; } = "shotgun";
    public string UnusualEffect { get; init; } = "";
    public bool CompareHas { get; init; }
    public DescentItemView CompareItem { get; init; }

    public int AffixLineCount => Item.AffixCount;
    public DescentAffixLine AffixLine(int i) => i switch
    {
        0 => new("Base Attack Rate", 13),
        1 => new("Bullet Range", 8),
        _ => new("Sentry Damage", -4),
    };
    public string RequirementToken => "#Descent_Req_Brawn";
    public int RequirementValue => 14;
    public bool RequirementMet => true;
    public int CompareAffixCount => CompareHas ? AffixLineCount : 0;
    public DescentAffixLine CompareAffix(int i) => i switch
    {
        0 => new("Base Attack Rate", 7),
        1 => new("Bullet Range", 8),
        _ => new("Sentry Damage", -2),
    };
}

/// <summary>The clicks, recorded so a wiring test can read them back.</summary>
public sealed class FakeInvActions : IDescentItemActions
{
    public int LastHover = -1;
    public int LastUse = -1;
    public int LastFrom = -1;
    public int LastTo = -1;

    public void Hover(int slotOrCell) => LastHover = slotOrCell;
    public void Use(int slotOrCell) => LastUse = slotOrCell;
    public void Move(int fromCell, int toCell) => (LastFrom, LastTo) = (fromCell, toCell);
}

/// <summary>The Australium (D12).</summary>
public sealed class FakeInvPurse : IDescentInvPurse
{
    public long Australium { get; init; } = 1043;
}

/// <summary>The belt strip's four potions and their shared cooldown.</summary>
public sealed class FakeInvBelt : IDescentBelt
{
    public string[] Names = ["Healing Buff", "Vitality", "Vita", ""];
    public int[] Counts = [3, 1, 2, 0];
    public bool SlotReady { get; init; } = true;

    public int SlotCount => 4;
    public string SlotName(int i) => Names[i];
    public int SlotCountOf(int i) => Counts[i];
}
