using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using TF2.Descent.Shared;

namespace TF2.Descent.Server;

/// <summary>
/// SDK 2.6.0's Reauth (host plan_steam_auth S-20), on the game thread. The
/// gateway restarted and lost a player's Steam session: their client is asked
/// for a fresh ticket (<see cref="ReauthMessage"/>), answers with a
/// <see cref="TicketReply"/> net message, and the ticket goes to the host
/// under the request's nonce. The player plays on meanwhile; a denial comes
/// back from the host as a Kick, and no answer as "please reconnect".
/// </summary>
[SideService(Side.Server)]
public sealed class ReauthService(
    IHostLink link,
    PlayerService players,
    ClientMessages messages,
    ILog log) : IHostCommandHook, IClientLifecycle, IOrderedHook
{
    /// <summary>A request sent to a client and not yet answered.</summary>
    private sealed record Pending(string SteamId, string Nonce);

    private readonly Dictionary<int, Pending> _pending = new();

    public int HookOrder => 30;
    public int ClientLifecycleOrder => ModStages.First + 30;

    /// <summary>Who is waiting for a ticket, by entity index; for the facts.</summary>
    public int PendingCount => _pending.Count;

    public void OnReauth(ReauthNotice reauth)
    {
        if (players.BySteamId(reauth.SteamId) is not { } entry)
        {
            log.Msg($"[TF2Descent] reauth for {reauth.SteamId}, who is not here\n");
            return;
        }

        if (!messages.Reauth(entry.EntIndex, reauth.Nonce, reauth.GameServerSteamId))
        {
            log.Warning($"[TF2Descent] reauth for {reauth.SteamId}: the nonce cannot travel to the client; the host will time it out\n");
            return;
        }

        // A later request supersedes an earlier one, as the host's nonce does.
        _pending[entry.EntIndex] = new Pending(entry.SteamId, reauth.Nonce);
        log.Msg($"[TF2Descent] asked {reauth.SteamId} for a fresh Steam ticket (by {reauth.Deadline:u})\n");
    }

    public bool OnClientMessage(int entIndex, string key, ReadOnlySpan<byte> payload)
    {
        if (key != TicketReply.Key)
            return false;

        if (!TicketReply.TryRead(payload, out string nonce, out byte[] ticket))
        {
            log.Warning($"[TF2Descent] client {entIndex} sent a malformed ticket reply ({payload.Length} bytes)\n");
            return true;
        }

        // Only an answer to what this client was asked, under the nonce it was asked with.
        if (!_pending.TryGetValue(entIndex, out var pending) || pending.Nonce != nonce)
        {
            log.Msg($"[TF2Descent] client {entIndex} sent a ticket nobody asked it for; ignored\n");
            return true;
        }

        _pending.Remove(entIndex);
        _ = Submit(pending, ticket);
        return true;
    }

    public void OnClientDisconnected(int entIndex) => _pending.Remove(entIndex);

    private async Task Submit(Pending pending, byte[] ticket)
    {
        TicketAnswer answer;
        try
        {
            answer = await link.SubmitTicket(pending.SteamId, pending.Nonce, ticket);
        }
        catch (Exception e)
        {
            answer = new TicketAnswer(null, e.Message);
        }

        if (answer.Ok)
            log.Msg($"[TF2Descent] {pending.SteamId} re-authenticated with Steam\n");
        else
            log.Warning($"[TF2Descent] {pending.SteamId}'s fresh ticket: {answer.Outcome?.ToString() ?? answer.Refusal}\n");
    }
}
