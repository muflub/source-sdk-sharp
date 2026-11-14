using System.Collections.Immutable;
using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Server.Buildings;
using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Gear;

/// <summary>
/// A hero's kit follows his equipment where he stands (plan_descent_logic HE4).
///
/// <list type="bullet">
/// <item><b>An equipment change</b> (the ledger's <see cref="IItemLedgerHook"/>: an equip, an
/// unequip, a worn item changed in place): the hero's stats are recomputed, then, while he is
/// alive and his weapon slots would now be answered otherwise than his kit was built from,
/// the kit is rebuilt in place (<see cref="ITFKitRefresh"/>: no heal, no move; the held weapon
/// stays while it exists, else the slot's successor or the first weapon left; each fresh
/// weapon made ready as a spawn's is). Last, the health and every ammunition pool are held
/// under a maximum that fell: cut to the new one, never raised. A change that leaves the
/// weapon slots as they were (two trinkets swapped) rebuilds nothing.</item>
/// <item><b>A sheet change</b> (a level-up, a point spent) can wake or put to sleep a weapon's
/// rolled attributes (a requirement met or lost): the contribution source marks the hero
/// (<see cref="Check"/>), and the end of the frame rebuilds his kit if its answer differs.</item>
/// <item><b>The first kit</b> (unhosted): a hero the ledger loads for the first time in this
/// process with nothing equipped has his class's starting weapons
/// (<see cref="StartingKit"/>) equipped from the backpack through the ledger, each into its
/// base's slot, once; never again for that hero, whatever he equips or takes off later.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroKitSettle(
    HeroEquipment equipment,
    HeroKitSource source,
    Heroes heroes,
    StartingKit kit,
    ITFKitRefresh refresh,
    ITFPlayerResupply resupply,
    ITFAmmoPurse purse,
    ITFPlayerClasses classes,
    IEngineEntities entities,
    IEnginePlayers players,
    ILog log) : IItemLedgerHook, IFrameLifecycle, IClientLifecycle, IOrderedHook
{
    /// <summary>In TF2 Descent's equipment block (the ledger hook's chain).</summary>
    public const int Order = ModStages.First + 402;

    /// <summary>The ammunition a maximum can fall for (the TF pools a class carries).</summary>
    private static ReadOnlySpan<TFAmmo> Pools => [TFAmmo.Primary, TFAmmo.Secondary, TFAmmo.Metal, TFAmmo.Grenades1, TFAmmo.Grenades2, TFAmmo.Grenades3];

    private readonly HashSet<int> _pending = [];
    private readonly HashSet<string> _firstKit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImmutableSortedDictionary<string, Held>> _seen = new(StringComparer.Ordinal);

    public int HookOrder => Order;

    /// <summary>
    /// Its own place in the frame (the driver refuses a tie; the default 0 is TF2's
    /// autobalance's), after TF2's band and clear of the AI's First + 400..665.
    /// </summary>
    public int FrameOrder => ModStages.First + 702;

    /// <summary>After the ledger (First + 7) and the services that place a joining hero.</summary>
    public int ClientLifecycleOrder => ModStages.First + 403;

    /// <summary>The heroes marked for the end of this frame.</summary>
    public IReadOnlyCollection<int> Pending => _pending;

    /// <summary>The heroes whose first kit has been put on (by ledger key).</summary>
    public IReadOnlyCollection<string> FirstKitDone => _firstKit;

    /// <summary>The hero's sheet was recomputed: his kit is checked at the end of the frame.</summary>
    public void Check(int playerIndex) => _pending.Add(playerIndex);

    public void OnItemsChanged(ItemsChanged change)
    {
        if (change.Operation == "load")
            FirstKit(change.Hero);

        if (equipment.Ledger.Find(change.Hero) is not { } hero)
            return;
        var now = hero.Inventory.Equipped;
        bool changed = !_seen.TryGetValue(change.Hero, out var was) ? now.Count > 0 : !Same(was, now);
        _seen[change.Hero] = now;
        if (!changed)
            return;

        foreach (var h in heroes.All)
        {
            if (h.PlayerIndex >= 0 && equipment.HeroOf(h.PlayerIndex) == change.Hero)
                Settle(h.PlayerIndex, h, change.Operation);
        }
    }

    public void OnPostThink(float frameTime)
    {
        if (_pending.Count == 0)
            return;
        foreach (int index in _pending.ToArray())
        {
            _pending.Remove(index);
            var player = players.GetHandle(index);
            if (!Armed(player) || equipment.HeroOf(index) is null)
                continue;
            if (source.Differs(index, classes.ClassOf(player)))
                Rebuild(index, player, "sheet");
        }
    }

    public void OnClientDisconnected(int entIndex)
    {
        _pending.Remove(entIndex);
        source.Forget(entIndex);
    }

    /// <summary>
    /// The hero in <paramref name="index"/> after his equipment changed: the stats again,
    /// then the kit where he stands when its answer differs, then the maxima held.
    /// </summary>
    public void Settle(int index, HeroComponent? hero, string why)
    {
        var player = players.GetHandle(index);
        bool alive = Armed(player);
        var before = alive ? Maxima(player) : default;

        hero?.Recompute();
        _pending.Remove(index);
        if (!alive)
            return;

        bool rebuilt = source.Differs(index, classes.ClassOf(player)) && refresh.Refresh(player);
        Hold(player, before);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Equipment] {index} {why}: kit {(rebuilt ? "rebuilt" : "unchanged")}, health {entities.GetHealth(player)}/{resupply.MaxHealth(player)}\n"));
    }

    /// <summary>A player who carries a kit now: alive and of a class (a joining hero is alive before he has one).</summary>
    private bool Armed(EntityHandle player) =>
        player != EntityHandle.None && players.IsAlive(player)
        && classes.ClassOf(player) is > TFClass.Undefined and < TFClass.CountAll;

    private void Rebuild(int index, EntityHandle player, string why)
    {
        var before = Maxima(player);
        bool rebuilt = refresh.Refresh(player);
        Hold(player, before);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Equipment] {index} {why}: kit {(rebuilt ? "rebuilt" : "unchanged")}, health {entities.GetHealth(player)}/{resupply.MaxHealth(player)}\n"));
    }

    /// <summary>The player's health maximum and each pool's, as they stand.</summary>
    private (int Health, int[] Ammo) Maxima(EntityHandle player)
    {
        var ammo = new int[Pools.Length];
        for (int i = 0; i < Pools.Length; i++)
            ammo[i] = resupply.MaxAmmo(player, Pools[i]);
        return (resupply.MaxHealth(player), ammo);
    }

    /// <summary>The health and each pool held under a maximum that fell since <paramref name="before"/>; nothing is raised.</summary>
    private void Hold(EntityHandle player, (int Health, int[] Ammo) before)
    {
        int max = resupply.MaxHealth(player);
        if (max < before.Health && entities.GetHealth(player) > max)
            entities.SetHealth(player, max);

        var pools = purse.Pools(player);
        for (int i = 0; i < Pools.Length; i++)
        {
            int now = resupply.MaxAmmo(player, Pools[i]);
            string name = TFAmmoNames.Name(Pools[i]);
            if (now < before.Ammo[i] && pools.TryGetValue(name, out int count) && count > now)
                purse.TakeAmmo(player, count - now, name);
        }
    }

    private static bool Same(ImmutableSortedDictionary<string, Held> a, ImmutableSortedDictionary<string, Held> b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.Count != b.Count)
            return false;
        foreach (var (slot, held) in a)
        {
            if (!b.TryGetValue(slot, out var other) || !ReferenceEquals(held, other))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The first kit (see the class summary): once a hero, in-memory only, and only for a
    /// hero with nothing equipped. Each weapon of the class's starting kit in the backpack
    /// goes into its base's slot, through the ledger, so the rules decide each.
    /// </summary>
    private void FirstKit(string key)
    {
        if (equipment.Ledger.Kind != LedgerKind.InMemory || !_firstKit.Add(key))
            return;
        if (equipment.Ledger.Find(key) is not { } hero || hero.Inventory.Equipped.Count > 0)
            return;

        var bases = equipment.Rules.Content.ItemBases;
        var starting = kit.WeaponsOf(hero.Sheet.ClassId);
        var plan = new List<(int From, string Slot, string Base)>();
        foreach (var (index, held) in hero.Inventory.Backpack)
        {
            var row = bases[held.Item.BaseId];
            if (row.Kind == ItemKinds.Weapon && starting.Contains(row.Id) && plan.All(p => p.Slot != row.Slot))
                plan.Add((index, row.Slot, row.Id));
        }
        if (plan.Count > 0)
            _ = EquipFirstKit(key, plan);
    }

    private async Task EquipFirstKit(string key, IReadOnlyList<(int From, string Slot, string Base)> plan)
    {
        var done = new List<string>();
        foreach (var (from, slot, baseId) in plan)
        {
            var r = await equipment.Ledger.Equip(key, from, slot);
            done.Add(r.Ok ? $"{slot} {baseId}" : $"{slot} {baseId} refused: {r.Refusal}");
        }
        log.Msg($"[TF2Descent/Equipment] {key}: first kit equipped: {string.Join(", ", done)}\n");
    }
}
