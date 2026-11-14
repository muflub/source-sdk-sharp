using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Horde.Director;

/// <summary>
/// The party a level's roster is rolled for (plan_tf2_rpg D11; plan_descent_logic HO9): asked
/// once, at level ready. A hero who joins later changes nothing: the roster is the level's for
/// its life, as the seed is.
/// </summary>
public interface IHordeParty
{
    int PartySize();
}

/// <summary>
/// The human clients connected at level ready (a client in a slot that is no bot), at least
/// one: on a fresh boot nobody has connected yet, so one; on a level change the party that
/// travelled is still connected.
/// </summary>
[SideService(Side.Server, As = [typeof(IHordeParty)])]
public sealed class ConnectedHordeParty(IConnectedClients clients, IBotPlayers bots) : IHordeParty
{
    public int PartySize()
    {
        int connected = 0;
        for (int slot = 1; slot <= HordeController.MaxHeroes; slot++)
            if (clients.UserId(slot) != -1)
                connected++;
        return Math.Max(1, connected - bots.Bots.Count);
    }
}
