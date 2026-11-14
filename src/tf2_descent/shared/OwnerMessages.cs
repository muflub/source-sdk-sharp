using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

//=============================================================================
// WHAT ONLY THE OWNER IS TOLD (plan_descent_logic UI1's item-view cache, UI3).
// Each message goes to the hero's own client and to no other: his items as he
// may see them, his sheet's numbers, his skill ranks and what each next rank
// asks. Rows of the content are named by their index in the rules' tables,
// which the client embeds byte for byte; the fingerprint each message carries
// (ContentFingerprint) is what the client checks the indices against. Every
// payload fits one 255-byte user message at its fullest (a fact holds it).
//=============================================================================

/// <summary>Where an item the owner holds sits.</summary>
public enum ItemContainer : byte
{
    Backpack,
    Equipped,
    Belt,
    Stash,
}

/// <summary>What the server says of an item beside its numbers.</summary>
[Flags]
public enum ItemViewFlags : byte
{
    None = 0,
    /// <summary>Identified: its affixes and unusual effect are in the view. Unidentified, the view carries none (D22).</summary>
    Identified = 1,
    /// <summary>Durability 0: equipped, it grants nothing (2h).</summary>
    Broken = 2,
    /// <summary>Maximum durability 0: it cannot be equipped and grants nothing (L7 as amended).</summary>
    Unusable = 4,
    /// <summary>Equipped and counting for nothing now (an unmet requirement, a level, broken, unusable).</summary>
    Inactive = 8,
    /// <summary>The hero meets every requirement of the item now (L2: read against Now).</summary>
    MeetsRequirements = 16,
}

/// <summary>One item as its owner may see it, read off the wire.</summary>
/// <param name="Slot">The backpack, belt or stash index; for an equipped item the index of its equipment slot in inventory.json.</param>
/// <param name="Base">The item base's index in item_bases.json.</param>
/// <param name="Unusual">The unusual effect's index in unusual_effects.json; -1 for none (or hidden).</param>
/// <param name="Unique">The unique's index in uniques.json; -1 for none (or hidden).</param>
/// <param name="Unmet">Bit 0: the base's level is above the hero's; bit k + 1: the base's k-th attribute requirement is unmet.</param>
/// <param name="Affixes">Each affix's index in affixes.json and its rolled value; empty for an unidentified item.</param>
public sealed record OwnerItemView(
    long Id, ItemContainer Container, int Slot, int Base, int Rarity, int ItemLevel, ItemViewFlags Flags,
    int Durability, int MaxDurability, int Count, int Unusual, int Unique, int Unmet,
    IReadOnlyList<(int Affix, float Value)> Affixes)
{
    public bool Identified => (Flags & ItemViewFlags.Identified) != 0;
}

/// <summary>What starts and ends a resync of the owner's items, or tells his purse and capacity.</summary>
public enum OwnerItemsPhase : byte
{
    /// <summary>Forget every item held: the views that follow are the whole inventory.</summary>
    Begin,
    /// <summary>The resync is whole: <see cref="OwnerItemsView.Count"/> views were sent since its begin.</summary>
    End,
    /// <summary>Only the purse and the capacity changed.</summary>
    State,
}

/// <summary>A resync's bracket, or the purse and backpack capacity, read off the wire.</summary>
public sealed record OwnerItemsView(OwnerItemsPhase Phase, uint Fingerprint, int Count, int BackpackCapacity, long Australium);

/// <summary>The owner's items as they reach his client: one view at a time, a removal, a resync's bracket.</summary>
public interface IOwnerItemsHook
{
    void OnItem(OwnerItemView item);

    void OnItemGone(long id);

    void OnItems(OwnerItemsView sync);
}

/// <summary>
/// One item on the wire (<c>item_view</c>): sent to the owner when the item
/// comes into his view or changes (where it is, its durability, whether he
/// meets it now), and as one row of a resync.
/// </summary>
public partial struct OwnerItem
{
    /// <summary>The most affixes a view carries: a rarity's six and two curses.</summary>
    public const int MaxAffixes = 8;

    public partial long Id { get; set; }
    public partial byte Container { get; set; }
    public partial short Slot { get; set; }
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

    [UserMessageArray(MaxCount = MaxAffixes)]
    public partial byte[] Affixes { get; set; }

    [UserMessageArray(MaxCount = MaxAffixes)]
    public partial float[] Values { get; set; }

    public OwnerItemView View()
    {
        var ids = Affixes ?? [];
        var values = Values ?? [];
        var affixes = new (int, float)[Math.Min(ids.Length, values.Length)];
        for (int i = 0; i < affixes.Length; i++)
            affixes[i] = (ids[i], values[i]);
        return new OwnerItemView(Id, (ItemContainer)Container, Slot, Base, Rarity, ItemLevel, (ItemViewFlags)Flags,
                                 Durability, MaxDurability, Count, Unusual - 1, Unique - 1, Unmet, affixes);
    }
}

[UserMessage(Name)]
public sealed class OwnerItemMessage(IHooks<IOwnerItemsHook>? hooks) : IUserMessage<OwnerItem>
{
    public OwnerItemMessage() : this(null) { }

    public const string Name = "item_view";

    public void OnReceived(OwnerItem message)
    {
        var view = message.View();
        hooks?.Each(nameof(IOwnerItemsHook.OnItem), h => h.OnItem(view));
    }
}

/// <summary>An item left the owner's view (<c>item_gone</c>): dropped, sold, drunk, gone.</summary>
public partial struct OwnerItemGone
{
    public partial long Id { get; set; }
}

[UserMessage(Name)]
public sealed class OwnerItemGoneMessage(IHooks<IOwnerItemsHook>? hooks) : IUserMessage<OwnerItemGone>
{
    public OwnerItemGoneMessage() : this(null) { }

    public const string Name = "item_gone";

    public void OnReceived(OwnerItemGone message)
    {
        long id = message.Id;
        hooks?.Each(nameof(IOwnerItemsHook.OnItemGone), h => h.OnItemGone(id));
    }
}

/// <summary>
/// A resync's bracket (<c>item_sync</c>): <see cref="OwnerItemsPhase.Begin"/>
/// before the whole inventory, one <see cref="OwnerItem"/> each, then
/// <see cref="OwnerItemsPhase.End"/> with the count; or the purse and the
/// backpack's capacity alone (<see cref="OwnerItemsPhase.State"/>).
/// </summary>
public partial struct OwnerItems
{
    public partial byte Phase { get; set; }
    public partial uint Fingerprint { get; set; }
    public partial ushort Count { get; set; }
    public partial ushort BackpackCapacity { get; set; }
    public partial long Australium { get; set; }

    public OwnerItemsView View() => new((OwnerItemsPhase)Phase, Fingerprint, Count, BackpackCapacity, Australium);
}

[UserMessage(Name)]
public sealed class OwnerItemsMessage(IHooks<IOwnerItemsHook>? hooks) : IUserMessage<OwnerItems>
{
    public OwnerItemsMessage() : this(null) { }

    public const string Name = "item_sync";

    public void OnReceived(OwnerItems message)
    {
        var view = message.View();
        hooks?.Each(nameof(IOwnerItemsHook.OnItems), h => h.OnItems(view));
    }
}

//-----------------------------------------------------------------------------
// The sheet
//-----------------------------------------------------------------------------

/// <summary>
/// The owner's sheet as the server derives it, read off the wire: per
/// attribute (attributes.json's order) its base, now, cap, and what one more
/// point would add to maximum life and mana; the resistances in
/// damage_types.json's order and their cap, armour, crit chance, and the
/// damage range of the weapon in the hands. Tenths are whole tenths.
/// </summary>
public sealed record SheetNumbersView(
    uint Fingerprint, IReadOnlyList<int> Base, IReadOnlyList<int> NowTenths, IReadOnlyList<int> Cap,
    IReadOnlyList<int> LifePerPointTenths, IReadOnlyList<int> ManaPerPointTenths,
    IReadOnlyList<int> ResistPercent, int ResistCap, int Armour, int CritTenths, int DamageMin, int DamageMax);

public interface ISheetNumbersHook
{
    void OnSheetNumbers(SheetNumbersView sheet);
}

/// <summary>The sheet's numbers on the wire (<c>hero_sheet</c>), sent to the owner when they change.</summary>
public partial struct SheetNumbers
{
    /// <summary>The most attributes or resistances a sheet carries.</summary>
    public const int MaxRows = 8;

    public partial uint Fingerprint { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial short[] Base { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial short[] NowTenths { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial short[] Cap { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial short[] LifePerPointTenths { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial short[] ManaPerPointTenths { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial short[] ResistPercent { get; set; }

    public partial byte ResistCap { get; set; }
    public partial short Armour { get; set; }
    public partial short CritTenths { get; set; }
    public partial short DamageMin { get; set; }
    public partial short DamageMax { get; set; }

    public SheetNumbersView View() => new(
        Fingerprint, Ints(Base), Ints(NowTenths), Ints(Cap), Ints(LifePerPointTenths), Ints(ManaPerPointTenths),
        Ints(ResistPercent), ResistCap, Armour, CritTenths, DamageMin, DamageMax);

    private static int[] Ints(short[]? values) => values is null ? [] : Array.ConvertAll(values, v => (int)v);

    /// <summary>A number as a wire short: rounded to the nearest, held inside the short's range; a NaN is 0.</summary>
    public static short Short(double value) =>
        double.IsNaN(value) ? (short)0 : (short)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
}

[UserMessage(Name)]
public sealed class SheetNumbersMessage(IHooks<ISheetNumbersHook>? hooks) : IUserMessage<SheetNumbers>
{
    public SheetNumbersMessage() : this(null) { }

    public const string Name = "hero_sheet";

    public void OnReceived(SheetNumbers message)
    {
        var view = message.View();
        hooks?.Each(nameof(ISheetNumbersHook.OnSheetNumbers), h => h.OnSheetNumbers(view));
    }
}

//-----------------------------------------------------------------------------
// Skill ranks
//-----------------------------------------------------------------------------

/// <summary>Why the next rank of a skill cannot be bought now, as the server's rules answer (0: it can).</summary>
public enum SkillWhy : byte
{
    Buyable,
    UnknownSkill,
    OtherClass,
    BelowLevel,
    Prerequisite,
    AtMaxRank,
    NoPoints,
}

/// <summary>
/// The owner's skill ranks, read off the wire: for skills
/// <see cref="First"/> onward in skills.json's order, the real rank, why the
/// next rank cannot be bought (<see cref="SkillWhy"/>), the hero level the
/// next rank asks, and whether every prerequisite is held (the rules check
/// the level first, so a skill below its level says nothing of them in
/// <see cref="Why"/>); <see cref="Total"/> is the table's size.
/// </summary>
public sealed record SkillRanksView(uint Fingerprint, int First, int Total, IReadOnlyList<int> Ranks,
                                    IReadOnlyList<SkillWhy> Why, IReadOnlyList<int> NextLevel,
                                    IReadOnlyList<bool>? PrerequisitesMet = null);

public interface ISkillRanksHook
{
    void OnSkillRanks(SkillRanksView ranks);
}

/// <summary>The skill ranks on the wire (<c>skill_ranks</c>): sent to the owner when a rank or a refusal changes; a table longer than <see cref="MaxSkills"/> goes in several.</summary>
public partial struct SkillRanks
{
    /// <summary>The most skills one message carries: four arrays of them fit 255 bytes.</summary>
    public const int MaxSkills = 60;

    public partial uint Fingerprint { get; set; }
    public partial byte First { get; set; }
    public partial byte Total { get; set; }

    [UserMessageArray(MaxCount = MaxSkills)]
    public partial byte[] Ranks { get; set; }

    [UserMessageArray(MaxCount = MaxSkills)]
    public partial byte[] Why { get; set; }

    [UserMessageArray(MaxCount = MaxSkills)]
    public partial byte[] NextLevel { get; set; }

    /// <summary>1 where every prerequisite of the skill is held at its rank.</summary>
    [UserMessageArray(MaxCount = MaxSkills)]
    public partial byte[] Prerequisites { get; set; }

    public SkillRanksView View() => new(
        Fingerprint, First, Total, Array.ConvertAll(Ranks ?? [], r => (int)r),
        Array.ConvertAll(Why ?? [], w => (SkillWhy)w), Array.ConvertAll(NextLevel ?? [], l => (int)l),
        Array.ConvertAll(Prerequisites ?? [], p => p != 0));
}

[UserMessage(Name)]
public sealed class SkillRanksMessage(IHooks<ISkillRanksHook>? hooks) : IUserMessage<SkillRanks>
{
    public SkillRanksMessage() : this(null) { }

    public const string Name = "skill_ranks";

    public void OnReceived(SkillRanks message)
    {
        var view = message.View();
        hooks?.Each(nameof(ISkillRanksHook.OnSkillRanks), h => h.OnSkillRanks(view));
    }
}
