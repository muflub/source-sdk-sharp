using SourceSharp.Abi;
using SourceSharp.Ai.Client.Setup;

namespace SourceSharp.Ai.TF2.Client;

/// <summary>
/// What bot setup asks of TF2 (plan_bots_teams_tf2 C.2): two teams, RED (2) and BLU (3),
/// named as TF2's own HUD names them, and TF2's skill curves and bot names, the files the
/// server's TFSkillCurves and TFGameFileBotNames read.
/// </summary>
[SideService(Side.Client, As = [typeof(IBotSetupGame)])]
public sealed class TFBotSetup : IBotSetupGame
{
    public const string CurvesPath = "scripts/ai/tf2_skills.json";

    public const string NamesPath = "scripts/ai/tf2_bot_names.json";

    /// <summary>RED and BLU: TF2 plays no third team.</summary>
    public int Teams => 2;

    /// <summary><c>#TF_Name_Red</c> for RED (team 2), <c>#TF_Name_Blue</c> for BLU (team 3).</summary>
    public string TeamToken(int index) => index == 0 ? "#TF_Name_Red" : "#TF_Name_Blue";

    public string SkillCurvesPath => CurvesPath;

    public string BotNamesPath => NamesPath;

    /// <summary>
    /// TF2 keeps its teams within <c>mp_teams_unbalance_limit</c> (1 by default; TFTeams refuses
    /// a join past it), so a split the player picked more than one apart turns the check off
    /// (0). An even split, or each bot joining the smaller team, leaves stock's default.
    /// </summary>
    public const string UnevenLine = "mp_teams_unbalance_limit 0";

    public string UnevenTeamsLine => UnevenLine;
}
