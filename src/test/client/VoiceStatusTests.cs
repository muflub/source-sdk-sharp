using SourceSharp.Abi;
using SourceSharp.Client.Voice;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The voice manager as stock's CVoiceStatus ran (voice_status.cpp): who is
/// talking, the server's masks, the mutes, and the lines it sends the server
/// (VModEnable, vban) and when.
/// </summary>
public class VoiceStatusTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Engine : IVoiceEngine
    {
        public readonly Dictionary<int, string> Guids = new() { [1] = "STEAM_1:0:1", [2] = "STEAM_1:0:2", [33] = "STEAM_1:0:33" };
        public readonly List<(string Line, bool Reliable)> Sent = [];

        public string? GuidOf(int client) => Guids.GetValueOrDefault(client);

        public void ServerCommand(string line, bool reliable) => Sent.Add((line, reliable));

        public int Enabled;

        public void EnableVoice() => Enabled++;
    }

    private sealed class File : IVoiceBanFile
    {
        public byte[]? Bytes;

        public byte[]? Read() => Bytes;

        public void Write(byte[] file) => Bytes = file;
    }

    private static (VoiceStatus Voice, Engine Engine, Clock Clock, File File, VoiceModEnableConVar Mod) Make(byte[]? banFile = null)
    {
        var engine = new Engine();
        var clock = new Clock();
        var file = new File { Bytes = banFile };
        var mod = new VoiceModEnableConVar();
        mod.NotifyChanged("1", "0");
        var v = new VoiceStatus(engine, clock, new RecordingLog(), file, mod) { LevelInitialized = true };
        return (v, engine, clock, file, mod);
    }

    [Fact]
    public void A_speaker_is_talking_until_they_stop_and_minus_one_and_two_are_this_client_and_the_server()
    {
        var (v, _, _, _, _) = Make();
        v.Status(3, true);
        Assert.True(v.IsSpeaking(3));
        v.Status(3, false);
        Assert.False(v.IsSpeaking(3));
        v.Status(-1, true);
        v.Status(-2, true);
        Assert.Equal((true, true), (v.LocalTalking, v.ServerAcked));
        v.Status(65, true);                     // past VOICE_MAX_PLAYERS: ignored
        Assert.False(v.IsSpeaking(65));
    }

    [Fact]
    public void Talking_turns_voice_modenable_on()
    {
        var (v, e, _, _, _) = Make();
        v.Status(-1, false);
        Assert.Equal(0, e.Enabled);
        v.Status(-1, true);
        Assert.Equal(1, e.Enabled);
    }

    [Fact]
    public void The_servers_mask_says_who_is_audible()
    {
        var (v, _, _, _, _) = Make();
        v.ServerMasks(0b101, 0, 1, 0, true);
        Assert.Equal((true, false, true, true), (v.IsAudible(1), v.IsAudible(2), v.IsAudible(3), v.IsAudible(33)));
    }

    [Fact]
    public void Once_a_second_the_server_hears_voice_modenable_when_it_differs_and_the_mutes_when_they_differ()
    {
        var (v, e, clock, _, mod) = Make();
        v.Frame();                              // 10 s since 0: due
        Assert.Equal([("VModEnable 1", true)], e.Sent);      // never told: -1 differs; no mutes, none differ
        e.Sent.Clear();
        clock.Now = 10.5f;
        mod.NotifyChanged("0", "1");            // voice turned off
        v.Frame();
        Assert.Empty(e.Sent);                   // not a second yet
        clock.Now = 11.5f;
        v.Frame();
        Assert.Equal([("VModEnable 0", true)], e.Sent);
        e.Sent.Clear();
        clock.Now = 13f;
        v.ServerMasks(0, 0b10, 0, 0, false);    // the server thinks player 2 is muted, this client does not
        v.Frame();
        Assert.Equal([("vban 0 0", false)], e.Sent);
    }

    [Fact]
    public void A_new_level_whose_clock_starts_again_still_gets_its_once_a_second_update()
    {
        var (v, e, clock, _, mod) = Make();
        clock.Now = 500f;
        v.Frame();                              // the last update at 500 s on the old level
        e.Sent.Clear();
        v.LevelInitialized = false;
        v.LevelInitialized = true;              // a new level: its clock starts near 0
        clock.Now = 3f;
        mod.NotifyChanged("0", "1");
        v.Frame();
        Assert.Equal([("VModEnable 0", true)], e.Sent);
    }

    [Fact]
    public void Asked_by_the_server_everything_is_sent()
    {
        var (v, e, _, _, _) = Make();
        v.ServerMasks(0, 0, 0, 0, true);
        v.ServerRequestedState();
        Assert.Equal([("VModEnable 1", true), ("vban 0 0", false)], e.Sent);
    }

    [Fact]
    public void Before_the_level_nothing_is_sent()
    {
        var (v, e, _, _, _) = Make();
        v.LevelInitialized = false;
        v.ServerRequestedState();
        Assert.Empty(e.Sent);
    }

    [Fact]
    public void A_mute_toggles_is_written_and_reaches_the_server_in_its_slot()
    {
        var (v, e, _, f, _) = Make();
        v.ServerMasks(0, 0, 0, 0, true);
        v.ToggleBlocked(2);
        Assert.True(v.IsBlocked(2));
        Assert.Contains(("vban 2 0", false), e.Sent);       // player 2 is bit 1 of the first mask
        v.ToggleBlocked(33);
        Assert.Contains(("vban 2 1", false), e.Sent);       // player 33 is bit 0 of the second
        var reread = new VoiceBanList();
        reread.Load(f.Bytes);
        Assert.True(reread.IsBanned(VoiceBanList.IdOf("STEAM_1:0:2")));
        v.ToggleBlocked(2);
        Assert.False(v.IsBlocked(2));
    }

    [Fact]
    public void The_ban_file_is_read_at_start()
    {
        var bans = new VoiceBanList();
        bans.SetBanned(VoiceBanList.IdOf("STEAM_1:0:1"), true);
        var (v, _, _, _, _) = Make(bans.Save());
        Assert.True(v.IsBlocked(1));
        Assert.False(v.IsBlocked(2));
    }
}
