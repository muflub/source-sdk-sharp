//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The scoreboard's class column (stock's <c>g_TF_PR-&gt;GetPlayerClass</c>,
/// tf_clientscoreboard.cpp:1473): the class each live <c>tf_player_data</c>
/// carrier holds, as the non-localized name
/// (<c>g_aPlayerClassNames_NonLocalized</c>). Whether a row may show it is the
/// screen's gate — stock asks the local team, not the data.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFScoreboardClasses)])]
public sealed class TFScoreboardClasses(TFClientCarriers carriers) : ITFScoreboardClasses
{
    public string ClassNameOf(int playerIndex)
    {
        var carrier = carriers.Of(playerIndex);
        if (carrier is null)
            return "";                       // no carrier: stock's IsConnected arm is false
        var tfClass = (TFClass)carrier.State.PlayerClass;
        return tfClass == TFClass.Undefined ? "" : TFClassNames.NonLocalized(tfClass);
    }
}
