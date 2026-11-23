using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Town;

/// <summary>What one open window shows its customer, as the town decided it (<see cref="TownService"/>).</summary>
public sealed record TownShow(
    TownStall? Stall, int StallIndex, TownScreen Screen, TownStance Stance,
    IReadOnlyList<VendorItem> Stock, IReadOnlyList<(LedgerItem Item, long Price)> BuyBack,
    TownQuoteKind? QuoteKind, IReadOnlyList<(long Id, long Price)> Quotes, long RepairAll,
    int StashCapacity, int StorageRank, CraftQuote? Craft, DepthRows? Depths,
    IReadOnlyList<(int Rarity, int Weight)>? Odds = null, bool Opened = false);

/// <summary>
/// The town's messages to one customer (plan_descent_logic WO8, UI4): a window's state, then
/// what it shows (<see cref="TownShow"/>), then the panel; a verb's status line. Every message
/// goes to that player's client alone. Item views are the owner item's (the rules' view, so an
/// unidentified ware carries its base, rarity and level and nothing else).
/// </summary>
[SideService(Side.Server)]
public sealed class TownSender(IUserMessageSender sender, OwnerItemViews views, TF2DescentRules rules)
{
    private uint Fingerprint => _fingerprint ??= ContentKeys.Fingerprint(rules.Content);
    private uint? _fingerprint;

    /// <summary>The window's panel name: <c>descent_&lt;screen&gt;</c>, as each window claims it (UI1).</summary>
    public static string PanelOf(TownScreen screen) => screen switch
    {
        TownScreen.Vendor => "descent_vendor",
        TownScreen.Stash => "descent_stash",
        TownScreen.Craft => "descent_craftbench",
        TownScreen.Crate => "descent_crate",
        TownScreen.Depth => "descent_depthpicker",
        _ => "",
    };

    /// <summary>Everything an open window shows, to <paramref name="player"/>.</summary>
    public void Show(int player, TownShow show, HeroSheet? sheet, HeroStats? stats)
    {
        sender.Send(new TownWindow
        {
            Screen = (byte)show.Screen,
            Stall = (byte)Math.Clamp(show.StallIndex, 0, byte.MaxValue),
            Stance = (byte)show.Stance,
            Fingerprint = Fingerprint,
            StashCapacity = (ushort)Math.Clamp(show.StashCapacity, 0, ushort.MaxValue),
            StorageRank = (byte)Math.Clamp(show.StorageRank, 0, byte.MaxValue),
            RepairAll = show.RepairAll,
            OddsRarities = [.. (show.Odds ?? []).Take(TownWindow.MaxOdds).Select(o => (byte)o.Rarity)],
            OddsWeights = [.. (show.Odds ?? []).Take(TownWindow.MaxOdds).Select(o => o.Weight)],
            NameToken = Cut(show.Stall?.NameToken ?? (show.Screen == TownScreen.Depth ? "#Descent_Depth_Title" : ""), TownWindow.MaxToken),
            StanceToken = Cut(show.Stall?.StanceToken ?? "", TownWindow.MaxToken),
            Opened = show.Opened,
        }, player);

        if (show.Screen is TownScreen.Vendor or TownScreen.Crate)
        {
            List(player, TownList.Stock, [.. show.Stock.Select(s => (new LedgerItem(s.Id, s.Item, ItemPlace.Backpack, -1), s.Price))], sheet, stats);
            List(player, TownList.BuyBack, show.BuyBack, sheet, stats);
        }
        if (show.QuoteKind is { } kind)
            Quotes(player, kind, show.Quotes);
        if (show.Craft is { } craft)
            sender.Send(craft, player);
        if (show.Depths is { } depths)
            sender.Send(depths, player);
    }

    private void List(int player, TownList list, IReadOnlyList<(LedgerItem Item, long Price)> wares, HeroSheet? sheet, HeroStats? stats)
    {
        int count = Math.Min(wares.Count, byte.MaxValue);
        sender.Send(new VendorList { List = (byte)list, Count = (byte)count }, player);
        for (int i = 0; i < count; i++)
            if (views.Of(wares[i].Item, sheet, stats, null) is { } item)
                sender.Send(VendorWare.Of(list, i, wares[i].Price, item), player);
    }

    private void Quotes(int player, TownQuoteKind kind, IReadOnlyList<(long Id, long Price)> quotes)
    {
        int total = Math.Min(quotes.Count, byte.MaxValue);
        int first = 0;
        do
        {
            var run = quotes.Skip(first).Take(Math.Min(TownQuotes.MaxQuotes, total - first)).ToList();
            sender.Send(new TownQuotes
            {
                Kind = (byte)kind,
                First = (byte)first,
                Total = (byte)total,
                Ids = [.. run.Select(q => q.Id)],
                Prices = [.. run.Select(q => q.Price)],
            }, player);
            first += run.Count;
        }
        while (first < total);
    }

    /// <summary>A verb's answer on the open window's status line: a token, its argument, refused or not, and the item it made.</summary>
    public void Status(int player, string token, string arg, bool error, long item = 0) =>
        sender.Send(new TownStatus
        {
            Token = Cut(token, TownStatus.MaxToken),
            Arg = Cut(arg, TownStatus.MaxArg),
            Error = error,
            Item = item,
        }, player);

    /// <summary>
    /// A verb's answer as a HUD notice, for a hero with no window open (the Lost &amp; Found needs
    /// none): a status line draws only inside a town window.
    /// </summary>
    public void Notice(int player, string token, string arg) =>
        sender.Send(new Shared.Notice { Text = Cut(token, Shared.Notice.MaxText), Arg = Cut(arg, Shared.Notice.MaxArg) }, player);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
