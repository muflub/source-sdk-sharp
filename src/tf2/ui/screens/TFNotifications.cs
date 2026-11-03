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
/// The notification banner (stock's <c>CHudNotificationPanel</c>,
/// tf_hud_notification_panel.cpp): one
/// <see cref="ITFNotifications.Token"/> on a translucent plate, top centre,
/// for <c>tf_hud_notification_duration</c> (3 s) — stock's own OnTick fade
/// is commented out (:306-324), so the banner shows whole until its time
/// passes and the next arrival replaces it (stock reloads its control
/// settings per message, :130-131). Stock's per-type art
/// (<c>Notification_Icon</c>) rides the vpk's own res files, which are not
/// in the fork: the banner is text on the themed plate (the art fallback
/// the other HUD layers keep), at stock's render group "mid" position —
/// top centre, the plate standing in for base_notification.res.
/// </summary>
public sealed class TFNotifications(ITFNotifications notifications) : UiScreen
{
    public override string Name => "tfnotifications";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    public override void Describe(ref UiFrame ui)
    {
        if (!notifications.Present || notifications.Token.Length == 0)
            return;

        using (ui.Panel().At(UiAnchor.Top).Inset(Space.None, Space.XL).Key("notify"))
            ui.Text(notifications.Token, Text.Body, Tone.Text);
    }
}
