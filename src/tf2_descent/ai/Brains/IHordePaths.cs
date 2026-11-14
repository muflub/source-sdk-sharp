using SourceSharp.Ai;

namespace TF2.Descent.Ai.Brains;

/// <summary>How a path search ended.</summary>
public enum PathAnswer : byte
{
    /// <summary>A path: its corners were written.</summary>
    Found = 0,

    /// <summary>No path joins the two points for the class, or one of them is on no cell of it.</summary>
    Unreachable,

    /// <summary>The search's expansion budget ran out first: the walker goes by the room route this time.</summary>
    OutOfBudget,

    /// <summary>The level's graph is not ready (or was let go with the level).</summary>
    NotReady,
}

/// <summary>
/// THE SEAM A ROBOT'S BRAIN GETS PATHS THROUGH (plan_descent_logic 3.6, 3.7): the level's
/// navigation graph, asked from a decider on the AI workers. The server answers over the
/// graph it derived from the level's <c>.nav3d</c> (<c>TF2.Descent.Server.Horde.Nav.HordePaths</c>);
/// a fact answers its own.
///
/// <para>
/// <b>Determinism.</b> A search is a pure function of its arguments and the graph: any thread,
/// any number at once, the same corners. The graph is named by <see cref="Epoch"/>, which the
/// server also writes into each robot's view (<see cref="HordeView.NavEpoch"/>) once the graph
/// is ready, and a decider plans on the graph only when the two agree. So a recording made
/// while the graph became ready replays byte for byte over the same graph, and a replay over
/// another graph goes by the room route and differs where it matters.
/// </para>
/// </summary>
public interface IHordePaths
{
    /// <summary>The graph's name: non-zero once it is ready, 0 before (and after the level).</summary>
    int Epoch { get; }

    /// <summary>
    /// A path for a body of <paramref name="cls"/> from <paramref name="from"/> to
    /// <paramref name="to"/>: its corners after the start, nearest first, the last
    /// <paramref name="to"/> itself, into <paramref name="corners"/> with how each is reached
    /// into <paramref name="arrive"/> (both as long); <paramref name="count"/> how many were
    /// written and <paramref name="truncated"/> whether the path goes on past them. At most
    /// <paramref name="budget"/> cells are expanded.
    /// </summary>
    PathAnswer Find(AiVector from, AiVector to, ClearanceClass cls, int budget, Span<AiVector> corners, Span<NavEdgeType> arrive,
                    out int count, out bool truncated);
}
