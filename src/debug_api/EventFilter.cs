namespace Debug.Api;

/// <summary>
/// Which entity events a reader wants: <c>/events</c>' filters, and the event
/// condition of <c>/wait</c>.
///
/// <para>
/// FILTERED HERE, NOT BY THE CALLER. The gate paged through every event in
/// pages of 2,000 on each poll and filtered them itself, and once missed the
/// one it was waiting for (an OnStartTouch at seq 4868 among 1,776 others). A
/// server-side filter reads the ring once, under its lock, and answers only
/// what matched.
/// </para>
///
/// <para>
/// Every criterion that is set must match; an unset one matches anything. A
/// filter with nothing set matches every event, which is what <c>/events</c>
/// answered before filters existed.
/// </para>
/// </summary>
/// <param name="Kinds">The kinds wanted, or null for any.</param>
/// <param name="Name">The event's name (an output's name, or the target an input named), as a glob.</param>
/// <param name="ClassName">The entity's classname, as a glob.</param>
/// <param name="Handle">One entity, by handle.</param>
/// <param name="Index">One entity, by index.</param>
public sealed record EventFilter(
    IReadOnlySet<EntityEventKind>? Kinds = null,
    string? Name = null,
    string? ClassName = null,
    uint? Handle = null,
    int? Index = null)
{
    /// <summary>The filter that matches every event.</summary>
    public static EventFilter Any => new();

    /// <summary>Whether anything at all is being filtered on.</summary>
    public bool IsEmpty => Kinds is null && Name is null && ClassName is null && Handle is null && Index is null;

    public bool Matches(in EntityEvent e)
        => (Kinds is null || Kinds.Contains(e.Kind))
        && (Name is null || Glob.Matches(Name, e.Name))
        && (ClassName is null || Glob.Matches(ClassName, e.ClassName))
        && (Handle is null || e.Handle == Handle.Value)
        && (Index is null || e.Index == Index.Value);

    /// <summary>
    /// The filter a query string names, or the reason it names none.
    ///
    /// <para>
    /// An unknown kind or a handle that does not parse is an ERROR, not a
    /// filter that matches nothing: a typo would otherwise read as a quiet
    /// world, which is the answer a waiting test least needs to be lied to with.
    /// </para>
    /// </summary>
    public static bool TryParse(Func<string, string?> get, out EventFilter filter, out string? error)
    {
        filter = Any;
        error = null;

        HashSet<EntityEventKind>? kinds = null;
        if (get("kind") is { Length: > 0 } kindText)
        {
            kinds = new HashSet<EntityEventKind>();
            foreach (string part in kindText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(part, ignoreCase: true, out EntityEventKind kind) || !Enum.IsDefined(kind))
                {
                    error = $"no such event kind '{part}'; expected spawned, removed, input, output or level";
                    return false;
                }

                kinds.Add(kind);
            }
        }

        uint? handle = null;
        if (get("handle") is { Length: > 0 } handleText)
        {
            if (!uint.TryParse(handleText, out uint h))
            {
                error = $"handle '{handleText}' is not a number";
                return false;
            }

            handle = h;
        }

        int? index = null;
        if (get("index") is { Length: > 0 } indexText)
        {
            if (!int.TryParse(indexText, out int i))
            {
                error = $"index '{indexText}' is not a number";
                return false;
            }

            index = i;
        }

        filter = new EventFilter(kinds, NullIfEmpty(get("name")), NullIfEmpty(get("classname")), handle, index);
        return true;
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}

/// <summary>
/// Case-insensitive globs: <c>*</c> for any run, <c>?</c> for one character.
/// Names in Source are case-insensitive, so the match is too.
/// </summary>
public static class Glob
{
    public static bool Matches(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, resume = 0;

        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }
}
