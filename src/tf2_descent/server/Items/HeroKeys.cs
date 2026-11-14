using System.Globalization;
using SourceSharp.Abi;

namespace TF2.Descent.Server.Items;

/// <summary>
/// The name the item ledger knows a player by. Hosted: the SteamID the host answered at the
/// join (the engine's is not the player's, sdk-integration.md §5), or none until the join has.
/// Unhosted: the engine's SteamID64, or <c>slot&lt;n&gt;</c> when the engine has none (a LAN
/// client, a bot), which holds across respawns and map changes, the client keeping its slot.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroKeys(HostService host, PlayerService players, IConnectedClients clients)
{
    /// <summary>The hero in the client slot <paramref name="entIndex"/>; null for an empty slot or a hosted player not joined yet.</summary>
    public string? Of(int entIndex)
    {
        if (host.Mode == HostingMode.Hosted)
            return players.ByEntity(entIndex)?.SteamId;
        if (clients.UserId(entIndex) < 0)
            return null;
        ulong steam = clients.SteamId64(entIndex);
        return steam != 0 ? steam.ToString(CultureInfo.InvariantCulture) : "slot" + entIndex.ToString(CultureInfo.InvariantCulture);
    }
}
