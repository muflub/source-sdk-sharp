namespace SourceSharp.Server.Interface;

/// <summary>
/// The development API, as the server sees it.
///
/// The contract lives here rather than with the implementation so the server
/// never has to reference the implementation. That is what lets the assembly
/// stay off disk-to-memory entirely unless it was asked for: a compile-time
/// reference would load Debug.Api into every server, whether or not
/// anybody wanted a debugging socket open.
/// </summary>
public interface IDebugApi : IDisposable
{
    /// <summary>Begins listening. False if it could not, which is not fatal.</summary>
    bool Start();

    /// <summary>
    /// Runs whatever the API was asked to do, on the game thread. Called once
    /// per server think: its requests arrive on another thread and the engine
    /// is single-threaded, so this is the only place their work can happen.
    /// </summary>
    void Pump();

    /// <summary>
    /// Mints an invitation to the debug channel for one joining client, or null
    /// when there is no channel to invite anybody to.
    ///
    /// <para>
    /// <b>NULL IS THE DEFAULT AND NULL MEANS THE SERVER DID NOT OPT IN.</b> The
    /// channel is off unless this process was started with
    /// <c>-debuguiserve</c> — which is a separate flag from <c>-debugapi</c>, which
    /// is itself what decides whether this interface has an implementation
    /// behind it at all. A server that has not opted in never invites, and the
    /// call site is one <c>if</c> that is false.
    /// </para>
    ///
    /// <para>
    /// <b>THE INVITATION CARRIES NO ADDRESS</b>, and the type it returns
    /// structurally cannot carry one. See <c>DebugChannelInvitation.TryRead</c>
    /// for the rule and for why it is enforced as a payload-length check rather
    /// than as a convention.
    /// </para>
    /// </summary>
    /// <param name="into">Where the invitation's bytes go: the game sends them as they are.</param>
    /// <returns>The bytes written; 0 when there is no invitation, -1 when it did not fit.</returns>
    int WriteInvitation(int slot, Span<byte> into);

    /// <summary>
    /// The level is going.
    ///
    /// <para>
    /// What this exists for is the nonce table: an invitation offered for the
    /// map that is ending must not still be good for the map that follows.
    /// Sessions and sockets go with <see cref="IDisposable.Dispose"/>; the offers
    /// would otherwise outlive the thing they were offers to.
    /// </para>
    /// </summary>
    void LevelShutdown();

}
