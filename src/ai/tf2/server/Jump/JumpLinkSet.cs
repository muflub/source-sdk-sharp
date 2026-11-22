namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// A level's blast jumps as a route search takes them (lane G5c-2a): the links beside the graph
/// (<see cref="NavLinks"/>, each <see cref="NavLink.Id"/> the jump's place in <see cref="Jumps"/>)
/// and the jumps themselves, whose recipes the follower will hand the pilot (G5c-2b).
/// Immutable; read from any thread.
/// </summary>
public sealed class JumpLinkSet
{
    private readonly NavLinks _links;
    private readonly JumpLink[] _jumps;

    private JumpLinkSet(NavLinks links, JumpLink[] jumps)
    {
        _links = links;
        _jumps = jumps;
    }

    /// <summary>
    /// The set of <paramref name="jumps"/> over <paramref name="graph"/>: a link a jump, in their
    /// order, crossing in the jump's seconds from its first command to the touch (its run-up is on
    /// the takeoff's ground, its flight over whatever lies between), costed by
    /// <paramref name="costs"/> at the run speed of the body that jumps.
    /// </summary>
    public static JumpLinkSet Of(NavGraph graph, IReadOnlyList<JumpLink> jumps, NavLinkCosts costs)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(jumps);
        return new JumpLinkSet(NavLinks.Build(graph, LinksOf(jumps, null), costs), [.. jumps]);
    }

    /// <summary>
    /// The same jumps over <paramref name="graph"/> (lane G9), each link's <see cref="NavLink.Health"/> (what a route search
    /// spends on it) <paramref name="spends"/>' for it, index for index, and weighed as <see cref="Of"/> weighs it, at its
    /// jump's own health: a link that lands by health (<see cref="JumpHealthRule.Spend"/>) is taken with less to spend, and
    /// a route through it costs what it did.
    /// </summary>
    public JumpLinkSet WithSpends(NavGraph graph, IReadOnlyList<float> spends, NavLinkCosts costs)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(spends);
        if (spends.Count != _jumps.Length)
            throw new ArgumentException($"{spends.Count} spends for {_jumps.Length} links", nameof(spends));
        var weighed = new float[_jumps.Length];
        for (int i = 0; i < weighed.Length; i++)
            weighed[i] = _jumps[i].Health;
        return new JumpLinkSet(NavLinks.Build(graph, LinksOf(_jumps, spends), costs, weighed), _jumps);
    }

    /// <summary>The links of <paramref name="jumps"/>, a link a jump in their order, each spending its jump's health or <paramref name="spends"/>' for it.</summary>
    private static NavLink[] LinksOf(IReadOnlyList<JumpLink> jumps, IReadOnlyList<float>? spends)
    {
        var links = new NavLink[jumps.Count];
        for (int i = 0; i < links.Length; i++)
        {
            JumpLink j = jumps[i];
            JumpStart start = j.Recipe.Start();
            links[i] = new NavLink(j.From, j.To, NavLinkKind.Blast, j.Seconds, spends?[i] ?? j.Health, j.Robustness, i)
            {
                Start = new AiVector(start.Origin.X, start.Origin.Y, start.Origin.Z),
            };
        }

        return links;
    }

    /// <summary>The links a search takes.</summary>
    public NavLinks Links => _links;

    /// <summary>The jumps, by a link's <see cref="NavLink.Id"/>.</summary>
    public IReadOnlyList<JumpLink> Jumps => _jumps;

    /// <summary>The jump link <paramref name="index"/> of <see cref="Links"/> (a path's step) crosses by.</summary>
    public JumpLink JumpOf(int index) => _jumps[_links[index].Id];

    /// <summary>How far past a link's takeoff its jump may still be launched: what its robustness was measured over (± 16 u at takeoff).</summary>
    public const float LinkSlide = 16f;

    /// <summary>
    /// The plan the program flies for link <paramref name="index"/> (lane G5c-2b): its recipe from standing at the
    /// recipe's start (<see cref="JumpPlan.StartsStanding"/>), numbered by the link's ability argument
    /// (<see cref="NavLinks.Argument"/>), with the simulator's touch, apex and time in the air from
    /// <paramref name="flown"/>, one flight of the recipe from its start.
    /// </summary>
    public JumpPlan PlanOf(int index, in JumpOutcome flown)
    {
        JumpLink j = JumpOf(index);
        var landing = new AiVector(j.Recipe.Landing.X, j.Recipe.Landing.Y, j.Recipe.Landing.Z);
        return new JumpPlan(j.Recipe, landing, LinkSlide, j.Seconds, flown.AirSeconds, j.WalkSeconds ?? 0f, j.Health, j.Robustness, flown.Touch, flown.Apex)
        {
            Id = NavLinks.Argument(index),
            StartsStanding = true,
        };
    }
}
