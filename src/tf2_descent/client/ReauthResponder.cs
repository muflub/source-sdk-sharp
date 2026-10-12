using SourceSharp.Abi;
using TF2.Descent.Shared;

namespace TF2.Descent.Client;

/// <summary>
/// The client half of the Reauth (host plan_steam_auth S-20): mints a fresh
/// Steam auth session ticket for the gateway's game server, cancelling the
/// one before it, and sends it back as a <see cref="TicketReply"/>.
/// </summary>
[SideService(Side.Client)]
public sealed class ReauthResponder(ISteamAuthTickets tickets, IEngineNetworking network, ILog log) : IReauthHook
{
    private uint _previous;

    public void OnReauth(string nonce, ulong gameServerSteamId)
    {
        // The ticket the gateway lost is useless now; Steam stops honouring it.
        if (_previous != 0)
        {
            tickets.Cancel(_previous);
            _previous = 0;
        }

        if (tickets.Create(gameServerSteamId, out string why) is not { } ticket)
        {
            log.Warning($"[TF2Descent] the server asked for a fresh Steam ticket and none could be made: {why}\n");
            return;
        }

        _previous = ticket.Handle;
        if (TicketReply.Write(nonce, ticket.Bytes) is not { } payload)
        {
            log.Warning($"[TF2Descent] a {ticket.Bytes.Length}-byte Steam ticket does not fit a reply\n");
            return;
        }

        if (!network.SendServerMessage(TicketReply.Key, payload))
            log.Warning("[TF2Descent] the fresh Steam ticket could not be sent: no channel to the server\n");
    }
}
