using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Code compiled into BOTH sides — the C# equivalent of game/shared under
/// CLIENT_DLL / GAME_DLL. Each side's collectible ALC loads its own copy, so
/// the two are distinct types that never exchange instances; they communicate
/// over the network, exactly as the C++ halves do.
/// </summary>
public static class SharedInfo
{
    /// <summary>
    /// Both sides print this. Seeing it from client AND server is the proof
    /// that the shared source is in both sides' assemblies (it compiles into
    /// each since plan_side_isolation P7).
    /// </summary>
    public const string Version = "SourceSharp 0.1.0";

    public static void Announce(Side side, ILog log)
    {
        log.Msg($"[SourceSharp/{side}] shared code says: {Version}\n");
    }
}
