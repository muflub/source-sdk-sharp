using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Town;

/// <summary>Why a town verb was refused that the rules and the ledger do not say.</summary>
public static class TownRefusals
{
    /// <summary>This map is not the town.</summary>
    public const string NotInTown = "not_in_town";
    /// <summary>The hero has no town window open.</summary>
    public const string NoWindow = "no_window";
    /// <summary>The window open is another stall's: this one does not do that.</summary>
    public const string WrongVendor = "wrong_vendor";
    /// <summary>The hero walked away from the stall (its window is closed).</summary>
    public const string OutOfReach = "out_of_reach";
    /// <summary>The bench holds nothing this recipe can run on.</summary>
    public const string NoRecipe = "no_recipe";
    /// <summary>The Scout has no crate left in this restock.</summary>
    public const string NoCrate = "no_crate";
    /// <summary>Nothing the hero holds needs the Engineer.</summary>
    public const string NothingToRepair = "nothing_to_repair";
    /// <summary>The town may not send the hero to that depth (the rules' destinations).</summary>
    public const string DepthClosed = "depth_closed";
    /// <summary>Unhosted, no level of that depth is installed in the pool.</summary>
    public const string NoLevel = "no_level";
}

/// <summary>
/// THE TOWN'S STALLS (plan_descent_logic WO8; plan_tf2_rpg Phase 9, D15). In the town only, at
/// the level's start, a <see cref="TownVendor"/> stands at each stall's marker
/// (<see cref="TownStalls"/>). +USE within reach opens the stall's window for that hero alone
/// (its state and what it shows go first, then the panel's VGUIMenu); walking away closes it.
/// Each verb a window's buttons send (<see cref="TownCommands"/>) acts for the hero who sent it:
/// the town checks he is in town, at that stall and within reach, the ledger carries it out by
/// the rules, and the answer comes back on the window's own status line. While a window is open
/// what it shows follows every change to the hero's items and his price stats, and a vendor's
/// stock follows the rules' restock.
/// </summary>
[SideService(Side.Server)]
public sealed class TownService(
    ITownWorld world,
    IItemLedger ledger,
    Heroes heroes,
    TF2DescentRules rules,
    TownSender sender,
    TownCraft craft,
    ILog log)
    : IGameLifecycle, IFrameLifecycle, IClientLifecycle, IItemLedgerHook, IHeroStatsWatcher, IOrderedHook
{
    /// <summary>In TF2 Descent's town block, after the owner views (First + 1100 to 1130).</summary>
    public const int Order = ModStages.First + 1215;

    public int HookOrder => Order;

    public int LifecycleOrder => Order;

    public int ClientLifecycleOrder => Order;

    public int FrameOrder => Order;

    /// <summary>How far from where he stood when it opened (a stall, or the way down) a hero may walk before his window closes.</summary>
    public const float Reach = 160f;

    /// <summary>Frames between the reach and restock checks.</summary>
    public const int CheckEvery = 8;

    /// <summary>The rules' vendor of the crate stall.</summary>
    public const string CrateVendor = "scout";

    /// <summary>One hero's open window: which stall (-1 the way down), its screen, where he stood, and whether walking away closes it (a dev open does not).</summary>
    private sealed record Session(int Stall, TownScreen Screen, Vector Anchor, bool Reach);

    private readonly Dictionary<int, Session> _sessions = [];
    private readonly HashSet<int> _busy = [];
    private readonly List<(int Stall, int Entity, Vector At)> _placed = [];
    private readonly Dictionary<int, long> _restockShown = [];
    private double _openedAt;
    private int _frames;

    /// <summary>Whether this level is the town (its stalls stand).</summary>
    public bool InTown { get; private set; }

    /// <summary>The stalls standing on this level: their index, entity and place.</summary>
    public IReadOnlyList<(int Stall, int Entity, Vector At)> Placed => _placed;

    /// <summary>The restock a vendor shows now: the rules' over the town's age.</summary>
    public long Restock => rules.Vendors.Restock(world.Now - _openedAt);

    /// <summary>The window <paramref name="player"/> has open: its screen and stall; null for none.</summary>
    public (TownScreen Screen, int Stall)? Open(int player) =>
        _sessions.TryGetValue(player, out var s) ? (s.Screen, s.Stall) : null;

    //-------------------------------------------------------------------------
    // The level
    //-------------------------------------------------------------------------

    public void OnLevelReady(string mapName) => Place(mapName);

    /// <summary>On the town's map, each stall at its marker; anywhere else, none.</summary>
    public void Place(string map)
    {
        _placed.Clear();
        _sessions.Clear();
        InTown = world.IsTown(map);
        if (!InTown)
            return;
        _openedAt = world.Now;
        var stalls = TownStalls.All;
        for (int i = 0; i < stalls.Count; i++)
        {
            var stall = stalls[i];
            if (world.Marker(stall.Marker) is not { } marker)
            {
                log.Warning($"[TF2Descent/Town] {map} has no {stall.Marker}: the {stall.Id} stall stands nowhere\n");
                continue;
            }
            Vector at = world.Floor(marker + stall.Offset);
            // Facing the street down the town's long axis: a north-side stall faces south and the other way about.
            float yaw = marker.Y > 0 ? 270f : 90f;
            int entity = world.MakeStall(i, stall, at, yaw);
            if (entity <= 0)
            {
                log.Warning($"[TF2Descent/Town] the engine made no {VendorNetState.ClassName} for the {stall.Id} stall\n");
                continue;
            }
            _placed.Add((i, entity, at));
        }
        log.Msg($"[TF2Descent/Town] {map}: {_placed.Count} of {stalls.Count} stalls stand ({string.Join(", ", _placed.Select(p => stalls[p.Stall].Id))})\n");
    }

    public void OnLevelShutdown()
    {
        _placed.Clear();
        _sessions.Clear();
        _restockShown.Clear();
        InTown = false;
    }

    public void OnClientDisconnected(int entIndex)
    {
        _sessions.Remove(entIndex);
        _restockShown.Remove(entIndex);
    }

    //-------------------------------------------------------------------------
    // Opening and closing
    //-------------------------------------------------------------------------

    /// <summary>A hero used a stall's entity: its window opens for him.</summary>
    public void Use(int entity, int player)
    {
        int stall = world.StallOf(entity);
        if (stall >= 0)
            OpenStall(player, stall);
    }

    /// <summary>Opens stall <paramref name="stall"/>'s window for <paramref name="player"/>; with <paramref name="reach"/> false walking away leaves it open (a dev open).</summary>
    public bool OpenStall(int player, int stall, bool reach = true)
    {
        var stalls = TownStalls.All;
        if (!InTown || stall < 0 || stall >= stalls.Count || world.HeroOf(player) is null)
            return false;
        Vector anchor = _placed.FirstOrDefault(p => p.Stall == stall).At;
        return Begin(player, new Session(stall, stalls[stall].Screen, anchor, reach));
    }

    /// <summary>The way down was used: the depth picker opens for the hero, at where he stands.</summary>
    public bool OpenDepth(int player, bool reach = true)
    {
        if (!InTown || world.HeroOf(player) is null)
            return false;
        if (_sessions.TryGetValue(player, out var open) && open.Screen == TownScreen.Depth)
            return true;   // the shaft's trigger fires again while he falls: the picker is already up
        return Begin(player, new Session(-1, TownScreen.Depth, world.Feet(player) ?? default, reach));
    }

    private bool Begin(int player, Session session)
    {
        if (_sessions.TryGetValue(player, out var was) && was.Screen != session.Screen)
            world.Panel(player, TownSender.PanelOf(was.Screen), false);
        _sessions[player] = session;
        SyncPrices(player);
        _ = Refresh(player, opened: true);
        world.Panel(player, TownSender.PanelOf(session.Screen), true);
        string what = session.Stall >= 0 ? TownStalls.All[session.Stall].Id : "the way down";
        log.Msg($"[TF2Descent/Town] {player} opened {what} ({session.Screen})\n");
        return true;
    }

    /// <summary>Closes the hero's window, on the server and his client.</summary>
    public void Close(int player)
    {
        if (!_sessions.Remove(player, out var session))
            return;
        world.Panel(player, TownSender.PanelOf(session.Screen), false);
        log.Msg($"[TF2Descent/Town] {player} closed {(session.Stall >= 0 ? TownStalls.All[session.Stall].Id : "the way down")}\n");
    }

    public void OnPostThink(float frameTime)
    {
        if (!InTown || _sessions.Count == 0 || ++_frames % CheckEvery != 0)
            return;
        CheckReach();
        long restock = Restock;
        foreach (int player in _sessions.Keys.ToList())
            if (_restockShown.TryGetValue(player, out long shown) && shown != restock)
                _ = Refresh(player);
    }

    /// <summary>Every open window whose hero has walked away from where it opened closes.</summary>
    public void CheckReach()
    {
        foreach (var (player, session) in _sessions.ToList())
        {
            if (!session.Reach)
                continue;
            if (world.Feet(player) is not { } feet || Distance(feet, session.Anchor) > Reach)
                Close(player);
        }
    }

    private static float Distance(Vector a, Vector b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    //-------------------------------------------------------------------------
    // What a window shows
    //-------------------------------------------------------------------------

    private void SyncPrices(int player)
    {
        if (heroes.Of(player)?.Stats is { } stats && world.HeroOf(player) is { } key)
            ledger.SetPrices(key, rules.Pricing.BuyerOf(stats));
    }

    /// <summary>Sends <paramref name="player"/>'s open window what it shows now.</summary>
    public async Task Refresh(int player, bool opened = false)
    {
        if (!_sessions.TryGetValue(player, out var session) || world.HeroOf(player) is not { } key)
            return;
        if (ledger.Find(key) is null && !(await ledger.Load(key)).Ok)
            return;
        var stall = session.Stall >= 0 ? TownStalls.All[session.Stall] : null;
        long restock = Restock;
        IReadOnlyList<VendorItem> stock = [];
        if (stall?.Vendor is { } vendor && (session.Screen == TownScreen.Crate || IsShop(stall)))
            stock = (await ledger.Stock(key, vendor, restock)).Items;
        if (!_sessions.TryGetValue(player, out var still) || still != session || ledger.Find(key) is not { } h)
            return;   // closed, or another window opened, while the stock was asked for

        var buyBack = new List<(LedgerItem, long)>();
        if (stall is not null && IsShop(stall))
            for (int i = 0; i < h.Inventory.BuyBack.Count; i++)
                // As the rules' view shows it (the owner views leave buy-back out of the backpack's; here it is a ware).
                buyBack.Add((new LedgerItem(h.Inventory.BuyBack[i].Id, h.Inventory.BuyBack[i].Item, ItemPlace.Backpack, i),
                             ledger.BuyBackPrice(key, i) ?? 0));

        TownQuoteKind? kind = stall?.Stance switch
        {
            TownStance.Identify when session.Screen == TownScreen.Vendor => TownQuoteKind.Identify,
            TownStance.Repair when session.Screen == TownScreen.Vendor => TownQuoteKind.Repair,
            _ => null,
        };
        var quotes = kind is { } k ? Quotes(key, h, k) : [];
        long repairAll = kind == TownQuoteKind.Repair ? quotes.Sum(q => q.Price) : 0;

        var show = new TownShow(stall, Math.Max(0, session.Stall), session.Screen, stall?.Stance ?? TownStance.Weapons,
                                stock, buyBack, kind, quotes, repairAll,
                                rules.Inventory.StashSlots([h.Sheet]), StorageRank(h),
                                session.Screen == TownScreen.Craft ? CraftQuoteOf(h) : null,
                                session.Screen == TownScreen.Depth ? DepthRowsOf(player) : null,
                                session.Screen == TownScreen.Crate ? Odds() : null, opened);
        var hero = heroes.Of(player);
        sender.Show(player, show, hero?.Sheet ?? h.Sheet, hero?.Stats);
        _restockShown[player] = restock;
    }

    /// <summary>The published crate odds (the rules' <c>Crafting.GambleOdds</c>), each rarity's chance out of <see cref="TownWindow.OddsScale"/>.</summary>
    private List<(int Rarity, int Weight)> Odds() =>
        [.. rules.Crafting.GambleOdds().OrderBy(o => o.Key).Select(o => (o.Key, (int)Math.Round(o.Value * TownWindow.OddsScale, MidpointRounding.AwayFromZero)))];

    private static bool IsShop(TownStall stall) =>
        stall.Screen == TownScreen.Vendor && stall.Vendor is not null
        && stall.Stance is TownStance.Weapons or TownStance.Armour or TownStance.Healing or TownStance.Magic;

    private int StorageRank(LedgerHero h) =>
        h.Sheet.SkillRanks.TryGetValue(rules.Content.InventorySettings.Stash.Skill, out int rank) ? rank : 0;

    /// <summary>What the Spy or the Engineer charges for each item of the hero's he would act on: the backpack, and for a repair what he wears.</summary>
    private List<(long Id, long Price)> Quotes(string key, LedgerHero h, TownQuoteKind kind)
    {
        var what = kind == TownQuoteKind.Identify ? LedgerPrice.Identify : LedgerPrice.Repair;
        var items = h.Inventory.Backpack.Values.AsEnumerable();
        if (kind == TownQuoteKind.Repair)
            items = h.Inventory.Equipped.Values.Concat(items);
        var quotes = new List<(long, long)>();
        foreach (var held in items)
            if (ledger.Price(key, what, held.Item) is { } price && price > 0)
                quotes.Add((held.Id, price));
        return quotes;
    }

    private CraftQuote CraftQuoteOf(LedgerHero h)
    {
        int work = craft.Work(h);
        ItemInstance? item = work >= 0 ? h.Inventory.Backpack[work].Item : null;
        var costs = new int[TownCraft.RecipeCount];
        ushort ready = 0;
        for (int r = 0; r < TownCraft.RecipeCount; r++)
        {
            if (craft.Plan(h, (BenchRecipe)r) is not { } plan)
                continue;
            costs[r] = plan.Cost;
            if ((BenchRecipe)r != BenchRecipe.Reroll && craft.Ready(h, plan))
                ready |= (ushort)(1 << r);
        }
        int affixes = Math.Min(item?.Affixes.Length ?? 0, CraftQuote.MaxAffixes);
        var rerollCosts = new int[affixes];
        byte rerollReady = 0;
        for (int a = 0; a < affixes; a++)
        {
            if (craft.Plan(h, BenchRecipe.Reroll, a) is not { } plan)
                continue;
            rerollCosts[a] = plan.Cost;
            if (craft.Ready(h, plan))
                rerollReady |= (byte)(1 << a);
        }
        if (rerollReady != 0)
            ready |= 1 << (int)BenchRecipe.Reroll;
        return new CraftQuote
        {
            Item = work >= 0 ? h.Inventory.Backpack[work].Id : 0,
            Rerolls = (byte)Math.Clamp(item?.RerollCount ?? 0, 0, byte.MaxValue),
            Metal = craft.Metal(h),
            Costs = costs,
            Ready = ready,
            RerollCosts = rerollCosts,
            RerollReady = rerollReady,
        };
    }

    /// <summary>The depths the way down serves (the cage's and the camps'), each open when the rules let this hero pick it.</summary>
    public DepthRows DepthRowsOf(int player)
    {
        var open = world.Destinations(player).ToHashSet();
        var depths = Enumerable.Range(1, TownDescents.CageDepths).Append(TownDescents.FirstCamp).Append(TownDescents.SecondCamp).ToList();
        return new DepthRows
        {
            Depths = [.. depths.Select(d => (byte)d)],
            Flags = [.. depths.Select(d => (byte)((open.Contains(d) ? DepthFlags.Open : 0)
                                                  | (d is TownDescents.FirstBoss or TownDescents.SecondBoss or TownDescents.LastBoss ? DepthFlags.Boss : 0)))],
        };
    }

    //-------------------------------------------------------------------------
    // Following the hero
    //-------------------------------------------------------------------------

    public void OnItemsChanged(ItemsChanged change)
    {
        foreach (int player in _sessions.Keys.ToList())
            if (!_busy.Contains(player) && world.HeroOf(player) == change.Hero)
                _ = Refresh(player);
    }

    public void StatsChanged(HeroComponent hero)
    {
        if (hero.PlayerIndex <= 0 || !_sessions.ContainsKey(hero.PlayerIndex))
            return;
        SyncPrices(hero.PlayerIndex);
        if (!_busy.Contains(hero.PlayerIndex))
            _ = Refresh(hero.PlayerIndex);
    }

    public void HeroLeft(int playerIndex) => _sessions.Remove(playerIndex);

    //-------------------------------------------------------------------------
    // The verbs
    //-------------------------------------------------------------------------

    /// <summary>
    /// The hero's window, if a verb that <paramref name="fits"/> it may run: in town, a window
    /// open, the stall that does this, within reach. Null, the refusal told, otherwise.
    /// </summary>
    private (Session Session, string Key, TownStall? Stall)? Gate(int player, Func<Session, TownStall?, bool> fits)
    {
        string? why = null;
        Session? session = null;
        string? key = world.HeroOf(player);
        if (!InTown)
            why = TownRefusals.NotInTown;
        else if (key is null)
            why = LedgerRefusals.NoHero;
        else if (!_sessions.TryGetValue(player, out session))
            why = TownRefusals.NoWindow;
        else if (!fits(session, session.Stall >= 0 ? TownStalls.All[session.Stall] : null))
            why = TownRefusals.WrongVendor;
        else if (session.Reach && (world.Feet(player) is not { } feet || Distance(feet, session.Anchor) > Reach))
        {
            why = TownRefusals.OutOfReach;
            Close(player);
        }
        if (why is not null)
        {
            Refuse(player, why);
            return null;
        }
        SyncPrices(player);
        return (session!, key!, session!.Stall >= 0 ? TownStalls.All[session.Stall] : null);
    }

    private static bool Shop(Session s, TownStall? stall) => stall is not null && IsShop(stall);

    private static bool Stance(Session s, TownStall? stall, TownStance stance) =>
        s.Screen == TownScreen.Vendor && stall?.Stance == stance;

    /// <summary>The token a refusal's code is told by: worded ones their own, any other <c>#Descent_Town_Refused</c> with the code.</summary>
    public static string TokenOf(string code) =>
        TownStatusTokens.Worded.Contains(code) ? "#Descent_Town_" + code : TownStatusTokens.Refused;

    private void Refuse(int player, string code)
    {
        string token = TokenOf(code);
        sender.Status(player, token, token == TownStatusTokens.Refused ? code : "", error: true);
        log.Msg($"[TF2Descent/Town] {player} refused: {code}\n");
    }

    private string NameOf(ItemInstance item) =>
        item.Unique is { } unique && rules.Content.Uniques.TryGet(unique, out var u) ? u.Name
        : rules.Content.ItemBases.TryGet(item.BaseId, out var b) ? b.Name : item.BaseId;

    /// <summary>Runs a verb's ledger work for <paramref name="player"/>, then tells its answer and refreshes the window once.</summary>
    private async Task<LedgerResult?> Run(int player, string verb, Func<Task<LedgerResult>> work, Func<LedgerResult, (string Arg, long Item)>? done = null)
    {
        _busy.Add(player);
        string? key = world.HeroOf(player);
        // The purse before the whole verb: a crate is a buy and an open, two changes.
        long before = key is not null ? ledger.Find(key)?.Inventory.Australium ?? 0 : 0;
        try
        {
            var r = await work();
            long after = key is not null ? ledger.Find(key)?.Inventory.Australium ?? 0 : 0;
            if (r.Ok)
            {
                var (arg, item) = done?.Invoke(r) ?? ("", 0);
                sender.Status(player, TownStatusTokens.Done(verb), arg, error: false, item);
                log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"[TF2Descent/Town] {player} {verb} {arg}: done, Australium {before} -> {after}\n"));
            }
            else
            {
                Refuse(player, r.Refusal ?? InventoryRefusals.Refused);
            }
            return r;
        }
        finally
        {
            _busy.Remove(player);
            await Refresh(player);
        }
    }

    public Task<LedgerResult?> Buy(int player, long stockId)
    {
        if (Gate(player, Shop) is not { } g)
            return Task.FromResult<LedgerResult?>(null);
        return Run(player, "buy", () => ledger.Buy(g.Key, g.Stall!.Vendor!, stockId),
                   r => (Added(r) is { } a ? NameOf(a.Item) : "", stockId));
    }

    public Task<LedgerResult?> Sell(int player, int slot)
    {
        if (Gate(player, Shop) is not { } g)
            return Task.FromResult<LedgerResult?>(null);
        string name = ledger.Find(g.Key)?.Inventory.Backpack.GetValueOrDefault(slot) is { } held ? NameOf(held.Item) : "";
        return Run(player, "sell", () => ledger.Sell(g.Key, g.Stall!.Vendor!, slot), _ => (name, 0));
    }

    public Task<LedgerResult?> BuyBack(int player, int index)
    {
        if (Gate(player, Shop) is not { } g)
            return Task.FromResult<LedgerResult?>(null);
        return Run(player, "buyback", () => ledger.BuyBack(g.Key, g.Stall!.Vendor!, index),
                   r => (Changed(r) is { } c ? NameOf(c.Item) : "", Changed(r)?.Id ?? 0));
    }

    public Task<LedgerResult?> Identify(int player, int slot)
    {
        if (Gate(player, (s, st) => Stance(s, st, TownStance.Identify)) is not { } g)
            return Task.FromResult<LedgerResult?>(null);
        return Run(player, "identify", () => ledger.Identify(g.Key, slot),
                   r => (Changed(r) is { } c ? NameOf(c.Item) : "", Changed(r)?.Id ?? 0));
    }

    /// <summary>The Engineer's repair of one item: a backpack slot by its number, or what is worn in an equipment slot by its id.</summary>
    public Task<LedgerResult?> Repair(int player, string what)
    {
        if (Gate(player, (s, st) => Stance(s, st, TownStance.Repair)) is not { } g)
            return Task.FromResult<LedgerResult?>(null);
        return Run(player, "repair",
                   () => int.TryParse(what, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
                       ? ledger.Repair(g.Key, slot)
                       : ledger.RepairWorn(g.Key, what, WornRepair.Town),
                   r => (Changed(r) is { } c ? NameOf(c.Item) : "", Changed(r)?.Id ?? 0));
    }

    /// <summary>The Engineer's repair of everything that needs it, worn first, for the sum of the quotes; refused whole when the purse is short.</summary>
    public async Task<LedgerResult?> RepairAll(int player)
    {
        if (Gate(player, (s, st) => Stance(s, st, TownStance.Repair)) is not { } g || ledger.Find(g.Key) is not { } h)
            return null;
        var worn = h.Inventory.Equipped.Where(p => ledger.Price(g.Key, LedgerPrice.Repair, p.Value.Item) > 0).Select(p => p.Key).ToList();
        var carried = h.Inventory.Backpack.Where(p => ledger.Price(g.Key, LedgerPrice.Repair, p.Value.Item) > 0).Select(p => p.Key).ToList();
        long total = Quotes(g.Key, h, TownQuoteKind.Repair).Sum(q => q.Price);
        if (worn.Count + carried.Count == 0)
        {
            Refuse(player, TownRefusals.NothingToRepair);
            return null;
        }
        if (h.Inventory.Australium < total)
        {
            Refuse(player, InventoryRefusals.NotEnoughAustralium);
            return null;
        }
        return await Run(player, "repair_all", async () =>
        {
            LedgerResult last = LedgerResult.Refused(TownRefusals.NothingToRepair);
            foreach (string slot in worn)
                if (!(last = await ledger.RepairWorn(g.Key, slot, WornRepair.Town)).Ok)
                    return last;
            foreach (int slot in carried)
                if (!(last = await ledger.Repair(g.Key, slot)).Ok)
                    return last;
            return last;
        }, _ => (total.ToString(CultureInfo.InvariantCulture), 0));
    }

    /// <summary>A bench recipe (<see cref="BenchRecipe"/>, <paramref name="affix"/> a re-roll's) over the inputs the bench names (<see cref="TownCraft.Plan"/>).</summary>
    public Task<LedgerResult?> Craft(int player, int recipe, int affix)
    {
        if (Gate(player, (s, _) => s.Screen == TownScreen.Craft) is not { } g || ledger.Find(g.Key) is not { } h)
            return Task.FromResult<LedgerResult?>(null);
        if (recipe < 0 || recipe >= TownCraft.RecipeCount || craft.Plan(h, (BenchRecipe)recipe, affix) is not { } plan)
        {
            Refuse(player, TownRefusals.NoRecipe);
            return Task.FromResult<LedgerResult?>(null);
        }
        return Run(player, "craft",
                   () => plan.Recipe == BenchRecipe.Salvage ? ledger.Salvage(g.Key, plan.Slots[0]) : ledger.Craft(g.Key, plan.Name, plan.Slots),
                   r => (plan.Name, Added(r)?.Id ?? 0));
    }

    /// <summary>The crate stall: pay for the Scout's first crate of this restock and open it; the item it holds arrives unidentified.</summary>
    public async Task<LedgerResult?> Crate(int player)
    {
        if (Gate(player, (s, _) => s.Screen == TownScreen.Crate) is not { } g)
            return null;
        var stock = await ledger.Stock(g.Key, CrateVendor, Restock);
        if (stock.Items.Count == 0)
        {
            Refuse(player, TownRefusals.NoCrate);
            return null;
        }
        var crate = stock.Items[0];
        return await Run(player, "crate", async () =>
        {
            var bought = await ledger.Buy(g.Key, CrateVendor, crate.Id);
            if (!bought.Ok || ledger.Find(g.Key) is not { } h)
                return bought;
            int slot = h.Inventory.Backpack.FirstOrDefault(p => p.Value.Id == crate.Id) is { Value: not null } at ? at.Key : -1;
            return await ledger.OpenCrate(g.Key, slot);
        }, r => (Added(r) is { } a ? NameOf(a.Item) : "", Added(r)?.Id ?? 0));
    }

    public Task<LedgerResult?> StashIn(int player, int slot)
    {
        if (Gate(player, (s, _) => s.Screen == TownScreen.Stash) is not { } g)
            return Task.FromResult<LedgerResult?>(null);
        string name = ledger.Find(g.Key)?.Inventory.Backpack.GetValueOrDefault(slot) is { } held ? NameOf(held.Item) : "";
        return Run(player, "stash_in", () => ledger.Deposit(g.Key, slot), r => (name, Changed(r)?.Id ?? 0));
    }

    public Task<LedgerResult?> StashOut(int player, int slot)
    {
        if (Gate(player, (s, _) => s.Screen == TownScreen.Stash) is not { } g)
            return Task.FromResult<LedgerResult?>(null);
        string name = ledger.Find(g.Key)?.Stash.Slots.GetValueOrDefault(slot) is { } held ? NameOf(held.Item) : "";
        return Run(player, "stash_out", () => ledger.Withdraw(g.Key, slot), r => (name, Changed(r)?.Id ?? 0));
    }

    /// <summary>Goes down to <paramref name="depth"/>, when the rules let this hero pick it; the window closes as he goes.</summary>
    public bool Descend(int player, int depth)
    {
        if (Gate(player, (s, _) => s.Screen == TownScreen.Depth) is null)
            return false;
        if (!world.Destinations(player).Contains(depth))
        {
            Refuse(player, TownRefusals.DepthClosed);
            return false;
        }
        if (world.Descend(player, depth) is { } why)
        {
            Refuse(player, why);
            return false;
        }
        sender.Status(player, TownStatusTokens.Done("descend"), depth.ToString(CultureInfo.InvariantCulture), error: false);
        log.Msg($"[TF2Descent/Town] {player} descends to depth {depth}\n");
        Close(player);
        return true;
    }

    private static LedgerItem? Added(LedgerResult r) => r.Change?.Added.FirstOrDefault();

    private static LedgerItem? Changed(LedgerResult r) => r.Change?.Changed.FirstOrDefault() ?? r.Change?.Added.FirstOrDefault();
}

/// <summary>The status line's tokens (<c>#Descent_Town_*</c> in the mod's language file).</summary>
public static class TownStatusTokens
{
    /// <summary>A refusal the language file does not word: its code goes as the argument.</summary>
    public const string Refused = "#Descent_Town_Refused";

    /// <summary>A verb's success: <c>#Descent_Town_Done_&lt;verb&gt;</c>.</summary>
    public static string Done(string verb) => "#Descent_Town_Done_" + verb;

    /// <summary>Every verb that tells a success.</summary>
    public static IReadOnlyList<string> Verbs =>
        ["buy", "sell", "buyback", "identify", "repair", "repair_all", "craft", "crate", "stash_in", "stash_out", "descend"];

    /// <summary>The refusals the language file words, each <c>#Descent_Town_&lt;code&gt;</c>.</summary>
    public static IReadOnlyList<string> Worded =>
    [
        TownRefusals.NotInTown, TownRefusals.NoWindow, TownRefusals.WrongVendor, TownRefusals.OutOfReach, TownRefusals.NoRecipe,
        TownRefusals.NoCrate, TownRefusals.NothingToRepair, TownRefusals.DepthClosed, TownRefusals.NoLevel,
        InventoryRefusals.BackpackFull, InventoryRefusals.StashFull, InventoryRefusals.WithdrawOnly, InventoryRefusals.NoItem,
        InventoryRefusals.NotEnoughAustralium, InventoryRefusals.WrongClass,
        LedgerRefusals.NoHero, LedgerRefusals.NotInStock, LedgerRefusals.HostUnsupported,
        TF2.Descent.Rules.Economy.RepairRefusals.NotRepairable, TF2.Descent.Rules.Economy.RepairRefusals.AtMaximum,
        TF2.Descent.Rules.Economy.RepairRefusals.Unusable,
        TF2.Descent.Rules.Economy.CraftRefusals.NotEnoughMetal, TF2.Descent.Rules.Economy.CraftRefusals.WrongInputs,
        TF2.Descent.Rules.Economy.CraftRefusals.NotIdentified, TF2.Descent.Rules.Economy.CraftRefusals.AlreadyIdentified,
        TF2.Descent.Rules.Economy.CraftRefusals.CannotUpgrade, TF2.Descent.Rules.Economy.CraftRefusals.NotSalvageable,
    ];
}
