using System.Collections.Immutable;

namespace TF2.Descent.Rules.Hero;

/// <summary>
/// The character a hero is, as the rules see it (plan_tf2_rpg 2a, Appendix C): class,
/// level, total experience, each attribute's Base (class start plus points invested),
/// unspent points, skill ranks and the respec count. Now is not stored: it is Base plus
/// what gear, skills and buffs contribute (<see cref="HeroRules.Stats"/>).
/// </summary>
public sealed record HeroSheet
{
    public required string ClassId { get; init; }
    public string Name { get; init; } = "";
    public int Level { get; init; } = 1;

    /// <summary>Total experience earned (never the progress inside a level): level L is reached at <see cref="HeroRules.XpAtLevel"/>(L).</summary>
    public long Xp { get; init; }

    /// <summary>Base of each attribute, by attribute id.</summary>
    public ImmutableSortedDictionary<string, int> Attributes { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>Attribute points earned and not yet spent ("Points to distribute").</summary>
    public int AttributePoints { get; init; }

    /// <summary>Skill points earned and not yet spent.</summary>
    public int SkillPoints { get; init; }

    /// <summary>Skill ranks bought with skill points, by skill id (RU6 gives them meaning).</summary>
    public ImmutableSortedDictionary<string, int> SkillRanks { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>How many respecs this hero has bought: the next one's price rises with it.</summary>
    public int RespecCount { get; init; }

    /// <summary>The deepest level this hero has reached (the host keeps it in a column).</summary>
    public int ReachedDepth { get; init; }

    /// <summary>A hardcore hero dies once (R8, D7).</summary>
    public bool Hardcore { get; init; }

    /// <summary>A hardcore hero that died: unplayable, and never rises.</summary>
    public bool Fallen { get; init; }

    /// <summary>Spell ranks by spell id, raised by reading tomes (R27; RU6 gives them meaning).</summary>
    public ImmutableSortedDictionary<string, int> SpellRanks { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>Kills by robot template id: what the bestiary reveals (ruling L6, default E7; RU12).</summary>
    public ImmutableSortedDictionary<string, int> Bestiary { get; init; } = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>The potion belt's contents, slot by slot, as item ids (D4, D16).</summary>
    public ImmutableArray<string> Belt { get; init; } = [];

    /// <summary>The bosses this hero has killed, by template id (the camps of mine plan D4).</summary>
    public ImmutableSortedSet<string> BossKills { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    /// <summary>
    /// The skill bar: the skill in each of its slots, slot 1 first, "" for an empty slot
    /// (skill_settings.json's barSlots of them). A slotted skill is a cast skill the hero has
    /// a real rank in (<see cref="HeroRules.Audit"/>); a respec empties the bar.
    /// </summary>
    public ImmutableArray<string> SkillSlots { get; init; } = [];

    public int Base(string attribute) => Attributes.TryGetValue(attribute, out var v) ? v : 0;
}
