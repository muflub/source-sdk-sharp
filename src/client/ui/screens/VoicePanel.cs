using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// WHO IS SPEAKING (stock CHudVoiceStatus and CHudVoiceSelfStatus): bottom
/// right, above the ammo, one row a speaker in the order they started, the
/// voice icon, the skull when dead (stock's dead icon), the name, on their
/// team's colour; and the local player's own voice icon while they speak.
/// </summary>
public sealed class VoicePanel(IVoiceSource voice) : UiPanel
{
    /// <summary>Above the ammo panel: its height and the HUD's bottom gap.</summary>
    public const float BottomDu = 148;

    public override void Describe(ref UiFrame ui)
    {
        var v = voice;
        if (v.Count == 0 && !v.LocalSpeaking)
            return;
        using (ui.Column(Space.XS, Align.End).At(UiAnchor.BottomRight).Inset(Space.XXL, new UiLength(BottomDu, false)))
        {
            for (int i = 0; i < v.Count; i++)
            {
                var s = v.Speaker(i);
                using (ui.Row(Space.S, Align.Centre).Key(i).Fill(Tone.Window).Round(Radius.M).Pad(Space.M, Space.XS)
                         .Border(UiTeams.ToneOf(s.Team, Tone.Line), 2))
                {
                    ui.Icon("voice", Tone.Text, 20).Name("voice");
                    if (!s.Alive)
                        ui.Icon("skull", Tone.TextMuted, 20).Name("dead");
                    ui.Data(s.Name, Text.Body, UiTeams.ToneOf(s.Team, Tone.Text)).Elide().MaxWidth(240).Name("name");
                }
            }
            if (v.LocalSpeaking)
                using (ui.Box().Key(-1).Fill(Tone.Window).Round(Radius.M).Pad(Space.S))
                    ui.Icon("voice", Tone.Accent, 28).Name("self");
        }
    }
}
