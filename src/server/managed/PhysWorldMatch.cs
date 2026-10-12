namespace SourceSharp.Server;

/// <summary>
/// Which physics objects <c>ss_physworld_object &lt;request&gt;</c> reports on:
/// the managed twin of native physworld_match_rule.h (plan_physics_managed B1).
///
/// <para>
/// "*" AND ONLY "*" is every object, the nameless ones included. Anything else
/// is a case-insensitive substring of the object's name, and a nameless object
/// answers none. An EMPTY request matches nothing: that is tier1's V_stristr
/// with an empty needle, the spelling probes once used for "everything" and got
/// a quiet subset. C#'s Contains("") is true, so the rule is written out rather
/// than inherited, and the probes' contract stays the native one.
/// </para>
/// </summary>
public static class PhysWorldMatch
{
    /// <summary>Whether the request is the bare "*".</summary>
    public static bool AllRequested(string? wanted) => wanted == "*";

    /// <summary>Whether an object of this name answers the request.</summary>
    public static bool Matches(string? name, string? wanted)
    {
        if (AllRequested(wanted))
            return true;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(wanted))
            return false;

        return name.Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }
}
