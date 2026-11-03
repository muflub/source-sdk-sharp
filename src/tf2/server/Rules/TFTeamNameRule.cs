//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// The TF teams' names on the server: <c>g_aTeamNames</c>
/// (<c>shared/tf/tf_shareddefs.cpp:18</c>), which <c>CTFTeamManager::Init</c>
/// hands each <c>tf_team</c> it creates, so the log lines name Red and Blue
/// where HL2MP names Combine and Rebels.
/// </summary>
[SideService(Side.Server)]
public sealed class TFTeamNameRule : ITeamNameRule, IOrderedHook
{
    /// <summary>The rule's order: TF2's layer, below the mods' range.</summary>
    public const int Order = 1_000_000 - 1_000 + 22;

    public int HookOrder => Order;

    public string Name(int team, string name) => team switch
    {
        0 => "Unassigned",
        1 => "Spectator",
        2 => "Red",
        3 => "Blue",
        _ => name,
    };
}
