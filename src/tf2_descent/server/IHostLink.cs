using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// What the game's services ask of the hosting service, every task completing
/// on the game thread (plan_host_sdk §2.9). <see cref="HostLink"/> is the one
/// implementation; facts give the services a recording one.
/// </summary>
public interface IHostLink : IDisposable
{
    HostingMode Mode();

    void Start();

    Task<HostResult<BootInfo>> Booting(string map);

    Task<HostResult<int>> MapReady(int port, string map);

    void Players(int count);

    /// <summary>Spec §5's join. The outcome's SteamId, not <paramref name="engineSteamId"/>, names the player from then on.</summary>
    Task<JoinOutcome> Join(string peer, string engineSteamId, BootInfo boot, string className);

    Task<bool> Checkpoint(string steamId);

    Task<bool> Leave(string steamId, string peer);

    void ReplyExec(string commandId, string output);

    Task<TravelAnswer> Travel(string steamId, TravelDirection direction, BootInfo boot);

    /// <summary>The client's fresh ticket for the player's pending Reauth; Stale when none is pending under that nonce.</summary>
    Task<TicketAnswer> SubmitTicket(string steamId, string nonce, byte[] ticket);

    /// <summary>
    /// D-H16: tells the host this player died at <paramref name="position"/>,
    /// which moves everything they carried onto their corpse and records where
    /// they died, so the way back to it survives the level. True when the host
    /// took the record; false is an answer, not a lost request — the caller
    /// retries a false the same way as a throw (plan_descent_death §4 M2.4).
    /// </summary>
    Task<bool> RecordDeath(string steamId, Vector position);

    /// <summary>
    /// D-H16: moves the player's own corpse's items back onto the character,
    /// as many as fit. Only the owner's corpse: any other answer is a refusal.
    /// </summary>
    Task<LootAnswer> LootCorpse(string steamId);
}
