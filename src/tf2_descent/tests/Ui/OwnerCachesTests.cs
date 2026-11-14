extern alias client;

using SourceSharp.Abi;

using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Shared;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using ClientItems = client::TF2.Descent.Shared.ItemContainer;
using ClientWhy = client::TF2.Descent.Shared.SkillWhy;
using IClientUserMessage = client::SourceSharp.Abi.IUserMessage;
using ClientReader = client::SourceSharp.Abi.UserMessageReader;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;
using ItemCache = client::TF2.Descent.Client.Items.OwnerItemCache;
using RanksCache = client::TF2.Descent.Client.Items.OwnerSkillRanksCache;
using SheetCache = client::TF2.Descent.Client.Items.OwnerSheetCache;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The owner's caches on the client (plan_descent_logic UI1's item-view cache, UI3): fed by the
/// bytes the server's payloads write, through the client's own message handlers; each forgets
/// all it held when a level ends or begins.
/// </summary>
public sealed class OwnerCachesTests
{
    private static readonly Catalog Content = new();

    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Log : ILog
    {
        public List<string> Warnings { get; } = [];
        public void Msg(string text) { }
        public void Warning(string text) => Warnings.Add(text);
    }

    private static byte[] Bytes<T>(T payload) where T : IUserMessagePayload<T>
    {
        var buffer = new byte[255];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits");
        return buffer[..n];
    }

    private static void Deliver(IClientUserMessage handler, byte[] bytes) => handler.OnReceived(new ClientReader(bytes));

    private sealed class ItemWire
    {
        public readonly Log Log = new();
        public readonly ItemCache Cache;
        private readonly client::TF2.Descent.Shared.OwnerItemMessage _item;
        private readonly client::TF2.Descent.Shared.OwnerItemGoneMessage _gone;
        private readonly client::TF2.Descent.Shared.OwnerItemsMessage _sync;

        public ItemWire()
        {
            Cache = new ItemCache(Content, Log);
            var hooks = new Hooks<client::TF2.Descent.Shared.IOwnerItemsHook>(Cache);
            _item = new(hooks);
            _gone = new(hooks);
            _sync = new(hooks);
        }

        public void Item(long id, ItemContainer container, int slot, int baseIndex = 0) =>
            Deliver(_item, Bytes(new OwnerItem { Id = id, Container = (byte)container, Slot = (short)slot, Base = (ushort)baseIndex,
                                                Affixes = [], Values = [] }));

        public void Gone(long id) => Deliver(_gone, Bytes(new OwnerItemGone { Id = id }));

        public void Sync(OwnerItemsPhase phase, int count = 0, uint? fingerprint = null, int capacity = 0, long australium = 0) =>
            Deliver(_sync, Bytes(new OwnerItems { Phase = (byte)phase, Count = (ushort)count, Fingerprint = fingerprint ?? Content.Fingerprint,
                                                  BackpackCapacity = (ushort)capacity, Australium = australium }));
    }

    [Fact]
    public void An_item_view_is_held_where_the_server_put_it()
    {
        var wire = new ItemWire();

        wire.Item(5, ItemContainer.Backpack, 3, baseIndex: 2);

        var held = wire.Cache.In(ClientItems.Backpack, 3);
        Assert.NotNull(held);
        Assert.Equal((5L, 2), (held!.Id, held.Base));
    }

    [Fact]
    public void A_changed_item_replaces_its_old_view()
    {
        var wire = new ItemWire();
        wire.Item(5, ItemContainer.Backpack, 3);

        wire.Item(5, ItemContainer.Equipped, 0);

        Assert.Null(wire.Cache.In(ClientItems.Backpack, 3));
        Assert.Equal(5L, wire.Cache.In(ClientItems.Equipped, 0)!.Id);
        Assert.Single(wire.Cache.Items);
    }

    [Fact]
    public void A_gone_item_leaves_the_cache()
    {
        var wire = new ItemWire();
        wire.Item(5, ItemContainer.Backpack, 3);

        wire.Gone(5);

        Assert.Empty(wire.Cache.Items);
    }

    [Fact]
    public void A_resync_forgets_what_was_held_and_holds_exactly_what_follows_over_several_messages()
    {
        var wire = new ItemWire();
        wire.Item(1, ItemContainer.Backpack, 0);
        wire.Item(2, ItemContainer.Backpack, 1);

        wire.Sync(OwnerItemsPhase.Begin, count: 3);
        Assert.Empty(wire.Cache.Items);
        Assert.False(wire.Cache.Synced);
        wire.Item(7, ItemContainer.Equipped, 0);
        wire.Item(8, ItemContainer.Equipped, 1);
        wire.Item(9, ItemContainer.Belt, 0);
        wire.Sync(OwnerItemsPhase.End, count: 3);

        Assert.True(wire.Cache.Synced);
        Assert.Equal([7L, 8L, 9L], wire.Cache.Items.Select(i => i.Id));
        Assert.Empty(wire.Log.Warnings);
    }

    [Fact]
    public void A_resync_that_ends_short_of_its_count_is_said()
    {
        var wire = new ItemWire();

        wire.Sync(OwnerItemsPhase.Begin, count: 2);
        wire.Item(7, ItemContainer.Equipped, 0);
        wire.Sync(OwnerItemsPhase.End, count: 2);

        Assert.Single(wire.Log.Warnings);
    }

    [Fact]
    public void The_state_carries_the_purse_and_the_capacity()
    {
        var wire = new ItemWire();

        wire.Sync(OwnerItemsPhase.State, capacity: 16, australium: 250);

        Assert.Equal((16, 250L), (wire.Cache.BackpackCapacity, wire.Cache.Australium));
    }

    [Fact]
    public void A_level_shutdown_forgets_every_item_and_the_purse()
    {
        var wire = new ItemWire();
        wire.Sync(OwnerItemsPhase.State, capacity: 16, australium: 250);
        wire.Sync(OwnerItemsPhase.Begin, count: 1);
        wire.Item(7, ItemContainer.Equipped, 0);
        wire.Sync(OwnerItemsPhase.End, count: 1);

        wire.Cache.OnLevelShutdown();

        Assert.Empty(wire.Cache.Items);
        Assert.False(wire.Cache.Synced);
        Assert.Equal((0, 0L), (wire.Cache.BackpackCapacity, wire.Cache.Australium));
    }

    [Fact]
    public void A_level_init_keeps_the_items_that_arrived_before_it()
    {
        // Live, the spawn's resync arrives while the client signs on, before the
        // engine's LevelInitPostEntity: a cache that forgot there showed nothing.
        var wire = new ItemWire();
        wire.Cache.OnLevelShutdown();
        wire.Item(7, ItemContainer.Equipped, 0);

        ((IClientLevelHook)wire.Cache).OnLevelInit("tf2_descent_town");

        Assert.Single(wire.Cache.Items);
    }

    [Fact]
    public void Another_contents_fingerprint_marks_the_cache_and_is_said_once()
    {
        var wire = new ItemWire();

        wire.Sync(OwnerItemsPhase.Begin, fingerprint: Content.Fingerprint ^ 1);
        wire.Sync(OwnerItemsPhase.End, fingerprint: Content.Fingerprint ^ 1);

        Assert.True(wire.Cache.ContentMismatch);
        Assert.Single(wire.Log.Warnings);
    }

    [Fact]
    public void The_servers_own_view_of_a_hero_arrives_whole()
    {
        // The server's sender over a real hero, its bytes delivered to the client's cache.
        var rig = new OwnerViews.OwnerRig();
        var wire = new ItemWire();
        var item = new client::TF2.Descent.Shared.OwnerItemMessage(new Hooks<client::TF2.Descent.Shared.IOwnerItemsHook>(wire.Cache));
        var sync = new client::TF2.Descent.Shared.OwnerItemsMessage(new Hooks<client::TF2.Descent.Shared.IOwnerItemsHook>(wire.Cache));
        int from = rig.Sent.Count;

        rig.Items.Resync(OwnerViews.OwnerRig.Index);
        foreach (var (key, _, bytes) in rig.Sent.Skip(from))
            Deliver(key == OwnerViews.OwnerRig.KeyOf<OwnerItem>() ? item : sync, bytes);

        Assert.True(wire.Cache.Synced);
        Assert.False(wire.Cache.ContentMismatch);
        Assert.Equal(rig.Gear.Held.Items.Where(i => i.Place != TF2.Descent.Server.Items.ItemPlace.BuyBack).Select(i => i.Id).Order(),
                     wire.Cache.Items.Select(i => i.Id));
        var melee = rig.Gear.Held.Inventory.Equipped["melee"];
        Assert.Equal(melee.Id, wire.Cache.In(ClientItems.Equipped, 2)!.Id);
    }

    //-------------------------------------------------------------------------

    [Fact]
    public void The_sheet_cache_holds_the_last_numbers_and_forgets_them_at_a_level_end()
    {
        var cache = new SheetCache(Content);
        var handler = new client::TF2.Descent.Shared.SheetNumbersMessage(new Hooks<client::TF2.Descent.Shared.ISheetNumbersHook>(cache));

        Deliver(handler, Bytes(new SheetNumbers { Fingerprint = Content.Fingerprint, Base = [20, 5, 20, 15], Armour = 7,
                                                  NowTenths = [], Cap = [], LifePerPointTenths = [], ManaPerPointTenths = [], ResistPercent = [] }));
        Assert.Equal(7, cache.Sheet!.Armour);
        Assert.Equal([20, 5, 20, 15], cache.Sheet.Base);

        cache.OnLevelShutdown();
        Assert.Null(cache.Sheet);
    }

    [Fact]
    public void A_level_init_keeps_the_sheet_and_ranks_that_arrived_before_it()
    {
        var sheet = new SheetCache(Content);
        var ranks = new RanksCache(Content);
        sheet.OnSheetNumbers(new client::TF2.Descent.Shared.SheetNumbersView(Content.Fingerprint, [1], [1], [1], [0], [0], [0], 75, 0, 0, 0, 0));
        ranks.OnSkillRanks(new client::TF2.Descent.Shared.SkillRanksView(Content.Fingerprint, 0, 1, [3], [ClientWhy.Buyable], [1]));

        ((IClientLevelHook)sheet).OnLevelInit("tf2_descent_town");
        ((IClientLevelHook)ranks).OnLevelInit("tf2_descent_town");

        Assert.NotNull(sheet.Sheet);
        Assert.True(ranks.Known);
    }

    [Fact]
    public void A_sheet_from_other_content_is_not_held()
    {
        var cache = new SheetCache(Content);

        cache.OnSheetNumbers(new client::TF2.Descent.Shared.SheetNumbersView(Content.Fingerprint ^ 1, [1], [1], [1], [0], [0], [0], 75, 0, 0, 0, 0));

        Assert.Null(cache.Sheet);
    }

    [Fact]
    public void The_rank_cache_reads_a_table_split_over_two_messages()
    {
        var cache = new RanksCache(Content);
        var handler = new client::TF2.Descent.Shared.SkillRanksMessage(new Hooks<client::TF2.Descent.Shared.ISkillRanksHook>(cache));

        Deliver(handler, Bytes(new SkillRanks { Fingerprint = Content.Fingerprint, First = 0, Total = 4, Ranks = [1, 0], Why = [0, 3], NextLevel = [1, 6] }));
        Deliver(handler, Bytes(new SkillRanks { Fingerprint = Content.Fingerprint, First = 2, Total = 4, Ranks = [0, 2], Why = [4, 0], NextLevel = [6, 12] }));

        Assert.True(cache.Known);
        Assert.Equal([1, 0, 0, 2], Enumerable.Range(0, 4).Select(cache.RankOf));
        Assert.Equal([ClientWhy.Buyable, ClientWhy.BelowLevel, ClientWhy.Prerequisite, ClientWhy.Buyable], Enumerable.Range(0, 4).Select(cache.WhyOf));
        Assert.Equal(12, cache.NextLevelOf(3));
    }

    [Fact]
    public void The_rank_cache_forgets_at_a_level_end()
    {
        var cache = new RanksCache(Content);
        cache.OnSkillRanks(new client::TF2.Descent.Shared.SkillRanksView(Content.Fingerprint, 0, 1, [3], [ClientWhy.Buyable], [1]));

        cache.OnLevelShutdown();

        Assert.False(cache.Known);
        Assert.Equal(0, cache.RankOf(0));
    }
}
