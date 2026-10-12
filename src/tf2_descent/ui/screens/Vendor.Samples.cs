using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

// The vendor from fakes (the ui-screen loop's sample step): one sample per
// skin the VendorKind names — Hale's weapons, the Engineer's armour and his
// repair bench, the medic's consumables, Merasmus's tomes, the Spy's identify
// service with an unidentified row picked, the Scout's crate stall — plus the
// service stance at the window's minimum. Ware names are
// ui.Data (game copy, case kept); every #Descent_* token lives in
// game/mod_tf2_descent/resource/descent_english.txt.

public static class VendorSamples
{
    public static UiScreen Build(UiSampleServices s, FakeWareVendor vendor, FakeWareGrid grid,
        FakeWareHover? wareHover = null, FakeVendorItemHover? itemHover = null, MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add<IDescentVendor>(vendor);
        s.Add<IDescentItemGridSource>(grid);
        s.Add<IDescentVendorActions>(new FakeWareActions());
        // The hint fake answers both keys (the screen's hover subject and the
        // HintTip part's content): one view-model, as the game's DI wires it.
        // The resting samples leave it inactive — an inactive tip draws
        // nothing, which is the pointer-less state. Same for the item
        // tooltip's subject (an inactive tooltip draws nothing).
        s.Add<IDescentVendorHover>(wareHover ??= new FakeWareHover());
        s.Add<IDescentHintTip>(wareHover);
        s.Add<IDescentItemHover>(itemHover ??= new FakeVendorItemHover());
        return s.Create<Vendor>();
    }

    [UiSample("hale-stock")]
    private static UiScreen HaleStock(UiSampleServices s) => Build(s,
        new FakeWareVendor
        {
            Kind = DescentVendorKind.Weapons,
            VendorNameToken = "#Descent_Vendor_Hale",
            StanceToken = "#Descent_Vendor_Stance_Weapons",
            PlayerAustralium = 1200,
            SellTab = true,
            BuyWares =
            [
                Ware("Rocket Launcher", DescentRarity.Strange, 42, 90),
                Ware("Minigun", DescentRarity.Vintage, 31, 55),
                Ware("Deadringer's Repeater", DescentRarity.Unusual, 55, 899, unusual: true),
                Ware("Golden Frying Pan", DescentRarity.Australium, 61, 4200, affordable: false),
                Ware("Scattergun", DescentRarity.Stock, 22, 18),
                Ware("SMG", DescentRarity.Vintage, 27, 40, identified: false),
                Ware("Knife", DescentRarity.Stock, 15, 9),
                Ware("Medigun", DescentRarity.Strange, 40, 160),
                Ware("Shotgun", DescentRarity.Stock, 20, 22, dur: 3, durMax: 8),
            ],
            SellWares =
            [
                Ware("Sold Scattergun", DescentRarity.Stock, 22, 8),
                Ware("Sold Flask", DescentRarity.Vintage, 18, 14, affordable: false),
            ],
        },
        new FakeWareGrid
        {
            Capacity = 24,
            Items =
            [
                Item("Uchigatana", DescentRarity.Unusual, 44, unusual: true, affixes: 3),
                Item("Cow Mangosa", DescentRarity.Strange, 38, dur: 20, durMax: 20, affixes: 2),
                Item("Worn Boots", DescentRarity.Stock, 12, dur: 1, durMax: 14, broken: true),
                Item("Vacillator", DescentRarity.Vintage, 26, dur: 9, durMax: 16, affixes: 1),
                Item("Saxton's Gauntlets", DescentRarity.Strange, 33, affixes: 2),
                Item("Ham", DescentRarity.Stock, 5),
                Item("Crowbar", DescentRarity.Vintage, 17, dur: 6, durMax: 10),
                Item("Backpack Charm", DescentRarity.Vintage, 9, affixes: 1),
            ],
        });

    [UiSample("engineer-armour")]
    private static UiScreen EngineerArmour(UiSampleServices s) => Build(s,
        new FakeWareVendor
        {
            Kind = DescentVendorKind.Armour,
            VendorNameToken = "#Descent_Vendor_Engineer",
            StanceToken = "#Descent_Vendor_Stance_Armour",
            PlayerAustralium = 300,
            SellTab = true,
            BuyWares =
            [
                Ware("Riveted Vest", DescentRarity.Strange, 38, 210),
                Ware("Hard Hat", DescentRarity.Vintage, 24, 60),
                Ware("Shotgunner's Greaves", DescentRarity.Unusual, 47, 780, unusual: true),
                Ware("Sentry Plating", DescentRarity.Australium, 58, 3600, affordable: false),
                Ware("Bone-Plate Belt", DescentRarity.Stock, 16, 26),
                Ware("Welded Buckler", DescentRarity.Vintage, 29, 88, identified: false),
                Ware("Engineer's Goggles", DescentRarity.Stock, 12, 14, dur: 4, durMax: 9),
            ],
            SellWares =
            [
                Ware("Sold Hard Hat", DescentRarity.Vintage, 24, 15),
            ],
        },
        new FakeWareGrid
        {
            Capacity = 24,
            Items =
            [
                Item("Roadside Rig", DescentRarity.Strange, 36, unusual: true, affixes: 3),
                Item("Welded Gauntlets", DescentRarity.Vintage, 22, dur: 11, durMax: 11, affixes: 1),
                Item("Cracked Guard", DescentRarity.Stock, 10, dur: 0, durMax: 12, broken: true),
                Item("Spanner Charm", DescentRarity.Vintage, 8, affixes: 1),
                Item("Dust Mask", DescentRarity.Stock, 6),
            ],
        });

    [UiSample("medic-potions")]
    private static UiScreen MedicPotions(UiSampleServices s) => Build(s,
        new FakeWareVendor
        {
            Kind = DescentVendorKind.Healing,
            VendorNameToken = "#Descent_Vendor_Medic",
            StanceToken = "#Descent_Vendor_Stance_Healing",
            PlayerAustralium = 40,
            SellTab = false,
            BuyWares =
            [
                Ware("Small Health Kit", DescentRarity.Stock, 3, 4),
                Ware("Medic's Tonic", DescentRarity.Vintage, 8, 12),
                Ware("Rocket Launcher", DescentRarity.Stock, 4, 6),
                Ware("Adrenaline Vial", DescentRarity.Strange, 14, 30),
                Ware("Medical Mist", DescentRarity.Unusual, 21, 90, unusual: true, affordable: false),
                Ware("Grapeshot Syrup", DescentRarity.Vintage, 7, 10),
            ],
            SellWares = [],
        },
        new FakeWareGrid
        {
            Capacity = 24,
            Items =
            [
                Item("Health Kit", DescentRarity.Stock, 3),
                Item("Health Kit", DescentRarity.Stock, 3),
                Item("Vial", DescentRarity.Vintage, 6),
                Item("Bone Splint", DescentRarity.Stock, 4, dur: 5, durMax: 5),
                Item("Canteen", DescentRarity.Vintage, 7, dur: 2, durMax: 9),
            ],
        });

    [UiSample("merasmus-magic")]
    private static UiScreen MerasmusMagic(UiSampleServices s) => Build(s,
        new FakeWareVendor
        {
            Kind = DescentVendorKind.Magic,
            VendorNameToken = "#Descent_Vendor_Merasmus",
            StanceToken = "#Descent_Vendor_Stance_Magic",
            PlayerAustralium = 260,
            SellTab = false,
            BuyWares =
            [
                Ware("Tome of the Pyro's Fire", DescentRarity.Strange, 30, 140),
                Ware("Chaos Tome", DescentRarity.Vintage, 18, 55),
                Ware("Merasmus's Mismatched Grimoire", DescentRarity.Unusual, 44, 660, unusual: true, affordable: false),
                Ware("Voodoo Codex", DescentRarity.Stock, 9, 12),
                Ware("Scroll of the Imp", DescentRarity.Strange, 26, 95),
                Ware("Tome of Forgotten Names", DescentRarity.Vintage, 1, 250),
            ],
            SellWares = [],
        },
        new FakeWareGrid
        {
            Capacity = 24,
            Items =
            [
                Item("Familiar's Bell", DescentRarity.Vintage, 12, affixes: 1),
                Item("Cracked Oracle Glass", DescentRarity.Strange, 27, dur: 3, durMax: 9),
                Item("Charred Bookmark", DescentRarity.Stock, 2),
            ],
        });

    [UiSample("spy-identify")]
    private static UiScreen SpyIdentify(UiSampleServices s) => Build(s,
        new FakeWareVendor
        {
            Kind = DescentVendorKind.Identify,
            VendorNameToken = "#Descent_Vendor_Spy",
            StanceToken = "#Descent_Vendor_Stance_Identify",
            PlayerAustralium = 90,
            SellTab = false,
            ServiceFee = 25,
            BuyWares = [],
            SellWares = [],
        },
        new FakeWareGrid
        {
            Capacity = 8,
            Items =
            [
                Item("Fists of Radium", DescentRarity.Strange, 29, identified: false, affixes: 0),
                Item("Locket", DescentRarity.Unusual, 35, identified: false, affixes: 0, unusual: true),
                Item("Bootstraps", DescentRarity.Vintage, 14, identified: false, dur: 8, durMax: 8),
                Item("Coin", DescentRarity.Stock, 1, identified: false),
            ],
        });

    /// <summary>The Repairman's service stance: the pack's first piece is whole,
    /// so the picked row proves <c>FirstServiceable</c> skipped it for the
    /// broken one, and the fee plates price that piece and the whole pack.</summary>
    [UiSample("engineer-repair")]
    private static UiScreen EngineerRepair(UiSampleServices s) => Build(s,
        new FakeWareVendor
        {
            Kind = DescentVendorKind.Repair,
            VendorNameToken = "#Descent_Vendor_Engineer",
            StanceToken = "#Descent_Vendor_Stance_Repair",
            PlayerAustralium = 150,
            SellTab = false,
            ServiceFee = 30,
            ServiceFeeAll = 110,
            BuyWares = [],
            SellWares = [],
        },
        new FakeWareGrid
        {
            Capacity = 8,
            Items =
            [
                Item("Vacillator", DescentRarity.Vintage, 26, dur: 16, durMax: 16, affixes: 1),
                Item("Worn Boots", DescentRarity.Stock, 12, dur: 0, durMax: 14, broken: true),
                Item("Crowbar", DescentRarity.Vintage, 17, dur: 6, durMax: 10),
                Item("Ham", DescentRarity.Stock, 5),
            ],
        });

    /// <summary>The pointer on the first ware row: the hint popup hangs at
    /// the row's edge with the price note (the fake names the row; a real
    /// pointer reports through <see cref="IDescentVendorActions.HoverWare"/>).</summary>
    [UiSample("hale-ware-hovered")]
    private static UiScreen HaleWareHovered(UiSampleServices s)
        => Build(s,
            new FakeWareVendor
            {
                Kind = DescentVendorKind.Weapons,
                VendorNameToken = "#Descent_Vendor_Hale",
                StanceToken = "#Descent_Vendor_Stance_Weapons",
                PlayerAustralium = 1200,
                SellTab = true,
                BuyWares =
                [
                    Ware("Rocket Launcher", DescentRarity.Strange, 42, 90),
                    Ware("Minigun", DescentRarity.Vintage, 31, 55),
                    Ware("Scattergun", DescentRarity.Stock, 22, 18),
                ],
                SellWares = [],
            },
            new FakeWareGrid { Capacity = 24, Items = [] },
            wareHover: new FakeWareHover { WareIndex = 0 });

    /// <summary>The pointer on an identify bench's item: the ItemTooltip
    /// hangs at the grid, naming the affixes the "?" hides (D22).</summary>
    [UiSample("spy-item-hovered")]
    private static UiScreen SpyItemHovered(UiSampleServices s)
        => Build(s,
            new FakeWareVendor
            {
                Kind = DescentVendorKind.Identify,
                VendorNameToken = "#Descent_Vendor_Spy",
                StanceToken = "#Descent_Vendor_Stance_Identify",
                PlayerAustralium = 90,
                SellTab = false,
                ServiceFee = 25,
                BuyWares = [],
                SellWares = [],
            },
            new FakeWareGrid
            {
                Capacity = 8,
                Items =
                [
                    Item("Fists of Radium", DescentRarity.Strange, 29, identified: false, affixes: 0),
                    Item("Locket", DescentRarity.Unusual, 35, identified: false, affixes: 0, unusual: true),
                ],
            },
            itemHover: new FakeVendorItemHover
            {
                Hovering = true,
                Item = Item("Fists of Radium", DescentRarity.Strange, 29, affixes: 3),
            });

    [UiSample("scout-crates")]
    private static UiScreen ScoutCrates(UiSampleServices s) => Build(s,
        new FakeWareVendor
        {
            Kind = DescentVendorKind.Gambling,
            VendorNameToken = "#Descent_Vendor_Scout",
            StanceToken = "#Descent_Vendor_Stance_Gambling",
            PlayerAustralium = 120,
            SellTab = false,
            BuyWares = [Ware("Salvaged Crate", DescentRarity.Vintage, 1, 45)],
            SellWares = [],
        },
        new FakeWareGrid { Capacity = 8, Items = [] });

    /// <summary>The service stance at the window's minimum, where the plan's
    /// layout is tightest: the grid and the fee column stack (under 900 du the
    /// window is narrow), and the fee plates, the two primaries and the purse
    /// must all still fit.</summary>
    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Build(s,
            new FakeWareVendor
            {
                Kind = DescentVendorKind.Repair,
                VendorNameToken = "#Descent_Vendor_Engineer",
                StanceToken = "#Descent_Vendor_Stance_Repair",
                PlayerAustralium = 150,
                SellTab = false,
                ServiceFee = 30,
                ServiceFeeAll = 110,
                BuyWares = [],
                SellWares = [],
            },
            new FakeWareGrid
            {
                Capacity = 8,
                Items =
                [
                    Item("Worn Boots", DescentRarity.Stock, 12, dur: 0, durMax: 14, broken: true),
                    Item("Crowbar", DescentRarity.Vintage, 17, dur: 6, durMax: 10),
                ],
            },
            store: new MemoryWindowStore().With("vendor", new UiWindowPlacement(200, 120, 640, 420)));

    // ---- builders ---------------------------------------------------------------

    private static DescentWare Ware(string name, DescentRarity rarity, int ilvl, long price,
        bool affordable = true, bool identified = true, bool unusual = false, int dur = 0, int durMax = 0)
        => new(Item(name, rarity, ilvl, identified, unusual, dur, durMax), price, affordable);

    private static DescentItemView Item(string name, DescentRarity rarity, int ilvl, bool identified = true,
        bool unusual = false, int dur = 0, int durMax = 0, int affixes = 0, bool broken = false)
        => new(name, rarity, ilvl, identified, dur, durMax, affixes, unusual, broken, false, false);
}

// ---- fakes --------------------------------------------------------------------

public sealed class FakeWareVendor : IDescentVendor
{
    public DescentVendorKind Kind { get; init; }
    public string VendorNameToken { get; init; } = "#Descent_Vendor_Hale";
    public string StanceToken { get; init; } = "#Descent_Vendor_Stance_Weapons";
    public long PlayerAustralium { get; init; }
    public DescentWare[] BuyWares { get; init; } = [];
    public DescentWare[] SellWares { get; init; } = [];
    public long ServiceFee { get; init; }
    public long ServiceFeeAll { get; init; }
    public bool SellTab { get; init; }

    public int BuyCount => BuyWares.Length;
    public DescentWare Buy(int i) => BuyWares[i];
    public int SellCount => SellWares.Length;
    public DescentWare Sell(int i) => SellWares[i];
}

public sealed class FakeWareGrid : IDescentItemGridSource
{
    public DescentItemView?[] Items { get; init; } = [];
    public int Capacity { get; init; }
    public bool Overflow { get; init; }

    public int CellCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Items.Length; i++)
                if (Items[i] is not null) n++;
            return n;
        }
    }

    public DescentItemView? Cell(int i) => i < Items.Length ? Items[i] : null;
}

public sealed class FakeWareActions : IDescentVendorActions
{
    public string Last { get; private set; } = "";
    public int LastIndex { get; private set; } = -1;

    public void Buy(int wareIndex) => (Last, LastIndex) = ("buy", wareIndex);
    public void Sell(int wareIndex) => (Last, LastIndex) = ("sell", wareIndex);
    public void ServiceOne(int itemIndex) => (Last, LastIndex) = ("service-one", itemIndex);
    public void ServiceAll() => (Last, LastIndex) = ("service-all", -1);
    public void HoverWare(int wareIndex) => (Last, LastIndex) = ("hover-ware", wareIndex);
    public void HoverBackpack(int itemIndex) => (Last, LastIndex) = ("hover-backpack", itemIndex);
}

/// <summary>The ware hint's content — the shopkeep's pitch for the row the
/// pointer names. Which ware is hovered is a settable property: a sample
/// forces one; a real pointer reports it through
/// <see cref="IDescentVendorActions.HoverWare"/>. The pitch is server copy,
/// so the fake speaks ui.Data lines, exactly as the host's view-model would.</summary>
public sealed class FakeWareHover : IDescentVendorHover
{
    /// <summary>Set by a sample to force the popup; -1 keeps it inactive.</summary>
    public int WareIndex { get; set; } = -1;

    public bool Active => WareIndex >= 0;
    public string TitleToken => string.Empty;
    public string TitleData => "Rocket Launcher";
    public string SubtitleData => "ilvl 42";
    public int LineCount => 1;
    public string LineToken(int i) => string.Empty;
    public string LineData(int i) => "Fires rockets. Ask about the ammo bundle.";
    public string LineGood(int i) => null!;
    public bool LineGoodNegative(int i) => false;
    public string ValueToken(int i) => string.Empty;
    public string ValueData(int i) => "90 Australium";
    public bool ValueGood(int i) => true;
}

/// <summary>The identify bench's / crate stall's item tooltip subject: the
/// identified view of the hovered item, so the popup can show what the "?"
/// hides (D22 — the slot shows "?", the tooltip is the Spy's service).</summary>
public sealed class FakeVendorItemHover : IDescentItemHover
{
    public bool Hovering { get; init; }
    public DescentItemView Item { get; init; } = new("Fists of Radium", DescentRarity.Strange, 29, true, 0, 0, 3, false, false, false, false);
    public string Family { get; init; } = "melee weapon";
    public string UnusualEffect { get; init; } = "";
    public bool CompareHas { get; init; }
    public DescentItemView CompareItem { get; init; }

    public int AffixLineCount => Item.AffixCount;
    public DescentAffixLine AffixLine(int i) => i switch
    {
        0 => new("Base Attack Rate", 11),
        1 => new("Melee Range", 6),
        _ => new("Burn Damage", 4),
    };
    public string RequirementToken => "#Descent_Req_Brawn";
    public int RequirementValue => 12;
    public bool RequirementMet => true;
    public int CompareAffixCount => CompareHas ? AffixLineCount : 0;
    public DescentAffixLine CompareAffix(int i) => AffixLine(i);
}
