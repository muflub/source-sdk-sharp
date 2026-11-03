//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Client.Player;
using TF2.Shared.Ctf;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The notification panel's game half (stock's
/// <c>CHudNotificationPanel</c>, tf_hud_notification_panel.cpp): a
/// <see cref="TFHudNotify"/> arrival starts the show — its type byte and the
/// fade time at <c>tf_hud_notification_duration</c> ahead of now (:109,
/// :134). A later arrival replaces the one showing (stock reloads its
/// control settings per message). Stock's minmode arm and the show-count
/// convars (:112-127) are not ported — our HUD has no minmode and no
/// kart/ghost notices.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFNotifications), typeof(ITFHudNotifyHook)])]
public sealed class TFHudNotifications(IGameClock clock, TFHudNotificationDurationConVar duration,
                                       IEngineHud screen, TFClientPlayerResources resources)
    : ITFNotifications, ITFHudNotifyHook
{
    private byte _type;
    private float _fades;

    public void OnHudNotify(TFHudNotify notify)
    {
        _type = notify.Type;
        _fades = clock.Now + duration.Value;
    }

    public bool Present => clock.Now < _fades;

    public byte Type => _type;

    public float SecondsLeft => MathF.Max(0f, _fades - clock.Now);

    public int LocalTeam
        => screen.LocalPlayerIndex is > 0 and int local && local < TFPlayerResourceNetState.Slots
           ? resources.Live?.State.Team[local] ?? 0
           : 0;

    public string Token => TFNotificationTokens.Of(Type, LocalTeam);
}

/// <summary>GetNotificationByType's table (tf_hud_notification_panel.cpp:342-434):
/// each type names a <c>resource/UI/notifications/notify_*.res</c> whose team
/// variant the local side picks (<c>bOnBlueTeam</c>, :331-338 — no local
/// player reads red); a type with no res (the prebuilt ones we do not carry)
/// draws nothing. The res files are not in the fork, so the copy is this
/// port's tokens (tf2_english.txt) named after the res basenames.</summary>
public static class TFNotificationTokens
{
    /// <summary>bOnBlueTeam's arm: the blue variant on blue, the red one otherwise.</summary>
    private static string PerTeam(int localTeam, string blue, string red) => localTeam == Blue ? blue : red;

    /// <summary>TF_TEAM_BLUE.</summary>
    private const int Blue = 3;

    public static string Of(byte type, int localTeam) => type switch
    {
        (byte)TFHudNotifyType.YourFlagTaken => PerTeam(localTeam, "#TF2_Notify_YourFlagTaken_Blue", "#TF2_Notify_YourFlagTaken_Red"),
        (byte)TFHudNotifyType.YourFlagDropped => PerTeam(localTeam, "#TF2_Notify_YourFlagDropped_Blue", "#TF2_Notify_YourFlagDropped_Red"),
        (byte)TFHudNotifyType.YourFlagReturned => PerTeam(localTeam, "#TF2_Notify_YourFlagReturned_Blue", "#TF2_Notify_YourFlagReturned_Red"),
        (byte)TFHudNotifyType.YourFlagCaptured => PerTeam(localTeam, "#TF2_Notify_YourFlagCaptured_Blue", "#TF2_Notify_YourFlagCaptured_Red"),
        (byte)TFHudNotifyType.EnemyFlagTaken => PerTeam(localTeam, "#TF2_Notify_EnemyFlagTaken_Blue", "#TF2_Notify_EnemyFlagTaken_Red"),
        (byte)TFHudNotifyType.EnemyFlagDropped => PerTeam(localTeam, "#TF2_Notify_EnemyFlagDropped_Blue", "#TF2_Notify_EnemyFlagDropped_Red"),
        (byte)TFHudNotifyType.EnemyFlagReturned => PerTeam(localTeam, "#TF2_Notify_EnemyFlagReturned_Blue", "#TF2_Notify_EnemyFlagReturned_Red"),
        (byte)TFHudNotifyType.EnemyFlagCaptured => PerTeam(localTeam, "#TF2_Notify_EnemyFlagCaptured_Blue", "#TF2_Notify_EnemyFlagCaptured_Red"),
        (byte)TFHudNotifyType.TouchingEnemyCtfCap => PerTeam(localTeam, "#TF2_Notify_TouchingEnemyCtfCap_Blue", "#TF2_Notify_TouchingEnemyCtfCap_Red"),
        _ => "",
    };
}
