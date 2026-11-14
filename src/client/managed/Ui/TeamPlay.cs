using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// Whether the game is team play, as the client reads it: from the roster. Stock's
/// knob, <c>mp_teamplay</c>, is <c>FCVAR_NOTIFY</c> and not replicated
/// (game.cpp:33; the client asks <c>HL2MPRules()-&gt;IsTeamplay()</c>, which the
/// game rules proxy carries and this build has no networked copy of), so the
/// client cannot ask the server's mode. The roster answers the same question:
/// under the server's team modes every player in play is on a playing team (2 or
/// 3) and under deathmatch every player is on 0, so one connected player on a
/// playing team is team play. Spectators (1) and the unassigned (0) say nothing.
/// </summary>
public static class TeamPlay
{
    /// <summary>The first playing team's number (Combine); Rebels is the next.</summary>
    public const int FirstPlayingTeam = 2;

    /// <summary>
    /// Whether the game is team play: the server's playing mode says, when the
    /// client knows it (<c>sv_mode_active</c> and the catalog: team deathmatch
    /// is team play before anyone has joined a team); else the roster.
    /// </summary>
    public static bool Of(IActiveMode? catalog, PlayerResourceNetState state)
        => catalog?.ActiveMode is { } mode ? mode.Teamplay : FromRoster(state);

    /// <summary>Whether any connected player of <paramref name="state"/> is on a playing team.</summary>
    public static bool FromRoster(PlayerResourceNetState state)
    {
        for (int slot = 1; slot < PlayerResourceNetState.Slots; slot++)
            if (state.HasFlag(slot, PlayerResourceNetState.FlagConnected) && state.Team[slot] >= FirstPlayingTeam)
                return true;
        return false;
    }
}
