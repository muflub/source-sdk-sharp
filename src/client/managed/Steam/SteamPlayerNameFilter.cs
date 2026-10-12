//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

namespace SourceSharp.Client.Steam;

/// <summary>
/// Stock's UTIL_GetFilteredPlayerName( iPlayerIndex, pszName ) (util_shared.cpp,
/// client): Steam's name filter in the name context, with no source SteamID
/// (stock's index lookup is gone: an invalid CSteamID) into a
/// MAX_PLAYER_NAME_LENGTH buffer; with no SteamUtils the name as it came.
/// </summary>
public sealed class SteamPlayerNameFilter(ISteamClientApi steam) : IPlayerNameFilter
{
    /// <summary>k_ETextFilteringContextName.</summary>
    public const int NameContext = 3;

    /// <summary>MAX_PLAYER_NAME_LENGTH (public/const.h).</summary>
    public const int MaxPlayerNameLength = 32;

    public string Filter(string name)
        => steam.FilterText(NameContext, 0, name, MaxPlayerNameLength) ?? name;
}
