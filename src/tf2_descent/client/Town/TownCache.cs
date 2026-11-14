using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Descent.Client.Content;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Town;

/// <summary>
/// What the owner's client holds of the town window he has open (plan_descent_logic UI4), as the
/// server last sent it: the window's state, a vendor's stock and buy-back, the server's quotes
/// for his own items, the bench's quote, the depths, and the last verb's answer. Fed by the town's
/// messages only; a window opening starts its lists and its status line over; a level's end
/// forgets everything. Nothing here decides.
/// </summary>
public interface ITownView
{
    /// <summary>Counts every change: a source that keeps something built rebuilds it when this moves.</summary>
    int Version { get; }

    /// <summary>The window last opened; null before any this level.</summary>
    TownWindowView? Window { get; }

    /// <summary>Whether the server names rows of other tables than the client embeds.</summary>
    bool ContentMismatch { get; }

    /// <summary>A vendor's list: what he sells, or what he keeps for buy-back, by index (null for a ware not yet arrived).</summary>
    IReadOnlyList<VendorWareView?> List(TownList list);

    /// <summary>The server's price for an item of the hero's, by the ledger's id; null when it quoted none.</summary>
    long? Quote(TownQuoteKind kind, long id);

    /// <summary>The bench's quote; null before one.</summary>
    CraftQuoteView? Craft { get; }

    /// <summary>The depth picker's rows; null before them.</summary>
    DepthRowsView? Depths { get; }

    /// <summary>The last verb's answer since the window opened; null for none.</summary>
    TownStatusView? Status { get; }
}

/// <summary>The town window cache over the town's messages.</summary>
[SideService(Side.Client, As = [typeof(ITownView)])]
public sealed class TownCache(DescentCatalog catalog) : ITownView, ITownHook, IClientLevelHook, IOrderedHook
{
    /// <summary>In TF2 Descent's town block (UI4): unique among the client's level hooks.</summary>
    public int HookOrder => 1205;

    private VendorWareView?[] _stock = [];
    private VendorWareView?[] _buyBack = [];
    private readonly Dictionary<long, long>[] _quotes = [new(), new()];

    public int Version { get; private set; }

    public TownWindowView? Window { get; private set; }

    public bool ContentMismatch => Window is { } w && w.Fingerprint != catalog.Fingerprint;

    public CraftQuoteView? Craft { get; private set; }

    public DepthRowsView? Depths { get; private set; }

    public TownStatusView? Status { get; private set; }

    public IReadOnlyList<VendorWareView?> List(TownList list) => list == TownList.Stock ? _stock : _buyBack;

    public long? Quote(TownQuoteKind kind, long id) =>
        (int)kind < _quotes.Length && _quotes[(int)kind].TryGetValue(id, out long price) ? price : null;

    public void OnTownWindow(TownWindowView window)
    {
        // Another window, or one opened again by a use: its lists and status line start over.
        if (window.Opened || Window is not { } was || was.Screen != window.Screen || was.Stall != window.Stall)
            Forget(keepWindow: false);
        Window = window;
        Version++;
    }

    public void OnVendorList(TownList list, int count)
    {
        var fresh = new VendorWareView?[Math.Max(0, count)];
        if (list == TownList.Stock)
            _stock = fresh;
        else
            _buyBack = fresh;
        Version++;
    }

    public void OnVendorWare(VendorWareView ware)
    {
        var list = ware.List == TownList.Stock ? _stock : _buyBack;
        if (ware.Index >= 0 && ware.Index < list.Length)
            list[ware.Index] = ware;
        Version++;
    }

    public void OnTownQuotes(TownQuotesView quotes)
    {
        if ((int)quotes.Kind >= _quotes.Length)
            return;
        var kept = _quotes[(int)quotes.Kind];
        if (quotes.First == 0)
            kept.Clear();
        for (int i = 0; i < quotes.Ids.Count && i < quotes.Prices.Count; i++)
            kept[quotes.Ids[i]] = quotes.Prices[i];
        Version++;
    }

    public void OnTownStatus(TownStatusView status)
    {
        Status = status;
        Version++;
    }

    public void OnCraftQuote(CraftQuoteView quote)
    {
        Craft = quote;
        Version++;
    }

    public void OnDepthRows(DepthRowsView rows)
    {
        Depths = rows;
        Version++;
    }

    public void OnLevelShutdown() => Forget(keepWindow: false);

    private void Forget(bool keepWindow)
    {
        if (!keepWindow)
            Window = null;
        _stock = [];
        _buyBack = [];
        foreach (var q in _quotes)
            q.Clear();
        Craft = null;
        Depths = null;
        Status = null;
        Version++;
    }
}
