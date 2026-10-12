using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// How a ticket submission came out: the host's verdict, or the refusal of
/// the call itself (the service unreachable, say).
/// </summary>
public sealed record TicketAnswer(TicketOutcome? Outcome, string? Refusal = null)
{
    public bool Ok => Outcome == TicketOutcome.Ok;
}

/// <summary>
/// Each player's pending SDK Reauth, on the SDK thread: the latest request
/// supersedes the earlier, and a ticket is submitted only under the pending
/// request's nonce, once.
/// </summary>
internal sealed class PendingReauths
{
    private readonly Dictionary<string, ReauthRequest> _bySteamId = new(StringComparer.Ordinal);

    public void Add(ReauthRequest r) => _bySteamId[r.SteamId] = r;

    public void Forget(string steamId) => _bySteamId.Remove(steamId);

    /// <summary>Stale, as the host would answer, for a nonce that is not the pending one (superseded, or already answered).</summary>
    public async Task<TicketAnswer> Submit(string steamId, string nonce, byte[] ticket)
    {
        if (!_bySteamId.TryGetValue(steamId, out var r) || r.Nonce != nonce)
            return new TicketAnswer(TicketOutcome.Stale);

        _bySteamId.Remove(steamId);
        var result = await r.Submit(ticket);
        return result.Ok
            ? new TicketAnswer(result.Value)
            : new TicketAnswer(null, $"{result.Refusal!.Error}: {result.Refusal.Reason}");
    }
}

public sealed partial class HostLink
{
    // SDK thread only.
    private readonly PendingReauths _reauths = new();

    /// <summary>Raised inside Pump on the SDK thread: kept here, told to the game.</summary>
    private void OnReauth(ReauthRequest r)
    {
        _reauths.Add(r);
        var notice = new ReauthNotice(r.SteamId, r.Nonce, r.GameServerSteamId, r.Deadline);
        ToGame(nameof(IHostCommandHook.OnReauth), h => h.OnReauth(notice));
    }

    /// <summary>SDK 2.6.0 Reauth: submits the client's ticket under the request it answers (<see cref="PendingReauths.Submit"/>).</summary>
    public Task<TicketAnswer> SubmitTicket(string steamId, string nonce, byte[] ticket) =>
        Call(_ => _reauths.Submit(steamId, nonce, ticket));
}
