namespace SourceSharp.Ai.Server.Skills;

/// <summary>The skill curves the brains read (plan_hl2mp_ai §10), or why there are none.</summary>
public interface ISkillCurveSource
{
    SkillCurves? Curves { get; }

    string Refusal { get; }

    /// <summary>Later bots read <paramref name="parameter"/> as <paramref name="value"/> at <paramref name="level"/>, until the game ends (<c>ai_curve</c>, the aim range's probes).</summary>
    SkillCurves? Probe(string parameter, int level, float value);
}
