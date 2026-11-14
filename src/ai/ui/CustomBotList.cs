using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Ai.Ui;

/// <summary>
/// One custom bot's row on a game's bots page (HL2MP's, TF2's): its name (or
/// that the server names it, in the game's words), its style's two skills, its
/// spend as a number and a meter against the pool, and Edit (the bot editor)
/// and Remove. Narrow, the buttons go under the name.
/// </summary>
public sealed class CustomBotList(IBotSetupSource source, IBotSetupActions actions) : UiPanel<int>
{
    public const float RowDu = 64;

    /// <summary>Narrower than this, a row's name, meter and two buttons stop fitting: the buttons go under the name.</summary>
    public const float NarrowDu = 680;

    public const float NarrowRowDu = 112;

    public override void Describe(ref UiFrame ui, in int i)
    {
        CustomBotRow row = source.Custom(i);
        bool narrow = ui.DesignWidth < NarrowDu;
        using (ui.Column(Space.S, Align.Stretch).Size(Size.Fill, Size.Fill).Pad(Space.M, Space.S))
        {
            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
            {
                using (ui.Column(Space.None, Align.Start).Width(Size.Fill))
                {
                    if (row.Name.Length > 0)
                        ui.Data(row.Name, Text.Body).Elide();
                    else
                        ui.Text("#Ai_CreateServer_Unnamed", Text.Body, Tone.TextMuted).Elide();
                    using (ui.Row(Space.XS, Align.Centre))
                    {
                        ui.Label(row.StyleFirstToken);
                        ui.Label(row.StyleSecondToken);
                    }
                }

                ui.Text($"{row.Spent} / {row.Pool}", Text.Body).Width(Size.Du(64)).TextAlign(Align.End);
                ui.Meter(row.Pool > 0 ? row.Spent / (float)row.Pool : 0f, Tone.Accent, Size.Du(80));
                if (!narrow)
                    Buttons(ref ui, i);
            }

            if (narrow)
            {
                using (ui.Row(Space.M, Align.Centre).Width(Size.Fill))
                {
                    ui.Flex();
                    Buttons(ref ui, i);
                }
            }
        }
    }

    private void Buttons(ref UiFrame ui, int i)
    {
        if (ui.Button("edit", "#Ai_CreateServer_Edit").Clicked)
            actions.EditCustom(i);
        if (ui.Button("remove", "#Ai_CreateServer_Remove").Clicked)
            actions.RemoveCustom(i);
    }
}
