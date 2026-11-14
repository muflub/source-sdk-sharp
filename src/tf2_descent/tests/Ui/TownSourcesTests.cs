extern alias client;

using TF2.Descent.Client.Ui;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using CraftQuoteView = client::TF2.Descent.Shared.CraftQuoteView;
using DepthFlags = client::TF2.Descent.Shared.DepthFlags;
using DepthRowsView = client::TF2.Descent.Shared.DepthRowsView;
using IGameClock = client::SourceSharp.Abi.IGameClock;
using IItemViews = client::TF2.Descent.Client.Items.IItemViews;
using ItemContainer = client::TF2.Descent.Shared.ItemContainer;
using ItemViewFlags = client::TF2.Descent.Shared.ItemViewFlags;
using OwnerItemView = client::TF2.Descent.Shared.OwnerItemView;
using Src = client::TF2.Descent.Client.Ui;
using TownCache = client::TF2.Descent.Client.Town.TownCache;
using TownList = client::TF2.Descent.Shared.TownList;
using TownQuoteKind = client::TF2.Descent.Shared.TownQuoteKind;
using TownQuotesView = client::TF2.Descent.Shared.TownQuotesView;
using TownScreen = client::TF2.Descent.Shared.TownScreen;
using TownStance = client::TF2.Descent.Shared.TownStance;
using TownStatusView = client::TF2.Descent.Shared.TownStatusView;
using TownWindowView = client::TF2.Descent.Shared.TownWindowView;
using VendorWareView = client::TF2.Descent.Shared.VendorWareView;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The town windows' live sources (plan_descent_logic UI4) over what the owner's client holds:
/// the town cache as the server's messages fill it, and fake item views; the content is the
/// client's own embedded tables. Every number they show is one the server sent.
/// </summary>
public sealed class TownSourcesTests
{
    private static readonly Catalog Content = new();

    private static int Base(string id) => Content.Bases.ToList().FindIndex(b => b.Id == id);

    private sealed class FakeItems : IItemViews
    {
        public List<OwnerItemView> Held { get; } = [];
        public int Version => Held.Count;
        public bool Synced => true;
        public bool ContentMismatch => false;
        public long Australium { get; set; } = 500;
        public int BackpackCapacity { get; set; } = 12;
        public IReadOnlyList<OwnerItemView> Items => Held;
        public OwnerItemView? In(ItemContainer container, int slot) => Held.FirstOrDefault(i => i.Container == container && i.Slot == slot);
    }

    private sealed class Lines : Src.IDescentCommandLine
    {
        public List<string> Sent { get; } = [];
        public void Send(string line) => Sent.Add(line);
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private static OwnerItemView Item(long id, ItemContainer container, int slot, string baseId, int count = 1, bool identified = true,
                                      params (int, float)[] affixes)
        => new(id, container, slot, Base(baseId), 1, 7, identified ? ItemViewFlags.Identified : ItemViewFlags.None, 50, 60, count, -1, -1, 0, affixes);

    private static TownWindowView Window(TownScreen screen, TownStance stance = TownStance.Weapons, int stash = 20, int rank = 0,
                                         long repairAll = 0, bool opened = true) =>
        new(screen, 0, stance, Content.Fingerprint, stash, rank, repairAll, [1, 2, 3], [75_000, 22_000, 3_000],
            "#Descent_Vendor_Hale", "#Descent_Vendor_Stance_Weapons", opened);

    private static void Wares(TownCache town, TownList list, params (long Id, string Base, long Price)[] wares)
    {
        town.OnVendorList(list, wares.Length);
        for (int i = 0; i < wares.Length; i++)
            town.OnVendorWare(new VendorWareView(list, i, wares[i].Price, Item(wares[i].Id, ItemContainer.Backpack, -1, wares[i].Base)));
    }

    //-------------------------------------------------------------------------
    // The vendor
    //-------------------------------------------------------------------------

    [Fact]
    public void The_vendor_shows_the_servers_stock_and_buy_back_at_its_prices_against_the_purse()
    {
        var town = new TownCache(Content);
        var items = new FakeItems { Australium = 500 };
        town.OnTownWindow(Window(TownScreen.Vendor));
        Wares(town, TownList.Stock, (11, "rocket_launcher", 400), (12, "shotgun", 900));
        Wares(town, TownList.BuyBack, (13, "cap", 30));
        var vendor = new Src.VendorSource(town, items, new Src.ItemViewNames(Content), new Src.VendorPicks(town, new Lines()));

        Assert.Equal(DescentVendorKind.Weapons, vendor.Kind);
        Assert.Equal("#Descent_Vendor_Hale", vendor.VendorNameToken);
        Assert.Equal(2, vendor.BuyCount);
        Assert.Equal(400, vendor.Buy(0).Price);
        Assert.True(vendor.Buy(0).Affordable);
        Assert.False(vendor.Buy(1).Affordable);
        Assert.Equal(1, vendor.SellCount);
        Assert.Equal(30, vendor.Sell(0).Price);
        Assert.True(vendor.SellTab);
        Assert.Equal(500, vendor.PlayerAustralium);
        Assert.Equal(Content.Bases[Base("rocket_launcher")].Name, vendor.Buy(0).Item.BaseName);
    }

    [Fact]
    public void With_no_vendor_window_open_the_vendor_shows_nothing()
    {
        var town = new TownCache(Content);
        town.OnTownWindow(Window(TownScreen.Stash));
        Wares(town, TownList.Stock, (11, "rocket_launcher", 400));
        var vendor = new Src.VendorSource(town, new FakeItems(), new Src.ItemViewNames(Content), new Src.VendorPicks(town, new Lines()));

        Assert.Equal(0, vendor.BuyCount);
        Assert.Equal("", vendor.VendorNameToken);
    }

    [Fact]
    public void The_vendors_buttons_send_the_heros_town_verbs()
    {
        var town = new TownCache(Content);
        var items = new FakeItems();
        items.Held.Add(Item(5, ItemContainer.Backpack, 3, "cap"));
        town.OnTownWindow(Window(TownScreen.Vendor));
        Wares(town, TownList.Stock, (11, "rocket_launcher", 400));
        Wares(town, TownList.BuyBack, (13, "cap", 30));
        var lines = new Lines();
        var clock = new Clock();
        var actions = new Src.VendorActions(town, items, lines, new Src.WarePointer(clock), new Src.ItemPointer(clock));
        var picks = new Src.VendorPicks(town, lines);

        actions.Buy(0);
        actions.Sell(3);
        picks.BuyBack(0);
        actions.Sell(4);   // an empty cell sends nothing

        Assert.Equal(["tf2_descent_town_buy 11", "tf2_descent_town_sell 3", "tf2_descent_town_buyback 0"], lines.Sent);
    }

    [Fact]
    public void The_service_fee_is_the_servers_quote_for_the_picked_item_and_repair_all_its_sum()
    {
        var town = new TownCache(Content);
        var items = new FakeItems();
        items.Held.Add(Item(5, ItemContainer.Backpack, 1, "rocket_launcher"));
        items.Held.Add(Item(6, ItemContainer.Backpack, 4, "shotgun"));
        town.OnTownWindow(Window(TownScreen.Vendor, TownStance.Repair, repairAll: 77));
        town.OnTownQuotes(new TownQuotesView(TownQuoteKind.Repair, 0, 2, [5, 6], [30, 47]));
        var picks = new Src.VendorPicks(town, new Lines());
        var vendor = new Src.VendorSource(town, items, new Src.ItemViewNames(Content), picks);

        Assert.Equal(30, vendor.ServiceFee);       // nothing picked: the first quoted
        picks.PickService(4);
        Assert.Equal(47, vendor.ServiceFee);
        Assert.Equal(77, vendor.ServiceFeeAll);
        Assert.False(vendor.SellTab);
        Assert.Equal(DescentVendorKind.Repair, vendor.Kind);
    }

    [Fact]
    public void A_ware_under_the_pointer_is_the_hints_subject_with_its_price_and_the_item_it_would_replace()
    {
        var town = new TownCache(Content);
        var items = new FakeItems();
        int primary = Content.EquipSlots.ToList().FindIndex(e => e.Id == "primary");
        items.Held.Add(Item(9, ItemContainer.Equipped, primary, "rocket_launcher"));
        town.OnTownWindow(Window(TownScreen.Vendor));
        Wares(town, TownList.Stock, (11, "rocket_launcher", 400));
        var clock = new Clock();
        var pointer = new Src.WarePointer(clock);
        var hover = new Src.VendorHoverSource(pointer, town, items, new Src.ItemViewNames(Content), Content);
        var hint = new Src.HintTipSource(new Src.SheetHoverSource(new Src.SheetPointer(clock), new NoNumbers(), Content, null!),
                                         new Src.SkillHoverSource(new Src.SkillPointer(clock), new NoRanks(), Content), hover);
        Assert.False(hint.Active);

        pointer.Report(0);

        Assert.True(hint.Active);
        Assert.Equal(0, hover.WareIndex);
        Assert.Equal(Content.Bases[Base("rocket_launcher")].Name, hint.TitleData);
        Assert.Equal("400 Australium", hint.ValueData(0));
        Assert.Contains(Enumerable.Range(0, hint.LineCount), i => hint.LineToken(i) == "#Descent_Vendor_Compare");
    }

    private sealed class NoNumbers : client::TF2.Descent.Client.Items.IHeroSheetNumbers
    {
        public client::TF2.Descent.Shared.SheetNumbersView? Sheet => null;
    }

    private sealed class NoRanks : client::TF2.Descent.Client.Items.IHeroSkillRanks
    {
        public bool Known => false;
        public int RankOf(int skill) => 0;
        public client::TF2.Descent.Shared.SkillWhy WhyOf(int skill) => client::TF2.Descent.Shared.SkillWhy.UnknownSkill;
        public int NextLevelOf(int skill) => 0;
        public bool PrerequisitesMetOf(int skill) => false;
    }

    //-------------------------------------------------------------------------
    // The status line
    //-------------------------------------------------------------------------

    [Fact]
    public void The_status_line_is_the_servers_last_answer_and_a_window_opened_again_starts_it_over()
    {
        var town = new TownCache(Content);
        var status = new Src.TownStatusSource(town);
        town.OnTownWindow(Window(TownScreen.Vendor));

        town.OnTownStatus(new TownStatusView("#Descent_Town_not_enough_australium", "", true, 0));
        Assert.Equal("#Descent_Town_not_enough_australium", status.StatusToken);
        Assert.True(status.StatusError);
        town.OnTownWindow(Window(TownScreen.Vendor, opened: false));   // the window shown again after a change
        Assert.Equal("#Descent_Town_not_enough_australium", status.StatusToken);

        town.OnTownWindow(Window(TownScreen.Vendor, opened: true));

        Assert.Equal("", status.StatusToken);
    }

    //-------------------------------------------------------------------------
    // The stash
    //-------------------------------------------------------------------------

    [Fact]
    public void The_stash_shows_the_stash_items_the_servers_capacity_and_rank()
    {
        var town = new TownCache(Content);
        var items = new FakeItems();
        items.Held.Add(Item(5, ItemContainer.Stash, 2, "cap"));
        items.Held.Add(Item(6, ItemContainer.Backpack, 0, "shotgun"));
        town.OnTownWindow(Window(TownScreen.Stash, stash: 36, rank: 1));
        var stash = new Src.StashSource(town, items, new Src.ItemViewNames(Content));

        Assert.Equal(1, stash.CellCount);
        Assert.Equal(36, stash.Capacity);
        Assert.Equal(1, stash.StoragePlanRankShown);
        Assert.NotNull(stash.Cell(2));
        Assert.Null(stash.Cell(0));
    }

    [Fact]
    public void A_move_across_the_lockers_span_deposits_and_one_back_withdraws()
    {
        var items = new FakeItems { BackpackCapacity = 12 };
        items.Held.Add(Item(6, ItemContainer.Backpack, 2, "shotgun"));
        items.Held.Add(Item(7, ItemContainer.Stash, 0, "cap"));
        var lines = new Lines();
        var actions = new Src.ItemActions(items, Content, lines, new Src.ItemPointer(new Clock()));

        actions.Move(2, 12 + 4);     // backpack cell 2 to a stash cell
        actions.Move(12 + 0, 5);     // stash cell 0 to a backpack cell
        actions.Move(2, 7);          // within the backpack

        Assert.Equal(["tf2_descent_town_stash_in 2", "tf2_descent_town_stash_out 0", "tf2_descent_item_move 2 7"], lines.Sent);
    }

    //-------------------------------------------------------------------------
    // The crate, the bench, the depths
    //-------------------------------------------------------------------------

    [Fact]
    public void The_crate_stall_shows_the_scouts_crate_its_price_the_published_odds_and_what_it_held()
    {
        var town = new TownCache(Content);
        var items = new FakeItems { Australium = 600 };
        town.OnTownWindow(Window(TownScreen.Crate, TownStance.Gambling));
        Wares(town, TownList.Stock, (21, "rocket_launcher", 550));
        var crate = new Src.CrateSource(town, items, new Src.ItemViewNames(Content));
        Assert.Equal(550, crate.PriceAustralium);
        Assert.True(crate.Affordable);
        Assert.Equal(3, crate.OddsRowCount);
        Assert.Equal(75_000, crate.OddsWeight(0));
        Assert.Equal(DescentRarity.Vintage, crate.OddsRarity(0));
        Assert.False(crate.Opening);

        items.Held.Add(Item(30, ItemContainer.Backpack, 0, "shotgun", identified: false));
        town.OnTownStatus(new TownStatusView("#Descent_Town_Done_crate", "Shotgun", false, 30));

        Assert.True(crate.Opening);
        Assert.False(crate.OpenedItem!.Value.Identified);
    }

    [Fact]
    public void The_bench_reads_the_servers_quote_and_a_reroll_rolls_the_one_unlocked_affix()
    {
        var town = new TownCache(Content);
        var items = new FakeItems();
        items.Held.Add(Item(40, ItemContainer.Backpack, 0, "rocket_launcher", affixes: [(0, 1.1f), (1, 2f)]));
        items.Held.Add(Item(41, ItemContainer.Backpack, 1, "scrap", count: 9));
        town.OnTownWindow(Window(TownScreen.Craft));
        town.OnCraftQuote(new CraftQuoteView(40, 2, 9, [27, 0, 9, 0, 6, 0, 0, 0], (1 << 4) | (1 << 1), [3, 12], 1));
        var lines = new Lines();
        var picks = new Src.BenchPicks(town, lines);
        var bench = new Src.CraftSource(town, items, new Src.ItemViewNames(Content), picks);
        var shortage = new Src.CraftShortageSource(bench);

        Assert.Equal(8, bench.RecipeTabCount);
        Assert.Equal(9, bench.Scrap);
        Assert.Equal(2, bench.RerollCount);
        Assert.False(bench.CanCraft);                 // craft by base: not ready
        Assert.Equal(18, shortage.ScrapShort);
        picks.SelectRecipeTab(4);
        Assert.True(bench.CanCraft);                  // the repair kit is ready
        picks.SelectRecipeTab(1);
        Assert.Equal(1, bench.LockedCount);           // all but the first locked
        Assert.Equal(3, bench.RerollCost);
        Assert.True(bench.CanCraft);

        picks.Craft();
        picks.ToggleLock(1);                          // two unlocked: no re-roll
        Assert.False(bench.CanCraft);

        Assert.Equal(["tf2_descent_town_craft 1 0"], lines.Sent);
    }

    [Fact]
    public void The_depth_picker_offers_the_servers_rows_open_or_locked_and_descends_by_the_verb()
    {
        var town = new TownCache(Content);
        town.OnTownWindow(Window(TownScreen.Depth));
        town.OnDepthRows(new DepthRowsView([1, 2, 3, 4, 5], [DepthFlags.Open, DepthFlags.Open, DepthFlags.None, DepthFlags.None, DepthFlags.Boss]));
        var lines = new Lines();
        var depths = new Src.DepthsSource(town, new NoHeroes(), new NoNames());

        Assert.Equal(5, depths.RowCount);
        Assert.False(depths.Row(0).Locked);
        Assert.True(depths.Row(2).Locked);
        Assert.True(depths.Row(4).Boss);
        Assert.Equal("#Descent_Depth_Level_2", depths.Row(1).TilesetToken);
        new Src.DepthActions(town, lines).Descend(2);
        Assert.Equal(["tf2_descent_town_descend 2"], lines.Sent);
    }

    private sealed class NoHeroes : client::TF2.Descent.Client.IHeroStates
    {
        public client::TF2.Descent.Client.HeroState? Of(int playerIndex) => null;
        public client::TF2.Descent.Client.HeroState? Local() => null;
        public IReadOnlyList<client::TF2.Descent.Client.HeroState> All() => [];
    }

    private sealed class NoNames : client::SourceSharp.Client.Vgui.IPlayerNames
    {
        public bool TryGet(int slot, out client::SourceSharp.Abi.ClientPlayerInfo info)
        {
            info = default;
            return false;
        }
    }

    [Fact]
    public void A_level_end_forgets_the_window_and_what_it_showed()
    {
        var town = new TownCache(Content);
        town.OnTownWindow(Window(TownScreen.Vendor));
        Wares(town, TownList.Stock, (11, "rocket_launcher", 400));

        town.OnLevelShutdown();

        Assert.Null(town.Window);
        Assert.Empty(town.List(TownList.Stock));
    }

    [Fact]
    public void The_wires_stances_are_the_vendor_windows_kinds_in_their_order()
    {
        Assert.Equal(Enum.GetNames<DescentVendorKind>(), Enum.GetNames<TownStance>());
        Assert.Equal(Enum.GetValues<DescentVendorKind>().Select(k => (int)k), Enum.GetValues<TownStance>().Select(k => (int)k));
    }
}
