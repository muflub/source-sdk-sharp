using SourceSharp.Abi;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Server.Items;

/// <summary>
/// The <see cref="IItemLedger"/> game code is given: the hosted ledger when this server runs
/// against a hosting service, the in-memory one when it runs <c>-unhosted</c> (E8), chosen by
/// <see cref="HostService.Mode"/> once the server is up and said in the log. It also keeps the
/// floor per level, loads an unhosted hero when their client becomes active, and drops a
/// hosted hero from the cache when they leave (the host is the record between instances).
/// </summary>
[SideService(Side.Server, As = [typeof(IItemLedger)])]
public sealed class ItemLedger(
    HostService host,
    InMemoryItemLedger memory,
    HostedItemLedger hosted,
    HeroKeys keys,
    ILog log) : IItemLedger, IGameLifecycle, IClientLifecycle
{
    /// <summary>After HostService (First + 10), which decides the mode at PostInit.</summary>
    public int LifecycleOrder => ModStages.First + 73;

    /// <summary>Before PlayerService (First + 10) forgets who a leaving client was.</summary>
    public int ClientLifecycleOrder => ModStages.First + 7;

    private ItemLedgerBase Current => host.Mode == HostingMode.Hosted ? hosted : memory;

    public void OnPostInit()
    {
        hosted.InHub = () => host.Boot?.InstanceKind == "hub";
        log.Msg(Current.Kind == LedgerKind.Hosted
            ? "[TF2Descent] item ledger: hosted (the hosting service's)\n"
            : $"[TF2Descent] item ledger: in-memory (unhosted), session seed 0x{memory.SessionSeed:X}\n");
    }

    /// <summary>A new level: the last level's floor and offers are gone; heroes stay.</summary>
    public void OnLevelInit(string mapName)
    {
        memory.NewLevel();
        hosted.NewLevel();
    }

    /// <summary>Unhosted, a hero is loaded (made, with the starting kit, the first time) as their client becomes active.</summary>
    public void OnClientActive(int entIndex)
    {
        if (host.Mode == HostingMode.Hosted || keys.Of(entIndex) is not { } hero)
            return;
        _ = memory.Load(hero);
    }

    public void OnClientDisconnected(int entIndex)
    {
        if (keys.Of(entIndex) is { } hero)
            Current.Forget(hero);
    }

    public LedgerKind Kind => Current.Kind;
    public IReadOnlyCollection<FloorItem> Floor => Current.Floor;
    public Task<LedgerResult> Load(string hero) => Current.Load(hero);
    public LedgerHero? Find(string hero) => Current.Find(hero);
    public void SetSheet(string hero, HeroSheet sheet) => Current.SetSheet(hero, sheet);
    public Task<DropResult> KillDrop(KillDrop kill) => Current.KillDrop(kill);
    public Task<DropResult> BossDrop(BossDrop boss) => Current.BossDrop(boss);
    public Task<DropResult> PotionDrop(string hero, string potionBase, uint killSeq, int depth, Vector position) =>
        Current.PotionDrop(hero, potionBase, killSeq, depth, position);
    public Task<DropResult> ContainerDrop(ContainerDrop drop) => Current.ContainerDrop(drop);
    public Task<LedgerResult> Claim(string hero, long floorId) => Current.Claim(hero, floorId);
    public Task<LedgerResult> CollectAustralium(string hero, long amount) => Current.CollectAustralium(hero, amount);
    public Task<LedgerResult> Drop(string hero, int slot, Vector position) => Current.Drop(hero, slot, position);
    public Task<LedgerResult> Move(string hero, int from, int to) => Current.Move(hero, from, to);
    public Task<LedgerResult> Stack(string hero, int from, int to) => Current.Stack(hero, from, to);
    public Task<LedgerResult> Equip(string hero, int from, string slot) => Current.Equip(hero, from, slot);
    public Task<LedgerResult> Unequip(string hero, string slot) => Current.Unequip(hero, slot);
    public Task<LedgerResult> ToBelt(string hero, int from, int index) => Current.ToBelt(hero, from, index);
    public Task<LedgerResult> FromBelt(string hero, int index) => Current.FromBelt(hero, index);
    public Task<LedgerResult> Drink(string hero, int index) => Current.Drink(hero, index);
    public Task<LedgerResult> Deposit(string hero, int from) => Current.Deposit(hero, from);
    public Task<LedgerResult> Withdraw(string hero, int stashSlot) => Current.Withdraw(hero, stashSlot);
    public Task<VendorAnswer> Stock(string hero, string vendor) => Current.Stock(hero, vendor);
    public Task<LedgerResult> Buy(string hero, string vendor, long stockId) => Current.Buy(hero, vendor, stockId);
    public Task<LedgerResult> Sell(string hero, string vendor, int slot) => Current.Sell(hero, vendor, slot);
    public Task<LedgerResult> BuyBack(string hero, string vendor, int index) => Current.BuyBack(hero, vendor, index);
    public Task<LedgerResult> Identify(string hero, int slot) => Current.Identify(hero, slot);
    public Task<LedgerResult> Repair(string hero, int slot) => Current.Repair(hero, slot);
    public Task<LedgerResult> Craft(string hero, string recipe, IReadOnlyList<int> slots) => Current.Craft(hero, recipe, slots);
    public Task<LedgerResult> Salvage(string hero, int slot) => Current.Salvage(hero, slot);
    public Task<LedgerResult> OpenCrate(string hero, int slot) => Current.OpenCrate(hero, slot);
    public Task<LedgerResult> Give(string hero, string baseId, int rarity, int itemLevel) => Current.Give(hero, baseId, rarity, itemLevel);
    public Task<bool> RecordDeath(string hero, Vector position) => Current.RecordDeath(hero, position);
    public Task<LootAnswer> LootCorpse(string hero) => Current.LootCorpse(hero);
    public Task<LedgerResult> Consume(string hero, int slot) => Current.Consume(hero, slot);
    public Task<LedgerResult> Wear(string hero, IReadOnlyList<WearEvents> events) => Current.Wear(hero, events);
    public Task<LedgerResult> RepairWorn(string hero, string equipSlot, WornRepair how) => Current.RepairWorn(hero, equipSlot, how);
    public Task<LedgerResult> SetDurability(string hero, string equipSlot, int durability, int maxDurability) =>
        Current.SetDurability(hero, equipSlot, durability, maxDurability);
    public void Forget(string hero) => Current.Forget(hero);
}
