using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The mode the next level starts in (plan_bots_teams_tf2 D.2): what the
/// create-server page chose, or the last <c>sv_mode</c> switch, kept across a
/// map change. Empty leaves the pick to the map and the mods' selectors.
/// </summary>
[ConVar("sv_mode_next", "", Help = "The game mode the next level starts in when its map can play it; empty: the map's own")]
public sealed class SvModeNextConVar : StringConVar { }

/// <summary>
/// First on the mode-selector chain: <c>sv_mode_next</c>'s mode, when the
/// level's map can play it (<see cref="GameModeCatalog.Supports"/>); otherwise
/// no opinion, said in the log, and the map's own pick follows.
/// </summary>
public sealed class RequestedModeSelector(SvModeNextConVar next, GameModeCatalog catalog, ILog log) : IGameModeSelector, IOrderedHook
{
    /// <summary>Before every mod's selector: a mode asked for by name outranks one guessed from the map.</summary>
    public const int Order = -1000;

    public int HookOrder => Order;

    public string? PickMode()
    {
        string id = next.Value;
        if (id.Length == 0)
            return null;
        string map = catalog.CurrentMap;
        var support = catalog.Supports(map, id);
        if (support.Supported)
            return id;
        log.Msg($"[SourceSharp/Modes] sv_mode_next '{id}' passed over: {support.Reason}\n");
        return null;
    }
}
