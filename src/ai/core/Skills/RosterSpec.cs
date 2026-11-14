namespace SourceSharp.Ai;

/// <summary>
/// One bot of a roster: the name it is to have (null: the binding draws
/// one, or a profile's own name wins) and its skills as <c>ai_bot_add</c>
/// takes them: a preset, <c>skills=a,b,…</c>, or <c>profile=&lt;name&gt;</c>
/// (a profile on the server's own disk). <see cref="Team"/> is the team it joins
/// under a mode with teams (plan_bots_teams_tf2 B.6): 0 auto, as a player's
/// <c>jointeam 0</c> leaves it to the mode, else the team's number.
/// </summary>
public readonly record struct RosterEntry(string? Name, string Skills, int Team = 0);

/// <summary>A bot's <c>team=</c> (plan_bots_teams_tf2 B.6): <c>auto</c> (0), or a playing team's number, 2 to 5 (up to four teams).</summary>
public static class RosterTeam
{
    public const int Auto = 0;

    /// <summary>The team <paramref name="value"/> names (the text after <c>team=</c>), or null with the refusal.</summary>
    public static int? Parse(string value, out string? refusal)
    {
        refusal = null;
        switch (value)
        {
            case "auto":
                return Auto;
            case "2":
                return 2;
            case "3":
                return 3;
            case "4":
                return 4;
            case "5":
                return 5;
            default:
                refusal = $"team '{value}' is not auto or 2 to 5";
                return null;
        }
    }

    public static string Name(int team) => team == Auto ? "auto" : team.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

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
            string fields = e.Name is null ? e.Skills : $"name={e.Name}:{e.Skills}";
            return e.Team == RosterTeam.Auto ? fields : $"{fields}:team={RosterTeam.Name(e.Team)}";
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
            int team = RosterTeam.Auto;
            bool teamed = false;
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
                else if (field.StartsWith("team=", StringComparison.Ordinal))
                {
                    if (teamed)
                        why = "two teams";
                    teamed = true;
                    if (RosterTeam.Parse(field["team=".Length..], out string? bad) is { } t)
                        team = t;
                    else
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
                entries.Add(new RosterEntry(name, skills!, team));
        }

        return entries;
    }
}
