using SourceSharp.Abi;

using TF2.Server.Entities.Ctf;
using TF2.Shared;
using TF2.Shared.Ctf;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The flag's per-team announcer: stock's <c>CTeamRecipientFilter</c> +
/// <c>SendHudNotification</c> pairs (entity_capture_flag.cpp's CTF arms,
/// func_capture_zone.cpp's throttle). Each fact seats a red player at slot 2
/// and a blue one at slot 3 and reads what each hears.
/// </summary>
public sealed class TFAnnouncerTests
{
    private const int RedSlot = 2;
    private const int BlueSlot = 3;
    private const int RedMate = 4;

    private static readonly EntityHandle Flag = new(0x0040_0064u);

    private readonly RecordingEngine _engine = new();
    private readonly Sink _sent = new();
    private readonly Dictionary<int, EntityHandle> _slots = new();
    private readonly Dictionary<EntityHandle, int> _teams = new();

    private sealed class Sink : IUserMessageSender
    {
        public readonly List<(string Key, byte[] Payload, int Player, bool Reliable)> Sent = [];

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
            => Sent.Add((key, payload.ToArray(), player, reliable));
    }

    public TFAnnouncerTests()
    {
        Seat(RedSlot, (int)TFTeam.Red);
        Seat(BlueSlot, (int)TFTeam.Blue);

        // The engine's reads the announcer asks: the team of a seated player,
        // and the slot of the player a handle names.
        _engine.ReturnsEachCall("Players.GetHandle", () =>
            _slots.TryGetValue((int)_engine.Calls[^1].Args[0]!, out EntityHandle h) ? h : EntityHandle.None);
        _engine.ReturnsEachCall("Players.GetTeam", () =>
            _teams.TryGetValue((EntityHandle)_engine.Calls[^1].Args[0]!, out int team) ? team : 0);
        _engine.ReturnsEachCall("Entities.GetIndex", () =>
            _slots.Where(kv => kv.Value == (EntityHandle)_engine.Calls[^1].Args[0]!)
                  .Select(kv => kv.Key).FirstOrDefault(0));
    }

    private void Seat(int slot, int team)
    {
        var handle = new EntityHandle((uint)slot | (1u << 16));
        _slots[slot] = handle;
        _teams[handle] = team;
    }

    private TFAnnouncer Announcer(bool waiting = false) => new(_engine, _engine, _engine, _sent, new FixedRules(waiting));

    /// <summary>The rules slice the notice gate reads; everything else is inert.</summary>
    private sealed class FixedRules(bool waiting) : ICtfRules
    {
        public bool OvertimeAllowedForCtf => true;
        public void SetOvertimeAllowedForCtf(bool allowed) { }
        public bool FlagsMayBeCapped => true;
        public bool InWaitingForPlayers => waiting;
        public bool InStalemate => false;
        public bool CanFlagBeCaptured(EntityHandle player) => false;
        public IEnumerable<ItemTeamFlag> Flags => [];
        public ItemTeamFlag? CarriedBy(EntityHandle player) => null;
        public bool CheckCapsPerRound() => false;
        public bool PointInRespawnFlagZone(Vector point) => false;
        public void Report(string kind, TFTeam team, int index) { }
        public int FlagCaptures(TFTeam team) => 0;
        public void RewardTeam(TFTeam team, bool asCapture, int score) { }
        public void SetFlagCaptures(TFTeam team, int captures) { }
    }

    private IReadOnlyList<(int Team, EntityHandle Entity, string Sound)> Sounds() =>
        _engine.AllOf("Sound.EmitSoundToTeam")
               .Select(c => ((int)c.Args[0]!, (EntityHandle)c.Args[1]!, (string)c.Args[2]!))
               .ToList();

    private IReadOnlyList<(int Player, byte Type)> Notices() =>
        _sent.Sent.Select(s => (s.Player, s.Payload[0])).ToList();

    // ---- pick-up (entity_capture_flag.cpp:1259-1281) ------------------------

    [Fact]
    public void A_pick_up_tells_each_team_its_own_line()
    {
        Announcer().Stolen(Flag, TFTeam.Red, _slots[RedSlot]);

        // The taker's team hears the "we have it" script; the team that lost
        // the flag hears the "they have ours" one.
        Assert.Equal([(2, Flag, TFCtfSounds.TeamStolen), (3, Flag, TFCtfSounds.EnemyStolen)], Sounds());
    }

    [Fact]
    public void The_pick_ups_notices_reach_each_team_and_leave_the_picker_out()
    {
        Seat(RedMate, (int)TFTeam.Red);

        Announcer().Stolen(Flag, TFTeam.Red, _slots[RedSlot]);

        // HUD_NOTIFY_YOUR_FLAG_TAKEN (0) to the victims; HUD_NOTIFY_ENEMY_FLAG_TAKEN (4)
        // to the taker's team, with the picker (slot 2) removed and the team's other
        // player (slot 4) kept — stock's RemoveRecipient names the picker and no one else.
        Assert.Equal([(BlueSlot, (byte)TFHudNotifyType.YourFlagTaken), (RedMate, (byte)TFHudNotifyType.EnemyFlagTaken)],
                     Notices().OrderBy(n => n.Player).ToList());
        Assert.All(_sent.Sent, s => Assert.True(s.Reliable));
    }

    // ---- capture (entity_capture_flag.cpp:1501-1549) ------------------------

    [Fact]
    public void A_capture_tells_each_team_its_own_line_and_notices_everyone_of_the_capping_team()
    {
        Announcer().Captured(Flag, TFTeam.Red, _slots[RedSlot]);

        Assert.Equal([(2, Flag, TFCtfSounds.TeamCaptured), (3, Flag, TFCtfSounds.EnemyCaptured)], Sounds());
        Assert.Equal([(RedSlot, (byte)TFHudNotifyType.EnemyFlagCaptured), (BlueSlot, (byte)TFHudNotifyType.YourFlagCaptured)],
                     Notices().OrderBy(n => n.Player).ToList());
    }

    // ---- drop (entity_capture_flag.cpp:1947-1968) ---------------------------

    [Fact]
    public void A_drop_tells_each_team_its_own_line_including_the_dropper()
    {
        Announcer().Dropped(Flag, TFTeam.Red, _slots[RedSlot]);

        Assert.Equal([(2, Flag, TFCtfSounds.TeamDropped), (3, Flag, TFCtfSounds.EnemyDropped)], Sounds());
        Assert.Equal([(RedSlot, (byte)TFHudNotifyType.EnemyFlagDropped), (BlueSlot, (byte)TFHudNotifyType.YourFlagDropped)],
                     Notices().OrderBy(n => n.Player).ToList());
    }

    // ---- return (entity_capture_flag.cpp:912-930) ---------------------------

    [Fact]
    public void A_return_tells_the_flags_team_their_flag_is_back_and_the_others_the_enemy_got_it()
    {
        // The inverted pair: the flag's OWN team hears the "enemy returned"
        // script with the YOUR_FLAG notice; the other team the "team returned"
        // script with the ENEMY_FLAG notice.
        Announcer().Returned(Flag, TFTeam.Red);

        Assert.Equal([(2, Flag, TFCtfSounds.EnemyReturned), (3, Flag, TFCtfSounds.TeamReturned)], Sounds());
        Assert.Equal([(RedSlot, (byte)TFHudNotifyType.YourFlagReturned), (BlueSlot, (byte)TFHudNotifyType.EnemyFlagReturned)],
                     Notices().OrderBy(n => n.Player).ToList());
    }

    // ---- the touching-enemy-cap notice (func_capture_zone.cpp:256-262) ------

    [Fact]
    public void The_touching_enemy_cap_notice_goes_to_the_one_player_alone_and_unreliably()
    {
        Announcer().TouchingEnemyCap(_slots[BlueSlot]);

        var notice = Assert.Single(Notices());
        Assert.Equal((BlueSlot, (byte)TFHudNotifyType.TouchingEnemyCtfCap), notice);
        // CSingleUserRecipientFilter resets m_bReliable (recipientfilter.cpp:54);
        // the team notices stay reliable.
        Assert.False(_sent.Sent[0].Reliable);
    }

    // ---- the notice gate (tf_gamerules.cpp:18656-18659) ---------------------

    [Fact]
    public void While_waiting_for_players_the_sound_plays_but_no_notice_is_sent()
    {
        Announcer(waiting: true).Stolen(Flag, TFTeam.Red, _slots[RedSlot]);

        Assert.Equal([(2, Flag, TFCtfSounds.TeamStolen), (3, Flag, TFCtfSounds.EnemyStolen)], Sounds());
        Assert.Empty(Notices());
    }

    [Fact]
    public void The_touching_enemy_cap_notice_is_also_skipped_while_waiting_for_players()
    {
        Announcer(waiting: true).TouchingEnemyCap(_slots[BlueSlot]);

        Assert.Empty(Notices());
    }

    [Fact]
    public void An_unseated_player_hears_no_notice()
    {
        _slots.Remove(BlueSlot);

        Announcer().Stolen(Flag, TFTeam.Red, _slots[RedSlot]);

        Assert.DoesNotContain(Notices(), n => n.Player == BlueSlot);
    }

    // ---- the attack/defend arms ---------------------------------------------

    [Fact]
    public void An_attack_defend_pick_up_tells_each_team_its_own_line_and_no_notice()
    {
        Announcer().AttackDefendStolen(Flag, TFTeam.Red, _slots[RedSlot]);

        Assert.Equal([(2, Flag, TFAttackDefendSounds.TeamStolen), (3, Flag, TFAttackDefendSounds.EnemyStolen)],
                     Sounds());
        Assert.Empty(Notices());
    }

    [Fact]
    public void An_attack_defend_capture_tells_each_team_its_line_and_stings_every_player_once()
    {
        Announcer().AttackDefendCaptured(Flag, TFTeam.Red, _slots[RedSlot]);

        // The per-team lines, then the broadcast sting carried by each team's
        // filter — between them every player hears it once.
        Assert.Equal([(2, Flag, TFAttackDefendSounds.TeamCaptured), (2, Flag, TFAttackDefendSounds.Captured),
                      (3, Flag, TFAttackDefendSounds.EnemyCaptured), (3, Flag, TFAttackDefendSounds.Captured)],
                     Sounds());
        Assert.Empty(Notices());
    }

    [Fact]
    public void An_attack_defend_drop_tells_each_team_its_own_line_and_no_notice()
    {
        Announcer().AttackDefendDropped(Flag, TFTeam.Red, _slots[RedSlot]);

        Assert.Equal([(2, Flag, TFAttackDefendSounds.TeamDropped), (3, Flag, TFAttackDefendSounds.EnemyDropped)],
                     Sounds());
        Assert.Empty(Notices());
    }

    [Fact]
    public void An_attack_defend_return_tells_the_flags_team_their_line_and_the_others_theirs()
    {
        // The inverted pair, as the CTF return.
        Announcer().AttackDefendReturned(Flag, TFTeam.Red);

        Assert.Equal([(2, Flag, TFAttackDefendSounds.EnemyReturned), (3, Flag, TFAttackDefendSounds.TeamReturned)],
                     Sounds());
        Assert.Empty(Notices());
    }
}
