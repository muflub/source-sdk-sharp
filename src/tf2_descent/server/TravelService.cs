using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// A player asked to go down (or up): the mine cage, the shaft, a level's
/// stairs (plan_host_sdk §2.10). One request per player at a time; the answer
/// is shown to them.
/// </summary>
[SideService(Side.Server)]
public sealed class TravelService(
    IHostLink link,
    HostService host,
    PlayerService players,
    ClientMessages messages,
    ILog log)
{
    private readonly HashSet<int> _asking = new();

    /// <summary>Starts a travel request for the player at <paramref name="entIndex"/>; the answer arrives a frame or more later.</summary>
    public void Request(int entIndex, TravelDirection direction)
    {
        if (host.Mode != HostingMode.Hosted)
        {
            log.Msg("[TF2Descent] travel asked for with no host (unhosted): nothing happens\n");
            return;
        }

        if (host.Boot is not { } boot || players.ByEntity(entIndex) is not { } player || !_asking.Add(entIndex))
            return;

        _ = Ask(entIndex, player.SteamId, direction, boot);
    }

    private async Task Ask(int entIndex, string steamId, TravelDirection direction, BootInfo boot)
    {
        try
        {
            var answer = await link.Travel(steamId, direction, boot);
            switch (answer.State)
            {
                case TravelState.Started:
                    messages.Notice(entIndex, direction == TravelDirection.Down ? "The cage drops into the dark..." : "Back to town...");
                    break;
                case TravelState.Waiting:
                    messages.Notice(entIndex, $"The cage is being readied (about {Math.Ceiling(answer.Eta.TotalSeconds)} s). Try again then.");
                    break;
                default:
                    messages.Notice(entIndex, $"The way is closed: {answer.Reason}");
                    break;
            }
        }
        catch (Exception e)
        {
            log.Warning($"[TF2Descent] travel for {steamId} failed: {e.Message}\n");
        }
        finally
        {
            _asking.Remove(entIndex);
        }
    }
}
