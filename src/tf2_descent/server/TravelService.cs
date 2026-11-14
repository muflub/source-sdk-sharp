using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using TF2.Descent.Rules;

namespace TF2.Descent.Server;

/// <summary>
/// A player asked to go down (or up): the mine cage, the shaft, a level's
/// stairs (plan_host_sdk §2.10). One request per player at a time; the answer
/// is shown to them.
/// <para>
/// From the town, down goes to a picked depth (plan_descent_mine_levels D3):
/// one of <c>TownDescents.Destinations</c> for the depth the player's
/// character has reached and the bosses it has killed, else refused with a
/// notice and nothing asked of the host. The party rule (D8: a party may enter
/// any level its leader has reached) waits on parties; until then the
/// requesting player's own character decides.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TravelService(
    IHostLink link,
    HostService host,
    PlayerService players,
    ClientMessages messages,
    IBossKills bossKills,
    ILog log)
{
    /// <summary>The depth a descent from the town goes to when none was picked: the first level.</summary>
    public const int DefaultDepth = 1;

    /// <summary>The notice for a depth the player may not pick; its argument is the depth.</summary>
    public const string DepthClosedToken = "#Descent_Travel_DepthClosed";

    /// <summary>The host is preparing the level; the argument is the seconds until it is worth asking again.</summary>
    public const string WaitingToken = "#Descent_Travel_Waiting";

    /// <summary>The host refused the travel; the argument is its reason.</summary>
    public const string RefusedToken = "#Descent_Travel_Refused";

    /// <summary>On the way up, through the town portal.</summary>
    public const string UpToken = "#Descent_Travel_Up";

    /// <summary>On the way back to where the character died.</summary>
    public const string CorpseToken = "#Descent_Travel_Corpse";

    /// <summary>
    /// The notice for travel that started: going down, the way down it takes
    /// (<see cref="WaysDown"/>: from the town to the picked depth, from a
    /// level out of it); up and back to a corpse, their own.
    /// </summary>
    public static string StartedToken(TravelDirection direction, BootInfo boot, int depth) => direction switch
    {
        TravelDirection.Up => UpToken,
        TravelDirection.Corpse => CorpseToken,
        _ => WaysDown.Token(IsHub(boot) ? WaysDown.FromTown(depth) : WaysDown.FromLevel(boot.Depth)),
    };

    private readonly HashSet<int> _asking = new();

    /// <summary>
    /// Starts a travel request for the player at <paramref name="entIndex"/>;
    /// the answer arrives a frame or more later. <paramref name="depth"/> is
    /// the depth picked for a descent from the town (0: <see cref="DefaultDepth"/>);
    /// a level's way down always leads to the next depth.
    /// </summary>
    public void Request(int entIndex, TravelDirection direction, int depth = 0)
    {
        if (host.Mode != HostingMode.Hosted)
        {
            log.Msg("[TF2Descent] travel asked for with no host (unhosted): nothing happens\n");
            return;
        }

        if (host.Boot is not { } boot || players.ByEntity(entIndex) is not { } player)
            return;

        int picked = depth > 0 ? depth : DefaultDepth;
        if (direction == TravelDirection.Down && IsHub(boot) && !MayPick(player, picked))
        {
            messages.Notice(entIndex, DepthClosedToken, picked.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        if (!_asking.Add(entIndex))
            return;

        _ = Ask(entIndex, player.SteamId, direction, boot, picked);
    }

    private static bool IsHub(BootInfo boot) => string.Equals(boot.InstanceKind, "hub", StringComparison.Ordinal);

    /// <summary>Whether the town may send <paramref name="player"/> to <paramref name="depth"/>: the cage's levels they have reached, and the camps whose bosses their character has killed.</summary>
    private bool MayPick(PlayerService.Entry player, int depth)
        => TownDescents.Destinations(Math.Max(player.ReachedDepth, 0), bossKills.Depths(player.CharacterId)).Contains(depth);

    private async Task Ask(int entIndex, string steamId, TravelDirection direction, BootInfo boot, int depth)
    {
        try
        {
            var answer = await link.Travel(steamId, direction, boot, depth);
            switch (answer.State)
            {
                case TravelState.Started:
                    messages.Notice(entIndex, StartedToken(direction, boot, depth));
                    break;
                case TravelState.Waiting:
                    messages.Notice(entIndex, WaitingToken,
                                    Math.Ceiling(answer.Eta.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    messages.Notice(entIndex, RefusedToken, answer.Reason);
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
