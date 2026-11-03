using SourceSharp.Ai.Hl2mp.Trees;

namespace SourceSharp.Ai.Replay;

/// <summary><c>ai-replay jank</c>'s game side: HL2MP's weapon reaches, and the tree of this build for a recording that carries none.</summary>
public static class JankTool
{
    /// <summary>A weapon class's reach, as the bots fight with it.</summary>
    public static float WeaponRange(string weaponClass) => Hl2mpWeaponRanges.For(weaponClass).EffectiveRange;

    /// <summary>The shape this build's decider has: a tree needs no game to be read.</summary>
    public static AiDecisionTree ThisBuildsTree()
    {
        var bot = new ActorId(1, 1);
        var world = new MemoryWorld(new MapKnowledge("", [], [], new WeaponKnowledge([])), null, AiMetrics.TickSeconds, id => id.ToString());
        var memory = new BotMemory(bot, 2, new MemoryParams(5f, MemoryParams.HabitHalfLife), world, new HabitStore());
        var decider = new Hl2mpDecider(new BrainSetup(bot, 1, memory, default, AiMetrics.TickSeconds));
        return new AiDecisionTree([.. decider.NodeNames], [.. decider.NodeBranches]);
    }

    /// <summary>The report of one recording.</summary>
    public static JankReport Read(string path)
    {
        using FileStream input = File.OpenRead(path);
        return AiJank.Read(input, WeaponRange, ThisBuildsTree());
    }
}
