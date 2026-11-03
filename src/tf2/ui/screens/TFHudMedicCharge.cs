//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>
/// The medic's charge meter, stock's <c>CHudMedicChargeMeter</c>
/// (tf_hud_mediccharge.cpp): the ÜberCharge bar and its percent
/// (<c>m_pChargeMeter</c>'s progress and the "charge" dialog variable,
/// OnTick :255-259), the beam's patient under it (m_hHealTarget through
/// g_PR), and the ready pulse — the meter full starts
/// <c>HudMedicCharged</c> (:261-268) and the release stops it (:269-277),
/// so the percent blinks while charged and holds steady while draining.
/// </summary>
public sealed class MedicChargePanel(ITFMedicCharge medic) : UiPanel
{
    public bool Present => medic.Present;

    public override void Describe(ref UiFrame ui)
    {
        if (!Present)
            return;

        // The percent blinks on the ready pulse (HudMedicCharged); the
        // release stops it (:269-277).
        float charge = medic.Charge;
        bool ready = charge >= 1f && !medic.Releasing;
        bool blink = ready && (int)(ui.Now / 0.5) % 2 == 1;
        var tone = blink ? Tone.Danger : Tone.Accent;

        using (ui.Column(Space.XS, Align.Start).Key("medic charge"))
        {
            using (ui.Row(Space.S, Align.Centre))
            {
                ui.Text("#TF2_Hud_UberCharge", Text.Caption, Tone.Text).HudSize();
                ui.Flex();
                ui.Number((int)(charge * 100), Text.Body, tone).HudSize().Key("percent");
            }

            // The meter: the level as the medigun states it, eased like the
            // health cross (ContinuousProgressBar's SetProgress, :257).
            ui.Meter(ui.Animate("charge", charge, Motion.Standard), tone, Size.Fill, 4).Key("charge");

            // The beam's patient (m_hHealTarget), when there is one.
            string patient = medic.HealTarget;
            if (patient.Length > 0)
                using (ui.Row(Space.XS, Align.Centre).Key("healing"))
                {
                    ui.Text("#TF2_Hud_Healing", Text.Caption, Tone.TextMuted).HudSize();
                    ui.Data(patient, Text.Caption).HudSize().Elide();
                }
        }
    }
}
