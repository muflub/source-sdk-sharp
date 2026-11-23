using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server.Portal;

/// <summary>
/// The town portal's trips (plan_descent_logic WO4), the two ways a server can make them.
/// <list type="bullet">
/// <item><b>Hosted</b>: up is the travel service's (<see cref="TravelService.Request"/>, so
/// <c>IHostLink.Travel</c> with <see cref="TravelDirection.Portal"/>: the SDK's <c>TownPortal</c>);
/// back is the host's teleporter anchor (<see cref="IHostAnchors.Return"/>), which sends the
/// hero alone to the anchor's level copy, the running instance while it runs. A refusal naming
/// <c>no_anchor</c> is a teleporter the host no longer has.</item>
/// <item><b>Unhosted</b>: one process holds one map, so a trip is this process's level
/// change: up to <see cref="TownMap"/>, back to the portal's own map, which loads that level
/// again from its map file and seed. Every player in the process comes along: there is no
/// other server for anyone to stay on.</item>
/// </list>
/// </summary>
[SideService(Side.Server, As = [typeof(IPortalRoute)])]
public sealed class PortalRoute(HostService host, TravelService travel, IHostAnchors anchors, IServerConsole console,
                                ClientMessages messages, ILog log) : IPortalRoute
{
    /// <summary>The town's map (src/mapgen's TF2DescentTown.MapName, <c>make town</c>).</summary>
    public const string TownMap = "tf2_descent_town";

    /// <summary>The host's refusal of a return through a teleporter it no longer has (plan_descent_death §4 M3).</summary>
    public const string NoAnchor = "no_anchor";

    public bool Hosted => host.Mode == HostingMode.Hosted;

    public async Task<PortalTrip> Up(int player, string hero, PortalRecord portal)
    {
        if (Hosted)
        {
            TravelAnswer? answer = await travel.Request(player, TravelDirection.Portal);
            return answer is { State: TravelState.Started }
                ? PortalTrip.Go
                : new PortalTrip(false, answer?.Reason is { Length: > 0 } why ? why : "the host was not asked");
        }

        // Sent in town: a notice sent just before this process's changelevel never reaches the HUD (F28).
        messages.NoticeAfterArrival(player, TravelService.UpToken);
        log.Msg($"[TF2Descent/Portal] unhosted: {hero} goes up through portal {portal.Id}: this process changes level to {TownMap}, and every player in it comes along\n");
        console.Execute($"changelevel {TownMap}");
        return PortalTrip.Go;
    }

    public async Task<PortalTrip> Back(int player, string hero, PortalRecord portal)
    {
        if (Hosted)
        {
            TravelAnswer answer = await anchors.Return(hero, portal.Character, portal.LevelId);
            if (answer.State == TravelState.Started)
                return PortalTrip.Go;
            return new PortalTrip(false, answer.Reason, Gone: answer.Reason.Contains(NoAnchor, StringComparison.Ordinal));
        }

        if (!IsMapName(portal.Map))
            return new PortalTrip(false, $"'{portal.Map}' is not a map name");

        log.Msg($"[TF2Descent/Portal] unhosted: {hero} goes back through portal {portal.Id}: this process changes level to {portal.Map}, " +
                "which loads that level again from its map and seed\n");
        console.Execute($"changelevel {portal.Map}");
        return PortalTrip.Go;
    }

    /// <summary>The name reaches the console: a plain map name only.</summary>
    public static bool IsMapName(string map) => map.Length > 0 && map.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
