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

    /// <summary>The USERINFO for a bot whose stream draws <paramref name="random"/>.</summary>
    public static KeyValuePair<string, string>[] For(AiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return
        [
            new("cl_playermodel", Model(random.NextInt(ModelCount))),
            new("cl_defaultweapon", DefaultWeapon),
        ];
    }
}
