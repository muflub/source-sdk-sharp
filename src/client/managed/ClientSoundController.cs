using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The client's sound envelope controller (sound cluster 17, lane c2-sims):
/// the shared <see cref="SoundEnvelopeController"/>, stepped once a frame by
/// the client module (stock's client system <c>Update</c>) and reset when the
/// level shuts down (stock's <c>LevelShutdownPreEntity</c>).
/// </summary>
public sealed class ClientSoundController(IEngineSound patches, IGameClock clock, IEngineHost host, IEngineConsole console)
    : SoundEnvelopeController(patches, clock, host, console), IClientSoundController
{
}
