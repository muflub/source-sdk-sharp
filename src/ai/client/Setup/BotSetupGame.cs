namespace SourceSharp.Ai.Client.Setup;

/// <summary>
/// What bot setup asks of the game it sets bots up for (plan_bots_teams_tf2 C.2): the
/// teams a bot can be put on, the skill curves the editor's parameters come from and the
/// pools its names are drawn from. Each binding's client answers it once (HL2MP's teams
/// from <c>mp_tdm_teams</c>, TF2's RED and BLU); everything else on the create-server page
/// and in the editor is the same in every game.
/// </summary>
public interface IBotSetupGame
{
    /// <summary>How many teams play, numbered from <see cref="CreateServerModel.FirstTeam"/> (2 to <see cref="CreateServerModel.MostTeams"/>).</summary>
    int Teams { get; }

    /// <summary>The token naming the team at <paramref name="index"/> (0 is team 2).</summary>
    string TeamToken(int index);

    /// <summary>The game file holding the skill curves (<c>scripts/ai/&lt;game&gt;_skills.json</c>).</summary>
    string SkillCurvesPath { get; }

    /// <summary>
    /// The console line Start runs before the roster when the picked teams are more than one
    /// bot apart (<see cref="CreateServerModel.Uneven"/>), so the game lets the split the player
    /// chose stand; "" when the game's default already does (HL2MP's <c>mp_teams_unbalance_limit</c>
    /// defaults to 0, TF2's to 1; coordinator ruling 2026-11-11, option b).
    /// </summary>
    string UnevenTeamsLine { get; }

    /// <summary>The game file holding the pools the server draws bot names from (<c>scripts/ai/&lt;game&gt;_bot_names.json</c>).</summary>
    string BotNamesPath { get; }
}
