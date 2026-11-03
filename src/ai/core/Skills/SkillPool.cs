using System.Collections.Immutable;
using System.Globalization;

namespace SourceSharp.Ai;

/// <summary>A pool's judgement of a profile: accepted, or refused with the reason naming what is wrong.</summary>
public readonly record struct SkillVerdict(bool Accepted, string Refusal)
{
    public static SkillVerdict Ok => new(true, "");

    public static SkillVerdict Refuse(string why) => new(false, why);
}

/// <summary>
/// A point pool over a list of skills (plan_hl2mp_ai §10): a profile fits when
/// every level is 0..<see cref="Cap"/> and the levels sum to at most
/// <see cref="Points"/>. The one judge of a profile, on both sides: the
/// client's editor and the server's <c>ai_bot_add</c> ask the same class.
/// </summary>
public sealed class SkillPool
{
    public SkillPool(ImmutableArray<SkillDefinition> skills, int points, int cap)
    {
        if (skills.IsDefaultOrEmpty)
            throw new ArgumentException("a pool needs skills", nameof(skills));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(points);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        Skills = skills;
        Points = points;
        Cap = cap;
    }

    public ImmutableArray<SkillDefinition> Skills { get; }

    public int Points { get; }

    public int Cap { get; }

    /// <summary>Whether <paramref name="profile"/> fits this pool, and why not.</summary>
    public SkillVerdict Judge(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Skills.SequenceEqual(Skills))
            return SkillVerdict.Refuse($"profile '{profile.Name}' is over other skills than this pool's {Skills.Length}");

        for (int i = 0; i < Skills.Length; i++)
        {
            int level = profile.Levels[i];
            if (level < 0)
                return SkillVerdict.Refuse($"{Skills[i].Key} is {level}, below 0");
            if (level > Cap)
                return SkillVerdict.Refuse($"{Skills[i].Key} is {level}, above the cap of {Cap}");
        }

        int spent = profile.Spent;
        if (spent > Points)
            return SkillVerdict.Refuse($"profile '{profile.Name}' spends {spent} of a {Points}-point pool");
        return SkillVerdict.Ok;
    }

    /// <summary>A profile of zeros: nothing spent.</summary>
    public SkillProfile Empty(string name) => new(name, Skills, ImmutableArray.CreateRange(new int[Skills.Length]));

    /// <summary>
    /// A profile from a <c>skills=</c> list (§3.1, §3.5): one integer per
    /// skill, in table order, comma-separated. Refused, naming the fault, when
    /// it does not parse or does not fit.
    /// </summary>
    public SkillVerdict TryParse(string name, string spec, out SkillProfile? profile)
    {
        profile = null;
        ArgumentNullException.ThrowIfNull(spec);
        string[] parts = spec.Split(',');
        if (parts.Length != Skills.Length)
            return SkillVerdict.Refuse($"skills= has {parts.Length} levels; it takes {Skills.Length}, one per skill in order: "
                                       + string.Join(",", Skills.Select(s => s.Key)));

        var levels = ImmutableArray.CreateBuilder<int>(Skills.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int level))
                return SkillVerdict.Refuse($"{Skills[i].Key} is '{parts[i].Trim()}', not a number");
            levels.Add(level);
        }

        var candidate = new SkillProfile(name, Skills, levels.MoveToImmutable());
        SkillVerdict verdict = Judge(candidate);
        if (verdict.Accepted)
            profile = candidate;
        return verdict;
    }
}
