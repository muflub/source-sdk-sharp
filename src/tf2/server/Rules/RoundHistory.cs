//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Server.Rules;

/// <summary>
/// The mini-rounds a map has played (<c>CTeamplayRoundBasedRules</c>'s
/// <c>m_iszPreviousRounds</c>, <c>m_iszRoundToPlayNext</c>,
/// <c>m_iszFirstRoundPlayed</c>): what the control-point master picks the
/// next round by. Only the last two played are kept, newest first.
/// </summary>
public sealed class RoundHistory
{
    private readonly List<string> _previous = new();

    /// <summary>Stock <c>GetRoundToPlayNext</c>/<c>SetRoundToPlayNext</c>: a mini-round's name, or "".</summary>
    public string RoundToPlayNext { get; set; } = string.Empty;

    /// <summary>Stock <c>GetFirstRoundPlayed</c>/<c>SetFirstRoundPlayed</c>.</summary>
    public string FirstRoundPlayed { get; set; } = string.Empty;

    /// <summary>Stock <c>GetLastPlayedRound</c>: the newest, or "".</summary>
    public string LastPlayedRound => _previous.Count > 0 ? _previous[0] : string.Empty;

    /// <summary>Stock <c>AddPlayedRound</c>: to the head, and only the last two are stored.</summary>
    public void AddPlayedRound(string round)
    {
        if (round.Length == 0)
            return;

        _previous.Insert(0, round);
        while (_previous.Count > 2)
            _previous.RemoveAt(_previous.Count - 1);
    }

    /// <summary>Stock <c>IsPreviouslyPlayedRound</c>.</summary>
    public bool IsPreviouslyPlayedRound(string round) => _previous.Contains(round);

    /// <summary>Stock <c>m_iszPreviousRounds.RemoveAll()</c> (a full map reset).</summary>
    public void ClearPlayed() => _previous.Clear();

    /// <summary>The machine's reset: nothing played, nothing chosen.</summary>
    public void Reset()
    {
        _previous.Clear();
        RoundToPlayNext = string.Empty;
        FirstRoundPlayed = string.Empty;
    }
}
