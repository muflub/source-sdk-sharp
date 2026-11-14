using SourceSharp.Abi;
using SourceSharp.Ai.Client.Setup;
using SourceSharp.Client.Vgui;

namespace SourceSharp.Ai.Hl2mp.Client;

/// <summary>
/// What bot setup asks of HL2MP: the teams team deathmatch plays (<c>mp_tdm_teams</c>, 2 to 4;
/// user 2026-11-08), named Combine, Rebels, Metro Police and Resistance, and HL2MP's skill
/// curves and bot names.
/// </summary>
[SideService(Side.Client, As = [typeof(IBotSetupGame)])]
public sealed class Hl2mpBotSetup(ClTdmTeamsConVar teamCount) : IBotSetupGame
{
    public const string CurvesPath = "scripts/ai/hl2mp_skills.json";

    /// <summary>The server's pools (GameFileBotNames.Path).</summary>
    public const string NamesPath = "scripts/ai/hl2mp_bot_names.json";

    public int Teams => teamCount.Value;

    /// <summary><c>#Ai_CreateServer_Team2</c> (the Combine, hl2mp_gamerules.h's TEAM_COMBINE) and on.</summary>
    public string TeamToken(int index) => $"#Ai_CreateServer_Team{index + CreateServerModel.FirstTeam}";

    public string SkillCurvesPath => CurvesPath;

    public string BotNamesPath => NamesPath;

    /// <summary>None: team deathmatch's <c>mp_teams_unbalance_limit</c> defaults to 0 (TeamDeathmatchJoin), so an uneven split already stands.</summary>
    public string UnevenTeamsLine => "";
}
