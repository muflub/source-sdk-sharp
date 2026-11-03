//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Ctf;

/// <summary>
/// HudNotification_t (tf_shareddefs.h:1731-1744) as the flag's announcer
/// carries it: the flag events and the touching-enemy-cap notice. The client
/// maps the byte and the local player's team onto a
/// <c>resource/UI/notifications/notify_*.res</c> panel
/// (tf_hud_notification_panel.cpp:342-434), which is why the names read from
/// the listener's side of the play.
/// </summary>
public enum TFHudNotifyType : byte
{
    /// <summary>HUD_NOTIFY_YOUR_FLAG_TAKEN: the enemy has your flag.</summary>
    YourFlagTaken = 0,

    /// <summary>HUD_NOTIFY_YOUR_FLAG_DROPPED.</summary>
    YourFlagDropped = 1,

    /// <summary>HUD_NOTIFY_YOUR_FLAG_RETURNED.</summary>
    YourFlagReturned = 2,

    /// <summary>HUD_NOTIFY_YOUR_FLAG_CAPTURED.</summary>
    YourFlagCaptured = 3,

    /// <summary>HUD_NOTIFY_ENEMY_FLAG_TAKEN.</summary>
    EnemyFlagTaken = 4,

    /// <summary>HUD_NOTIFY_ENEMY_FLAG_DROPPED.</summary>
    EnemyFlagDropped = 5,

    /// <summary>HUD_NOTIFY_ENEMY_FLAG_RETURNED.</summary>
    EnemyFlagReturned = 6,

    /// <summary>HUD_NOTIFY_ENEMY_FLAG_CAPTURED.</summary>
    EnemyFlagCaptured = 7,

    /// <summary>HUD_NOTIFY_TOUCHING_ENEMY_CTF_CAP.</summary>
    TouchingEnemyCtfCap = 8,
}

/// <summary>HudNotify's payload (tf_gamerules.cpp:18656-18664): the type byte,
/// and whether the HUD shows it in minmode.</summary>
public partial struct TFHudNotify
{
    public partial byte Type { get; set; }

    public partial bool ForceShow { get; set; }
}

/// <summary>What the notification's arrival tells the HUD (stock's
/// CHudNotificationPanel::MsgFunc_HudNotify, tf_hud_notification_panel.cpp:96).</summary>
public interface ITFHudNotifyHook
{
    void OnHudNotify(TFHudNotify notify);
}

/// <summary>The server's per-team flag notices: a type byte aimed at one team.
/// Stock TF2's own "HudNotify" name (tf_gamerules.cpp:18650) — keyed under the
/// module because this build's stock-name set is HL2DM's
/// (UserMessageKey.StockNames).</summary>
[UserMessage("HudNotify")]
public sealed class TFHudNotifyMessage : IUserMessage<TFHudNotify>
{
    private readonly IHooks<ITFHudNotifyHook>? _hooks;

    /// <summary>A handler whose notices go nowhere: the no-container fallback's (and the server's).</summary>
    public TFHudNotifyMessage()
    {
    }

    /// <summary>The client's container's: the hook chain the notice dispatches to.</summary>
    [Inject]
    public TFHudNotifyMessage(IHooks<ITFHudNotifyHook>? hooks) => _hooks = hooks;

    public void OnReceived(TFHudNotify message) =>
        _hooks?.Each(nameof(ITFHudNotifyHook.OnHudNotify), hook => hook.OnHudNotify(message));
}
