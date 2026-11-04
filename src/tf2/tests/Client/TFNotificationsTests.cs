extern alias client;

using Xunit;

using ClientMod = client::TF2.Client;
using ClientAbi = client::SourceSharp.Abi;
using ClientPlayer = client::TF2.Client.Player;
using ClientUi = client::TF2.Client.Ui;

// The shared interop compiles per side; the notify struct and hook are the
// client's copy.
using ClientCtf = client::TF2.Shared.Ctf;

using Engine = client::SourceSharp.Abi.RecordingEngine;

namespace TF2.Tests.Client;

/// <summary>
/// The notification panel's game half (tf_hud_notification_panel.cpp): a
/// HudNotify arrival starts the show for tf_hud_notification_duration (3 s,
/// :29), a later arrival replaces the one showing, the local team comes from
/// the player resource, and GetNotificationByType's per-team token table
/// (:342-434) — no local player reads red (bOnBlueTeam, :331-338).
/// </summary>
public sealed class TFNotificationsTests
{
    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;

    /// <summary>The local player index the rig's engine answers.</summary>
    private const int Local = 5;

    /// <summary>A convar backend that stores what it is told, so a SetValue round-trips.</summary>
    private sealed class Backend : ClientAbi.IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;

        public string Get(int id) => _values[id];

        public string? ValueOf(string name)
            => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    private sealed class NotifRig
    {
        public readonly Engine Engine = Engine.Default();
        public readonly ClientPlayer.TFClientPlayerResources Resources = new();
        public readonly ClientUi.TFHudNotifications Notifications;

        public NotifRig(float now = 10f, int? local = Local)
        {
            Engine.Returns("Clock.Now", now);
            if (local is int slot)
                Engine.Returns("Hud.LocalPlayerIndex", slot);

            var convars = new ClientAbi.ConVarRegistry(new Backend());
            convars.RegisterTypes([typeof(ClientMod.TFHudNotificationDurationConVar)]);
            var duration = convars.Declared<ClientMod.TFHudNotificationDurationConVar>();

            var player = new ClientPlayer.ClientTFPlayerResource(Engine, Resources);
            player.State.BindLocal();
            player.State.Team[Local] = Blue;
            Resources.Live = player;

            Notifications = new ClientUi.TFHudNotifications(Engine, duration, Engine, Resources);
        }

        /// <summary>Advances the clock (the engine's only clock is stubbed).</summary>
        public void At(float now) => Engine.Returns("Clock.Now", now);
    }

    private static void Arrive(ClientUi.TFHudNotifications notif, ClientCtf.TFHudNotifyType type)
        => notif.OnHudNotify(new ClientCtf.TFHudNotify { Type = (byte)type });

    [Fact]
    public void Nothing_shows_before_the_first_arrival()
    {
        var rig = new NotifRig(now: 10f);
        Assert.False(rig.Notifications.Present);
        Assert.Equal(0f, rig.Notifications.SecondsLeft, 3);
    }

    [Fact]
    public void An_arrival_shows_for_the_duration_and_names_its_type()
    {
        var rig = new NotifRig(now: 10f);
        Arrive(rig.Notifications, ClientCtf.TFHudNotifyType.EnemyFlagTaken);

        Assert.True(rig.Notifications.Present);
        Assert.Equal((byte)ClientCtf.TFHudNotifyType.EnemyFlagTaken, rig.Notifications.Type);
        Assert.Equal(3f, rig.Notifications.SecondsLeft, 3);
    }

    [Fact]
    public void The_banner_is_gone_when_the_duration_passes()
    {
        var rig = new NotifRig(now: 10f);
        Arrive(rig.Notifications, ClientCtf.TFHudNotifyType.YourFlagTaken);

        rig.At(12.9f);
        Assert.True(rig.Notifications.Present);

        rig.At(13f);
        Assert.False(rig.Notifications.Present);
        Assert.Equal(0f, rig.Notifications.SecondsLeft, 3);
    }

    [Fact]
    public void A_later_arrival_replaces_the_one_showing()
    {
        var rig = new NotifRig(now: 10f);
        Arrive(rig.Notifications, ClientCtf.TFHudNotifyType.YourFlagTaken);
        rig.At(11f);
        Arrive(rig.Notifications, ClientCtf.TFHudNotifyType.TouchingEnemyCtfCap);

        Assert.Equal((byte)ClientCtf.TFHudNotifyType.TouchingEnemyCtfCap, rig.Notifications.Type);
        Assert.Equal(3f, rig.Notifications.SecondsLeft, 3);   // the window restarts

        rig.At(13f);
        Assert.True(rig.Notifications.Present);   // the first arrival's window would be over
    }

    [Fact]
    public void The_local_team_comes_from_the_player_resource()
    {
        var rig = new NotifRig();
        Assert.Equal(Blue, rig.Notifications.LocalTeam);

        rig.Resources.Live!.State.Team[Local] = Red;
        Assert.Equal(Red, rig.Notifications.LocalTeam);
    }

    [Fact]
    public void No_local_player_reads_no_team()
    {
        var rig = new NotifRig(local: 0);
        Assert.Equal(0, rig.Notifications.LocalTeam);
    }

    [Fact]
    public void Each_type_names_its_token_and_blue_local_takes_the_blue_variant()
    {
        var tokens = new (ClientCtf.TFHudNotifyType Type, string Token)[]
        {
            (ClientCtf.TFHudNotifyType.YourFlagTaken, "#TF2_Notify_YourFlagTaken_Blue"),
            (ClientCtf.TFHudNotifyType.YourFlagDropped, "#TF2_Notify_YourFlagDropped_Blue"),
            (ClientCtf.TFHudNotifyType.YourFlagReturned, "#TF2_Notify_YourFlagReturned_Blue"),
            (ClientCtf.TFHudNotifyType.YourFlagCaptured, "#TF2_Notify_YourFlagCaptured_Blue"),
            (ClientCtf.TFHudNotifyType.EnemyFlagTaken, "#TF2_Notify_EnemyFlagTaken_Blue"),
            (ClientCtf.TFHudNotifyType.EnemyFlagDropped, "#TF2_Notify_EnemyFlagDropped_Blue"),
            (ClientCtf.TFHudNotifyType.EnemyFlagReturned, "#TF2_Notify_EnemyFlagReturned_Blue"),
            (ClientCtf.TFHudNotifyType.EnemyFlagCaptured, "#TF2_Notify_EnemyFlagCaptured_Blue"),
            (ClientCtf.TFHudNotifyType.TouchingEnemyCtfCap, "#TF2_Notify_TouchingEnemyCtfCap_Blue"),
        };
        foreach (var (type, token) in tokens)
            Assert.Equal(token, ClientUi.TFNotificationTokens.Of((byte)type, Blue));
    }

    [Fact]
    public void A_red_local_takes_the_red_variant_of_every_type()
    {
        var tokens = new (ClientCtf.TFHudNotifyType Type, string Token)[]
        {
            (ClientCtf.TFHudNotifyType.YourFlagTaken, "#TF2_Notify_YourFlagTaken_Red"),
            (ClientCtf.TFHudNotifyType.YourFlagDropped, "#TF2_Notify_YourFlagDropped_Red"),
            (ClientCtf.TFHudNotifyType.YourFlagReturned, "#TF2_Notify_YourFlagReturned_Red"),
            (ClientCtf.TFHudNotifyType.YourFlagCaptured, "#TF2_Notify_YourFlagCaptured_Red"),
            (ClientCtf.TFHudNotifyType.EnemyFlagTaken, "#TF2_Notify_EnemyFlagTaken_Red"),
            (ClientCtf.TFHudNotifyType.EnemyFlagDropped, "#TF2_Notify_EnemyFlagDropped_Red"),
            (ClientCtf.TFHudNotifyType.EnemyFlagReturned, "#TF2_Notify_EnemyFlagReturned_Red"),
            (ClientCtf.TFHudNotifyType.EnemyFlagCaptured, "#TF2_Notify_EnemyFlagCaptured_Red"),
            (ClientCtf.TFHudNotifyType.TouchingEnemyCtfCap, "#TF2_Notify_TouchingEnemyCtfCap_Red"),
        };
        foreach (var (type, token) in tokens)
            Assert.Equal(token, ClientUi.TFNotificationTokens.Of((byte)type, Red));
    }

    [Fact]
    public void An_unknown_type_names_nothing()
        => Assert.Equal("", ClientUi.TFNotificationTokens.Of(200, Blue));
}
