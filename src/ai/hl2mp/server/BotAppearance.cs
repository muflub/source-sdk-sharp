//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// The USERINFO a bot joins with (plan_hl2mp_ai §3.1): stock HL2MP's
/// unassigned-team model list (hl2mp_bot.h's g_ppszRandomModels), one drawn
/// from the bot's own stream as stock draws one with RandomInt, and a default
/// weapon a fight can use (cl_defaultweapon; a player's default is the gravity
/// gun).
/// </summary>
public static class BotAppearance
{
    public const int ModelCount = 19;

    public const string DefaultWeapon = "weapon_smg1";

    /// <summary>Model <paramref name="i"/> of the list, in stock's order.</summary>
    public static string Model(int i) => i switch
    {
        0 => "models/humans/group03/male_01.mdl",
        1 => "models/humans/group03/male_02.mdl",
        2 => "models/humans/group03/female_01.mdl",
        3 => "models/humans/group03/male_03.mdl",
        4 => "models/humans/group03/female_02.mdl",
        5 => "models/humans/group03/male_04.mdl",
        6 => "models/humans/group03/female_03.mdl",
        7 => "models/humans/group03/male_05.mdl",
        8 => "models/humans/group03/female_04.mdl",
        9 => "models/humans/group03/male_06.mdl",
        10 => "models/humans/group03/female_06.mdl",
        11 => "models/humans/group03/male_07.mdl",
        12 => "models/humans/group03/female_07.mdl",
        13 => "models/humans/group03/male_08.mdl",
        14 => "models/humans/group03/male_09.mdl",
        15 => "models/combine_soldier.mdl",
        16 => "models/combine_soldier_prisonguard.mdl",
        17 => "models/combine_super_soldier.mdl",
        18 => "models/police.mdl",
        _ => throw new ArgumentOutOfRangeException(nameof(i), i, $"the list holds {ModelCount}"),
    };

    /// <summary>HL2MP's Combine team (TEAM_COMBINE) and its Rebels (TEAM_REBELS), hl2mp_gamerules.h; with three or four teams ours, Metro Police and Resistance.</summary>
    public const int Combine = 2, Rebels = 3, MetroPolice = 4, Resistance = 5;

    /// <summary>Where the list's Combine models begin: the rest before them are the citizens.</summary>
    public const int FirstCombine = 15;

    /// <summary>The list's Metro Police model (police.mdl), the Combine's own with two teams.</summary>
    public const int Police = 18;

    /// <summary>
    /// The models <paramref name="team"/> wears with <paramref name="teams"/> playing (user 2026-11-08):
    /// with two teams stock's split (the Combine all four combine models, the Rebels every citizen); with
    /// three or more Metro Police wear police.mdl and the Combine drop it; with four the Resistance wear
    /// the female citizens and the Rebels drop them. Any other team: the whole list.
    /// </summary>
    public static int[] ModelsOf(int team, int teams)
    {
        IEnumerable<int> all = Enumerable.Range(0, ModelCount);
        bool female(int i) => Model(i).Contains("/female_", StringComparison.Ordinal);
        return team switch
        {
            Combine => [.. all.Where(i => i >= FirstCombine && (teams < 3 || i != Police))],
            Rebels => [.. all.Where(i => i < FirstCombine && (teams < 4 || !female(i)))],
            MetroPolice when teams >= 3 => [Police],
            Resistance when teams >= 4 => [.. all.Where(female)],
            _ => [.. all],
        };
    }

    /// <summary>
    /// The model a draw of <paramref name="draw"/> (0..<see cref="ModelCount"/>) dresses a bot in: any of the
    /// list, or under a team the same draw folded onto that team's own models (plan_bots_teams_tf2 B.6),
    /// so the stream's draws stay the same.
    /// </summary>
    public static string ModelFor(int draw, int team, int teams = 2)
    {
        int[] own = ModelsOf(team, teams);
        return Model(own[draw % own.Length]);
    }

    /// <summary>The USERINFO for a bot whose stream draws <paramref name="random"/>, on <paramref name="team"/> when it joins a fixed one.</summary>
    public static KeyValuePair<string, string>[] For(AiRandom random, int team = 0, int teams = 2)
    {
        ArgumentNullException.ThrowIfNull(random);
        return
        [
            new("cl_playermodel", ModelFor(random.NextInt(ModelCount), team, teams)),
            new("cl_defaultweapon", DefaultWeapon),
        ];
    }
}
