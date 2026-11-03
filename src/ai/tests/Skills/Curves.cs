namespace SourceSharp.Ai.Tests.Skills;

/// <summary>The game's skill curves, as the server reads them (a missing file fails the fact that asks).</summary>
public static class Curves
{
    public static string Path => SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_skills.json");

    public static SkillCurves Game() => SkillCurves.Parse(File.ReadAllText(Path));
}
