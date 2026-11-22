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
/// any level its leader has reached; WO9): in a party, the way down is the
/// leader's to ask for (<see cref="Party.IPartyTravel"/>), so the depths are
/// his; a member asking is told the leader picks, and nothing is asked of the
/// host, unless his party is on a level and he would join it.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TravelService(
    IHostLink link,
    HostService host,
    PlayerService players,
    ClientMessages messages,
    IBossKills bossKills,
    Party.IPartyTravel party,
    Level.ILevelData level,
    LevelHop hop,
    IServerConsole console,
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
    /// <remarks>
    /// The task answers what the host said, or null when nothing was asked of it (unhosted, no
    /// boot or player yet, a closed depth, a request already out). The town portal's route
    /// (WO4) waits on it to know whether its trip started; every other caller leaves it.
    /// </remarks>
    public Task<TravelAnswer?> Request(int entIndex, TravelDirection direction, int depth = 0)
    {
        if (host.Mode != HostingMode.Hosted)
        {
            if (direction is TravelDirection.Down or TravelDirection.Up && level.Current is { } here)
                return Task.FromResult(Unhosted(entIndex, here.Depth, direction));
            log.Msg("[TF2Descent] travel asked for with no host (unhosted): nothing happens\n");
            return Task.FromResult<TravelAnswer?>(null);
        }

        if (host.Boot is not { } boot || players.ByEntity(entIndex) is not { } player)
            return Task.FromResult<TravelAnswer?>(null);

        if (party.Refusal(entIndex, direction) is { } refused)
        {
            messages.Notice(entIndex, refused);
            return Task.FromResult<TravelAnswer?>(null);
        }

        int picked = depth > 0 ? depth : DefaultDepth;
        if (direction == TravelDirection.Down && IsHub(boot) && !MayPick(player, picked))
        {
            messages.Notice(entIndex, DepthClosedToken, picked.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromResult<TravelAnswer?>(null);
        }

        if (!_asking.Add(entIndex))
            return Task.FromResult<TravelAnswer?>(null);

        return Ask(entIndex, player.SteamId, direction, boot, picked);
    }

    /// <summary>The notice for a way down with no level installed below (unhosted); the argument is the depth.</summary>
    public const string NoLevelToken = "#Descent_Travel_NoLevel";

    /// <summary>
    /// Unhosted, a level's way down or up (the town's cage and the portal already have theirs):
    /// this process changes level, down to the next depth's installed level
    /// (<see cref="LevelHop.InstalledLevel"/>), up to the town, and every player in it comes
    /// along; with no level installed below the hero is told so. Nothing is asked of a host, so
    /// the answer is always null.
    /// </summary>
    private TravelAnswer? Unhosted(int entIndex, int depth, TravelDirection direction)
    {
        bool up = direction == TravelDirection.Up;
        int next = depth + 1;
        string? map = up ? Portal.PortalRoute.TownMap : hop.InstalledLevel(next);
        if (map is null)
        {
            string text = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            messages.Notice(entIndex, NoLevelToken, text);
            log.Msg($"[TF2Descent] unhosted: player {entIndex} took the way down from depth {depth}, and no level of depth {text} is installed in {hop.PoolMaps}\n");
            return null;
        }

        // Sent on the next level: a notice sent just before this process's changelevel never reaches the HUD (F28).
        messages.NoticeAfterArrival(entIndex, up ? UpToken : WaysDown.Token(WaysDown.FromLevel(depth)));
        log.Msg($"[TF2Descent] unhosted: player {entIndex} goes {(up ? "up" : "down")} from depth {depth}: this process changes level to {map}, and every player in it comes along\n");
        console.Execute($"changelevel {map}");
        return null;
    }

    private static bool IsHub(BootInfo boot) => string.Equals(boot.InstanceKind, "hub", StringComparison.Ordinal);

    /// <summary>Whether the town may send <paramref name="player"/> to <paramref name="depth"/>: the cage's levels they have reached, and the camps whose bosses their character has killed.</summary>
    private bool MayPick(PlayerService.Entry player, int depth)
        => TownDescents.Destinations(Math.Max(player.ReachedDepth, 0), bossKills.Depths(player.CharacterId)).Contains(depth);

    private async Task<TravelAnswer?> Ask(int entIndex, string steamId, TravelDirection direction, BootInfo boot, int depth)
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
            return answer;
        }
        catch (Exception e)
        {
            log.Warning($"[TF2Descent] travel for {steamId} failed: {e.Message}\n");
            return null;
        }
        finally
        {
            _asking.Remove(entIndex);
        }
    }
}
