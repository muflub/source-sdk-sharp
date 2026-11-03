namespace SourceSharp.Ai;

/// <summary>
/// One bot of a roster: the name it is to have (null: the binding draws
/// one, or a profile's own name wins) and its skills as <c>ai_bot_add</c>
/// takes them: a preset, <c>skills=a,b,…</c>, or <c>profile=&lt;name&gt;</c>
/// (a profile on the server's own disk).
/// </summary>
public readonly record struct RosterEntry(string? Name, string Skills);

/// <summary>
/// The roster a server adds at a level's start (plan_hl2mp_ai §3.5): a
/// semicolon list, each entry a preset word or colon-separated fields
/// <c>name=&lt;n&gt;</c>, <c>skills=a,b,…</c>, <c>profile=&lt;p&gt;</c>. The
/// client writes it (<see cref="Format"/>), the server reads it
/// (<see cref="Parse"/>) and judges every entry's skills itself. A name is a
/// profile's name (<see cref="ProfileFile.NameRefusal"/>), so no entry can
/// carry a separator or a quote into the console line it rides in.
/// </summary>
public static class RosterSpec
{
    /// <summary>The spec for <paramref name="entries"/>, in order.</summary>
    public static string Format(IEnumerable<RosterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return string.Join(";", entries.Select(e =>
        {
            if (e.Name is { } n && ProfileFile.NameRefusal(n) is { } why)
                throw new ArgumentException(why, nameof(entries));
            if (e.Skills.Contains(';') || e.Skills.Contains(':') || e.Skills.Contains('"') || e.Skills.Contains(' '))
                throw new ArgumentException($"'{e.Skills}' is not a skills field", nameof(entries));
            return e.Name is null ? e.Skills : $"name={e.Name}:{e.Skills}";
        }));
    }

    /// <summary>
    /// The entries of <paramref name="spec"/>; an entry that does not read is
    /// left out and its refusal, naming it by position, goes to
    /// <paramref name="refusals"/>. Empty entries are skipped.
    /// </summary>
    public static IReadOnlyList<RosterEntry> Parse(string spec, ICollection<string> refusals)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(refusals);
        var entries = new List<RosterEntry>();
        string[] parts = spec.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            string entry = parts[i].Trim();
            if (entry.Length == 0)
                continue;
            string? name = null, skills = null, why = null;
            foreach (string raw in entry.Split(':'))
            {
                string field = raw.Trim();
                if (field.StartsWith("name=", StringComparison.Ordinal))
                {
                    if (name is not null)
                        why = "two names";
                    name = field["name=".Length..];
                    if (ProfileFile.NameRefusal(name) is { } bad)
                        why = bad;
                }
                else if (field.Length == 0)
                {
                    why = "an empty field";
                }
                else
                {
                    if (skills is not null)
                        why = $"two skills fields ('{skills}' and '{field}')";
                    skills = field;
                }
            }

            why ??= skills is null ? "no skills (a preset, skills=a,b,... or profile=<name>)" : null;
            if (why is not null)
                refusals.Add($"roster entry {i + 1} '{entry}': {why}");
            else
                entries.Add(new RosterEntry(name, skills!));
        }

        return entries;
    }
}
