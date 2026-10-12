extern alias client;
using HudMessageArgs = client::SourceSharp.Shared.HudMessageArgs;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two text entities whose client half had to be written as part of the
/// port, and the decisions in that half.
///
/// <para>
/// Both send a user message nothing in this build was reading: stock's readers
/// are <c>message.cpp</c> and <c>hud_hintdisplay.cpp</c>, and both left the
/// client build when the HUD moved to C#. So what is checked here is the far
/// side — the ring a titles.txt message lands in, and the token substitution
/// that is the entire reason a hint is a different thing from a message.
/// </para>
/// </summary>
public class MessageEntityTests
{

    [Fact]
    public void TitlesAreLocalised()
    {
        // Not optional, and this is the entry that proves it: HL2's own
        // titles.txt is almost entirely tokens, and CHAPTER1_TITLE's message is
        // the literal string "#HL2_Chapter1_Title". Drawn without the lookup,
        // that token is what a player reads.
        string Localise(string token)
            => token == "#HL2_Chapter1_Title" ? "POINT INSERTION" : token;

        Assert.True(HudMessages.LocaliseTitle("#HL2_Chapter1_Title", Localise) == "POINT INSERTION", "a title's text goes through the localisation tables");

        // A titles.txt entry ends with the newline before its closing brace, and
        // a token with that stuck to it matches nothing.
        Assert.True(HudMessages.LocaliseTitle("#HL2_Chapter1_Title\n", Localise) == "POINT INSERTION", "the trailing newline is stripped before the lookup");

        Assert.True(HudMessages.LocaliseTitle("just words", Localise) == "just words", "plain text with no entry is drawn as it is");
    }

    [Fact]
    public void MessageAttenuationsMatchStock()
    {
        // env_message spells attenuation as one of four sizes and stock turns
        // each into a soundlevel through ATTN_TO_SNDLVL, which is 50 + 20/a.
        // The default case is the one worth pinning: stock stores SNDLVL_IDLE —
        // a soundLEVEL of 60 — in a field the next line reads as an ATTENUATION,
        // so "small radius" comes out as 50dB rather than as 60.
        Assert.True(Reflect(1) == 66, "medium radius is 66dB");
        Assert.True(Reflect(2) == 75, "large radius is 75dB");
        Assert.True(Reflect(3) == 0, "everywhere has no level");
        Assert.True(Reflect(0) == 50, "small radius is 50dB, which is stock's own mix-up");
    }

    /// <summary>Reaches the private mapping without giving it a public surface.</summary>
    private static int Reflect(int attenuation)
    {
        var method = typeof(EnvMessage).GetMethod("SoundLevelFor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (int)method!.Invoke(null, [attenuation])!;
    }

    [Fact]
    public void TitlesTakeTheirOwnSlots()
    {
        var hud = new HudMessages(new RecordingLog(), Fake(), Fake());

        // A game_text names its channel; a title has none and takes the next
        // free space. The two must not be able to evict one another.
        hud.Show(new HudMessageArgs(0, 0.5f, 0.5f, Color32.White, Color32.White,
                                    0, 0f, 0f, 10f, 0f, "from a game_text"), 0f);

        hud.ShowTitle("GAMEOVER", "from a title", "", 0.5f, 0.7f, Color32.White,
                      0f, 0f, 10f, 0f);

        Assert.True(hud.ActiveCount(1f) == 1, "the game_text is still up");
        Assert.True(hud.ActiveTitleCount(1f) == 1, "and the title is up beside it");

        // And they expire on their own timings.
        Assert.True(hud.ActiveCount(20f) == 0 && hud.ActiveTitleCount(20f) == 0, "both are gone once the hold has passed");
    }

    [Fact]
    public void ATitleCanClearAnother()
    {
        var hud = new HudMessages(new RecordingLog(), Fake(), Fake());

        hud.ShowTitle("HINT_USE", "press use", "", 0.5f, 0.7f, Color32.White,
                      0f, 0f, 60f, 0f);

        Assert.True(hud.ActiveTitleCount(1f) == 1, "the hint is up");

        // titles.txt's own mechanism: an entry names the one it replaces, so a
        // "well done" takes the "press use" down rather than overlapping it.
        hud.ShowTitle("HINT_DONE", "well done", "HINT_USE", 0.5f, 0.7f, Color32.White,
                      0f, 0f, 60f, 1f);

        Assert.True(hud.ActiveTitleCount(2f) == 1, "and the one that cleared it is the only one left");
    }

    [Fact]
    public void AFullRingOverwritesTheOldest()
    {
        var hud = new HudMessages(new RecordingLog(), Fake(), Fake());

        for (int i = 0; i < HudMessages.TitleSlots; i++)
        {
            hud.ShowTitle($"T{i}", $"message {i}", "", 0.5f, 0.7f, Color32.White,
                          0f, 0f, 600f, i);
        }

        Assert.True(hud.ActiveTitleCount(10f) == HudMessages.TitleSlots, "the ring fills");

        // A message the map has just sent is the one the player is meant to
        // read, so the oldest goes rather than the new one being dropped.
        hud.ShowTitle("NEW", "the newest", "", 0.5f, 0.7f, Color32.White,
                      0f, 0f, 600f, 100f);

        Assert.True(hud.ActiveTitleCount(101f) == HudMessages.TitleSlots, "and stays full rather than growing");
    }

    private static string NoLocalisation(string token) => token;

    private static string FakeBinding(string command) => command switch
    {
        "+use" => "E",
        "+attack" => "MOUSE1",
        _ => "",
    };

    [Fact]
    public void HintsSubstituteBindings()
    {
        Assert.True(HudHints.Resolve("Press %+use% to open", NoLocalisation, FakeBinding)
                == "Press E to open", "a bound command becomes its key");

        Assert.True(HudHints.Resolve("%+attack% fires, %+use% opens", NoLocalisation, FakeBinding)
                == "MOUSE1 fires, E opens", "two tokens in one sentence both resolve");

        // Stock draws this rather than leaving a gap, and that choice belongs on
        // this side: the ABI answers with an empty string and says nothing about
        // what to show for it.
        Assert.True(HudHints.Resolve("Press %+zoom% to zoom", NoLocalisation, FakeBinding)
                == $"Press {HudHints.Unbound} to zoom", "an unbound command says so");

        Assert.True(HudHints.Resolve("just words", NoLocalisation, FakeBinding) == "just words", "a sentence with no tokens is left alone");

        Assert.True(HudHints.Resolve("", NoLocalisation, FakeBinding).Length == 0, "and an empty hint stays empty");
    }

    [Fact]
    public void HintsLocaliseFirst()
    {
        // The order matters and is not arbitrary: every stock hint is a token
        // whose TRANSLATION carries the %binding%. Substituting first would
        // leave the tokens in the translated text untouched.
        string Localise(string token)
            => token == "#Hint_open" ? "Press %+use% to open the door" : token;

        Assert.True(HudHints.Resolve("#Hint_open", Localise, FakeBinding)
                == "Press E to open the door", "a token is looked up and then its bindings resolved");

        Assert.True(HudHints.Resolve("#Hint_missing", Localise, FakeBinding) == "#Hint_missing", "a token with no entry is drawn as it arrived");
    }

    [Fact]
    public void HintsSurviveAwkwardPerCents()
    {
        // Stock's parser walks off the end of the string on an unpaired '%',
        // which "50% health" is. Here it is a literal.
        Assert.True(HudHints.Resolve("50% health", NoLocalisation, FakeBinding) == "50% health", "an unpaired per cent is a per cent");

        Assert.True(HudHints.Resolve("100%% done", NoLocalisation, FakeBinding) == "100% done", "a doubled per cent is one per cent");

        Assert.True(HudHints.Resolve("open: %+use%", NoLocalisation, FakeBinding) == "open: E", "a token at the very end resolves");
    }
}
