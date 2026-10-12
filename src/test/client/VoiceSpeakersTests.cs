using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Who is speaking as stock's CHudVoiceStatus keeps it (hud_voicestatus.cpp,
/// over CVoiceStatus::UpdateSpeakerStatus): a speaker added at the end when
/// they start, gone when they stop (HL2MP's fade_out_time 0), -1 the local
/// player's own icon, -2 ignored.
/// </summary>
public class VoiceSpeakersTests
{
    private sealed class Players : IVoicePlayers
    {
        public bool TryFind(int client, out string name, out int team, out bool alive)
        {
            (name, team, alive) = ($"p{client}", client % 2 == 0 ? 2 : 3, client != 3);
            return true;
        }
    }

    [Fact]
    public void Speakers_are_listed_in_the_order_they_start_and_leave_when_they_stop()
    {
        var v = new VoiceSpeakers(new Players());
        v.Status(4, true);
        v.Status(2, true);
        v.Status(3, true);
        v.Status(4, true);                       // already speaking: not added twice
        Assert.Equal(["p4", "p2", "p3"], Enumerable.Range(0, v.Count).Select(i => v.Speaker(i).Name));
        v.Status(2, false);
        Assert.Equal(["p4", "p3"], Enumerable.Range(0, v.Count).Select(i => v.Speaker(i).Name));
        Assert.False(v.Speaker(1).Alive);
    }

    [Fact]
    public void Minus_one_is_the_local_player_and_minus_two_is_ignored()
    {
        var v = new VoiceSpeakers(new Players());
        v.Status(-1, true);
        v.Status(-2, true);
        Assert.Equal((true, 0), (v.LocalSpeaking, v.Count));
        v.Status(-1, false);
        Assert.False(v.LocalSpeaking);
    }
}
