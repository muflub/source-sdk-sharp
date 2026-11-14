using SourceSharp.Abi;

namespace SourceSharp.Ai.Server;

/// <summary>A bot's skill as the scoreboard shows it beside its name (user ruling 2026-11-01, plan_hl2mp_ai §3.5.1).</summary>
public static class BotSkillLabel
{
    /// <summary>"(Easy)", "(Medium)" (the slider's name for <c>normal</c>), "(Hard)", "(Expert)", or "(Custom N)" for N points spent.</summary>
    public static string For(SkillProfile skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (SkillPresets.TryGet(skills.Name, out SkillProfile? preset) && preset!.Spent == skills.Spent)
        {
            return skills.Name switch
            {
                "easy" => "(Easy)",
                "normal" => "(Medium)",
                "hard" => "(Hard)",
                _ => "(Expert)",
            };
        }

        return $"(Custom {skills.Spent})";
    }
}

/// <summary>The pools a bot's name is drawn from, or why there are none.</summary>
public interface IBotNameSource
{
    BotNamePools? Pools { get; }

    string Refusal { get; }
}
