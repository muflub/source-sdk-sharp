using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Descent.Client.Content;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Items;

//=============================================================================
// WHAT THE OWNER'S CLIENT HOLDS OF HIS HERO (plan_descent_logic UI1, UI3): his
// items, his sheet's numbers and his skill ranks, as the server last sent
// them. Each cache is fed by its messages only, and forgets everything when a
// level ends (managed client services outlive a map; the server resyncs at
// the next spawn, whose messages arrive before the next level's init). Nothing here decides: the screens' sources read
// these and the content the client embeds.
//=============================================================================

/// <summary>The owner's items as the server last sent them (the item-view cache).</summary>
public interface IItemViews
{
    /// <summary>Counts every change: a source that keeps a list rebuilds it when this moves.</summary>
    int Version { get; }

    /// <summary>A resync has ended since the level began: the cache holds the whole inventory.</summary>
    bool Synced { get; }

    /// <summary>The server names rows of other tables than the client embeds: no item can be named rightly.</summary>
    bool ContentMismatch { get; }

    /// <summary>The Australium carried.</summary>
    long Australium { get; }

    /// <summary>The backpack's capacity as the server derives it.</summary>
    int BackpackCapacity { get; }

    /// <summary>Every item held, in id order.</summary>
    IReadOnlyList<OwnerItemView> Items { get; }

    /// <summary>The item in <paramref name="container"/>'s <paramref name="slot"/>, or null.</summary>
    OwnerItemView? In(ItemContainer container, int slot);
}

/// <summary>The item-view cache over the owner's item messages.</summary>
[SideService(Side.Client, As = [typeof(IItemViews)])]
public sealed class OwnerItemCache(DescentCatalog catalog, ILog log) : IItemViews, IOwnerItemsHook, IClientLevelHook, IOrderedHook
{
    /// <summary>In TF2 Descent's owner-views block (UI3): unique among the client's level hooks.</summary>
    public int HookOrder => 1100;

    private readonly SortedDictionary<long, OwnerItemView> _items = new();
    private OwnerItemView[] _list = [];
    private bool _told;

    public int Version { get; private set; }

    public bool Synced { get; private set; }

    public bool ContentMismatch { get; private set; }

    public long Australium { get; private set; }

    public int BackpackCapacity { get; private set; }

    public IReadOnlyList<OwnerItemView> Items => _list;

    public OwnerItemView? In(ItemContainer container, int slot)
    {
        foreach (var item in _list)
            if (item.Container == container && item.Slot == slot)
                return item;
        return null;
    }

    public void OnItem(OwnerItemView item)
    {
        _items[item.Id] = item;
        Changed();
    }

    public void OnItemGone(long id)
    {
        if (_items.Remove(id))
            Changed();
    }

    public void OnItems(OwnerItemsView sync)
    {
        Check(sync.Fingerprint);
        switch (sync.Phase)
        {
            case OwnerItemsPhase.Begin:
                _items.Clear();
                Synced = false;
                break;
            case OwnerItemsPhase.End:
                Synced = true;
                if (_items.Count != sync.Count)
                    log.Warning($"[TF2Descent/Items] the resync said {sync.Count} items and {_items.Count} came\n");
                break;
            case OwnerItemsPhase.State:
                Australium = sync.Australium;
                BackpackCapacity = sync.BackpackCapacity;
                break;
        }
        Changed();
    }

    // Not at a level's start: the spawn's resync arrives while the client signs on,
    // before the engine's LevelInitPostEntity, and forgetting there lost it.
    public void OnLevelShutdown() => Forget();

    private void Forget()
    {
        _items.Clear();
        Synced = false;
        ContentMismatch = false;
        Australium = 0;
        BackpackCapacity = 0;
        Changed();
    }

    private void Check(uint fingerprint)
    {
        ContentMismatch = fingerprint != catalog.Fingerprint;
        if (ContentMismatch && !_told)
        {
            _told = true;
            log.Warning($"[TF2Descent/Items] the server's content ({fingerprint:x8}) is not this client's ({catalog.Fingerprint:x8}): items cannot be named\n");
        }
    }

    private void Changed()
    {
        _list = [.. _items.Values];
        Version++;
    }
}

/// <summary>The owner's sheet numbers as the server last sent them; null before any.</summary>
public interface IHeroSheetNumbers
{
    SheetNumbersView? Sheet { get; }
}

/// <summary>The sheet cache over <c>hero_sheet</c>.</summary>
[SideService(Side.Client, As = [typeof(IHeroSheetNumbers)])]
public sealed class OwnerSheetCache(DescentCatalog catalog) : IHeroSheetNumbers, ISheetNumbersHook, IClientLevelHook, IOrderedHook
{
    public int HookOrder => 1101;

    public SheetNumbersView? Sheet { get; private set; }

    public void OnSheetNumbers(SheetNumbersView sheet) => Sheet = sheet.Fingerprint == catalog.Fingerprint ? sheet : null;

    // Not at a level's start: the spawn's resync arrives while the client signs on,
    // before the engine's LevelInitPostEntity, and forgetting there lost it.
    public void OnLevelShutdown() => Sheet = null;
}

/// <summary>The owner's skill ranks as the server last sent them, by the skill's index in the table.</summary>
public interface IHeroSkillRanks
{
    /// <summary>Whether any ranks have come since the level began.</summary>
    bool Known { get; }

    int RankOf(int skill);

    SkillWhy WhyOf(int skill);

    /// <summary>The hero level the skill's next rank asks; 0 at its top rank or when not known.</summary>
    int NextLevelOf(int skill);

    /// <summary>Whether every prerequisite of the skill is held, as the server says.</summary>
    bool PrerequisitesMetOf(int skill);
}

/// <summary>The rank cache over <c>skill_ranks</c>.</summary>
[SideService(Side.Client, As = [typeof(IHeroSkillRanks)])]
public sealed class OwnerSkillRanksCache(DescentCatalog catalog) : IHeroSkillRanks, ISkillRanksHook, IClientLevelHook, IOrderedHook
{
    public int HookOrder => 1102;

    private int[] _ranks = [];
    private SkillWhy[] _why = [];
    private int[] _next = [];
    private bool[] _prereq = [];

    public bool Known { get; private set; }

    public int RankOf(int skill) => skill >= 0 && skill < _ranks.Length ? _ranks[skill] : 0;

    public SkillWhy WhyOf(int skill) => skill >= 0 && skill < _why.Length ? _why[skill] : SkillWhy.UnknownSkill;

    public int NextLevelOf(int skill) => skill >= 0 && skill < _next.Length ? _next[skill] : 0;

    public bool PrerequisitesMetOf(int skill) => skill >= 0 && skill < _prereq.Length && _prereq[skill];

    public void OnSkillRanks(SkillRanksView ranks)
    {
        if (ranks.Fingerprint != catalog.Fingerprint)
            return;
        if (_ranks.Length != ranks.Total)
        {
            _ranks = new int[ranks.Total];
            _why = new SkillWhy[ranks.Total];
            Array.Fill(_why, SkillWhy.UnknownSkill);
            _next = new int[ranks.Total];
            _prereq = new bool[ranks.Total];
        }
        for (int i = 0; i < ranks.Ranks.Count && ranks.First + i < _ranks.Length; i++)
        {
            _ranks[ranks.First + i] = ranks.Ranks[i];
            _why[ranks.First + i] = i < ranks.Why.Count ? ranks.Why[i] : SkillWhy.UnknownSkill;
            _next[ranks.First + i] = i < ranks.NextLevel.Count ? ranks.NextLevel[i] : 0;
            _prereq[ranks.First + i] = ranks.PrerequisitesMet is { } met && i < met.Count && met[i];
        }
        Known = true;
    }

    // Not at a level's start: the spawn's resync arrives while the client signs on,
    // before the engine's LevelInitPostEntity, and forgetting there lost it.
    public void OnLevelShutdown() => Forget();

    private void Forget()
    {
        _ranks = [];
        _why = [];
        _next = [];
        _prereq = [];
        Known = false;
    }
}

/// <summary>The client's dispatch of the owner's messages (plan_descent_logic UI3).</summary>
[SideService(Side.Client)]
public sealed class OwnerViewsClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<IOwnerItemsHook>();
        registry.AddHooks<ISheetNumbersHook>();
        registry.AddHooks<ISkillRanksHook>();
    }
}
