using System.Collections.Immutable;
using System.Text.Json;

namespace SourceSharp.Ai;

/// <summary>One skill's two pools of descriptors: for a bot well above the even spread, and well below it.</summary>
public sealed record TraitPools(ImmutableArray<string> High, ImmutableArray<string> Low);

/// <summary>
/// The pools bot names are drawn from (plan_hl2mp_ai §3.5.1, user rulings
/// 2026-11-01): first names, and per skill a high and a low pool of words or
/// short phrases. Data, read by the binding through the file seam.
/// </summary>
public sealed record BotNamePools(ImmutableArray<string> First, ImmutableDictionary<string, TraitPools> Traits)
{
    /// <summary>The pools a <c>format: 1</c> JSON file holds, or the refusal naming what is wrong.</summary>
    public static BotNamePools Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) || format.GetInt32() != 1)
            throw new InvalidDataException("the bot names file is not format 1");
        ImmutableArray<string> Strings(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetString() ?? "")];
        var traits = ImmutableDictionary.CreateBuilder<string, TraitPools>(StringComparer.Ordinal);
        foreach (JsonProperty skill in root.GetProperty("traits").EnumerateObject())
            traits[skill.Name] = new TraitPools(Strings(skill.Value.GetProperty("high")), Strings(skill.Value.GetProperty("low")));
        var pools = new BotNamePools(Strings(root.GetProperty("first")), traits.ToImmutable());
        if (pools.First.IsEmpty || pools.First.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("the bot names file has no first names, or an empty one");
        return pools;
    }
}

/// <summary>A bot's most distinctive trait: the skill, and whether it is high or low.</summary>
public readonly record struct BotTrait(int Skill, bool High);

/// <summary>
/// Names a bot (plan_hl2mp_ai §3.5.1): a first name and a descriptor from the
/// pool of its most distinctive trait, "Bobby Sniper" or "Tommy Camps Alot".
/// The trait is the skill furthest from the even spread of the bot's points
/// (spent / skills), high above it, low below; ties go by skill order and
/// then the seed. The seed picks every entry, so a replay names the same
/// bots. A name another bot holds is drawn again, and a numeral is the last
/// resort. Every name fits <see cref="MaxLength"/>, the first name cut first.
/// </summary>
public static class BotNamer
{
    /// <summary>The engine's <c>MAX_PLAYER_NAME_LENGTH</c> (32) less its NUL.</summary>
    public const int MaxLength = 31;

    private const int Redraws = 16;

    /// <summary>The trait <paramref name="profile"/> stands out by; <paramref name="seed"/> settles a tie among skills.</summary>
    public static BotTrait Trait(SkillProfile profile, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(profile);
        float even = (float)profile.Spent / profile.Levels.Length;
        float best = -1f;
        var tied = new List<BotTrait>();
        for (int i = 0; i < profile.Levels.Length; i++)
        {
            float d = profile.Levels[i] - even;
            float size = MathF.Abs(d);
            if (size > best + 1e-4f)
            {
                best = size;
                tied.Clear();
            }

            if (MathF.Abs(size - best) <= 1e-4f)
                tied.Add(new BotTrait(i, d >= 0f));
        }

        return tied[(int)(seed % (ulong)tied.Count)];
    }

    /// <summary>A name for a bot of <paramref name="profile"/> drawn by <paramref name="seed"/>, none of <paramref name="taken"/>.</summary>
    public static string Name(BotNamePools pools, SkillProfile profile, ulong seed, IReadOnlySet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(pools);
        ArgumentNullException.ThrowIfNull(taken);
        BotTrait trait = Trait(profile, seed);
        string key = profile.Skills[trait.Skill].Key;
        ImmutableArray<string> descriptors = pools.Traits.TryGetValue(key, out TraitPools? p)
            ? trait.High ? p.High : p.Low
            : [];
        var rng = new AiRandom(seed);
        string name = "";
        // Two Ineses are one too many while the pool has someone else (user report 2026-11-02).
        string[] free = [.. pools.First.Where(f => !FirstNameUsed(f, taken))];
        ReadOnlySpan<string> firsts = free.Length > 0 ? free : pools.First.AsSpan();
        for (int draw = 0; draw < Redraws; draw++)
        {
            string first = firsts[rng.NextInt(firsts.Length)];
            string descriptor = descriptors.IsEmpty ? "" : descriptors[rng.NextInt(descriptors.Length)];
            name = Fit(first, descriptor);
            if (!taken.Contains(name))
                return name;
        }

        return Distinct(name, taken);
    }

    /// <summary>
    /// <paramref name="name"/>, or with a numeral if it is taken ("Alyx 2"): the
    /// engine would make it "(1)Alyx" otherwise, and nobody is called that.
    /// </summary>
    public static string Distinct(string name, IReadOnlySet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);
        if (!taken.Contains(name))
            return name;
        for (int n = 2; ; n++)
        {
            string numbered = Fit(name, "", $" {n}");
            if (!taken.Contains(numbered))
                return numbered;
        }
    }

    private static bool FirstNameUsed(string first, IReadOnlySet<string> taken)
    {
        foreach (string t in taken)
        {
            if (t.Equals(first, StringComparison.OrdinalIgnoreCase) || t.StartsWith(first + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>"first descriptor" within <see cref="MaxLength"/>, the first name cut before the descriptor.</summary>
    public static string Fit(string first, string descriptor, string suffix = "")
    {
        string tail = (descriptor.Length > 0 ? " " + descriptor : "") + suffix;
        if (tail.Length >= MaxLength)
            return tail.Trim()[..Math.Min(tail.Trim().Length, MaxLength)];
        int room = MaxLength - tail.Length;
        return (first.Length > room ? first[..room].TrimEnd() : first) + tail;
    }
}
