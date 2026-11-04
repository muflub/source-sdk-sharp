using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// Sends a player down or up, as the level transition they touched would
/// (plan_host_sdk §11, P9 criterion 3). The game pods of a hosted cluster run
/// without the dev API, so a hop is driven from the service's Exec, which runs
/// a server console command on the instance. Same path as the map's
/// logic_level_transition: TravelService.Request.
/// </summary>
[ConCommand("tf2_descent_travel",
            Help = "tf2_descent_travel <player slot> <down|up>: travel as a level transition would.")]
public sealed class TravelCommand(TravelService travel)
{
    public void Execute([ArgRange(1, int.MaxValue)] int slot, Way way)
        => travel.Request(slot, way == Way.Up ? TravelDirection.Up : TravelDirection.Down);

    /// <summary>What a level transition can do; a corpse's travel is the death flow's, not a line's.</summary>
    public enum Way
    {
        Down,
        Up,
    }
}
