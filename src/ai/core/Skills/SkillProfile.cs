using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>One skill a bot spends points on: a key for files and specs, and a name for people.</summary>
public readonly record struct SkillDefinition(string Key, string Name);

/// <summary>
/// The ten skills and the pool (plan_hl2mp_ai §10, rulings 2026-11-01): Appendix
/// B's ten with Soloness replaced by Headhunter; 60 points to spend, at most 10
/// on one skill. A binding may define another list (TF2 adds Soloness back as
/// an eleventh); <see cref="SkillProfile"/> is a vector over whichever list.
/// </summary>
public static class StandardSkills
{
    public const int Pool = 60;

    public const int Cap = 10;

    public static readonly ImmutableArray<SkillDefinition> Ten =
    [
        new("accuracy", "Accuracy"),
        new("alertness", "Alertness"),
        new("aggression", "Aggression"),
        new("cover", "Cover"),
        new("tenacity", "Tenacity"),
        new("cunning", "Cunning"),
        new("discipline", "Discipline"),
        new("agility", "Agility"),
        new("nerve", "Nerve"),
        new("headhunter", "Headhunter"),
    ];

    /// <summary>The pool the ten are spent from.</summary>
    public static SkillPool StandardPool() => new(Ten, Pool, Cap);
}

/// <summary>
/// A named allocation of skill levels: one level per skill of
/// <see cref="Skills"/>, in its order. Immutable; an edit makes a new one.
/// Says nothing about whether it fits a pool: <see cref="SkillPool"/> judges.
/// </summary>
public sealed class SkillProfile
{
    public SkillProfile(string name, ImmutableArray<SkillDefinition> skills, ImmutableArray<int> levels)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (skills.IsDefault || levels.IsDefault)
            throw new ArgumentException("a profile needs its skills and their levels");
        if (skills.Length != levels.Length)
            throw new ArgumentException($"{levels.Length} levels for {skills.Length} skills", nameof(levels));
        Name = name;
        Skills = skills;
        Levels = levels;
    }

    public string Name { get; }

    public ImmutableArray<SkillDefinition> Skills { get; }

    public ImmutableArray<int> Levels { get; }

    /// <summary>The points this profile spends.</summary>
    public int Spent => Levels.Sum();

    /// <summary>The level of the skill keyed <paramref name="key"/>.</summary>
    public int this[string key]
    {
        get
        {
            int i = IndexOf(key);
            return i >= 0 ? Levels[i] : throw new KeyNotFoundException($"no skill '{key}'");
        }
    }

    public int IndexOf(string key)
    {
        for (int i = 0; i < Skills.Length; i++)
            if (string.Equals(Skills[i].Key, key, StringComparison.Ordinal))
                return i;
        return -1;
    }

    /// <summary>This profile with skill <paramref name="index"/> at <paramref name="level"/>.</summary>
    public SkillProfile With(int index, int level) => new(Name, Skills, Levels.SetItem(index, level));

    /// <summary>This profile under another name.</summary>
    public SkillProfile Named(string name) => new(name, Skills, Levels);

    /// <summary>
    /// The style name: the two highest skills, ties by table order
    /// (§3.5, §10), e.g. "Accuracy Discipline".
    /// </summary>
    public string StyleName
    {
        get
        {
            int[] order = Enumerable.Range(0, Levels.Length).OrderByDescending(i => Levels[i]).ThenBy(i => i).ToArray();
            return order.Length switch
            {
                0 => "",
                1 => Skills[order[0]].Name,
                _ => Skills[order[0]].Name + " " + Skills[order[1]].Name,
            };
        }
    }

    public bool SameLevels(SkillProfile other) => Levels.SequenceEqual(other.Levels);
}
