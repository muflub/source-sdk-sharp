using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// The HL2MP AI binding's presence in a level (plan_hl2mp_ai Phase 1 step 4):
/// it says it composed, once per level, so a live boot shows the mod's
/// assembly was listed, loaded and built by the container. The bots, senses
/// and applier of later phases hang off this level lifecycle.
/// </summary>
[SideService(Side.Server)]
public sealed class Hl2mpAiService(ILog log) : IGameLifecycle
{
    /// <summary>After the core's stages, among the mods'; no other mod in the test mod's list uses it.</summary>
    public int LifecycleOrder => ModStages.First + 500;

    /// <summary>How many levels this service has seen come up.</summary>
    public int Levels { get; private set; }

    public void OnLevelInitPostEntity(string mapName)
    {
        Levels++;
        log.Msg($"[SourceSharp/Ai] HL2MP AI composed for {mapName}\n");
    }
}
