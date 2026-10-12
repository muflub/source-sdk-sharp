using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

//=============================================================================
// How the hosting service's events reach TF2Descent's classes (plan_host_sdk
// §2.11): hook interfaces, implemented by [SideService] classes and called on
// the game thread through IHooks<T>. HostLink is the one class that listens to
// the SDK's C# events; it turns each into one of these.
//=============================================================================

/// <summary>This instance's place in the service: booted, live, or failed to start.</summary>
public interface IHostBootHook
{
    /// <summary>Booting answered: which level this is and the seed its rolls replay from.</summary>
    void OnBooted(BootInfo info) { }

    /// <summary>MapReady answered: the gateway now routes players here.</summary>
    void OnLive() { }

    /// <summary>The SDK could not start or the service refused this instance; the server is quitting.</summary>
    void OnHostFailed(string why) { }
}

/// <summary>The service's commands (spec §6), each on the game thread.</summary>
public interface IHostCommandHook
{
    void OnKick(KickCommand command) { }

    void OnSay(SayCommand command) { }

    void OnDrain(DrainCommand command) { }

    void OnShutdown(ShutdownCommand command) { }

    /// <summary>The player has been routed elsewhere: make their client reconnect.</summary>
    void OnRetry(RetryCommand command) { }

    /// <summary>Run a console command; reply through <see cref="HostLink.ReplyExec"/>.</summary>
    void OnExec(string commandId, string command) { }

    /// <summary>
    /// The player is moving to another instance. HostLink has already
    /// checkpointed and released their character (every held player's, for a
    /// same-process hop) and told the service it is ready, in that order on
    /// the SDK thread; this is the game's notice. For a same-process hop the
    /// game changes level to <see cref="HopNotice.Map"/> (LevelHop); across
    /// processes it waits for the service's Retry.
    /// </summary>
    void OnHopped(HopNotice hop) { }

    /// <summary>The party's next level down is booting (D-H14): its map can be fetched now.</summary>
    void OnNextLevel(NextLevelInfo next) { }

    /// <summary>
    /// The gateway lost this player's Steam session (SDK 2.6.0 Reauth): ask
    /// their client for a fresh ticket and hand it to
    /// <see cref="IHostLink.SubmitTicket"/> before the deadline.
    /// </summary>
    void OnReauth(ReauthNotice reauth) { }
}

/// <summary>What a Reauth asks, for the game thread: the request itself stays on the SDK thread.</summary>
public sealed record ReauthNotice(string SteamId, string Nonce, ulong GameServerSteamId, DateTimeOffset Deadline);

/// <summary>
/// What the game thread learns of one hop (docs/sdk-integration.md §6).
/// </summary>
/// <param name="SameProcess">
/// The whole process becomes the target instance (the host's local
/// SameProcess travel): no Retry comes, the game changes level itself.
/// </param>
/// <param name="LevelFiles">The target's level files to link into pool/maps; empty for the hub.</param>
/// <param name="Ready">Whether the service acknowledged the game's Ready.</param>
public sealed record HopNotice(string SteamId, string TargetInstance, string Map, bool SameProcess,
                               IReadOnlyList<string> LevelFiles, bool Ready);

/// <summary>A player's character lease was lost: stop them acting.</summary>
public interface ILeaseHook
{
    void OnLeaseLost(string steamId, LeaseLostReason reason, string detail);
}

/// <summary>The SDK's connection to the service came or went.</summary>
public interface IHostConnectionHook
{
    void OnConnectionChanged(bool connected);
}

/// <summary>
/// A reason the hop's Ready must wait, asked on the game thread before
/// HostLink checkpoints, releases and acknowledges a PrepareHop (plan_
/// descent_death §4 M2.3): a dead player's Ready waits until their death
/// screen has shown long enough. A hold that throws is logged and does not
/// stop the hop.
/// </summary>
public interface IHopReadyHold
{
    Task BeforeReady(string steamId);
}

/// <summary>
/// TF2.Descent.Server's registrations that an attribute cannot express: the
/// dispatch for each hook interface above, and the rules module under its
/// Host.Contracts contract (TF2.Descent.Rules cannot carry SourceSharp attributes).
/// </summary>
[SideService(Side.Server)]
public sealed class TF2DescentServerRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<IHostBootHook>();
        registry.AddHooks<IHostCommandHook>();
        registry.AddHooks<ILeaseHook>();
        registry.AddHooks<IHostConnectionHook>();
        registry.AddHooks<IHopReadyHold>();
        registry.Add<SourceSharp.Host.Contracts.IGameRules, TF2.Descent.Rules.TF2DescentRules>();
    }
}
