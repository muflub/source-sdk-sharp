using SourceSharp.Abi;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// How much each bot's routes mind where players died (lane G4a): its Cunning over the cap, by its slot,
/// set by <see cref="TFExperienceUse"/> from the bots' skills before their commands run and read by the
/// navigation profile (<see cref="TFNavProfile.HazardFor"/>). Its own service so the profile, which the
/// navigation is built with, need not hold the bots, which hold the navigation. Read from any thread: a
/// new table replaces the old one whole.
/// </summary>
[SideService(Side.Server)]
public sealed class TFRouteWeights
{
    private volatile float[] _bySlot = [];

    /// <summary>The weight of the bot in <paramref name="slot"/>; 0 for a player that is no bot of ours.</summary>
    public float Of(int slot)
    {
        float[] now = _bySlot;
        return (uint)slot < (uint)now.Length ? now[slot] : 0f;
    }

    /// <summary>Replaces the table: one weight a slot.</summary>
    public void Set(float[] bySlot) => _bySlot = bySlot ?? throw new ArgumentNullException(nameof(bySlot));

    /// <summary>The table of <paramref name="bots"/>' weights by slot, <paramref name="slots"/> long: each bot's <see cref="WeightOf"/> its skills, 0 for every other slot.</summary>
    public static float[] Table(IEnumerable<AiBot> bots, int slots)
    {
        ArgumentNullException.ThrowIfNull(bots);
        var table = new float[slots];
        foreach (AiBot b in bots)
        {
            if ((uint)b.Id.Slot < (uint)table.Length)
                table[b.Id.Slot] = WeightOf(b.Skills);
        }

        return table;
    }

    /// <summary>A profile's route weight: its Cunning over the cap (Easy 0.1, Normal 0.3, Hard 0.5, Expert 0.6); 0 without the skill.</summary>
    public static float WeightOf(SkillProfile skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        int i = skills.IndexOf("cunning");
        return i < 0 ? 0f : Math.Clamp(skills.Levels[i], 0, StandardSkills.Cap) / (float)StandardSkills.Cap;
    }
}
