namespace Debug.Api.Server.Interfaces;

/// <summary>One route a provider serves: its method, its path, and what it answers.</summary>
public readonly record struct DebugRoute(string Method, string Path, string Description);

/// <summary>A provider's answer: the HTTP status and the JSON body.</summary>
public readonly record struct DebugRouteReply(int Status, string Json);

/// <summary>
/// Routes a mod adds to the dev API (plan_hl2mp_ai §7.3): the server consults
/// every registered provider after its own routes, and lists their routes at
/// <c>/routes</c>. <see cref="Handle"/> runs on the game thread, so a
/// provider reads the game's state as it stands that tick.
/// </summary>
public interface IDebugRouteProvider
{
    /// <summary>
    /// The routes it serves, by exact path; a path ending in '/' serves every
    /// path under it, which <see cref="Handle"/> receives whole.
    /// </summary>
    IReadOnlyList<DebugRoute> Routes { get; }

    /// <summary>The answer for one of <see cref="Routes"/>' paths.</summary>
    DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query);
}
