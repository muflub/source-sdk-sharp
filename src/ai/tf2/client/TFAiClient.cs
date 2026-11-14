using SourceSharp.Abi;
using SourceSharp.Client;

namespace SourceSharp.Ai.TF2.Client;

/// <summary>
/// The TF2 AI binding's presence on the client (plan_bots_teams_tf2 P6 step 8): it says it
/// composed, once per level, so a live boot shows the mod's AI client assemblies were
/// listed, loaded and built by the container, as HL2MP's Hl2mpAiClient does.
/// </summary>
[SideService(Side.Client)]
public sealed class TFAiClient(ILog log) : IClientLevelHook, IOrderedHook
{
    /// <summary>In the mods' range, above TF2's own (999,000-999,999, TFClassTable.LoadOrder's).</summary>
    public const int Order = 1_000_500;

    public int HookOrder => Order;

    /// <summary>How many levels this client has seen come up.</summary>
    public int Levels { get; private set; }

    public void OnLevelInit(string mapName)
    {
        Levels++;
        log.Msg($"[SourceSharp/Ai] TF2 AI client composed for {mapName}\n");
    }
}
