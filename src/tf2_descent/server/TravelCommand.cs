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
public sealed class TravelCommand(TravelService travel, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        if (!TryParse(args, out int slot, out var direction))
        {
            log.Warning("[TF2Descent] usage: tf2_descent_travel <player slot> <down|up>\n");
            return;
        }

        travel.Request(slot, direction);
    }

    /// <summary>A slot of 1 or more, then down or up (any case).</summary>
    public static bool TryParse(string args, out int slot, out TravelDirection direction)
    {
        direction = TravelDirection.Down;
        slot = 0;

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out slot) || slot <= 0)
            return false;

        switch (parts[1].ToLowerInvariant())
        {
            case "down": direction = TravelDirection.Down; return true;
            case "up":   direction = TravelDirection.Up;   return true;
            default:     slot = 0; return false;
        }
    }
}
