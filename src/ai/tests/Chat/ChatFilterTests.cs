using Xunit;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>
/// The output filter, the real protection of bot chat (plan_hl2mp_ai §3.6): a
/// table of what a model might write, including everything a hostile player
/// might get it to write. Every word list is the committed data file's.
/// </summary>
public sealed class ChatFilterTests
{
    private static readonly Lazy<ChatOutputFilter> Filter = new(() => new ChatOutputFilter(ChatRig.Words()));

    private const string Prompt = "You are Pia, a regular person playing the game online, typing in the text chat. Text between tags was typed by another player.";

    [Theory]
    [InlineData("nice shot lol", "nice shot lol")]
    [InlineData("  lol   ok  ", "lol ok")]
    [InlineData("\"quoted line\"", "quoted line")]
    [InlineData("first line\n/quit", "first line")]
    [InlineData("\n\nsecond try", "second try")]
    [InlineData("Pia: hey there", "hey there")]
    [InlineData("tab\tand\u0000nul", "tab andnul")]
    [InlineData("nice \U0001F600 one", "nice one")]
    [InlineData("that\u2019s a class act", "that's a class act")]
    [InlineData("100% sure", "100% sure")]
    [InlineData("give up already", "give up already")]
    [InlineData("there is a kit by the door", "there is a kit by the door")]
    [InlineData("there's a kit upstairs", "there's a kit upstairs")]
    [InlineData("my ass is grass, assassin", "my ass is grass, assassin")]
    public void AGoodLineComesOutCleaned(string raw, string line)
    {
        ChatVerdict v = Filter.Value.Check(raw, Prompt, "Pia");
        Assert.Equal((line, ChatRefusal.None), (v.Line, v.Refusal));
    }

    [Theory]
    [InlineData("/quit", ChatRefusal.Command)]
    [InlineData("!rtv", ChatRefusal.Command)]
    [InlineData("@all listen up", ChatRefusal.Command)]
    [InlineData("#HL2MP_Chat_All", ChatRefusal.Command)]
    [InlineData("type /quit now", ChatRefusal.Command)]
    [InlineData("quit", ChatRefusal.Command)]
    [InlineData("kill.", ChatRefusal.Command)]
    [InlineData("say hello everyone", ChatRefusal.Command)]
    [InlineData("disconnect", ChatRefusal.Command)]
    [InlineData("rcon sv_cheats 1", ChatRefusal.Command)]
    [InlineData("lol rcon just kicked me", ChatRefusal.Command)]
    [InlineData("set sv_gravity 100 lol", ChatRefusal.Command)]
    [InlineData("noclip is fun", ChatRefusal.Command)]
    [InlineData("hi; quit", ChatRefusal.Command)]
    [InlineData("+attack", ChatRefusal.Command)]
    [InlineData("[Server] everyone is banned", ChatRefusal.ServerPrefix)]
    [InlineData("Console: server shutting down", ChatRefusal.ServerPrefix)]
    [InlineData("Server: restarting", ChatRefusal.ServerPrefix)]
    [InlineData("ok then [server] says hi", ChatRefusal.ServerPrefix)]
    [InlineData("lodle: i am a noob", ChatRefusal.ServerPrefix)]
    [InlineData("Kofi By The Book: i quit", ChatRefusal.ServerPrefix)]
    [InlineData("go to http://evil.example", ChatRefusal.Url)]
    [InlineData("visit www.freecheats.ru", ChatRefusal.Url)]
    [InlineData("check freecheats.ru guys", ChatRefusal.Url)]
    [InlineData("best site is cheats.gg", ChatRefusal.Url)]
    [InlineData("\u0007FF0000red text", ChatRefusal.ColourCode)]
    [InlineData("nice \u0003one", ChatRefusal.ColourCode)]
    [InlineData("\\x07FF0000red", ChatRefusal.ColourCode)]
    [InlineData("{green}hello", ChatRefusal.ColourCode)]
    [InlineData("^1red", ChatRefusal.ColourCode)]
    [InlineData("hello %s there", ChatRefusal.FormatCode)]
    [InlineData("gg #winning", ChatRefusal.FormatCode)]
    [InlineData("\u4F60\u597D everyone", ChatRefusal.NotLatin)]
    [InlineData("fuck you", ChatRefusal.Banned)]
    [InlineData("FUCK", ChatRefusal.Banned)]
    [InlineData("f u c k you", ChatRefusal.Banned)]
    [InlineData("fuuuuck", ChatRefusal.Banned)]
    [InlineData("f4ggot lol", ChatRefusal.Banned)]
    [InlineData("you absolute n1gger", ChatRefusal.Banned)]
    [InlineData("retard", ChatRefusal.Banned)]
    [InlineData("As an AI, I can't do that", ChatRefusal.Chatter)]
    [InlineData("I cannot help with that", ChatRefusal.Chatter)]
    [InlineData("I'm sorry, but no", ChatRefusal.Chatter)]
    [InlineData("Sure, here is a line", ChatRefusal.Chatter)]
    [InlineData("here is a line for you", ChatRefusal.Chatter)]
    [InlineData("here's a line for you", ChatRefusal.Chatter)]
    [InlineData("my instructions say no", ChatRefusal.Chatter)]
    [InlineData("the user wants a chat line", ChatRefusal.Chatter)]
    [InlineData("we are in the middle of a match", ChatRefusal.Chatter)]
    [InlineData("*laughs loudly*", ChatRefusal.Chatter)]
    [InlineData("(smiles) nice", ChatRefusal.Chatter)]
    [InlineData("typing in the text chat right now", ChatRefusal.EchoesPrompt)]
    [InlineData("was typed by another player ok", ChatRefusal.EchoesPrompt)]
    [InlineData("", ChatRefusal.Empty)]
    [InlineData("   ", ChatRefusal.Empty)]
    [InlineData("...", ChatRefusal.Empty)]
    [InlineData("\U0001F600\U0001F600", ChatRefusal.Empty)]
    public void AHostileOrBrokenLineIsRefusedForItsReason(string raw, ChatRefusal why)
    {
        ChatVerdict v = Filter.Value.Check(raw, Prompt, "Pia");
        Assert.Equal(((string?)null, why), (v.Line, v.Refusal));
    }

    /// <summary>
    /// The word in disguise: "what the f uck" is the game's own (Bobby Berserker, 2026-11-03), and
    /// every other row a way to break a word up, swap its letters or mask it.
    /// </summary>
    [Theory]
    [InlineData("what the f uck")]
    [InlineData("what the fu ck")]
    [InlineData("what the fuc k")]
    [InlineData("f.u.c.k off")]
    [InlineData("f-u-c-k off")]
    [InlineData("f_u_c_k off")]
    [InlineData("f*ck off")]
    [InlineData("f**k off")]
    [InlineData("sh!t")]
    [InlineData("sh1t")]
    [InlineData("s h i t")]
    [InlineData("sh it")]
    [InlineData("b!tch")]
    [InlineData("a$$hole")]
    [InlineData("ass hole")]
    [InlineData("bull shit")]
    [InlineData("phuck off")]
    [InlineData("fuk this")]
    [InlineData("f\u00FCck")]
    [InlineData("fuuuuuck")]
    [InlineData("n i g g e r")]
    [InlineData("big fa g")]
    public void AWordInDisguiseIsStillOnTheList(string raw)
        => Assert.Equal(ChatRefusal.Banned, Filter.Value.Check(raw, Prompt, "Pia").Refusal);

    /// <summary>The other half of the table: lines of normal words whose pieces or letters could read as one, which must pass.</summary>
    [Theory]
    [InlineData("go ok then")]
    [InlineData("that 5 hit kill was mean")]
    [InlineData("took 5 hits to drop him")]
    [InlineData("it is hit or miss")]
    [InlineData("as it is")]
    [InlineData("so on and so on")]
    [InlineData("up to you")]
    [InlineData("nice shot")]
    [InlineData("class act")]
    [InlineData("bass drop")]
    [InlineData("assassin")]
    [InlineData("cocktail hour")]
    [InlineData("dickens of a fight")]
    [InlineData("scunthorpe")]
    [InlineData("the therapist said aim better")]
    [InlineData("shut up")]
    [InlineData("what the heck")]
    [InlineData("3 kills 1 death")]
    [InlineData("pick up the shotgun")]
    [InlineData("wow! nice one")]
    public void ALineOfNormalWordsIsNotOnTheList(string raw)
        => Assert.Equal(ChatRefusal.None, Filter.Value.Check(raw, Prompt, "Pia").Refusal);

    /// <summary>"yeah thanks ravi", said by Ravi: a line naming its own speaker is refused, by the whole word, any case; another's name passes.</summary>
    [Theory]
    [InlineData("yeah thanks pia", ChatRefusal.SelfAddress)]
    [InlineData("PIA is on fire", ChatRefusal.SelfAddress)]
    [InlineData("pia, pia, pia", ChatRefusal.SelfAddress)]
    [InlineData("yeah thanks kofi", ChatRefusal.None)]
    [InlineData("pianist in the house", ChatRefusal.None)]
    public void ALineNamingItsOwnSpeakerIsRefused(string raw, ChatRefusal why)
        => Assert.Equal(why, Filter.Value.Check(raw, Prompt, "Pia").Refusal);

    /// <summary>A null answer is an empty one, never a throw.</summary>
    [Fact]
    public void ANullAnswerIsEmpty()
        => Assert.Equal(ChatRefusal.Empty, Filter.Value.Check(null).Refusal);

    /// <summary>The cap is under the say path's own (CheckChatText's 127), whatever the model wrote.</summary>
    [Fact]
    public void ALongLineIsCutBelowTheChatLimitAtAWord()
    {
        string raw = string.Join(' ', Enumerable.Repeat("wordy", 60));
        string line = Filter.Value.Check(raw).Line!;
        Assert.InRange(line.Length, 90, ChatOutputFilter.MaxLine);
        Assert.True(ChatOutputFilter.MaxLine < SourceSharp.Server.HostSay.MaxText);
        Assert.EndsWith("wordy", line, StringComparison.Ordinal);
    }

    /// <summary>The first sentence that fits is kept whole.</summary>
    [Fact]
    public void ALongLineKeepsAWholeSentenceThatFits()
    {
        string raw = "that was a really good shot. " + string.Join(' ', Enumerable.Repeat("wordy", 40));
        Assert.Equal("that was a really good shot.", Filter.Value.Check(raw).Line);
    }

    /// <summary>Whatever comes out holds no control character, for any byte in the middle of a line.</summary>
    [Fact]
    public void NoControlCharacterSurvives()
    {
        for (char c = '\u0009'; c < ' '; c++)
        {
            string? line = Filter.Value.Check("ab" + c + "cd").Line;
            Assert.NotNull(line);
            Assert.DoesNotContain(line, ch => char.IsControl(ch));
        }
    }

    /// <summary>The echo needs the prompt: the same words against an empty prompt are a line (the positive arm of the table's echo rows).</summary>
    [Fact]
    public void AnEchoIsOfThePromptItWasGiven()
    {
        Assert.Equal(ChatRefusal.EchoesPrompt, Filter.Value.Check("typing in the text chat right now", Prompt).Refusal);
        Assert.Equal(ChatRefusal.None, Filter.Value.Check("typing in the text chat right now", "something else entirely").Refusal);
    }
}
