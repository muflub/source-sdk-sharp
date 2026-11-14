using SourceSharp.Abi;

using TF2.Descent.Server.Horde.Director;

namespace TF2.Descent.Server.Party;

/// <summary>
/// The party size a level's roster is rolled for (plan_tf2_rpg D11; WO9), asked once at level
/// ready: the largest party this server holds, counting its members wherever they are, and never
/// fewer than the human clients connected (<see cref="ConnectedHordeParty"/>, what was asked
/// before parties): two strangers on one listen server still meet a horde scaled for two.
/// At a level change the clients are still reconnecting when the level is ready (measured live:
/// the connected count was 1 with two clients travelling), so a connected hero's party cannot be
/// asked for; unhosted, every party this process holds travelled with it (a disconnect leaves the
/// party), so the largest of them is the travelling party. Hosted, the host's <c>BootInfo</c>
/// names no party (SDK 2.6.0): a fresh instance holds none at level ready, and the size is the
/// connected count, as before.
/// </summary>
[SideService(Side.Server, As = [typeof(IHordeParty)])]
public sealed class PartyHordeParty(ConnectedHordeParty connected, IParties parties) : IHordeParty
{
    public int PartySize()
    {
        int largest = 0;
        foreach (var party in parties.All)
            largest = Math.Max(largest, party.Size);
        return Math.Max(connected.PartySize(), largest);
    }
}
