using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>
/// The four presets of §10 as read-only profiles over the ten: easy (20
/// points), normal (35; the slider's Medium), hard (50) and expert (60, the
/// whole pool, which is why it is reachable only as a custom bot). Each
/// matches its stock namesake's recognition time through the Alertness
/// curve (Phase 5).
/// </summary>
public static class SkillPresets
{
    //                                       Acc Ale Agg Cov Ten Cun Dis Agi Ner Head
    public static readonly ImmutableArray<int> EasyLevels = [2, 2, 3, 1, 2, 1, 2, 2, 3, 2];
    public static readonly ImmutableArray<int> NormalLevels = [4, 4, 4, 3, 3, 3, 4, 3, 4, 3];
    public static readonly ImmutableArray<int> HardLevels = [6, 6, 5, 5, 5, 5, 6, 4, 5, 3];
    public static readonly ImmutableArray<int> ExpertLevels = [8, 7, 6, 6, 6, 6, 7, 5, 5, 4];

    /// <summary>The preset names, easiest first.</summary>
    public static readonly ImmutableArray<string> Names = ["easy", "normal", "hard", "expert"];

    /// <summary>The preset called <paramref name="name"/>, or false.</summary>
    public static bool TryGet(string name, out SkillProfile? profile)
    {
        ImmutableArray<int> levels = name switch
        {
            "easy" => EasyLevels,
            "normal" => NormalLevels,
            "hard" => HardLevels,
            "expert" => ExpertLevels,
            _ => default,
        };
        profile = levels.IsDefault ? null : new SkillProfile(name, StandardSkills.Ten, levels);
        return profile is not null;
    }
}
