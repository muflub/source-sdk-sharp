using SourceSharp.Abi;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// The town's windows (vendor, stash, crafting bench, crate stall, depth
// picker) before UI4 wires them (plan_descent_logic UI1). Each answers
// "closed, nothing to show", never sample data, and names what the server
// must send; an action does nothing until its client command exists.
//=============================================================================

/// <summary>
/// A vendor (<see cref="IDescentVendor"/>), with nothing for sale and nothing
/// to buy back. Live when a vendor's use (WO8) sends the vendor's kind, its
/// wares as item views with prices, what it would pay for the backpack's
/// items, and its service fees.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentVendor)])]
public sealed class VendorSource : IDescentVendor
{
    public DescentVendorKind Kind => DescentVendorKind.Weapons;

    public string VendorNameToken => "";

    public string StanceToken => "";

    public long PlayerAustralium => 0;

    public int BuyCount => 0;

    public DescentWare Buy(int i) => default;

    public int SellCount => 0;

    public DescentWare Sell(int i) => default;

    public long ServiceFee => 0;

    public long ServiceFeeAll => 0;

    public bool SellTab => false;
}

/// <summary>A vendor's buttons (<see cref="IDescentVendorActions"/>): nothing is sent until UI4's buy, sell and service commands.</summary>
[SideService(Side.Client, As = [typeof(IDescentVendorActions)])]
public sealed class VendorActions : IDescentVendorActions
{
    public void Buy(int wareIndex) { }

    public void Sell(int wareIndex) { }

    public void ServiceOne(int itemIndex) { }

    public void ServiceAll() { }

    public void HoverWare(int wareIndex) { }

    public void HoverBackpack(int itemIndex) { }
}

/// <summary>A ware's hover hint (<see cref="IDescentVendorHover"/>): nothing hovered.</summary>
[SideService(Side.Client, As = [typeof(IDescentVendorHover)])]
public sealed class VendorHoverSource : InactiveHint, IDescentVendorHover
{
    public int WareIndex => -1;
}

/// <summary>The vendor's portrait (<see cref="IDescentVendorPortrait"/>): not drawn. Live when the engine renders the vendor's face to a surface.</summary>
[SideService(Side.Client, As = [typeof(IDescentVendorPortrait)])]
public sealed class VendorPortraitSource : IDescentVendorPortrait
{
    public bool Active => false;

    public int Texture => 0;
}

/// <summary>
/// The stash locker (<see cref="IDescentStash"/>), empty. Live when the stash's
/// use (WO8) sends the account's stash: capacity, the Storage Plan rank, and
/// each cell's item view.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentStash)])]
public sealed class StashSource : IDescentStash
{
    public int CellCount => 0;

    public int Capacity => 0;

    public bool Overflow => false;

    public DescentItemView? Cell(int i) => null;

    public int StoragePlanRankShown => 0;
}

/// <summary>
/// The depth picker (<see cref="IDescentDescent"/>), with no depths. Live when
/// the mine cage's use (WO7, mine plan E1) sends the depths the party may
/// pick: each one's tileset, reached, the leader's reach, locked and boss.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentDescent)])]
public sealed class DepthsSource : IDescentDescent
{
    public string PartyLeaderToken => "";

    public int RowCount => 0;

    public DescentDepthRow Row(int i) => default;
}

/// <summary>Descending (<see cref="IDescentDescentActions"/>): nothing is sent until UI4's travel command.</summary>
[SideService(Side.Client, As = [typeof(IDescentDescentActions)])]
public sealed class DepthActions : IDescentDescentActions
{
    public void Descend(int depth) { }
}

/// <summary>
/// The crate stall (<see cref="IDescentCrate"/>), with no crate. Live when the
/// stall's use (WO8) sends the chosen base, its price, the odds table and the
/// opened item's view.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentCrate)])]
public sealed class CrateSource : IDescentCrate
{
    public string BaseName => "";

    public long PriceAustralium => 0;

    public bool Affordable => false;

    public int OddsRowCount => 0;

    public DescentRarity OddsRarity(int i) => DescentRarity.Stock;

    public long OddsWeight(int i) => 0;

    public bool Opening => false;

    public DescentItemView? OpenedItem => null;
}

/// <summary>Opening a crate (<see cref="IDescentCrateActions"/>): nothing is sent until UI4's open command.</summary>
[SideService(Side.Client, As = [typeof(IDescentCrateActions)])]
public sealed class CrateActions : IDescentCrateActions
{
    public void Open() { }
}

/// <summary>
/// The crafting bench (<see cref="IDescentCraft"/>), with no recipe and no item
/// on it. Live when the bench's use (WO8) sends the recipe tabs, the item
/// under work with its affix lines and locks, the reroll count and cost, and
/// the hero's metal.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentCraft)])]
public sealed class CraftSource : IDescentCraft
{
    public int RecipeTabCount => 0;

    public string RecipeTabToken(int i) => "";

    public int RecipeTabIndex => 0;

    public string InputsToken => "";

    public DescentItemView? ItemUnderTest => null;

    public int AffixLineCount => 0;

    public DescentAffixLine AffixLine(int i) => default;

    public int LockedCount => 0;

    public bool RerollAffixLocked(int i) => false;

    public int RerollCount => 0;

    public long RerollCost => 0;

    public int Scrap => 0;

    public int Reclaimed => 0;

    public int Refined => 0;

    public bool CanCraft => false;

    public string PreviewToken => "";
}

/// <summary>The bench's buttons (<see cref="IDescentCraftActions"/>): nothing is sent until UI4's craft command.</summary>
[SideService(Side.Client, As = [typeof(IDescentCraftActions)])]
public sealed class CraftActions : IDescentCraftActions
{
    public void SelectRecipeTab(int i) { }

    public void ToggleLock(int affixIndex) { }

    public void Craft() { }
}

/// <summary>What the bench is short of (<see cref="IDescentCraftShortage"/>): nothing, with nothing on the bench.</summary>
[SideService(Side.Client, As = [typeof(IDescentCraftShortage)])]
public sealed class CraftShortageSource : IDescentCraftShortage
{
    public int ScrapShort => 0;

    public int ReclaimedShort => 0;

    public int RefinedShort => 0;
}
