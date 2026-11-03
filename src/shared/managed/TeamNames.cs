using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A rule for what a team is called: a mod's names where the core's are
/// HL2MP's (Unassigned, Spectator, Combine, Rebels).
///
/// <para>
/// A <b>CHAIN</b>, folded like <see cref="IAmmoCarryRule"/>: every rule in
/// <see cref="IOrderedHook.HookOrder"/> order is handed the name so far — the
/// caller's own first — and returns it unchanged for no opinion. Register with
/// <c>[SideService(Side.X)]</c> on the side the name is for: the server's is the
/// team entity's name (logs), the client's the name a player reads (chat,
/// scoreboard, team menu), which a mod may localise.
/// </para>
/// </summary>
public interface ITeamNameRule
{
    /// <summary>
    /// The name of <paramref name="team"/>, given <paramref name="name"/>, the
    /// answer so far. Return it unchanged for no opinion.
    /// </summary>
    string Name(int team, string name);
}

/// <summary>
/// One side's team names: the caller's own name folded through the registered
/// <see cref="ITeamNameRule"/>s.
/// </summary>
public sealed class TeamNames
{
    private readonly IHooks<ITeamNameRule>? _rules;
    private readonly ILog? _log;

    /// <summary>The names over <paramref name="rules"/>. Null is no rules: every name is the caller's.</summary>
    public TeamNames(IHooks<ITeamNameRule>? rules = null, ILog? log = null)
    {
        _rules = rules;
        _log = log;
    }

    /// <summary>No rules: every team keeps the caller's name.</summary>
    public static TeamNames Stock() => new();

    /// <summary>
    /// The name of <paramref name="team"/>, starting from <paramref name="name"/>.
    /// A rule that throws is named and skipped, and the name before it stands;
    /// a null answer is read as no opinion.
    /// </summary>
    public string Of(int team, string name)
    {
        if (_rules is null)
            return name;

        foreach (var rule in _rules.All)
        {
            try
            {
                name = rule.Name(team, name) ?? name;
            }
            catch (Exception e)
            {
                _log?.Warning($"[SourceSharp] team name rule {rule.GetType().FullName} threw: {e.Message}; "
                            + $"team {team} keeps \"{name}\"\n");
            }
        }

        return name;
    }
}
