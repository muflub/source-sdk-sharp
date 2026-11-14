namespace SourceSharp.Ai;

/// <summary>
/// Friend or foe (plan_bots_teams_tf2 B.2): two actors are friends when both are on
/// the same game team, numbered 2 and up. Spectator (1) and unassigned (0) make no
/// friends, so a deathmatch, whose binding reports team 0 for everyone, has none.
/// The one place the core names a team number; the binding says which numbers are
/// teams. Read from the current row each decide, never remembered of a person (B.9).
/// </summary>
public static class TeamRule
{
    /// <summary>The lowest team number that is a playing team.</summary>
    public const int FirstGameTeam = 2;

    public static bool Friends(int a, int b) => a >= FirstGameTeam && a == b;

    /// <summary>The highest team number a mode may play (four teams: 2, 3, 4 and 5; user 2026-11-08 "also support 2/3/4 teams").</summary>
    public const int LastGameTeam = 5;

    /// <summary>The most teams a mode plays.</summary>
    public const int MostTeams = LastGameTeam - FirstGameTeam + 1;

    /// <summary>Whether <paramref name="team"/> is a playing team at all (a mode with teams has put someone on it).</summary>
    public static bool IsGameTeam(int team) => team >= FirstGameTeam;
}
