using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

using TF2.Descent.Client.Content;
using TF2.Descent.Client.Items;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// THE HERO'S OWN WINDOWS OVER LIVE DATA (plan_descent_logic UI3): the
// character sheet, the inventory and the skill tree. Each source reads the
// hero state every client holds (IHeroStates), what only the owner is sent
// (his item views, sheet numbers and skill ranks: client/Items) and the
// content the client embeds (DescentCatalog). An action asks the server with
// the hero's own verb (IDescentCommandLine); what the server decided comes
// back through the same caches. The spellbook stays empty here (its scrolls
// and tomes are the belt package's).
//=============================================================================

/// <summary>The rules' class id of a TF2 class number (3 is "soldier"); "" for none.</summary>
public static class HeroClassIds
{
    public static string Of(int classId) => classId switch
    {
        1 => "scout",
        2 => "sniper",
        3 => "soldier",
        4 => "demoman",
        5 => "medic",
        6 => "heavy",
        7 => "pyro",
        8 => "spy",
        9 => "engineer",
        _ => "",
    };
}

//-----------------------------------------------------------------------------
// The character sheet
//-----------------------------------------------------------------------------

/// <summary>
/// The sheet's spend plan: points staged on attributes by the + buttons, held
/// on the client until Apply sends each as <c>tf2_descent_spend &lt;attribute&gt;
/// &lt;count&gt;</c>. Staging never passes the points held or an attribute's
/// cap (the server's numbers); the server decides the spend itself.
/// </summary>
[SideService(Side.Client)]
public sealed class SheetPlan(IHeroStates heroes, IHeroSheetNumbers numbers, DescentCatalog catalog, IDescentCommandLine line)
{
    private readonly int[] _pending = new int[SheetNumbers.MaxRows];

    public int Pending(int attribute) => attribute >= 0 && attribute < _pending.Length ? _pending[attribute] : 0;

    public int Staged => _pending.Sum();

    public void Stage(int attribute, int count)
    {
        if (numbers.Sheet is not { } sheet || attribute < 0 || attribute >= sheet.Base.Count || attribute >= _pending.Length || count <= 0)
            return;
        int points = heroes.Local()?.AttributePoints ?? 0;
        int room = Math.Min(points - Staged, sheet.Cap[attribute] - sheet.Base[attribute] - _pending[attribute]);
        _pending[attribute] += Math.Max(0, Math.Min(count, room));
    }

    public void Apply()
    {
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i] > 0 && DescentCatalog.At(catalog.Attributes, i) is { } attribute)
                line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_spend {attribute.Id} {_pending[i]}"));
            _pending[i] = 0;
        }
    }

    public void Reset() => Array.Clear(_pending);
}

/// <summary>
/// The character sheet (<see cref="IDescentSheet"/>): the hero state's
/// scalars (name, class, level, experience, life, mana, points), the owner's
/// sheet numbers (each attribute's base, now and cap; resistances and their
/// cap; armour; crit; the weapon in hand's damage range), the Australium the
/// item views carry, and the points staged by the plan, with the maxima they
/// would give by the server's own per-point numbers.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentSheet)])]
public sealed class SheetSource(IHeroStates heroes, IEngineHud hud, IPlayerNames names, IHeroSheetNumbers numbers,
                                IItemViews items, DescentCatalog catalog, SheetPlan plan) : IDescentSheet
{
    public string Name => heroes.Local() is { } h && names.TryGet(h.PlayerIndex, out var info) ? info.Name : "";

    public string ClassToken => heroes.Local() is { } h ? HeroClassTokens.Of(h.ClassId) : "";

    public int Level => heroes.Local()?.Level ?? 0;

    public long Xp => heroes.Local()?.Xp ?? 0;

    public long XpToNext => heroes.Local() is { } h ? Math.Max(0, h.NextLevelXp - h.Xp) : 0;

    public long Australium => items.Australium;

    private SheetNumbersView? Numbers => heroes.Local() is null ? null : numbers.Sheet;

    public int AttributeCount => Numbers is { } n ? Math.Min(n.Base.Count, catalog.Attributes.Count) : 0;

    public DescentAttribute Attribute(int i)
    {
        if (Numbers is not { } n || i < 0 || i >= AttributeCount)
            return default;
        return new DescentAttribute(catalog.Attributes[i].NameToken, n.Base[i],
                                    Math.Max(1, (int)Math.Round(n.NowTenths[i] / 10.0, MidpointRounding.AwayFromZero)),
                                    n.Cap[i], plan.Pending(i));
    }

    public int AttributePoints => heroes.Local()?.AttributePoints ?? 0;

    public int Life => heroes.Local() is null ? 0 : Math.Max(0, hud.LocalPlayerHealth);

    public int LifeMax => heroes.Local()?.LifeMax ?? 0;

    public int Mana => heroes.Local()?.Mana ?? 0;

    public int ManaMax => heroes.Local()?.ManaMax ?? 0;

    public int Armour => Numbers?.Armour ?? 0;

    public int CritPercent => Numbers is { } n ? (int)Math.Round(n.CritTenths / 10.0, MidpointRounding.AwayFromZero) : 0;

    public int DamageMin => Numbers?.DamageMin ?? 0;

    public int DamageMax => Numbers?.DamageMax ?? 0;

    public int ResistCount => Numbers is { } n ? Math.Min(n.ResistPercent.Count, catalog.DamageTypes.Count) : 0;

    public string ResistLabel(int i) => DescentCatalog.At(catalog.DamageTypes, i) is { } t ? "#Descent_Type_" + t.Name : "";

    public int ResistPercent(int i) => Numbers is { } n && i >= 0 && i < n.ResistPercent.Count ? n.ResistPercent[i] : 0;

    public int ResistCap => Numbers?.ResistCap ?? 0;

    public bool HasPartyReviveHint => false;

    public int PendingLifeMax => LifeMax + Staged(n => n.LifePerPointTenths);

    public int PendingManaMax => ManaMax + Staged(n => n.ManaPerPointTenths);

    /// <summary>What the staged points add, by the server's per-point tenths.</summary>
    private int Staged(Func<SheetNumbersView, IReadOnlyList<int>> perPoint)
    {
        if (Numbers is not { } n)
            return 0;
        var tenths = perPoint(n);
        int sum = 0;
        for (int i = 0; i < tenths.Count; i++)
            sum += plan.Pending(i) * tenths[i];
        return (int)Math.Floor(sum / 10.0);
    }
}

/// <summary>The sheet's buttons (<see cref="IDescentSheetActions"/>): stage, apply and reset the plan; the pointer's row.</summary>
[SideService(Side.Client, As = [typeof(IDescentSheetActions)])]
public sealed class SheetActions(SheetPlan plan, SheetPointer pointer) : IDescentSheetActions
{
    public void Stage(int attributeIndex, int count) => plan.Stage(attributeIndex, count);

    public void Apply() => plan.Apply();

    public void Reset() => plan.Reset();

    public void Hover(int attributeIndex, int derivedIndex) => pointer.Report(attributeIndex, derivedIndex);
}

/// <summary>
/// The sheet's hover hint (<see cref="IDescentSheetHover"/>): for an
/// attribute, what each point of it gives (the content's per-point stats, and
/// the life and mana a point adds by the server's numbers); for a derived
/// plate, what it is and its value.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentSheetHover)])]
public sealed class SheetHoverSource(SheetPointer pointer, IHeroSheetNumbers numbers, DescentCatalog catalog, IDescentSheet sheet)
    : IDescentSheetHover
{
    private readonly List<(string Token, string Good)> _lines = [];
    private (int Row, int Other, SheetNumbersView? Sheet) _built = (-2, -2, null);

    public int HoverAttribute => pointer.Active && pointer.Row >= 0 && pointer.Row < catalog.Attributes.Count ? pointer.Row : -1;

    public int HoverDerived => pointer.Active && pointer.Row < 0 && pointer.Other is >= 0 and <= 4 ? pointer.Other : -1;

    public bool Active => HoverAttribute >= 0 || HoverDerived >= 0;

    public string TitleToken => HoverAttribute >= 0 ? catalog.Attributes[HoverAttribute].NameToken
        : HoverDerived switch
        {
            0 => "#Descent_Hud_Life",
            1 => "#Descent_Hud_Mana",
            2 => "#Descent_Sheet_Armour",
            3 => "#Descent_Sheet_Crit",
            4 => "#Descent_Sheet_Damage",
            _ => "",
        };

    public string TitleData => "";

    public string SubtitleData => "";

    public int LineCount => Lines().Count;

    public string LineToken(int i) => i >= 0 && i < Lines().Count ? Lines()[i].Token : "";

    public string LineData(int i) => "";

    public string LineGood(int i) => i >= 0 && i < Lines().Count ? Lines()[i].Good : "";

    public bool LineGoodNegative(int i) => LineGood(i).StartsWith('-');

    public string ValueToken(int i) => "";

    public string ValueData(int i) => i != 0 || HoverDerived < 0 ? "" : HoverDerived switch
    {
        0 => string.Create(CultureInfo.InvariantCulture, $"{sheet.Life} / {sheet.LifeMax}"),
        1 => string.Create(CultureInfo.InvariantCulture, $"{sheet.Mana} / {sheet.ManaMax}"),
        2 => sheet.Armour.ToString(CultureInfo.InvariantCulture),
        3 => string.Create(CultureInfo.InvariantCulture, $"{sheet.CritPercent} %"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{sheet.DamageMin} - {sheet.DamageMax}"),
    };

    public bool ValueGood(int i) => false;

    /// <summary>The hint's lines, built again only when the subject or the numbers change.</summary>
    private List<(string Token, string Good)> Lines()
    {
        var key = (pointer.Active ? pointer.Row : -1, pointer.Active ? pointer.Other : -1, numbers.Sheet);
        if (key == _built)
            return _lines;
        _built = key;
        _lines.Clear();
        if (HoverAttribute is int a and >= 0)
        {
            var attribute = catalog.Attributes[a];
            foreach (var p in attribute.PerPoint)
                _lines.Add((DescentCatalog.StatToken(p.Stat), ItemViewNames.Signed(p.Amount)));
            if (numbers.Sheet is { } n && a < n.LifePerPointTenths.Count && n.LifePerPointTenths[a] != 0)
                _lines.Add((DescentCatalog.StatToken("max_life"), ItemViewNames.Signed(n.LifePerPointTenths[a] / 10.0)));
            if (numbers.Sheet is { } m && a < m.ManaPerPointTenths.Count && m.ManaPerPointTenths[a] != 0)
                _lines.Add((DescentCatalog.StatToken("max_mana"), ItemViewNames.Signed(m.ManaPerPointTenths[a] / 10.0)));
        }
        else if (HoverDerived is int d and >= 0)
        {
            _lines.Add((d switch
            {
                0 => "#Descent_Sheet_Life_Detail",
                1 => "#Descent_Sheet_Mana_Detail",
                2 => "#Descent_Sheet_Armour_Detail",
                3 => "#Descent_Sheet_Crit_Detail",
                _ => "#Descent_Sheet_Damage_Detail",
            }, ""));
        }
        return _lines;
    }
}

//-----------------------------------------------------------------------------
// The inventory
//-----------------------------------------------------------------------------

/// <summary>
/// The paper doll (<see cref="IDescentEquipment"/>): inventory.json's nine
/// equipment slots, every one earned (no slot is skill-gated in the content),
/// each holding what the owner's item views put there.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentEquipment)])]
public sealed class EquipmentSource(IHeroStates heroes, IItemViews items, ItemViewNames names, DescentCatalog catalog) : IDescentEquipment
{
    /// <summary>The doll's position of an equipment slot id; null for a slot the doll does not draw.</summary>
    public static DescentEquipSlot? SlotOf(string id) => id switch
    {
        "primary" => DescentEquipSlot.Primary,
        "secondary" => DescentEquipSlot.Secondary,
        "melee" => DescentEquipSlot.Melee,
        "hat" => DescentEquipSlot.Hat,
        "body" => DescentEquipSlot.Body,
        "feet" => DescentEquipSlot.Feet,
        "trinket1" => DescentEquipSlot.Trinket1,
        "trinket2" => DescentEquipSlot.Trinket2,
        "amulet" => DescentEquipSlot.Amulet,
        _ => null,
    };

    public int SlotCount => heroes.Local() is null ? 0 : catalog.EquipSlots.Count;

    public DescentEquipSlot Slot(int i) => DescentCatalog.At(catalog.EquipSlots, i) is { } s && SlotOf(s.Id) is { } slot ? slot : (DescentEquipSlot)i;

    public bool SlotEarned(int i) => true;

    public DescentItemView? SlotItem(int i) =>
        items.ContentMismatch ? null : items.In(ItemContainer.Equipped, i) is { } item ? names.View(item) : null;
}

/// <summary>
/// The backpack (<see cref="IDescentItemGridSource"/>): the owner's backpack
/// items by cell, the capacity the server derives, and the withdraw-only tail
/// when an item sits past it.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentItemGridSource)])]
public sealed class BackpackSource(IItemViews items, ItemViewNames names) : IDescentItemGridSource
{
    public int CellCount
    {
        get
        {
            if (items.ContentMismatch)
                return 0;
            int n = 0;
            foreach (var item in items.Items)
                if (item.Container == ItemContainer.Backpack)
                    n++;
            return n;
        }
    }

    public int Capacity => items.BackpackCapacity;

    public bool Overflow
    {
        get
        {
            foreach (var item in items.Items)
                if (item.Container == ItemContainer.Backpack && item.Slot >= items.BackpackCapacity)
                    return true;
            return false;
        }
    }

    public DescentItemView? Cell(int i) =>
        items.ContentMismatch ? null : items.In(ItemContainer.Backpack, i) is { } item ? names.View(item) : null;
}

/// <summary>
/// What a doll slot or backpack cell is asked (<see cref="IDescentItemActions"/>).
/// The inventory keys a backpack cell by its index and a doll slot as
/// <c>-1 - slot</c>. Using a backpack item equips it
/// (<c>tf2_descent_item_equip &lt;cell&gt;</c>, the slot its base takes); using a
/// doll slot takes its item off (<c>tf2_descent_item_unequip &lt;slot&gt;</c>);
/// a move within the backpack is <c>tf2_descent_item_move</c>. The server
/// decides each and tells a refusal's reason.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentItemActions)])]
public sealed class ItemActions(IItemViews items, DescentCatalog catalog, IDescentCommandLine line, ItemPointer pointer) : IDescentItemActions
{
    public void Hover(int slotOrCell) => pointer.Report(slotOrCell);

    public void Use(int slotOrCell)
    {
        var c = CultureInfo.InvariantCulture;
        if (slotOrCell >= 0)
        {
            if (items.In(ItemContainer.Backpack, slotOrCell) is not null)
                line.Send(string.Create(c, $"tf2_descent_item_equip {slotOrCell}"));
            return;
        }
        int slot = -1 - slotOrCell;
        if (items.In(ItemContainer.Equipped, slot) is not null && DescentCatalog.At(catalog.EquipSlots, slot) is { } s)
            line.Send($"tf2_descent_item_unequip {s.Id}");
    }

    public void Move(int fromCell, int toCell)
    {
        if (fromCell < 0 || toCell < 0 || fromCell == toCell)
            return;
        // The stash locker keys its own cells after the backpack's span (UI4): a move across is a deposit or a withdrawal.
        int span = StashKeys.Span(items);
        var c = CultureInfo.InvariantCulture;
        if (fromCell >= span)
        {
            if (items.In(ItemContainer.Stash, fromCell - span) is not null && toCell < span)
                line.Send(string.Create(c, $"tf2_descent_town_stash_out {fromCell - span}"));
            return;
        }
        if (items.In(ItemContainer.Backpack, fromCell) is null)
            return;
        line.Send(toCell >= span
            ? string.Create(c, $"tf2_descent_town_stash_in {fromCell}")
            : string.Create(c, $"tf2_descent_item_move {fromCell} {toCell}"));
    }
}

/// <summary>
/// The item tooltip (<see cref="IDescentItemHover"/>) of the cell or slot the
/// pointer is on: the item's name, rarity, item level and family; its affix
/// lines (none for an unidentified item: the server sends none); its unusual
/// effect; the requirement line, the unmet one marked (the server's word);
/// and, for a backpack item, the equipped item in the slot it would take, to
/// compare against.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentItemHover)])]
public sealed class ItemHoverSource(ItemPointer pointer, IItemViews items, ItemViewNames names, DescentCatalog catalog) : IDescentItemHover
{
    private OwnerItemView? Subject =>
        !pointer.Active || items.ContentMismatch ? null
        : pointer.Row >= StashKeys.Span(items) ? items.In(ItemContainer.Stash, pointer.Row - StashKeys.Span(items))
        : pointer.Row >= 0 ? items.In(ItemContainer.Backpack, pointer.Row)
        : items.In(ItemContainer.Equipped, -1 - pointer.Row);

    /// <summary>The equipped item a backpack item would replace (the first equipment slot taking its base's slot).</summary>
    private OwnerItemView? Compared
    {
        get
        {
            if (pointer.Row < 0 || Subject is not { } item || names.BaseOf(item) is not { IsGear: true } b)
                return null;
            for (int i = 0; i < catalog.EquipSlots.Count; i++)
                if (catalog.EquipSlots[i].Accepts == b.Slot && items.In(ItemContainer.Equipped, i) is { } held)
                    return held;
            return null;
        }
    }

    public bool Hovering => Subject is not null;

    public DescentItemView Item => Subject is { } s ? names.View(s) : default;

    public int AffixLineCount => Subject?.Affixes.Count ?? 0;

    public DescentAffixLine AffixLine(int i) =>
        Subject is { } s && i >= 0 && i < s.Affixes.Count ? names.Line(s.Affixes[i].Affix, s.Affixes[i].Value) : default;

    public string Family => Subject is { } s ? names.FamilyOf(s) : "";

    public string UnusualEffect => Subject is { } s ? names.EffectOf(s) : "";

    public string RequirementToken => Subject is { } s ? names.Requirement(s).Token : "";

    public bool RequirementMet => Subject is not { } s || names.Requirement(s).Met;

    public bool CompareHas => Compared is not null;

    public DescentItemView CompareItem => Compared is { } c ? names.View(c) : default;

    public int CompareAffixCount => Compared?.Affixes.Count ?? 0;

    public DescentAffixLine CompareAffix(int i) =>
        Compared is { } c && i >= 0 && i < c.Affixes.Count ? names.Line(c.Affixes[i].Affix, c.Affixes[i].Value) : default;
}

/// <summary>
/// How the stash locker keys its cells to <see cref="IDescentItemActions"/> (the Stash screen's
/// <c>BpSpan</c>): the backpack's cells first, then the stash's after the backpack's span, the
/// larger of its item count and its capacity.
/// </summary>
public static class StashKeys
{
    public static int Span(IItemViews items)
    {
        int count = 0;
        foreach (var item in items.Items)
            if (item.Container == ItemContainer.Backpack)
                count++;
        return Math.Max(count, items.BackpackCapacity);
    }
}

/// <summary>The Australium the inventory shows (<see cref="IDescentInvPurse"/>): the purse the item views carry.</summary>
[SideService(Side.Client, As = [typeof(IDescentInvPurse)])]
public sealed class PurseSource(IItemViews items) : IDescentInvPurse
{
    public long Australium => items.Australium;
}

//-----------------------------------------------------------------------------
// The skill tree
//-----------------------------------------------------------------------------

/// <summary>Which skill tab the window shows: the one the player last picked.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillTabSelection), typeof(IDescentSkillTabActions)])]
public sealed class SkillTabSelectionSource : IDescentSkillTabSelection, IDescentSkillTabActions
{
    public int SelectedTab { get; private set; }

    public void SelectTab(int tab) => SelectedTab = Math.Max(0, tab);
}

/// <summary>
/// The skill window (<see cref="IDescentSkills"/>): the hero's class's tabs
/// from the content, then the shared Mercenary tab last; each skill a card
/// with the content's name, kind, max rank, tier, mana cost and cooldown, and
/// the server's word on it (the real rank, whether the next rank can be bought
/// and why not, the level it asks). The tab shown is the selection's. With no
/// hero, or before the ranks arrive, one empty tab.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentSkills)])]
public sealed class SkillsSource(IHeroStates heroes, IHeroSkillRanks ranks, DescentCatalog catalog, SkillTabSelectionSource selection)
    : IDescentSkills, IDescentSkillTabSelection
{
    private readonly List<LiveSkillTab> _tabs = [];
    private readonly LiveSkillTab _empty = new("#Descent_Skill_Title", [], catalog, ranks, heroes);
    private string _builtFor = "\0";

    public string ClassToken => heroes.Local() is { } h ? HeroClassTokens.Of(h.ClassId) : "";

    public int HeroLevel => heroes.Local()?.Level ?? 0;

    public int SkillPoints => heroes.Local()?.SkillPoints ?? 0;

    public int TabCount => Tabs().Count == 0 ? 1 : Tabs().Count;

    public IDescentSkillTab Tab(int i) => Tabs() is { Count: > 0 } tabs ? tabs[Math.Clamp(i, 0, tabs.Count - 1)] : _empty;

    public int SelectedTab => Math.Clamp(selection.SelectedTab, 0, TabCount - 1);

    /// <summary>The class's tabs and the shared one, built again only when the hero's class changes.</summary>
    private List<LiveSkillTab> Tabs()
    {
        string cls = heroes.Local() is { } h && ranks.Known ? HeroClassIds.Of(h.ClassId) : "";
        if (cls == _builtFor)
            return _tabs;
        _builtFor = cls;
        _tabs.Clear();
        if (cls.Length == 0)
            return _tabs;
        foreach (var tab in catalog.Tabs.Where(t => t.Class == cls).Append(catalog.Tabs.FirstOrDefault(t => t.Class == catalog.SharedClass)))
        {
            if (tab is null)
                continue;
            var skills = Enumerable.Range(0, catalog.Skills.Count).Where(i => catalog.Skills[i].Tab == tab.Id).ToArray();
            if (skills.Length > 0)
                _tabs.Add(new LiveSkillTab(tab.TitleToken, skills, catalog, ranks, heroes));
        }
        return _tabs;
    }
}

/// <summary>One tab of skills, by their indices in the table; a card's id is its skill's index.</summary>
public sealed class LiveSkillTab(string title, int[] skills, DescentCatalog catalog, IHeroSkillRanks ranks, IHeroStates heroes)
    : IDescentSkillTab, IDescentSkillGroups
{
    public string TitleToken => title;

    public int SkillCount => skills.Length;

    public DescentSkill Skill(int i)
    {
        if (i < 0 || i >= skills.Length)
            return default;
        int index = skills[i];
        var s = catalog.Skills[index];
        int rank = ranks.RankOf(index);
        var why = ranks.WhyOf(index);
        int level = ranks.NextLevelOf(index);
        bool shared = s.Class == catalog.SharedClass;
        int tier = shared ? 1 : Math.Max(1, s.Tier);
        int unlock = level > 0 ? level : shared || tier - 1 >= catalog.TierLevels.Count ? 0 : catalog.TierLevels[tier - 1];
        int heroLevel = heroes.Local()?.Level ?? 0;
        bool unlocked = why != SkillWhy.BelowLevel;
        // A card locked by level shows the level it asks where the costs would go: the
        // card has room for one of the two (its hint still names the costs).
        return new DescentSkill(
            index, s.NameToken, KindOf(s), rank, s.MaxRank, 0, tier, unlock, heroLevel,
            Unlocked: unlocked,
            Buyable: why == SkillWhy.Buyable,
            ManaCost: unlocked && s.ManaCost.Length > 0 ? s.ManaCost + " mana" : "",
            Cooldown: unlocked && s.Cooldown.Length > 0 ? s.Cooldown + " s" : "",
            FamilyTag: "",
            PrereqMet: ranks.PrerequisitesMetOf(index));
    }

    public int PrereqCount(int skillIndex) =>
        skillIndex >= 0 && skillIndex < skills.Length ? catalog.Skills[skills[skillIndex]].Prerequisites.Count : 0;

    public int PrereqId(int skillIndex, int i) =>
        skillIndex >= 0 && skillIndex < skills.Length && catalog.Skills[skills[skillIndex]].Prerequisites is { } p && i >= 0 && i < p.Count
            ? catalog.SkillIndex(p[i])
            : -1;

    public int SynergyCount(int skillIndex) => 0;

    public string SynergyText(int skillIndex, int i) => "";

    /// <summary>The Mercenary tab is one group: the content names no groups.</summary>
    public string GroupTitleToken(int group) => "#Descent_Skill_Group_Generic";

    public static DescentSkillKind KindOf(CatalogSkill s) => s.Kind switch
    {
        "action" => DescentSkillKind.Action,
        "toggle" => DescentSkillKind.Toggle,
        "capstone" => DescentSkillKind.Capstone,
        _ => s.Scope is { } scope && scope != "all" ? DescentSkillKind.UpgradeFamily : DescentSkillKind.Upgrade,
    };
}

/// <summary>Buying a rank (<see cref="IDescentSkillActions"/>): <c>tf2_descent_learn &lt;skill&gt;</c>; the server decides and tells a refusal.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillActions)])]
public sealed class SkillActions(DescentCatalog catalog, IDescentCommandLine line, SkillPointer pointer) : IDescentSkillActions
{
    public void Buy(int skillId)
    {
        if (DescentCatalog.At(catalog.Skills, skillId) is { } skill)
            line.Send($"tf2_descent_learn {skill.Id}");
    }

    public void Hover(int skillId) => pointer.Report(skillId);
}

/// <summary>
/// The skill card's hover hint (<see cref="IDescentSkillHover"/>): the
/// skill's name and rank, its kind, mana cost and cooldown as the content
/// writes them, and the server's word on its next rank (learnable, or why not).
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentSkillHover)])]
public sealed class SkillHoverSource(SkillPointer pointer, IHeroSkillRanks ranks, DescentCatalog catalog) : IDescentSkillHover
{
    private readonly List<(string Token, string Good)> _lines = [];
    private (int Skill, int Rank, SkillWhy Why, int Level) _built = (-2, 0, SkillWhy.UnknownSkill, 0);

    public int HoveredSkill => pointer.Active && DescentCatalog.At(catalog.Skills, pointer.Row) is not null ? pointer.Row : -1;

    public bool Active => HoveredSkill >= 0;

    public string TitleToken => HoveredSkill >= 0 ? catalog.Skills[HoveredSkill].NameToken : "";

    public string TitleData => "";

    public string SubtitleData => HoveredSkill >= 0
        ? string.Create(CultureInfo.InvariantCulture, $"{ranks.RankOf(HoveredSkill)} / {catalog.Skills[HoveredSkill].MaxRank}")
        : "";

    public int LineCount => Lines().Count;

    public string LineToken(int i) => i >= 0 && i < Lines().Count ? Lines()[i].Token : "";

    public string LineData(int i) => "";

    public string LineGood(int i) => i >= 0 && i < Lines().Count ? Lines()[i].Good : "";

    public bool LineGoodNegative(int i) => false;

    public string ValueToken(int i) => "";

    public string ValueData(int i) => "";

    public bool ValueGood(int i) => false;

    private List<(string Token, string Good)> Lines()
    {
        int skill = HoveredSkill;
        var key = (skill, skill >= 0 ? ranks.RankOf(skill) : 0, skill >= 0 ? ranks.WhyOf(skill) : SkillWhy.UnknownSkill,
                   skill >= 0 ? ranks.NextLevelOf(skill) : 0);
        if (key == _built)
            return _lines;
        _built = key;
        _lines.Clear();
        if (skill < 0)
            return _lines;
        var s = catalog.Skills[skill];
        _lines.Add((KindToken(LiveSkillTab.KindOf(s)), ""));
        if (s.ManaCost.Length > 0)
            _lines.Add(("#Descent_Hint_Mana", s.ManaCost));
        if (s.Cooldown.Length > 0)
            _lines.Add(("#Descent_Hint_Cooldown", s.Cooldown + " s"));
        var why = ranks.WhyOf(skill);
        _lines.Add(("#Descent_SkillWhy_" + why, why == SkillWhy.BelowLevel
            ? ranks.NextLevelOf(skill).ToString(CultureInfo.InvariantCulture)
            : ""));
        return _lines;
    }

    private static string KindToken(DescentSkillKind kind) => kind switch
    {
        DescentSkillKind.Action => "#Descent_Kind_Action",
        DescentSkillKind.Upgrade => "#Descent_Kind_Upgrade",
        DescentSkillKind.UpgradeFamily => "#Descent_Kind_Family",
        DescentSkillKind.Toggle => "#Descent_Kind_Toggle",
        DescentSkillKind.Capstone => "#Descent_Kind_Capstone",
        _ => "#Descent_Kind_Utility",
    };
}

/// <summary>The Mercenary tab's group headings (<see cref="IDescentSkillGroups"/>): one group, the content names none.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillGroups)])]
public sealed class SkillGroupsSource : IDescentSkillGroups
{
    public string GroupTitleToken(int group) => "#Descent_Skill_Group_Generic";
}

/// <summary>
/// The hover popup's content (<see cref="IDescentHintTip"/>, what every
/// screen's HintTip part reads): the subject whose pointer is on a row, the
/// sheet's, the skill tree's or the vendor's wares (UI4); inactive when none is.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentHintTip)])]
public sealed class HintTipSource(IDescentSheetHover sheet, IDescentSkillHover skill, IDescentVendorHover vendor) : IDescentHintTip
{
    private IDescentHintTip? Live => sheet.Active ? sheet : skill.Active ? skill : vendor.Active ? vendor : null;

    public bool Active => Live is not null;

    public string TitleToken => Live?.TitleToken ?? "";

    public string TitleData => Live?.TitleData ?? "";

    public string SubtitleData => Live?.SubtitleData ?? "";

    public int LineCount => Live?.LineCount ?? 0;

    public string LineToken(int i) => Live?.LineToken(i) ?? "";

    public string LineData(int i) => Live?.LineData(i) ?? "";

    public string LineGood(int i) => Live?.LineGood(i) ?? "";

    public bool LineGoodNegative(int i) => Live?.LineGoodNegative(i) ?? false;

    public string ValueToken(int i) => Live?.ValueToken(i) ?? "";

    public string ValueData(int i) => Live?.ValueData(i) ?? "";

    public bool ValueGood(int i) => Live?.ValueGood(i) ?? false;
}
