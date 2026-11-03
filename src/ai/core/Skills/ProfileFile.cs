using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace SourceSharp.Ai;

/// <summary>A saved profile as its file holds it: the profile, the pool it was saved under, and its revisions.</summary>
public sealed record BotProfileDocument(SkillProfile Profile, int Pool, ImmutableArray<ProfileRevision> Revisions);

/// <summary>
/// A custom bot's profile on disk (plan_hl2mp_ai §3.5): one JSON file per
/// profile, versioned, hand-editable, so every load is judged again:
/// <c>format</c> other than <see cref="Format"/>, a name that is not a
/// profile's name or not the file's, an unknown or missing skill, a level
/// outside 0..cap and a sum above the pool are each refused by name. A file
/// saved under a smaller pool loads; one whose sum does not fit this pool
/// does not. The bytes are written in one fixed shape, so a file this class
/// wrote reads back and writes again byte for byte.
/// </summary>
public static class ProfileFile
{
    public const int Format = 1;

    /// <summary>The longest name a profile takes.</summary>
    public const int NameMax = 32;

    /// <summary>The time format a revision is written in: UTC, to the second.</summary>
    public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// Why <paramref name="name"/> cannot be a profile's name, or null: one to
    /// <see cref="NameMax"/> letters, digits, <c>_</c> or <c>-</c>. The name
    /// becomes a file name and a console argument, so it is never path-joined
    /// or quoted: anything else is refused.
    /// </summary>
    public static string? NameRefusal(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "a profile needs a name";
        if (name.Length > NameMax)
            return $"'{name}' is {name.Length} characters; a profile's name takes at most {NameMax}";
        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                return $"'{name}' has '{c}': a profile's name is letters, digits, _ and -";
        }

        return null;
    }

    /// <summary>The file's bytes for <paramref name="document"/>.</summary>
    public static byte[] Write(BotProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteString("name", document.Profile.Name);
            w.WriteNumber("pool", document.Pool);
            WriteSkills(w, document.Profile.Skills, document.Profile.Levels);
            w.WriteStartArray("revisions");
            foreach (ProfileRevision r in document.Revisions)
            {
                w.WriteStartObject();
                w.WriteNumber("n", r.Number);
                w.WriteString("at", r.At.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture));
                w.WriteString("note", r.Note);
                WriteSkills(w, document.Profile.Skills, r.Levels);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>
    /// Reads <paramref name="bytes"/>, the file called <paramref name="fileName"/>
    /// holding the profile named <paramref name="expectedName"/>, against
    /// <paramref name="pool"/>; refused, naming the file and the fault, when it
    /// is not a profile this pool takes.
    /// </summary>
    public static SkillVerdict Read(ReadOnlySpan<byte> bytes, string fileName, string expectedName, SkillPool pool,
                                    out BotProfileDocument? document)
    {
        ArgumentNullException.ThrowIfNull(pool);
        document = null;
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(bytes.ToArray());
        }
        catch (JsonException e)
        {
            return SkillVerdict.Refuse($"{fileName} is not JSON: {e.Message}");
        }

        using (json)
        {
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return SkillVerdict.Refuse($"{fileName} is not a profile: its top level is not an object");
            if (!TryInt(root, "format", out int format))
                return SkillVerdict.Refuse($"{fileName} has no format");
            if (format != Format)
                return SkillVerdict.Refuse($"{fileName} is format {format}; this build reads format {Format}");

            string name = root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
            if (NameRefusal(name) is { } badName)
                return SkillVerdict.Refuse($"{fileName}: {badName}");
            if (!string.Equals(name, expectedName, StringComparison.Ordinal))
                return SkillVerdict.Refuse($"{fileName} holds '{name}', not '{expectedName}'");
            if (!TryInt(root, "pool", out int savedPool) || savedPool <= 0)
                return SkillVerdict.Refuse($"{fileName} has no pool");

            if (!root.TryGetProperty("skills", out JsonElement skills))
                return SkillVerdict.Refuse($"{fileName} has no skills");
            SkillVerdict verdict = ReadLevels(skills, fileName, "", pool, out ImmutableArray<int> levels);
            if (!verdict.Accepted)
                return verdict;
            var profile = new SkillProfile(name, pool.Skills, levels);
            verdict = pool.Judge(profile);
            if (!verdict.Accepted)
                return SkillVerdict.Refuse($"{fileName}: {verdict.Refusal}");

            var revisions = ImmutableArray.CreateBuilder<ProfileRevision>();
            if (root.TryGetProperty("revisions", out JsonElement list))
            {
                if (list.ValueKind != JsonValueKind.Array)
                    return SkillVerdict.Refuse($"{fileName}: revisions is not a list");
                var numbers = new HashSet<int>();
                foreach (JsonElement r in list.EnumerateArray())
                {
                    if (!TryInt(r, "n", out int number) || number <= 0 || !numbers.Add(number))
                        return SkillVerdict.Refuse($"{fileName}: a revision has no number of its own");
                    string where = $" (revision #{number})";
                    if (!r.TryGetProperty("at", out JsonElement at) || at.ValueKind != JsonValueKind.String
                        || !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture,
                                                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset when))
                        return SkillVerdict.Refuse($"{fileName}{where}: no time");
                    string note = r.TryGetProperty("note", out JsonElement nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString()! : "";
                    if (!r.TryGetProperty("skills", out JsonElement rs))
                        return SkillVerdict.Refuse($"{fileName}{where}: no skills");
                    verdict = ReadLevels(rs, fileName, where, pool, out ImmutableArray<int> revLevels);
                    if (!verdict.Accepted)
                        return verdict;
                    verdict = pool.Judge(new SkillProfile(name, pool.Skills, revLevels));
                    if (!verdict.Accepted)
                        return SkillVerdict.Refuse($"{fileName}{where}: {verdict.Refusal}");
                    revisions.Add(new ProfileRevision(number, when, note, revLevels));
                }
            }

            document = new BotProfileDocument(profile, savedPool, revisions.ToImmutable());
            return SkillVerdict.Ok;
        }
    }

    private static void WriteSkills(Utf8JsonWriter w, ImmutableArray<SkillDefinition> skills, ImmutableArray<int> levels)
    {
        w.WriteStartObject("skills");
        for (int i = 0; i < skills.Length; i++)
            w.WriteNumber(skills[i].Key, levels[i]);
        w.WriteEndObject();
    }

    private static SkillVerdict ReadLevels(JsonElement skills, string fileName, string where, SkillPool pool, out ImmutableArray<int> levels)
    {
        levels = default;
        if (skills.ValueKind != JsonValueKind.Object)
            return SkillVerdict.Refuse($"{fileName}{where}: skills is not an object");
        var found = new int?[pool.Skills.Length];
        foreach (JsonProperty p in skills.EnumerateObject())
        {
            int i = -1;
            for (int k = 0; k < pool.Skills.Length; k++)
            {
                if (string.Equals(pool.Skills[k].Key, p.Name, StringComparison.Ordinal))
                    i = k;
            }

            if (i < 0)
                return SkillVerdict.Refuse($"{fileName}{where}: unknown skill '{p.Name}'");
            if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out int level))
                return SkillVerdict.Refuse($"{fileName}{where}: {p.Name} is not a whole number");
            if (level < 0 || level > pool.Cap)
                return SkillVerdict.Refuse($"{fileName}{where}: {p.Name} is {level}, outside 0..{pool.Cap}");
            found[i] = level;
        }

        var b = ImmutableArray.CreateBuilder<int>(pool.Skills.Length);
        for (int k = 0; k < found.Length; k++)
        {
            if (found[k] is not { } level)
                return SkillVerdict.Refuse($"{fileName}{where}: no level for '{pool.Skills[k].Key}'");
            b.Add(level);
        }

        levels = b.MoveToImmutable();
        return SkillVerdict.Ok;
    }

    private static bool TryInt(JsonElement e, string key, out int value)
    {
        value = 0;
        return e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out value);
    }
}
