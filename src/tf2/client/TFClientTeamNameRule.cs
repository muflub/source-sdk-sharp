//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace TF2.Client;

/// <summary>
/// The TF teams' names as a player reads them: <c>C_TFTeam::UpdateTeamName</c>
/// (<c>client/tf/c_tf_team.cpp:111-206</c>), the localised <c>#TF_RedTeam_Name</c>,
/// <c>#TF_BlueTeam_Name</c> and <c>#TF_Spectators</c>, or RED, BLU and SPECTATORS
/// when the tables lack them; an unassigned team has no name. The tournament,
/// party-leader and Mann vs. Machine names are not ported (no such modes).
/// </summary>
[SideService(Side.Client)]
public sealed class TFClientTeamNameRule(IEngineHud hud) : ITeamNameRule, IOrderedHook
{
    /// <summary>The rule's order: TF2's layer, below the mods' range.</summary>
    public const int Order = 1_000_000 - 1_000 + 23;

    public int HookOrder => Order;

    public string Name(int team, string name) => team switch
    {
        0 => "",
        1 => Localised("#TF_Spectators", "SPECTATORS"),
        2 => Localised("#TF_RedTeam_Name", "RED"),
        3 => Localised("#TF_BlueTeam_Name", "BLU"),
        _ => name,
    };

    private string Localised(string token, string fallback)
    {
        string found = hud.Localise(token);
        return found.Length > 0 && found != token ? found : fallback;
    }
}
