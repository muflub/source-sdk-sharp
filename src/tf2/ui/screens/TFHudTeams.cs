using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>
/// The TF teams' tones: team 2 is TeamA and team 3 TeamB — the theme's own
/// pairing (<see cref="UiTeams"/>: TeamA is team 2's colour, whatever the
/// scheme makes it; the TF2 theme sets TeamA to the red because TF_TEAM_RED
/// is 2). Team numbers are stock's (tf_shareddefs.h: TF_TEAM_RED 2,
/// TF_TEAM_BLUE 3), spelled here because the UI assembly holds no
/// TF2.Shared reference.
/// </summary>
public static class TFHudTeams
{
    public static Tone ToneOf(int team) => team switch
    {
        Red => Tone.TeamA,
        Blue => Tone.TeamB,
        _ => Tone.TextMuted,
    };

    private const int Red = 2;
    private const int Blue = 3;
}
