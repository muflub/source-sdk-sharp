namespace SourceSharp.Ai.Hl2mp.Client.Setup;

/// <summary>The screens' copy for the ten skills (game/mod_test/resource/test_english.txt), in the pool's order.</summary>
public static class SkillTokens
{
    /// <summary>The token of skill <paramref name="index"/> of the standard ten.</summary>
    public static string Of(int index) => index switch
    {
        0 => "#Ai_Skill_Accuracy",
        1 => "#Ai_Skill_Alertness",
        2 => "#Ai_Skill_Aggression",
        3 => "#Ai_Skill_Cover",
        4 => "#Ai_Skill_Tenacity",
        5 => "#Ai_Skill_Cunning",
        6 => "#Ai_Skill_Discipline",
        7 => "#Ai_Skill_Agility",
        8 => "#Ai_Skill_Nerve",
        9 => "#Ai_Skill_Headhunter",
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "the ten skills are 0..9"),
    };

    /// <summary>A profile's style name as the two skills' tokens, highest first, ties by table order (§10).</summary>
    public static (string First, string Second) Style(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int first = -1, second = -1;
        for (int i = 0; i < profile.Levels.Length; i++)
        {
            if (first < 0 || profile.Levels[i] > profile.Levels[first])
            {
                second = first;
                first = i;
            }
            else if (second < 0 || profile.Levels[i] > profile.Levels[second])
            {
                second = i;
            }
        }

        return (first < 0 ? "" : Of(first), second < 0 ? "" : Of(second));
    }
}
