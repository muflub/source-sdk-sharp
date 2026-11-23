using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

using TF2.Descent.Client.Content;
using TF2.Descent.Client.Items;
using TF2.Descent.Client.Town;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// THE TOWN'S WINDOWS OVER LIVE DATA (plan_descent_logic UI4): the vendor, the
// stash locker, the crafting bench, the crate stall and the depth picker. Each
// source reads the window the server opened and what it shows (ITownView), the
// owner's own items (IItemViews) and the content the client embeds
// (DescentCatalog). Every price, quote and refusal is the server's; a button
// sends the hero's own town verb (tf2_descent_town_*) and the answer comes
// back through the same caches, the refusal on the window's status line.
//=============================================================================

/// <summary>The status line every town window draws (<see cref="IDescentTownStatus"/>): the server's answer to the hero's last verb there.</summary>
[SideService(Side.Client, As = [typeof(IDescentTownStatus)])]
public sealed class TownStatusSource(ITownView town) : IDescentTownStatus
{
    public string StatusToken => town.Status?.Token ?? "";

    public string StatusData => town.Status?.Arg ?? "";

    public bool StatusError => town.Status?.Error ?? false;
}

/// <summary>The vendor window's pointer: the ware row under it.</summary>
[SideService(Side.Client)]
public sealed class WarePointer(IGameClock clock) : PointerReport(clock);

/// <summary>
/// The vendor window's two picks the screen holds and the game must know (<see cref="IDescentVendorPicks"/>):
/// the service stance's item, which its fee prices; and the buy-back tab's action.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentVendorPicks)])]
public sealed class VendorPicks(ITownView town, IDescentCommandLine line) : IDescentVendorPicks
{
    /// <summary>The backpack cell the service stance picked; -1 for none (the first it would act on is priced).</summary>
    public int Service { get; private set; } = -1;

    public void PickService(int itemIndex) => Service = itemIndex;

    public void BuyBack(int soldIndex)
    {
        var sold = town.List(TownList.BuyBack);
        if (soldIndex >= 0 && soldIndex < sold.Count && sold[soldIndex] is not null)
            line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_town_buyback {soldIndex}"));
    }
}

/// <summary>
/// The vendor window (<see cref="IDescentVendor"/>): the stance, title and pitch the server
/// opened it with, the hero's purse, the stock and the buy-back list at the server's prices
/// (a row dims when the purse is short), and for the Spy and the Engineer the server's fee for
/// the item the window picked (or the first it would act on) and the Engineer's repair-all.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentVendor)])]
public sealed class VendorSource(ITownView town, IItemViews items, ItemViewNames names, VendorPicks picks) : IDescentVendor
{
    private TownWindowView? Window => town.Window is { Screen: TownScreen.Vendor } w && !town.ContentMismatch ? w : null;

    public DescentVendorKind Kind => Window is { } w ? (DescentVendorKind)w.Stance : DescentVendorKind.Weapons;

    public string VendorNameToken => Window?.NameToken ?? "";

    public string StanceToken => Window?.StanceToken ?? "";

    public long PlayerAustralium => items.Australium;

    public int BuyCount => Window is null ? 0 : town.List(TownList.Stock).Count;

    public DescentWare Buy(int i) => Ware(TownList.Stock, i);

    public int SellCount => Window is null ? 0 : town.List(TownList.BuyBack).Count;

    public DescentWare Sell(int i) => Ware(TownList.BuyBack, i);

    private DescentWare Ware(TownList list, int i)
    {
        var wares = town.List(list);
        if (Window is null || i < 0 || i >= wares.Count || wares[i] is not { } w)
            return default;
        return new DescentWare(names.View(w.Item), w.Price, items.Australium >= w.Price);
    }

    /// <summary>The quote kind the window's stance answers with; null for a shop.</summary>
    private TownQuoteKind? Quotes => Window?.Stance switch
    {
        TownStance.Identify => TownQuoteKind.Identify,
        TownStance.Repair => TownQuoteKind.Repair,
        _ => null,
    };

    public long ServiceFee
    {
        get
        {
            if (Quotes is not { } kind || items.ContentMismatch)
                return 0;
            if (picks.Service >= 0 && items.In(ItemContainer.Backpack, picks.Service) is { } picked)
                return town.Quote(kind, picked.Id) ?? 0;
            // Nothing picked: the first backpack item the server quoted, as the window picks one.
            OwnerItemView? first = null;
            foreach (var item in items.Items)
                if (item.Container == ItemContainer.Backpack && town.Quote(kind, item.Id) is not null && (first is null || item.Slot < first.Slot))
                    first = item;
            return first is null ? 0 : town.Quote(kind, first.Id) ?? 0;
        }
    }

    public long ServiceFeeAll => Window is { Stance: TownStance.Repair } w ? w.RepairAll : 0;

    public bool SellTab => Window?.Stance is TownStance.Weapons or TownStance.Armour or TownStance.Healing or TownStance.Magic;
}

/// <summary>
/// The vendor window's buttons (<see cref="IDescentVendorActions"/>): a buy names the ware's id
/// (<c>tf2_descent_town_buy</c>), a sale the backpack cell, a service the Spy's identify or the
/// Engineer's repair of the cell, repair-all; the pointer on a ware is the hint's subject, on a
/// backpack cell the item tooltip's.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentVendorActions)])]
public sealed class VendorActions(ITownView town, IItemViews items, IDescentCommandLine line, WarePointer wares, ItemPointer cells)
    : IDescentVendorActions
{
    public void Buy(int wareIndex)
    {
        var stock = town.List(TownList.Stock);
        if (wareIndex >= 0 && wareIndex < stock.Count && stock[wareIndex] is { } ware)
            line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_town_buy {ware.Item.Id}"));
    }

    public void Sell(int wareIndex)
    {
        if (items.In(ItemContainer.Backpack, wareIndex) is not null)
            line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_town_sell {wareIndex}"));
    }

    public void ServiceOne(int itemIndex)
    {
        if (items.In(ItemContainer.Backpack, itemIndex) is null)
            return;
        switch (town.Window?.Stance)
        {
            case TownStance.Identify:
                line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_town_identify {itemIndex}"));
                break;
            case TownStance.Repair:
                line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_town_repair {itemIndex}"));
                break;
        }
    }

    public void ServiceAll()
    {
        if (town.Window?.Stance == TownStance.Repair)
            line.Send("tf2_descent_town_repair_all");
    }

    public void HoverWare(int wareIndex) => wares.Report(wareIndex);

    public void HoverBackpack(int itemIndex) => cells.Report(itemIndex);
}

/// <summary>
/// The ware row's hint (<see cref="IDescentVendorHover"/>): the ware's name, its rarity and item
/// level, its affix lines (none for one the server sent unidentified), its price, and the item
/// the hero wears in the slot it would take, with that item's lines, to compare.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentVendorHover)])]
public sealed class VendorHoverSource(WarePointer pointer, ITownView town, IItemViews items, ItemViewNames names, DescentCatalog catalog)
    : IDescentVendorHover
{
    private readonly List<(string Token, string Data, string Good)> _lines = [];
    private (int Row, int Version, int Items) _built = (-2, -1, -1);

    private VendorWareView? Ware =>
        pointer.Active && town.Window is { Screen: TownScreen.Vendor } && pointer.Row >= 0 && pointer.Row < town.List(TownList.Stock).Count
            ? town.List(TownList.Stock)[pointer.Row]
            : null;

    public int WareIndex => Ware is null ? -1 : pointer.Row;

    public bool Active => Ware is not null;

    public string TitleToken => "";

    public string TitleData => Ware is { } w ? names.NameOf(w.Item) : "";

    public string SubtitleData => Ware is { } w ? string.Create(CultureInfo.InvariantCulture, $"ilvl {w.Item.ItemLevel}") : "";

    public int LineCount => Lines().Count;

    public string LineToken(int i) => i >= 0 && i < Lines().Count ? Lines()[i].Token : "";

    public string LineData(int i) => i >= 0 && i < Lines().Count ? Lines()[i].Data : "";

    public string LineGood(int i) => i >= 0 && i < Lines().Count ? Lines()[i].Good : "";

    public bool LineGoodNegative(int i) => LineGood(i).StartsWith('-');

    public string ValueToken(int i) => "";

    /// <summary>The price, the hint's emphasised value (green when the purse covers it).</summary>
    public string ValueData(int i) => i == 0 && Ware is { } w ? string.Create(CultureInfo.InvariantCulture, $"{w.Price} Australium") : "";

    public bool ValueGood(int i) => i == 0 && Ware is { } w && items.Australium >= w.Price;

    /// <summary>The equipped item a ware would replace (the first equipment slot taking its base's slot).</summary>
    private OwnerItemView? Compared(VendorWareView ware)
    {
        if (names.BaseOf(ware.Item) is not { IsGear: true } b)
            return null;
        for (int i = 0; i < catalog.EquipSlots.Count; i++)
            if (catalog.EquipSlots[i].Accepts == b.Slot && items.In(ItemContainer.Equipped, i) is { } held)
                return held;
        return null;
    }

    private List<(string Token, string Data, string Good)> Lines()
    {
        var key = (pointer.Active ? pointer.Row : -1, town.Version, items.Version);
        if (key == _built)
            return _lines;
        _built = key;
        _lines.Clear();
        if (Ware is not { } ware)
            return _lines;
        if (!ware.Item.Identified)
            _lines.Add(("#Descent_Vendor_Unidentified", "", ""));
        foreach (var (affix, value) in ware.Item.Affixes)
        {
            var line = names.Line(affix, value);
            _lines.Add(("", line.Name, ItemViewNames.Signed(line.Value)));
        }
        if (Compared(ware) is { } held)
        {
            _lines.Add(("#Descent_Vendor_Compare", "", ""));
            _lines.Add(("", names.NameOf(held), ""));
            foreach (var (affix, value) in held.Affixes)
            {
                var line = names.Line(affix, value);
                _lines.Add(("", line.Name, ItemViewNames.Signed(line.Value)));
            }
        }
        return _lines;
    }
}

//-----------------------------------------------------------------------------
// The stash locker
//-----------------------------------------------------------------------------

/// <summary>
/// The stash locker (<see cref="IDescentStash"/>): the owner's stash items by cell (the item
/// views carry them), the capacity and the Storage Plan rank the server opened the locker with,
/// and the withdraw-only tail past it.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentStash)])]
public sealed class StashSource(ITownView town, IItemViews items, ItemViewNames names) : IDescentStash
{
    public int CellCount
    {
        get
        {
            if (items.ContentMismatch)
                return 0;
            int n = 0;
            foreach (var item in items.Items)
                if (item.Container == ItemContainer.Stash)
                    n++;
            return n;
        }
    }

    public int Capacity => town.Window?.StashCapacity ?? 0;

    public bool Overflow
    {
        get
        {
            foreach (var item in items.Items)
                if (item.Container == ItemContainer.Stash && item.Slot >= Capacity)
                    return true;
            return false;
        }
    }

    public DescentItemView? Cell(int i) =>
        items.ContentMismatch ? null : items.In(ItemContainer.Stash, i) is { } item ? names.View(item) : null;

    public int StoragePlanRankShown => town.Window?.StorageRank ?? 0;
}

//-----------------------------------------------------------------------------
// The crate stall
//-----------------------------------------------------------------------------

/// <summary>
/// The crate stall (<see cref="IDescentCrate"/>): the Scout's first crate this restock, at the
/// server's price; the published odds the server sent (out of 100,000); and once the hero has
/// opened one, the item it held (the server names it on the status line; the item view arrives
/// with the rest of the backpack), unidentified.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentCrate)])]
public sealed class CrateSource(ITownView town, IItemViews items, ItemViewNames names) : IDescentCrate
{
    private TownWindowView? Window => town.Window is { Screen: TownScreen.Crate } w && !town.ContentMismatch ? w : null;

    private VendorWareView? Crate => Window is not null && town.List(TownList.Stock) is { Count: > 0 } stock ? stock[0] : null;

    public string BaseName => Crate is { } c ? names.NameOf(c.Item) : "";

    public long PriceAustralium => Crate?.Price ?? 0;

    public bool Affordable => Crate is { } c && items.Australium >= c.Price;

    public int OddsRowCount => Window?.OddsRarities.Count ?? 0;

    public DescentRarity OddsRarity(int i) =>
        Window is { } w && i >= 0 && i < w.OddsRarities.Count ? (DescentRarity)Math.Clamp(w.OddsRarities[i], 0, (int)DescentRarity.Australium) : DescentRarity.Stock;

    public long OddsWeight(int i) => Window is { } w && i >= 0 && i < w.OddsWeights.Count ? w.OddsWeights[i] : 0;

    /// <summary>The item the last open made: the status line's, once its view has come.</summary>
    private OwnerItemView? Opened =>
        Window is not null && town.Status is { Error: false, Item: > 0 } s && s.Token == "#Descent_Town_Done_crate"
            ? items.Items.FirstOrDefault(i => i.Id == s.Item)
            : null;

    public bool Opening => Opened is not null;

    public DescentItemView? OpenedItem => Opened is { } item ? names.View(item) : null;
}

/// <summary>Opening a crate (<see cref="IDescentCrateActions"/>): <c>tf2_descent_town_crate</c>; the server pays for the Scout's first crate and opens it.</summary>
/// <summary>
/// A town window closed on the client tells the server (<c>tf2_descent_town_close &lt;screen&gt;</c>),
/// which ends its session at once (the whole run's F27: it waited for the hero to walk 160 u away).
/// Only for the town window last opened; a close the server itself sent (a walk away, a
/// descent) is told too and finds no session there, so it changes nothing.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentTownClose)])]
public sealed class TownCloseSender(ITownView town, IDescentCommandLine line) : IDescentTownClose
{
    public const string Verb = "tf2_descent_town_close";

    public void Closed(string screen)
    {
        if (town.Window is { } open && ScreenOf(open.Screen) == screen)
            line.Send($"{Verb} {screen}");
    }

    /// <summary>The client screen a town window shows on.</summary>
    public static string ScreenOf(TownScreen screen) => screen switch
    {
        TownScreen.Vendor => "vendor",
        TownScreen.Stash => "stash",
        TownScreen.Craft => "craftbench",
        TownScreen.Crate => "crate",
        TownScreen.Depth => "depthpicker",
        _ => "",
    };
}

[SideService(Side.Client, As = [typeof(IDescentCrateActions)])]
public sealed class CrateActions(ITownView town, IDescentCommandLine line) : IDescentCrateActions
{
    public void Open()
    {
        if (town.Window is { Screen: TownScreen.Crate })
            line.Send("tf2_descent_town_crate");
    }
}

//-----------------------------------------------------------------------------
// The crafting bench
//-----------------------------------------------------------------------------

/// <summary>
/// What the bench window holds on the client: the recipe picked and which affixes are locked
/// against a re-roll (the rest of the bench is the server's quote). The locks start over with
/// another item under work, every affix but the first locked (a re-roll rolls one affix, D24).
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentCraftActions)])]
public sealed class BenchPicks(ITownView town, IDescentCommandLine line) : IDescentCraftActions
{
    /// <summary>The bench's recipes, in the server's order (TF2.Descent.Server.Town.BenchRecipe).</summary>
    public const int RecipeCount = 8, Reroll = 1;

    private readonly bool[] _locked = new bool[CraftQuote.MaxAffixes];
    private long _lockedFor = -1;

    public int Recipe { get; private set; }

    public void SelectRecipeTab(int i) => Recipe = Math.Clamp(i, 0, RecipeCount - 1);

    /// <summary>Whether affix <paramref name="i"/> of the item under work is locked.</summary>
    public bool Locked(int i)
    {
        Sync();
        return i >= 0 && i < _locked.Length && _locked[i];
    }

    /// <summary>The one affix a re-roll would roll (exactly one unlocked); -1 otherwise.</summary>
    public int Unlocked(int affixes)
    {
        Sync();
        int found = -1;
        for (int i = 0; i < affixes && i < _locked.Length; i++)
        {
            if (_locked[i])
                continue;
            if (found >= 0)
                return -1;
            found = i;
        }
        return found;
    }

    public void ToggleLock(int affixIndex)
    {
        Sync();
        if (affixIndex >= 0 && affixIndex < _locked.Length)
            _locked[affixIndex] = !_locked[affixIndex];
    }

    public void Craft()
    {
        if (town.Window is not { Screen: TownScreen.Craft } || town.Craft is not { } q)
            return;
        int affix = Recipe == Reroll ? Unlocked(q.RerollCosts.Count) : 0;
        if (Recipe == Reroll && affix < 0)
            return;
        line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_town_craft {Recipe} {affix}"));
    }

    private void Sync()
    {
        long item = town.Craft?.Item ?? 0;
        if (item == _lockedFor)
            return;
        _lockedFor = item;
        for (int i = 0; i < _locked.Length; i++)
            _locked[i] = i > 0;
    }
}

/// <summary>
/// The crafting bench (<see cref="IDescentCraft"/>): the bench's recipes as tabs; the item under
/// work the server names (the hero's first gear) with its affix lines and the window's locks;
/// its re-rolls so far and the server's cost of re-rolling the one unlocked affix; the metal
/// the hero carries by rung; whether the server says the picked recipe would run now.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentCraft)])]
public sealed class CraftSource(ITownView town, IItemViews items, ItemViewNames names, BenchPicks picks) : IDescentCraft
{
    /// <summary>Each recipe's tab, inputs and preview tokens, in the server's order.</summary>
    private static string Tab(int i) => i switch
    {
        0 => "Base", 1 => "Reroll", 2 => "Upgrade", 3 => "Potions", 4 => "Repair", 5 => "Salvage", 6 => "MetalUp", _ => "MetalDown",
    };

    private CraftQuoteView? Quote => town.Window is { Screen: TownScreen.Craft } && !town.ContentMismatch ? town.Craft : null;

    private OwnerItemView? Work => Quote is { Item: > 0 } q ? items.Items.FirstOrDefault(i => i.Id == q.Item) : null;

    public int RecipeTabCount => Quote is null ? 0 : BenchPicks.RecipeCount;

    public string RecipeTabToken(int i) => "#Descent_Craft_Tab_" + Tab(i);

    public int RecipeTabIndex => picks.Recipe;

    public string InputsToken => Quote is null ? "" : picks.Recipe == 5 ? "#Descent_Craft_Inputs_Salvage" : "#Descent_Craft_Inputs_" + Tab(picks.Recipe);

    public DescentItemView? ItemUnderTest => Work is { } w ? names.View(w) : null;

    public int AffixLineCount => Work?.Affixes.Count ?? 0;

    public DescentAffixLine AffixLine(int i) =>
        Work is { } w && i >= 0 && i < w.Affixes.Count ? names.Line(w.Affixes[i].Affix, w.Affixes[i].Value) : default;

    public int LockedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < AffixLineCount; i++)
                if (picks.Locked(i))
                    n++;
            return n;
        }
    }

    public bool RerollAffixLocked(int i) => picks.Locked(i);

    public int RerollCount => Quote?.Rerolls ?? 0;

    public long RerollCost => Quote is { } q && picks.Unlocked(AffixLineCount) is var a and >= 0 && a < q.RerollCosts.Count ? q.RerollCosts[a] : 0;

    private int Metal(string rung)
    {
        int n = 0;
        foreach (var item in items.Items)
            if (item.Container == ItemContainer.Backpack && names.BaseOf(item)?.Id == rung)
                n += item.Count;
        return n;
    }

    public int Scrap => Metal("scrap");

    public int Reclaimed => Metal("reclaimed");

    public int Refined => Metal("refined");

    public bool CanCraft
    {
        get
        {
            if (Quote is not { } q)
                return false;
            if (picks.Recipe != BenchPicks.Reroll)
                return (q.Ready & (1 << picks.Recipe)) != 0;
            int a = picks.Unlocked(AffixLineCount);
            return a >= 0 && (q.RerollReady & (1 << a)) != 0;
        }
    }

    public string PreviewToken => Quote is null ? "" : "#Descent_Craft_Preview_" + Tab(picks.Recipe);

    /// <summary>The metal the picked recipe costs, in scrap units (a re-roll's: its unlocked affix's).</summary>
    public int Cost => Quote is { } q ? picks.Recipe == BenchPicks.Reroll ? (int)RerollCost : picks.Recipe < q.Costs.Count ? q.Costs[picks.Recipe] : 0 : 0;

    /// <summary>The metal the hero carries, in scrap units, as the server counted it.</summary>
    public int MetalUnits => Quote?.Metal ?? 0;
}

/// <summary>What the bench is short of (<see cref="IDescentCraftShortage"/>): the picked recipe's cost less the metal carried, in scrap.</summary>
[SideService(Side.Client, As = [typeof(IDescentCraftShortage)])]
public sealed class CraftShortageSource(CraftSource bench) : IDescentCraftShortage
{
    public int ScrapShort => Math.Max(0, bench.Cost - bench.MetalUnits);

    public int ReclaimedShort => 0;

    public int RefinedShort => 0;
}

//-----------------------------------------------------------------------------
// The depth picker
//-----------------------------------------------------------------------------

/// <summary>
/// The depth picker (<see cref="IDescentDescent"/>): the depths the town's way down serves, each
/// open when the server says the rules let this hero pick it (no parties yet: the hero leads
/// himself, so his reach is the leader's), and the boss levels.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentDescent)])]
public sealed class DepthsSource(ITownView town, IHeroStates heroes, IPlayerNames names) : IDescentDescent
{
    private DepthRowsView? Rows => town.Window is { Screen: TownScreen.Depth } ? town.Depths : null;

    public string PartyLeaderToken => heroes.Local() is { } h && names.TryGet(h.PlayerIndex, out var info) ? info.Name : "";

    public int RowCount => Rows?.Depths.Count ?? 0;

    public DescentDepthRow Row(int i)
    {
        if (Rows is not { } rows || i < 0 || i >= rows.Depths.Count)
            return default;
        int depth = rows.Depths[i];
        var flags = i < rows.Flags.Count ? rows.Flags[i] : DepthFlags.None;
        bool open = (flags & DepthFlags.Open) != 0;
        return new DescentDepthRow(depth, "#Descent_Depth_Level_" + depth.ToString(CultureInfo.InvariantCulture),
                                   Reached: open, LeaderReached: open, Locked: !open, Boss: (flags & DepthFlags.Boss) != 0);
    }
}

/// <summary>Descending (<see cref="IDescentDescentActions"/>): <c>tf2_descent_town_descend &lt;depth&gt;</c>; the server checks the rules and sends the hero down.</summary>
[SideService(Side.Client, As = [typeof(IDescentDescentActions)])]
public sealed class DepthActions(ITownView town, IDescentCommandLine line) : IDescentDescentActions
{
    public void Descend(int depth)
    {
        if (town.Window is { Screen: TownScreen.Depth })
            line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_town_descend {depth}"));
    }
}
