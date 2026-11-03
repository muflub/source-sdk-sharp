using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Client;

/// <summary>
/// The HL2MP AI binding's presence on the client (plan_hl2mp_ai Phase 5c
/// step 1): it says it composed, once per level, so a live boot shows the
/// mod's client assembly was listed, loaded and built by the container, as
/// the server's <c>Hl2mpAiService</c> does for the server's.
/// </summary>
[SideService(Side.Client)]
public sealed class Hl2mpAiClient(ILog log) : IClientLevelHook, IOrderedHook
{
    /// <summary>After the core's level hooks (the core's own hold the low orders); no other hook in the test mod uses it.</summary>
    public int HookOrder => 500;

    /// <summary>How many levels this client has seen come up.</summary>
    public int Levels { get; private set; }

    public void OnLevelInit(string mapName)
    {
        Levels++;
        log.Msg($"[SourceSharp/Ai] HL2MP AI client composed for {mapName}\n");
    }
}
