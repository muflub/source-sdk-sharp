namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>Which tone a game team is drawn in: a pure function of the team number.</summary>
public static class UiTeams
{
    /// <summary>HL2DM's team numbers: Combine (2) is TeamA, Rebels (3) TeamB.</summary>
    public const int Combine = 2, Rebels = 3;

    /// <summary>
    /// A team number's tone: TeamA for 2, TeamB for 3, and <paramref name="none"/>
    /// for no team (0 unassigned, 1 spectators, or deathmatch with no teams).
    /// </summary>
    public static Tone ToneOf(int team, Tone none = Tone.Line) => team switch
    {
        Combine => Tone.TeamA,
        Rebels => Tone.TeamB,
        _ => none,
    };
}
