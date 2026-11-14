using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

//=============================================================================
// THE TOWN'S WINDOWS, TO THE CUSTOMER ONLY (plan_descent_logic WO8, UI4). A
// stall the hero uses opens its window: first the window's state (which
// screen, which stall and stance), then what it shows (a vendor's stock and
// buy-back, the server's prices for the hero's own items, the bench's
// recipes, the depths), then the stock panel's VGUIMenu. Each goes to that
// hero's client and to no other, again whenever what it shows changes while
// the window is open; the answer to each of his verbs comes back as a status
// line the window itself draws (a HUD notice hides under an open window).
// Every number here is the server's: the client names rows from the content
// it embeds and decides nothing.
//=============================================================================

/// <summary>Which town window a stall opens.</summary>
public enum TownScreen : byte
{
    None,
    Vendor,
    Stash,
    Craft,
    Crate,
    Depth,
}

/// <summary>Which face a vendor window wears: the order of the screens' <c>DescentVendorKind</c> (a fact holds the two together).</summary>
public enum TownStance : byte
{
    Weapons,
    Armour,
    Healing,
    Magic,
    Identify,
    Repair,
    Gambling,
}

/// <summary>A vendor's two lists: what he sells, and what the hero sold him that he keeps for buy-back.</summary>
public enum TownList : byte
{
    Stock,
    BuyBack,
}

/// <summary>What a quote prices: the Spy's identify of an item, or the Engineer's repair of one.</summary>
public enum TownQuoteKind : byte
{
    Identify,
    Repair,
}

/// <summary>One depth row's flags (<see cref="DepthRows"/>).</summary>
[Flags]
public enum DepthFlags : byte
{
    None = 0,
    /// <summary>The hero (or his party) may pick it now (the rules' <c>TownDescents.Destinations</c>).</summary>
    Open = 1,
    /// <summary>The level holds a boss.</summary>
    Boss = 2,
}

/// <summary>The town's messages as the owner's client takes them.</summary>
public interface ITownHook
{
    void OnTownWindow(TownWindowView window);

    void OnVendorList(TownList list, int count);

    void OnVendorWare(VendorWareView ware);

    void OnTownQuotes(TownQuotesView quotes);

    void OnTownStatus(TownStatusView status);

    void OnCraftQuote(CraftQuoteView quote);

    void OnDepthRows(DepthRowsView rows);
}

//-----------------------------------------------------------------------------
// The window
//-----------------------------------------------------------------------------

/// <summary>A window's state, read off the wire.</summary>
public sealed record TownWindowView(TownScreen Screen, int Stall, TownStance Stance, uint Fingerprint, int StashCapacity, int StorageRank,
                                    long RepairAll, IReadOnlyList<int> OddsRarities, IReadOnlyList<int> OddsWeights,
                                    string NameToken, string StanceToken, bool Opened);

/// <summary>
/// A town window opened (<c>town_window</c>), sent before what it shows: the screen, the stall
/// (its index in the town's stall table), the vendor's stance, the content fingerprint, the
/// stash's capacity and the account's Storage Plan rank (the stash), what repairing
/// everything costs (the Engineer; 0 for nothing to repair), and the crate stall's published
/// odds (each rarity's index and its weight out of 100,000; D23), and its title and stance
/// tokens. Sent again when one changes.
/// </summary>
public partial struct TownWindow
{
    /// <summary>The most odds rows a window carries (the rarity ladder's five).</summary>
    public const int MaxOdds = 5;

    /// <summary>What the published weights are out of.</summary>
    public const int OddsScale = 100_000;

    /// <summary>The longest title or stance token carried.</summary>
    public const int MaxToken = 40;

    public partial byte Screen { get; set; }
    public partial byte Stall { get; set; }
    public partial byte Stance { get; set; }
    public partial uint Fingerprint { get; set; }
    public partial ushort StashCapacity { get; set; }
    public partial byte StorageRank { get; set; }
    public partial long RepairAll { get; set; }

    [UserMessageArray(MaxCount = MaxOdds)]
    public partial byte[] OddsRarities { get; set; }

    [UserMessageArray(MaxCount = MaxOdds)]
    public partial int[] OddsWeights { get; set; }

    /// <summary>The window's title, a token of the mod's language file.</summary>
    [UserMessageString(MaxLength = MaxToken)]
    public partial string NameToken { get; set; }

    /// <summary>The vendor's one-line stance, a token; "" for none.</summary>
    [UserMessageString(MaxLength = MaxToken)]
    public partial string StanceToken { get; set; }

    /// <summary>The window has just opened (a use), not shown again after a change: its status line starts over.</summary>
    public partial bool Opened { get; set; }

    public TownWindowView View() => new((TownScreen)Screen, Stall, (TownStance)Stance, Fingerprint, StashCapacity, StorageRank, RepairAll,
                                        Array.ConvertAll(OddsRarities ?? [], r => (int)r), OddsWeights ?? [], NameToken ?? "", StanceToken ?? "", Opened);
}

[UserMessage(Name)]
public sealed class TownWindowMessage(IHooks<ITownHook>? hooks) : IUserMessage<TownWindow>
{
    public TownWindowMessage() : this(null) { }

    public const string Name = "town_window";

    public void OnReceived(TownWindow message)
    {
        var view = message.View();
        hooks?.Each(nameof(ITownHook.OnTownWindow), h => h.OnTownWindow(view));
    }
}

//-----------------------------------------------------------------------------
// A vendor's lists
//-----------------------------------------------------------------------------

/// <summary>A list starts over (<c>vendor_list</c>): it holds <see cref="Count"/> wares, which follow one a message.</summary>
public partial struct VendorList
{
    public partial byte List { get; set; }
    public partial byte Count { get; set; }
}

[UserMessage(Name)]
public sealed class VendorListMessage(IHooks<ITownHook>? hooks) : IUserMessage<VendorList>
{
    public VendorListMessage() : this(null) { }

    public const string Name = "vendor_list";

    public void OnReceived(VendorList message)
    {
        var (list, count) = ((TownList)message.List, (int)message.Count);
        hooks?.Each(nameof(ITownHook.OnVendorList), h => h.OnVendorList(list, count));
    }
}

/// <summary>One ware, read off the wire: its list, its place in it, the price, and the item as a buyer may see it before buying.</summary>
public sealed record VendorWareView(TownList List, int Index, long Price, OwnerItemView Item);

/// <summary>
/// One ware (<c>vendor_ware</c>): the list and index, the ledger's id (what a buy names), the
/// price the hero pays, and the item as the rules' vendor view shows it (the owner item's fields).
/// </summary>
public partial struct VendorWare
{
    public partial byte List { get; set; }
    public partial byte Index { get; set; }
    public partial long Id { get; set; }
    public partial long Price { get; set; }
    public partial ushort Base { get; set; }
    public partial byte Rarity { get; set; }
    public partial byte ItemLevel { get; set; }
    public partial byte Flags { get; set; }
    public partial ushort Durability { get; set; }
    public partial ushort MaxDurability { get; set; }
    public partial ushort Count { get; set; }

    /// <summary>The unusual effect's index + 1; 0 for none.</summary>
    public partial byte Unusual { get; set; }

    /// <summary>The unique's index + 1; 0 for none.</summary>
    public partial byte Unique { get; set; }

    public partial byte Unmet { get; set; }

    [UserMessageArray(MaxCount = OwnerItem.MaxAffixes)]
    public partial byte[] Affixes { get; set; }

    [UserMessageArray(MaxCount = OwnerItem.MaxAffixes)]
    public partial float[] Values { get; set; }

    /// <summary>A ware carrying an owner item's view (its id, base, rarity and the rest).</summary>
    public static VendorWare Of(TownList list, int index, long price, OwnerItem item) => new()
    {
        List = (byte)list,
        Index = (byte)Math.Clamp(index, 0, byte.MaxValue),
        Id = item.Id,
        Price = price,
        Base = item.Base,
        Rarity = item.Rarity,
        ItemLevel = item.ItemLevel,
        Flags = item.Flags,
        Durability = item.Durability,
        MaxDurability = item.MaxDurability,
        Count = item.Count,
        Unusual = item.Unusual,
        Unique = item.Unique,
        Unmet = item.Unmet,
        Affixes = item.Affixes,
        Values = item.Values,
    };

    public VendorWareView View()
    {
        var ids = Affixes ?? [];
        var values = Values ?? [];
        var affixes = new (int, float)[Math.Min(ids.Length, values.Length)];
        for (int i = 0; i < affixes.Length; i++)
            affixes[i] = (ids[i], values[i]);
        var item = new OwnerItemView(Id, ItemContainer.Backpack, -1, Base, Rarity, ItemLevel, (ItemViewFlags)Flags,
                                     Durability, MaxDurability, Count, Unusual - 1, Unique - 1, Unmet, affixes);
        return new VendorWareView((TownList)List, Index, Price, item);
    }
}

[UserMessage(Name)]
public sealed class VendorWareMessage(IHooks<ITownHook>? hooks) : IUserMessage<VendorWare>
{
    public VendorWareMessage() : this(null) { }

    public const string Name = "vendor_ware";

    public void OnReceived(VendorWare message)
    {
        var view = message.View();
        hooks?.Each(nameof(ITownHook.OnVendorWare), h => h.OnVendorWare(view));
    }
}

//-----------------------------------------------------------------------------
// Quotes: the server's prices for the hero's own items
//-----------------------------------------------------------------------------

/// <summary>A run of quotes, read off the wire: what they price, from which of how many, each item's id and price.</summary>
public sealed record TownQuotesView(TownQuoteKind Kind, int First, int Total, IReadOnlyList<long> Ids, IReadOnlyList<long> Prices);

/// <summary>
/// What the Spy or the Engineer would charge for each of the hero's items he would act on
/// (<c>town_quotes</c>), by the ledger's id, <see cref="MaxQuotes"/> a message; the first of a
/// run (<see cref="First"/> 0) replaces every quote of its kind.
/// </summary>
public partial struct TownQuotes
{
    /// <summary>The most quotes a message carries.</summary>
    public const int MaxQuotes = 12;

    public partial byte Kind { get; set; }
    public partial byte First { get; set; }
    public partial byte Total { get; set; }

    [UserMessageArray(MaxCount = MaxQuotes)]
    public partial long[] Ids { get; set; }

    [UserMessageArray(MaxCount = MaxQuotes)]
    public partial long[] Prices { get; set; }

    public TownQuotesView View() => new((TownQuoteKind)Kind, First, Total, Ids ?? [], Prices ?? []);
}

[UserMessage(Name)]
public sealed class TownQuotesMessage(IHooks<ITownHook>? hooks) : IUserMessage<TownQuotes>
{
    public TownQuotesMessage() : this(null) { }

    public const string Name = "town_quotes";

    public void OnReceived(TownQuotes message)
    {
        var view = message.View();
        hooks?.Each(nameof(ITownHook.OnTownQuotes), h => h.OnTownQuotes(view));
    }
}

//-----------------------------------------------------------------------------
// The status line
//-----------------------------------------------------------------------------

/// <summary>A verb's answer, read off the wire: a <c>#Descent_Town_*</c> token, its argument, whether it was refused, and the item it made (0: none).</summary>
public sealed record TownStatusView(string Token, string Arg, bool Error, long Item);

/// <summary>The answer to one of the hero's town verbs (<c>town_status</c>), for the open window's status line.</summary>
public partial struct TownStatus
{
    public const int MaxToken = 48, MaxArg = 64;

    [UserMessageString(MaxLength = MaxToken)]
    public partial string Token { get; set; }

    [UserMessageString(MaxLength = MaxArg)]
    public partial string Arg { get; set; }

    public partial bool Error { get; set; }

    public partial long Item { get; set; }

    public TownStatusView View() => new(Token ?? "", Arg ?? "", Error, Item);
}

[UserMessage(Name)]
public sealed class TownStatusMessage(IHooks<ITownHook>? hooks) : IUserMessage<TownStatus>
{
    public TownStatusMessage() : this(null) { }

    public const string Name = "town_status";

    public void OnReceived(TownStatus message)
    {
        var view = message.View();
        hooks?.Each(nameof(ITownHook.OnTownStatus), h => h.OnTownStatus(view));
    }
}

//-----------------------------------------------------------------------------
// The bench
//-----------------------------------------------------------------------------

/// <summary>The bench as the server prices it, read off the wire (<see cref="CraftQuote"/>).</summary>
public sealed record CraftQuoteView(long Item, int Rerolls, int Metal, IReadOnlyList<int> Costs, int Ready,
                                    IReadOnlyList<int> RerollCosts, int RerollReady);

/// <summary>
/// The crafting bench (<c>craft_quote</c>): the item under work (the ledger's id of the hero's
/// first gear in the backpack; 0 for none), its re-rolls so far, the metal he holds in scrap
/// units, each recipe's metal cost in the bench's recipe order and whether the rules would run
/// it now (a bit each), and for a re-roll each affix's cost and readiness.
/// </summary>
public partial struct CraftQuote
{
    /// <summary>The most recipes, and affixes, a quote carries.</summary>
    public const int MaxRecipes = 8, MaxAffixes = OwnerItem.MaxAffixes;

    public partial long Item { get; set; }
    public partial byte Rerolls { get; set; }
    public partial int Metal { get; set; }

    [UserMessageArray(MaxCount = MaxRecipes)]
    public partial int[] Costs { get; set; }

    public partial ushort Ready { get; set; }

    [UserMessageArray(MaxCount = MaxAffixes)]
    public partial int[] RerollCosts { get; set; }

    public partial byte RerollReady { get; set; }

    public CraftQuoteView View() => new(Item, Rerolls, Metal, Costs ?? [], Ready, RerollCosts ?? [], RerollReady);
}

[UserMessage(Name)]
public sealed class CraftQuoteMessage(IHooks<ITownHook>? hooks) : IUserMessage<CraftQuote>
{
    public CraftQuoteMessage() : this(null) { }

    public const string Name = "craft_quote";

    public void OnReceived(CraftQuote message)
    {
        var view = message.View();
        hooks?.Each(nameof(ITownHook.OnCraftQuote), h => h.OnCraftQuote(view));
    }
}

//-----------------------------------------------------------------------------
// The depth picker
//-----------------------------------------------------------------------------

/// <summary>The depths the picker lists, read off the wire, shallowest first.</summary>
public sealed record DepthRowsView(IReadOnlyList<int> Depths, IReadOnlyList<DepthFlags> Flags);

/// <summary>The depth picker's rows (<c>depth_rows</c>): every depth the town's way down serves, and whether each is open now.</summary>
public partial struct DepthRows
{
    public const int MaxRows = 16;

    [UserMessageArray(MaxCount = MaxRows)]
    public partial byte[] Depths { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial byte[] Flags { get; set; }

    public DepthRowsView View() =>
        new(Array.ConvertAll(Depths ?? [], d => (int)d), Array.ConvertAll(Flags ?? [], f => (DepthFlags)f));
}

[UserMessage(Name)]
public sealed class DepthRowsMessage(IHooks<ITownHook>? hooks) : IUserMessage<DepthRows>
{
    public DepthRowsMessage() : this(null) { }

    public const string Name = "depth_rows";

    public void OnReceived(DepthRows message)
    {
        var view = message.View();
        hooks?.Each(nameof(ITownHook.OnDepthRows), h => h.OnDepthRows(view));
    }
}
