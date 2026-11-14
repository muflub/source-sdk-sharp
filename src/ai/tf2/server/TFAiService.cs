using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// The TF2 AI binding's presence in a level (plan_bots_teams_tf2 P6 step 1):
/// it says it composed, once per level, so a live boot shows the binding was
/// listed in the tf2 mod, loaded and built by the container.
/// </summary>
[SideService(Side.Server)]
public sealed class TFAiService(ILog log) : IGameLifecycle
{
    /// <summary>After the core's stages, among the mods', beside the HL2MP binding's slot (never composed with it).</summary>
    public int LifecycleOrder => ModStages.First + 510;

    /// <summary>How many levels this service has seen come up.</summary>
    public int Levels { get; private set; }

    public void OnLevelInitPostEntity(string mapName)
    {
        Levels++;
        log.Msg($"[SourceSharp/Ai] TF2 AI composed for {mapName}\n");
    }
}
